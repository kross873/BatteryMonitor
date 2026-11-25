using System.Security.Cryptography;
using System.Diagnostics;
using System.Globalization;
using static BatteryMonitor.UiPreferences;

namespace BatteryMonitor;

internal sealed class UpdatePage : Panel
{
    private readonly Label _status, _lastCheck;
    private readonly TextBox _notes;
    private readonly Button _check, _download;
    private readonly ProgressBar _progress;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Action<string, UpdateRelease> _install;
    private UpdateRelease? _release;
    private bool _busy;

    internal UpdatePage(Action<string, UpdateRelease> install)
    {
        _install = install;
        Dock = DockStyle.Fill; BackColor = SettingsWindow.Canvas; ForeColor = SettingsWindow.Ink;
        var card = new SettingsWindow.CardPanel { Dock = DockStyle.Fill, Padding = new Padding(20) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = SettingsWindow.Surface };
        flow.Controls.Add(new Label { Text = T("Обновления Battery Monitor"), AutoSize = true, Font = new Font("Segoe UI Semibold", 16f), Margin = new Padding(0,0,0,12) });
        flow.Controls.Add(new Label { Text = T("Установленная версия:") + " " + UpdateClient.CurrentVersion, AutoSize = true, Font = new Font("Segoe UI Semibold", 15f), Margin = new Padding(0,0,0,12) });
        _status = new Label { Text = UpdateClient.IsConfigured ? T("Нажмите «Проверить обновления».") : T("Источник обновлений ещё не настроен издателем."), AutoSize = true, MaximumSize = new Size(540,0), ForeColor = SettingsWindow.Muted, Margin = new Padding(0,0,0,12) };
        flow.Controls.Add(_status);
        _lastCheck = new Label { AutoSize = true, MaximumSize = new Size(540,0), ForeColor = SettingsWindow.Muted, Margin = new Padding(0,0,0,12) };
        flow.Controls.Add(_lastCheck);
        RefreshLastCheck();
        VisibleChanged += (_, _) => { if (Visible) RefreshLastCheck(); };
        _check = SettingsWindow.MakeButton(T("Проверить обновления"), 280, true);
        _download = SettingsWindow.MakeButton(T("Скачать и установить"), 280);
        _check.Enabled = UpdateClient.IsConfigured; _download.Enabled = false;
        _check.Click += async (_, _) => await CheckAsync();
        _download.Click += async (_, _) => await DownloadAsync();
        flow.Controls.Add(_check); flow.Controls.Add(_download);
        var releases = SettingsWindow.MakeButton(T("Открыть релизы GitHub"), 280);
        releases.Enabled = UpdateClient.ReleasesPage != null;
        releases.Click += (_, _) =>
        {
            try
            {
                if (UpdateClient.ReleasesPage is { } uri)
                    using (Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })) { }
            }
            catch (Exception ex) { ShowError(ex); }
        };
        flow.Controls.Add(releases);
        flow.Controls.Add(new Label { Text = T("Что нового"), AutoSize = true, Font = new Font("Segoe UI Semibold", 11f), Margin = new Padding(0,12,0,4) });
        _notes = new TextBox { ReadOnly = true, Multiline = true, WordWrap = true, ScrollBars = ScrollBars.Vertical, Width = 540, Height = 112, BackColor = SettingsWindow.Canvas, ForeColor = SettingsWindow.Ink, Text = T("Нет доступного обновления.") };
        flow.Controls.Add(_notes);
        _progress = new ProgressBar { Width = 280, Height = 12, Visible = false, Margin = new Padding(0,8,0,8) };
        flow.Controls.Add(_progress);
        var automatic = new CheckBox { Text = T("Проверять обновления при запуске"), AutoSize = true, Checked = UpdatePreferences.AutoCheck, Enabled = UpdateClient.IsConfigured };
        automatic.CheckedChanged += (_, _) =>
        {
            try { UpdatePreferences.AutoCheck = automatic.Checked; }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Battery Monitor"); }
        };
        flow.Controls.Add(automatic);
        flow.Controls.Add(new Label { Text = T("Установка только после подтверждения. Настройка проверки сохраняется сразу."), AutoSize = true, MaximumSize = new Size(540,0), ForeColor = SettingsWindow.Muted });
        card.Controls.Add(flow); Controls.Add(card);
    }
    private void RefreshLastCheck()
    {
        if (IsDisposed || Disposing) return;
        var culture = CultureInfo.GetCultureInfo(Active.Language switch { "uk" => "uk-UA", "en" => "en-US", _ => "ru-RU" });
        _lastCheck.Text = T("Последняя успешная проверка:") + " " + (UpdatePreferences.LastSuccessfulCheck?.ToLocalTime().ToString("g", culture) ?? T("Ещё не выполнялась"));
    }
    private void SetBusy(bool busy)
    {
        _busy = busy;
        if (IsDisposed || Disposing) return;
        _check.Enabled = !busy && UpdateClient.IsConfigured;
        _download.Enabled = !busy && _release != null;
    }
    private async Task CheckAsync()
    {
        if (_busy) return;
        _release = null; _notes.Text = T("Нет доступного обновления."); SetBusy(true); _status.Text = T("Проверяем обновления…");
        try
        {
            var release = await UpdateClient.CheckAsync(_lifetime.Token);
            if (IsDisposed || Disposing) return;
            _release = release;
            RefreshLastCheck();
            _notes.Text = release == null ? T("Нет доступного обновления.") : string.IsNullOrWhiteSpace(release.Notes) ? T("Описание изменений не предоставлено.") : release.Notes;
            _status.Text = release == null ? T("Установлена актуальная версия.") : T("Доступна версия:") + " " + release.Version + $" ({release.Size / 1048576.0:F1} MB)";
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); }
    }
    private async Task DownloadAsync()
    {
        if (_busy || _release is not { } release) return;
        if (MessageBox.Show(T("Скачать проверенное обновление?") + " " + release.Version, "Battery Monitor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        SetBusy(true); _progress.Value = 0; _progress.Visible = true; _status.Text = T("Скачиваем обновление…");
        string? path = null;
        try
        {
            path = await UpdateClient.DownloadAsync(release, new Progress<int>(value => { if (!IsDisposed && !Disposing) _progress.Value = Math.Clamp(value,0,100); }), _lifetime.Token);
            if (IsDisposed || Disposing) return;
            _status.Text = T("Подпись и контрольная сумма проверены.");
            if (MessageBox.Show(T("Обновление проверено. Закрыть Battery Monitor и открыть установщик?"), "Battery Monitor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _install(path, release);
            path = null; // The main process now owns the verified download.
        }
        catch (Exception ex) { ShowError(ex); }
        finally
        {
            if (path != null) UpdateClient.DeleteDownload(path);
            if (!IsDisposed && !Disposing) _progress.Visible = false;
            SetBusy(false);
        }
    }
    private void ShowError(Exception ex)
    {
        if (IsDisposed || Disposing) return;
        _status.Text = (ex is CryptographicException ? T("Проверка безопасности не пройдена. Установка отменена.") : T("Не удалось получить обновление.")) + "\n" + ex.Message;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed) { _lifetime.Cancel(); _lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}
