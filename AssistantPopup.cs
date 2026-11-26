using System.Diagnostics;
using static BatteryMonitor.UiPreferences;

namespace BatteryMonitor;

// Optional, short-lived local notification. Never activates the settings window
// or launches anything without a click. No additional scanner or network calls.
internal sealed class AssistantPopup : Form
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 50 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Action _open;
    private int _lastPreferenceSecond = -1;
    private readonly Panel _mascot;
    internal AssistantPopup(AssistantNotice notice, Action open)
    {
        _open = open;
        Text = "domovoi";
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 10);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(365, 128);
        BackColor = Color.FromArgb(37, 30, 54);
        ForeColor = Color.FromArgb(244, 235, 250);
        Padding = new Padding(7);
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = BackColor };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 93));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _mascot = new Panel { Dock = DockStyle.Fill, BackColor = BackColor, Cursor = Cursors.Hand };
        _mascot.Paint += (_, e) =>
        {
            double t = _clock.Elapsed.TotalSeconds;
            var mood = notice.Mood;
            DomovoiArt.Paint(e.Graphics, _mascot.Width, _mascot.Height, t, 0,
                DomovoiArt.ActionPose(t, 0, mood, t), mood, 1);
        };
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        right.Controls.Add(new Label { Text = "domovoi  /  Battery Monitor", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(250,208,130), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        right.Controls.Add(new Label { Text = notice.Message, Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        Button Make(string text, int width) => new() { Text = text, Width = width, Height = 27, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(66,48,89), ForeColor = ForeColor, Margin = new Padding(0,0,5,0), Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        var show = Make(T("Показать"), 120);
        var dismiss = Make("×", 42);
        show.Click += (_, _) => OpenNotice();
        dismiss.Click += (_, _) => Close();
        _mascot.Click += (_, _) => OpenNotice();
        buttons.Controls.Add(show); buttons.Controls.Add(dismiss); right.Controls.Add(buttons, 0, 2);
        grid.Controls.Add(_mascot, 0, 0); grid.Controls.Add(right, 1, 0); Controls.Add(grid);
        _timer.Tick += (_, _) =>
        {
            int second = (int)_clock.Elapsed.TotalSeconds;
            if (second >= 9) { Close(); return; }
            if (second != _lastPreferenceSecond)
            {
                _lastPreferenceSecond = second;
                if (AssistantPreferences.Quiet || !AssistantPreferences.Desktop || !DomovoiPreferences.LoadEnabled()) { Close(); return; }
            }
            _mascot.Invalidate();
        };
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000080; return p; }
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var area = Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        Location = new Point(Math.Max(area.Left, area.Right - Width - 14), Math.Max(area.Top, area.Bottom - Height - 14));
        _clock.Restart(); _timer.Start();
    }
    private void OpenNotice()
    {
        Close(); _open();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
