# Lab notebook: Ryzen 9 9955HX3D (Legion Pro 7 16AFR10H)

Data only, in order. Raw runs live in `runs/` (git-ignored); what matters is
copied here. Cores are 0-based; CCD0 = cores 0-7 (V-Cache), CCD1 = 8-15.
BIOS: all-core Curve Optimizer sign -, magnitude 5 (the baseline, -5 on all
cores after every reboot), PBO scalar 1x, Legion Optimization enabled. The
BIOS (SMCN20WW) exposes no per-core CO and no Curve Shaper.

## 2026-08-27 - Phase 0: does the detector see anything?

Prime95 30.19 pinned to core 11, one worker. Signal = `results.txt` progress
(first self-test at 20 s, ~3 lines/min, deterministic FFT sequence).

| Regime | Margins | Runs | Result |
|---|---|---|---|
| small FFT (AVX-512), 180 s | -8 ... -25 in steps of 3 | 3 per margin | clean 3/3 everywhere, WHEA 0 |
| SSE, FFT Huge 8960K-32768K + suspension 1 s/10 s, 360 s | -25, -28, -30 | 3 per margin | clean 3/3, WHEA 0 |

Physical contrast -5 / -25, same core and load, medians of 3 x 176 samples
(`watch`, LHM + SMU PM table v0x621202):

| Margin | Clock | Effective | V core (PM) | GHz (PM) | W core | T core |
|---|---|---|---|---|---|---|
| -5 | 5005-5010 | 2547-2551 | 1.0832 | 5.005 | 13.96-13.99 | 72.8 |
| -25 | 5165-5170 | 2628-2632 | 1.0675 | 5.167 | 13.92-13.96 | 72.7 |
| delta | **+160 MHz** | +3.1 % | **-15.7 mV** | +3.2 % | 0 | 0 |

(5.167/5.005) x (1.0675/1.0832)^2 = 1.003: constant power. The margin acts;
the core is capped at ~14 W and the undervolt turns into clock. Neither
Prime95 regime reaches fMax (5.45 GHz) on this laptop.

An earlier attempt with 16 workers (recipe without `NumCores`) gave a "mute"
-8 that did not reproduce with one worker: it was the load, not the silicon.

## 2026-08-27 - y-cruncher, core 11, 360 s, suspension 1 s/10 s, 1 thread, 1 GiB

| Binary | Tests | Margin | Result | GHz | V p50 / max | W | T |
|---|---|---|---|---|---|---|---|
| `04-P4P` (SSE3) | SFTv4, FFTv4, N63 | -30 | all `Passed` | **5.450** | 1.153 / 1.165 | 9.1 | 64.3 |
| `24-ZN5 ~ Komari` (AVX-512) | SFTv4, FFTv4, N63 | -30 | all `Passed` | 5.289 | 1.113 / 1.178 | 10.5 | 70.8 |
| `04-P4P`, core 0 (CCD0) | idem | -30 | all `Passed` | 5.150 | 1.065 / 1.073 | 7.3 | 60.0 |

`04-P4P` is the only engine that takes a core to fMax. The safety limit was
then raised by explicit decision from -30 to -40 and to -50 (the SMU minimum).
Core 11 with `04-P4P`: -35, -40, -45, -50 all clean; 30 min at -50 clean
(8 iterations, 24 `Passed`, WHEA 0). Voltage at fMax per margin: -30 1.153,
-35 1.133, -40 1.119, -45 1.095, -50 1.076 V (linear, ~-3.8 mV per count);
SFTv4 speed constant across the range (no clock stretching).

CoreCycler 0.11.0.4 in manual mode on core 11 at -45 (`04-P4P`, 6 min,
suspension): `No core has thrown an error`, `No WHEA errors`. Agrees.

## 2026-08-27 - Phase 1: per-core sweep (`fase1.ps1`, from -50 in steps of 5)

Per core and margin: `04-P4P` then `24-ZN5`, 360 s each, suspension 1 s/10 s,
tests SFTv4/FFTv4/N63. Limit = first margin clean on both. Every run restores
-5.

### Positives (the first of the project)

