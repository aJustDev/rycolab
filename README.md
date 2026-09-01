# rycolab

Curve Optimizer test bench for AMD Ryzen on Windows. Finds the most
aggressive **stable** per-core undervolt your chip can take, measured and
verified against the hardware, then keeps it applied through sleep and
reboot.

```
git clone https://github.com/aJustDev/rycolab && cd rycolab
.\install.ps1                          builds and installs (elevates itself); open a new console
rycolab find --quick --cores 0         ten minutes on one core: the whole flow, nothing saved
rycolab find                           the real campaign: hours, hands off, resumes after a reboot
rycolab on                             apply the proposed profile and keep it applied
rycolab                                what is on the cores right now, and what to do next
```

Needs Windows 10 1809 or newer, the .NET runtime 9 or newer (the SDK to
build) and a Ryzen with per-core Curve Optimizer through the SMU (see
Supported hardware). Commands that touch the hardware need an elevated
console (`sudo rycolab ...` works if Windows sudo is enabled); `rycolab`,
`status`, `report` and `profile show` do not.

## What it does, and what it does not

It writes per-core Curve Optimizer margins through the SMU mailbox
(ZenStates.Core, the library Legion Toolkit, ZenTimings and SMUDebugTool
use), reads every write back, finds each core's limit with y-cruncher, and
runs a guard that re-applies the profile after sleep and at logon, watches
for hardware errors and returns to the BIOS baseline when something is wrong.

It does not touch Curve Shaper, PBO limits, all-core offsets, the BIOS, or
anything but the per-core margins. Every write is volatile: a reboot returns
the CPU to the BIOS state. A profile belongs to one die; never copy one from
another machine, not even the same model (the fingerprint cannot tell two
chips apart, and the limits are the silicon lottery).

## Why it exists

Vendor tools let you **write** per-core Curve Optimizer margins but not
**check** what actually got applied. Legion Toolkit even reads the real margin
from the SMU in `LoadFromHardwareAsync` and overwrites the UI with the JSON on
disk three lines later. Without that read-back, tuning an undervolt is
guessing. First thing the probe showed on the reference machine:

```
With Legion Toolkit open        CCD0 -3   CCD1 -7     (the profile)
After a reboot, without it      -5 on all sixteen     (the BIOS)
```

Two different configurations depending on whether an app was open, and no tool
said so. Writers do not add up either: they replace each other, last one wins.

## Safety model

1. **Detect compute errors, not hangs.** A hang is a late, terminal symptom.
   The real risk of an aggressive Curve Optimizer is silent wrong results.
2. **Never measure without verifying first.** Every configuration is read back
   from the SMU before a single data point is taken. On mismatch, abort.
3. **Safety limits have no bypass flag.** Allowed margin -50..0 (a positive
   value raises the voltage and is always rejected); every write is read
   back; a block that writes restores what was there if the process dies
   before committing; the stress campaigns insist on AC power.

`on` refuses a profile without a source, from another CPU, or with any core
below its measured limit. The guard re-applies the profile after resume (the
BIOS restores the baseline on wake), retries the SMU write, re-applies a lost
margin (three times an hour, then it gives up), and on any hardware error
restores the baseline and stops with code 10. A profile starts in
`validating` and becomes `steady` after 20 h guarded, or 7 days with at least
8 h guarded, without WHEA and without an unexpected reboot (Kernel-Power 41
since the previous guard tick counts as a `reset`; a hard reset leaves no
WHEA). Bad news (WHEA, reset, margin lost, giveup) raises a Windows toast
with its own chime, one per kind per 10 min; `rycolab dev plan set notify
false` turns it off.

Talking to the SMU needs a kernel driver: `inpoutx64.sys` (InpOut32),
installed as a system service the first time the SMU is opened and left in
place by `uninstall` because other tools share it (`uninstall --purge` asks;
see Build).

## Commands

```
rycolab                       what is on the cores right now and what to do next
rycolab install               copy to %LOCALAPPDATA%\rycolab, user PATH, y-cruncher (official zip,
                              SHA-256 checked), baseline read from the hardware, scheduled task
rycolab find [--quick]        find each core's limit and propose the profile (hours; hands off; it may reboot
                              and resume). --quick: three tests and 180 s per run instead of eight and 360 s
rycolab on                    apply the profile and keep it: hidden guard, re-applied after sleep and at logon
rycolab off                   stop the guard, back to the BIOS baseline, task disabled
rycolab status [--once]       is the profile on the cores, in what phase, last sample, events, battery;
                              refreshed every 2 s until Ctrl+C. --once prints and exits; --all adds the
                              machine, the Lenovo EC (needs sudo) and the Windows scheme; --follow is
                              the per-core guard view
rycolab report [<campaign>]   limits, positives with time to error, telemetry, events; --md writes markdown
rycolab profile show|from-sweep <campaign> [--margin 5]|export <path>
rycolab legion <command>      Lenovo Legion only: fan, power (battery profile), charge (docs/legion.md)
rycolab uninstall [--purge]   task, PATH and binaries; --purge also the data
```

