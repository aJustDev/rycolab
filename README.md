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

Tested on one machine so far (see `docs/lab-notebook.md`): per-core sweep with
y-cruncher, four positive signals (compute error, process crash, WHEA, machine
hang), a guard that re-applies the profile after resume, JSONL + SQLite
records, reports, installer. Other Ryzen models should work but have not been
tried; the per-core telemetry needs `rycolab dev calibrate` on a table version
other than the reference one.

## Usage

Needs Windows, the .NET runtime (9 or newer; the exe rolls forward) and an
AMD Ryzen with per-core Curve Optimizer through the SMU. Commands that touch
the hardware need an elevated console (PowerShell or Terminal opened with
"Run as administrator"; `sudo rycolab ...` also works if Windows sudo is
enabled); `rycolab`, `status`, `report` and `profile show` do not.

```
rycolab install               copy to %LOCALAPPDATA%\rycolab, user PATH, y-cruncher (official zip,
                              SHA-256 checked), baseline read from the hardware, scheduled task
rycolab                       one screen: installed?, profile, guard, last sample, and what to do next
rycolab find [--quick]        find each core's limit and propose the profile (hours; hands off; it may reboot
                              and resume). --quick: three tests and 180 s per run instead of eight and 360 s
rycolab profile from-sweep <campaign> [--margin 5]     profile = limit + margin, with its source
rycolab on                    apply the profile and keep it: hidden guard, re-applied after sleep and at logon
rycolab status [--once]       live panel with everything applied, refreshed every 2 s until Ctrl+C:
                              Curve Optimizer (guard, phase, per-core), battery profile, Lenovo EC
                              (needs sudo; hints otherwise), Windows scheme. --once prints and exits;
                              --follow is the per-core guard view
rycolab off                   stop the guard, back to the BIOS baseline, task disabled
rycolab report [<campaign>]   limits, positives with time to error, telemetry, events; --md
rycolab report --bench <csv> [--vs <csv>] [--battery]   summary of a `dev log` CSV: power, temps, clocks, V, fans, battery
rycolab report --health       battery capacity history: the guard samples FullChargedCapacity, design
                              capacity and cycle count once a day, so degradation shows as data, not guesswork
rycolab power show|battery|ac|restore|auto on|off   Lenovo Legion battery profile (see below)
rycolab charge show|normal|conservation|rapid|night on|off   Lenovo battery charge mode through the Energy
                              driver (\\.\EnergyDrv, what Legion Toolkit's battery section talks to): conservation
                              stops at ~80 % (firmware threshold), rapid charges fastest; night charge is a separate
                              slow-overnight toggle. Every write is read back; the Vantage registry key is kept in sync
rycolab charge full [--target 98]   one-shot full charge: rapid now, and the running guard restores the
                              previous mode when the battery reaches the target (a manual mode change cancels it)
rycolab uninstall [--purge]   task, PATH and binaries; --purge also the data
```

### First run on a new machine

1. `rycolab install` (elevated, from the build output the first time). It
   prints the CPU, its core count, the SMU type and whether per-core Curve
   Optimizer is available, and reads the current margins as the baseline.
2. `rycolab dev probe` (elevated): reads every core's margin. Nothing is
   written. If any core is not readable, stop here.
3. Optional but recommended on hardware nobody has tried: one harmless write
   and back, `rycolab dev apply --core 0 --margin -3`, `rycolab dev probe`
   (only core 0 changed), `rycolab dev reset`.
4. `rycolab find --quick --cores 0`: about ten minutes on one core, shows the
   whole flow (checks, estimate, confirmation, live table, proposal) without
   committing to anything; a partial sweep is not saved unless `--accept`.
5. `rycolab find`: the real campaign. It prints the estimate and asks before
   starting; leave the machine plugged in and alone (a too-deep margin can
   reboot it; `rycolab find` again resumes). Accept the proposal at the end,
   then `rycolab on`.

`on` refuses a profile without a source, from another CPU, or with any core
below its measured limit. The guard writes `state.json` on every sample (what
`status` reads), re-applies after resume (the BIOS restores the baseline on
wake), retries the SMU write, and on any WHEA event restores the baseline and
stops with code 10. A profile starts in `validating` and becomes `steady`
after 20 h guarded or 7 days without WHEA and without an unexpected reboot
(Kernel-Power 41 since the previous guard tick is recorded as a `reset` event
and counted; a hard reset leaves no WHEA). Bad news (WHEA, reset, margin
lost, giveup) also raises a Windows toast with its own chime, one per kind
per 10 min; `rycolab dev plan set notify false` turns it off and
`rycolab dev toast` sends a test one.

