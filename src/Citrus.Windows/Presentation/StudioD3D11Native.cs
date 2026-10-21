using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Citrus.Windows.Presentation;

/// <summary>Small D3D11 ABI surface; delegates are cached rather than rebuilt per frame.</summary>
internal static class StudioD3D11Native
{
    private static readonly ConcurrentDictionary<(nint, Type), Delegate> Methods = new();
    internal static T Method<T>(nint instance, int index) where T : Delegate
    {
        nint address = Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), index * IntPtr.Size);
        return (T)Methods.GetOrAdd((address, typeof(T)), static key => Marshal.GetDelegateForFunctionPointer(key.Item1, key.Item2));
    }
    internal static void Check(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }
    internal static void Release(ref nint resource) { nint value = resource; resource = 0; if (value != 0) Marshal.Release(value); }
    [DllImport("d3d11.dll", ExactSpelling = true)]
    internal static extern int D3D11CreateDevice(nint adapter, uint driverType, nint software, uint flags,
        nint levels, uint levelCount, uint sdk, out nint device, out uint level, out nint context);
    [DllImport("d3dcompiler_47.dll", ExactSpelling = true, CharSet = CharSet.Ansi)]
    internal static extern int D3DCompile(byte[] source, nuint length, string sourceName, nint defines,
        nint include, string entry, string target, uint flags1, uint flags2, out nint code, out nint errors);
    [StructLayout(LayoutKind.Sequential)] internal struct TextureDescription
    {
        internal uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality,
            Usage, BindFlags, CpuAccessFlags, MiscFlags;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct BufferDescription
    { internal uint ByteWidth, Usage, BindFlags, CpuAccessFlags, MiscFlags, StructureByteStride; }
    [StructLayout(LayoutKind.Sequential)] internal struct QueryDescription
    { internal uint Query, MiscFlags; }
    [StructLayout(LayoutKind.Sequential)] internal struct SubresourceData
    { internal nint Data; internal uint Pitch, SlicePitch; }
    [StructLayout(LayoutKind.Sequential)] internal struct MappedData
    { internal nint Data; internal uint RowPitch, DepthPitch; }
    [StructLayout(LayoutKind.Sequential)] internal struct Box
    { internal uint Left, Top, Front, Right, Bottom, Back; }
    [StructLayout(LayoutKind.Sequential)] internal struct Viewport
    { internal float X, Y, Width, Height, MinDepth, MaxDepth; }
    [StructLayout(LayoutKind.Sequential)] internal struct SamplerDescription
    {
        internal uint Filter, AddressU, AddressV, AddressW; internal float MipLodBias;
        internal uint MaxAnisotropy, Comparison; internal float BorderR, BorderG, BorderB, BorderA, MinLod, MaxLod;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int CreateTexture(nint self, ref TextureDescription description, nint data, out nint texture);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int CreateBuffer(nint self, ref BufferDescription description, nint data, out nint buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int CreateView(nint self, nint texture, nint description, out nint view);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int CreateShader(nint self, nint bytecode, nuint length, nint linkage, out nint shader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int CreateSampler(nint self, ref SamplerDescription description, out nint sampler);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int CreateQuery(nint self, ref QueryDescription description, out nint query);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate void EndQuery(nint self, nint query);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int ReadQuery(nint self, nint query, out int completed, uint dataSize, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate void FlushCommands(nint self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate void SetResources(nint self, uint slot, uint count, [In] nint[] resources);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate void SetShader(nint self, nint shader, nint instances, uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate void SetTarget(nint self, uint count, [In] nint[] targets, nint depth);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate void SetViewports(nint self, uint count, ref Viewport viewport);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate void SetTopology(nint self, uint topology);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate void SetLayout(nint self, nint layout);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate void Draw(nint self, uint count, uint first);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate void Update(nint self, nint resource, uint subresource, nint box, nint data, uint rowPitch, uint depthPitch);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate void Copy(nint self, nint target, uint targetSubresource, uint x, uint y, uint z, nint source, uint sourceSubresource, ref Box box);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate void CopyResource(nint self, nint target, nint source);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int Map(nint self, nint texture, uint subresource, uint mode, uint flags, out MappedData mapped);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate void Unmap(nint self, nint texture, uint subresource);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate nint BlobPointer(nint self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate nuint BlobLength(nint self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int DeviceStatus(nint self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate void GetTextureDescription(nint self, out TextureDescription description);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate void GetResourceDevice(nint self, out nint device);
}
