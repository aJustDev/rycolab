# Lenovo Legion extras

`rycolab legion` groups what only exists on a Lenovo Legion machine: the EC
fan switch, the battery profile and the charge modes. None of it is needed
for Curve Optimizer; it lives here because the reference machine is a Legion
Pro 7 and every knob was measured on it (`lab-notebook.md`). All of it talks
to the same interfaces Legion Toolkit uses (WMI `LENOVO_GAMEZONE_DATA` /
`LENOVO_OTHER_METHOD`, the `\\.\EnergyDrv` driver) and reads every write
back. On a machine without them every command says so and does nothing.

```
rycolab legion power show|battery|ac|restore|auto on|off   Lenovo Legion battery profile (see below)
rycolab legion charge show|normal|conservation|rapid|night on|off   Lenovo battery charge mode through the Energy
                              driver (\\.\EnergyDrv, what Legion Toolkit's battery section talks to): conservation
                              stops at ~80 % (firmware threshold), rapid charges fastest; night charge is a separate
                              slow-overnight toggle. Every write is read back; the Vantage registry key is kept in sync
rycolab legion charge full [--target 98]   one-shot full charge: rapid now, and the running guard restores the
                              previous mode when the battery reaches the target (a manual mode change cancels it)
rycolab legion fan show|on|off|auto [--on 85] [--off 80] [--hold 3]   the EC "fan full speed" switch, by hand or
                              driven by the EC CPU temperature; selects the custom power mode itself, restores it on exit
```

## Fans on Lenovo Legion

The EC drives the fans from a 10-level table (CPU fan 1700...5200 RPM on the
reference machine) and ramps at about 60 RPM/s whatever the curve says, so
under a sustained load the CPU sits at its thermal limit for a minute before
the fan reaches its top level. The "maximum fan speed" switch in Legion
Toolkit (`FanFullSpeed`, WMI `LENOVO_OTHER_METHOD` id `0x04020000`) goes past
the table (5700 / 5700 / 7200 RPM) and ramps in seconds; measured under
Cinebench R23 at the same 145 W it gave -3 C and +107 MHz sustained. The EC
only honours the switch in the custom power mode (smart fan mode 255), so
`legion fan on` and `legion fan auto` select it themselves (the same WMI call Legion
Toolkit makes), print the CPU power limits the custom slot runs with (never
written by rycolab), and `legion fan off` or the end of `auto` restore the mode
found. `rycolab legion fan show` prints mode, limits, switch, fans and EC temperatures. `rycolab legion fan auto` (elevated)
turns the switch on after `--hold` seconds at or above `--on` C of EC CPU
temperature, off below `--off`, and off again when it exits. Legion Toolkit, if
running, re-applies its own preset (mode, switch and, if
`amd_overclocking.json` exists, its per-core Curve Optimizer) on mode
change, AC events, resume and start; the guard restores the profile within
one interval, `auto` reports the mode change. The EC's fan table is untouched.

## Battery profile on Lenovo Legion

`rycolab legion power battery` (elevated) changes, in this order, what makes the
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
change (`power-prev.json`) and `rycolab legion power ac` puts it back; `legion power
restore` writes every snapshot value even if it looks untouched. `legion power
show` prints line, discharge W, charge, GPU mode and dGPU presence, panel,
brightness, the Windows slider per line and the DC values. The Windows
power-mode slider is not written: Windows keeps one position per line and
switches it itself.

`rycolab legion power auto on` makes the guard apply the battery profile 15 s after
the AC line drops and restore it 15 s after it is back (the debounce ignores
the line blips of a few seconds that the reference machine's adapter
produces). One change per knob, never a burst. The guard must already be
running (`rycolab on`, which needs AC to start; it keeps running on
battery). Each knob was measured on the reference machine before going in
(see the lab notebook); a knob that does not move the discharge rate is not
in the default profile.


## The dGPU and the switch to iGPU-only

Probes wake the dGPU: nvidia-smi, NVML and `Win32_VideoController` all reset
its idle timer, and a card that is kept awake never leaves the bus after the
switch to iGPU-only. rycolab checks presence through `Win32_PnPEntity` only.
If the card still does not leave, the EC is re-notified (NotifyDGPUStatus,
Legion Toolkit's EnsureDGPUEjected) every guard tick, and after six minutes
the node is disabled as a last resort, with a toast. A DISABLED node is not
success: the silicon keeps ~20 W with no driver managing it. Success is the
node leaving the bus. `legion power ac` re-enables the node before switching
back. The full story is in `lab-notebook.md` (2026-09-01).
