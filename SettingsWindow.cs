using static BatteryMonitor.UiPreferences;
using System.Drawing.Drawing2D;
using System.Diagnostics;

namespace BatteryMonitor;

internal sealed class SettingsWindow : Form
{
    private readonly DataGridView _devices;
    private readonly AppearancePage _appearance;
    private readonly Label _status, _saveStatus;
    private readonly ComboBox _interval, _theme, _language;
    private readonly string[] _themes = ["system", "light", "dark"];
    private readonly string[] _languages = ["ru", "uk", "en"];
    private readonly CheckBox _startup, _widget;
    private readonly Button _refresh;
    private readonly Action<MonitorSettings, bool> _apply;
    private readonly Font _heading = new("Segoe UI", 24f, FontStyle.Bold);
    private readonly Font _section = new("Segoe UI Semibold", 12f);
    private readonly Font _deviceFont = new("Segoe UI Semibold", 10f);
    private readonly Font _small = new("Segoe UI", 9f);
    private Icon? _windowIcon;
    internal static Color Ink => Dark ? Color.FromArgb(233, 238, 247) : Color.FromArgb(20, 32, 53);
    internal static Color Muted => Dark ? Color.FromArgb(165, 177, 196) : Color.FromArgb(82, 96, 118);
    private static readonly Color Blue = Color.FromArgb(0, 92, 207);
    internal static Color Canvas => Dark ? Color.FromArgb(25, 29, 36) : Color.FromArgb(240, 244, 251);
    internal static Color Surface => Dark ? Color.FromArgb(35, 41, 51) : Color.White;
    private static Color HeaderFill => Dark ? Color.FromArgb(44, 52, 64) : Color.FromArgb(232, 238, 248);
    private static Color SelectedFill => Dark ? Color.FromArgb(48, 66, 90) : Color.FromArgb(229, 239, 255);
    private static Color Good => Dark ? Color.FromArgb(104, 221, 151) : Color.FromArgb(25, 123, 71);
    private static Color Low => Dark ? Color.FromArgb(255, 132, 121) : Color.FromArgb(180, 55, 42);
    private static Color Line => Dark ? Color.FromArgb(62, 72, 88) : Color.FromArgb(216, 225, 239);

