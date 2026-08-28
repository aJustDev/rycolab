# Sources

Rule: **the source wins over memory and over our own documents.** Before
asserting how an engine or a tool behaves, read the repository and cite
`file:line`. A script that worked outweighs a document that says otherwise:
investigate the discrepancy, do not "fix" the script.

## Repositories

| Project | Repository | Notes |
|---|---|---|
| Prime95 | `shafferjohn/Prime95` | 30.19 b20 |
| CoreCycler | `sp00n/corecycler` (`master`) | ships the y-cruncher binaries under `test_programs/` |
| y-cruncher | `Mysticial/y-cruncher` | `.cfg` format, test list, per-architecture binaries |
| Legion Toolkit | `BartoszCichecki/LenovoLegionToolkit` | GPL-3.0 |
| ZenStates.Core | `irusanov/ZenStates-Core` | NuGet 1.0.1 |
| LibreHardwareMonitor | `LibreHardwareMonitor/LibreHardwareMonitor` | NuGet 0.9.7-pre689 |
| InpOut32 | highrez.co.uk | `inpoutx64.dll`, port access for ZenStates |

Fetch one file without cloning:

```
gh api -H "Accept: application/vnd.github.raw" repos/<owner>/<repo>/contents/<path> > file
```

## Verified anchors

### Prime95 (30.19 b20, `master` as of 2026-08-27)

| What | Where |
|---|---|
| `-t` starts the torture with `TortureCores` workers, default `HW_NUM_CORES` | `prime95/Prime95Doc.cpp:1162-1168` (`OnUsrTorture`) |
| `NumCores` in `prime.txt` sets `HW_NUM_CORES` | `commonc.c:487` |
| Since 30.10b5 `local.txt` is merged into `prime.txt` | `commonc.c:1377`, `1409` |
| `NumWorkers` is read and **rewritten** into `prime.txt` at startup | `commonc.c:1797-1800` |
| `ErrorCheck` only affects LL/PRP work, **not** the torture | `commonc.c:1795`; uses in `commonb.c:6564, 8859, 11781`; absent in `selfTestInternal` |
| Pass text `Self-test %i%s%s passed!` | `commonb.c:7202` |
| Failure texts (`FATAL ERROR`, `ILLEGAL SUMOUT`, `Hardware failure`, `Rounding was`, `TORTURE TEST FAILED`) | `commonb.c:7194-7200` |
| Unconditional rounding check in the torture (`> 0.45` -> `STOP_FATAL_ERROR`) | `commonb.c:7713-7726` |
| Retry only after `ILLEGAL SUMOUT`, writes two lines first | `commonb.c:7695-7709` |
| Final residue compared with a precomputed table | `commonb.c:7747-7762` |
| `TortureTime=1` -> one self-test per FFT length | `commonb.c:7776` |
| FFT choice is **deterministic** (no `rand()`) | `commonb.c:8118-8135` |
| `EnableSetAffinity=0` -> no affinity set by Prime95 | `commonb.c` (`SET_PRIORITY_TORTURE`) |
| The torture never reads `Affinity=`; that is `SET_PRIORITY_NORMAL_WORK` only | `commonb.c` |
| `torture_core_num = thread_num` (worker N -> core N) | `commonb.c` (`tortureTest`) |
| Affinity options and `EnableSetAffinity` | `undoc.txt:760-790` |

### CoreCycler (`script-corecycler.ps1`, `a95b523`)

