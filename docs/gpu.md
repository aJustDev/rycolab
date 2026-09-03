# The NVIDIA V/F curve

`rycolab gpu` applies an undervolt curve to an NVIDIA GPU and keeps it
applied, the way `rycolab on` keeps the Curve Optimizer profile: through the
guard, verified by readback, recorded in the database, and withdrawn on the
GPU's own kind of positive (a driver reset). What it does not do: search for
the limit. The curve is yours, from MSI Afterburner, from Green Curve or by
hand; rycolab measures what it does and keeps it in place.

## What a curve is here

The driver exposes a voltage/frequency curve of 128 points (on the
reference machine's RTX 5080 Laptop: 450 mV / 180 MHz at the bottom, 1240 mV
/ 2827 MHz at the top; point 127 is the low-power point). An undervolt is
"flat at F MHz from V mV up": the GPU never asks for more than V to run at
F, and never runs faster than F. rycolab stores exactly that
(`gpu-profile.json`: `LockMhz`, `LockMv`, and `LowOffsetMhz`, a uniform
offset for the points below the lock, capped so none of them rises above it).

Applying it means writing a frequency offset per point through the private
NvAPI calls the overclocking tools use. On Blackwell (RTX 50) the driver
ignores individual offsets on the points above the lock, so every tail
point gets the driver's minimum offset (-1000 MHz) and the lock point alone
sets the ceiling: the mechanism Green Curve found and rycolab ports. Every
write is read back; a curve that does not land is reset to the driver's
own (`FlattenTargets`, `Apply`, `IsFlatAt` in `VfCurve.cs`).

## Commands

```
rycolab gpu probe [--all]                  the GPU, its family, the curve and the offsets on it
rycolab gpu import <file> [--profile 1]    an Afterburner profile (Profiles\VEN_10DE...cfg) or a Green Curve config.ini
rycolab gpu set --lock 2655@875 [--below 300]
rycolab gpu show                           the saved profile and whether it is on the curve
rycolab gpu apply | on | off               put it on the curve (the guard keeps it) | clear a safety lock and apply | offsets to 0
```

`import` from Afterburner decodes the `VFCurve=` blob of the profile
(format 2: per point an offset, a voltage and the base clock at save time)
and takes the plateau as the lock: on the reference machine's profile,
2655 MHz from 875 mV with +300 MHz below. The base clocks Afterburner saved
drift with temperature; rycolab computes its offsets against the live curve
when it applies, so the lock lands where the profile says regardless.

`apply` writes, verifies and marks the profile enabled. From then on the
guard re-applies it at logon, after sleep and when the dGPU comes back on
the bus (on a Legion in iGPU-only mode the card is gone on battery); it
checks every tick that the curve still carries the profile and re-applies
at most three times an hour.

## The safety lock

A driver reset (System log: `Display` 4101, or `nvlddmkm` 14) is the GPU's
WHEA. The guard logs it (`gpu-tdr`), resets the offsets to 0, disables the
profile and writes a safety lock into it. The same happens when an apply
does not verify, or when the curve keeps losing the profile. Nothing is
re-applied until you run `rycolab gpu on`. `rycolab status` shows the lock
on the `gpu curve` row; `rycolab report --power` counts the resets.

## What the guard records

With the card on the bus every tick carries the GPU clock, memory clock,
power, temperature and utilisation (NVML, opened only while the card is
present because opening it wakes a sleeping dGPU), whether the profile was
on the curve, and the driver resets so far. `rycolab report --power` turns
that into hours on the bus, MHz p50 / p95, W, temperature and the hours the
profile was on; `rycolab db sql` has the rest.

## Limits and the honest bits

- No search. The CPU harness finds the limit with a checksum test; there is
  no open equivalent for a GPU on Windows, and a GPU undervolt fails as a
  crash an hour into a game. The signals rycolab has are the driver reset
  and, when you run one, an A-B benchmark with `rycolab dev log`.
- Blackwell and Lovelace verified by the layouts' authors; the reference
  machine is a Blackwell laptop. Other families use the same layout as
  Green Curve does; an unknown family is a best guess and `probe` says so.
- The per-point writes and the NVML clock offsets share state in the
  driver; rycolab writes only through NvAPI and reads only through NVML.
  Running Afterburner or Green Curve at the same time is a fight over the
  same curve, like Legion Toolkit over the CO margins.
- Read on the reference machine 2026-09-03 (128 points), a single-point
  write verified (-15 MHz on point 70: 2160 -> 2145 MHz and back).

## Sources

Green Curve (github.com/aufkrawall/green-curve, MIT): the layouts, the
batched write and the Blackwell flatten, ported into `src/Rycolab.Core/Gpu`
with attribution in `NOTICE`. LACT issue #936: the first public description
of the 128-point structures. NV-UV (closed) for the idea of a compute-error
stress and an automatic step-down, not built here.
