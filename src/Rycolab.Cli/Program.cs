using Rycolab.Cli;
using Rycolab.Cli.Commands;
using Rycolab.Core;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var argv = Environment.GetCommandLineArgs().Skip(1).ToList();
var command = argv.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))?.ToLowerInvariant();
if (command is not null) argv.Remove(argv.First(a => string.Equals(a, command, StringComparison.OrdinalIgnoreCase)));

// `dev <sub>`: the low-level commands the user-facing ones are built on.
var dev = false;
if (command == "dev")
{
    dev = true;
    command = argv.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))?.ToLowerInvariant();
    if (command is null or "help" or "-h" or "--help") { PrintDevHelp(); return 0; }
    argv.Remove(argv.First(a => string.Equals(a, command, StringComparison.OrdinalIgnoreCase)));
}
var opts = new Args(argv);

if (command is "help" or "-h" or "--help")
{
    PrintHelp();
    return 0;
}

// Commands that only read files never need elevation.
var unelevated = command is null or "status" or "report" or "profile" or "version" || (dev && command is "plan" or "toast");
if (!unelevated && !Elevation.IsElevated())
{
    Console.Error.WriteLine($"'rycolab {(dev ? "dev " : "")}{command}' needs administrator privileges to talk to the SMU.");
    Console.Error.WriteLine("Open an elevated console (or use sudo) and try again.");
    return 3;
}

try
{
    if (dev)
        return command switch
        {
            "probe" => ProbeCommand.Run(opts),
            "apply" => ApplyCommand.Run(opts),
            "reset" => ResetCommand.Run(opts),
            "guard" => GuardCommand.Run(opts),
            "sweep" => SweepCommand.Run(opts),
            "watch" => WatchCommand.Run(opts),
            "sensors" => SensorsCommand.Run(opts),
            "calibrate" => CalibrateCommand.Run(opts),
            "plan" => PlanCommand.Run(opts),
            "toast" => ToastCommand.Run(opts),
            "task" => TaskCommand.Run(opts),
            "profile" => ProfileCommand.Run(opts),
            "log" => LogCommand.Run(opts),
            _ => UnknownDev(command!),
        };

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
        // The task itself runs `guard --plain`; keep it reachable at the top level.
        "guard" => GuardCommand.Run(opts),
        "fan" => FanCommand.Run(opts),
        "power" => PowerCommand.Run(opts),
        "charge" => ChargeCommand.Run(opts),
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
    Console.Error.WriteLine($"Unknown command: {c}. `rycolab help` lists them; low-level commands live under `rycolab dev`.");
    return 2;
}

static int UnknownDev(string c)
{
    Console.Error.WriteLine($"Unknown dev command: {c}");
    PrintDevHelp();
    return 2;
}

static void PrintHelp()
{
    Console.WriteLine("""
        rycolab - per-core Curve Optimizer for Ryzen: find the limit, keep the profile

          rycolab                       what is applied right now and what to do next
          rycolab install               copy to %LOCALAPPDATA%\rycolab, PATH, y-cruncher, baseline, task
          rycolab find [--quick]        find each core's limit and propose a profile (hours; hands off)
          rycolab on | off              keep the profile applied (hidden guard) | back to the baseline
          rycolab status [--once]       live panel (2 s refresh, Ctrl+C closes): Curve Optimizer, battery profile,
                                        Lenovo EC (with sudo), Windows scheme; --once prints it and exits
          rycolab report [<campaign>]   limits, positives, telemetry, events; --md writes markdown
          rycolab report --bench <csv> [--vs <csv>] [--battery]   summary of a `dev log` CSV (samples > 100 W, or on battery)
          rycolab profile show|from-sweep <campaign> [--margin 5]|export <path>
          rycolab uninstall [--purge]   remove task, PATH and binaries; --purge also the data
          rycolab fan show|on|off|auto  Lenovo Legion: the EC "fan full speed" switch, by hand or by CPU temperature
                                        (auto: --on 85 --off 80 --hold 3; selects custom mode itself, restores it on exit)
          rycolab power show|battery|ac|restore|auto on|off   Lenovo Legion battery profile: quiet mode, iGPU only, 60 Hz,
                                        brightness 40 %, DC scheme values; `ac` restores; `auto` lets the guard do it on AC line changes
                                        (battery: --gpu igpu|auto|keep --hz 60 --brightness 40 --no-windows --close-apps)
          rycolab charge show|normal|conservation|rapid|night on|off   Lenovo battery charge mode (conservation stops at ~80 %)
          rycolab dev <command>         low-level: probe, apply, reset, guard, sweep, watch, sensors,
                                        calibrate, plan, toast, task, profile import, log   (`rycolab dev help`)

        find
          --quick        three tests and 180 s per run instead of eight and 360 s
          --cores <spec> 0-15, 0,3,8-11 ...      --resume   continue the unfinished campaign
          --engines <l>  zn5,p4p,zn2 or binary names; overrides the config for this run
          --yes          no questions            --accept   save the proposed profile

        SAFETY
          Allowed margin -50..0; positive values are rejected. Every write is read back.
          Moves are walked in steps of 3. `on` refuses a profile without a source, from
          another CPU, or more aggressive than its measured limits. A reboot or a sleep
          returns the cores to the BIOS baseline; the guard re-applies the profile and,
          on any WHEA event, restores the baseline and stops.

        EXIT CODES
          0 ok   1 error   2 mismatch / refused   3 needs elevation   4 blocked by safety
          10 positive (WHEA or margin lost)
        """);
}

static void PrintDevHelp()
{
    Console.WriteLine("""
        rycolab dev <command>   (elevated unless noted)

          probe [--sensors] [--write-test] [--json f] [--compare f]     margins read from the SMU; --write-test writes core 0's own value back
          apply --margin M [--core N | --ccd 0|1] | --profile [path] [--force]   [--dry-run]
          reset [--to N]                                                  all cores to the baseline
          guard [--profile path] [--minutes N] [--interval s] [--plain]   the guard, in this console
          sweep [--campaign n] [--cores 0-15] [--engines zn5,p4p] [--start M] [--top M]
                [--step N] [--seconds S] [--no-suspend] [--plain]         the sweep without the wizard
          watch --core N [--seconds] [--interval ms] [--jsonl f] [--summary f] [--raw]
          sensors                                                         LibreHardwareMonitor dump
          calibrate [--core N] [--seconds 40]     locate the per-core PM table blocks (pm-index.json)
          plan show | init [--force] | set <key> <value>                  config.json (no elevation)
          toast [--title t] [--body b]      test notification, same path as the guard (no elevation)
          task install|run|stop|remove|status                             the scheduled task by hand
          profile import --cores a,...,p --campaign <name> [--limits a,...,p] [--note ...]
          log --out <file.csv> [--interval 2] [--minutes N]   package W, temps, effective clocks, core V, Lenovo fans, battery W
        """);
}
