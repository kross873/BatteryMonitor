using static BatteryMonitor.UiPreferences;
using System.Drawing.Drawing2D;
using System.Diagnostics;

namespace BatteryMonitor;

internal sealed class SettingsWindow : Form
{
    private readonly FlowLayoutPanel _devices;
    private readonly Label _compatibilitySummary;
    private string _cardSignature = "";
    private readonly AppearancePage _appearance;
    private readonly DomovoiPet _domovoi;
    private readonly BatteryAssistant _assistant;
    private IReadOnlyList<Peripheral> _assistantDevices = Array.Empty<Peripheral>();
    private readonly Label _assistantLabel;
    private readonly Button _assistantOpen;
    private readonly Action<int> _assistantSelect;
    private readonly Func<string, Point?> _assistantTarget;
    private AssistantNotice? _assistantNotice;
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
    internal static Color Ink => Dark ? Color.FromArgb(244, 235, 250) : Color.FromArgb(43, 33, 58);
    internal static Color Muted => Dark ? Color.FromArgb(186, 171, 206) : Color.FromArgb(101, 83, 121);
    private static Color Accent => Dark ? Color.FromArgb(250, 208, 130) : Color.FromArgb(121, 75, 34);
    private static Color Sidebar => Dark ? Color.FromArgb(29, 23, 43) : Color.FromArgb(233, 224, 243);
    internal static Color Canvas => Dark ? Color.FromArgb(22, 18, 34) : Color.FromArgb(247, 242, 250);
    internal static Color Surface => Dark ? Color.FromArgb(37, 30, 54) : Color.FromArgb(255, 252, 255);
    private static Color HeaderFill => Dark ? Color.FromArgb(53, 43, 75) : Color.FromArgb(237, 228, 246);
    private static Color SelectedFill => Dark ? Color.FromArgb(66, 48, 89) : Color.FromArgb(222, 205, 241);
    private static Color Good => Dark ? Color.FromArgb(114, 215, 170) : Color.FromArgb(17, 116, 79);
    private static Color Low => Dark ? Color.FromArgb(255, 132, 121) : Color.FromArgb(180, 55, 42);
    private static Color Line => Dark ? Color.FromArgb(77, 61, 101) : Color.FromArgb(213, 198, 231);

