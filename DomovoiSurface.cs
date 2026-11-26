using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BatteryMonitor;

// Persistent premultiplied-alpha DIB for a layered child HWND.
// No bitmap/region allocation or parent repaint per frame.
internal sealed class DomovoiSurface : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Header
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Blend { public byte Operation, Flags, Alpha, Format; }
    [StructLayout(LayoutKind.Sequential)]
    private struct XY { public int X, Y; }

    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref Header info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dest, IntPtr position, ref XY size,
        IntPtr source, ref XY origin, uint key, ref Blend blend, uint flags);

    private IntPtr _dc, _dib, _old;
    private Bitmap? _bitmap;
    private Graphics? _graphics;
    internal int Width { get; }
    internal int Height { get; }
    internal Graphics Graphics => _graphics ?? throw new ObjectDisposedException(nameof(DomovoiSurface));

    internal DomovoiSurface(int width, int height)
    {
        Width = width; Height = height;
        try
        {
            _dc = CreateCompatibleDC(IntPtr.Zero);
            if (_dc == IntPtr.Zero) throw new Win32Exception();
            var h = new Header { Size = (uint)Marshal.SizeOf<Header>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
            _dib = CreateDIBSection(_dc, ref h, 0, out var bits, IntPtr.Zero, 0);
            if (_dib == IntPtr.Zero || bits == IntPtr.Zero) throw new Win32Exception();
            _old = SelectObject(_dc, _dib);
            if (_old == IntPtr.Zero || _old == new IntPtr(-1)) throw new Win32Exception();
            _bitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, bits);
            _graphics = System.Drawing.Graphics.FromImage(_bitmap);
        }
        catch { Dispose(); throw; }
    }
    internal void Present(IntPtr hwnd)
    {
        Graphics.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
        var size = new XY { X = Width, Y = Height };
        var origin = new XY();
        var blend = new Blend { Alpha = 255, Format = 1 };
        if (!UpdateLayeredWindow(hwnd, IntPtr.Zero, IntPtr.Zero, ref size, _dc, ref origin, 0, ref blend, 2))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Domovoi layered rendering failed.");
    }
    public void Dispose()
    {
        _graphics?.Dispose(); _graphics = null;
        _bitmap?.Dispose(); _bitmap = null;
        if (_dc != IntPtr.Zero && _old != IntPtr.Zero && _old != new IntPtr(-1)) SelectObject(_dc, _old);
        if (_dib != IntPtr.Zero) DeleteObject(_dib);
        if (_dc != IntPtr.Zero) DeleteDC(_dc);
        _old = _dib = _dc = IntPtr.Zero;
    }
}
