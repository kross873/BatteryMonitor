using System.Diagnostics;
using System.Runtime.InteropServices;
using static BatteryMonitor.UiPreferences;

namespace BatteryMonitor;

internal sealed class DomovoiPet : Control
{
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 16 };
    private readonly ContextMenuStrip _menu = new();
    private bool _dragging, _lowBattery, _disposed, _moving;
    private bool _physics;
    private Point _grab, _lastMouse;
    private double _x, _y, _vx, _vy;
    private long _lastDrag, _lastTick;
    private int _frame, _idleTicks;
    private int _relativeX, _relativeY;
    internal event EventHandler? HideRequested;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr hwnd, ref NativeRect rect, IntPtr region, uint flags);

    internal DomovoiPet()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Size = new Size(104, 124);
        BackColor = SettingsWindow.Canvas;
        Cursor = Cursors.Hand;
        AccessibleName = T("Интерактивный домовой");
        (_relativeX, _relativeY) = DomovoiPreferences.LoadPosition();
        _physics = DomovoiPreferences.LoadPhysics();

        _menu.Items.Add(T("Вернуть на место"), null, (_, _) => ResetPosition());
        var physicsItem = new ToolStripMenuItem(T("Физика домового"))
        { CheckOnClick = true, Checked = _physics };
        physicsItem.CheckedChanged += (_, _) =>
        {
            _physics = physicsItem.Checked;
            DomovoiPreferences.SavePhysics(_physics);
            if (!_physics) StopMotion();
        };
        _menu.Items.Add(physicsItem);
        _menu.Items.Add(T("Скрыть домового"), null, (_, _) => HideRequested?.Invoke(this, EventArgs.Empty));
        ContextMenuStrip = _menu;
        UpdateShape();
        _lastTick = Stopwatch.GetTimestamp();
        _animation.Tick += (_, _) => Tick();
        _animation.Start();
    }

    private int MaxX => Math.Max(0, (Parent?.ClientSize.Width ?? Width) - Width);
    private int MaxY => Math.Max(0, (Parent?.ClientSize.Height ?? Height) - Height);
    private double Scale => Math.Max(0.75, DeviceDpi / 96d);

    private void UpdateShape()
    {
        if (Width <= 0 || Height <= 0) return;
        var old = Region;
        Region = DomovoiArt.Shape(Width, Height);
        old?.Dispose();
    }

    // A shaped sibling HWND covers nested controls in the active page.
    // Invalidate the old AND new rectangles, including underlying child
    // windows, instead of painting only the mascot at its new location.
    private void RepaintArea(Rectangle area)
    {
        var parent = Parent;
        if (parent == null || parent.IsDisposed || parent.Disposing || !parent.IsHandleCreated) return;
        area.Inflate(2, 2);
        area.Intersect(parent.ClientRectangle);
        if (area.IsEmpty || area.Width <= 0 || area.Height <= 0) return;
        var rect = new NativeRect { Left = area.Left, Top = area.Top, Right = area.Right, Bottom = area.Bottom };
        const uint InvalidateFlag = 0x0001, Erase = 0x0004, AllChildren = 0x0080, UpdateNow = 0x0100;
        if (!RedrawWindow(parent.Handle, ref rect, IntPtr.Zero, InvalidateFlag | Erase | AllChildren | UpdateNow))
            parent.Invalidate(area, true);
    }

    private void MoveTo(double x, double y)
    {
        _x = Math.Clamp(x, 0, MaxX);
        _y = Math.Clamp(y, 0, MaxY);
        var point = new Point((int)Math.Round(_x), (int)Math.Round(_y));
        if (point == Location) return;
        var before = Bounds;
        Location = point;
        RepaintArea(Rectangle.Union(before, Bounds));
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateShape();
        RestorePosition();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (_animation == null || _disposed) return;
        if (Visible)
        {
            _lastTick = Stopwatch.GetTimestamp();
            _animation.Start();
            RepaintArea(Bounds);
        }
        else
        {
            if (_dragging) FinishDrag(false);
            if (_moving) StopMotion();
            _animation.Stop();
            RepaintArea(Bounds);
        }
    }

    internal void RestorePosition()
    {
        if (Parent == null || _disposed) return;
        if (_dragging) { MoveTo(Left, Top); return; }
        if (_moving) { StopMotion(); return; }
        MoveTo(MaxX * _relativeX / 10000d, MaxY * _relativeY / 10000d);
    }

    private void SavePosition()
    {
        if (Parent == null) return;
        if (MaxX > 0) _relativeX = Math.Clamp((int)Math.Round(Left * 10000d / MaxX), 0, 10000);
        if (MaxY > 0) _relativeY = Math.Clamp((int)Math.Round(Top * 10000d / MaxY), 0, 10000);
        DomovoiPreferences.SavePosition(_relativeX, _relativeY);
    }

    private void StopMotion()
    {
        _moving = false;
        _vx = _vy = 0;
        MoveTo(Left, Top);
        SavePosition();
        Invalidate();
    }

    private void ResetPosition()
    {
        _moving = false;
        _vx = _vy = 0;
        _relativeX = DomovoiPreferences.DefaultX;
        _relativeY = DomovoiPreferences.DefaultY;
        RestorePosition();
        SavePosition();
    }

    private void FinishDrag(bool throwOnRelease)
    {
        if (!_dragging) return;
        _dragging = false;
        Capture = false;
        if (!_physics || !throwOnRelease || Parent == null)
        {
            StopMotion();
            return;
        }
        // Releasing after holding the mouse still must not replay an old throw.
        if (Stopwatch.GetElapsedTime(_lastDrag).TotalMilliseconds > 100)
            _vx = _vy = 0;
        _x = Left;
        _y = Top;
        _moving = true;
        _lastTick = Stopwatch.GetTimestamp();
        Invalidate();
    }

    private void Tick()
    {
        long now = Stopwatch.GetTimestamp();
        double dt = Math.Clamp(Stopwatch.GetElapsedTime(_lastTick, now).TotalSeconds, 0, 0.04);
        _lastTick = now;
        if (!Visible || _disposed || Parent == null || FindForm()?.WindowState == FormWindowState.Minimized)
            return;

        if (_moving && !_dragging && _physics)
        {
            // Substeps keep collisions stable after a delayed UI timer tick.
            int steps = Math.Max(1, (int)Math.Ceiling(dt / 0.008));
            double step = dt / steps, scale = Scale;
            for (int i = 0; i < steps; i++)
            {
                _vy = Math.Min(_vy + 1800 * scale * step, 1550 * scale);
                _vx *= Math.Exp(-0.8 * step);
                _x += _vx * step;
                _y += _vy * step;
                if (_x < 0) { _x = 0; _vx = Math.Abs(_vx) * 0.45; }
                if (_x > MaxX) { _x = MaxX; _vx = -Math.Abs(_vx) * 0.45; }
                if (MaxX == 0) _vx = 0;
                if (_y < 0) { _y = 0; _vy = Math.Abs(_vy) * 0.35; }
                if (_y >= MaxY)
                {
                    _y = MaxY;
                    _vy = Math.Abs(_vy) < 100 * scale ? 0 : -Math.Abs(_vy) * 0.32;
                    _vx *= Math.Exp(-8 * step);
                }
                if (MaxY == 0) _vy = 0;
            }
            MoveTo(_x, _y);
            if (_y >= MaxY && Math.Abs(_vy) < 1 && Math.Abs(_vx) < 9 * scale)
                StopMotion();
        }
        if (++_idleTicks >= 3)
        {
            _idleTicks = 0;
            _frame = (_frame + 1) % 1000;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || Parent == null) return;
        _moving = false;
        _vx = _vy = 0;
        _grab = e.Location;
        _lastMouse = Parent.PointToClient(MousePosition);
        _lastDrag = Stopwatch.GetTimestamp();
        _dragging = true;
        Capture = true;
        BringToFront();
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || Parent == null) return;
        Point p = Parent.PointToClient(MousePosition);
        if (p == _lastMouse) return;
        long now = Stopwatch.GetTimestamp();
        double dt = Math.Max(0.008, Stopwatch.GetElapsedTime(_lastDrag, now).TotalSeconds);
        double cap = 1300 * Scale;
        if (dt > 0.1) { _vx = 0; _vy = 0; }
        _vx = Math.Clamp(_vx * 0.3 + (p.X - _lastMouse.X) / dt * 0.7, -cap, cap);
        _vy = Math.Clamp(_vy * 0.3 + (p.Y - _lastMouse.Y) / dt * 0.7, -cap, cap);
        _lastMouse = p;
        _lastDrag = now;
        MoveTo(p.X - _grab.X, p.Y - _grab.Y);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) FinishDrag(true);
        base.OnMouseUp(e);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) FinishDrag(false);
    }

    internal void SetLowBattery(bool low)
    {
        if (_lowBattery == low) return;
        _lowBattery = low;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width <= 0 || Height <= 0) return;
        DomovoiArt.Paint(e.Graphics, Width, Height, _frame, _dragging || _moving, _lowBattery);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            if (_moving || _dragging) SavePosition();
            _disposed = true;
            _dragging = _moving = false;
            _animation.Stop();
            _animation.Dispose();
            ContextMenuStrip = null;
            _menu.Dispose();
        }
        base.Dispose(disposing);
    }
}
