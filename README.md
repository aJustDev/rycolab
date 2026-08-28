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

## Usage

Needs Windows, the .NET 9 runtime and an AMD Ryzen with per-core Curve
Optimizer through the SMU. Commands that touch the hardware need an elevated
console (`sudo rycolab ...` on Windows 11 with sudo enabled); `rycolab`,
`status`, `report` and `profile show` do not.

```
rycolab install               copy to %LOCALAPPDATA%\rycolab, user PATH, y-cruncher (official zip,
                              SHA-256 checked), baseline read from the hardware, scheduled task
rycolab                       one screen: installed?, profile, guard, last sample, and what to do next
rycolab sweep                 find each core's limit (hours; leave the machine alone; it may reboot)
rycolab profile from-sweep <campaign> [--margin 5]     profile = limit + margin, with its source
rycolab on                    apply the profile and keep it: hidden guard, re-applied after sleep and at logon
rycolab status [--follow]     guard, phase (validating / steady), last sample, WHEA, events
rycolab off                   stop the guard, back to the BIOS baseline, task disabled
rycolab report [<campaign>]   limits, positives with time to error, telemetry, events; --md
rycolab uninstall [--purge]   task, PATH and binaries; --purge also the data
```

`on` refuses a profile without a source, from another CPU, or with any core
below its measured limit. The guard writes `state.json` on every sample (what
`status` reads), re-applies after resume (the BIOS restores the baseline on
wake), retries the SMU write, and on any WHEA event restores the baseline and
stops with code 10. A profile starts in `validating` and becomes `steady`
after 20 h guarded or 7 days without WHEA.

Data lives in `%LOCALAPPDATA%\rycolab` (`RYCOLAB_HOME` overrides): `bin\`,
`tools\y-cruncher\`, `config.json` (baseline, engines, tests, seconds),
`profile.json`, `state.json`, `validation.json`, `guard\` (journal, SQLite,
positives) and `campaigns\<name>\` (`runs.jsonl`, `samples.jsonl`,
`rycolab.db`, `limits.json`, `in-progress.json`, `positives\`).

Sweep signals: y-cruncher compute error, dead process, WHEA (17-20, 46, 47) or
Kernel-Power 41 during the run, and machine hang (`in-progress.json` still
there when the sweep starts again).

Low-level commands for diagnostics (elevated): `probe`, `apply`, `reset`,
`guard`, `watch`, `sensors`, `plan` (config.json), `task`, `profile import`.
`rycolab help` lists them all.

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
