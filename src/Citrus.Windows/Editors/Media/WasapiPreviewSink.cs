using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Citrus.Conversion.Editing.Media;
using Citrus.Core.Editing;

namespace Citrus.Windows.Editors.Media;

public sealed class AudioDeviceUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Owned shared-mode WASAPI renderer. COM creation/use/release stays on one MTA thread.
/// Documented IAudioClient AUTOCONVERTPCM/SRC_DEFAULT_QUALITY converts PCM16 to the endpoint mix format.
/// Clock comes from IAudioClock.GetPosition/GetFrequency, never from submitted bytes or UI ticks.
/// See learn.microsoft.com/windows/win32/coreaudio/rendering-a-stream and iaudioclock-getposition.</summary>
public sealed class WasapiPreviewSink : IPreviewAudioSink
{
    private readonly object sync = new();
    private CancellationTokenSource? cancellation;
    private Thread? worker;
    private Task completion = Task.CompletedTask;
    private long clockTicks;
    private long clockFrequency = 1;
    private bool disposed;
    public RationalTime Position
    {
        get
        {
            // Device clocks can have frequencies exceeding RationalTime's denominator. Convert to
            // 100ns units using integer arithmetic; truncation is less than one 100ns unit.
            var n = (System.Numerics.BigInteger)Interlocked.Read(ref clockTicks) * 10_000_000;
            return new(checked((long)(n / Interlocked.Read(ref clockFrequency))), 10_000_000);
        }
    }
    public Task Completion { get { lock (sync) return completion; } }
    public Task StartAsync(AudioPcmInfo pcm, CancellationToken ct)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (worker is not null) throw new InvalidOperationException("A WASAPI sink plays one owned audition.");
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WASAPI requires Windows.");
            if (pcm.SampleRate is <8000 or >192000 || pcm.Channels is <1 or >2 || pcm.SampleFrames <= 0)
                throw new NotSupportedException("Native preview currently supports mono/stereo PCM16 at 8–192 kHz.");
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            completion = finished.Task;
            worker = new Thread(() => Render(pcm, cancellation.Token, started, finished)) { IsBackground = true, Name = "Citrus WASAPI preview" };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
            return started.Task;
        }
    }
    public void Stop()
    {
        Thread? owned;
        lock (sync) { cancellation?.Cancel(); owned = worker; }
        // No UI callbacks or dispatcher waits occur on this worker. Stop returns only after native release.
        if (owned is not null && owned != Thread.CurrentThread) owned.Join();
    }
    public async ValueTask DisposeAsync()
    {
        lock (sync) { if (disposed) return; disposed = true; }
        Stop();
        try { await Completion.ConfigureAwait(false); } catch (Exception) { /* Completion retains the device/cancellation failure for the transport. */ }
        finally { cancellation?.Dispose(); }
    }
    private void Render(AudioPcmInfo pcm, CancellationToken ct, TaskCompletionSource started, TaskCompletionSource finished)
    {
        IMMDeviceEnumerator? enumerator = null; IMMDevice? device = null; IAudioClient? client = null;
        IAudioRenderClient? render = null; IAudioClock? clock = null;
        bool initializedCom = false;
        Exception? failure = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            Check(CoInitializeEx(IntPtr.Zero, 0)); initializedCom = true;
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            int hr = enumerator.GetDefaultAudioEndpoint(0, 1, out device);
            if (hr == unchecked((int)0x80070490)) throw new AudioDeviceUnavailableException("Windows has no default audio render endpoint.");
            Check(hr);
            var clientId = typeof(IAudioClient).GUID;
            Check(device.Activate(ref clientId, 23, IntPtr.Zero, out var activated)); client = (IAudioClient)activated;
            var format = new WaveFormat { FormatTag = 1, Channels = checked((ushort)pcm.Channels), SamplesPerSec = (uint)pcm.SampleRate,
                BitsPerSample = 16, BlockAlign = checked((ushort)(pcm.Channels * 2)), AverageBytesPerSec = checked((uint)(pcm.SampleRate * pcm.Channels * 2)), ExtraSize = 0 };
            // Shared render, 100ms requested buffer, no exclusive device ownership and no system codec path.
            Check(client.Initialize(0, 0x88000000, 1_000_000, 0, ref format, IntPtr.Zero));
            Check(client.GetBufferSize(out uint capacity));
            if (capacity == 0 || capacity > (uint)pcm.SampleRate * 2) throw new IOException("Unexpected WASAPI buffer capacity.");
            var renderId = typeof(IAudioRenderClient).GUID;
            Check(client.GetService(ref renderId, out var renderer)); render = (IAudioRenderClient)renderer;
            var clockId = typeof(IAudioClock).GUID;
            Check(client.GetService(ref clockId, out var audioClock)); clock = (IAudioClock)audioClock;
            Check(clock.GetFrequency(out ulong frequency));
            if (frequency == 0 || frequency > long.MaxValue) throw new IOException("Invalid WASAPI device clock frequency.");
            Interlocked.Exchange(ref clockFrequency, checked((long)frequency));
            using var file = new FileStream(pcm.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
            long expected = checked(pcm.SampleFrames * format.BlockAlign);
            if (file.Length != expected) throw new InvalidDataException("PCM payload does not match its exact sample count.");
            byte[] buffer = new byte[checked((int)capacity * format.BlockAlign)];
            long submitted = 0;
            Fill(capacity);
            Check(clock.GetPosition(out ulong origin, out _));
            Check(client.Start()); started.TrySetResult();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                Check(client.GetCurrentPadding(out uint padding));
                if (padding > capacity) throw new IOException("Invalid WASAPI render padding.");
                Check(clock.GetPosition(out ulong current, out _));
                if (current < origin || current - origin > long.MaxValue) throw new IOException("WASAPI clock moved backwards.");
                Interlocked.Exchange(ref clockTicks, (long)(current - origin));
                if (submitted == pcm.SampleFrames && padding == 0) break;
                Fill(capacity - padding);
                if (ct.WaitHandle.WaitOne(5)) ct.ThrowIfCancellationRequested();
            }
            void Fill(uint available)
            {
                uint count = (uint)Math.Min(available, pcm.SampleFrames - submitted);
                if (count == 0) return;
                ct.ThrowIfCancellationRequested();
                int bytes = checked((int)count * format.BlockAlign);
                file.ReadExactly(buffer.AsSpan(0, bytes));
                Check(render.GetBuffer(count, out var target));
                try { Marshal.Copy(buffer, 0, target, bytes); }
                finally { Check(render.ReleaseBuffer(count, 0)); }
                submitted += count;
            }
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            // ReleaseBuffer is paired in Fill even on copy failure. Stop/Reset precede every RCW release.
            try { if (client is not null) { _ = client.Stop(); _ = client.Reset(); } }
            catch (Exception ex) { failure ??= ex; }
            foreach (object? owned in new object?[] { clock, render, client, device, enumerator })
                try { Release(owned); } catch (Exception ex) { failure ??= ex; }
            if (initializedCom) CoUninitialize();
            if (failure is OperationCanceledException)
            { started.TrySetCanceled(ct); finished.TrySetCanceled(ct); }
            else if (failure is not null)
            { started.TrySetException(failure); finished.TrySetException(failure); }
            else { started.TrySetResult(); finished.TrySetResult(); }
        }
    }
    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat { public ushort FormatTag, Channels; public uint SamplesPerSec, AverageBytesPerSec; public ushort BlockAlign, BitsPerSample, ExtraSize; }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr activation, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        [PreserveSig] int OpenPropertyStore(uint access, out IntPtr properties);
        [PreserveSig] int GetId(out IntPtr id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int mode, uint flags, long duration, long periodicity, ref WaveFormat format, IntPtr session);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int mode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }
    [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioRenderClient
    {
        [PreserveSig] int GetBuffer(uint frames, out IntPtr data);
        [PreserveSig] int ReleaseBuffer(uint frames, uint flags);
    }
    [ComImport, Guid("CD63314F-3FBA-4A1B-812C-EF96358728E7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClock
    {
        [PreserveSig] int GetFrequency(out ulong frequency);
        [PreserveSig] int GetPosition(out ulong position, out ulong qpcPosition);
        [PreserveSig] int GetCharacteristics(out uint characteristics);
    }
}