| Time | Core | Margin | Engine | Signal | After |
|---|---|---|---|---|---|
| 14:50:51 | 0 | -50 | `04-P4P` | **crash** `0xc0000005` (mini-dump), right after a resume from suspension; 1/2 on repeat | 294 s |
| 15:01:30 | 0 | -50 | `24-ZN5` | `SFTv4 Failed`, `Bottom word mismatch` | 29 s |
| 15:09:40 | 0 | -45 | `24-ZN5` | idem | 79 s |
| 15:29:44 | 1 | -50 | `24-ZN5` | idem | 39 s |
| 15:38:05 | 1 | -45 | `24-ZN5` | idem | 89 s |
| 15:57:38 | 2 | -50 | `24-ZN5` | idem | 9 s |
| 16:06:08 | 2 | -45 | `24-ZN5` | idem | 99 s |
| 16:26:06 | 3 | -50 | `24-ZN5` | idem | 29 s |
| 16:46:15 | 4 | -50 | `24-ZN5` | **cold reboot** (Kernel-Power 41) | ~45 s |
| 20:33:37 | 8 | -50 | `24-ZN5` | **WHEA 47** (corrected, memory component, physical address `0x100b3d5207`); the run passed | 35 s |
| 20:45:42 | 9 | -50 | `24-ZN5` | `SFTv4 Failed`, `Checksum Mismatch` | 9 s |
| 20:53:32 | 9 | -45 | `24-ZN5` | idem | 59 s |
| 21:25:35 | 11 | -50 | `24-ZN5` | `Bottom word mismatch` | 9 s |
| 21:48:37 | 12 | -50 | `24-ZN5` | `Checksum Mismatch` | 19 s |

### Limits (6 min, both engines)

| Core | CCD | Limit | -50 `04-P4P` / `24-ZN5` | -45 | -40 | V at fMax at the limit |
|---|---|---|---|---|---|---|
| 0 | 0 | **-40** | clean (crash 1/2) / fail 29 s | clean / fail 79 s | clean / clean | 1.033 |
| 1 | 0 | **-40** | clean / fail 39 s | clean / fail 89 s | clean / clean | 1.035 |
| 2 | 0 | **-40** | clean / fail 9 s | clean / fail 99 s | clean / clean | 1.034 |
| 3 | 0 | **-45** | clean / fail 29 s | clean / clean | | 1.027 |
| 4 | 0 | **-45** | clean / **reboot** | clean / clean | | 1.024 |
| 5 | 0 | **-45** (a) | not tested | clean / clean | | 1.033 |
| 6 | 0 | **-45** (a) | not tested | clean / clean | | 1.041 |
| 7 | 0 | **-45** (a) | not tested | clean / clean | | 1.042 |
| 8 | 1 | **-50** (b) | clean / clean (WHEA 47 at 35 s) | | | 1.078 |
| 9 | 1 | **-40** | clean / fail 9 s | clean / fail 59 s | clean / clean | 1.096 |
| 10 | 1 | **-50** | clean / clean | | | 1.090 |
| 11 | 1 | **-45** | clean / fail 9 s | clean / clean | | 1.108 |
| 12 | 1 | **-45** | clean / fail 19 s | clean / clean | | 1.079 |
| 13 | 1 | **-50** | clean / clean | | | 1.092 |
| 14 | 1 | **-50** | clean / clean | | | 1.109 |
| 15 | 1 | **-50** | clean / clean | | | 1.107 |

(a) Cores 5-7 were started at -45 (the four before had failed at -50 and core
4 rebooted the machine); limit by definition, no positive of their own.
(b) The only WHEA event of the project happened during that run; -50 on core 8
is not a clean limit.

```
CCD0   0:-40  1:-40  2:-40  3:-45  4:-45  5:-45  6:-45  7:-45
CCD1   8:-50* 9:-40 10:-50 11:-45 12:-45 13:-50 14:-50 15:-50     * WHEA 47
```

Pattern: `04-P4P` passes -50 on every core; `24-ZN5` (AVX-512) is the engine
that discriminates, and time to error grows with the margin (-50: 9-39 s;
-45: 59-99 s). CCD1 reaches fMax 5.45 GHz with `04-P4P`; CCD0 stays at
5.15 GHz. Core 11 had only passed -50 with `04-P4P` earlier in the day; with
`24-ZN5` it fails at -50.

WHEA: zero events in the whole System log history until 2026-08-27 20:33:37
(id 47, warning, corrected, memory, during core 8 at -50 with `24-ZN5`).

## 2026-08-27 - Phase 1b: idle soak with the candidate profile

Candidate = limit + 5 (core 8 treated as -45 because of the WHEA event):

