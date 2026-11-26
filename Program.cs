using static BatteryMonitor.UiPreferences;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;

namespace BatteryMonitor;

internal static class Program
{
    private static (string Path, UpdateRelease Release)? _pendingUpdate;
    internal static void QueueUpdate(string path, UpdateRelease release) => _pendingUpdate = (path, release);

    [STAThread]
    private static void Main()
    {
        using (var instance = new Mutex(true, @"Local\BatteryMonitor.App.094", out bool firstInstance))
        {
            if (!firstInstance) return;
            try
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new TrayApplicationContext());
            }
            finally { instance.ReleaseMutex(); }
        } // Dispose the named mutex before Inno Setup checks AppMutex.
        if (_pendingUpdate is { } pending)
        {
            try { UpdateClient.LaunchVerified(pending.Path, pending.Release); }
            catch (Exception ex)
            {
                UpdateClient.DeleteDownload(pending.Path);
                MessageBox.Show(T("Не удалось открыть установщик. Запустите Battery Monitor снова.") + "\n" + ex.Message, "Battery Monitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private bool _closing;
    private System.Windows.Forms.Timer? _updateTimer;
    private readonly CancellationTokenSource _updateLifetime = new();
    private readonly TrayBatteryDisplay _display;
    private readonly TaskbarWidget? _widget;
    private readonly bool _windhawkMode = InstallationMode.UsesWindhawk();
    private readonly ToolStripMenuItem _trayOnlyToggle;
    private List<(string Text, string Detail)> _menuSnapshot = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ToolStripMenuItem _devicesMenu;
    private readonly ToolStripMenuItem _statusMenu;
    private readonly ToolStripMenuItem _g435Menu;
    private G435ManualReading? _g435Reading;
    private bool _g435UsbPresent;
    private bool _refreshing;
    private string _diagnostics = "Diagnostics not ready yet.";
    private WidgetSelection _selection = WidgetSelection.Load();
    private readonly ToolStripMenuItem _widgetDevicesMenu;
    private string _widgetMenuSignature = "";
    private MonitorSettings _settings = MonitorSettings.Load();
    private SettingsWindow? _settingsWindow;
    private readonly BatteryAssistant _assistant = new();
    private AssistantPopup? _assistantPopup;
    private DateTimeOffset _lastAssistantPopup = DateTimeOffset.MinValue;
    private readonly HashSet<string> _assistantUpdateVersions = new(StringComparer.Ordinal);
    private IReadOnlyList<Peripheral> _snapshot = Array.Empty<Peripheral>();
    private readonly ToolStripMenuItem _startupToggle;

    public TrayApplicationContext()
    {
        // Invalidate old data before either display mode starts.
        BatteryBridge.Publish(null);
        _devicesMenu = new ToolStripMenuItem(T("Устройства")) { Enabled = true };
        _widgetDevicesMenu = new ToolStripMenuItem(T("Устройства в виджете"));
        _g435Reading = G435ManualBattery.Load();
        _g435Menu = new ToolStripMenuItem(T("Logitech G435 — батарея по светодиоду"));
        _g435Menu.DropDownItems.Add(new ToolStripMenuItem(T("Включи G435 и коротко нажми Power")) { Enabled = false });
        _g435Menu.DropDownItems.Add(new ToolStripSeparator());
        _g435Menu.DropDownItems.Add(T("Зелёный — 31–100%"), null, (_, _) => SaveG435Led("green"));
        _g435Menu.DropDownItems.Add(T("Красный — 15–30%"), null, (_, _) => SaveG435Led("red"));
        _g435Menu.DropDownItems.Add(T("Мигающий красный — менее 15%"), null, (_, _) => SaveG435Led("flashing_red"));
        _g435Menu.DropDownItems.Add(new ToolStripSeparator());
        _g435Menu.DropDownItems.Add(T("Удалить ручную отметку"), null, (_, _) => ClearG435Led());
        _statusMenu = new ToolStripMenuItem(T("Запуск…")) { Enabled = false };
        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_devicesMenu);
        menu.Items.Add(_widgetDevicesMenu);
        UpdateWidgetMenu();
        menu.Items.Add(_g435Menu);
        _trayOnlyToggle = new ToolStripMenuItem(T("Только обычные значки в трее")) { Checked = _settings.TrayOnly, CheckOnClick = true };
        _trayOnlyToggle.Click += (_, _) =>
        {
            try { var next = _settings with { TrayOnly = _trayOnlyToggle.Checked }; next.Save(); _settings = next; UpdateIndicators(); }
            catch (Exception ex) { _trayOnlyToggle.Checked = _settings.TrayOnly; MessageBox.Show(ex.Message, "Battery Monitor"); }
        };
        if (!_windhawkMode) menu.Items.Add(_trayOnlyToggle);
        else menu.Items.Add(new ToolStripMenuItem(T("Режим: Windhawk")) { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        _startupToggle = new ToolStripMenuItem(T("Автозапуск с Windows")) { Checked = StartupSettings.IsConfigured(), CheckOnClick = true };
        _startupToggle.Click += (_, _) =>
        {
            try { StartupSettings.SetEnabled(_startupToggle.Checked); }
            catch (Exception ex)
            {
                _startupToggle.Checked = StartupSettings.IsConfigured();
                MessageBox.Show(ex.Message, T("Автозапуск"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            _settingsWindow?.SetStartup(_startupToggle.Checked);
        };
        menu.Items.Add(T("Настройки…"), null, (_, _) => OpenSettings());
        menu.Items.Add(_startupToggle);
        menu.Items.Add(T("Обновить сейчас"), null, async (_, _) => await RefreshDevicesAsync());
        menu.Items.Add(T("Сохранить диагностику..."), null, (_, _) => SaveDiagnostics());
        menu.Items.Add(T("О программе"), null, (_, _) => MessageBox.Show(
            "Battery Monitor " + UpdateClient.CurrentVersion + "\n\n" + T("Режим:") + " " + (_windhawkMode ? T("Windhawk (мод 0.4.0)") : T("Самостоятельный виджет")) + "\n" + T("До трёх устройств.") + "\n" + T("AJAZZ, стандартный HID, Bluetooth и контроллеры Windows.") + "\n" + T("USB G435 — точный заряд неизвестен."),
            "Battery Monitor", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.Add(T("Выход"), null, (_, _) => ExitThread());

        _tray = new NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? DrawBatteryIcon(null),
            Text = "Battery Monitor — scanning...",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => OpenSettings();
        _display = new TrayBatteryDisplay(_tray, menu, OpenSettings, percent => percent.HasValue ? DrawBatteryIcon(percent) : System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? DrawBatteryIcon(null));
        _widget = _windhawkMode ? null : new TaskbarWidget();

        _timer = new System.Windows.Forms.Timer { Interval = _settings.RefreshSeconds * 1000 };
        _timer.Tick += async (_, _) => await RefreshDevicesAsync();
        _timer.Start();
        _ = RefreshDevicesAsync();
        if (UpdateClient.IsConfigured && UpdatePreferences.AutoCheck)
        {
            _updateTimer = new System.Windows.Forms.Timer { Interval = 10000 };
            _updateTimer.Tick += async (_, _) =>
            {
                _updateTimer?.Stop();
                try
                {
                    var release = await UpdateClient.CheckAsync(_updateLifetime.Token);
                    if (!_closing && release != null)
                    {
                        var notice = BatteryAssistant.Update(release.Version);
                        if (_assistantUpdateVersions.Add(notice.Message) && !_assistant.Recent.Any(n => n.Kind == AssistantKind.Update && n.Message == notice.Message))
                        {
                            _assistant.Record(notice);
                            PresentAssistantNotice(notice);
                        }
                        if (!AssistantPreferences.Quiet)
                            _tray.ShowBalloonTip(5000, "Battery Monitor", notice.Message + " — " + T("Откройте настройки обновлений."), ToolTipIcon.Info);
                    }
                }
                catch { /* The manual check shows network errors; don't interrupt startup. */ }
            };
            _tray.BalloonTipClicked += (_, _) => { OpenSettings(); if (_assistant.Latest is { Kind: AssistantKind.Update } notice) _settingsWindow?.OpenAssistantNotice(notice); };
            _updateTimer.Start();
        }
    }

    private void InstallUpdate(string path, UpdateRelease release)
    {
        Program.QueueUpdate(path, release);
        ExitThread();
    }

    private void OpenSettings()
    {
        if (_closing) return;
        if (_settingsWindow is null || _settingsWindow.IsDisposed)
        {
            _settingsWindow = new SettingsWindow(_settings, StartupSettings.IsConfigured(),
                ApplySettings, RefreshDevicesAsync, SaveDiagnostics, InstallUpdate, _assistant);
            _settingsWindow.Show();
            if (_assistant.Latest is { } latest) _settingsWindow.ShowAssistantNotice(latest, false);
        }
        if (_settingsWindow.WindowState == FormWindowState.Minimized)
            _settingsWindow.WindowState = FormWindowState.Normal;
        _settingsWindow.SetSnapshot(_snapshot, _statusMenu.Text ?? string.Empty, _refreshing);
        _settingsWindow.Activate();
    }

    private void ApplySettings(MonitorSettings next, bool startup)
    {
        // Saving the checked startup option updates an old executable path to this build.
        StartupSettings.SetEnabled(startup);
        next = next with { TrayOnly = _settings.TrayOnly };
        next.Save();
        _settings = next;
        _startupToggle.Checked = StartupSettings.IsConfigured();
        _timer.Interval = next.RefreshSeconds * 1000;
        UpdateIndicators();
        _ = RefreshDevicesAsync();
    }

    private void PresentAssistantNotice(AssistantNotice notice)
    {
        if (_closing) return;
        bool open = _settingsWindow is { IsDisposed: false, Visible: true } window &&
            window.WindowState != FormWindowState.Minimized;
        if (_settingsWindow is { IsDisposed: false } settings) settings.ShowAssistantNotice(notice, open);
        if (open || !notice.Urgent || AssistantPreferences.Quiet || !AssistantPreferences.Desktop ||
            !DomovoiPreferences.LoadEnabled() || DateTimeOffset.UtcNow - _lastAssistantPopup < TimeSpan.FromMinutes(2)) return;
        try
        {
            _assistantPopup?.Close();
            _assistantPopup = new AssistantPopup(notice, () =>
            {
                OpenSettings();
                _settingsWindow?.OpenAssistantNotice(notice);
            });
            _assistantPopup.Show();
            _lastAssistantPopup = DateTimeOffset.UtcNow;
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); _assistantPopup?.Dispose(); _assistantPopup = null; }
    }

    private async Task RefreshDevicesAsync()
    {
        if (_refreshing || _closing) return;
        _refreshing = true;
        bool complete = true;
        AssistantNotice? notice = null;
        _statusMenu.Text = T("USB/HID сканирование...");
        _settingsWindow?.SetSnapshot(_snapshot, _statusMenu.Text ?? string.Empty, true);
        try
        {
            // HID scanner is on a worker thread to avoid freezing the tray.
            var usb = await Task.Run(HidUsbScanner.Scan);
            if (_closing) return;
            var devices = new List<Peripheral>(usb.Devices);
            // On startup, publish fresh USB results immediately rather than waiting
            // for Bluetooth enumeration (up to 12 seconds). Never substitute a cached percent.
            if (_snapshot.Count == 0 && devices.Any(d => d.Connected && d.BatteryPercent is >= 0 and <= 100))
            {
                _snapshot = devices.ToArray();
                UpdateIndicators();
                UpdateWidgetMenu();
            }
            var diagnostics = new StringBuilder(usb.Diagnostics);
            try
            {
                var bt = await BluetoothBatteryScanner.ScanAsync();
                devices.AddRange(bt);
                diagnostics.AppendLine($"Bluetooth connected: {bt.Count}; known batteries: {bt.Count(d => d.BatteryPercent.HasValue)}");
            }
            catch (Exception ex) { complete = false; diagnostics.AppendLine($"Bluetooth scan unavailable: {ex.GetType().Name}"); }
            try
            {
                var pads = GameControllerBatteryProvider.Scan();
                // Prefer a HID or Bluetooth reading if there is already an exact matching name with a known charge.
                foreach (var pad in pads)
                {
                    if (devices.Any(d => d.BatteryPercent.HasValue && string.Equals(d.Label, pad.Label, StringComparison.OrdinalIgnoreCase))) continue;
                    devices.Add(pad);
                }
                diagnostics.AppendLine($"Windows game controller batteries: {pads.Count}");
            }
            catch (Exception ex) { complete = false; diagnostics.AppendLine($"Game controller scan unavailable: {ex.GetType().Name}"); }
            diagnostics.AppendLine("G435: receiver presence is NOT headset battery/status. No HID commands sent to G435.");
            diagnostics.AppendLine("G435 manual LED reading: " +
                (_g435Reading is { IsFresh: true } manual ? manual.RangeLabel + " (manual)" : "none/currently stale"));
            if (_closing) return;
            _diagnostics = diagnostics.ToString();
            _snapshot = devices.ToArray();
            _g435UsbPresent = devices.Any(d => d.Name == "Logitech G435 (USB-приёмник)");
            UpdateG435Menu();
            var entries = new List<(string Text, string Detail)>();
            if (devices.Count == 0)
            {
                entries.Add((T("Нужные устройства не найдены"), ""));
                _statusMenu.Text = T("Устройства не найдены");
            }
            else
            {
                // Show battery information directly on hover in the Devices submenu.
                foreach (var d in devices.OrderBy(d => d.Name.StartsWith("AJAZZ", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                                         .ThenBy(d => d.Name))
                {
                    string suffix;
                    if (d.BatteryPercent is int p)
                        suffix = $" — {p}%";
                    else if (d.Name.Contains("G435", StringComparison.OrdinalIgnoreCase))
                        suffix = _g435Reading is { IsFresh: true } deviceNote
                            ? $" — {T("вручную")}: {deviceNote.RangeLabel}"
                            : T(" — точный заряд недоступен");
                    else suffix = T(" — заряд недоступен");
                    entries.Add((T(d.Name) + suffix, d.Detail));
                }
                _statusMenu.Text = $"{T("Обновлено")} {DateTime.Now:HH:mm:ss} · {T("заряд")}: {devices.Count(d => d.BatteryPercent.HasValue)}/{devices.Count}";
            }
            UpdateDeviceMenu(entries);
            UpdateIndicators();
            UpdateWidgetMenu();
            try
            {
                var events = _assistant.Observe(_snapshot, DateTimeOffset.UtcNow, complete);
                if (events.Count > 0) notice = _assistant.Latest;
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }

        }
        catch (Exception ex)
        {
            if (_closing) return;
            _assistant.InvalidateHistory();
            _snapshot = Array.Empty<Peripheral>();
            _statusMenu.Text = T("Ошибка USB-сканирования");
            _diagnostics = "Scan error: " + ex.GetType().Name;
            UpdateDeviceMenu(new List<(string Text, string Detail)> { (T("Сохраните диагностику"), "") });
            UpdateWidgetMenu();
            UpdateIndicators();
        }
        finally
        {
            _refreshing = false;
            if (!_closing)
            {
                _settingsWindow?.SetSnapshot(_snapshot, _statusMenu.Text ?? string.Empty, false);
                if (notice != null) PresentAssistantNotice(notice);
            }
        }
    }

    private void UpdateIndicators()
    {
        if (_closing) return;
        var selected = _settings.ShowNativeWidget ? _selection.Select(_snapshot) : Array.Empty<Peripheral>();
        if (_windhawkMode)
        {
            BatteryBridge.PublishDevices(selected);
            _display.Update(Array.Empty<Peripheral>());
            return;
        }
        _widget?.Update(selected, !_settings.TrayOnly && _settings.ShowNativeWidget);
        _display.Update(_settings.TrayOnly ? selected : Array.Empty<Peripheral>());
    }

    private void UpdateWidgetMenu()
    {
        var available = _snapshot.Where(d => d.BatteryPercent.HasValue && d.Connected).OrderBy(d => d.Name).ToArray();
        var signature = $"{_selection.Auto}|{string.Join(";", _selection.Keys.OrderBy(k => k))}|" + string.Join(";", available.Select(d => d.Key + "=" + d.Name));
        if (signature == _widgetMenuSignature) return;
        _widgetMenuSignature = signature;
        while (_widgetDevicesMenu.DropDownItems.Count > 0)
        {
            var old = _widgetDevicesMenu.DropDownItems[0];
            _widgetDevicesMenu.DropDownItems.RemoveAt(0);
            old.Dispose();
        }
        var automatic = new ToolStripMenuItem(T("Автоматически (до 3)")) { Checked = _selection.Auto };
        automatic.Click += (_, _) => ChangeSelection(new WidgetSelection(true, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
        _widgetDevicesMenu.DropDownItems.Add(automatic);
        _widgetDevicesMenu.DropDownItems.Add(new ToolStripSeparator());
        if (available.Length == 0) _widgetDevicesMenu.DropDownItems.Add(new ToolStripMenuItem(T("Нет устройств с известным зарядом")) { Enabled = false });
        foreach (var device in available)
        {
            var item = new ToolStripMenuItem(T(device.Name)) { Checked = !_selection.Auto && _selection.Keys.Contains(device.Key) };
            item.Click += (_, _) =>
            {
                var keys = _selection.Auto ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : new HashSet<string>(_selection.Keys, StringComparer.OrdinalIgnoreCase);
                if (!keys.Remove(device.Key))
                {
                    if (keys.Count >= 3) { MessageBox.Show(T("Можно выбрать до трёх устройств. Снимите отметку с другого устройства или включите автоматический режим.")); return; }
                    keys.Add(device.Key);
                }
                ChangeSelection(new WidgetSelection(false, keys));
            };
            _widgetDevicesMenu.DropDownItems.Add(item);
        }
        var clear = new ToolStripMenuItem(T("Скрыть все (ручной выбор)"));
        clear.Click += (_, _) => ChangeSelection(new WidgetSelection(false, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
        _widgetDevicesMenu.DropDownItems.Add(clear);
    }

    private void ChangeSelection(WidgetSelection next)
    {
        try { next.Save(); _selection = next; UpdateIndicators(); UpdateWidgetMenu(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Battery Monitor"); }
    }

    private void UpdateDeviceMenu(List<(string Text, string Detail)> entries)
    {
        if (_menuSnapshot.SequenceEqual(entries)) return;
        while (_devicesMenu.DropDownItems.Count > 0)
        {
            var old = _devicesMenu.DropDownItems[0];
            _devicesMenu.DropDownItems.RemoveAt(0);
            old.Dispose();
        }
        foreach (var entry in entries)
            _devicesMenu.DropDownItems.Add(new ToolStripMenuItem(entry.Text)
                { Enabled = false, ToolTipText = entry.Detail });
        _menuSnapshot = entries;
    }

    private void SaveG435Led(string led)
    {
        try
        {
            _g435Reading = new G435ManualReading(led, DateTimeOffset.Now);
            G435ManualBattery.Save(_g435Reading);
            _ = RefreshDevicesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, T("Не удалось сохранить ручную отметку G435"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ClearG435Led()
    {
        try
        {
            G435ManualBattery.Delete();
            _g435Reading = null;
            _ = RefreshDevicesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, T("Не удалось удалить отметку G435"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateG435Menu()
    {
        // USB receiver presence never proves that the headset is on or connected.
        _g435Menu.Enabled = _g435UsbPresent;
        if (!_g435UsbPresent) _g435Menu.Text = T("Logitech G435 — USB-приёмник не найден");
        else if (_g435Reading is { IsFresh: true } note)
            _g435Menu.Text = $"Logitech G435 — {T("вручную")}: {note.RangeLabel}";
        else _g435Menu.Text = T("Logitech G435 — проверить по светодиоду");

        _g435Menu.DropDownItems[^1].Enabled = _g435Reading != null;
    }

    private void SaveDiagnostics()
    {
        using var dialog = new SaveFileDialog
        {
            Title = T("Сохранить диагностику Battery Monitor"),
            FileName = "BatteryMonitor-diagnostics-v" + UpdateClient.CurrentVersion + ".txt",
            Filter = T("Текст (*.txt)|*.txt"),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            try
            {
                File.WriteAllText(dialog.FileName, _diagnostics + "\r\nDisplay mode: " + (_windhawkMode ? "Windhawk" : "Standalone") + "\r\n" + (_widget?.PositionDiagnostics() ?? ""), Encoding.UTF8);
                MessageBox.Show(T("Отчёт сохранён. Можно отправить его в чат."), "Battery Monitor",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, T("Не удалось сохранить отчёт"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private static Icon DrawBatteryIcon(int? percent)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var pen = new Pen(Color.White, 2.2f);
            g.DrawRoundedRectangle(pen, 4, 9, 23, 15, 3);
            g.FillRectangle(Brushes.White, 27, 13, 3, 7);
            if (percent is int p)
            {
                var width = Math.Max(2, (int)(18 * Math.Clamp(p, 0, 100) / 100.0));
                var color = p <= 20 ? Color.IndianRed : p <= 45 ? Color.Goldenrod : Color.LimeGreen;
                using var brush = new SolidBrush(color);
                g.FillRectangle(brush, 7, 12, width, 9);
            }
            else
            {
                using var brush = new SolidBrush(Color.SlateGray);
                g.FillRectangle(brush, 7, 12, 18, 9);
                using var f = new Font("Segoe UI", 7, FontStyle.Bold);
                g.DrawString("?", f, Brushes.White, 11, 10);
            }
        }
        var handle = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    protected override void ExitThreadCore()
    {
        _closing = true;
        _assistantPopup?.Dispose(); _assistantPopup = null;
        _updateTimer?.Stop(); _updateTimer?.Dispose();
        _updateLifetime.Cancel(); _updateLifetime.Dispose();
        _settingsWindow?.Dispose();
        _timer.Stop();
        _timer.Dispose();
        BatteryBridge.Publish(null);
        _widget?.Dispose();
        if (_windhawkMode) _trayOnlyToggle.Dispose();
        _display.Dispose();
        _tray.Visible = false;
        var previousIcon = _tray.Icon;
        var menu = _tray.ContextMenuStrip;
        _tray.Dispose();
        previousIcon?.Dispose();
        menu?.Dispose();
        base.ExitThreadCore();
    }
}

internal static class GraphicsExtensions
{
    public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, int x, int y, int width, int height, int radius)
    {
        using var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + width - d, y, d, d, 270, 90);
        path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
        path.AddArc(x, y + height - d, d, d, 90, 90);
        path.CloseFigure();
        graphics.DrawPath(pen, path);
    }
}
