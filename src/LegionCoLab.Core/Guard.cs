namespace LegionCoLab.Core;

public sealed record GuardTick(
    DateTime Ts, int Elapsed, bool Ok, int?[] Hardware, int Whea, double? CpuLoad, double? PackagePower, string State);

public sealed class GuardOptions
{
    public int? Minutes { get; init; }
    public int IntervalSeconds { get; init; } = 60;
    public int MaxReappliesPerHour { get; init; } = 3;
    public int ResumeSettleSeconds { get; init; } = 10;
    public string RunsDir { get; init; } = Path.Combine(Plan.RepoRoot, "runs", "guard");
}

/// <summary>
/// Vigilante del perfil: lo aplica, lo reaplica al reanudar de suspension
/// (la BIOS devuelve la base al despertar, visto el 28/08/2026), relee el
/// hardware y cuenta WHEA cada intervalo, y deja la base puesta al salir.
///
/// Codigos: 0 termino limpio (tiempo agotado o Ctrl+C), 10 positivo (WHEA o
/// margen perdido mas veces de las admitidas), 1 no pudo aplicar.
/// </summary>
public sealed class Guard
{
    private readonly CoController _co;
    private readonly Plan _plan;
    private readonly GuardOptions _o;
    private readonly Journal _journal;
    private readonly Telemetry? _telemetry;
    private readonly Action<GuardTick> _onTick;
    private readonly Action<string> _onEvent;

    private readonly List<DateTime> _reapplies = [];
    private DateTime? _resumeAt;

    public Guard(CoController co, Plan plan, GuardOptions options, Telemetry? telemetry, Action<GuardTick> onTick, Action<string> onEvent)
    {
        _co = co;
        _plan = plan;
        _o = options;
        _telemetry = telemetry;
        _onTick = onTick;
        _onEvent = onEvent;
        Directory.CreateDirectory(_o.RunsDir);
        _journal = new Journal(Path.Combine(_o.RunsDir, "guard.jsonl"));
        _store = new Store(Path.Combine(_o.RunsDir, "colab.db"));
    }

    private readonly Store _store;

    public int Run(CancellationToken ct)
    {
        var t0 = DateTime.Now;
        var code = 0;
        Event("start", $"perfil {string.Join(",", _plan.Profile)}  intervalo {_o.IntervalSeconds}s  {(_o.Minutes is { } m ? m + " min" : "sin limite")}");

        try
        {
            if (!ApplyPlan("inicio")) return 1;

            using var power = new PowerWatch();
            power.Suspending += () => Event("suspend", "el sistema entra en suspension");
            power.Resumed += () => { _resumeAt = DateTime.Now; Event("resume", "el sistema se ha reanudado; se reaplica en unos segundos"); };

            var wheaSeen = 0;
            while (!ct.IsCancellationRequested)
            {
                if (!Wait(ct)) break;

                if (_resumeAt is { } r && (DateTime.Now - r).TotalSeconds >= _o.ResumeSettleSeconds)
                {
                    _resumeAt = null;
                    if (!ApplyPlan("reanudacion")) { code = 1; break; }
                }

                var readings = _co.ReadAll();
                var hw = readings.Select(x => x.Margin).ToArray();
                var bad = _plan.Mismatches(readings);
                var hardware = Whea.HardwareSince(t0);
                var el = (int)(DateTime.Now - t0).TotalSeconds;
                var cpu = _telemetry?.CpuLoad();
                var pkg = _telemetry?.Read().PackagePower;

                if (hardware.Count > wheaSeen)
                {
                    foreach (var e in hardware.Skip(wheaSeen))
                        Event("whea", $"{e.Time:HH:mm:ss} {e.Provider.Replace("Microsoft-Windows-", "")} id {e.Id}: {e.Message}");
                    wheaSeen = hardware.Count;
                    Journal.WriteJsonFile(Path.Combine(_o.RunsDir, "positivos", $"whea-{DateTime.Now:yyyyMMdd-HHmmss}.json"),
                        new { plan = _plan, hardware = hw, events = hardware });
                    Tick(t0, el, bad.Count == 0, hw, hardware.Count, cpu, pkg, "WHEA");
                    code = 10;
                    break;
                }

                if (bad.Count > 0 && _resumeAt is null)
                {
                    Event("changed", $"hardware {string.Join(",", hw)}  nucleos fuera del plan: {string.Join(",", bad)}");
                    _reapplies.RemoveAll(t => (DateTime.Now - t).TotalHours >= 1);
                    if (_reapplies.Count >= _o.MaxReappliesPerHour)
                    {
                        Event("giveup", $"{_reapplies.Count} reaplicaciones en una hora; se deja la base");
                        Tick(t0, el, false, hw, hardware.Count, cpu, pkg, "perdido");
                        code = 10;
                        break;
                    }
                    _reapplies.Add(DateTime.Now);
                    if (!ApplyPlan("perdido")) { code = 1; break; }
                    Tick(t0, el, true, _co.ReadAll().Select(x => x.Margin).ToArray(), hardware.Count, cpu, pkg, "reaplicado");
                    continue;
                }

                Tick(t0, el, bad.Count == 0, hw, hardware.Count, cpu, pkg, bad.Count == 0 ? "ok" : "esperando reanudacion");

                if (_o.Minutes is { } min && el >= min * 60) { Event("done", $"{min} min cumplidos"); break; }
            }
            if (ct.IsCancellationRequested) Event("cancel", "interrumpido");
        }
        catch (Exception ex)
        {
            Event("error", ex.Message);
            code = 1;
        }
        finally
        {
            var restored = _co.TryRestore(_plan.Base);
            var after = _co.ReadAll().Select(x => x.Margin).ToArray();
            Event("restore", $"base {_plan.Base}: {restored} nucleos escritos; hardware {string.Join(",", after)}  codigo {code}");
            _journal.Dispose();
            _store.Dispose();
        }
        return code;
    }

    private bool ApplyPlan(string why)
    {
        try
        {
            var after = Stepper.Apply(_co, _plan.Targets(_co.CoreCount));
            Event("apply", $"{why}: perfil aplicado y verificado: {string.Join(",", after.Select(x => x.Margin))}");
            return true;
        }
        catch (Exception ex)
        {
            Event("apply-failed", $"{why}: {ex.Message}");
            return false;
        }
    }

    private bool Wait(CancellationToken ct)
    {
        var deadline = DateTime.Now.AddSeconds(_o.IntervalSeconds);
        while (DateTime.Now < deadline)
        {
            if (ct.IsCancellationRequested) return false;
            if (_resumeAt is not null) return true;   // no esperar el intervalo entero tras despertar
            Thread.Sleep(250);
        }
        return true;
    }

    private void Tick(DateTime t0, int el, bool ok, int?[] hw, int whea, double? cpu, double? pkg, string state)
    {
        var t = new GuardTick(DateTime.Now, el, ok, hw, whea, cpu, pkg, state);
        _journal.Write(new { kind = "tick", t.Ts, t.Elapsed, t.Ok, t.Hardware, t.Whea, t.CpuLoad, t.PackagePower, t.State });
        _store.AddTick(t);
        _onTick(t);
    }

    private void Event(string kind, string detail)
    {
        _journal.Write(new { kind, ts = DateTime.Now, detail });
        _store.AddEvent(DateTime.Now, kind, detail);
        _onEvent($"{DateTime.Now:HH:mm:ss}  {kind}: {detail}");
    }
}