    public SettingsWindow(MonitorSettings settings, bool startup,
        Action<MonitorSettings, bool> apply, Func<Task> refresh, Action diagnostics, Action<string, UpdateRelease> installUpdate)
    {
        _apply = apply;
        Dark = Active.IsDark();
        HandleCreated += (_, _) => SetTitleBar(this);
        // The executable icon is already embedded by the project.
        ShowIcon = true;
        try { _windowIcon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); if (_windowIcon != null) Icon = _windowIcon; } catch { }
        Text = T("Battery Monitor — настройки");
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(900, 780);
        MinimumSize = new Size(780, 735);
        BackColor = Canvas;
        ForeColor = Ink;
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 20, 12), ColumnCount = 1, RowCount = 3, BackColor = Canvas };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        Controls.Add(shell);
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        var devicesTab = MakeButton(T("Устройства и общие"), 210);
        var appearanceTab = MakeButton(T("Оформление"), 160);
        var updatesTab = MakeButton(T("Обновления"), 160);
        nav.Controls.Add(devicesTab); nav.Controls.Add(appearanceTab); nav.Controls.Add(updatesTab);
        shell.Controls.Add(nav, 0, 0);
        var pages = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Canvas };
        shell.Controls.Add(pages, 0, 1);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Canvas, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 222));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        pages.Controls.Add(layout);
        _appearance = new AppearancePage { Visible = false };
        pages.Controls.Add(_appearance);
        var updates = new UpdatePage(installUpdate) { Visible = false };
        pages.Controls.Add(updates);
        void SelectPage(int page)
        {
            layout.Visible = page == 0;
            _appearance.Visible = page == 1;
            updates.Visible = page == 2;
            if (page == 1) _appearance.BringToFront(); else if (page == 2) updates.BringToFront(); else layout.BringToFront();
            PaintButton(devicesTab, page == 0); PaintButton(appearanceTab, page == 1); PaintButton(updatesTab, page == 2);
        }
        devicesTab.Click += (_, _) => SelectPage(0);
        appearanceTab.Click += (_, _) => SelectPage(1);
        updatesTab.Click += (_, _) => SelectPage(2);
        SelectPage(0);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(8, 6, 8, 10), Margin = Padding.Empty, BackColor = Canvas };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        var logo = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 12, 4), BackColor = Canvas };
        logo.Paint += (_, e) => DrawLogo(e.Graphics, logo.ClientRectangle);
        header.Controls.Add(logo, 0, 0); header.SetRowSpan(logo, 2);
        header.Controls.Add(new Label { Text = "Battery Monitor", Dock = DockStyle.Fill, Font = _heading, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty }, 1, 0);
        _status = new Label { Text = T("Ожидание данных…"), Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.TopLeft, Margin = Padding.Empty, AutoEllipsis = true };
        header.Controls.Add(_status, 1, 1); layout.Controls.Add(header, 0, 0);

        var deviceCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(10), Margin = new Padding(0, 0, 0, 8) };
        // Standard WinForms grid: no deferred ImageList bitmap or owner-draw callbacks.
        _devices = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
            AllowUserToResizeColumns = false, AllowUserToOrderColumns = false,
            RowHeadersVisible = false, MultiSelect = false, TabStop = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            BackgroundColor = Surface, BorderStyle = BorderStyle.None,
            GridColor = Line, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeight = 34,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ScrollBars = ScrollBars.Vertical
        };
        _devices.RowTemplate.Height = 42;
        _devices.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface, ForeColor = Ink, Font = Font,
            SelectionBackColor = SelectedFill, SelectionForeColor = Ink,
            Padding = new Padding(8, 0, 8, 0), WrapMode = DataGridViewTriState.False
        };
        _devices.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = HeaderFill, ForeColor = Ink, Font = _deviceFont,
            SelectionBackColor = HeaderFill, SelectionForeColor = Ink,
            Padding = new Padding(8, 0, 8, 0)
        };
        _devices.Columns.Add(new DataGridViewTextBoxColumn { Name = "Device", HeaderText = T("Устройство"), FillWeight = 48, MinimumWidth = 140, SortMode = DataGridViewColumnSortMode.NotSortable });
        _devices.Columns.Add(new DataGridViewTextBoxColumn { Name = "Battery", HeaderText = T("Заряд"), FillWeight = 24, MinimumWidth = 100, SortMode = DataGridViewColumnSortMode.NotSortable });
        _devices.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = T("Состояние"), FillWeight = 28, MinimumWidth = 120, SortMode = DataGridViewColumnSortMode.NotSortable });
        _devices.Columns[0].DefaultCellStyle.Font = _deviceFont;
        _devices.Columns[1].DefaultCellStyle.Font = _deviceFont;
        deviceCard.Controls.Add(_devices); layout.Controls.Add(deviceCard, 0, 1);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        _refresh = MakeButton(T("↻  Обновить сейчас"), 185);
        _refresh.Click += async (_, _) => await refresh();
        var report = MakeButton(T("Сохранить диагностику"), 220);
        report.Click += (_, _) => diagnostics();
        var compatibility = MakeButton(T("Сообщить о совместимости"), 270);
        compatibility.Click += (_, _) =>
        {
            if (MessageBox.Show(this, T("Открыть публичную форму GitHub? Данные не отправляются автоматически. Не публикуйте личные данные и непроверенные файлы диагностики."), "Battery Monitor", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                return;
            try
            {
                // Fixed public issue form; no device IDs, logs or personal data in the URL.
                using var browser = Process.Start(new ProcessStartInfo("https://github.com/kross873/BatteryMonitor/issues/new?template=device-compatibility.yml") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, T("Не удалось открыть форму GitHub.") + "\n" + ex.Message, "Battery Monitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        actions.Controls.Add(_refresh); actions.Controls.Add(report); actions.Controls.Add(compatibility); layout.Controls.Add(actions, 0, 2);

        var group = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 10, 16, 10), Margin = Padding.Empty };
        var options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, BackColor = Surface };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62)); options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        for (int i = 0; i < 5; i++) options.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        var section = new Label { Text = T("Общие настройки"), Dock = DockStyle.Fill, Font = _section, TextAlign = ContentAlignment.MiddleLeft };
        options.Controls.Add(section, 0, 0); options.SetColumnSpan(section, 2);
        options.Controls.Add(new Label { Text = T("Частота проверки заряда"), AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        _interval = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = Canvas, ForeColor = Ink };
        foreach (int seconds in MonitorSettings.Intervals) _interval.Items.Add($"{seconds} {T("сек.")}");
        _interval.SelectedIndex = Math.Max(0, Array.IndexOf(MonitorSettings.Intervals, settings.RefreshSeconds)); options.Controls.Add(_interval, 1, 1);
        _startup = new CheckBox { Text = T("Запускать вместе с Windows"), Checked = startup, AutoSize = true, Anchor = AnchorStyles.Left };
        _widget = new CheckBox { Text = T("Показывать виджет заряда"), Checked = settings.ShowNativeWidget, AutoSize = true, Anchor = AnchorStyles.Left };
        options.Controls.Add(_startup, 0, 2); options.SetColumnSpan(_startup, 2);
        options.Controls.Add(_widget, 0, 3); options.SetColumnSpan(_widget, 2);
        var ui = UiPreferences.Load();
        _theme = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Canvas, ForeColor = Ink };
        _theme.Items.AddRange(new object[] { T("Как в Windows"), T("Светлая"), T("Тёмная") });
        _theme.SelectedIndex = Math.Max(0, Array.IndexOf(_themes, ui.Theme));
        _language = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Canvas, ForeColor = Ink };
        _language.Items.AddRange(new object[] { "Русский", "Українська", "English" });
        _language.SelectedIndex = Math.Max(0, Array.IndexOf(_languages, ui.Language));
        options.Controls.Add(new Label { Text = T("Тема приложения"), AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        options.Controls.Add(_theme, 1, 4);
        options.Controls.Add(new Label { Text = T("Язык интерфейса"), AutoSize = true, Anchor = AnchorStyles.Left }, 0, 5);
        options.Controls.Add(_language, 1, 5);
        group.Controls.Add(options); layout.Controls.Add(group, 0, 3);
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = T("Выбор устройств — через меню значка в трее. Внешний вид — во вкладке «Оформление»."), Font = _small, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(0, 8, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        _saveStatus = new Label { Dock = DockStyle.Fill, Text = T("Настройки сохраняются локально"), Font = _small, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft };
        var save = MakeButton(T("Применить"), 130, true); save.Dock = DockStyle.Fill; save.Click += (_, _) => Apply();
        var close = MakeButton(T("Закрыть"), 110); close.Dock = DockStyle.Fill; close.Click += (_, _) => Close();
        footer.Controls.Add(_saveStatus, 0, 0); footer.Controls.Add(save, 1, 0); footer.Controls.Add(close, 2, 0); shell.Controls.Add(footer, 0, 2);
        AcceptButton = save; CancelButton = close;
    }

    internal static Button MakeButton(string text, int width, bool primary = false)
    {
        var b = new Button { Text = text, Width = width, Height = 36, Margin = new Padding(0, 2, 8, 4), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        PaintButton(b, primary); return b;
    }
    private static void PaintButton(Button b, bool primary)
    {
        b.BackColor = primary ? Blue : Surface; b.ForeColor = primary ? Color.White : Ink;
        b.FlatAppearance.BorderColor = primary ? Blue : Line; b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(0, 76, 180) : SelectedFill;
        b.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(0, 65, 155) : HeaderFill;
    }
    internal sealed class CardPanel : Panel
    {
        public CardPanel() { BackColor = Surface; DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 24 || Height < 24) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent?.BackColor ?? Canvas);
            using var shape = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 10);
            using var fill = new SolidBrush(Surface); using var pen = new Pen(Line);
            e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(pen, shape);
        }
    }
    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var p = new GraphicsPath(); int d = radius * 2;
        p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
    }
    private static void DrawLogo(Graphics g, Rectangle area)
    {
        if (area.Width < 24 || area.Height < 24) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int size = Math.Min(area.Width - 2, area.Height - 2); var r = new Rectangle(1, (area.Height - size) / 2, size, size);
        using var path = Rounded(r, Math.Min(14, size / 4));
        using var fill = new LinearGradientBrush(r, Color.FromArgb(40, 160, 244), Color.FromArgb(20, 62, 206), 55f); g.FillPath(fill, path);
        float x = r.X + size * .19f, y = r.Y + size * .31f, w = size * .58f, h = size * .38f;
        using var pen = new Pen(Color.White, Math.Max(2, size / 23f)); g.DrawRectangle(pen, x, y, w, h); g.DrawLine(pen, x + w + 3, y + h * .32f, x + w + 3, y + h * .68f);
        using var white = new SolidBrush(Color.White); g.FillPolygon(white, new PointF[] { new(x+w*.54f,y+h*.07f), new(x+w*.27f,y+h*.53f), new(x+w*.46f,y+h*.53f), new(x+w*.40f,y+h*.94f), new(x+w*.72f,y+h*.43f), new(x+w*.53f,y+h*.43f) });
    }
    public void SetSnapshot(IReadOnlyList<Peripheral> devices, string status, bool refreshing)
    {
        if (IsDisposed || Disposing) return;
        _status.Text = status; _refresh.Enabled = !refreshing;
        _devices.SuspendLayout();
        try
        {
            _devices.Rows.Clear();
            foreach (var device in devices)
            {
                bool receiver = device.Name == "Logitech G435 (USB-приёмник)";
                string name = device.Name == "AJAZZ wireless mouse (3151:5007)" ? "AJAZZ AJ179 APEX" : T(device.Name);
                int? percent = device.BatteryPercent is >= 0 and <= 100 ? device.BatteryPercent : null;
                string chargeText = percent is int charge ? $"{charge}%" : T("Недоступен");
                string state = receiver ? T("Приёмник найден") : device.Key.StartsWith("hid:") ? T("HID обнаружен") : device.Connected ? T("Подключено") : T("Не подключено");
                var row = _devices.Rows[_devices.Rows.Add(name, chargeText, state)];
                Color chargeColor = percent is int value ? (value <= 20 ? Low : Good) : Muted;
                row.Cells[1].Style.ForeColor = chargeColor;
                row.Cells[1].Style.SelectionForeColor = chargeColor;
                Color stateColor = state == T("Подключено") ? Good : Muted;
                row.Cells[2].Style.ForeColor = stateColor;
                row.Cells[2].Style.SelectionForeColor = stateColor;
            }
            if (devices.Count == 0) _devices.Rows.Add(T("Нет данных об устройствах"), "", "");
            _devices.ClearSelection();
        }
        finally { _devices.ResumeLayout(); }
    }
    public void SetStartup(bool enabled) => _startup.Checked = enabled;
    private void Apply()
    {
        try
        {
            _appearance.ValidateSettings();
            var next = new MonitorSettings(MonitorSettings.Intervals[_interval.SelectedIndex], _widget.Checked);
            _appearance.SaveSettings(); _apply(next, _startup.Checked);
            var prefs = new UiPreferences(_languages[_language.SelectedIndex], _themes[_theme.SelectedIndex]);
            prefs.Save();
            _saveStatus.Text = T("Настройки сохранены"); _saveStatus.ForeColor = Good;
            if (prefs != Active) MessageBox.Show(this, T("Настройки сохранены. Для смены языка или темы полностью закройте Battery Monitor через меню в трее и запустите снова."), "Battery Monitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _saveStatus.Text = T("Не удалось сохранить"); _saveStatus.ForeColor = Low;
            MessageBox.Show(this, ex.Message, "Battery Monitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { _heading.Dispose(); _section.Dispose(); _deviceFont.Dispose(); _small.Dispose(); _windowIcon?.Dispose(); }
    }
}
