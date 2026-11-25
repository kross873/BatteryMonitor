using static BatteryMonitor.UiPreferences;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BatteryMonitor;

// Uses only the supported WinForms notification-area interface. No Explorer hooks.
internal sealed class TrayBatteryDisplay : IDisposable
{
    private readonly NotifyIcon[] _slots;
    private readonly string?[] _rendered = new string?[3];
    private readonly Func<int?, Icon> _fallback;
    private bool _disposed;

    public TrayBatteryDisplay(NotifyIcon primary, ContextMenuStrip menu, Action openSettings, Func<int?, Icon> fallback)
    {
        _fallback = fallback;
        _slots = new[] { primary, new NotifyIcon(), new NotifyIcon() };
        _rendered[0] = "fallback"; // primary already has the application icon
        for (int i = 1; i < _slots.Length; i++)
        {
            _slots[i].ContextMenuStrip = menu;
            _slots[i].DoubleClick += (_, _) => openSettings();
        }
    }

    public void Update(IReadOnlyList<Peripheral> selected)
    {
        if (_disposed) return;
        var (custom, saved) = AppearanceSettings.Load();
        var appearance = custom ? saved : AppearanceSettings.Default;
        bool light = TrayPercentIcon.IsLightTaskbar();
        for (int i = 0; i < _slots.Length; i++)
        {
            var slot = _slots[i];
            if (i >= selected.Count || selected[i].BatteryPercent is not int percent || percent is < 0 or > 100)
            {
                if (i == 0)
                {
                    Replace(i, "fallback", () => _fallback(null));
                    if (slot.Text != T("Battery Monitor | нет выбранного заряда"))
                        slot.Text = T("Battery Monitor | нет выбранного заряда");
                    slot.Visible = true;
                }
                else if (slot.Visible) slot.Visible = false;
                continue;
            }
            Color color = TrayPercentIcon.GetColor(appearance, percent, light);
            bool bold = appearance.Weight != "normal";
            string key = $"{percent}:{color.ToArgb()}:{bold}";
            Replace(i, key, () => TrayPercentIcon.Create(percent, color, bold));
            string name = new string(selected[i].Name.Where(c => !char.IsControl(c) && !char.IsSurrogate(c)).Take(50).ToArray());
            string tip = $"{name} — {percent}%";
            if (slot.Text != tip) slot.Text = tip;
            if (!slot.Visible) slot.Visible = true;
        }
    }

    private void Replace(int index, string key, Func<Icon> make)
    {
        if (_rendered[index] == key) return;
        Icon next = make();
        var old = _slots[index].Icon;
        try { _slots[index].Icon = next; }
        catch { next.Dispose(); throw; }
        _rendered[index] = key;
        old?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // The application context owns the primary icon and shared menu.
        for (int i = 1; i < _slots.Length; i++)
        {
            var slot = _slots[i];
            slot.Visible = false;
            var icon = slot.Icon;
            slot.ContextMenuStrip = null;
            slot.Dispose();
            icon?.Dispose();
        }
    }
}

internal static class TrayPercentIcon
{
    internal static bool IsLightTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
        }
        catch { return false; }
    }

    internal static Color GetColor(AppearanceSettings appearance, int percent, bool light)
    {
        string mode = appearance.ColorMode == "preset" ? (appearance.Style is "gaming" or "battery" ? "charge" : "system") : appearance.ColorMode;
        return mode switch
        {
            "charge" => percent <= 20 ? Color.FromArgb(242, 93, 93) : percent <= 45 ? Color.FromArgb(244, 183, 50) : Color.FromArgb(73, 205, 118),
            "cyan" => Color.FromArgb(102, 204, 255),
            "purple" => Color.FromArgb(180, 140, 240),
            "white" => Color.White,
            "custom" when AppearanceSettings.ValidColor(appearance.CustomColor) => ColorTranslator.FromHtml(appearance.CustomColor),
            _ => light ? Color.FromArgb(30, 30, 30) : Color.White
        };
    }

    internal static Icon Create(int percent, Color color, bool bold)
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var path = new GraphicsPath())
        using (var family = new FontFamily("Segoe UI"))
        using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // Digits only: a percent sign would make the text too small at 16 pixels.
            path.AddString(Math.Clamp(percent, 0, 100).ToString(CultureInfo.InvariantCulture), family,
                (int)(bold ? FontStyle.Bold : FontStyle.Regular), 32, PointF.Empty, format);
            RectangleF bounds = path.GetBounds();
            float scale = Math.Min(29f / Math.Max(1f, bounds.Width), 25f / Math.Max(1f, bounds.Height));
            using var transform = new Matrix(scale, 0, 0, scale,
                (32f - bounds.Width * scale) / 2 - bounds.X * scale,
                (32f - bounds.Height * scale) / 2 - bounds.Y * scale);
            path.Transform(transform);
            using var outline = new Pen(color.GetBrightness() > 0.48f ? Color.FromArgb(220, 15, 15, 15) : Color.FromArgb(220, 245, 245, 245), 1.4f) { LineJoin = LineJoin.Round };
            using var brush = new SolidBrush(color);
            graphics.DrawPath(outline, path);
            graphics.FillPath(brush, path);
        }
        IntPtr handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
