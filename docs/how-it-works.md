# How it works

Field notes measured on the reference machine (Ryzen 9 9955HX3D, Legion Pro 7
16AFR10H) and checked against the sources. `file:line` anchors are in
`sources.md`; the raw numbers are in `lab-notebook.md`.

## Writing a margin

Per-core Curve Optimizer margins are PSM margins written through the SMU
mailbox (`SetDldoPsmMargin`) via ZenStates.Core. On CPUs with CCDs the core
mask is `((ccd << 8) | core) << 20` for reads and writes (Legion Toolkit).
On APUs reads take the flat core index, but writes need `core << 20`:
ZenStates packs the argument as `(mask & 0xFFF00000) | (margin & 0xFFFF)`,
so a flat index would be masked away and every write would hit core 0. APUs
also take the write on the MP1 mailbox (Cezanne 0x54, Rembrandt / Phoenix /
Hawk Point / Strix 0x4B, as ryzenadj and UXTU do), which `CoController`
fills into ZenStates' MP1 table; ZenStates alone only knows the RSMU
message, which the Ryzen 7 5800H rejected. `rycolab dev probe` shows which
mailbox writes use.

House rules (`Safety.cs`, `CoController.cs`, `Stepper.cs`, `SafetySession.cs`):

- Allowed range -50..0 (-30..0 on Zen 3). -50 is the SMU minimum on Ryzen
  7000+; a positive value raises the voltage and is always rejected.
- Every write is read back; a mismatch is a hard failure. (Until 0.3.0 a
  move was also walked in stops of 3 counts; the SMU applies a margin
  atomically, so the stops only cost time.)
- AC power is required.
- A block that writes runs under `SafetySession`: if the process dies (Ctrl+C,
  exception, console closed) before committing, the cores go back to what
  they were.
- **A reboot and a sleep/resume both return the cores to the BIOS baseline**
  (the all-core setting; -5 on the reference machine). That is the safety net,
  and it is why the profile has to be re-applied on resume (`Guard`).

## Signals

Four signals count as a positive, all validated on the reference machine:

| Signal | How it is seen | Example |
|---|---|---|
| Compute error | y-cruncher output line matching `error|fail|mismatch|invalid|exception|crash` (minus `0 errors`, `no errors`, `passed`, `Stop on Error`); exit 1 with `StopOnError` | `SFTv4 Failed`, `Bottom word mismatch`, `Checksum Mismatch` |
| Process crash | the engine process ends on its own | exit `0xc0000005`, mini-dump |
| Hardware error | Windows System log, `WHEA-Logger` ids 17-20, 46, 47 or `Kernel-Power` 41 since the run started | id 47, corrected, memory component |
| Machine hang | a run still `running` in the database when the sweep starts again after a reboot (the BIOS restored the baseline by itself) | cold reboot, Kernel-Power 41 |

A run is **clean** only when the engine is killed by the harness after the
full duration with none of the above.

## The sweep

Per core, four stages, each run at a verified margin and each run restoring
the baseline:

| Stage | What | Default |
|---|---|---|
| sweep | from the start margin (-50) upwards in coarse steps with the sweep engine; the first clean margin ends it | coarse 10, 360 s |
| fine | the step below that margin, when the coarse step skipped it | fine 5, 360 s |
| confirm | a long run at the limit; a positive moves the limit one step up and confirms again | 1800 s |
| soak | light load (`04-P4P`, the engine that reaches fMax) at limit + safety margin, where the profile will actually run; a positive moves the limit one step up and soaks again | 600 s |

The **limit** recorded for the core is the one that survived confirm and
soak; the profile is limit + safety margin (5). Every run writes the margin,
verifies it, inserts a `running` row in the database, runs the engine
pinned to the core with periodic suspension, samples telemetry at 1 Hz (a
row per sample), kills the engine, restores the baseline, and fills in the
verdict with its stage. Resumable: cores with a limit are skipped, and a
row still `running` at the next start is a machine hang (positive) or, in
the same boot session, a killed process (the run repeats).

Why the stages: time to error grows as the margin rises (reference machine,
CCD0, `24-ZN5`: -50 fails in 9-39 s, -45 in 79-99 s), so 6 minutes per run
is the minimum for the search and the limit itself deserves far longer; and
the first campaign passed core 7 clean at -45 in 180 s when its real limit
was -30, which is the in-house proof that one short clean run is not a
limit. Every source agrees that a too-deep Curve Optimizer fails at idle and
light load, not under an all-core torture, hence the soak at fMax.

## Engines

Only y-cruncher, pinned to one physical core (both logicals), with the
configuration CoreCycler generates (`stressTest.cfg`, `StopOnError`), stdin
redirected (otherwise it waits for a key press on any invalid parameter), and
periodic suspension of all its threads (1 s every 10 s, `SuspendThread` /
`ResumeThread`) to force idle-to-boost transitions.

Two binaries, both needed. `install` picks the sweep engine for the CPU
(`YCruncherBinaries.Recommended`): `24-ZN5 ~ Komari` when AVX-512 is
available, otherwise `19-ZN2 ~ Kagari` (AVX2, Zen 2/3); `04-P4P` is the soak
engine. `config.json` keeps the choice; on the reference machine:

