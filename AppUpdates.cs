using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media;
using DesktopIsland.Services;

namespace DesktopIsland;

public partial class MainWindow
{
    private readonly AppUpdateService appUpdates = new();
    private AppRelease? availableRelease;
    private bool checkingUpdate, installingUpdate;
    private DateTime nextUpdateCheck = DateTime.UtcNow.AddSeconds(8), postponeUpdateUntil;
    private string updateDetail = "", updateStatus = "Проверка обновлений ещё не выполнялась";
    private bool UpdateNoticeActive => availableRelease != null && (installingUpdate || DateTime.UtcNow >= postponeUpdateUntil && !telegramNoticeActive && !clipboardNoticeActive);
    internal string UpdateStatus => updateStatus;
    internal async Task CheckAppUpdatesAsync(bool manual = false)
    {
        if (App.VerificationMode || checkingUpdate || installingUpdate || closing || !manual && !settings.CheckUpdates) return;
        checkingUpdate = true; nextUpdateCheck = DateTime.UtcNow.AddHours(4);
        try
        {
            availableRelease = await appUpdates.CheckAsync();
            updateStatus = availableRelease == null ? "Установлена последняя версия" : "Доступна OSTROVOK " + availableRelease.Version;
            if (manual) postponeUpdateUntil = DateTime.MinValue;
            updateDetail = "Настройки сохранятся";

            UpdateCompactNotification(true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
        { updateStatus = "Не удалось проверить обновления. Повторите позже."; }
        finally { checkingUpdate = false; }
    }
    private void RenderUpdateNotice()
    {
        if (compactNoticeSignature != "update")
        {
            StopTelegramPlaneFlight();
            CompactCardGhostIcon.BeginAnimation(OpacityProperty, null);
            CompactCardGhostIcon.Opacity = 0;
        }
        compactNotificationWidth = Math.Min(530, Services.WindowInterop.WorkArea(handle).Width - 16);
        string title = installingUpdate ? "Обновляем OSTROVOK" : "Обновление " + availableRelease!.Version;
        if (CompactCardTitle.Text != title) PolishedMotion.Swap(CompactCardTitle, () => CompactCardTitle.Text = title, 280, 0);
        CompactCardText.Text = updateDetail; CompactCardText.ToolTip = updateDetail;
        CompactCardIcon.Data = Geometry.Parse("M 12,2 L 12,17 M 6,11 L 12,17 L 18,11 M 3,20 L 3,23 L 21,23 L 21,20");
        CompactCardIcon.Stroke = (Brush)FindResource("Ink"); CompactCardIcon.Fill = Brushes.Transparent;
        CompactCardIcon.Opacity = 1; CompactCardIcon.StrokeDashArray = null; CompactCardIcon.RenderTransform = Transform.Identity;
        CompactCardAction.Content = installingUpdate ? "Установка…" : "Обновить"; CompactCardAction.IsEnabled = !installingUpdate;
        UpdateLaterButton.Visibility = Visibility.Visible; UpdateLaterButton.IsEnabled = !installingUpdate;
        NoticeQueueCount.Text = ""; NoticeLifetime.Visibility = Visibility.Collapsed;
        compactNoticeSignature = "update";
    }
    private void PostponeAppUpdate(object sender, RoutedEventArgs e)
    {
        postponeUpdateUntil = DateTime.UtcNow.AddHours(24); UpdateCompactNotification(true);
    }
    internal async Task InstallAppUpdateAsync()
    {
        if (installingUpdate || availableRelease == null || App.VerificationMode) return;
        installingUpdate = true; UpdateCompactNotification(true);
        try
        {
            string target = Path.GetDirectoryName(Environment.ProcessPath!)!;
            string test = Path.Combine(target, ".ostrovok-write-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(test, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            int last = -1;
            var progress = new Progress<int>(percent =>
            {
                if (percent == last || closing) return; last = percent;
                updateDetail = percent < 100 ? "Скачивание: " + percent + "%" : "Проверяем архив и версию приложения…";
                UpdateCompactNotification(false);
            });
            string stage = await appUpdates.DownloadAsync(availableRelease, progress);
            string folder = Directory.GetParent(stage)!.FullName;
            string script = Path.Combine(folder, "apply-update.ps1"), plan = Path.Combine(folder, "plan.json");
            using (var resource = typeof(App).Assembly.GetManifestResourceStream("DesktopIsland.tools.apply-update.ps1")!)
            using (var file = File.Create(script)) resource.CopyTo(file);
            using var own = Process.GetCurrentProcess();
            File.WriteAllText(plan, JsonSerializer.Serialize(new { stage, target, pid = own.Id, started = own.StartTime.ToUniversalTime().ToString("O"), version = availableRelease.Version.ToString(), restart = true }));
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-PlanFile", plan }) start.ArgumentList.Add(argument);
            using var helper = Process.Start(start) ?? throw new IOException("Не запущен установщик");
            updateDetail = "Перезапускаем приложение…"; UpdateCompactNotification(false);
            preferences?.Close(); SaveSettings(); Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or System.ComponentModel.Win32Exception or InvalidOperationException or InvalidDataException)
        {
            installingUpdate = false; updateDetail = "Не удалось установить. Проверьте соединение и доступ к папке приложения.";
            updateStatus = updateDetail; UpdateCompactNotification(true);
        }
    }
}