    public SettingsWindow(MonitorSettings settings, bool startup,
        Action<MonitorSettings, bool> apply, Func<Task> refresh, Action diagnostics, Action<string, UpdateRelease> installUpdate, BatteryAssistant? assistant = null)
    {
        _apply = apply;
        _assistant = assistant ?? new BatteryAssistant();
        Dark = Active.IsDark();
        HandleCreated += (_, _) => SetTitleBar(this);
        // The executable icon is already embedded by the project.
        ShowIcon = true;
        try { _windowIcon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); if (_windowIcon != null) Icon = _windowIcon; } catch { }
        Text = T("Battery Monitor — настройки");
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(1060, 800);
        MinimumSize = new Size(900, 690);
        BackColor = Canvas;
        ForeColor = Ink;
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, BackColor = Canvas, Margin = Padding.Empty };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 218));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        Controls.Add(shell);
        var sidebar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Sidebar, Padding = new Padding(14), Margin = Padding.Empty };
        sidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        shell.Controls.Add(sidebar, 0, 0); shell.SetRowSpan(sidebar, 3);
        var brand = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = Sidebar };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 45)); brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var logo = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 12, 5, 40) };
        logo.Paint += (_, e) => DrawLogo(e.Graphics, logo.ClientRectangle);
        brand.Controls.Add(logo, 0, 0);
        brand.Controls.Add(new Label { Text = "domovoi\nBattery Monitor", Font = _section, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0,0,0,18), ForeColor = Ink }, 1, 0);
        sidebar.Controls.Add(brand, 0, 0);
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Margin = Padding.Empty, BackColor = Sidebar };
        var devicesTab = MakeButton(T("Устройства"), 184);
        var appearanceTab = MakeButton(T("Оформление"), 184);
        var updatesTab = MakeButton(T("Обновления"), 184);
        var compatibilityTab = MakeButton(T("Совместимость"), 184);
        foreach (var b in new[] { devicesTab, appearanceTab, updatesTab, compatibilityTab }) { b.Height = 48; b.TextAlign = ContentAlignment.MiddleLeft; b.Padding = new Padding(12,0,0,0); nav.Controls.Add(b); }
        sidebar.Controls.Add(nav, 0, 1);
        var signature = new Panel { Dock = DockStyle.Fill, BackColor = Sidebar, Margin = Padding.Empty };
        signature.Paint += (_, e) => DrawNightSignature(e.Graphics, signature.ClientRectangle);
        var credit = new Label { Text = "domovoi  /  v" + UpdateClient.CurrentVersion, Font = _small, ForeColor = Muted, Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft };
        signature.Controls.Add(credit);
        sidebar.Controls.Add(signature, 0, 2);
        var pages = new Panel { Dock = DockStyle.Fill, Margin = new Padding(24,18,24,0), BackColor = Canvas };
        shell.Controls.Add(pages, 1, 0);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Canvas, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 208));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        pages.Controls.Add(layout);
        _appearance = new AppearancePage { Visible = false };
        pages.Controls.Add(_appearance);
        var updates = new UpdatePage(installUpdate) { Visible = false };
        pages.Controls.Add(updates);
        var compatibility = new Panel { Dock = DockStyle.Fill, BackColor = Canvas, Visible = false, AutoScroll = true };
        var helpCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(20) };
        var help = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Surface, Padding = new Padding(8) };
        help.Controls.Add(new Label { Text = T("Совместимость"), Font = _heading, AutoSize = true, Margin = new Padding(0,0,0,12) });
        _compatibilitySummary = new Label { Text = T("Ожидание данных…"), ForeColor = Muted, AutoSize = true, MaximumSize = new Size(500,0), Margin = new Padding(0,0,0,18) };
        help.Controls.Add(_compatibilitySummary);
        help.Controls.Add(new Label { Text = T("Помогите расширить поддержку устройств"), Font = _section, AutoSize = true, Margin = new Padding(0,0,0,8) });
        help.Controls.Add(new Label { Text = T("Укажите модель устройства и работает ли отображение заряда. Отчёты публичные, данные автоматически не отправляются."), AutoSize = true, MaximumSize = new Size(500,0), ForeColor = Muted, Margin = new Padding(0,0,0,16) });
        var reportCompatibility = MakeButton(T("Сообщить о совместимости"), 300, true);
        reportCompatibility.Click += (_, _) => OpenCompatibilityReport();
        help.Controls.Add(reportCompatibility);
        var compatibilityList = MakeButton(T("Список совместимости"), 300);
        compatibilityList.Click += (_, _) => OpenBrowser("https://github.com/kross873/BatteryMonitor/blob/main/docs/COMPATIBILITY.md");
        help.Controls.Add(compatibilityList);
        help.Controls.Add(new Label { Text = T("Обнаружение USB-приёмника не гарантирует получение заряда."), AutoSize = true, ForeColor = Muted, MaximumSize = new Size(500,0), Margin = new Padding(0,20,0,0) });
        helpCard.Controls.Add(help); compatibility.Controls.Add(helpCard); pages.Controls.Add(compatibility);
        _domovoi = new DomovoiPet { Visible = _appearance.DomovoiEnabled };
        pages.Controls.Add(_domovoi);
        pages.ClientSizeChanged += (_, _) => _domovoi.RestorePosition();
        _domovoi.HideRequested += (_, _) => _appearance.DomovoiEnabled = false;
        _appearance.DomovoiEnabledChanged += (_, _) =>
        {
            _domovoi.Visible = _appearance.DomovoiEnabled;
            if (_domovoi.Visible) { _domovoi.RestorePosition(); _domovoi.BringToFront(); }
        };
        void SelectPage(int page)
        {
            var views = new Control[] { layout, _appearance, updates, compatibility };
            var buttons = new[] { devicesTab, appearanceTab, updatesTab, compatibilityTab };
            for (int i = 0; i < views.Length; i++)
            {
                views[i].Visible = i == page;
                PaintNavigation(buttons[i], i == page);
            }
            views[page].BringToFront();
            if (_domovoi.Visible) _domovoi.BringToFront();
        }
        devicesTab.Click += (_, _) => SelectPage(0);
        appearanceTab.Click += (_, _) => SelectPage(1);
        updatesTab.Click += (_, _) => SelectPage(2);
        compatibilityTab.Click += (_, _) => SelectPage(3);
        SelectPage(0);
        _assistantSelect = SelectPage;

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, BackColor = Canvas };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(new Label { Text = T("Устройства и общие"), Dock = DockStyle.Fill, Font = _heading, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty }, 0, 0);
        _status = new Label { Text = T("Ожидание данных…"), Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.TopLeft, Margin = Padding.Empty, AutoEllipsis = true };
        header.Controls.Add(_status, 0, 1); layout.Controls.Add(header, 0, 0);
        _devices = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0,0,0,8), BackColor = Canvas, Padding = new Padding(0,0,2,0) };
        _devices.SizeChanged += (_, _) => ResizeDeviceCards();
        layout.Controls.Add(_devices, 0, 1);
        // Explicit perch action only; no autonomous obstruction of cards.
        // Resolve on each frame so a scrolled/recreated card is not retained.
        _domovoi.FindPerch = () =>
        {
            if (!layout.Visible || !_devices.Visible || !_devices.IsHandleCreated || !pages.IsHandleCreated) return null;
            var viewport = pages.RectangleToClient(_devices.RectangleToScreen(_devices.ClientRectangle));
            foreach (Control card in _devices.Controls)
            {
                if (!card.Visible || !card.IsHandleCreated) continue;
                var bounds = pages.RectangleToClient(card.RectangleToScreen(card.ClientRectangle));
                if (!viewport.Contains(bounds) || bounds.Width < 60) continue;
                float scale = DomovoiArt.ScaleFor(_domovoi.Width, _domovoi.Height);
                int foot = (int)Math.Round(_domovoi.Height / 2d + (410 - 215 + 15) * scale);
                int edge = bounds.Top >= foot ? bounds.Top : bounds.Bottom;
                int x = bounds.Right - _domovoi.Width / 2 - 42;
                int y = edge - foot;
                if (y < 0 || y + _domovoi.Height > pages.ClientSize.Height) continue;
                return new Point(Math.Clamp(x, 0, Math.Max(0,pages.ClientSize.Width - _domovoi.Width)), y);
            }
            return null;
        };

        _assistantTarget = key =>
        {
            if (!layout.Visible || !_devices.IsHandleCreated || !pages.IsHandleCreated) return null;
            var card = _devices.Controls.Cast<Control>().FirstOrDefault(c => string.Equals(c.Tag as string, key, StringComparison.Ordinal));
            if (card == null || !card.Visible || !card.IsHandleCreated) return null;
            var viewport = pages.RectangleToClient(_devices.RectangleToScreen(_devices.ClientRectangle));
            var bounds = pages.RectangleToClient(card.RectangleToScreen(card.ClientRectangle));
            if (!viewport.Contains(bounds)) return null;
            return new Point(Math.Clamp(bounds.Right - _domovoi.Width / 2, 0, Math.Max(0, pages.ClientSize.Width - _domovoi.Width)),
                Math.Clamp(bounds.Bottom - _domovoi.Height, 0, Math.Max(0, pages.ClientSize.Height - _domovoi.Height)));
        };
        var assistantMenu = new ToolStripMenuItem(T("Помощник domovoi"));
        assistantMenu.DropDownItems.Add(T("Заряд устройств и прогноз"), null, (_, _) => ShowAssistantSummary());
        assistantMenu.DropDownItems.Add(T("Обновить сейчас"), null, async (_, _) => await refresh());
        assistantMenu.DropDownItems.Add(T("Оформление виджета"), null, (_, _) => SelectPage(1));
        assistantMenu.DropDownItems.Add(T("Проверить обновления"), null, async (_, _) => { SelectPage(2); await updates.RequestCheckAsync(); });
        assistantMenu.DropDownItems.Add(T("Сохранить диагностику"), null, (_, _) => diagnostics());
        assistantMenu.DropDownItems.Add(T("Совместимость"), null, (_, _) => SelectPage(3));
        var quiet = new ToolStripMenuItem(T("Не беспокоить")) { CheckOnClick = true, Checked = AssistantPreferences.Quiet };
        quiet.CheckedChanged += (_, _) =>
        {
            try { AssistantPreferences.Write("AssistantQuiet", quiet.Checked); _domovoi.SetLowBattery(!quiet.Checked && _assistantDevices.Any(d => d.Connected && d.BatteryPercent is >= 0 and <= 20)); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "domovoi"); }
        };
        assistantMenu.DropDownOpening += (_, _) => { if (quiet.Checked != AssistantPreferences.Quiet) quiet.Checked = AssistantPreferences.Quiet; };
        assistantMenu.DropDownItems.Add(quiet);
        _domovoi.AddAssistantMenu(assistantMenu);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, BackColor = Canvas };
        _refresh = MakeButton(T("↻  Обновить сейчас"), 185);
        _refresh.Click += async (_, _) => await refresh();
        var report = MakeButton(T("Сохранить диагностику"), 220);
        report.Click += (_, _) => diagnostics();
        actions.Controls.Add(_refresh); actions.Controls.Add(report); layout.Controls.Add(actions, 0, 2);

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

        var assistantCard = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(24,4,24,0), Padding = new Padding(12,7,12,7) };
        var assistantLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Surface, Margin = Padding.Empty };
        assistantLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        assistantLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        assistantLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        _assistantLabel = new Label { Text = "domovoi  /  " + T("Заряд устройств и прогноз"), Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Ink, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
        _assistantOpen = MakeButton(T("Показать"), 110, true); _assistantOpen.Dock = DockStyle.Fill; _assistantOpen.Enabled = false;
        _assistantOpen.Click += (_, _) => { if (_assistantNotice is { } notice) OpenAssistantNotice(notice); };
        var details = MakeButton(T("Подробнее"), 110); details.Dock = DockStyle.Fill; details.Click += (_, _) => ShowAssistantSummary();
        assistantLayout.Controls.Add(_assistantLabel, 0, 0); assistantLayout.Controls.Add(_assistantOpen, 1, 0); assistantLayout.Controls.Add(details, 2, 0);
        assistantCard.Controls.Add(assistantLayout); shell.Controls.Add(assistantCard, 1, 1);
        updates.ReleaseFound += version =>
        {
            var notice = BatteryAssistant.Update(version);
            if (_assistant.Recent.Any(n => n.Kind == AssistantKind.Update && n.Message == notice.Message)) return;
            _assistant.Record(notice); ShowAssistantNotice(notice, true);
        };

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(0, 8, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        _saveStatus = new Label { Dock = DockStyle.Fill, Text = T("Настройки сохраняются локально"), Font = _small, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft };
        var save = MakeButton(T("Применить"), 130, true); save.Dock = DockStyle.Fill; save.Click += (_, _) => Apply();
        var close = MakeButton(T("Закрыть"), 110); close.Dock = DockStyle.Fill; close.Click += (_, _) => Close();
        footer.Controls.Add(_saveStatus, 0, 0); footer.Controls.Add(save, 1, 0); footer.Controls.Add(close, 2, 0); footer.Margin = new Padding(24,0,24,12); shell.Controls.Add(footer, 1, 2);
        AcceptButton = save; CancelButton = close;
        Shown += (_, _) => { _domovoi.RestorePosition(); _domovoi.BringToFront(); };
    }

    internal void ShowAssistantNotice(AssistantNotice notice, bool animate)
    {
        if (IsDisposed || Disposing) return;
        _assistantNotice = notice;
        _assistantLabel.Text = T("Последнее событие:") + " " + notice.Message;
        _assistantLabel.ForeColor = notice.Urgent ? Low : Ink;
        _assistantOpen.Enabled = true;
        if (animate && !AssistantPreferences.Quiet && Visible && WindowState != FormWindowState.Minimized)
        {
            Func<Point?>? target = notice.DeviceKey is string key && notice.Urgent ? () => _assistantTarget(key) : null;
            _domovoi.ReactToAssistant(notice, () => OpenAssistantNotice(notice), target);
        }
    }

    internal void OpenAssistantNotice(AssistantNotice notice)
    {
        if (IsDisposed || Disposing) return;
        _assistantSelect((int)notice.Destination);
        foreach (var card in _devices.Controls.OfType<CardPanel>()) { card.Emphasized = false; card.Invalidate(); }
        if (notice.Destination == AssistantDestination.Devices && notice.DeviceKey is string key)
        {
            var card = _devices.Controls.OfType<CardPanel>().FirstOrDefault(c => string.Equals(c.Tag as string, key, StringComparison.Ordinal));
            if (card != null)
            {
                _devices.ScrollControlIntoView(card);
                card.Emphasized = true; card.Invalidate();
                // Explicit navigation may move the mascot; the user can disable autonomous reactions.
                _domovoi.ReactToAssistant(notice with { Kind = AssistantKind.Info }, () => { }, () => _assistantTarget(key), true);
            }
        }
    }

    private void ShowAssistantSummary()
    {
        using var dialog = new Form { Text = "domovoi — " + T("Заряд устройств и прогноз"), StartPosition = FormStartPosition.CenterParent, Size = new Size(650, 540), MinimumSize = new Size(420, 300), BackColor = Canvas, ForeColor = Ink, ShowInTaskbar = false, AutoScaleMode = AutoScaleMode.Font, Font = Font };
        var report = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = true, ScrollBars = ScrollBars.Vertical, BackColor = Surface, ForeColor = Ink, Text = _assistant.Summary(_assistantDevices) };
        var bottom = MakeButton(T("Закрыть"), 120); bottom.Dock = DockStyle.Bottom; bottom.DialogResult = DialogResult.OK;
        dialog.Controls.Add(report); dialog.Controls.Add(bottom); dialog.CancelButton = bottom;
        dialog.HandleCreated += (_, _) => SetTitleBar(dialog);
        dialog.ShowDialog(this);
    }

    internal static Button MakeButton(string text, int width, bool primary = false)
    {
        var b = new NightButton { Text = text, Width = width, Height = 36, Margin = new Padding(0, 2, 8, 4), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        PaintButton(b, primary); return b;
    }
    private static void PaintButton(Button b, bool primary)
    {
        b.BackColor = primary ? Accent : Surface; b.ForeColor = primary ? (Dark ? Color.FromArgb(43,29,24) : Color.White) : Ink;
        b.FlatAppearance.BorderColor = primary ? Accent : Line; b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = primary ? (Dark ? Color.FromArgb(255,224,165) : Color.FromArgb(102,61,28)) : SelectedFill;
        b.FlatAppearance.MouseDownBackColor = primary ? (Dark ? Color.FromArgb(227,181,102) : Color.FromArgb(85,49,24)) : HeaderFill;
    }
    private sealed class NightButton : Button
    {
        private bool _hover, _pressed;
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = _pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) _pressed = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) _pressed = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 4 || Height < 4) { base.OnPaint(e); return; }
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent?.BackColor ?? Canvas);
            int radius = Math.Min((Height - 1) / 2, Math.Max(3, DeviceDpi * 9 / 96));
            using var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), radius);
            Color bg = !Enabled ? HeaderFill : _pressed ? FlatAppearance.MouseDownBackColor : _hover ? FlatAppearance.MouseOverBackColor : BackColor;
            using var fill = new SolidBrush(bg); e.Graphics.FillPath(fill, path);
            if (FlatAppearance.BorderSize > 0 || Focused)
            {
                using var stroke = new Pen(Focused ? Accent : FlatAppearance.BorderColor);
                e.Graphics.DrawPath(stroke, path);
            }
            var bounds = Rectangle.FromLTRB(Padding.Left + 6, Padding.Top, Width - Padding.Right - 6, Height - Padding.Bottom);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            flags |= TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
            TextRenderer.DrawText(e.Graphics, Text, Font, bounds, Enabled ? ForeColor : Muted, flags);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5));
        }
    }

    internal sealed class CardPanel : Panel
    {
        internal bool Emphasized { get; set; }
        public CardPanel() { BackColor = Surface; DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 24 || Height < 24) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent?.BackColor ?? Canvas);
            using var shape = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Math.Min(Math.Min(Width, Height) / 2 - 1, Math.Max(8, DeviceDpi * 14 / 96)));
            using var fill = new SolidBrush(Surface); using var pen = new Pen(Emphasized ? Accent : Line, Emphasized ? 2 : 1);
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
        using var fill = new LinearGradientBrush(r, Color.FromArgb(105, 77, 147), Color.FromArgb(43, 31, 69), 55f); g.FillPath(fill, path);
        float x = r.X + size * .19f, y = r.Y + size * .31f, w = size * .58f, h = size * .38f;
        using var pen = new Pen(Color.FromArgb(255, 220, 147), Math.Max(2, size / 23f)); g.DrawRectangle(pen, x, y, w, h); g.DrawLine(pen, x + w + 3, y + h * .32f, x + w + 3, y + h * .68f);
        using var white = new SolidBrush(Color.FromArgb(255, 220, 147)); g.FillPolygon(white, new PointF[] { new(x+w*.54f,y+h*.07f), new(x+w*.27f,y+h*.53f), new(x+w*.46f,y+h*.53f), new(x+w*.40f,y+h*.94f), new(x+w*.72f,y+h*.43f), new(x+w*.53f,y+h*.43f) });
    }
    private static void PaintNavigation(Button b, bool active)
    {
        b.BackColor = active ? SelectedFill : Sidebar;
        b.ForeColor = active ? (Dark ? Accent : Ink) : Muted;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = SelectedFill;
        b.FlatAppearance.MouseDownBackColor = HeaderFill;
    }

    private static void DrawNightSignature(Graphics g, Rectangle area)
    {
        if (area.Width < 50 || area.Height < 46) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int h = Math.Max(30, area.Height - 26);
        var r = new Rectangle(1, 1, area.Width - 3, h - 2);
        using var frame = Rounded(r, 10);
        using var sky = new LinearGradientBrush(r, Color.FromArgb(35, 27, 55), Color.FromArgb(66, 45, 88), 75f);
        g.FillPath(sky, frame);
        var old = g.Save(); g.SetClip(frame);
        using var moon = new SolidBrush(Color.FromArgb(255, 217, 148));
        int m = Math.Min(31, h / 2);
        g.FillEllipse(moon, r.Right - m - 13, 12, m, m);
        using var shadow = new SolidBrush(Color.FromArgb(57, 40, 78));
        g.FillEllipse(shadow, r.Right - m - 4, 7, m, m);
        using var stars = new SolidBrush(Color.FromArgb(210, 190, 235));
        for (int i = 0; i < 11; i++)
        {
            int x = 9 + (i * 37 + 12) % Math.Max(1, r.Width - 20);
            int y = 7 + (i * 13) % Math.Max(1, h / 2);
            g.FillEllipse(stars, x, y, i % 3 == 0 ? 3 : 2, i % 3 == 0 ? 3 : 2);
        }
        using var houses = new SolidBrush(Color.FromArgb(25, 20, 39));
        for (int i = 0; i < 7; i++)
        {
            int x = i * Math.Max(1, r.Width / 6), bh = 12 + (i * 11) % 21;
            int w = Math.Max(9, r.Width / 7);
            g.FillRectangle(houses, x, r.Bottom - bh, w, bh);
            if (i % 2 == 0) g.FillRectangle(moon, x + 4, r.Bottom - bh + 5, 3, 4);
        }
        g.Restore(old);
    }

    private void OpenBrowser(string url)
    {
        try { using var browser = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, T("Не удалось открыть форму GitHub.") + "\n" + ex.Message, "Battery Monitor", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    private void OpenCompatibilityReport()
    {
        if (MessageBox.Show(this, T("Открыть публичную форму GitHub? Данные не отправляются автоматически. Не публикуйте личные данные и непроверенные файлы диагностики."), "Battery Monitor", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            OpenBrowser("https://github.com/kross873/BatteryMonitor/issues/new?template=device-compatibility.yml");
    }
    private void ResizeDeviceCards()
    {
        if (_devices.IsDisposed) return;
        int available = Math.Max(180, _devices.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
        int width = available >= 560 ? (available - 24) / 2 : available - 12;
        foreach (Control card in _devices.Controls) if (card.Width != width) card.Width = width;
    }
    private Control CreateDeviceCard(string name, string state, int? percent)
    {
        var card = new CardPanel { Width = 300, Height = 146, Margin = new Padding(0,0,12,12), Padding = new Padding(16), AccessibleName = name + " — " + (percent is int p ? p + "%" : T("Недоступен")) };
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Surface, Margin = Padding.Empty };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
        content.Controls.Add(new Label { Text = name, Font = _deviceFont, Dock = DockStyle.Fill, AutoEllipsis = true, Margin = Padding.Empty }, 0, 0);
        content.Controls.Add(new Label { Text = state, ForeColor = Muted, Dock = DockStyle.Fill, AutoEllipsis = true, Font = _small, Margin = Padding.Empty }, 0, 1);
        var color = percent is int charge ? (charge <= 20 ? Low : Good) : Muted;
        content.Controls.Add(new Label { Text = percent is int value ? value + "%" : T("Недоступен"), Font = percent.HasValue ? _heading : _section, ForeColor = color, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = Padding.Empty }, 0, 2);
        var bar = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Margin = Padding.Empty };
        if (percent is int level)
        {
            bar.Paint += (_, e) => DrawChargeBar(e.Graphics, bar.ClientRectangle, level, color);
            bar.Resize += (_, _) => bar.Invalidate();
        }
        else bar.Visible = false;
        content.Controls.Add(bar, 0, 3);
        card.Controls.Add(content);
        return card;
    }
    private static void DrawChargeBar(Graphics g, Rectangle area, int level, Color color)
    {
        if (area.Width < 2 || area.Height < 2) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, area.Width - 1, area.Height - 1);
        int radius = Math.Max(1, Math.Min(rect.Width, rect.Height) / 2);
        using var track = Rounded(rect, radius);
        using var bg = new SolidBrush(HeaderFill);
        g.FillPath(bg, track);
        var old = g.Save(); g.SetClip(track);
        using var fill = new SolidBrush(color);
        g.FillRectangle(fill, 0, 0, area.Width * Math.Clamp(level, 0, 100) / 100f, area.Height);
        g.Restore(old);
    }

    public void SetSnapshot(IReadOnlyList<Peripheral> devices, string status, bool refreshing)
    {
        if (IsDisposed || Disposing) return;
        _assistantDevices = devices;
        _status.Text = status; _refresh.Enabled = !refreshing;
        _domovoi.SetLowBattery(!AssistantPreferences.Quiet && devices.Any(d => d.Connected && d.BatteryPercent is >= 0 and <= 20));
        _compatibilitySummary.Text = T("Обнаружено устройств:") + " " + devices.Count + "  ·  " + T("Заряд доступен:") + " " + devices.Count(d => d.BatteryPercent is >= 0 and <= 100);
        var signature = string.Join("\n", devices.Select(d => d.Key + "|" + d.Name + "|" + d.BatteryPercent + "|" + d.Connected));
        if (_cardSignature == signature && _devices.Controls.Count > 0) return;
        _cardSignature = signature;
        _devices.SuspendLayout();
        try
        {
            while (_devices.Controls.Count > 0) { var old = _devices.Controls[0]; _devices.Controls.RemoveAt(0); old.Dispose(); }
            foreach (var device in devices.OrderByDescending(d => d.BatteryPercent.HasValue).ThenBy(d => d.Name))
            {
                bool receiver = device.Name == "Logitech G435 (USB-приёмник)";
                string name = device.Name == "AJAZZ wireless mouse (3151:5007)" ? "AJAZZ AJ179 APEX" : T(device.Name);
                int? percent = device.BatteryPercent is >= 0 and <= 100 ? device.BatteryPercent : null;
                string state = receiver ? T("Приёмник найден") : device.Key.StartsWith("hid:") ? T("HID обнаружен") : device.Connected ? T("Подключено") : T("Не подключено");
                var card = CreateDeviceCard(name, state, percent); card.Tag = device.Key;
                _devices.Controls.Add(card);
            }
            if (devices.Count == 0) _devices.Controls.Add(CreateDeviceCard(T("Нет данных об устройствах"), "", null));
            ResizeDeviceCards();
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