`rycolab status` on the reference machine, ten hours into validation:

```
            rycolab 0.2.0   2026-09-01 22:57:15

            PROFILE APPLIED   validating   guard pid 23224 since 22:28   10.6 h guarded   0 WHEA   0 resets
profile     -35,-35,-30,-35,-35,-40,-45,-25,-45,-30,-40,-30,-40,-45,-45,-45   find-20260828-1232, limit + 5
hardware    all 16 cores on profile   last sample 22:56:38   CPU 3 %   package 28.0 W
events      22:28:33  start: profile -35,-35,-30,-35,-35,-40,-45,-25,-45,-30,-40,-30,-40,-45,-45,-45  interval 60s
            22:28:33  apply: start: profile applied and verified: -35,-35,-30,-35,-35,-40,-45,-25,-45,-30,-40,...
            22:28:48  power: AC line back -> restored the snapshot: no battery profile applied (no snapshot)
battery     AC   100.0 Wh full (100 % of 99.9 Wh design, 6 cycles)   power auto on, battery profile not applied

  Next: profile in validation: 10.6 h guarded, 1 resumes, 0 WHEA, 0 unexplained resets. Use the machine normally.
```

The first line turns red when the profile is not on the cores, and the
`hardware` row then lists every core with the wrong ones in red. The end of
`rycolab find` on the same machine (86 runs, 26 positives, 3 machine hangs,
four days with interruptions):

