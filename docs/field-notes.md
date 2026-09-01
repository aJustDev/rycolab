# Field notes

Measured on the reference machine (Ryzen 9 9955HX3D, Legion Pro 7 16AFR10H),
not assumed. The raw numbers are in `lab-notebook.md`; the Lenovo-only ones
(fans, battery, dGPU) in `legion.md`.

- LibreHardwareMonitor's `Core #N VID` **is not a per-core voltage** on the
  9955HX3D: all 16 report the same value and move together. Discarded.
  Per-core voltage comes from the SMU power table (`PmTable.cs`).
- LibreHardwareMonitor's `Package` power (a RAPL energy-counter delta) returns
  intermittent garbage on the 9955HX3D: 150-270 W at ~1 % CPU. Package power
  comes from the SMU power table (located by `dev calibrate`); the guard shows
  nothing rather than that on a table version without an index.
- What does discriminate per core in LHM is `Core #N (SMU)` (power) and the
  effective clock.
- CCDs are numbered **from 0**, like Legion Toolkit and the SMU mask. HWiNFO
  and LibreHardwareMonitor number from 1: our CCD0 is their `CCD1 (Tdie)`. The
  translation lives in `Topology.CcdTempSensor` and nowhere else.
- Each physical core owns two logical processors: core N is logical 2N (SMT
  on, no disabled cores; other topologies are not handled yet).
- On this laptop every sustained torture is capped at ~14 W per core; only
  y-cruncher `04-P4P` (SSE3) reaches fMax (5.45 GHz). `24-ZN5` (AVX-512) is
  the engine that finds the errors: the definitive campaign's 26 positives
  all came from it and `04-P4P` never failed. See `how-it-works.md`.
- The margin acts physically: from -5 to -25 on one core, +160 MHz and
  -15.7 mV at constant 14 W; at fMax the voltage falls ~3.8 mV per count and
  the test speed stays constant (no clock stretching).
- A sleep of any kind, and a reboot, return every core to the BIOS baseline.
  The guard re-applies after resume; the sweep invalidates a run that lost
  time or margin.
- A too-deep margin cold-reboots the machine with no WHEA and no journal
  line: the only trace is Kernel-Power 41 at the next boot. The sweep treats
  a run left in progress across a reboot as a positive (hang); the guard
  counts the reboot as a `reset`.
- Legion Toolkit, if it has `amd_overclocking.json`, re-applies its own
  per-core Curve Optimizer on mode change, AC events, resume and start. The
  guard re-applies the profile within one interval and gives up after three
  times in an hour; close one of the two.
