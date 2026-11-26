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
    private DomovoiPose _renderedPose, _pose, _basePose;
    private readonly DomovoiRig _rig = new();
    private float _lookX, _lookY;
    private Point? _perchTarget;
    internal Func<Point?>? FindPerch { get; set; }
    private bool _poseReady;
    private DomovoiMood _actionMood = DomovoiMood.Idle;
    private double _actionRemaining, _actionElapsed, _nextIdleAction = 7, _walkElapsed;
    private double _pressTime, _strokeDistance, _lastStrokeTime, _lastClickTime = -10;
    private int _rapidClicks, _strokeTurns, _strokeDirection;
    private Point _pressPoint, _strokePoint;
    private bool _pressMoved, _strokeReady;
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
        var actions = new ToolStripMenuItem(T("Действия домового"));
        actions.DropDownItems.Add(T("Помахать рукой"), null, (_, _) => StartAction(DomovoiMood.Wave, 2.8));
        actions.DropDownItems.Add(T("Потанцевать"), null, (_, _) => StartAction(DomovoiMood.Dance, 4.5));
        actions.DropDownItems.Add(T("Потянуться"), null, (_, _) => StartAction(DomovoiMood.Stretch, 2.4));
        actions.DropDownItems.Add(T("Поспать"), null, (_, _) => StartAction(DomovoiMood.Sleepy, 10));
        actions.DropDownItems.Add(T("Сесть отдохнуть"), null, (_, _) => StartAction(DomovoiMood.Sitting, 7));
        actions.DropDownItems.Add(T("Выпить чай"), null, (_, _) => StartAction(DomovoiMood.Tea, 6));
        actions.DropDownItems.Add(T("Попрограммировать"), null, (_, _) => StartAction(DomovoiMood.Coding, 8));
        var perch = new ToolStripMenuItem(T("Сесть на карточку"));
        perch.Click += (_, _) =>
        {
            var target = FindPerch?.Invoke();
            if (target.HasValue)
            {
                StartAction(DomovoiMood.Perched, 9);
                _perchTarget = target;
            }
        };
        actions.DropDownItems.Add(perch);
        actions.DropDownOpening += (_, _) => perch.Enabled = FindPerch?.Invoke() != null;
        _menu.Items.Add(actions);
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
        _actionRemaining > 0 ? _actionMood : _walking ? DomovoiMood.Walking : _idle >= 48 ? DomovoiMood.Sleepy :
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
            var pose = _poseReady ? _pose : DomovoiArt.ActionPose(_time, _limbSwing, Mood, _actionElapsed);
            if (_dragging) PositionAtGrip(pose);
            var g = _surface.Graphics;
            g.Clear(Color.Transparent);
            DomovoiArt.Paint(g, Width, Height, _time, _angle, pose, Mood, _facing, _lookX, _lookY);
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
            _actionRemaining = 0; _poseReady = false; _perchTarget = null;
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
        _nextIdleAction = 7;
    }

    private void StartAction(DomovoiMood mood, double seconds)
    {
        if (_dragging || _disposed) return;
        StopWalk(); Wake();
        _happy = _dizzy = 0;
        _perchTarget = null;
        _actionMood = mood; _actionRemaining = seconds; _actionElapsed = 0;
    }

    private void ClearRotation()
    {
        _angle = _angularVelocity = _limbSwing = _limbVelocity = 0;
        _mouseVx = _mouseVy = _ax = _ay = 0;
        _rig.Reset();
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
        _actionRemaining = 0; _perchTarget = null;
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
        bool tap = !_pressMoved && _time - _pressTime < .24;
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
        if (tap && !_disposed && Visible)
        {
            _rapidClicks = _time - _lastClickTime < .85 ? _rapidClicks + 1 : 1;
            _lastClickTime = _time;
            if (_rapidClicks >= 3) StartAction(DomovoiMood.Annoyed, 2.2);
            else if (_rapidClicks == 1) StartAction(DomovoiMood.Wave, 1.8);
        }
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
        if (_actionRemaining > 0)
        {
            _actionRemaining = Math.Max(0, _actionRemaining - dt);
            _actionElapsed += dt;
        }
        if (_perchTarget.HasValue)
        {
            var current = FindPerch?.Invoke();
            if (_dragging || _actionRemaining <= 0 || !current.HasValue)
            {
                _perchTarget = null;
                if (_actionMood == DomovoiMood.Perched) _actionRemaining = 0;
                SavePosition();
            }
            else
            {
                _perchTarget = current;
                double ease = 1 - Math.Exp(-7 * dt);
                MoveTo(_x + (current.Value.X - _x) * ease, _y + (current.Value.Y - _y) * ease);
            }
        }
        if (_strokeReady && _time - _lastStrokeTime > .55)
        {
            _strokeReady = false; _strokeDistance = 0; _strokeTurns = _strokeDirection = 0;
        }
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
                double filter = 1 - Math.Exp(-12 * dt);
                double oldVx = _mouseVx, oldVy = _mouseVy;
                _mouseVx += (Math.Clamp(vx,-3000,3000) - _mouseVx) * filter;
                _mouseVy += (Math.Clamp(vy,-3000,3000) - _mouseVy) * filter;
                _ax += (Math.Clamp((_mouseVx-oldVx)/dt,-9000,9000) - _ax) * filter;
                _ay += (Math.Clamp((_mouseVy-oldVy)/dt,-9000,9000) - _ay) * filter;
            }
            _mouseX = cursor.X; _mouseY = cursor.Y;
            if (Math.Abs(cursor.X - _pressPoint.X) + Math.Abs(cursor.Y - _pressPoint.Y) > 5 * DpiScale) _pressMoved = true;
        }
        if (_physics)
        {
            var pose = _poseReady ? _pose : DomovoiArt.ActionPose(_time,_limbSwing,Mood,_actionElapsed);
            double target = _dragging ? DomovoiArt.HangingAngle(_grip, pose, _facing, -_ax, 2100-_ay) : 0;
            int count = Math.Max(1, (int)Math.Ceiling(dt / .006));
            double step = dt / count;
            for (int i = 0; i < count; i++)
            {
                double error = Wrap(target - _angle);
                _angularVelocity += (55 * error - 11.5 * _angularVelocity) * step;
                _angularVelocity = Math.Clamp(_angularVelocity, -1000, 1000);
                _angle = Wrap(_angle + _angularVelocity * step);
                double limbTarget = _dragging ? Math.Clamp(-_angularVelocity * .075, -36, 36) : 0;
                _limbVelocity += (75 * (limbTarget - _limbSwing) - 12 * _limbVelocity) * step;
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
                _walkElapsed += dt;
                double ramp = Math.Clamp(Math.Min(_walkElapsed, _walkRemaining) / .25, 0, 1);
                double stepX = _x + _walkDirection * 44 * DpiScale * dt * ramp;
                MoveTo(stepX, _y);
                if (stepX <= 0 || stepX >= MaxX || _walkRemaining <= 0)
                    StopWalk();
            }
        }
        else if (_wander && !_dragging && !_hover && !_menu.Visible &&
            _idle >= _nextWalk && _idle < 48 && _happy <= 0 && _dizzy <= 0 && _actionRemaining <= 0 && MaxX > 20)
        {
            _walkDirection = _x > MaxX / 2d ? -1 : 1;
            _facing = _walkDirection;
            _walkRemaining = 2.2;
            _walkElapsed = 0;
            _walking = true;
            _nextWalk = _idle + 17;
        }

        if (_wander && !_dragging && !_walking && !_hover && !_menu.Visible &&
            _actionRemaining <= 0 && _happy <= 0 && _dizzy <= 0 && _idle >= _nextIdleAction && _idle < 45)
        {
            _actionMood = Random.Shared.Next(6) switch
            {
                0 => DomovoiMood.Stretch, 1 => DomovoiMood.Wave, 2 => DomovoiMood.Dance,
                3 => DomovoiMood.Sitting, 4 => DomovoiMood.Tea, _ => DomovoiMood.Coding
            };
            _actionRemaining = _actionMood is DomovoiMood.Tea or DomovoiMood.Coding or DomovoiMood.Sitting ? 5.5 :
                _actionMood == DomovoiMood.Dance ? 3.2 : 2.4;
            _actionElapsed = 0;
            _nextIdleAction = _idle + 15 + Random.Shared.NextDouble() * 5;
        }
        var desired = DomovoiArt.ActionPose(_time,_limbSwing,Mood,_actionElapsed);
        _basePose = _poseReady ? DomovoiArt.Blend(_basePose,desired,dt) : desired;
        _pose = _rig.Apply(_basePose, dt, _dragging, _physics, _grip.Part, _angle, _angularVelocity, _ax, _facing);
        _poseReady = true;
        UpdateGaze(dt);
        _paintElapsed += dt;
        bool active = _dragging || _walking || _actionRemaining > 0 || _happy > 0 || _dizzy > 0 ||
            Math.Abs(_angularVelocity) > 2 || Math.Abs(_angle) > .3;
        // Present every active timer tick, rather than skipping a tick
        // when the actual timer interval is just under a threshold.
        if (active || _paintElapsed >= .032 || Mood != _lastMood)
        {
            _paintElapsed = 0;
            RefreshPose();
        }
    }

    private void UpdateGaze(double dt)
    {
        float goalX = 0, goalY = 0;
        if (!_dragging && Parent != null && Mood is DomovoiMood.Idle or DomovoiMood.Walking or DomovoiMood.Sitting or DomovoiMood.Perched)
        {
            var cursor = Parent.PointToClient(MousePosition);
            if (Parent.ClientRectangle.Contains(cursor))
            {
                var eye = DomovoiArt.Offset(new DomovoiGrip(DomovoiPart.Head, new PointF(180,182)),
                    _pose, Width, Height, _angle, _facing);
                var delta = new PointF((float)(cursor.X - _x - Width / 2d - eye.X),
                    (float)(cursor.Y - _y - Height / 2d - eye.Y));
                var local = DomovoiArt.Rotate(delta, -_angle);
                local.X *= _facing;
                local = DomovoiArt.Rotate(local, -_pose.Head);
                double distance = Math.Sqrt(local.X * local.X + local.Y * local.Y);
                if (distance > 1)
                {
                    float strength = (float)Math.Clamp(distance / (45 * DpiScale), 0, 1);
                    goalX = (float)(local.X / distance * 10) * strength;
                    goalY = (float)(local.Y / distance * 7) * strength;
                }
            }
        }
        float t = (float)(1 - Math.Exp(-9 * dt));
        _lookX += (goalX - _lookX) * t;
        _lookY += (goalY - _lookY) * t;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        if (!_dragging) { StopWalk(); Wake(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        _strokeReady = false; _strokeDistance = 0; _strokeTurns = _strokeDirection = 0;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || Parent == null) return;
        StopWalk();
        Wake();
        _dizzy = _happy = _actionRemaining = 0;
        _perchTarget = null;
        _rig.Reset();
        _basePose = _pose = _renderedPose; _poseReady = true;
        _grip = DomovoiArt.Grab(e.Location, Width, Height, _renderedAngle, _renderedFacing, _renderedPose);
        _facing = _renderedFacing;
        _angle = _renderedAngle;
        var cursor = Parent.PointToClient(MousePosition);
        _mouseX = cursor.X; _mouseY = cursor.Y;
        _pressPoint = cursor; _pressTime = _time; _pressMoved = false;
        _strokeReady = false; _strokeDistance = 0; _strokeTurns = _strokeDirection = 0;
        _mouseVx = _mouseVy = _ax = _ay = 0;
        _dragging = true;
        Capture = true;
        BringToFront();
        RefreshPose();
    }

    // Mouse events do not paint. One timer samples the latest position,
    // advances the spring and presents the whole pose together.
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            if (Parent != null)
            {
                var point = Parent.PointToClient(MousePosition);
                if (Math.Abs(point.X - _pressPoint.X) + Math.Abs(point.Y - _pressPoint.Y) > 5 * DpiScale)
                    _pressMoved = true;
            }
            return;
        }
        if (e.Button != MouseButtons.None || _menu.Visible) return;
        var grip = DomovoiArt.Grab(e.Location, Width, Height, _renderedAngle, _renderedFacing, _renderedPose);
        if (grip.Part is not (DomovoiPart.Head or DomovoiPart.Hat or DomovoiPart.Beard))
        {
            _strokeReady = false; _strokeDistance = 0; _strokeTurns = _strokeDirection = 0;
            return;
        }
        var screen = MousePosition;
        if (_strokeReady && _time - _lastStrokeTime < .55)
        {
            int dx = screen.X - _strokePoint.X;
            if (Math.Abs(dx) >= 2 * DpiScale)
            {
                int direction = Math.Sign(dx);
                if (_strokeDirection != 0 && direction != _strokeDirection) _strokeTurns++;
                _strokeDirection = direction;
                _strokeDistance += Math.Abs(dx);
                _strokePoint = screen;
                if (_strokeTurns >= 2 && _strokeDistance >= 35 * DpiScale)
                {
                    StartAction(DomovoiMood.Petting, 2.7);
                    _strokeDistance = 0; _strokeTurns = 0;
                }
            }
        }
        else { _strokePoint = screen; _strokeReady = true; }
        _lastStrokeTime = _time;
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
        _dizzy = 0; _actionRemaining = 0; _happy = 2.2; Wake();
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
