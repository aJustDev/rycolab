using System.Security.Principal;
using Rycolab.Cli;
using Rycolab.Cli.Commands;
using Rycolab.Core;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var argv = Environment.GetCommandLineArgs().Skip(1).ToArray();
var command = argv.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))?.ToLowerInvariant();
var opts = new Args(argv.Where(a => !string.Equals(a, command, StringComparison.OrdinalIgnoreCase)));

if (command is "help" or "-h" or "--help")
{
    PrintHelp();
    return 0;
}

// Commands that only read files never need elevation.
var unelevated = command is null or "status" or "report" or "profile" or "plan" or "version";
if (!unelevated && !IsElevated())
{
    Console.Error.WriteLine($"'rycolab {command}' needs administrator privileges to talk to the SMU.");
    Console.Error.WriteLine("Open an elevated console (or use sudo) and try again.");
    return 3;
}

try
{
    return command switch
    {
        null => RootCommand.Run(opts),
        "version" => Version(),
        "install" => InstallCommand.Run(opts),
        "uninstall" => UninstallCommand.Run(opts),
        "find" => FindCommand.Run(opts),
        "on" => OnCommand.Run(opts),
        "off" => OffCommand.Run(opts),
        "status" => StatusCommand.Run(opts),
        "report" => ReportCommand.Run(opts),
        "profile" => ProfileCommand.Run(opts),
        "sweep" => SweepCommand.Run(opts),
        "guard" => GuardCommand.Run(opts),
        "probe" => ProbeCommand.Run(opts),
        "apply" => ApplyCommand.Run(opts),
        "reset" => ResetCommand.Run(opts),
        "sensors" => SensorsCommand.Run(opts),
        "watch" => WatchCommand.Run(opts),
        "plan" => PlanCommand.Run(opts),
        "task" => TaskCommand.Run(opts),
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

static int Version()
{
    Console.WriteLine($"rycolab {typeof(Guard).Assembly.GetName().Version?.ToString(3) ?? "?"}  data {AppPaths.Data}");
    return 0;
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
        rycolab - per-core Curve Optimizer for Ryzen: find the limit, keep the profile

        USAGE
          rycolab                       what is applied right now and what to do next
          rycolab install               copy to %LOCALAPPDATA%\rycolab, PATH, y-cruncher, baseline, task
          rycolab on | off              keep the profile applied (hidden guard) | back to the baseline
          rycolab status [--follow]     guard, last sample, WHEA, hardware vs profile (no elevation)
          rycolab report [<campaign>]   limits, positives, telemetry, events; --md writes markdown
          rycolab find [--quick]        find each core's limit and propose a profile (hours)
          rycolab profile show|from-sweep <campaign>|import ...   the profile and where it came from
          rycolab uninstall [--purge]   remove task, PATH and binaries; --purge also the data

        FINDING A PROFILE
          rycolab find [--quick] [--cores 0-15] [--resume] [--yes] [--accept] [--plain]
            Checks (AC, y-cruncher, baseline), estimates the time, asks, stops the guard,
            runs the sweep and proposes profile = limit + safety margin. --quick: three
            tests and 180 s per run instead of eight and 360 s. Resumes an unfinished
            campaign (also after a reboot). --yes / --accept for scripts.
          rycolab sweep [--campaign n] [--cores 0-15] [--start -50] [--top -5] [--step 5]
                        [--seconds 360] [--no-suspend] [--plain]
            The sweep itself, without the wizard. Requires the baseline (run `off`).
          rycolab profile from-sweep <campaign> [--margin 5]
            profile = limit + margin, with the campaign as its source.

        LOW LEVEL (elevated; the sweep and the guard are built on these)
          probe [--sensors] [--json f] [--compare f] [--no-compare]
          apply --margin M [--core N | --ccd 0|1] | --profile [path] [--force]   [--dry-run]
          reset [--to N]
          guard [--profile path] [--minutes N] [--interval s] [--plain]
          watch --core N [--seconds] [--interval ms] [--jsonl f] [--summary f] [--raw]
          sensors
          plan show | init [--force] | set <key> <value>      config.json (baseline, engines, tests...)
          task install|run|stop|remove|status                 the scheduled task by hand
          profile import --cores a,...,p --campaign <name> [--limits a,...,p] [--note ...]
          profile export <path>

        SAFETY
          Allowed margin -50..0; positive values are rejected. Every write is read back.
          Moves are walked in steps of 3. `on` refuses a profile without a source, from
          another CPU, or more aggressive than its measured limits. A reboot or a sleep
          returns the cores to the BIOS baseline; the guard re-applies the profile.

        EXIT CODES
          0 ok   1 error   2 mismatch / refused   3 needs elevation   4 blocked by safety
          10 positive (WHEA or margin lost)
        """);
}
