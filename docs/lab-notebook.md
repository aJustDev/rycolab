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

## 2026-08-28 - Cinebench R23 with the candidate profile (guard on, validating)

Same protocol as the pre-project baseline (10 min minimum duration, HWiNFO
log, samples with CPU package power > 100 W: 309 in both runs). Profile
`-35,-35,-35,-40,-40,-40,-40,-40,-40,-35,-45,-40,-40,-45,-45,-45`, 0 WHEA.

| | Baseline (CO 0, 2026-08-25) | Candidate (2026-08-28) | Delta |
|---|---|---|---|
| R23 multi, 10 min | 38677 | 41433 | **+7.1 %** |
| Effective clock, 32 threads | 4316 MHz | 4613 MHz | **+297 MHz (+6.9 %)** |
| Package power | 149.5 W | 146.0 W | -3.5 W |
| Tctl | 99.8 C | 98.6 C | -1.2 C |
| Thermal limit | 99.1 % | 97.6 % | -1.5 |
| CCD0 / CCD1 Tdie | 93.3 / 99.2 C | 90.1 / 97.7 C | -3.2 / -1.5 C |
| VDDCR_VDD (SVI3) | 0.989 V | 0.961 V | **-28 mV** |

A uniform -5 (the BIOS default) gave +2 % clock at the same voltage. With the
per-core profile the saving is large enough to show up as both: ~300 MHz more
and 28 mV less, at lower power and temperature. The chip is still thermally
capped (97.6 %), so the ceiling is the cooling, not the silicon. A single
unlogged pass before this run scored 42488.

## Current state and next steps

Candidate profile validated so far by: 6-min limits with two engines and
three tests, 31 min idle soak, ~2 h of real use, one sleep/resume with
re-apply, and (as of 31/08) ~15 h guarded with 0 WHEA and 1 unexplained
reset pending explanation.

Roadmap (order agreed 2026-08-31):

1. Guard notifications: a Windows toast on WHEA / reset / giveup / margin
   lost, so a positive is seen without opening `status`. The guard already
   detects everything; only the emission is missing.
2. `rycolab charge full`: one-shot rapid charge that returns to
   conservation by itself at ~98 % (the guard has the loop to watch it).
3. Battery health history: one daily sample of FullChargedCapacity and
   cycle data into the guard's SQLite, `report --health` plots the pack's
   real degradation over months.
4. Step 5, the definitive campaign: `rycolab find` from scratch (16 cores,
   both engines, the eight-test battery, 360 s per run), then days of
   guarded validation to steady, and the report. The current profile comes
   from the phase-1 script: 3 tests (SFTv4/FFTv4/N63) x 360 s per engine
   and margin, most cores probed only at -50/-45/-40.
5. Step 6, publish: README polish, release, repo public.
6. Visual layer, when it is worth it: a tray icon reading state.json (no
   elevation) with green/amber/red and quick toggles; the `status` live
   panel stays the detail view. No web unless remote viewing is wanted.

