using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Rycolab.Core;

/// <summary>
/// Battery charge modes on Lenovo machines, through the same driver Legion
/// Toolkit uses (\\.\EnergyDrv, Lenovo Energy Management): normal,
/// conservation (stops at ~80 %, firmware threshold) and rapid charge, plus
/// the separate night-charge toggle. IOCTLs and write sequences copied from
/// Toolkit (Drivers.cs, BatteryFeature.cs, BatteryNightChargeFeature.cs);
/// every write is read back. Probed read-only on the reference machine
/// 2026-08-31: open ok, mode bit 0x20 (conservation), night charge
/// supported. On a machine without the driver every read is null.
/// </summary>
public sealed class LenovoEnergy : IDisposable
{
    private const uint IoctlChargeMode = 0x831020F8;
    private const uint IoctlNightCharge = 0x83102150;

    private readonly SafeFileHandle? _h;

    public bool IsAvailable => _h is not null;

    public LenovoEnergy()
    {
        var h = CreateFileW(@"\\.\EnergyDrv", 0x3 /* FILE_READ_DATA | FILE_WRITE_DATA */, 0x3, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0x80, IntPtr.Zero);
        _h = h.IsInvalid ? null : h;
    }

    public const string Normal = "normal", Conservation = "conservation", Rapid = "rapid";

    /// <summary>Current mode, decoded as Toolkit does: bit 0x20 conservation, bit 0x04 rapid, else normal.</summary>
    public string? ChargeMode()
    {
        if (Ioctl(IoctlChargeMode, 0xFF) is not { } v) return null;
        if ((v & 0x20) != 0) return Conservation;
        if ((v & 0x04) != 0) return Rapid;
        return Normal;
    }

    /// <summary>
    /// Toolkit's exact write sequences, then read back (up to 10 x 50 ms: the
    /// EC takes a moment). Also mirrors the mode into the registry key other
    /// Lenovo software reads. Returns the mode read back, or null on failure.
    /// </summary>
    public string? SetChargeMode(string mode)
    {
        uint[] codes = mode switch
        {
            Conservation => [0x08, 0x03],
            Normal => [0x05, 0x08],
            Rapid => [0x05, 0x07],
            _ => throw new ArgumentException($"unknown charge mode: {mode}"),
        };
        foreach (var c in codes)
            if (Ioctl(IoctlChargeMode, c) is null) return null;
        for (var i = 0; i < 10; i++)
        {
            if (ChargeMode() == mode) break;
            Thread.Sleep(50);
        }
        var actual = ChargeMode();
        if (actual is not null) SyncRegistry(actual);
        return actual;
    }

    /// <summary>Night charge: null = driver absent or not supported (bit 0 clear), else on/off (bit 4).</summary>
    public bool? NightCharge()
    {
        if (Ioctl(IoctlNightCharge, 0x11) is not { } v || (v & 0x1) == 0) return null;
        return (v & 0x10) != 0;
    }

    public bool? SetNightCharge(bool on)
    {
        if (Ioctl(IoctlNightCharge, on ? 0x80000012u : 0x12u) is null) return null;
        for (var i = 0; i < 10; i++)
        {
            if (NightCharge() == on) break;
            Thread.Sleep(50);
        }
        return NightCharge();
    }

    /// <summary>The key Vantage's addin reads; Toolkit keeps it in sync so the Fn shortcuts and other tools agree.</summary>
    private static void SyncRegistry(string mode)
    {
        try
        {
            var value = mode switch { Rapid => "Quick", Conservation => "Storage", _ => "Normal" };
            Registry.SetValue(@"HKEY_CURRENT_USER\Software\Lenovo\VantageService\AddinData\IdeaNotebookAddin", "BatteryChargeMode", value);
        }
        catch { /* cosmetic mirror; the driver is the truth */ }
    }

    private uint? Ioctl(uint code, uint input)
    {
        if (_h is null) return null;
        return DeviceIoControl(_h, code, ref input, 4, out var output, 4, out _, IntPtr.Zero) ? output : null;
    }

    public void Dispose() => _h?.Dispose();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle h, uint code, ref uint inBuf, int inSize, out uint outBuf, int outSize, out int returned, IntPtr overlapped);
}
