using System.Windows.Media;
using DesktopIsland.Services;

namespace DesktopIsland;

public partial class MainWindow
{
    private bool settingUp;
    private string connectionSummary = "Подключения ещё не проверены";
    internal string ConnectionSummary => connectionSummary;
    internal async Task AutoConnectAsync(bool manual)
    {
        if (App.VerificationMode || settingUp || closing || (!manual && !settings.AutoSetup)) return;
        settingUp = true;
        try
        {
            audio.Refresh(); await audio.RefreshDeviceNameAsync(); RefreshVolume();
            var states = new List<string> { audio.Available ? "Системный звук подключён" : "Windows не предоставила устройство звука" };
            var native = await yandexLyrics.PlaybackAsync();
            if (native != null)
            { yandexPlayback = native; yandexStateAt = Environment.TickCount64; media.UpdateNativePlayback(native); UpdateMedia(); states.Add("Яндекс Музыка подключена"); }
            else
            {
                if (settings.SetupVersion < 1 || manual) YandexLaunchSetup.Install();
                states.Add("Для управления Яндекс Музыкой используйте кнопку подключения в разделе «Музыка»");
            }
            if (TelegramNotificationService.HasIdentity && (manual || settings.SetupVersion < 1 || settings.TelegramNotificationsEnabled))
            {
                bool first = settings.SetupVersion < 1;
                string result = await telegramNotifications.ConnectAsync(first || manual);
                settings.TelegramNotificationsEnabled = telegramNotifications.Connected;
                states.Add(result);
            }
            else states.Add(TelegramNotificationService.HasIdentity ? "Уведомления отключены в настройках" : "Уведомления: подключите пакет Windows в разделе «Данные»");
            connectionSummary = string.Join(". ", states);
            if (settings.SetupVersion < 1 || manual)
            {
                AddActivity("Подключения проверены", connectionSummary);
                ShowClipboardNotice(new ClipboardEntry(), "Подключения OSTROVOK", audio.Available ? "Звук подключён. Состояние остальных подключений — в настройках системы." : "Проверьте подключения в настройках системы.", 7);
            }
            settings.SetupVersion = 1; SaveSettings();
        }
        catch (Exception ex) when (YandexLaunchSetup.IsRecoverable(ex))
        { App.Diagnostics?.Error(DiagnosticArea.Connections, ex); connectionSummary = "Не все подключения доступны. Проверьте разделы «Музыка» и «Данные»."; AddActivity("Проверка подключений", connectionSummary); }
        finally { settingUp = false; }
    }
    private void RefreshGamepad()
    {
        string text = settings.ShowGamepadBattery ? GamepadBattery.Read() : "";
        GamepadChargeText.Text = text;
        GamepadChargeText.Visibility = expanded && text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        HomeDate.ToolTip = text.Length > 0 ? text : null;
    }
}
