# rycolab

Curve Optimizer test bench for AMD Ryzen. Finds the most aggressive **stable**
per-core undervolt your chip can take, measured and verified against the
hardware, then keeps it applied through sleep and reboot.

Born on a Lenovo Legion Pro 7 16AFR10H (Ryzen 9 9955HX3D, 16 cores, two CCDs,
one with stacked cache). Nothing in it is Legion-specific: it talks to the SMU
mailbox through ZenStates.Core, which covers Ryzen with per-core Curve
Optimizer support (Zen 3/4/5). What depends on the exact chip (per-core
telemetry indices in the SMU power table) is calibrated, not assumed.

## Why it exists

Vendor tools let you **write** per-core Curve Optimizer margins but not
**check** what actually got applied. Legion Toolkit even reads the real margin
from the SMU in `LoadFromHardwareAsync` and overwrites the UI with the JSON on
disk three lines later. Without that read-back, tuning an undervolt is
guessing. This repository recovers it and builds a test bench around it.

First thing the probe showed on the reference machine:

```
With Legion Toolkit open        CCD0 -3   CCD1 -7     (the profile)
After a reboot, without it      -5 on all sixteen     (the BIOS)
```

Two different configurations depending on whether an app was open, and no tool
said so. Writers do not add up either: they replace each other, last one wins.

## Principles

1. **Detect compute errors, not hangs.** A hang is a late, terminal symptom.
   The real risk of an aggressive Curve Optimizer is silent wrong results.
2. **Never measure without verifying first.** Every configuration is read back
   from the SMU before a single data point is taken. On mismatch, abort.
3. **Safety limits have no bypass flag.**

## Status

Work in progress towards a publishable tool (see `docs/lab-notebook.md`). The
core is proven on the reference machine: per-core sweep with y-cruncher, four
positive signals (compute error, process crash, WHEA, machine hang), a guard
that re-applies the profile after resume, JSONL + SQLite records, reports. The
user-facing layer (`install`, `find`, `on`/`off`, unelevated `status`) is being
built on top of it.

## Usage (current commands)

Requires an **elevated** console and the .NET 9 runtime.

```
rycolab probe                  PSM margin applied on every core (compares with plan.json)
rycolab probe --sensors        adds effective clock and power per core
rycolab sensors                dumps the sensors with their exact names
rycolab watch --core N         1 Hz: clock, effective, V, GHz, W, T of one core
      [--seconds 180] [--interval 1000] [--jsonl f] [--summary f] [--raw]
rycolab plan init|show|set-core N M|set-profile a,...,p   plan.json (profile + sweep)
rycolab apply --plan           applies the plan.json profile to all cores
rycolab guard [--minutes N]    applies the plan, re-applies after resume, reads the
      [--interval 60] [--plain]   margin and counts WHEA every interval; leaves the baseline on exit
rycolab task install|run|stop|remove|status   scheduled task: HIDDEN guard at logon; run/stop by hand
rycolab status [--follow]      is guard alive?, last sample, events, hardware vs plan; --follow = live panel
rycolab sweep [--campaign n] [--cores 0-15] [--start -50] [--top -5] [--step 5] [--seconds 360]
      [--no-suspend] [--plain]   sweep: per core, bottom up, every y-cruncher engine in the plan;
                                 limit = first margin clean on all; resumable; restores the baseline
rycolab plan from-sweep <campaign> [--margin 5]   profile = limit + margin
rycolab report --campaign <n> [--md] [--rebuild]  limits, positives, telemetry, events (rycolab.db)
```

Campaign from scratch: `plan init` -> `sweep` -> `plan from-sweep` -> `guard
--minutes 30` (idle soak) -> `task install` and real use with sleep -> `report
--md`. Each campaign lives in `runs/<name>/`: `runs.jsonl` and `samples.jsonl`
(primary source, write-through), `rycolab.db` (SQLite, filled on the fly;
`report --rebuild` regenerates it), `limits.json`, `in-progress.json` (if it
is there at startup, the machine hung during that run: positive) and
`positives/`.

Sweep signals: y-cruncher compute error, dead process, WHEA (17-20, 46, 47) or
Kernel-Power 41 during the run, and machine hang.

`plan.json` (git-ignored; `plan.example.json` as a sample) holds the per-core
profile, the baseline and the sweep parameters. **Sleep and reboot restore the
BIOS baseline**: without `guard` the profile does not last. `guard` writes
`runs/guard/guard.jsonl` (samples and events) and, on a WHEA event,
`runs/guard/positives/whea-*.json`, exiting with code 10 and leaving the
baseline. Stop guard before rebuilding (it holds the executable).

y-cruncher binaries go in `tools/y-cruncher/Binaries/` (git-ignored): copy
them from the official y-cruncher distribution or from CoreCycler's
`test_programs/y-cruncher/Binaries`.

## Field notes

Measured on the reference machine, not assumed:

- LibreHardwareMonitor's `Core #N VID` **is not a per-core voltage** on the
  9955HX3D: all 16 report the same value and move together. Discarded.
  Per-core voltage comes from the SMU power table (`PmTable.cs`).
- What does discriminate per core in LHM is `Core #N (SMU)` (power) and the
  effective clock.
- CCDs are numbered **from 0**, like Legion Toolkit and the SMU mask. HWiNFO
  and LibreHardwareMonitor number from 1: our CCD0 is their `CCD1 (Tdie)`. The
  translation lives in `Topology.CcdTempSensor` and nowhere else.
- Each physical core owns two logical processors: core N is logical 2N.
- On this laptop every sustained torture is capped at ~14 W per core; only
  y-cruncher `04-P4P` (SSE3) reaches fMax (5.45 GHz). `24-ZN5` (AVX-512) is
  the engine that finds the errors. See `docs/how-it-works.md`.

## Build

Needs the **.NET 9 SDK** (x64).

```
dotnet build -c Release src/Rycolab.Cli
```

`inpoutx64.dll`, the port-access layer ZenStates.Core needs, is copied at
build time from a local path (by default the Legion Toolkit install). If it is
elsewhere:

```
dotnet build -c Release -p:InpOutSource=PATH\inpoutx64.dll src/Rycolab.Cli
```

## License

GPL-3.0. The core mask encoding and the SMU access sequence derive from Lenovo
Legion Toolkit, also GPL-3.0. See `NOTICE`.

## Warning

This writes to your processor's SMU mailbox. A bad undervolt produces wrong
results before it produces visible failures. Use it knowing that.
