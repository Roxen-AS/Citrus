using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Citrus.Windows.Presentation;

/// <summary>Capture-free prepared storage. A leased allocation has exactly one capture worker.</summary>
internal sealed class BootstrapCaptureScratch : IDisposable
{
    private static readonly byte[] Zeroes = new byte[64 * 1024];
    private nint memory, bitmap, previous, bits;
    internal int Width { get; }
    internal int Height { get; }
    internal long AllocationBytes { get; }
    internal nint Bits => bits;
    internal int ManagedCopyCount { get; private set; }
    internal BootstrapCaptureTiming LastTiming { get; set; }

    internal BootstrapCaptureScratch(int width, int height)
    {
        if (width is <= 0 or > 8192 || height is <= 0 or > 8192 || (long)width * height > 16_777_216)
            throw new ArgumentOutOfRangeException(nameof(width), "Capture storage must remain within the bounded ROI.");
        Width = width; Height = height; AllocationBytes = checked((long)width * height * 4);
        nint display = 0;
        try
        {
            // A non-null compatible DC avoids tying the retained memory DC's
            // lifetime to the temporary thread-pool thread that prepares it.
            // Creating DCs/DIBs reads no desktop pixels.
            display = CreateDC("DISPLAY", null, null, 0);
            if (display == 0) throw new Win32Exception();
            memory = CreateCompatibleDC(display);
            if (memory == 0) throw new Win32Exception();
            var information = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height,
                Planes = 1, BitCount = 32, SizeImage = checked((uint)AllocationBytes)
            };
            bitmap = CreateDIBSection(display, ref information, 0, out bits, 0, 0);
            if (bitmap == 0 || bits == 0) throw new Win32Exception();
            previous = SelectObject(memory, bitmap);
            if (previous == 0 || previous == new nint(-1)) throw new Win32Exception();
            Clear();
        }
        catch { Dispose(); throw; }
        finally { if (display != 0) DeleteDC(display); }
    }

    internal bool Blit(nint screen, GlassCaptureTile tile)
    {
        ObjectDisposedException.ThrowIf(bits == 0, this);
        return BitBlt(memory, tile.DestinationLeft, tile.DestinationTop, tile.Width, tile.Height, screen,
            checked(tile.Monitor.Left + tile.SourceLeft), checked(tile.Monitor.Top + tile.SourceTop), 0x40CC0020);
    }

    internal static bool Flush() => GdiFlush();

    internal byte[] CopyPixels()
    {
        ObjectDisposedException.ThrowIf(bits == 0, this);
        byte[] pixels = new byte[checked((int)AllocationBytes)];
        Marshal.Copy(bits, pixels, 0, pixels.Length);
        ManagedCopyCount++;
        return pixels;
    }

    internal void Clear()
    {
        if (bits == 0) return;
        int length = checked((int)AllocationBytes);
        for (int offset = 0; offset < length; offset += Zeroes.Length)
            Marshal.Copy(Zeroes, 0, bits + offset, Math.Min(Zeroes.Length, length - offset));
    }

    public void Dispose()
    {
        Clear();
        if (memory != 0 && previous != 0 && previous != new nint(-1)) SelectObject(memory, previous);
        previous = 0;
        if (bitmap != 0) { DeleteObject(bitmap); bitmap = 0; }
        bits = 0;
        if (memory != 0) { DeleteDC(memory); memory = 0; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        internal uint Size;
        internal int Width, Height;
        internal ushort Planes, BitCount;
        internal uint Compression, SizeImage;
        internal int XPelsPerMeter, YPelsPerMeter;
        internal uint ColorsUsed, ColorsImportant;
    }
    [DllImport("gdi32.dll", EntryPoint = "CreateDCW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateDC(string driver, string? device, string? output, nint mode);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateDIBSection(nint dc, ref BitmapInfoHeader information,
        uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BitBlt(nint destination,
        int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
}

internal readonly record struct BootstrapCaptureTiming(double PreparationMilliseconds, double BlitMilliseconds,
    double CopyMilliseconds, double ClearMilliseconds);
