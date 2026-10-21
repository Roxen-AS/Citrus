using System.Runtime.InteropServices;

namespace Citrus.Windows;

/// <summary>Owned PCM16 waveOut ring. The driver signals an event; it never calls managed code.
/// Event/WHDR_DONE contract: learn.microsoft.com/windows/win32/api/mmeapi/nf-mmeapi-waveoutopen.
/// Reset returns queued blocks before unprepare/free: nf-mmeapi-waveoutreset / nf-mmeapi-waveoutunprepareheader.</summary>
internal sealed class WaveOutFeedbackOutput : WaveOutFeedbackPlayer.IOutput
{
    private readonly IApi api;
    private readonly NativeBlock?[] buffers = new NativeBlock[3];
    private IntPtr handle;
    private bool disposed;
    public int BufferCount => buffers.Length;
    public int SamplesPerBuffer => 480; // 240 stereo frames = 5 ms at 48 kHz.
    public ManualResetEvent BufferCompleted { get; } = new(false);

    public WaveOutFeedbackOutput() : this(new NativeApi()) { }
    internal WaveOutFeedbackOutput(IApi api)
    {
        this.api = api;
        try
        {
            var format = new WaveFormat { FormatTag = 1, Channels = 2, SamplesPerSec = 48000,
                AverageBytesPerSec = 192000, BlockAlign = 4, BitsPerSample = 16 };
            Check(api.Open(out handle, ref format, BufferCompleted.SafeWaitHandle.DangerousGetHandle()));
            for (int i = 0; i < buffers.Length; i++)
            {
                var block = new NativeBlock(SamplesPerBuffer);
                buffers[i] = block;
                Check(api.Prepare(handle, block.Header));
                block.Prepared = true;
            }
            BufferCompleted.Reset();
        }
        catch { Dispose(); throw; }
    }

    public bool IsAvailable(int index)
    {
        NativeBlock block = buffers[index]!;
        return !block.Submitted || (Marshal.PtrToStructure<WaveHeader>(block.Header).Flags & 1) != 0;
    }

    public void Submit(int index, short[] samples)
    {
        NativeBlock block = buffers[index]!;
        Marshal.Copy(samples, 0, block.Data, SamplesPerBuffer);
        Check(api.Write(handle, block.Header));
        block.Submitted = true;
    }

    public void Reset()
    {
        if (disposed || handle == IntPtr.Zero) return;
        // waveOutReset returns all pending blocks with WHDR_DONE before returning.
        Check(api.Reset(handle));
        foreach (var block in buffers) if (block is not null) block.Submitted = false;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (handle != IntPtr.Zero) _ = api.Reset(handle);
        bool released = true;
        foreach (var block in buffers)
        {
            if (block is null) continue;
            // A failing/removed driver must never retain a pointer to freed memory.
            // In particular, WAVERR_STILLPLAYING requires keeping that block alive.
            if (block.Prepared && api.Unprepare(handle, block.Header) != 0) { released = false; continue; }
            block.Dispose();
        }
        bool closed = handle == IntPtr.Zero || api.Close(handle) == 0;
        handle = IntPtr.Zero;
        if (released && closed) BufferCompleted.Dispose();
        else BufferCompleted.SafeWaitHandle.SetHandleAsInvalid(); // Driver still owns it; do not recycle its event handle.
    }

    private static void Check(uint result)
    {
        if (result != 0) throw new InvalidOperationException($"Optional feedback output unavailable ({result}).");
    }

    private sealed class NativeBlock : IDisposable
    {
        public IntPtr Data { get; private set; }
        public IntPtr Header { get; private set; }
        public bool Prepared, Submitted;
        public NativeBlock(int samples)
        {
            try
            {
                Data = Marshal.AllocHGlobal(samples * sizeof(short));
                Header = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
                Marshal.StructureToPtr(new WaveHeader { Data = Data, BufferLength = (uint)(samples * sizeof(short)) }, Header, false);
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (Header != IntPtr.Zero) { Marshal.FreeHGlobal(Header); Header = IntPtr.Zero; }
            if (Data != IntPtr.Zero) { Marshal.FreeHGlobal(Data); Data = IntPtr.Zero; }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    internal struct WaveFormat
    {
        public ushort FormatTag, Channels;
        public uint SamplesPerSec, AverageBytesPerSec;
        public ushort BlockAlign, BitsPerSample, ExtraSize;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct WaveHeader
    {
        public IntPtr Data;
        public uint BufferLength, BytesRecorded;
        public UIntPtr User;
        public uint Flags, Loops;
        public IntPtr Next;
        public UIntPtr Reserved;
    }

    internal interface IApi
    {
        uint Open(out IntPtr handle, ref WaveFormat format, IntPtr signal);
        uint Prepare(IntPtr handle, IntPtr header);
        uint Write(IntPtr handle, IntPtr header);
        uint Reset(IntPtr handle);
        uint Unprepare(IntPtr handle, IntPtr header);
        uint Close(IntPtr handle);
    }
    private sealed class NativeApi : IApi
    {
        private static readonly uint HeaderSize = (uint)Marshal.SizeOf<WaveHeader>();
        public uint Open(out IntPtr handle, ref WaveFormat format, IntPtr signal) => waveOutOpen(out handle, uint.MaxValue, ref format, signal, IntPtr.Zero, 0x50000);
        public uint Prepare(IntPtr handle, IntPtr header) => waveOutPrepareHeader(handle, header, HeaderSize);
        public uint Write(IntPtr handle, IntPtr header) => waveOutWrite(handle, header, HeaderSize);
        public uint Reset(IntPtr handle) => waveOutReset(handle);
        public uint Unprepare(IntPtr handle, IntPtr header) => waveOutUnprepareHeader(handle, header, HeaderSize);
        public uint Close(IntPtr handle) => waveOutClose(handle);
        [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr handle, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(IntPtr handle, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutWrite(IntPtr handle, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutReset(IntPtr handle);
        [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(IntPtr handle, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutClose(IntPtr handle);
    }
}
