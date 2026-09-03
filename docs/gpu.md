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
"flat from V mV up": the GPU never asks for more than V, and never runs
faster than the lock point. rycolab stores it the way Afterburner does
(`gpu-profile.json`): `LockOffsetMhz`, the offset on the lock point at
`LockMv`, and `LowOffsetMhz`, a uniform offset for the points below,
capped so none of them rises above the lock. `LockMhz` and `LockBaseMhz`
only record what the offset meant where it was derived.

Why offsets and not a clock: the driver's base curve is not one curve. On
the reference machine it sits in two states about 260 MHz apart (idle and
awake; 2355 and 2617 MHz at 950 mV minutes apart, no offset on either),
and the offsets ride on it. A lock expressed as a clock and derived
against the idle base (+533 MHz for 2655 at 875 mV) became 2895 MHz when
the card woke for 3DMark: twenty driver resets in two minutes
(`nvlddmkm` 153, "Restarting TDR occurred on GPUID:100") on 2026-09-03.
Afterburner's own offset on that point was +300 against the awake base,
which yields 2655 awake and a harmless 2422 idle. So: the offset is the
profile, the clock it yields moves with the base, and `gpu probe` shows
both.

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
rycolab gpu set --offset +300@875 [--below 300]   (or --lock 2655@875: derived against the base read now)
rycolab gpu show                           the saved profile and whether it is on the curve
rycolab gpu apply | on | off               put it on the curve (the guard keeps it) | clear a safety lock and apply | offsets to 0
```

`import` from Afterburner decodes the `VFCurve=` blob of the profile
(format 2: per point an offset, a voltage and the base clock at save time)
and takes the plateau as the lock, keeping Afterburner's offset on its
first point: on the reference machine's profile, +300 MHz at 875 mV (2655
MHz on the 2355 base Afterburner saw) with +300 MHz below.

`apply` writes, verifies and marks the profile enabled. From then on the
guard re-applies it at logon, after sleep and when the dGPU comes back on
the bus (on a Legion in iGPU-only mode the card is gone on battery); it
checks every tick that the curve still carries the profile and re-applies
at most three times an hour.

## The safety lock

A driver reset (System log: `nvlddmkm` 153 "Restarting TDR occurred",
`Display` 4101, or `nvlddmkm` 14) is the GPU's WHEA. The guard logs it
(`gpu-tdr`), resets the offsets to 0, disables the profile and writes a
safety lock into it. The same happens when an apply does not verify, when
the curve loses the profile within ten minutes of a reset, or when it keeps
losing it (three re-applies in an hour). Nothing is re-applied until you
run `rycolab gpu on`. `rycolab status` shows the lock
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
- Verified on the reference machine 2026-09-03: the 128 points read; a
  single-point write (-15 MHz on point 70: 2160 -> 2145 MHz and back); the
  Blackwell flatten (47 points in one write, the driver reports the whole
  tail at the lock point's 2617 MHz, reset to 0 clean).
- The base curve moves. Minutes apart the same point read 2355 and 2617
  MHz with no offset on it (temperature, power mode). The offsets ride on
  the base, so a lock's clock drifts with it, exactly as Afterburner's
  does; the guard therefore checks the shape (flat from the lock voltage)
  and not the MHz, which is verified once at apply and shown by `probe`.

## Sources

Green Curve (github.com/aufkrawall/green-curve, MIT): the layouts, the
batched write and the Blackwell flatten, ported into `src/Rycolab.Core/Gpu`
with attribution in `NOTICE`. LACT issue #936: the first public description
of the 128-point structures. NV-UV (closed) for the idea of a compute-error
stress and an automatic step-down, not built here.
