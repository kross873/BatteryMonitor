using System.Drawing.Drawing2D;
using static BatteryMonitor.UiPreferences;

namespace BatteryMonitor;

// A small, clipped child control, not a separate window or desktop overlay.
// No network, sound, external assets or battery polling of its own.
internal sealed class DomovoiPet : Control
{
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 50 };
    private readonly ContextMenuStrip _menu = new();
    private bool _dragging, _lowBattery, _disposed;
    private Point _grab;
    private int _frame;
    private int _relativeX, _relativeY;
    internal event EventHandler? HideRequested;

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
        _menu.Items.Add(T("Вернуть на место"), null, (_, _) => ResetPosition());
        _menu.Items.Add(T("Скрыть домового"), null, (_, _) => HideRequested?.Invoke(this, EventArgs.Empty));
        ContextMenuStrip = _menu;
        UpdateShape();
        _animation.Tick += (_, _) =>
        {
            if (!Visible || FindForm()?.WindowState == FormWindowState.Minimized) return;
            _frame = (_frame + 1) % 1000;
            Invalidate();
        };
        _animation.Start();
    }

    private void UpdateShape()
    {
        if (Width <= 0 || Height <= 0) return;
        var old = Region;
        Region = DomovoiArt.Shape(Width, Height);
        old?.Dispose();
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
        if (Visible) _animation.Start();
        else { FinishDrag(); _animation.Stop(); }
    }

    internal void RestorePosition()
    {
        if (Parent == null || _dragging) return;
        int w = Math.Max(0, Parent.ClientSize.Width - Width);
        int h = Math.Max(0, Parent.ClientSize.Height - Height);
        Location = new Point((int)Math.Round(w * _relativeX / 10000d),
            (int)Math.Round(h * _relativeY / 10000d));
    }

    private void ResetPosition()
    {
        _relativeX = DomovoiPreferences.DefaultX;
        _relativeY = DomovoiPreferences.DefaultY;
        RestorePosition();
        DomovoiPreferences.SavePosition(_relativeX, _relativeY);
    }

    private void FinishDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        Capture = false;
        if (Parent != null)
        {
            int w = Math.Max(0, Parent.ClientSize.Width - Width);
            int h = Math.Max(0, Parent.ClientSize.Height - Height);
            if (w > 0) _relativeX = Math.Clamp((int)Math.Round(Left * 10000d / w), 0, 10000);
            if (h > 0) _relativeY = Math.Clamp((int)Math.Round(Top * 10000d / h), 0, 10000);
            RestorePosition();
            DomovoiPreferences.SavePosition(_relativeX, _relativeY);
        }
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || Parent == null) return;
        _grab = e.Location;
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
        Location = new Point(
            Math.Clamp(p.X - _grab.X, 0, Math.Max(0, Parent.ClientSize.Width - Width)),
            Math.Clamp(p.Y - _grab.Y, 0, Math.Max(0, Parent.ClientSize.Height - Height)));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) FinishDrag();
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
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width <= 0 || Height <= 0) return;
        DomovoiArt.Paint(e.Graphics, Width, Height, _frame, _dragging, _lowBattery);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _animation.Stop();
            _animation.Dispose();
            ContextMenuStrip = null;
            _menu.Dispose();
        }
        base.Dispose(disposing);
    }
}