| Binary | ISA | What it does on the reference machine |
|---|---|---|
| `04-P4P` | SSE3 | the only sustained load that reaches fMax (5.45 GHz, ~1.15 V at -30, 9 W): the top of the V/F curve |
| `24-ZN5 ~ Komari` | AVX-512 | 5.3-5.4 GHz, 10-12 W; **the engine that finds the errors** on both CCDs |

Prime95 (small FFT AVX-512, and CoreCycler's SSE/Huge recipe) was measured
first and found nothing on this laptop: every sustained torture is capped at
~14 W per core, so it never reaches the high end of the curve. The recipes
were PowerShell prototypes, since removed from the tree; the numbers are in
`lab-notebook.md` (phase 0).

y-cruncher offers eight tests (`BKT, BBP, SFTv4, SNT, SVT, FFTv4, N63, VT3`);
the reference sweep used three (`SFTv4, FFTv4, N63`). `BKT` is scalar integer,
the lightest load, and is the one most likely to push the clock on a
power-capped machine.

## Telemetry

Per-core voltage is not available from LibreHardwareMonitor on this chip
(`Core #N VID` is one value for all cores). It comes from the SMU power
table, whose per-core blocks were located empirically by diffing two margins
with a single loaded core:

| Position | What | Check |
|---|---|---|
| `301+N` | core power (W) | equals LHM `Core #N+1 (SMU)` |
| `317+N` | core voltage (V) | 1.0832 -> 1.0675 from -5 to -25 only on the loaded core |
| `333+N` | core temperature (C) | equals Tctl with one loaded core |
| `349+N` | core frequency (GHz) | equals LHM `Core #N+1` |

Valid for table version `0x621202` (613 floats). Any other version is located by
`rycolab dev calibrate` (pm-index.json) before the per-core numbers are used.

The margin acts physically: from -5 to -25 on one core, +160 MHz and -15.7 mV
at constant 14 W; at fMax the voltage falls linearly with the margin
(~-3.8 mV per count) and the test speed stays constant (no clock stretching).

## The guard

`Guard` applies the profile, then every interval reads all cores back and
counts WHEA events since it started. On resume from sleep (event, or inferred
from a preceding suspend or a clock jump, because the Windows event can
arrive after the first sample) it waits 10 s and re-applies, retrying up to
three times because the SMU can reject the first write right after waking.
If the margin is lost without a resume it re-applies (at most three times an
hour). Any WHEA event: restore the baseline, exit with code 10. On exit,
always the baseline.

The scheduled task runs it hidden at logon; `status` reads the state file it
publishes every tick, `report` the database (see Data below).

## What the literature says, and what it means here

Every source agrees that a too-deep Curve Optimizer fails **at idle and light
load**, not under an all-core stress test (Kernel-Power 41 at the desktop,
`CLOCK_WATCHDOG_TIMEOUT`, crashes on wake or when launching a game). The sweep
finds the point where the core computes wrong; the guard, the idle soak and
days of real use with sleep are the validation. Guides recommend staying one
or two steps above the first instability; the default safety margin is +5.

## Data

One SQLite database, `%LOCALAPPDATA%\rycolab\rycolab.db`, holds the history
of everything (`Store.cs`). WAL, so the unelevated `report` and `db` read
while the guard writes; `synchronous=FULL`, so a row is on disk when the
call returns and a hang keeps the last seconds (the reason the JSONL
journal of 0.1/0.2 was write-through; the database replaced it in 0.3.0,
`rycolab db import` brings the old files in once, incrementally for a
guard journal that is still being written).

| Table | One row per | Written by |
|---|---|---|
| `campaigns` | `find` / `dev sweep` campaign (name, plan as JSON, cores, quick) | Sweep |
| `runs` | engine run: core, margin, engine, stage, verdict, seconds, error, WHEA, the telemetry medians and maxima, `boot` | Sweep (`running` first, the verdict at the end) |
| `samples` | second of a run: clock, effective clock, V, GHz, W, temperature, package W, Tctl | Sweep |
| `limits` | core closed by a campaign (null: nothing up to the top survived) | Sweep |
| `sessions` | guard start (profile, interval, ad hoc or installed, exit code) | Guard |
| `ticks` | guard interval: margins read back, WHEA, CPU load, package W, state, AC line, battery W / % / Wh, EC temperatures and fans, power and GPU mode, panel Hz and brightness; since 0.4.0 the hottest core and its temperature, mean core V and highest core GHz (PM table), seconds since the last user input, charging W and charge mode, whether the dGPU is on the bus, the Windows overlay, and the SMU read latency in ms | Guard |
| `events` | guard or sweep event (`source`, kind, detail) | Guard, Sweep |
| `health` | day: full-charge Wh, design Wh, cycles | Guard |
| `bench`, `bench_samples` | `dev log` session and its rows (the CSV is the export) | `dev log` |

`report` reads it (`<campaign>`, `guard`, `--power`, `--campaigns`,
`--health`); `rycolab db sql "..."` runs any read-only statement and
`rycolab db export <table>` dumps one as CSV or JSONL. Schema changes are
additive (`Store.Migrate`); the rule for new data is in `CLAUDE.md`. What
stays in files: `profile.json`, `config.json`, `state.json`,
`validation.json` (state and configuration), the raw engine output of a
positive (`campaigns\<name>\positives\`) and the WHEA snapshots
(`guard\positives\`).
