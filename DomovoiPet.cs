using System.Diagnostics;
using static BatteryMonitor.UiPreferences;

namespace BatteryMonitor;

// The mascot is a small shaped child window. Movement physics are angular:
// it never falls or flies away from the location where the user releases it.
internal sealed class DomovoiPet : Control
{
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 16 };
    private readonly ContextMenuStrip _menu = new();
    private DomovoiSurface? _surface;
    private bool _renderFailed;
    private DomovoiGrip _grip;
    private DomovoiPose _renderedPose;
    private double _renderedAngle;
    private int _renderedFacing = 1;
    private double _mouseX, _mouseY, _mouseVx, _mouseVy, _ax, _ay;
    private bool _dragging, _disposed, _lowBattery, _physics, _wander;
    private bool _walking, _hover;
    private int _relativeX, _relativeY, _facing = 1, _walkDirection = 1;
    private long _lastTick;
    private double _x, _y, _time, _idle, _nextWalk = 12, _walkRemaining;
    private double _angle, _angularVelocity;
    private double _limbSwing, _limbVelocity, _happy, _dizzy, _paintElapsed;
    private DomovoiMood _lastMood = DomovoiMood.Idle;
    internal event EventHandler? HideRequested;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00080000; // WS_EX_LAYERED, alpha-blended child window.
            return cp;
        }
    }

    internal DomovoiPet()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
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

    private void RefreshPose()
    {
        if (_disposed || _renderFailed || !IsHandleCreated || Width < 1 || Height < 1 || !Visible) return;
        try
        {
            if (_surface == null || _surface.Width != Width || _surface.Height != Height)
            {
                _surface?.Dispose();
                _surface = new DomovoiSurface(Width, Height);
            }
            var pose = DomovoiArt.Pose(_time, _limbSwing, Mood);
            if (_dragging) PositionAtGrip(pose);
            var g = _surface.Graphics;
            g.Clear(Color.Transparent);
            DomovoiArt.Paint(g, Width, Height, _time, _angle, pose, Mood, _facing);
            _surface.Present(Handle);
            _renderedPose = pose; _renderedAngle = _angle; _renderedFacing = _facing;
            _lastMood = Mood;
        }
        catch (Exception ex)
        {
            _renderFailed = true;
            _animation.Stop();
            _surface?.Dispose(); _surface = null;
            System.Diagnostics.Trace.WriteLine(ex);
            HideRequested?.Invoke(this, EventArgs.Empty);
            Visible = false;
            if (FindForm() is Form form && !form.IsDisposed)
                form.BeginInvoke((Action)(() => MessageBox.Show(form, ex.Message, "Domovoi renderer", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
        }
    }

    private void PositionAtGrip(DomovoiPose pose)
    {
        if (Parent == null) return;
        var point = Parent.PointToClient(MousePosition);
        var offset = DomovoiArt.Offset(_grip, pose, Width, Height, _angle, _facing);
        // During dragging allow transparent padding (or a small part of the
        // body) outside the viewport, so the selected pixel stays pinned.
        double x = Math.Clamp(point.X, 0, Parent.ClientSize.Width) - Width / 2d - offset.X;
        double y = Math.Clamp(point.Y, 0, Parent.ClientSize.Height) - Height / 2d - offset.Y;
        MoveTo(x, y, false);
    }

    private void MoveTo(double x, double y, bool clamp = true)
    {
        _x = clamp ? Math.Clamp(x, 0, MaxX) : x;
        _y = clamp ? Math.Clamp(y, 0, MaxY) : y;
        var point = new Point((int)Math.Round(_x), (int)Math.Round(_y));
        if (point != Location) Location = point;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_animation != null) RefreshPose();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _surface?.Dispose(); _surface = null;
        base.OnHandleDestroyed(e);
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
        }
    }

    internal void RestorePosition()
    {
        if (Parent == null || _disposed) return;
        if (_dragging) { RefreshPose(); return; }
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
        _angle = _angularVelocity = _limbSwing = _limbVelocity = 0;
        _mouseVx = _mouseVy = _ax = _ay = 0;
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
        _angle = Wrap(_angle);
        _dizzy = Math.Abs(_angle) > 75 || Math.Abs(_angularVelocity) > 210 ? 2.1 : 0;
        if (!_physics) ClearRotation();
        // Do not fling or drop the mascot: only settle orientation. Near an
        // edge return the surface into the content area.
        MoveTo(Left, Top);
        SavePosition();
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
        if (_dragging)
        {
            var cursor = Parent.PointToClient(MousePosition);
            if (dt > .0001)
            {
                double vx = (cursor.X - _mouseX) / dt / DpiScale;
                double vy = (cursor.Y - _mouseY) / dt / DpiScale;
                double filter = 1 - Math.Exp(-18 * dt);
                double oldVx = _mouseVx, oldVy = _mouseVy;
                _mouseVx += (Math.Clamp(vx,-3000,3000) - _mouseVx) * filter;
                _mouseVy += (Math.Clamp(vy,-3000,3000) - _mouseVy) * filter;
                _ax += (Math.Clamp((_mouseVx-oldVx)/dt,-9000,9000) - _ax) * filter;
                _ay += (Math.Clamp((_mouseVy-oldVy)/dt,-9000,9000) - _ay) * filter;
            }
            _mouseX = cursor.X; _mouseY = cursor.Y;
        }
        if (_physics)
        {
            var pose = DomovoiArt.Pose(_time,_limbSwing,Mood);
            double target = _dragging ? DomovoiArt.HangingAngle(_grip, pose, _facing, -_ax, 2100-_ay) : 0;
            int count = Math.Max(1, (int)Math.Ceiling(dt / .006));
            double step = dt / count;
            for (int i = 0; i < count; i++)
            {
                double error = Wrap(target - _angle);
                _angularVelocity += (68 * error - 10.5 * _angularVelocity) * step;
                _angularVelocity = Math.Clamp(_angularVelocity, -1000, 1000);
                _angle = Wrap(_angle + _angularVelocity * step);
                double limbTarget = _dragging ? Math.Clamp(-_angularVelocity * .075, -36, 36) : 0;
                _limbVelocity += (92 * (limbTarget - _limbSwing) - 12 * _limbVelocity) * step;
                _limbSwing += _limbVelocity * step;
            }
            if (!_dragging && Math.Abs(_angle) < .12 && Math.Abs(_angularVelocity) < .5)
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
        double cadence = _dragging || _walking || Math.Abs(_angularVelocity) > 2 || Math.Abs(_angle) > .3 ? .014 : .050;
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
        _grip = DomovoiArt.Grab(e.Location, Width, Height, _renderedAngle, _renderedFacing, _renderedPose);
        _facing = _renderedFacing;
        _angle = _renderedAngle;
        var cursor = Parent.PointToClient(MousePosition);
        _mouseX = cursor.X; _mouseY = cursor.Y;
        _mouseVx = _mouseVy = _ax = _ay = 0;
        _dragging = true;
        Capture = true;
        BringToFront();
        RefreshPose();
    }

    // Mouse events do not paint. One timer samples the latest position,
    // advances the spring and presents the whole pose together.
    protected override void OnMouseMove(MouseEventArgs e) => base.OnMouseMove(e);

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

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        // The compositor owns the pixels; never erase the alpha surface.
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
            _surface?.Dispose(); _surface = null;
            ContextMenuStrip = null;
            _menu.Dispose();
        }
        base.Dispose(disposing);
    }
}
