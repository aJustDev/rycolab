using System.Runtime.InteropServices;
using System.Text;

namespace Rycolab.Core.Gpu;

/// <summary>
/// The NVIDIA driver's NvAPI, straight from nvapi64.dll through
/// nvapi_QueryInterface: the public entry points for identity and the
/// private ones for the V/F curve. Function ids and the private structure
/// layouts are ported from Green Curve (gpu_core.h, vf_backends.cpp,
/// gpu_backend.cpp; Copyright (c) 2026 aufkrawall, MIT) and from LACT issue
/// #936; verified on the RTX 5080 Laptop (driver r616) on 2026-09-03.
/// </summary>
public sealed class NvApi : IDisposable
{
    public const uint InitializeId = 0x0150E828, UnloadId = 0xD22BDD7E, EnumPhysicalGpusId = 0xE5AC921F,
        GetFullNameId = 0xCEEE8E9F, GetPciIdentifiersId = 0x2DDFB66E, GetArchInfoId = 0xD8265D24;

    public const int Blackwell = 0x1B0, Lovelace = 0x190, Ampere = 0x170, Turing = 0x160, Pascal = 0x130;

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr QueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NoArgs();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EnumGpus(IntPtr handles, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GpuBuffer(IntPtr gpu, IntPtr buffer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GpuName(IntPtr gpu, IntPtr name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GpuIds(IntPtr gpu, out uint deviceId, out uint subSystemId, out uint revisionId, out uint extDeviceId);

    private readonly Dictionary<uint, Delegate> _cache = [];

    public bool IsAvailable { get; }
    public string? Unavailable { get; }
    public IntPtr Gpu { get; }
    public string Name { get; } = "?";
    public int Architecture { get; }
    public uint DeviceId { get; }
    public uint SubSystemId { get; }

    public NvApi()
    {
        try
        {
            var rc = Get<NoArgs>(InitializeId)();
            if (rc != 0) { Unavailable = $"NvAPI_Initialize {rc}"; return; }
            var handles = Marshal.AllocHGlobal(64 * IntPtr.Size);
            try
            {
                rc = Get<EnumGpus>(EnumPhysicalGpusId)(handles, out var count);
                if (rc != 0 || count == 0) { Unavailable = rc != 0 ? $"NvAPI_EnumPhysicalGPUs {rc}" : "no NVIDIA GPU"; return; }
                Gpu = Marshal.ReadIntPtr(handles);
            }
            finally { Marshal.FreeHGlobal(handles); }

            var name = Marshal.AllocHGlobal(64);
            try { if (Get<GpuName>(GetFullNameId)(Gpu, name) == 0) Name = Marshal.PtrToStringAnsi(name) ?? "?"; }
            finally { Marshal.FreeHGlobal(name); }
            if (Get<GpuIds>(GetPciIdentifiersId)(Gpu, out var dev, out var sub, out _, out _) == 0) { DeviceId = dev; SubSystemId = sub; }
            // NV_GPU_ARCH_INFO v2: version, architecture_id, implementation_id, revision_id.
            using var arch = new Buffer(16, 2);
            if (Call(GetArchInfoId, arch) == 0) Architecture = arch.ReadInt32(4);
            IsAvailable = true;
        }
        catch (Exception ex) { Unavailable = ex.Message; }
    }

    public string Family => Architecture switch
    {
        Blackwell => "Blackwell", Lovelace => "Lovelace", Ampere => "Ampere", Turing => "Turing", Pascal => "Pascal",
        0 => "unknown", _ => $"0x{Architecture:X}",
    };

    /// <summary>A GPU function taking the handle and a versioned buffer; the NvAPI status (0 ok).</summary>
    public int Call(uint id, Buffer buffer) => Get<GpuBuffer>(id)(Gpu, buffer.Ptr);

    private T Get<T>(uint id) where T : Delegate
    {
        if (_cache.TryGetValue(id, out var d)) return (T)d;
        var p = QueryInterface(id);
        if (p == IntPtr.Zero) throw new InvalidOperationException($"NvAPI has no interface 0x{id:X8} on this driver");
        var f = Marshal.GetDelegateForFunctionPointer<T>(p);
        _cache[id] = f;
        return f;
    }

    public void Dispose()
    {
        try { if (IsAvailable) Get<NoArgs>(UnloadId)(); } catch { /* the driver may already be gone */ }
    }

    /// <summary>A zeroed native buffer with the NvAPI version word (version &lt;&lt; 16 | size) at offset 0.</summary>
    public sealed class Buffer : IDisposable
    {
        public IntPtr Ptr { get; }
        public int Size { get; }

        public Buffer(int size, uint version)
        {
            Size = size;
            Ptr = Marshal.AllocHGlobal(size);
            for (var i = 0; i < size; i++) Marshal.WriteByte(Ptr, i, 0);
            Marshal.WriteInt32(Ptr, 0, (int)((version << 16) | (uint)size));
        }

        public int ReadInt32(int offset) => Marshal.ReadInt32(Ptr, offset);
        public byte ReadByte(int offset) => Marshal.ReadByte(Ptr, offset);
        public void WriteInt32(int offset, int value) => Marshal.WriteInt32(Ptr, offset, value);
        public void WriteByte(int offset, byte value) => Marshal.WriteByte(Ptr, offset, value);
        public void Fill(int offset, int count, byte value) { for (var i = 0; i < count; i++) Marshal.WriteByte(Ptr, offset + i, value); }
        public byte[] ReadBytes(int offset, int count) { var b = new byte[count]; Marshal.Copy(Ptr + offset, b, 0, count); return b; }
        public void WriteBytes(int offset, byte[] bytes) => Marshal.Copy(bytes, 0, Ptr + offset, bytes.Length);
        public void Dispose() => Marshal.FreeHGlobal(Ptr);
    }
}