```
  Limits found (first clean margin per core):
    CCD0  0:-40  1:-40  2:-35  3:-40  4:-40  5:-45  6:-50  7:-30
    CCD1  8:-50  9:-35  10:-45  11:-35  12:-45  13:-50  14:-50  15:-50
  Proposed profile (limit + 5; cores without a limit stay at -5):
    CCD0  0:-35  1:-35  2:-30  3:-35  4:-35  5:-40  6:-45  7:-25
    CCD1  8:-45  9:-30  10:-40  11:-30  12:-40  13:-45  14:-45  15:-45

  Save it as your profile? [Y/n]
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
   reboot it; the campaign resumes by itself at the next logon). Accept the
   proposal at the end, then `rycolab on`.

If anything looks wrong at any point: a reboot returns the CPU to the BIOS
values, and `rycolab off` does the same without rebooting.

Data lives in `%LOCALAPPDATA%\rycolab` (`RYCOLAB_HOME` overrides): `bin\`,
`tools\y-cruncher\`, `config.json` (baseline, engines, tests, seconds),
`profile.json`, `state.json`, `validation.json`, `guard\` (journal, SQLite,
positives) and `campaigns\<name>\` (`runs.jsonl`, `samples.jsonl`,
`rycolab.db`, `limits.json`, `in-progress.json`, `positives\`).
`docs/profile.reference.json` is the reference machine's profile, kept as an
example of the format only.

## How it works

Per core, from -50 upwards in steps of 5; per margin, every engine in the
plan (y-cruncher `04-P4P` and the widest vector binary the CPU can run),
pinned to the core with periodic thread suspension, 360 s per run, telemetry
at 1 Hz from the SMU power table; the limit is the first margin clean on all
engines, and the proposed profile is the limit plus a safety margin (5).
Positives: y-cruncher compute error, dead process, WHEA (18-20, 46, 47; id 17
is PCIe, logged but not counted) or Kernel-Power 41 during the run, and
machine hang (a run left in progress across a reboot; in the same boot
session it is a killed process and the run repeats). Every run restores the
baseline; a run that lost time (sleep) or margin is invalid and repeats. The
details, the engines and what the literature says are in
`docs/how-it-works.md`; every number in `docs/lab-notebook.md`.

Low-level commands for diagnostics (elevated): `probe`, `apply`, `reset`,
`guard`, `sweep`, `watch`, `sensors`, `calibrate`, `plan` (config.json), `task`,
`profile import`, `log`, all under `rycolab dev`. `rycolab dev help` lists them.

To compare a benchmark before and after a change (a profile, a thermal mod):
`rycolab dev log --out before.csv` (elevated) while Cinebench or the game
runs, Ctrl+C or `--minutes N` to stop, the same again for `after.csv`, then
`rycolab report --bench after.csv --vs before.csv`. The CSV has package
power, Tctl and CCD temperatures, effective clock (average and per core),
core voltages from the PM table, VID, and on Lenovo Legion machines the fan
speeds and EC temperatures, plus the AC line and the battery's discharge
rate. The summary uses the samples above 100 W of package power
(`--min-power`); with `--battery` it uses the samples on battery instead and
adds the runtime a full charge would give at the mean discharge.
`rycolab report --health` is the battery capacity history the guard samples
once a day.

## Lenovo Legion extras

`rycolab legion fan|power|charge` drives what only a Legion machine has: the
EC fan switch, a measured battery profile (quiet mode, iGPU only, 60 Hz) that
the guard can apply on AC line changes, and the charge modes. Not needed for
Curve Optimizer; see `docs/legion.md`.

## Supported hardware

- Tested: Ryzen 9 9955HX3D (Legion Pro 7 16AFR10H), 16 cores, two CCDs of
  eight, SMT on. **The core map is this one's**: 8 cores per CCD, no disabled
  cores, core N on logical processor 2N. A 6-core CCD (7600X, 7900X, 9900X),
  a part with fused-off cores or SMT off is not handled yet.
- Expected to work with that topology: any Ryzen whose SMU exposes
  `SetDldoPsmMargin` in ZenStates.Core (Zen 3 desktop, Cezanne, Phoenix /
  Hawk Point, Raphael / Dragon Range, Granite Ridge / Fire Range). The core
  mask is the one Legion Toolkit uses (plain core index on APUs); `install`
  prints the SMU type and whether per-core Curve Optimizer is available, and
  refuses if it is not.
- Not supported: Zen 2 and Renoir / Lucienne / Van Gogh (no per-core command).
- Locked by AMD: mobile APUs below Ryzen 9 (Ryzen 5/7 5000H/U, 6000, 7040...).
  The SMU accepts the read but answers `FAILED` to every write (RSMU and MP1,
  16- and 20-bit margin) - verified on a Ryzen 7 5800H; the same is reported
  for the 5800H and 6850U in [RyzenAdj issue #233](https://github.com/FlyGoat/RyzenAdj/issues/233),
  where the UXTU author states Curve Optimizer on 5000+ mobile APUs works
  only on Ryzen 9 (HX/HS). `rycolab dev apply` says so when it happens.
- The per-core telemetry (voltage, clock, power, temperature from the SMU
  table) needs `rycolab dev calibrate` once on any table version other than
  the reference machine's; without it the sweep still works, with less data.
- y-cruncher engines are chosen for the CPU at `install`: `04-P4P` plus
  `24-ZN5 ~ Komari` (AVX-512) on Zen 4/5, or `19-ZN2 ~ Kagari` (AVX2) on
  Zen 2/3. On Zen 3 which engine finds the errors has not been measured yet.

What was measured on the reference machine, and what it contradicts in the
usual sensor tools, is in `docs/field-notes.md`.

## Build

Needs a **.NET SDK 9 or newer** (x64); the project targets
`net9.0-windows10.0.17763.0` and newer SDKs build it.

```
dotnet build -c Release src/Rycolab.Cli
dotnet test -c Release tests/Rycolab.Tests
```

The tests cover the logic that does not need the hardware (mask encoding,
the walk in steps, argument parsing, profile refusal rules, the y-cruncher
error criterion against real outputs, the journal to SQLite rebuild). CI
runs them on every push.

`rycolab` is not on the PATH until `install` puts it there, and the
installed copy does not update itself when you rebuild. `install.ps1` does
both steps: builds Release and runs `install` from the build output,
elevating if the console is not (`-NoBuild` skips the build; `-Args
"--ycruncher C:\folder"` passes options to `install`). `.\uninstall.ps1` is
the reverse (`-Purge` also deletes the data). By hand it is the same thing,
from an elevated console:

```
.\src\Rycolab.Cli\bin\Release\net9.0-windows10.0.17763.0\win-x64\rycolab.exe install
```

Then open a new console (the PATH change does not reach the current one).
To update an existing install: `rycolab off`, `.\install.ps1`, `rycolab on`.

`inpoutx64.dll`, the port-access layer ZenStates.Core needs, ships in
`third_party/inpout` (InpOut32, MIT) and is copied next to the executable at
build time. The first time the SMU is opened it installs its kernel driver
(`inpoutx64.sys`) as a system service. `rycolab uninstall` leaves it in
place because other tools (ZenTimings, SMUDebugTool) share it;
`uninstall --purge` asks whether to remove it (`--yes` answers for you).
ZenStates.Core 1.0.1 does not start without the DLL even though it embeds
PawnIO modules, so PawnIO alone is not an option yet.

## License

GPL-3.0. The core mask encoding and the SMU access sequence derive from Lenovo
Legion Toolkit, also GPL-3.0. See `NOTICE`.

## Warning and disclaimer

This writes to your processor's SMU mailbox. A bad undervolt produces wrong
results before it produces visible failures. Use it knowing that.

**Use at your own risk.** Undervolting can crash, reboot or corrupt data on
your machine, and an unstable margin can do so days later, at idle. This
software is provided "as is", without warranty of any kind; the authors are
not liable for any damage to your hardware, data or anything else arising
from its use (GPL-3.0, sections 15 and 16). Every write it makes is
volatile - a reboot returns the CPU to the BIOS state - but what runs while
a margin is unstable is your responsibility.
