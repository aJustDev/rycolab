using Rycolab.Core;
using Rycolab.Core.Gpu;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab gpu probe        the NVIDIA GPU, its family and its V/F curve with the offsets
/// </summary>
public static class GpuCommand
{
    public static int Run(Args args)
    {
        var sub = args.Positional.FirstOrDefault() ?? "probe";
        switch (sub)
        {
            case "probe": return Probe(args);
            default:
                Console.Error.WriteLine($"Unknown gpu command: {sub}. One of: probe.");
                return 2;
        }
    }

    private static int Probe(Args args)
    {
        using var api = new NvApi();
        if (!api.IsAvailable) { Console.Error.WriteLine($"  No NVIDIA GPU through NvAPI: {api.Unavailable}"); return 1; }
        Console.WriteLine();
        Console.WriteLine($"  {api.Name}   {api.Family} (0x{api.Architecture:X})   device {api.DeviceId:X8} subsystem {api.SubSystemId:X8}");
        VfCurve vf;
        VfPoint[] curve;
        try { vf = new VfCurve(api); curve = vf.Read(); }
        catch (Exception ex) { Console.Error.WriteLine($"  V/F curve: {ex.Message}"); return 1; }
        var data = curve.Count(p => p.HasData);
        var offsets = curve.Count(p => p.OffsetKhz != 0);
        Console.WriteLine($"  backend {vf.Backend.Name}{(vf.Backend.BestGuessOnly ? " (best guess, untested family)" : "")}   {data} points with data, {vf.EditablePoints} editable, {offsets} with an offset");
        var lockAt = VfCurve.DetectLock(curve);
        Console.WriteLine(lockAt is { } l ? $"  curve flat from point {l}: {curve[l].Mv:F1} mV, {curve[l].Mhz} MHz" : "  curve rising to the top: no lock in place");
        Console.WriteLine();
        Console.WriteLine("  point      mV    MHz  offset");
        var every = args.Has("all") ? 1 : 8;
        for (var i = 0; i < VfBackend.Points; i++)
            if (curve[i].HasData && (i % every == 0 || curve[i].OffsetKhz != 0 || i == lockAt || i == VfBackend.LastTailPoint || i == 127))
                Console.WriteLine($"  [{i,3}]  {curve[i].Mv,6:F1}  {curve[i].Mhz,5}  {(curve[i].OffsetKhz == 0 ? "" : $"{curve[i].OffsetKhz / 1000:+0;-0} MHz")}{(i == 127 ? "   (low power)" : "")}");
        Console.WriteLine();
        return 0;
    }
}
