using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BatteryMonitor;

// A separate, click-through window. It does not modify Explorer or reserve
// taskbar space. Hide rather than guessing when the tray cannot be located.
internal sealed class TaskbarWidget : Form
{
    private readonly System.Windows.Forms.Timer _positionTimer;
    private Peripheral[] _devices = Array.Empty<Peripheral>();
    private AppearanceSettings _appearance = AppearanceSettings.Default;
    private bool _widgetEnabled = true;
    private bool _light;
    private float _scale = 1f;
    private int _ticks;
    private int _cachedWidth;
    private IntPtr _taskbarHandle, _trayHandle;
    private IntPtr _foregroundHook, _locationHook, _visibilityHook;
    private readonly WinEventDelegate _eventCallback;
    private bool _repositioning;
    private bool _closing;
    private Rectangle? _clip;
    private IntPtr _lastForeground;
    private bool _foregroundIsShell;
    private long _lastRaise;
    private long _lastShellProbe;
    private bool _startVisible;
    private bool _wasStartVisible;
    private int _lastTrayInset = -1;
    private IntPtr _geometryTaskbar;
    private string _lastState = "";
    private readonly Queue<string> _positionLog = new();
    private static readonly IntPtr NotTopmost = new(-2);
    private const uint NoMove = 0x0002;
    private const uint NoSize = 0x0001;
    private const uint NoOwnerZOrder = 0x0200;
    private const uint ForegroundEvent = 0x0003;
    private const uint ShowEvent = 0x8002;
    private const uint HideEvent = 0x8003;
    private const uint ReorderEvent = 0x8004;
    private const uint LocationEvent = 0x800B;
    private const uint SkipOwnProcess = 0x0002;
    private static readonly IntPtr Topmost = new(-1);
    private const uint NoActivate = 0x0010;
    private const uint ShowWindowFlag = 0x0040;

