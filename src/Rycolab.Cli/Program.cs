using System.Security.Principal;
using Rycolab.Cli;
using Rycolab.Cli.Commands;
using Rycolab.Core;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var argv = Environment.GetCommandLineArgs().Skip(1).ToArray();
var command = argv.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))?.ToLowerInvariant();
var opts = new Args(argv.Where(a => !string.Equals(a, command, StringComparison.OrdinalIgnoreCase)));

if (command is null or "help" or "-h" or "--help")
{
    PrintHelp();
    return 0;
}

if (!IsElevated())
{
    Console.Error.WriteLine("rycolab needs administrator privileges to talk to the SMU.");
    Console.Error.WriteLine("Open an elevated console and try again.");
    return 3;
}

try
{
    return command switch
    {
        "probe" => ProbeCommand.Run(opts),
        "sensors" => SensorsCommand.Run(opts),
        "watch" => WatchCommand.Run(opts),
        "apply" => ApplyCommand.Run(opts),
        "reset" => ResetCommand.Run(opts),
        "plan" => PlanCommand.Run(opts),
        "guard" => GuardCommand.Run(opts),
        "sweep" => SweepCommand.Run(opts),
        "report" => ReportCommand.Run(opts),
        "task" => TaskCommand.Run(opts),
        "status" => StatusCommand.Run(opts),
        _ => Unknown(command),
    };
}
catch (SafetyViolationException ex)
{
    Console.Error.WriteLine($"BLOCKED BY SAFETY: {ex.Message}");
    return 4;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}

static int Unknown(string c)
{
    Console.Error.WriteLine($"Unknown command: {c}");
    PrintHelp();
    return 2;
}

static bool IsElevated()
{
    try
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }
    catch { return false; }
}

static void PrintHelp()
{
    Console.WriteLine("""
        rycolab - Curve Optimizer test bench for Ryzen

        USAGE
          rycolab <command> [options]

        COMMANDS
          probe      Read the PSM margin applied on every core
          apply      Write margins (walks in steps, verifies every stop)
          reset      Return all cores to the baseline
          sensors    Dump the available sensors with their exact names
          watch      Sample clock, effective clock, V, GHz, W and T of one core
          plan       Per-core profile and sweep parameters (plan.json)
          guard      Apply the plan, re-apply after resume, watch margin and WHEA
          sweep      Per-core sweep with y-cruncher: find each core's limit
          report     Limits, positives, telemetry and events of a campaign (rycolab.db)
          task       Scheduled task that runs guard (hidden) at logon
          status     Is guard alive?, last sample, events, hardware vs plan
          help       This help

        probe
          --compare <path>   Compare with a JSON profile with a CoreValues field.
                             By default compares with plan.json.
          --no-compare       Do not compare with any profile.
          --json <path>      Save the reading with a timestamp.
          --sensors          Add effective clock and power per core.

        watch
          --core <n>         Core to watch.
          --seconds <n>      Duration. Default 180.
          --interval <ms>    Interval between samples. Default 1000.
          --jsonl <path>     One JSON line per sample.
          --summary <path>   Medians at the end, as JSON.
          --raw              Add the SMU power table (raw floats).

        apply
          --margin <n>       Target margin. Only values <= 0.
          --core <n>         A single core.        Without --core or --ccd: all cores.
          --ccd <0|1>        A whole CCD (0 = cores 0-7).
          --profile <path>   Per core, from a JSON with CoreValues.
          --plan [path]      Per core, from plan.json.
          --dry-run          Show the plan and write nothing.

        reset
          --to <n>           Baseline to return to. Default -5.
          --dry-run          Show the plan and write nothing.

        plan
          init [--from-hardware] [--force]    Create plan.json (profile -5, or what is applied now)
          show                                 Show the plan
          set-core <n> <m>                     Change one core
          set-profile a,b,...,p                All cores at once
          from-sweep <campaign> [--margin 5]   Profile = limit + margin from runs/<campaign>/limits.json
          --plan <path>                        Another file. Default plan.json at the repo root.

        guard
          --plan <path>      Plan to guard. Default plan.json.
          --minutes <n>      Bounded soak; without it, unbounded (Ctrl+C to exit).
          --interval <s>     Seconds between samples. Default 60.
          --plain            No panel; one line per sample.
          On exit (time, Ctrl+C, WHEA or margin lost) leaves the baseline.
          Codes: 0 clean, 10 positive (WHEA or margin lost), 1 could not apply.

        sweep
          --campaign <n>     Folder runs/<n>. Default sweep-<date>. Resumable: skips cores
                             with a limit and treats an in-progress.json as a machine hang.
          --cores <spec>     0-15, 0,3,8-11 ...   Default all cores.
          --start/--top/--step/--seconds   Override the plan.
          --no-suspend       Without the periodic 1 s every 10 s suspension.
          --plain            No panel.
          Requires the hardware at the baseline (stop guard first). Restores the baseline after every run.

        report
          --campaign <n|dir>   runs/<n> (sweep) or runs/guard.
          --md [path]          Write markdown (default report.md in the campaign).
          --rebuild            Regenerate rycolab.db from the JSONL files.

        task
          install [--plan <path>]   Task at logon (elevated, hidden)
          run                       Start guard now through the task, independent of the console
          stop                      Ask guard to exit cleanly (restores the baseline)
          remove | status

        status
          --follow           Live panel reading the hidden guard's log. Ctrl+C only closes the panel.

        SAFETY
          Allowed margin: -50 to 0. A positive value RAISES the voltage and is
          always rejected. Every write is read back and aborted on mismatch.
          Large moves are walked in stops of at most 3 counts, verifying at
          every stop. If the process dies halfway, the cores go back to how they
          were; and a reboot returns them to the BIOS values.

        EXIT CODES
          0  ok             2  profile and hardware DO NOT match
          1  error          3  missing privileges       4  blocked by safety

        Requires an elevated console.
        """);
}