| What | Where |
|---|---|
| `prime.txt` recipe: `NumCores`, `NumThreads`, `NumWorkers`, `CoresPerTest`, `EnableSetAffinity=0`, `TortureHyperthreading=0` | `7542-7634` |
| Affinity is set by the script itself | `1793`, `10854` |
| Prime95 error detection: a new line containing `error` | `9952` |
| Stall: process CPU usage below expected, 3 checks | `9611-9614`, `9815-9859` |
| WHEA: event 19, APIC ID matched to the tested core | `427-440`, `11432` |
| `suspendPeriodically`: `SuspendThread` ~1 s per tick, forces load transitions | `3343-3475`, `198`, `6117`; `1813-1818`, `3473-3475`, `3628`; `default.config.ini:838, 884, 896` |
| Automatic Ryzen mode: `ryzen-smu-cli` writer (needs PawnIO) | `179`, `728`, `5358` |
| CO minimum -50 on Ryzen 7000+; ~3-5 mV per count | `default.config.ini:760-761` |
| `.automode` state + logon task to resume | `4347-4517`, `helpers/automode-startup-script.ps1` |
| y-cruncher pinned to a core: generated `stressTest.cfg` + affinity | `1230`, `1279`, `8418-8440` |
| Zen 5 binary: `24-ZN5 ~ Komari` (AVX-512) | `510`, `527`, `1261` |
| Light-load profile: SSE, FFT Huge, `suspendPeriodically=1` | `configs/low-load-scenario.Prime95.config.ini` |
| Example automatic mode: y-cruncher SFTv4/FFTv4/N63, 1 thread, +1 per error | `configs/Ryzen.AutomaticTestMode.Start.ini` |
| Defaults: 6 min per core, 15 s between cores, `numberOfThreads=1` | `default.config.ini:79, 136, 197` |
| Prime95 SSE recipe: `CpuSupportsAVX=0`, `AVX2=0`, `FMA3=0`, `AVX512=0` | `script-corecycler.ps1:7105-7110` |
| "Huge" = 8960K to MAX (32768K in SSE); `TortureMem=0`, `TortureTime=1` too | `default.config.ini:256`; `script-corecycler.ps1:285, 469, 7616-7617` |
| Why SSE and not AVX: light load lets the boost climb and finds errors AVX "simply cannot" | `readme.txt:132-140` |
| y-cruncher binaries in `test_programs/y-cruncher/Binaries/<mode>.exe`; `04-P4P` light, `19-ZN2`/`24-ZN5` heavy | `default.config.ini:274-326` |
| `stressTest.cfg` template (`Action StressTest`, `LogicalCores`, `TotalMemory`, `SecondsPerTest`, `StopOnError`, `Tests`) | `script-corecycler.ps1:8568-8603` |
| y-cruncher command line: `priority:-1 config <cfg>`; `pause:-2 colors:0` so it does not wait for a key | `script-corecycler.ps1:1237, 8421` |
| Eight y-cruncher tests by default: `BKT, BBP, SFTv4, SNT, SVT, FFTv4, N63, VT3`; CPU/memory load per test | `configs/default.config.ini:330-345` |
| "Use 04-P4P for low load testing and 19-ZN2 for higher/AVX2"; "It is unclear yet how Zen 5 / Ryzen 9000 CPUs will turn out" | `configs/default.config.ini:314-318` |
| 7945HX per-core reference values (-24 ... -49) | github.com/seerge/g-helper/discussions/736 |

### Legion Toolkit

| What | Where |
|---|---|
| Reads the hardware margin and discards it three lines later | `LoadFromHardwareAsync` |
| CCDs numbered from 0 in the UI | `HeaderTitle = $"CCD {currentCcdIndex}"` |
| Silent `DoNotApply` switch after 3 abnormal shutdowns | `THRESHOLD = 3` |
| Checks AC before applying | `Power.IsPowerAdapterConnectedAsync` |

### ZenStates.Core

| What | Where |
|---|---|
| Per-core PSM margin read/write | `GetPsmMarginSingleCore(uint)`, `SetPsmMarginSingleCore(uint,int)` |
| Core mask `((ccd << 8) \| core) << 20` (APU: flat index) | `Topology.CoreMask`, copied from LLT |
| SMU power table: `Cpu.RefreshPowerTable()`, raw floats in `Cpu.powerTable.Table`, version in `Cpu.smu.TableVersion` (NuGet 1.0.1; `master` has `RyzenSmu.PmTableVersion`) | `Cpu.cs:1147`, `PowerTable.cs:505` |
| `PowerTable` only interprets FCLK/MCLK/UCLK/VDDCR_SOC/CLDO_*; **nothing per core** | `PowerTable.cs:500-560` |

### 9955HX3D PM table (version `0x621202`, 613 floats) - located here, in no source

| Position | What | How it was verified (2026-08-27) |
|---|---|---|
| `301+N` | core N power (W) | equals LHM `Core #N+1 (SMU)` |
| `317+N` | core N **voltage** (V) | 1.0832 -> 1.0675 from -5 to -25 only on N=11 |
| `333+N` | core N temperature (C) | equals Tctl with one loaded core |
| `349+N` | core N frequency (GHz) | equals LHM `Core #N+1` |

Method: `watch --raw` at two margins, `scripts/pm-diff.ps1`. Repeat for any
other table version.

### External references (read 2026-08-27)

| What | Where |
|---|---|
| 9950X3D: per-CCD CO -25 (V-Cache) / -20; Curve Shaper Low/Med -30, High -25, Max -10; fMax 5550 (V-Cache) vs 5750; four loads: OCCT memory (light), y-cruncher BKT (light), y-cruncher bench (AVX), OCCT AVX (heavy) | skatterbencher.com/2025/03/11/skatterbencher-85-ryzen-9-9950x3d-overclocked-to-5900-mhz/ |
| "an unstable undervolt usually crashes at idle or light load, not under an all-core stress test"; `CLOCK_WATCHDOG_TIMEOUT`; ~1.13 V at 5.1 GHz all-core vs ~1.40 V VID in light boost | techfuelhq.com/articles/9800x3d-undervolt-guide-2026/ |
| "Stop one or two steps above your first instability, not at it" | msi.com/blog/how-to-use-curve-optimizer-to-lower-ryzen-9-9950x3d-temperatures-and-boost-performance |
| Curve Shaper: leave the Min Frequency point alone (it affects idle voltage) | SkatterBencher #85 |
| No published data for the 9955HX3D or the 16AFR10H | searches, 2026-08-27 |
