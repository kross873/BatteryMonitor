using System.Drawing.Drawing2D;
using static BatteryMonitor.UiPreferences;

namespace BatteryMonitor;

// A small, clipped child control, not a separate window or desktop overlay.
// No network, sound, external assets or battery polling of its own.
internal sealed class DomovoiPet : Control
{
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 80 };
    private readonly ContextMenuStrip _menu = new();
    private bool _dragging, _lowBattery;
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
        Size = new Size(88, 96);
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

    private static GraphicsPath Silhouette()
    {
        var p = new GraphicsPath();
        p.StartFigure();
        p.AddBezier(7, 48, 2, 38, 18, 37, 20, 29);
        p.AddBezier(20, 29, 22, 5, 44, 0, 61, 7);
        p.AddBezier(61, 7, 80, 10, 74, 31, 91, 40);
        p.AddBezier(91, 40, 103, 49, 91, 58, 88, 58);
        p.AddBezier(88, 58, 96, 72, 86, 89, 82, 93);
        p.AddBezier(82, 93, 95, 108, 71, 108, 61, 104);
        p.AddLine(61, 104, 39, 104);
        p.AddBezier(39, 104, 23, 110, 10, 105, 17, 94);
        p.AddBezier(17, 94, 6, 82, 7, 68, 14, 57);
        p.AddBezier(14, 57, 3, 57, 2, 51, 7, 48);
        p.CloseFigure();
        return p;
    }

    private void UpdateShape()
    {
        if (Width <= 0 || Height <= 0) return;
        using var shape = Silhouette();
        using var m = new Matrix();
        m.Scale(Width / 100f, Height / 110f);
        shape.Transform(m);
        // The shaped region leaves surrounding controls clickable.
        Region = new Region(shape);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateShape();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (_animation == null) return;
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
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(Width / 100f, Height / 110f);

        Color C(int rgb) => Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
        void Ellipse(int color, float x, float y, float w, float h)
        { using var b = new SolidBrush(C(color)); g.FillEllipse(b, x, y, w, h); }
        void Line(int color, float width, params PointF[] points)
        { using var p = new Pen(C(color), width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }; g.DrawLines(p, points); }
        void Curve(int color, Action<GraphicsPath> build)
        { using var path = new GraphicsPath(); build(path); path.CloseFigure(); using var b = new SolidBrush(C(color)); g.FillPath(b, path); }

        using (var outline = Silhouette())
        {
            using var baseBrush = new SolidBrush(C(0x272139));
            g.FillPath(baseBrush, outline);
            using var border = new Pen(C(0x151221), 2.2f);
            g.DrawPath(border, outline);
        }

        // Boots and a little movement when picked up.
        float swing = _dragging ? (float)Math.Sin(_frame * .9) * 3f : 0f;
        Ellipse(0x151421, 17, 92 + swing, 27, 13);
        Ellipse(0x151421, 57, 92 - swing, 27, 13);
        Line(0x65517e, 1.5f, new(22, 97 + swing), new(34, 97 + swing));
        Line(0x65517e, 1.5f, new(63, 97 - swing), new(76, 97 - swing));

        // Knitted cloak, face and hair.
        Ellipse(0x47375d, 16, 53, 69, 45);
        Ellipse(0x322842, 23, 58, 55, 39);
        Ellipse(0x12111a, 19, 35, 64, 40);
        Curve(0x9385ac, p => {
            p.AddBezier(18, 47, 20, 54, 16, 70, 25, 80);
            p.AddBezier(25, 80, 24, 91, 39, 98, 50, 100);
            p.AddBezier(50, 100, 63, 96, 77, 91, 76, 78);
            p.AddBezier(76, 78, 85, 66, 80, 54, 81, 47);
            p.AddBezier(81, 47, 67, 60, 64, 70, 50, 67);
            p.AddBezier(50, 67, 36, 70, 32, 59, 18, 47);
        });
        Curve(0xc9c0da, p => {
            p.AddBezier(20, 48, 25, 59, 37, 65, 50, 58);
            p.AddBezier(50, 58, 63, 65, 74, 59, 80, 48);
            p.AddBezier(80, 48, 80, 68, 67, 79, 50, 80);
            p.AddBezier(50, 80, 32, 79, 20, 68, 20, 48);
        });
        float breathe = (float)Math.Sin(_frame * .15) * 1.4f;
        Line(0xa99abd, 1.8f, new(32, 76), new(39, 86 + breathe), new(50, 94));
        Line(0xa99abd, 1.8f, new(67, 76), new(60, 86 + breathe), new(50, 94));
        Line(0xddd4e9, 1.1f, new(50, 82), new(50, 91));

        // Hat with a stitched patch and violet rim lighting.
        Curve(0x514065, p => {
            p.AddBezier(20, 33, 23, 11, 38, 4, 55, 9);
            p.AddBezier(55, 9, 74, 9, 70, 30, 88, 42);
            p.AddBezier(88, 42, 66, 37, 36, 37, 20, 33);
        });
        Curve(0x30253f, p => {
            p.AddBezier(24, 29, 31, 13, 38, 9, 51, 11);
            p.AddBezier(51, 11, 37, 18, 43, 28, 62, 34);
            p.AddLine(62, 34, 24, 29);
        });
        Curve(0x65517d, p => {
            p.AddBezier(8, 45, 24, 32, 62, 31, 86, 40);
            p.AddBezier(86, 40, 99, 44, 96, 53, 83, 52);
            p.AddBezier(83, 52, 63, 44, 35, 44, 17, 53);
            p.AddBezier(17, 53, 8, 57, 3, 52, 8, 45);
        });
        Line(0xae8ed2, 2f, new(10, 48), new(26, 41), new(43, 38), new(63, 39), new(85, 46));
        Curve(0x827193, p => { p.AddPolygon([new(36, 15), new(49, 17), new(47, 29), new(33, 26)]); });
        Line(0xc6b5d9, 1.2f, new(34, 17), new(38, 18));
        Line(0xc6b5d9, 1.2f, new(32, 23), new(36, 24));
        Line(0xc6b5d9, 1.2f, new(47, 21), new(50, 22));
        Line(0xc6b5d9, 1.2f, new(45, 27), new(49, 28));

        // Eyes blink; low charge is shown by worried eyebrows, not a popup.
        bool blink = _frame % 55 is 51 or 52;
        if (blink)
        {
            Line(0xf9f2ff, 2f, new(32, 55), new(41, 55));
            Line(0xf9f2ff, 2f, new(59, 55), new(68, 55));
        }
        else
        {
            float height = _dragging ? 9f : 6f;
            Ellipse(0x8b6cae, 29, 49, 15, height + 5);
            Ellipse(0x8b6cae, 56, 49, 15, height + 5);
            Ellipse(0xfff8ff, 31, 51, 11, height);
            Ellipse(0xfff8ff, 58, 51, 11, height);
        }
        if (_lowBattery)
        {
            Line(0xffb7a8, 2f, new(31, 49), new(42, 46));
            Line(0xffb7a8, 2f, new(58, 46), new(69, 49));
        }

        // Mittens and the silent mascot's little sleeve seams.
        Ellipse(0x655278, 13, 68, 15, 14);
        Ellipse(0x655278, 72, 68, 15, 14);
        Line(0x9b82b8, 1.3f, new(17, 72), new(23, 74));
        Line(0x9b82b8, 1.3f, new(77, 74), new(83, 72));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animation.Stop();
            _animation.Dispose();
            ContextMenuStrip = null;
            _menu.Dispose();
        }
        base.Dispose(disposing);
    }
}
