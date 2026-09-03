namespace Rycolab.Core.Gpu;

/// <summary>
/// Where the V/F curve lives in the private NvAPI buffers, per GPU family.
/// Ported from Green Curve's vf_backends.cpp (Copyright (c) 2026 aufkrawall,
/// MIT): Pascal to Blackwell share one layout; an unknown family gets the
/// same layout as a best guess. Status: 128 entries of (kHz, uV); control:
/// 128 entries whose frequency delta (kHz) sits inside a 36-byte record; a
/// 32-byte point mask at +4 in both, and the info call fills that mask with
/// the points the driver lets us edit.
/// </summary>
public sealed record VfBackend(
    string Name, bool BestGuessOnly,
    uint GetStatusId, uint GetInfoId, uint GetControlId, uint SetControlId,
    int StatusSize, uint StatusVersion, int StatusMaskOffset, int StatusNumClocksOffset, int StatusEntriesOffset, int StatusEntryStride,
    int InfoSize, uint InfoVersion, int InfoMaskOffset, int InfoNumClocksOffset,
    int ControlSize, uint ControlVersion, int ControlMaskOffset, int ControlEntriesOffset, int ControlEntryStride, int ControlDeltaOffset,
    int DefaultNumClocks)
{
    public const int Points = 128;
    /// <summary>Point 127 is the low-power point, not part of the boost tail.</summary>
    public const int LastTailPoint = 126;

    private static VfBackend Shared(string name, bool bestGuess) => new(name, bestGuess,
        0x21537AD4, 0x507B4B59, 0x23F1B133, 0x0733E009,
        0x1C28, 1, 0x04, 0x24, 0x48, 0x1C,
        0x182C, 1, 0x04, 0x14,
        0x2420, 1, 0x04, 0x44, 0x24, 0x14,
        15);

    public static VfBackend For(int architecture) => architecture switch
    {
        NvApi.Blackwell => Shared("blackwell", false),
        NvApi.Lovelace => Shared("lovelace", false),
        NvApi.Ampere => Shared("ampere", false),
        NvApi.Turing => Shared("turing", false),
        NvApi.Pascal => Shared("pascal", false),
        _ => Shared("future", true),
    };

    public int StatusEntry(int point) => StatusEntriesOffset + point * StatusEntryStride;
    public int ControlDelta(int point) => ControlEntriesOffset + point * ControlEntryStride + ControlDeltaOffset;
}
