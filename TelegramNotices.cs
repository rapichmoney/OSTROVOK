using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopIsland.Services;

namespace DesktopIsland;

public partial class MainWindow
{
    private readonly TelegramNotificationService telegramNotifications = new();
    private readonly Queue<TelegramMessage> telegramQueue = new();
    private readonly DispatcherTimer telegramNoticeTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool telegramNoticeActive;

    private void InitializeTelegramNotices()
    {
        CompactCard.IsVisibleChanged += (_, _) =>
        {
            if (!CompactCard.IsVisible) { if (telegramNoticeActive || planeFlying) StopTelegramPlaneFlight(); return; }
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { if (!closing && telegramNoticeActive && CompactCard.IsVisible && !planeFlying) FlyTelegramPlane(); }));
        };
        telegramNotifications.Received += ReceiveTelegramNotification;
        telegramNoticeTimer.Tick += DismissTelegramNotice;
    }

    private void ReceiveTelegramNotification(TelegramMessage message)
    {
        if (closing || !settings.TelegramNotificationsEnabled) return;
        if (telegramQueue.Count >= 20) telegramQueue.Dequeue();
        telegramQueue.Enqueue(message);
        if (!telegramNoticeActive) ShowNextTelegramNotice();
        else UpdateNotificationQueue();
    }

    private void ShowNextTelegramNotice()
    {
        telegramNoticeTimer.Stop();
        telegramNoticeActive = telegramQueue.TryDequeue(out var next);
        if (next != null)
        {
            TelegramNoticeTitle.Text = next.Title;
            TelegramNoticeText.Text = next.Body.Length > 1200 ? next.Body[..1200] + "…" : next.Body;
            CompactTelegramText.Text = "Telegram · " + next.Title + (next.Body.Length > 0 ? ": " + next.Body.Replace('\n', ' ') : "");
            telegramNoticeTimer.Start();
            Motion.Enter(CompactTelegramText, 5, 220); Motion.Enter(TelegramNoticeText, 5, 220);
        }
        else TelegramNoticeTitle.Text = TelegramNoticeText.Text = CompactTelegramText.Text = "";
        UpdateClipboardNoticeVisibility();
    }

    private void DismissTelegramNotice(object? sender, EventArgs e) => ShowNextTelegramNotice();

    private void UpdateTelegramNoticeVisibility()
    {
        TelegramNotice.Visibility = telegramNoticeActive && expanded ? Visibility.Visible : Visibility.Collapsed;
        CompactTelegramHost.Visibility = telegramNoticeActive && !expanded ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenTelegram(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("tg://") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Status.Text = "Не удалось открыть Telegram. Откройте приложение вручную."; }
    }
    private void OpenTelegramNotice(object sender, MouseButtonEventArgs e) { OpenTelegram(sender, e); e.Handled = true; }

    internal string TelegramNotificationStatus => telegramNotifications.Status;
    internal async Task<string> ConnectTelegramNotificationsAsync()
    {
        if (!TelegramNotificationService.HasIdentity)
        {
            string script = Path.Combine(AppContext.BaseDirectory, "Package", "Register.ps1");
            if (!File.Exists(script)) return "Нет пакета Windows. Откройте приложение из папки release или полного архива.";
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string value in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-AppFolder", AppContext.BaseDirectory }) start.ArgumentList.Add(value);
            using var process = Process.Start(start)!;
            Task<string> error = process.StandardError.ReadToEndAsync(); Task<string> output = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync(); await output; await error;
            if (process.ExitCode != 0) return "Не удалось зарегистрировать пакет Windows. Для этого компьютера нужна подписанная сборка; настройки безопасности не изменялись.";
            settings.TelegramNotificationsEnabled = true; SaveSettings();
            RestartForTelegram();
            return "Пакет подключён. Перезапускаем остров…";
        }
        string status = await telegramNotifications.ConnectAsync(true);
        settings.TelegramNotificationsEnabled = telegramNotifications.Connected; SaveSettings();
        return status;
    }

    private void RestartForTelegram()
    {
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.Environment["DESKTOPISLAND_RESTART_PATH"] = Environment.ProcessPath!;
        foreach (string value in new[] { "-NoProfile", "-NonInteractive", "-Command", "Wait-Process -Id " + Environment.ProcessId + " -Timeout 15 -ErrorAction SilentlyContinue; if (!(Get-Process -Id " + Environment.ProcessId + " -ErrorAction SilentlyContinue)) { Start-Process -FilePath $env:DESKTOPISLAND_RESTART_PATH -ArgumentList '--connect-telegram' -WindowStyle Hidden }" }) start.ArgumentList.Add(value);
        Process.Start(start); Close();
    }

    internal void DisableTelegramNotifications()
    {
        settings.TelegramNotificationsEnabled = false; telegramNotifications.Disconnect(); telegramQueue.Clear();
        ShowNextTelegramNotice(); SaveSettings();
    }
}