Not planned, measured out: custom fan tables (the ceiling is the heatsink,
not the curve), per-app automation (Legion Toolkit's turf), more battery
knobs (C6-C8 said the rest is noise). Hardware note kept apart: the thermal
mod idea for the heatsink contact.

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
- With the fix: `dev probe` reports `writes via MP1 0x54`; `dev apply --core 0
  --margin -3` -> `FAILED` with arg `0x0000FFFD` (16-bit margin) and again
  `FAILED` with `0x000FFFFD` (20-bit, UXTU's encoding). Not `UNKNOWN_CMD`,
  not `CMD_REJECTED_PREREQ`: the firmware knows the message and refuses it.
  RyzenAdj issue #233 has the same result on a 5800H and a 6850U, and the
  UXTU author states that on 5000-series and newer mobile APUs Curve
  Optimizer only works on Ryzen 9 parts. **Conclusion: the 5800H is locked
  by AMD; reads work, writes never will.** Hardware left at 0 x 8.
- 18:00: full cycle on the 5800H with the final tool: `install.ps1` (build +
  install, LOCKED warning at install), `dev probe` (warning), `dev probe
  --write-test` (`FAILED` on MP1 0x54 with 16- and 20-bit margins: "writes
  are LOCKED on this CPU"), `uninstall.ps1` (task, PATH and bin removed,
  data kept). Hardware at 0 x 8 throughout.

## 2026-08-28 - Fans: table, ramp and the full-speed switch (Legion Pro 7)

Same Cinebench R23 load (145-150 W package) logged with `rycolab dev log`
(2 s samples, fans from the Lenovo EC through WMI; HWiNFO does not see them).

- Fan table (`LENOVO_FAN_TABLE_DATA`): 10 levels, CPU 1700..5200, GPU
  1700..5400, PCH 1500..6500 RPM. What Legion Toolkit writes with
  `Fan_Set_Table` are level indices 1-10, not RPM. The CPU sensor's
  temperature column is `38 41 44 47 127 127 127 127 127 127`.
- Ramp: ~60 RPM/s regardless of the curve. C4b (factory curve) and C4c
  (100 % from ~60 C) reach 5200 at the same time, ~57 s after the load
  starts, while Tctl hits 97 C at ~30 s.
- Full-speed switch (`FanFullSpeed`, `0x04020000`): 5700 / 5700-5800 / 7200-7400
  RPM, i.e. past the table's top level. Switched on with the CPU already at
  92 C it goes from 2500 to 5700 RPM in 5 s (C4f).
- A/B at 120-240 s, both 145.5 W: table 5200 -> Tctl 97.3 C, 4570 MHz;
  switch 5700 -> Tctl 94.1 C, 4677 MHz. **-3.2 C, +107 MHz (+2.3 %).**
- The EC ignores the switch outside Legion Toolkit's custom power mode
  (smart fan mode 255): written in extreme mode (224) the flag reads back 1
  and the fans stay at 1700. In custom mode `rycolab fan on` reaches 5700 in
  6 s without Legion Toolkit.
- Tool: `rycolab fan show|on|off|auto`; `auto` drives the switch from the EC
  CPU temperature with hysteresis (default on >= 85 C, off <= 80 C, 3 s
  hold) and turns it off on exit.

Logs: `Legion-Linea-Base\hwinfo-logs\C4b..C4f*.csv`.

## 2026-08-28 23:50 - `fan auto` under Cinebench, an AC blip and a hard reset

`rycolab fan auto` (on >= 90 C EC for 6 s) + `dev log` (`C4g-fan-auto.csv`),
Cinebench R23, custom mode, Legion Toolkit running with `--trace`.

- 7 s: 150 W. 17 s: EC CPU 91 C. **23 s: switch ON, fans 5600/5400/7200 in
  the same 2 s sample**, Tctl held at 94.6-95.5 C instead of climbing.
- 23:51:12 (37 s): Windows reported the AC adapter **disconnected**, then
  connected again at 23:51:13. Nobody touched the plug (user statement). Legion Toolkit's automation reacted by
  setting the power mode to Quiet and then Extreme. Out of custom mode the
  EC ignores the switch: fans back to the table's 5200 by 50 s, Tctl 97-99 C
  for the rest of the run (120-240 s: 146.8 W, Tctl 99.0, 4791 MHz). `fan
  auto` now prints the mode change when it happens.
- The log ended at 23:53:32 (3 min, 150 W, Tctl 99.8). The fans were still
  at full speed (`fan auto` keeps the switch on until the EC CPU temperature
  drops below 80 C; Ctrl+C or `rycolab fan off` would have cleared it). The
  user tried to stop them by switching Legion Toolkit to quiet and back
  several times in a few seconds, and on one of those changes the machine
  **reset**: Kernel-Power 41 at boot 23:54:42, EventLog
  6008 "unexpected shutdown at 23:51:13" (that timestamp is the last flushed
  checkpoint, not the reset time), no BugCheck, no minidump, **no WHEA**.
  The guard's last flushed tick is 23:51:11 (later ticks lost with the
  reset, like the CSV tail). Guard back at logon 23:55:01, profile applied
  and verified 23:55:04.
- Cause not established. Facts on the table: a spontaneous AC "disconnect"
  under 150 W + fans at full speed 2.5 min before (the plug was not touched);
  several power-mode changes in a few seconds at the moment of the reset,
  each one rewriting the EC power limits and fan state; the candidate profile applied
  throughout (as it has been for 9 h including three 10-min Cinebench runs
  today without incident). The validation phase now has one unexplained
  reset that `status` does not show: the guard only counts WHEA.

## 2026-08-29 00:15 - `fan auto` without Legion Toolkit, and Legion Toolkit's own CO

`C4h-fan-auto-sin-llt.csv`: custom mode set in Legion Toolkit, then Legion
Toolkit closed; `fan auto` with the new defaults (on >= 85 C for 3 s).

- Full load at 17 s; EC CPU 85 C at 23 s; **switch ON at 29 s** (12 s after
  the load, vs 23 s with the old thresholds and 57 s for the table).
- 120-180 s: 145.0 W, Tctl 94.9 C, 4780 MHz, fans 5700 / 5700 / 7400. Same
  power as the table run C4e (97.3 C, 4570 MHz).
- Ctrl+C released the switch; fans ramped down. No reset, no WHEA. The custom
  mode survives closing Legion Toolkit.
- Competing writer, caught by the guard: at 00:13:53 Legion Toolkit was
  opened and set to custom mode; at 00:14:07 the guard read `-3 x 8 / -7 x 8`
  on all cores (Legion Toolkit's per-core Curve Optimizer profile) and
  re-applied ours one second later, before Cinebench started (00:14:57).
  Legion Toolkit's custom mode writes its CO values over rycolab's; the guard
  restores them within one interval (60 s) and gives up after three within an
  hour. Fix on the Legion Toolkit side: zero or disable its per-core CO.
- 00:40: `fan on` / `fan auto` select the custom power mode themselves
  (`LENOVO_GAMEZONE_DATA.SetSmartFanMode(255)`, Legion Toolkit's call) and
  `off` / the end of `auto` restore the previous one. Verified from extreme:
  limits in the custom slot identical to extreme (PL1 135, PL2 162, peak 195,
  cross 100 W, 100 C), 5700 RPM in 6 s, back to extreme on `off`. No Legion
  Toolkit involved.

## 2026-08-30 - Battery profile: what the machine already does, and `rycolab power`

Read before writing anything (the machine was on battery, 48 %, idle on the
desktop at 240 Hz and 100 % brightness: 22-26 W discharge, ~1.9 h left):

- Windows already keeps two slider positions: AC "best performance", battery
  "best power efficiency" (`ActiveOverlay{Ac,Dc}PowerScheme`). On DC the
  boost mode is already 0 (disabled), PCIe ASPM 2 (maximum), USB selective
  suspend 1; Wi-Fi power saving is 2 of 3 and max processor state 100 %.
  Energy Saver's setting is not exposed by `powercfg` on this build. So the
  Windows block of the profile is thin by construction: two values move.
- Lenovo EC on battery: smart fan mode reports 2 (performance), the limits
  printed are the AC ones (135/162/195 W); `IsACFitForOC` 0.
- GPU: `IsSupportIGPUMode` 3, mode 0 (hybrid), dGPU present and healthy
  (RTX 5080, `PCI\VEN_10DE&DEV_2C59`). Legion Toolkit changes it without a
  reboot and then sends `NotifyDGPUStatus` with whether the dGPU PnP node is
  still there (retrying 5 x 5 s). OverDrive: unsupported here. G-Sync: off.
- Panel: 2560x1600, modes at 48 / 60 / 75 / 100 / 120 / 240 Hz.
  `WmiMonitorBrightness` works (100 %).
- Battery: `root\WMI BatteryStatus` gives DischargeRate in mW (22157 at the
  first read), RemainingCapacity and FullChargedCapacity (99990 mWh).

Built: `BatteryInfo` (WMI), `WindowsPower` (refresh rate by
`ChangeDisplaySettingsEx`, brightness by WMI, DC scheme values by
`powercfg`, slider read from the registry), `LenovoEc.IGpuMode /
SetIGpuMode / NotifyDgpuStatus / DgpuPresent`, `PowerProfile` (battery /
ac with a snapshot in `power-prev.json`), `rycolab power`, the guard's
`power auto` with a 15 s debounce on the AC line, and `dev log` columns
`ac`, `bat_w`, `bat_pct`, `bat_wh` with `report --bench --battery`.

Note for the measurements: `rycolab on` refuses to start on battery, so the
guard has to be started on AC before unplugging; it keeps running after.
Measurement protocol (video loop, 12 min per run, `power ac` between runs):
C5a baseline, C5b iGPU only, C5c quiet, C5d 60 Hz, C5e brightness 40, C5f DC
scheme, C5g apps closed, C5h all together, C5a repeated once for the noise.

## 2026-08-30 20:13-21:08 - C5: battery A/B with YouTube as the load. Inconclusive on purpose.

Guard on battery since 20:13 (AC requirement dropped from the write path;
the scheduled task also needed AllowStartIfOnBatteries +
DontStopIfGoingOnBatteries, which schtasks.exe cannot set - Service.Install
now fixes it through PowerShell). Runs of 8 min, `power ac` between them,
the user watching YouTube at 2560x1600; battery 39 -> 8 Wh, stopped by the
low-battery cutoff before C5h (all knobs) and the repeated baseline.

| Run | Mean W | vs C5a |
|---|---|---|
| C5a base (240 Hz, 100 %, hybrid, performance) | 26.4 | - |
| C5b iGPU only | 27.4 | +3.6 % |
| C5c quiet (EC refused; measured = base) | 25.5 | -3.3 % |
| C5d 60 Hz | 28.9 | +9.6 % |
| C5e brightness 40 % | 28.8 | +9.2 % |
| C5f DC scheme (max state 99, Wi-Fi save 3) | 29.8 | +12.8 % |

The deltas grow monotonically with time, including the ones that cannot
increase consumption (brightness 40 % is at most neutral): the series
measures a drift - YouTube's load and/or the pack's behaviour as it
empties - not the knobs. No knob ranking comes out of this data.

What the session did establish:
- Mechanisms all work and are reversible: iGPU only ejects the dGPU in 4 s
  and brings it back in 4 s on `power ac`; refresh rate and brightness
  change and restore; the DC writes apply and restore; snapshot clean.
- The dGPU already sleeps in hybrid mode with no load: ejecting it saved
  nothing here (13.2 vs 13.3 W package, -2.3 C Tdie).
- The EC refuses `SetSmartFanMode(0)` (quiet) on battery on this machine:
  read-back stays 2 (performance). To investigate in the Toolkit source.
- The guard held the CO profile on battery for the whole hour: 0 WHEA, no
  events beyond the expected ones.

Next: repeat with a local video loop (constant load), interleaving each
knob with a baseline run (A-B-A) so the drift cancels, starting from a
full battery. Until then nothing goes into UNDERVOLT.md 7.4.

## 2026-08-30 21:20 - The smart fan mode map was off by one; quiet works on battery

Toolkit's `PowerModeFeature` passes offset 1 to `AbstractWmiFeature`
(`PowerModeFeature.cs:26`, `AbstractWmiFeature.cs:52-54`): the WMI value is
the enum plus one. Real map: **1 quiet, 2 balanced, 3 performance, 224
extreme, 255 custom**. Our 0/1/2 map was wrong, so:

- C5c sent `SetSmartFanMode(0)`, an invalid value the EC silently ignores -
  that was the "EC refuses quiet on battery". It refuses nothing; verified
  on AC: `SetSmartFanMode(1)` -> readback 1, restore clean.
- Every mode we logged was shifted: the "performance" read on battery was
  balanced (2); Toolkit itself blocks performance/extreme/custom on battery
  in software (`PowerModeFeature.cs:61-64`), the EC does not.
- 224 and 255 were beyond the shift, so `fan on/auto` (custom, 255) always
  did the right thing.

`LenovoEc` fixed (QuietMode = 1, ModeName remapped), `PowerProfile` uses it.

## 2026-08-30 22:05 - 2026-08-31 00:15 - C6/C7: the battery profile measured clean (A-B-A, fixed video segment)

Method that finally worked: the same 6-minute 4K film segment looped in VLC
fullscreen for every run (identical decode load), 6-minute runs, every knob
between two baseline runs, compared against the mean of its two neighbours.
Base spread fell from yesterday's monotone drift to +-0.8 W. Battery
74 -> 11 Wh across both campaigns; auto-stop worked.

C6 (panel / EC / GPU knobs), baselines 28.1-29.8 W (mean 28.6):

| Knob | W | Delta vs neighbours | Verdict |
|---|---|---|---|
| iGPU only | 28.20 | -0.8 W | inside noise (dGPU already sleeps in hybrid) |
| Quiet mode (now really applied: extreme -> 1) | 27.64 | -1.2 W (-4 %) | small, real |
| 60 Hz | 26.02 | -2.1 W (-7.4 %) | real |
| Brightness 40 % | 28.08 | -0.1 W | nothing (dark film + mini-LED local dimming) |
| DC scheme block (max 99, Wi-Fi 3) | 28.01 | -0.2 W | nothing |
| All together | 25.14 | -3.8 W (-13 %) | real; equals the sum of the parts (-4.0) |

Video runtime: 3.5 h baseline -> 4.0 h with the full profile (+14 %).

C7 (CPU scheme knobs on DC), baselines 27.5-28.2 W:

| Knob | W | Delta | Verdict |
|---|---|---|---|
| EPP 50 -> 100 | 27.44 | -0.1 W | nothing |
| Max processor state 80 % | 27.24 | -0.6 W | inside noise |
| Both | 27.51 | -0.7 W vs one base | inside noise |

Package power sits at 13.4-13.6 W in every run with mean effective clocks of
~65 MHz: in near-idle video the cores are already parked and EPP / the
frequency cap have nothing to bite on (boost is already off on DC). The CPU
side of light-load battery life is uncore + platform, not core frequency
policy. DC values verified restored (EPP 50, max state 100).

Conclusion for `power battery` defaults: quiet + 60 Hz carry the profile;
iGPU only stays (its value is stopping apps from waking the dGPU, and it is
free); brightness stays as a flag (content-dependent: dark film showed
nothing, a white page will not); the DC block stays (free, harmless).

## 2026-08-31 10:08-10:47 - C8: work per Wh under load on battery. Race-to-idle wins.

Fixed work unit: `y-cruncher bench` on all 16 cores, one computation per run,
A-B-A, a logger (2 s) during each computation. First pass with 500m digits
was useless (12 s per computation, 4 samples); repeated with 5b (~145 s).

Facts established by the 500m pass anyway:
- On battery the platform caps the package at ~38-46 W whatever the mode
  (135 W on AC): every knob acts under that ceiling.
- `PROCTHROTTLEMAX` 80/60 % and EPP change neither clocks (~2400-2500 MHz
  effective) nor duration: this AMD CPPC setup ignores them with boost off.

5b results (bat W x seconds = energy per computation):

| Run | s | pkg W | eff MHz | bat W | Wh/computation |
|---|---|---|---|---|---|
| base x3 | 144.9 / 142.8 / 142.2 | 44-46 | ~1600 | 61.0 / 56.7 / 52.6 | 2.46 / 2.25 / 2.08 |
| quiet | 168.4 (+17 %) | 40.6 | 1218 | 60.8 | 2.84 (+20 % vs neighbours) |
| EPP 100 | 141.7 (=) | 44.1 | 1646 | 50.8 | 2.00 (= inside base drift) |

The base bat W drifts down through the session (61 -> 53) with pkg constant,
so small energy deltas are noise; the quiet result is outside it: 17 % slower
with the same total battery draw = more energy per unit of work, not less.
Race-to-idle: the platform's own DC cap already puts the CPU near its
efficiency sweet spot; capping further (quiet) stretches the platform
overhead over more seconds and loses.

Conclusion for working on battery: no CPU knob improves work per Wh on this
machine. Quiet mode is for noise and temperature (-4 C, fans lower), at
+20 % energy per task; EPP and max-state do nothing. `power battery` keeps
quiet for light use; no `--work` mode is warranted by the data.

## 2026-08-31 10:49 - power auto armed; battery work closed

`rycolab power auto on --brightness keep` (quiet + iGPU only + 60 Hz + DC
block, brightness untouched) and the profile applied by hand for the current
battery session: 27.6 W at the desk, ~2.1 h left at 59 %. From now on the
guard applies it 15 s after the AC line drops and restores the snapshot 15 s
after it is back. The C5-C8 campaigns close the battery chapter: the wins
are quiet + 60 Hz (+14 % video runtime); everything else measured null or
counterproductive under load.

## 2026-08-31 11:15 - rycolab charge: battery charge modes without Legion Toolkit

Toolkit drives them through \.\EnergyDrv (Lenovo Energy Management driver),
not WMI: IOCTL 0x831020F8 (query with 0xFF; bit 0x20 conservation, 0x04
rapid) and 0x83102150 (night charge; bit 0 supported, bit 4 on; write
0x80000012 / 0x12). Write sequences: conservation [0x08,0x03], normal
[0x05,0x08], rapid [0x05,0x07]. There are exactly three modes plus the
night-charge toggle; the conservation threshold (~80 %) is firmware, not
configurable. Probed read-only first on this machine (driver opens, mode was
conservation, night charge supported/off), then `rycolab charge` added with
read-back on every write and the Vantage registry key kept in sync
(BatteryChargeMode: Normal/Quick/Storage), as Toolkit does. Round-trip
verified: conservation -> rapid -> conservation. Charge line added to the
status panel's Lenovo EC section.

## 2026-08-31 13:44 - The machine slept mid-campaign; core 3's limit is tainted

Windows' AC standby timeout (1 h, 0xe10) fired two hours into the step-5
campaign: y-cruncher load does not count as activity and the sweep does not
hold ES_SYSTEM_REQUIRED. The sleep hit core 3's -45 run with 24-ZN5: samples
stop at 90 s and the run closes as "CLEAN after 1096 s" - invalid, because
sleep returns every core to the BIOS baseline (-5), so most of that run
tested nothing. The campaign itself survived (the next run re-applies and
verifies its margin before starting, so core 4 onwards is unaffected).

Actions: AC standby timeout set to 0 for the rest of the campaign (restore
to 3600 s after; DC untouched), and core 3 marked to redo - its "-45" in
limits.json must be deleted before the final resume so it is measured again.

Interim limits with the 8-test battery: 0 -40, 1 -40, **2 -35** (fase 1 said
-40: the wider test battery is stricter on core 2, exactly what step 5 was
for), 3 -45 (tainted), 13 -50.

Tool fix pending: the sweep must call SetThreadExecutionState(ES_CONTINUOUS
| ES_SYSTEM_REQUIRED) while running, and invalidate any run whose wall time
exceeds its sample time by more than the interval (a clock gap = a sleep).

## 2026-08-31 15:45 - The campaign process died with the session; cleanup

The elevated `find` process was killed at ~14:00 (its parent shell went down
with a Claude Code session restart), mid-run on core 4 (-50, 24-ZN5, 60 s
in). Cleanup, in order:

- `dev probe` read margin **+1 on all 16 cores** - neither the profile nor
  the baseline, and phase 3 had shown that sleep restores -5. Same "1 on
  all cores" was printed by `off` on 30/08 19:50. Open question: something
  (sleep path? EC?) leaves +1 where -5 is expected. `dev reset` wrote and
  verified -5 on all cores without complaint, so the SMU mailbox was fine.
- `in-progress.json` deleted by hand: on resume the sweep would have
  recorded a false "machine hang" positive for core 4 at -50
  (Sweep.cs:83-90). The kill is externally explained; core 4 restarts its
  -50 pair cleanly.
- Core 3's tainted "-45" removed from limits.json (the 1096 s CLEAN);
  core 3 will be measured again on resume.
- Validated phase-1 profile re-applied for the afternoon (`rycolab on`);
  the campaign resumes tonight with `find --resume` (cores 3-12, 14, 15).

### Correction, 15:55: it was not (only) a killed process - the machine cold-rebooted

Kernel-Power 41 at **14:00:52** (confirmed in the System log): the machine
reset ~80 s into core 4's -50 run with `24-ZN5` - a genuine positive that
reproduces phase 1 exactly (core 4 also cold-rebooted at -50 on 27/08,
16:46). The session and the campaign process died with the OS, not the
other way round. Undone/corrected:

- `in-progress.json` restored verbatim: the resume must record the hang
  positive for core 4 at -50 and continue from -45, as designed.
- validation.json Resets set back to 1: the 14:00:52 reset happened under
  campaign test margins (baseline/-50), not under the validated profile;
  counting it against the profile's validation would be wrong. The 28/08
  reset stays counted (it remains unexplained).
- The +1-on-all-cores probe reading was the post-reboot state, which
  deepens the mystery: after a *boot* the BIOS applies -5 (verified on
  27/08). +1 after this reset and after `off` on 30/08 19:50 is still
  unexplained; the reset to -5 wrote and verified fine.

## 2026-08-31 15:53 - The campaign survives reboots by itself now

Two fixes and a relaunch after the afternoon's incidents:

- `KeepAwake` (SetThreadExecutionState ES_CONTINUOUS|ES_SYSTEM_REQUIRED)
  held by the sweep for its whole life: no more mid-run sleeps.
- Auto-resume: `find` registers the scheduled task `rycolab-find-resume`
  (ONLOGON, 30 s delay, no time limit) when a campaign starts and removes
  it when the campaign completes; a cold reboot from a positive continues
  the campaign at the next logon with no human involved. `find --resume`
  with nothing pending exits quietly and removes the task.
- The campaign itself now runs *through* that task, so it no longer depends
  on any shell session either. Resumed 15:53:30: core 4's reboot recorded
  as a hang positive (continues from -45), cores 0/1/2 kept, core 3
  re-measured from -50. 12 cores pending, ~5.1 h estimated.
- Remaining gap, accepted: the ONLOGON task waits at the lock screen unless
  auto-logon is enabled (netplwiz) while campaigns run; the user does that
  by hand - the tool never stores the Windows password.

## 2026-09-01 00:07 - Step 5 closed: definitive limits on all 16 cores

Campaign `find-20260828-1232` finished: 86 runs, 26 positives, 3 machine
hangs, 0 WHEA. It spanned four days because of the incidents above plus an
evening pause on 31/08 (the machine was needed: task instance ended,
`in-progress.json` deleted by hand - a manual stop is not a hang - and the
phase-1 profile re-applied meanwhile). Relaunched 22:23 through the resume
task; the last five cores took 104 min with no reboots.

Definitive limits (2 engines x 360 s x 8 tests per margin):

```
CCD0  0:-40  1:-40  2:-35  3:-40  4:-40  5:-45  6:-50  7:-30
CCD1  8:-50  9:-35  10:-45  11:-35  12:-45  13:-50  14:-50  15:-50
```

Versus phase 1 (3 tests x 180 s): six cores stricter (2, 3, 4, 9, 10, 11),
two deeper (6, 8), five equal, and the headline: **the phase-1 profile
carried cores 7 and 11 above their real limits** (-40 applied vs limits -30
and -35). Core 7 is now the prime suspect for the unexplained 28/08 reset;
phase 1 had passed it clean at -45, a 15-count overestimate - the shorter
battery simply does not see it. Every one of the 26 positives came from
24-ZN5 (Komari); 04-P4P never failed once: on this CPU, ZN5 is the
discriminating engine and P4P adds nothing but time.

Machine hangs, all recovered without a human: core 4 at -50 (14:00, before
the auto-resume fix), core 9 at -50 and -45 (18:30, 18:38) - resume task
plus temporary auto-logon brought the campaign back in under a minute each.

The proposed profile (limit + 5) saved itself (`--yes`) and was applied on
01/09 11:10: `-35,-35,-30,-35,-35,-40,-45,-25,-45,-30,-40,-30,-40,-45,-45,-45`.
Validation restarted from zero. The +5 margin stays after discussion: each
limit sits one 5-count step above a measured failure, CO instability lives
in idle and light load where the sweep never looks, and phase 1's core-7
overestimate is the in-house proof that "passed the test" is not "stable".

Housekeeping reverted: AC standby back to 1 h, auto-logon off and the
PasswordLess build version restored, auto-resume task removed itself on
completion. Still open: the +1-on-all-cores probe reading after some
reboots, and invalidating runs that span a wall-clock gap.

Full detail: `rycolab report` over the campaign dir.

## 2026-09-01 - Validation gate for the step-5 profile (review 15/09)

Agreed criterion for calling the applied profile (limit + 5) validated. The
scale comes from the one hard data point we have: a core 10 counts over its
limit (core 7 in phase 1) took **under 48 h of normal use** to reset the
machine, so the gate covers several times that horizon and the scenarios the
sweep never exercises (light and irregular load, sleep cycles, battery).

All conditions must hold on **2026-09-15**, read from the guard's own
counters (`rycolab status`), validation running since 01/09 11:10:

- **14 calendar days** with **0 WHEA and 0 unexplained resets**. A single
  corrected WHEA sends that core one step back (UNDERVOLT 7.2 rule).
- **>= 100 h guarded** (`guardedSeconds`).
- **>= 10 sleep/resume cycles** (`resumes`).
- **Several battery sessions** (journal `power` events; power auto is on).
- **>= 2 heavy-load sessions** (PUBG or Cinebench; the pending C5 Cinebench
  comparison counts as one).
- **>= 3 overnight idles** (the classic CO failure window).

On promotion to the raw limits (limit + 0): **not planned**. The prize is
~15 mV / ~+1 % Cinebench extrapolating C4; the cost is running exactly on
margins that were clean once for 12 min. limit + 5 is the profile of record.
If the experiment is ever wanted: only after this gate passes, then the same
14-day gate again at limit + 0, and the first WHEA returns that core to +5
permanently.

## 2026-09-01 - Guard notifications: toast + own chime (roadmap item 1)

Bad news (`whea`, `reset`, `changed`, `giveup`, `apply-failed`, `error`) now
raises a Windows toast from the guard's Event funnel, one per kind per
10 min (a flapping margin fires `changed` every interval and must not spam),
gated on the new `notify` config key (default on) and on PublishState
(installed-profile guards only).

Mechanics, native at the user's request (no PowerShell child): TFM bumped
to `net9.0-windows10.0.17763.0` for the WinRT projection - the output grows
by Microsoft.Windows.SDK.NET.dll (~25 MB), OS floor Win10 1809. `Notifier`
registers the AUMID `rycolab` under HKCU\Software\Classes\AppUserModelId
(what lets an unpackaged exe own toasts), sends the toast silent and plays
`rycolab-alert.wav` through winmm `PlaySound` (ASYNC|NODEFAULT): unpackaged
toasts only accept Windows' stock ms-winsoundevent sounds, so the chime is
ours - two synthesized tones (E5 -> A5, exponential decay, 0.65 s),
generated, no licensing. `rycolab dev toast` (unelevated) tests the path.

Verified: toast + chime unelevated, elevated (the risk case: the guard runs
/RL HIGHEST) and from the installed binary; `dev plan set notify false/true`
round-trip; existing commands fine on the new TFM. The user confirmed the
toasts and the chime.

Incident while reinstalling: `off` restored the baseline and closed the
journal cleanly (restore code 0) but the guard *process* stayed alive
afterwards, holding the bin DLLs; killed by hand (safe: baseline already
written). First time seen; some non-background thread survives Run's
return. Open item, to reproduce on the next off.

## 2026-09-01 15:24 - rycolab charge full (roadmap item 2), and the lingering guard again

`charge full [--target 98]`: switches to rapid, drops `charge-full.json`
(target + the mode to restore; rapid-before defaults to conservation) and
the guard closes it - one battery check per tick, at the target it restores
the mode, deletes the marker and writes a `charge` journal event (no toast:
not bad news). Manual `charge normal|conservation|rapid` cancels a pending
one; `charge show` and the status panel display it; without a running guard
the command warns that nothing will restore the mode.

Verified end to end on the real path: 79 % in conservation ->
`charge full --target 81` (minimal real test) -> rapid confirmed, marker
on, and at 15:24:04, two ticks later, the guard's event: "battery at 81 %
-> full charge done, mode back to conservation". `charge show` clean,
marker gone. The already-at-target branch refuses politely.

The lingering guard process reproduced on this reinstall with better data:
the stop file was seen instantly, the loop exited and restored cleanly,
and the process stayed alive with 6 threads anyway - a foreground thread
survives Run's return. Workaround shipped in GuardCommand: explicit
disposes and `Environment.Exit(code)` after the ordered shutdown (journal
and SQLite are closed by then). Root cause still open; candidates
(SystemEvents' pump, a LibreHardwareMonitor thread) unconfirmed - do not
guess, measure with a thread dump if it matters later.