    public TaskbarWidget()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        TopMost = true;
        DoubleBuffered = true;
        UpdateTheme();
        _positionTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _positionTimer.Tick += (_, _) => Reposition();
        _eventCallback = OnShellEvent;
        _foregroundHook = SetWinEventHook(ForegroundEvent, ForegroundEvent, IntPtr.Zero,
            _eventCallback, 0, 0, SkipOwnProcess);
        _positionTimer.Start();
        Reposition();
    }

    protected override bool ShowWithoutActivation => true;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; } // HTTRANSPARENT
        base.WndProc(ref m);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT
            parameters.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020;
            return parameters;
        }
    }

    public void Update(IReadOnlyList<Peripheral> selected, bool enabled)
    {
        var (custom, saved) = AppearanceSettings.Load();
        var appearance = custom ? saved : AppearanceSettings.Default;
        var next = selected.Where(d => d.Connected && d.BatteryPercent is >= 0 and <= 100).Take(3).ToArray();
        bool changed = !_devices.SequenceEqual(next) || _appearance != appearance || _widgetEnabled != enabled;
        _devices = next;
        _appearance = appearance;
        _widgetEnabled = enabled;
        if (changed) _cachedWidth = 0;
        Reposition();
        if (changed) Invalidate();
    }

    private int Px(float logical) => (int)Math.Round(logical * _scale);
    private Font WidgetFont() => new("Segoe UI", (_appearance.FontSize == 0 ? (_appearance.Style == "gaming" ? 16 : 14) : _appearance.FontSize) * _scale,
        _appearance.Weight == "normal" ? FontStyle.Regular : FontStyle.Bold, GraphicsUnit.Pixel);
    private const TextFormatFlags LabelFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding |
        TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
    private int LabelWidth(int value, Font font) => TextRenderer.MeasureText($"{value}%", font, new Size(int.MaxValue, int.MaxValue), LabelFlags).Width;
    private string IconMode => _appearance.Icon == "preset" ? (_appearance.Style == "minimal" ? "none" : _appearance.Style == "battery" ? "battery" : "auto") : _appearance.Icon;
    private int IconWidth => IconMode == "none" ? 0 : Px(_appearance.IconSize == 0 ? 18 : _appearance.IconSize);
    private int IconGap => IconWidth == 0 ? 0 : Px(_appearance.Gap);
    private int ItemWidth(int value, Font font) => IconWidth + IconGap + LabelWidth(value, font) + Px(2);
    private int ContentWidth()
    {
        if (_cachedWidth > 0) return _cachedWidth;
        using var font = WidgetFont();
        return _cachedWidth = Px(3) * 2 + _devices.Sum(d => ItemWidth(d.BatteryPercent!.Value, font)) + Math.Max(0, _devices.Length - 1) * Px(9);
    }

    private void UpdateTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            _light = key?.GetValue("SystemUsesLightTheme") is int theme && theme != 0;
        }
        catch { _light = false; }
        // Rare color key, so white or dark foreground text cannot disappear.
        var keyColor = Color.FromArgb(1, 2, 3);
        BackColor = keyColor;
        TransparencyKey = keyColor;
        Invalidate();
    }

    private void OnShellEvent(IntPtr hook, uint eventId, IntPtr hwnd, int objectId,
        int childId, uint threadId, uint time)
    {
        if (_closing || IsDisposed || hwnd == IntPtr.Zero) return;
        bool shellWindow = objectId == 0 && childId == 0 &&
            (hwnd == _taskbarHandle || hwnd == _trayHandle);
        if (eventId == ForegroundEvent || (shellWindow &&
            eventId is ShowEvent or HideEvent or ReorderEvent or LocationEvent))
        {
            if (eventId == ForegroundEvent || eventId is ShowEvent or HideEvent)
            { _lastRaise = 0; _lastShellProbe = 0; }
            Reposition();
        }
    }

    private void UpdateLocationHook(IntPtr taskbar)
    {
        if (taskbar == _taskbarHandle && _locationHook != IntPtr.Zero && _visibilityHook != IntPtr.Zero) return;
        if (taskbar != _taskbarHandle)
        {
            if (_locationHook != IntPtr.Zero) { UnhookWinEvent(_locationHook); _locationHook = IntPtr.Zero; }
            if (_visibilityHook != IntPtr.Zero) { UnhookWinEvent(_visibilityHook); _visibilityHook = IntPtr.Zero; }
            _taskbarHandle = taskbar;
            _trayHandle = IntPtr.Zero;
        }
        if (taskbar == IntPtr.Zero) return;
        uint thread = GetWindowThreadProcessId(taskbar, out uint processId);
        if (thread == 0 || processId == 0) return;
        // OUTOFCONTEXT (0): notifications only, no DLL injection into Explorer.
        if (_locationHook == IntPtr.Zero)
            _locationHook = SetWinEventHook(LocationEvent, LocationEvent, IntPtr.Zero,
                _eventCallback, processId, 0, SkipOwnProcess);
        if (_visibilityHook == IntPtr.Zero)
            _visibilityHook = SetWinEventHook(ShowEvent, ReorderEvent, IntPtr.Zero,
                _eventCallback, processId, 0, SkipOwnProcess);
    }

    private void Reposition()
    {
        if (_closing || IsDisposed || _repositioning) return;
        _repositioning = true;
        try { RepositionCore(); }
        finally { _repositioning = false; }
    }

    // Store only geometry and shell state, never window titles or application names.
    private void TraceState(string state)
    {
        if (state == _lastState) return;
        _lastState = state;
        _positionLog.Enqueue($"{DateTime.Now:HH:mm:ss.fff} {state}");
        while (_positionLog.Count > 180) _positionLog.Dequeue();
    }

    public string PositionDiagnostics() => "Widget position history (no window titles):\r\n" +
        string.Join("\r\n", _positionLog);

    private void HideFor(string reason)
    {
        TraceState("hidden: " + reason + "; start=" + _startVisible);
        Hide();
    }

    private static bool IsStartProcess(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hwnd, out uint processId);
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                   process.ProcessName.Equals("SearchHost", StringComparison.OrdinalIgnoreCase) ||
                   process.ProcessName.Equals("SearchApp", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private bool ProbeStart(Rectangle screen)
    {
        long now = Environment.TickCount64;
        if (now - _lastShellProbe < 100 && _lastShellProbe != 0) return _startVisible;
        _lastShellProbe = now;
        var foreground = GetForegroundWindow();
        if (IsStartProcess(foreground)) return true;
        // Start can be visible without owning the foreground HWND. Its host is
        // often a CoreWindow; check visible, uncloaked windows only.
        bool found = false;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || !GetWindowRect(hwnd, out var rect) ||
                rect.Width < Px(150) || rect.Height < Px(100)) return true;
            var className = new System.Text.StringBuilder(128);
            GetClassName(hwnd, className, className.Capacity);
            string cls = className.ToString();
            if (cls != "Windows.UI.Core.CoreWindow" && cls != "XamlExplorerHostIslandWindow" &&
                cls != "Microsoft.UI.Content.DesktopChildSiteBridge" && cls != "ApplicationFrameWindow") return true;
            if (DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) != 0 || cloaked != 0) return true;
            if (!screen.IntersectsWith(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom))) return true;
            if (!IsStartProcess(hwnd)) return true;
            found = true;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static bool IsAbove(IntPtr upper, IntPtr lower)
    {
        if (upper == IntPtr.Zero || lower == IntPtr.Zero || upper == lower) return false;
        var current = GetWindow(lower, 3); // GW_HWNDPREV
        for (int i = 0; current != IntPtr.Zero && i < 256; i++)
        {
            if (current == upper) return true;
            current = GetWindow(current, 3);
        }
        return false;
    }

    private void RepositionCore()
    {
        if (++_ticks % 100 == 0) UpdateTheme();
        if (!_widgetEnabled || _devices.Length == 0) { HideFor("disabled/no battery"); return; }
        var taskbar = FindWindow("Shell_TrayWnd", null);
        UpdateLocationHook(taskbar);
        if (taskbar == IntPtr.Zero || !IsWindowVisible(taskbar) || !GetWindowRect(taskbar, out var bar))
        { HideFor("taskbar unavailable"); return; }
        if (bar.Width < bar.Height) { HideFor("vertical taskbar"); return; }
        var screen = Screen.FromHandle(taskbar).Bounds;
        var barRect = Rectangle.FromLTRB(bar.Left, bar.Top, bar.Right, bar.Bottom);
        var visibleBar = Rectangle.Intersect(screen, barRect);
        uint dpi = GetDpiForWindow(taskbar);
        float scale = dpi == 0 ? 1f : Math.Clamp(dpi / 96f, 0.75f, 3f);
        if (_scale != scale) { _scale = scale; _cachedWidth = 0; Invalidate(); }
        _startVisible = ProbeStart(screen);
        bool startTransition = _startVisible != _wasStartVisible;
        _wasStartVisible = _startVisible;
        if (startTransition) _lastRaise = 0;
        if (_geometryTaskbar != taskbar) { _geometryTaskbar = taskbar; _lastTrayInset = -1; }
        var tray = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        _trayHandle = tray;
        int trayLeft;
        if (tray != IntPtr.Zero && IsWindowVisible(tray) && GetWindowRect(tray, out var tr) &&
            tr.Width > 0 && tr.Left >= bar.Left && tr.Right <= bar.Right + 4)
        {
            trayLeft = tr.Left;
            _lastTrayInset = bar.Right - trayLeft;
        }
        else if (_startVisible && _lastTrayInset >= 0 && _lastTrayInset < bar.Width)
        {
            // Use the last verified horizontal offset only while Start is open.
            trayLeft = bar.Right - _lastTrayInset;
        }
        else { HideFor("tray unavailable"); return; }
        if (bar.Height < Px(24) || visibleBar.Height <= Px(3))
        { HideFor("taskbar outside screen"); return; }
        if (!_startVisible && IsFullScreenForeground(screen, taskbar))
        { HideFor("fullscreen above taskbar"); return; }
        int width = ContentWidth();
        int height = Math.Min(bar.Height, Px(32));
        int x = trayLeft - width - Px(5);
        int y = bar.Top + (bar.Height - height) / 2;
        var target = new Rectangle(x, y, width, height);
        var visibleTarget = Rectangle.Intersect(target, visibleBar);
        // Clip the overlay to the exposed portion during the auto-hide animation,
        // instead of waiting until the entire widget is on screen.
        if (visibleTarget.Width != target.Width || visibleTarget.Height <= 0)
        { HideFor("widget outside visible taskbar"); return; }
        Rectangle? clip = visibleTarget == target ? null : new Rectangle(
            visibleTarget.Left - x, visibleTarget.Top - y, visibleTarget.Width, visibleTarget.Height);
        bool moved = Bounds != target;
        if (moved) Bounds = target;
        bool clipChanged = _clip != clip;
        if (clipChanged)
        {
            var old = Region;
            Region = clip.HasValue ? new Region(clip.Value) : null;
            old?.Dispose();
            _clip = clip;
        }
        bool wasHidden = !Visible;
        if (wasHidden) Show();
        long now = Environment.TickCount64;
        bool belowTaskbar = IsAbove(taskbar, Handle);
        bool needsRaise = wasHidden || moved || startTransition || _lastRaise == 0 ||
            (belowTaskbar && now - _lastRaise >= 50) || (_startVisible && now - _lastRaise >= 200);
        if (needsRaise)
        {
            // A Start reveal can raise the shell above a window that already
            // has WS_EX_TOPMOST. Re-enter the topmost group if necessary.
            if (belowTaskbar || (_startVisible && startTransition))
                SetWindowPos(Handle, NotTopmost, 0, 0, 0, 0, NoActivate | NoMove | NoSize | NoOwnerZOrder);
            bool raised = SetWindowPos(Handle, Topmost, x, y, width, height, NoActivate | ShowWindowFlag | NoOwnerZOrder);
            _lastRaise = now;
            if (!raised) TraceState("SetWindowPos failed; start=" + _startVisible);
        }
        TraceState($"visible: start={_startVisible}; bar={barRect}; target={target}; taskbarAbove={IsAbove(taskbar, Handle)}; cloaked=" +
            (DwmGetWindowAttribute(Handle, 14, out int widgetCloak, sizeof(int)) == 0 ? widgetCloak.ToString() : "unknown"));
        if (wasHidden || moved || clipChanged) Invalidate();
    }

    private bool IsFullScreenForeground(Rectangle screen, IntPtr taskbar)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == taskbar || foreground == Handle) return false;
        if (_lastForeground != foreground)
        {
            _lastForeground = foreground;
            var className = new System.Text.StringBuilder(128);
            GetClassName(foreground, className, className.Capacity);
            _foregroundIsShell = className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
            if (!_foregroundIsShell)
            {
                GetWindowThreadProcessId(foreground, out uint processId);
                try
                {
                    using var process = System.Diagnostics.Process.GetProcessById((int)processId);
                    _foregroundIsShell = process.ProcessName is "StartMenuExperienceHost" or "ShellExperienceHost" or "SearchHost" or "SearchApp";
                }
                catch { }
            }
        }
        if (_foregroundIsShell) return false;
        if (!GetWindowRect(foreground, out var rect)) return false;
        bool coversScreen = rect.Left <= screen.Left + 2 && rect.Top <= screen.Top + 2 &&
            rect.Right >= screen.Right - 2 && rect.Bottom >= screen.Bottom - 2;
        if (!coversScreen) return false;
        // Only suppress an actually covering foreground window if it is ABOVE the taskbar.
        // This allows the taskbar (and widget) to reveal over a maximized/fullscreen app.
        var current = GetWindow(taskbar, 3); // GW_HWNDPREV
        for (int i = 0; current != IntPtr.Zero && i < 256; i++)
        {
            if (current == foreground) return true;
            current = GetWindow(current, 3);
        }
        return false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        using var font = WidgetFont();
        int x = Px(3);
        foreach (var device in _devices)
        {
            int percent = device.BatteryPercent!.Value;
            var ink = TrayPercentIcon.GetColor(_appearance, percent, _light);
            DrawItem(g, x, device, percent, ink, font);
            x += ItemWidth(percent, font) + Px(9);
        }
    }

    private void DrawItem(Graphics g, int x, Peripheral device, int value, Color ink, Font font)
    {
        string name = device.Name.ToLowerInvariant();
        bool headset = name.Contains("head") || name.Contains("g435") || name.Contains("науш");
        bool mouse = name.Contains("mouse") || name.Contains("ajazz") || name.Contains("мыш");
        string mode = IconMode == "auto" ? (headset ? "headset" : mouse ? "mouse" : "battery") : IconMode;
        var saved = g.Save();
        try
        {
            g.TranslateTransform(x, ClientSize.Height / 2f);
            float iconScale = IconWidth / 18f;
            g.ScaleTransform(Math.Max(.01f, iconScale), Math.Max(.01f, iconScale));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var pen = new Pen(ink, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            if (mode == "battery")
            {
                g.DrawRectangle(pen, 1f, -5f, 14f, 10f);
                g.DrawLine(pen, 17f, -2f, 17f, 2f);
                using var brush = new SolidBrush(ink);
                g.FillRectangle(brush, 3f, -3f, 10f * value / 100f, 6f);
            }
            else if (mode == "headset")
            {
                g.DrawArc(pen, 1f, -7f, 14f, 14f, 180f, 180f);
                g.DrawLine(pen, 1f, 0f, 1f, 5f);
                g.DrawLine(pen, 15f, 0f, 15f, 5f);
                g.DrawRectangle(pen, 1f, 1f, 3f, 6f);
                g.DrawRectangle(pen, 12f, 1f, 3f, 6f);
            }
            else if (mode == "mouse")
            {
                // Rounded mouse shell with two distinct buttons and a separate wheel.
                using var outline = new Pen(ink, 1.35f)
                    { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                using var shell = new GraphicsPath();
                shell.AddBezier(9f, -8f, 4.3f, -8f, 2f, -5.5f, 2f, -1.8f);
                shell.AddLine(2f, -1.8f, 2f, 1.6f);
                shell.AddBezier(2f, 1.6f, 2f, 5.8f, 4.5f, 8f, 9f, 8f);
                shell.AddBezier(9f, 8f, 13.5f, 8f, 16f, 5.8f, 16f, 1.6f);
                shell.AddLine(16f, 1.6f, 16f, -1.8f);
                shell.AddBezier(16f, -1.8f, 16f, -5.5f, 13.7f, -8f, 9f, -8f);
                shell.CloseFigure();
                g.DrawPath(outline, shell);
                g.DrawLine(outline, 3f, -1.2f, 7f, -1.2f);
                g.DrawLine(outline, 11f, -1.2f, 15f, -1.2f);
                g.DrawLine(outline, 9f, -7.4f, 9f, -6.1f);
                using var wheelPen = new Pen(ink, 2.3f)
                    { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(wheelPen, 9f, -4.9f, 9f, -2.4f);
            }
        }
        finally { g.Restore(saved); }
        var bounds = new Rectangle(x + IconWidth + IconGap, 0, LabelWidth(value, font) + Px(2), ClientSize.Height);
        TextRenderer.DrawText(g, $"{value}%", font, bounds, ink, LabelFlags);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closing)
        {
            _closing = true;
            _positionTimer.Stop();
            _positionTimer.Dispose();
            if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);
            if (_locationHook != IntPtr.Zero) UnhookWinEvent(_locationHook);
            if (_visibilityHook != IntPtr.Zero) UnhookWinEvent(_visibilityHook);
        }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    private delegate bool EnumWindowsDelegate(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowsDelegate callback, IntPtr parameter);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    private delegate void WinEventDelegate(IntPtr hook, uint eventId, IntPtr hwnd,
        int objectId, int childId, uint threadId, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax,
        IntPtr module, WinEventDelegate callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? windowName);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder className, int maxCount);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
