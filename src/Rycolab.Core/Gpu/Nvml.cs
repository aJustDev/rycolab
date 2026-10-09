using System.Runtime.InteropServices;

namespace Rycolab.Core.Gpu;

/// <summary>
/// NVML (nvml.dll, shipped with the driver) for telemetry only: clocks,
/// power, temperature, utilisation, memory in use. Opening it wakes a sleeping dGPU, so the
/// guard opens it only while the card is on the bus and closes it when the
/// card leaves. Writes (clock lock, offsets) stay out: they share state with
/// the NvAPI curve writes and clobber them (Green Curve, LACT #936).
/// </summary>
public sealed class Nvml : IDisposable
{
    [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")] private static extern int Init();
    [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")] private static extern int Shutdown();
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")] private static extern int GetHandle(uint index, out IntPtr device);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetClockInfo")] private static extern int GetClock(IntPtr device, uint type, out uint mhz);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage")] private static extern int GetPower(IntPtr device, out uint mw);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")] private static extern int GetTemperature(IntPtr device, uint sensor, out uint c);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")] private static extern int GetUtilization(IntPtr device, out Utilization u);
    [StructLayout(LayoutKind.Sequential)] private struct Utilization { public uint Gpu, Memory; }
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo_v2")] private static extern int GetMemory(IntPtr device, ref Memory m);
    [StructLayout(LayoutKind.Sequential)] private struct Memory { public uint Version; public ulong Total, Reserved, Free, Used; }
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTotalEnergyConsumption")] private static extern int GetEnergy(IntPtr device, out ulong mj);

    private const uint ClockGraphics = 0, ClockMemory = 2;
    // The bottom of the curve and the memory's lowest clock: the card's deepest idle.
    private const int FloorMhz = 180, FloorMemMhz = 405;
    // NVML_STRUCT_VERSION(Memory, 2): the size of the struct and the version. The first version counts the driver's own reservation as used (300 MiB on an idle 5080).
    private const uint MemoryV2 = 2u << 24 | 40;

    private readonly IntPtr _device;
    private readonly bool _open;

    public bool IsAvailable => _open;
    public string? Unavailable { get; }

    public Nvml()
    {
        try
        {
            var rc = Init();
            if (rc != 0) { Unavailable = $"nvmlInit {rc}"; return; }
            rc = GetHandle(0, out _device);
            if (rc != 0) { Unavailable = $"nvmlDeviceGetHandleByIndex {rc}"; Shutdown(); return; }
            _open = true;
        }
        catch (Exception ex) { Unavailable = ex.Message; }
    }

    /// <summary>
    /// <paramref name="VramMb"/>: the card's memory in use by every process, MiB, as nvidia-smi shows it.
    /// <paramref name="EnergyMj"/>: the driver's energy counter since it loaded, mJ; two reads give the mean power between them.
    /// </summary>
    public readonly record struct Sample(int? Mhz, int? MemMhz, double? Watts, int? TempC, int? Util, int? VramMb, long? EnergyMj = null);

    /// <summary>Null when the handle went stale: after a driver reset NVML answers success with garbage (2332033 MHz, 2336538 W on 2026-09-03); the caller reopens.</summary>
    public Sample? Read()
    {
        if (!_open) return null;
        var mem = new Memory { Version = MemoryV2 };
        return Checked(new Sample(
            GetClock(_device, ClockGraphics, out var g) == 0 ? (int)g : null,
            GetClock(_device, ClockMemory, out var m) == 0 ? (int)m : null,
            GetPower(_device, out var mw) == 0 ? mw / 1000.0 : null,
            GetTemperature(_device, 0, out var c) == 0 ? (int)c : null,
            GetUtilization(_device, out var u) == 0 ? (int)u.Gpu : null,
            GetMemory(_device, ref mem) == 0 ? (int)(mem.Used >> 20) : null,
            GetEnergy(_device, out var mj) == 0 ? (long)mj : null));
    }

    /// <summary>
    /// What a raw read is worth. At the floor (180 MHz with the memory at 405) the power reading is not one:
    /// 307 W on 2026-10-05, a number that climbed from 16 to 842 W through 2026-10-07; it is dropped. One
    /// reading out of range is dropped alone, the rest of the sample stands; two or more are the stale handle.
    /// </summary>
    internal static Sample? Checked(Sample s)
    {
        if (s is { Mhz: FloorMhz, MemMhz: FloorMemMhz }) s = s with { Watts = null };
        var bad = 0;
        if (s.Mhz is not (null or (>= 0 and < 10000))) { s = s with { Mhz = null }; bad++; }
        if (s.MemMhz is not (null or (>= 0 and < 40000))) { s = s with { MemMhz = null }; bad++; }
        if (s.Watts is not (null or (>= 0 and < 1000))) { s = s with { Watts = null }; bad++; }
        if (s.TempC is not (null or (>= 0 and < 150))) { s = s with { TempC = null }; bad++; }
        if (s.Util is not (null or (>= 0 and <= 100))) { s = s with { Util = null }; bad++; }
        if (s.VramMb is not (null or (>= 0 and < 200000))) { s = s with { VramMb = null }; bad++; }
        return bad > 1 ? null : s;
    }

    public void Dispose()
    {
        if (_open) { try { Shutdown(); } catch { /* driver gone */ } }
    }
}
