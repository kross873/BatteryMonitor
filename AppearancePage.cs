using static BatteryMonitor.UiPreferences;
namespace BatteryMonitor;

internal sealed class AppearancePage : UserControl
{
    private readonly CheckBox _enabled;
    private readonly ComboBox _style, _icon, _color, _weight, _font, _iconSize;
    private readonly TextBox _hex;
    private readonly NumericUpDown _gap;
    private readonly TableLayoutPanel _fields;
    private readonly Label _preview;
    private static readonly string[] Styles = ["native", "minimal", "gaming", "battery"];
    private static readonly string[] Icons = ["preset", "mouse", "battery", "none"];
    private static readonly string[] Colors = ["preset", "system", "charge", "cyan", "purple", "white", "custom"];
    private static readonly string[] Weights = ["preset", "normal", "semibold", "bold"];
    private static readonly int[] FontSizes = [0, .. Enumerable.Range(10, 13)];
    private static readonly int[] IconSizes = [0, .. Enumerable.Range(10, 15)];

    public AppearancePage()
    {
        var (enabled, settings) = AppearanceSettings.Load();
        Dock = DockStyle.Fill;
        AutoScroll = true;
        BackColor = SettingsWindow.Canvas;
        ForeColor = SettingsWindow.Ink;
        var outer = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(16), BackColor = SettingsWindow.Surface };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(outer);
        outer.Controls.Add(new Label { Text = T("Оформление виджета"), AutoSize = true, Font = new Font("Segoe UI", 16f, FontStyle.Bold), Margin = new Padding(3, 4, 3, 12) });
        _enabled = new CheckBox { Text = T("Управлять оформлением из Battery Monitor"), AutoSize = true, Checked = enabled };
        outer.Controls.Add(_enabled);
        outer.Controls.Add(new Label { Text = T("Настройки виджета возле трея (самостоятельного или Windhawk). В режиме обычных значков размер определяет Windows."), AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = SettingsWindow.Muted });
        _fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(0, 6, 0, 0), BackColor = SettingsWindow.Surface };
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        outer.Controls.Add(_fields);
        _style = Choice(["Windows 11", T("Минимализм"), "Gaming", "Battery"], Array.IndexOf(Styles, settings.Style));
        _icon = Choice([T("По стилю"), T("Мышка"), T("Аккумулятор"), T("Без иконки")], Array.IndexOf(Icons, settings.Icon));
        _color = Choice([T("По стилю"), T("Системный"), T("По заряду"), T("Голубой"), T("Фиолетовый"), T("Белый"), T("Свой HEX")], Array.IndexOf(Colors, settings.ColorMode));
        _weight = Choice([T("По стилю"), T("Обычный"), T("Полужирный"), T("Жирный")], Array.IndexOf(Weights, settings.Weight));
        _font = Choice(FontSizes.Select(n => n == 0 ? T("По стилю") : n.ToString()).ToArray(), Array.IndexOf(FontSizes, settings.FontSize));
        _iconSize = Choice(IconSizes.Select(n => n == 0 ? T("По стилю") : n.ToString()).ToArray(), Array.IndexOf(IconSizes, settings.IconSize));
        _hex = new TextBox { Text = settings.CustomColor, MaxLength = 7, Dock = DockStyle.Fill, BackColor = SettingsWindow.Canvas, ForeColor = SettingsWindow.Ink };
        _gap = new NumericUpDown { Minimum = 0, Maximum = 16, Value = settings.Gap, Dock = DockStyle.Fill, BackColor = SettingsWindow.Canvas, ForeColor = SettingsWindow.Ink };
        Add(T("Стиль"), _style); Add(T("Иконка"), _icon); Add(T("Цвет"), _color); Add(T("Свой цвет (#RRGGBB)"), _hex);
        Add(T("Размер текста"), _font); Add(T("Размер иконки"), _iconSize); Add(T("Жирность"), _weight); Add(T("Отступ"), _gap);
        _preview = new Label { Text = T("Пример: 87%"), Dock = DockStyle.Top, Height = 44, BackColor = Color.FromArgb(37, 37, 37), ForeColor = Color.White, TextAlign = ContentAlignment.MiddleCenter };
        outer.Controls.Add(_preview);
        var reset = SettingsWindow.MakeButton(T("Сбросить оформление"), 230);
        reset.Click += (_, _) => { _style.SelectedIndex = 0; _icon.SelectedIndex = 0; _color.SelectedIndex = 0; _weight.SelectedIndex = 0; _font.SelectedIndex = 0; _iconSize.SelectedIndex = 0; _hex.Text = "#66CCFF"; _gap.Value = 5; UpdatePreview(); };
        outer.Controls.Add(reset);
        _enabled.CheckedChanged += (_, _) => { _fields.Enabled = _enabled.Checked; reset.Enabled = _enabled.Checked; UpdatePreview(); };
        foreach (var c in new[] { _style, _icon, _color, _weight, _font, _iconSize }) c.SelectedIndexChanged += (_, _) => UpdatePreview();
        _hex.TextChanged += (_, _) => UpdatePreview();
        _gap.ValueChanged += (_, _) => UpdatePreview();
        _fields.Enabled = enabled;
        reset.Enabled = enabled;
        UpdatePreview();
    }

    private static ComboBox Choice(string[] choices, int index)
    {
        var box = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = SettingsWindow.Canvas, ForeColor = SettingsWindow.Ink };
        box.Items.AddRange(choices);
        box.SelectedIndex = Math.Max(0, index);
        return box;
    }
    private void Add(string label, Control control)
    {
        int row = _fields.RowCount++;
        _fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _fields.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _fields.Controls.Add(control, 1, row);
        control.Margin = new Padding(3, 4, 3, 4);
    }
    private AppearanceSettings Read() => new(Styles[_style.SelectedIndex], Icons[_icon.SelectedIndex], Colors[_color.SelectedIndex],
        _hex.Text.Trim(), FontSizes[_font.SelectedIndex], IconSizes[_iconSize.SelectedIndex], Weights[_weight.SelectedIndex], (int)_gap.Value);
    public void ValidateSettings()
    {
        if (_enabled.Checked && !Read().IsValid()) throw new ArgumentException(T("В оформлении указан неверный цвет. Используйте #RRGGBB, например #66CCFF."));
    }
    public void SaveSettings() => Read().Save(_enabled.Checked);
    private void UpdatePreview()
    {
        var a = Read();
        _hex.Enabled = _enabled.Checked && a.ColorMode == "custom";
        string mode = a.ColorMode == "preset" ? (a.Style is "gaming" or "battery" ? "charge" : "system") : a.ColorMode;
        _preview.ForeColor = mode switch { "charge" => Color.FromArgb(73,183,113), "cyan" => Color.FromArgb(102,204,255), "purple" => Color.FromArgb(180,140,240), "custom" when AppearanceSettings.ValidColor(a.CustomColor) => ColorTranslator.FromHtml(a.CustomColor), _ => Color.White };
        int size = a.FontSize == 0 ? (a.Style == "gaming" ? 16 : 14) : a.FontSize;
        string weight = a.Weight == "preset" ? (a.Style == "minimal" ? "semibold" : "bold") : a.Weight;
        var old = _preview.Font;
        _preview.Font = new Font("Segoe UI", size, weight == "normal" ? FontStyle.Regular : FontStyle.Bold, GraphicsUnit.Pixel);
        if (_ownedPreviewFont) old.Dispose();
        _ownedPreviewFont = true;
        string icon = a.Icon == "preset" ? (a.Style == "minimal" ? "none" : a.Style == "battery" ? "battery" : "mouse") : a.Icon;
        _preview.Text = !_enabled.Checked ? T("Оформление по умолчанию") : $"{(icon == "none" ? "" : icon == "battery" ? "▰  " : "◉  ")}87%  ({T("пример")})";
    }
    private bool _ownedPreviewFont;
    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownedPreviewFont) { var font = _preview.Font; _preview.Font = Font; font.Dispose(); _ownedPreviewFont = false; }
        base.Dispose(disposing);
    }
}