Data lives in `%LOCALAPPDATA%\rycolab` (`RYCOLAB_HOME` overrides): `bin\`,
`tools\y-cruncher\`, `config.json` (baseline, engines, tests, seconds),
`profile.json`, `state.json`, `validation.json`, `guard\` (journal, SQLite,
positives) and `campaigns\<name>\` (`runs.jsonl`, `samples.jsonl`,
`rycolab.db`, `limits.json`, `in-progress.json`, `positives\`).

Sweep signals: y-cruncher compute error, dead process, WHEA (17-20, 46, 47) or
Kernel-Power 41 during the run, and machine hang (`in-progress.json` still
there when the sweep starts again).

Low-level commands for diagnostics (elevated): `probe`, `apply`, `reset`,
`guard`, `sweep`, `watch`, `sensors`, `calibrate`, `plan` (config.json), `task`,
`profile import`, `log`, all under `rycolab dev`. `rycolab dev help` lists them.

To compare a benchmark before and after a change (a profile, a thermal mod):
`rycolab dev log --out before.csv` (elevated) while Cinebench or the game
runs, Ctrl+C or `--minutes N` to stop, the same again for `after.csv`, then
`rycolab report --bench after.csv --vs before.csv`. The CSV has package
power, Tctl and CCD temperatures, effective clock (average and per core),
core voltages from the PM table, VID, and on Lenovo Legion machines the CPU /
GPU / PCH fan speeds and EC temperatures (the same WMI call Legion Toolkit
uses; HWiNFO does not see these fans), plus the AC line and the battery's
discharge rate, charge and remaining Wh. The summary uses the samples above
100 W of package power (`--min-power`), so idle before and after does not
dilute the means; with `--battery` it uses the samples on battery instead and
adds the runtime a full charge would give at the mean discharge.

### Fans on Lenovo Legion

The EC drives the fans from a 10-level table (CPU fan 1700...5200 RPM on the
reference machine) and ramps at about 60 RPM/s whatever the curve says, so
under a sustained load the CPU sits at its thermal limit for a minute before
the fan reaches its top level. The "maximum fan speed" switch in Legion
Toolkit (`FanFullSpeed`, WMI `LENOVO_OTHER_METHOD` id `0x04020000`) goes past
the table (5700 / 5700 / 7200 RPM) and ramps in seconds; measured under
Cinebench R23 at the same 145 W it gave -3 C and +107 MHz sustained. The EC
only honours the switch in the custom power mode (smart fan mode 255), so
`fan on` and `fan auto` select it themselves (the same WMI call Legion
Toolkit makes), print the CPU power limits the custom slot runs with (never
written by rycolab), and `fan off` or the end of `auto` restore the mode
found. `rycolab fan show` prints mode, limits, switch, fans and EC temperatures. `rycolab fan auto` (elevated)
turns the switch on after `--hold` seconds at or above `--on` C of EC CPU
temperature, off below `--off`, and off again when it exits. Legion Toolkit, if
running, re-applies its own preset (mode, switch and, if
`amd_overclocking.json` exists, its per-core Curve Optimizer) on mode
change, AC events, resume and start; the guard restores the profile within
one interval, `auto` reports the mode change. The EC's fan table is untouched.

### Battery profile on Lenovo Legion

`rycolab power battery` (elevated) changes, in this order, what makes the
difference on battery and nothing else: the EC power mode to quiet (the CPU
limits it runs with are printed, never written), the GPU mode to iGPU only
(`LENOVO_GAMEZONE_DATA.SetIGPUModeStatus`, Legion Toolkit's "Hybrid mode -
iGPU only"; no reboot; the EC is told whether the dGPU node has gone, as
Legion Toolkit does), the internal panel to 60 Hz (a display mode change,
frequency only) and 40 % brightness, and the DC values of the active Windows
power scheme (boost mode off, max processor state 99 %, PCIe ASPM maximum,
Wi-Fi maximum power saving, USB selective suspend). `--gpu igpu|auto|keep`,
`--hz`, `--brightness` (a non-numeric value like `--brightness keep` leaves
it alone), `--mode quiet|keep`, `--no-windows` and `--close-apps` (kills
Legion Toolkit and HWiNFO) tune it. Measured on the reference machine
(A-B-A, fixed video segment): quiet -1.2 W and 60 Hz -2.1 W carry the
profile (-13 % all together, 3.5 -> 4.0 h of video); brightness and the DC
block moved nothing there, and under load no CPU knob improves work per Wh
(race-to-idle: the platform's own ~45 W DC cap already governs efficiency;
quiet costs +20 % energy per task while being quieter and cooler). Everything is snapshotted before the first
change (`power-prev.json`) and `rycolab power ac` puts it back; `power
restore` writes every snapshot value even if it looks untouched. `power
show` prints line, discharge W, charge, GPU mode and dGPU presence, panel,
brightness, the Windows slider per line and the DC values. The Windows
power-mode slider is not written: Windows keeps one position per line and
switches it itself.

`rycolab power auto on` makes the guard apply the battery profile 15 s after
the AC line drops and restore it 15 s after it is back (the debounce ignores
the line blips of a few seconds that the reference machine's adapter
produces). One change per knob, never a burst. The guard must already be
running (`rycolab on`, which needs AC to start; it keeps running on
battery). Each knob was measured on the reference machine before going in
(see the lab notebook); a knob that does not move the discharge rate is not
in the default profile.

## Supported hardware

- Tested: Ryzen 9 9955HX3D (Legion Pro 7 16AFR10H), 16 cores, two CCDs.
- Expected to work: any Ryzen whose SMU exposes `SetDldoPsmMargin` in
  ZenStates.Core (Zen 3 desktop, Cezanne, Phoenix / Hawk Point, Raphael /
  Dragon Range, Granite Ridge / Fire Range). The core mask is the one Legion
  Toolkit uses (plain core index on APUs); `install` prints the SMU type and
  whether per-core Curve Optimizer is available, and refuses if it is not.
- Not supported: Zen 2 and Renoir / Lucienne / Van Gogh (no per-core command).
- Locked by AMD: mobile APUs below Ryzen 9 (Ryzen 5/7 5000H/U, 6000, 7040...).
  The SMU accepts the read but answers `FAILED` to every write (RSMU and MP1,
  16- and 20-bit margin) - verified on a Ryzen 7 5800H; the same is reported
  for the 5800H and 6850U in [RyzenAdj issue #233](https://github.com/FlyGoat/RyzenAdj/issues/233),
  where the UXTU author states Curve Optimizer on 5000+ mobile APUs works
  only on Ryzen 9 (HX/HS). `rycolab dev apply` says so when it happens.
- The core count comes from the CPU; the per-core telemetry (voltage, clock,
  power, temperature from the SMU table) needs `rycolab dev calibrate` once
  on any table version other than the reference machine's.
- y-cruncher engines are chosen for the CPU at `install`: `04-P4P` plus
  `24-ZN5 ~ Komari` (AVX-512) on Zen 4/5, or `19-ZN2 ~ Kagari` (AVX2) on
  Zen 2/3. On Zen 3 which engine finds the errors has not been measured yet.

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

Needs a **.NET SDK 9 or newer** (x64); the project targets `net9.0-windows`
and newer SDKs build it.

```
dotnet build -c Release src/Rycolab.Cli
```

`rycolab` is not on the PATH until `install` puts it there, and the
installed copy does not update itself when you rebuild. `install.ps1` does
both steps: builds Release and runs `install` from the build output,
elevating if the console is not:

```
.\install.ps1
```

(`-NoBuild` skips the build; `-Args "--ycruncher C:\folder"` passes options
to `install`.) `.\uninstall.ps1` is the reverse (`-Purge` also deletes the
data); it runs `rycolab uninstall` from the build so the installed folder
can be removed completely. By hand it is the same thing, from an elevated console:

```
.\src\Rycolab.Cli\bin\Release\net9.0-windows\win-x64\rycolab.exe install
```

Then open a new console (the PATH change does not reach the current one)
and `rycolab` works from anywhere.

`inpoutx64.dll`, the port-access layer ZenStates.Core needs, ships in
`third_party/inpout` (InpOut32, MIT) and is copied next to the executable at
build time. To update an existing install: `rycolab off`, build, run
`install` from the new build, `rycolab on`.

## License

GPL-3.0. The core mask encoding and the SMU access sequence derive from Lenovo
Legion Toolkit, also GPL-3.0. See `NOTICE`.

## Warning

This writes to your processor's SMU mailbox. A bad undervolt produces wrong
results before it produces visible failures. Use it knowing that.
