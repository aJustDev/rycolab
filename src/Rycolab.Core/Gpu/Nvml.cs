using System.Runtime.InteropServices;

namespace Rycolab.Core.Gpu;

/// <summary>
/// NVML (nvml.dll, shipped with the driver) for telemetry only: clocks,
/// power, temperature, utilisation. Opening it wakes a sleeping dGPU, so the
/// guard opens it only while the card is on the bus and closes it when the
/// card leaves. Writes (clock lock, offsets) stay out: they share state with
/// the NvAPI curve writes and clobber them (Green Curve, LACT #936).
/// </summary>
public sealed class Nvml : IDisposable
{
    [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")] private static extern int Init();
    [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")] private static extern int Shutdown();
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")] private static extern int GetHandle(uint index, out IntPtr device);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetClockInfo")] private static extern int GetClock(IntPtr device, uint type, out uint mhz);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage")] private static extern int GetPower(IntPtr device, out uint mw);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")] private static extern int GetTemperature(IntPtr device, uint sensor, out uint c);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")] private static extern int GetUtilization(IntPtr device, out Utilization u);
    [StructLayout(LayoutKind.Sequential)] private struct Utilization { public uint Gpu, Memory; }

    private const uint ClockGraphics = 0, ClockMemory = 2;

    private readonly IntPtr _device;
    private readonly bool _open;

    public bool IsAvailable => _open;
    public string? Unavailable { get; }

    public Nvml()
    {
        try
        {
            var rc = Init();
            if (rc != 0) { Unavailable = $"nvmlInit {rc}"; return; }
            rc = GetHandle(0, out _device);
            if (rc != 0) { Unavailable = $"nvmlDeviceGetHandleByIndex {rc}"; Shutdown(); return; }
            _open = true;
        }
        catch (Exception ex) { Unavailable = ex.Message; }
    }

    public readonly record struct Sample(int? Mhz, int? MemMhz, double? Watts, int? TempC, int? Util);

    /// <summary>Null when the handle went stale: after a driver reset NVML answers success with garbage (2332033 MHz, 2336538 W on 2026-09-03); the caller reopens.</summary>
    public Sample? Read()
    {
        if (!_open) return null;
        var s = new Sample(
            GetClock(_device, ClockGraphics, out var g) == 0 ? (int)g : null,
            GetClock(_device, ClockMemory, out var m) == 0 ? (int)m : null,
            GetPower(_device, out var mw) == 0 ? mw / 1000.0 : null,
            GetTemperature(_device, 0, out var c) == 0 ? (int)c : null,
            GetUtilization(_device, out var u) == 0 ? (int)u.Gpu : null);
        var plausible = s.Mhz is null or (>= 0 and < 10000) && s.MemMhz is null or (>= 0 and < 40000)
            && s.Watts is null or (>= 0 and < 1000) && s.TempC is null or (>= 0 and < 150) && s.Util is null or (>= 0 and <= 100);
        return plausible ? s : null;
    }

    public void Dispose()
    {
        if (_open) { try { Shutdown(); } catch { /* driver gone */ } }
    }
}
