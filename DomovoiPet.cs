using System.Diagnostics;
using System.Runtime.InteropServices;
using static BatteryMonitor.UiPreferences;

namespace BatteryMonitor;

// The mascot is a small shaped child window. Movement physics are angular:
// it never falls or flies away from the location where the user releases it.
internal sealed class DomovoiPet : Control
{
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 33 };
    private readonly ContextMenuStrip _menu = new();
    private Bitmap? _sprite;
    private bool _dragging, _disposed, _lowBattery, _physics, _wander;
    private bool _walking, _hover, _hasHeading;
    private int _relativeX, _relativeY, _facing = 1, _walkDirection = 1;
    private Point _grab, _lastMouse;
    private long _lastTick, _lastDrag;
    private double _x, _y, _time, _idle, _nextWalk = 12, _walkRemaining;
    private double _angle, _angularVelocity, _target, _spin, _tilt, _heading;
    private double _limbSwing, _limbVelocity, _happy, _dizzy, _paintElapsed;
    private DomovoiMood _lastMood = DomovoiMood.Idle;
    internal event EventHandler? HideRequested;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr hwnd, ref NativeRect rect, IntPtr region, uint flags);

    internal DomovoiPet()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Size = new Size(176, 176);
        BackColor = SettingsWindow.Canvas;
        Cursor = Cursors.Hand;
        AccessibleName = T("Интерактивный домовой");
        (_relativeX, _relativeY) = DomovoiPreferences.LoadPosition();
        _physics = DomovoiPreferences.LoadPhysics();
        _wander = DomovoiPreferences.LoadWander();

        _menu.Items.Add(T("Вернуть на место"), null, (_, _) => ResetPosition());
        var physics = new ToolStripMenuItem(T("Физика домового")) { CheckOnClick = true, Checked = _physics };
        physics.CheckedChanged += (_, _) =>
        {
            _physics = physics.Checked;
            DomovoiPreferences.SavePhysics(_physics);
            if (!_physics) ClearRotation();
            RefreshPose();
        };
        var wander = new ToolStripMenuItem(T("Самостоятельная ходьба")) { CheckOnClick = true, Checked = _wander };
        wander.CheckedChanged += (_, _) =>
        {
            _wander = wander.Checked;
            DomovoiPreferences.SaveWander(_wander);
            if (!_wander) StopWalk();
            _nextWalk = _idle + 12;
        };
        _menu.Items.Add(physics);
        _menu.Items.Add(wander);
        _menu.Items.Add(T("Скрыть домового"), null, (_, _) => HideRequested?.Invoke(this, EventArgs.Empty));
        ContextMenuStrip = _menu;
        RefreshPose();
        _lastTick = Stopwatch.GetTimestamp();
        _animation.Tick += (_, _) => Tick();
        _animation.Start();
    }

    private int MaxX => Math.Max(0, (Parent?.ClientSize.Width ?? Width) - Width);
    private int MaxY => Math.Max(0, (Parent?.ClientSize.Height ?? Height) - Height);
    private double DpiScale => Math.Max(.75, DeviceDpi / 96d);
    private static double Wrap(double angle) => Math.IEEERemainder(angle, 360);
    private DomovoiMood Mood => _dragging ? DomovoiMood.Held :
        _dizzy > 0 ? DomovoiMood.Dizzy : _happy > 0 ? DomovoiMood.Happy :
        _walking ? DomovoiMood.Walking : _idle >= 48 ? DomovoiMood.Sleepy :
        _lowBattery ? DomovoiMood.Worried : DomovoiMood.Idle;

    private void RepaintArea(Rectangle area)
    {
        var parent = Parent;
        if (parent == null || parent.IsDisposed || parent.Disposing || !parent.IsHandleCreated) return;
        area.Inflate(2, 2);
        area.Intersect(parent.ClientRectangle);
        if (area.Width <= 0 || area.Height <= 0) return;
        var rect = new NativeRect { Left = area.Left, Top = area.Top, Right = area.Right, Bottom = area.Bottom };
        const uint InvalidateFlag = 0x0001, Erase = 0x0004, AllChildren = 0x0080, UpdateNow = 0x0100;
        if (!RedrawWindow(parent.Handle, ref rect, IntPtr.Zero, InvalidateFlag | Erase | AllChildren | UpdateNow))
            parent.Invalidate(area, true);
    }

    private void RefreshPose()
    {
        if (_disposed || Width < 1 || Height < 1) return;
        var next = DomovoiArt.Render(Width, Height, _time, _angle, _limbSwing, Mood, _facing);
        Region shape;
        try { shape = DomovoiArt.Shape(next); }
        catch { next.Dispose(); throw; }
        var previous = _sprite;
        _sprite = next;
        // Control.Region takes ownership of the new region and disposes
        // its previous one; old rendered pixels are invalidated explicitly.
        Region = shape;
        previous?.Dispose();
        _lastMood = Mood;
        Invalidate();
        RepaintArea(Bounds);
    }

    private void MoveTo(double x, double y)
    {
        _x = Math.Clamp(x, 0, MaxX);
        _y = Math.Clamp(y, 0, MaxY);
        var point = new Point((int)Math.Round(_x), (int)Math.Round(_y));
        if (point == Location) return;
        Rectangle old = Bounds;
        Location = point;
        RepaintArea(Rectangle.Union(old, Bounds));
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (_animation == null || _disposed) return;
        RefreshPose();
        RestorePosition();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (_animation == null || _disposed) return;
        if (Visible)
        {
            _lastTick = Stopwatch.GetTimestamp();
            RefreshPose();
            _animation.Start();
        }
        else
        {
            FinishDrag();
            StopWalk();
            ClearRotation();
            _animation.Stop();
            RepaintArea(Bounds);
        }
    }

    internal void RestorePosition()
    {
        if (Parent == null || _disposed) return;
        if (_dragging) { MoveTo(Left, Top); return; }
        if (_walking) { StopWalk(); return; }
        MoveTo(MaxX * _relativeX / 10000d, MaxY * _relativeY / 10000d);
    }

    private void SavePosition()
    {
        if (Parent == null) return;
        if (MaxX > 0) _relativeX = Math.Clamp((int)Math.Round(Left * 10000d / MaxX), 0, 10000);
        if (MaxY > 0) _relativeY = Math.Clamp((int)Math.Round(Top * 10000d / MaxY), 0, 10000);
        DomovoiPreferences.SavePosition(_relativeX, _relativeY);
    }

    private void Wake()
    {
        _idle = 0;
        _nextWalk = 12;
    }

    private void ClearRotation()
    {
        _angle = _angularVelocity = _target = _spin = _tilt = _limbSwing = _limbVelocity = 0;
        _hasHeading = false;
    }

    private void StopWalk()
    {
        if (!_walking) return;
        _walking = false;
        MoveTo(Left, Top);
        SavePosition();
    }

    private void ResetPosition()
    {
        StopWalk();
        ClearRotation();
        Wake();
        _relativeX = DomovoiPreferences.DefaultX;
        _relativeY = DomovoiPreferences.DefaultY;
        RestorePosition();
        SavePosition();
        RefreshPose();
    }

    private void FinishDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        Capture = false;
        // Keep the dropped location exactly; only the pose settles.
        SavePosition();
        _spin = _tilt = 0;
        _angle = Wrap(_angle);
        _target = 0;
        _hasHeading = false;
        if (!_physics) ClearRotation();
        _dizzy = Math.Abs(_angle) > 75 || Math.Abs(_angularVelocity) > 210 ? 2.1 : .0;
        Wake();
        if (!_disposed) RefreshPose();
    }

    private void Tick()
    {
        long now = Stopwatch.GetTimestamp();
        double dt = Math.Clamp(Stopwatch.GetElapsedTime(_lastTick, now).TotalSeconds, 0, .05);
        _lastTick = now;
        if (_disposed || !Visible || Parent == null || FindForm()?.WindowState == FormWindowState.Minimized)
            return;
        _time += dt;
        _happy = Math.Max(0, _happy - dt);
        _dizzy = Math.Max(0, _dizzy - dt);
        if (!_dragging && !_hover && !_menu.Visible) _idle += dt;
        if (_physics)
        {
            if (_dragging)
            {
                if (Stopwatch.GetElapsedTime(_lastDrag).TotalMilliseconds > 75)
                    _tilt *= Math.Exp(-8 * dt);
                _target = _spin + _tilt;
            }
            else _target = 0;
            // Damped springs, substepped for stability; spring returns to
            // upright after release instead of applying linear gravity.
            int count = Math.Max(1, (int)Math.Ceiling(dt / .008));
            double step = dt / count;
            for (int i = 0; i < count; i++)
            {
                _angularVelocity += (92 * (_target - _angle) - 13 * _angularVelocity) * step;
                _angularVelocity = Math.Clamp(_angularVelocity, -900, 900);
                _angle += _angularVelocity * step;
                double limbTarget = _dragging ? Math.Clamp(-_angularVelocity * .11, -52, 52) : 0;
                _limbVelocity += (110 * (limbTarget - _limbSwing) - 14 * _limbVelocity) * step;
                _limbSwing += _limbVelocity * step;
            }
            // Limit unwrapped angles without discontinuity.
            if (_dragging && Math.Abs(_angle) > 720)
            {
                double offset = _angle - Wrap(_angle);
                _angle -= offset; _spin -= offset; _target -= offset;
            }
            if (!_dragging && Math.Abs(_angle) < .2 && Math.Abs(_angularVelocity) < .5)
                _angle = _angularVelocity = 0;
        }

        if (_walking)
        {
            if (_dragging || _hover || !_wander || _menu.Visible) StopWalk();
            else
            {
                _walkRemaining -= dt;
                double stepX = _x + _walkDirection * 44 * DpiScale * dt;
                MoveTo(stepX, _y);
                if (stepX <= 0 || stepX >= MaxX || _walkRemaining <= 0)
                    StopWalk();
            }
        }
        else if (_wander && !_dragging && !_hover && !_menu.Visible &&
            _idle >= _nextWalk && _idle < 48 && _happy <= 0 && _dizzy <= 0 && MaxX > 20)
        {
            _walkDirection = _x > MaxX / 2d ? -1 : 1;
            _facing = _walkDirection;
            _walkRemaining = 2.2;
            _walking = true;
            _nextWalk = _idle + 17;
        }

        _paintElapsed += dt;
        double cadence = _dragging || _walking || Math.Abs(_angularVelocity) > 2 || Math.Abs(_angle) > .3 ? .030 : .100;
        if (_paintElapsed >= cadence || Mood != _lastMood)
        {
            _paintElapsed = 0;
            RefreshPose();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        StopWalk();
        Wake();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || Parent == null) return;
        StopWalk();
        Wake();
        _dizzy = _happy = 0;
        _spin = _angle;
        _tilt = 0;
        _hasHeading = false;
        _grab = e.Location;
        _lastMouse = Parent.PointToClient(MousePosition);
        _lastDrag = Stopwatch.GetTimestamp();
        _dragging = true;
        Capture = true;
        BringToFront();
        RefreshPose();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || Parent == null) return;
        Point p = Parent.PointToClient(MousePosition);
        double dx = p.X - _lastMouse.X, dy = p.Y - _lastMouse.Y;
        if (dx == 0 && dy == 0) return;
        long now = Stopwatch.GetTimestamp();
        double dt = Math.Max(.008, Stopwatch.GetElapsedTime(_lastDrag, now).TotalSeconds);
        if (_physics)
        {
            double speed = Math.Sqrt(dx * dx + dy * dy) / dt / DpiScale;
            double heading = Math.Atan2(dy, dx) * 180 / Math.PI;
            if (_hasHeading && dt < .16 && speed > 170)
            {
                double turn = Wrap(heading - _heading);
                // Circular mouse gestures unwind the whole body; abrupt
                // reversals contribute to swing, not a false full turn.
                if (Math.Abs(turn) <= 120)
                    _spin += turn;
            }
            _heading = heading;
            _hasHeading = speed > 100;
            _tilt = Math.Clamp(dx / dt / DpiScale * .055, -48, 48);
            _target = _spin + _tilt;
        }
        _lastMouse = p;
        _lastDrag = now;
        MoveTo(p.X - _grab.X, p.Y - _grab.Y);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) FinishDrag();
        base.OnMouseUp(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left) return;
        FinishDrag();
        _dizzy = 0; _happy = 2.2; Wake();
        RefreshPose();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) FinishDrag();
    }

    internal void SetLowBattery(bool low)
    {
        if (_lowBattery == low) return;
        _lowBattery = low;
        RefreshPose();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_sprite != null) e.Graphics.DrawImageUnscaled(_sprite, 0, 0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            if (_dragging || _walking) SavePosition();
            _disposed = true;
            _dragging = _walking = false;
            _animation.Stop();
            _animation.Dispose();
            _sprite?.Dispose(); _sprite = null;
            ContextMenuStrip = null;
            _menu.Dispose();
        }
        base.Dispose(disposing);
    }
}