```
CCD0   0:-35  1:-35  2:-35  3:-40  4:-40  5:-40  6:-40  7:-40
CCD1   8:-40  9:-35 10:-45 11:-40 12:-40 13:-45 14:-45 15:-45
```

22:43-23:14, 31 min idle (desktop + video), sample every 60 s: margin intact
31/31, WHEA 0, CPU 0-7 %. Restored to -5.

## 2026-08-27/28 - Phase 3: real use with the candidate

| Time | What | Result |
|---|---|---|
| 23:41-01:10 | real use (desktop, video), sample every 60 s | margin intact 89/89; WHEA 0 |
| 01:10:13 | sleep (Kernel-Power 42) | |
| 08:32:28 | resume (Power-Troubleshooter 1) | **hardware at -5 x 16**: sleep restores the BIOS baseline |

## 2026-08-28 - The tool in C#

- `sweep --cores 13` with the three phase-1 tests reproduced the limit
  (-50, both engines clean at 362 s, 5.45 GHz / 1.085 V).
- Resume with guard alive, first attempt (10:11-10:14): the Windows resume
  event arrived 3 s *after* the first sample; guard re-applied immediately and
  the SMU rejected the write on core 12; guard gave up (code 1, baseline).
  Fixed: resume inferred from the preceding suspend or from a clock jump, 10 s
  settle, three attempts 5 s apart.
- Second attempt (10:42-10:45): `suspend` 10:42:53, resume inferred 10:45:12,
  Windows event 10:45:14, **profile re-applied and verified 10:45:22**.
- The hidden guard (scheduled task) has been running the candidate since
  then; `status` reads its journal.

## 2026-08-28 - The tool as a product (rycolab)

- `install`: 30 files to `%LOCALAPPDATA%\rycolab\bin`, user PATH, official
  y-cruncher zip (47 MB, SHA-256 verified), baseline read from the hardware
  (-5), config, scheduled task. Profile imported with the phase-1 limits as
  its source. `on` started the hidden guard and verified the profile within
  a minute; `status` and the bare `rycolab` work without elevation from
  `state.json`; `off` returned the baseline and disabled the task.
- `find --quick --cores 13`: checks, estimate, sweep (limit -50 again, both
  engines clean at 182 s), proposal shown and not saved (partial sweep).
- `dev calibrate --core 3`: idle table versus loaded table, plausibility of
  the loaded core, its idle value and the other fifteen, LHM as tie-breaker.
  Result 301 / 317 / 333 / 349 for power / voltage / temperature /
  frequency: the same positions found by hand on 2026-08-27. Two earlier
  criteria failed ("one core stands out" and "only one core moves"): under
  load the neighbours warm up and change clocks too.
- The PowerShell prototypes (`scripts/`) were removed from the tree; their
  logic lives in `Sweep`, `Guard` and `YCruncherEngine`. Git history before
  2026-08-28 keeps them.

## Current state and next steps

Candidate profile validated so far by: 6-min limits with two engines and
three tests, 31 min idle soak, ~2 h of real use, one sleep/resume with
re-apply. Pending: the definitive campaign from scratch with the tool (16
cores, eight y-cruncher tests, 360 s), then days of real use with sleep, and
the report. See the plan in the repository history.

## 2026-08-28 - Second machine: Ryzen 7 5800H (ASUS, Cezanne, 8 cores, 1 CCD)

- `install`: 8 cores, `TYPE_APU1`, `SetDldoPsmMargin` supported, engines
  `04-P4P | 19-ZN2 ~ Kagari` (no AVX-512). Baseline read from hardware: 0 on
  every core (the ASUS BIOS applies no Curve Optimizer).
- `dev probe`: 8 of 8 readable with the plain core index as mask
  (`0x0..0x7`), FMax 4450.
- `dev apply --core 0 --margin -3`: **the SMU rejected the write** (RSMU
  0x52, ZenStates.Core's command for Cezanne); rollback left 8 of 8 at 0.
  ryzenadj (`set_coper`) and UXTU send the per-core write to MP1 0x54 on
  Cezanne, packed as `(core << 20) | (margin & 0xFFFF)`; ZenStates packs the
  same way but masks the core with `0xFFF00000`, so the plain-index mask that
  Legion Toolkit uses on APUs would send every write to core 0. Fixed in the
  tool: reads keep the plain index, writes use `core << 20` and MP1 0x54 on
  Cezanne (0x4B on Rembrandt/Phoenix/Hawk Point/Strix, untested).
