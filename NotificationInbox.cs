using System.Diagnostics;
using System.Windows.Controls;
using DesktopIsland.Services;

namespace DesktopIsland;

public partial class MainWindow
{
    private readonly ObservableCollection<TelegramMessage> notificationHistory = new();
    private TelegramMessage? currentSocialNotice;
    private bool CurrentNoticeIsTelegram => currentSocialNotice?.SourceId == "telegram";
    private string CurrentSocialName => currentSocialNotice?.SourceName ?? "Telegram";
    private string CurrentSocialIcon => NotificationSources.Find(currentSocialNotice?.SourceId ?? "telegram")?.Icon ?? NotificationSources.Bell;

    private void InitializeNotificationInbox()
    {
        NotificationList.ItemsSource = notificationHistory;
        NotificationFilter.Items.Add("Все источники");
        foreach (var source in NotificationSources.All) NotificationFilter.Items.Add(source.Name);
        NotificationFilter.Items.Add("Другие приложения Windows");
        NotificationFilter.SelectedIndex = 0;
        telegramNotifications.SourceEnabled = (source, appId) => NotificationSources.Enabled(settings, source, appId);
        UpdateNotificationPresentation();
    }

    private void RememberNotification(TelegramMessage message)
    {
        notificationHistory.Insert(0, message);
        while (notificationHistory.Count > 100) notificationHistory.RemoveAt(notificationHistory.Count - 1);
        RefreshNotificationFilter();
    }

    private void UpdateNotificationPresentation()
    {
        var palette = SettingsPalette;
        TelegramNotice.Background = new System.Windows.Media.SolidColorBrush(palette.Bottom);
        TelegramNotice.BorderBrush = new System.Windows.Media.SolidColorBrush(palette.Rim);
    }

    private void NotificationFilterChanged(object sender, SelectionChangedEventArgs e) => RefreshNotificationFilter();
    private void RefreshNotificationFilter()
    {
        if (NotificationList == null || NotificationFilter == null) return;
        string? source = NotificationFilter.SelectedIndex > 0 && NotificationFilter.SelectedIndex <= NotificationSources.All.Length
            ? NotificationSources.All[NotificationFilter.SelectedIndex - 1].Id : NotificationFilter.SelectedIndex > NotificationSources.All.Length ? "windows" : null;
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(notificationHistory);
        view.Filter = source == null ? null : item => item is TelegramMessage message && message.SourceId == source;
        NotificationEmpty.Visibility = view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearNotificationHistory(object sender, RoutedEventArgs e)
    {
        notificationHistory.Clear(); RefreshNotificationFilter();
    }
    private void OpenNotificationSettings(object sender, RoutedEventArgs e) => OpenPreferences(7);
    private void OpenSelectedNotification(object sender, RoutedEventArgs e)
    {
        if (NotificationList.SelectedItem is TelegramMessage message) OpenNotificationSource(message);
    }
    private void OpenNotificationSource(TelegramMessage? message)
    {
        var source = NotificationSources.Find(message?.SourceId ?? "telegram");
        if (source == null)
        {
            ShowNotificationInbox();
            return;
        }
        try { Process.Start(new ProcessStartInfo(source.Address) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { Status.Text = "Не удалось открыть " + source.Name + ". Откройте источник вручную."; }
    }

    internal WindowsNotificationApp[] NotificationApplications => telegramNotifications.ObservedApplications;
    internal void ShowNotificationInbox() { settings.HiddenTabs = settings.HiddenTabs.Where(x => x != 11).ToArray(); ApplyTabVisibility(); Reveal(); Navigation.SelectedIndex = 11; }
    internal void SetNotificationSource(string source, bool enabled)
    {
        settings.NotificationSources = enabled ? settings.NotificationSources.Append(source).Distinct().ToArray() : settings.NotificationSources.Where(x => x != source).ToArray();
        PruneNotificationSources(); SaveSettings();
    }
    internal void SetAdditionalNotificationApp(string appId, bool enabled)
    {
        settings.AdditionalNotificationApps = enabled ? settings.AdditionalNotificationApps.Append(appId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : settings.AdditionalNotificationApps.Where(x => !x.Equals(appId, StringComparison.OrdinalIgnoreCase)).ToArray();
        PruneNotificationSources(); SaveSettings();
    }
    private void PruneNotificationSources()
    {
        foreach (var message in notificationHistory.Where(x => !NotificationSources.Enabled(settings, x.SourceId, x.AppId)).ToArray()) notificationHistory.Remove(message);
        var kept = telegramQueue.Where(x => NotificationSources.Enabled(settings, x.SourceId, x.AppId)).ToArray();
        telegramQueue.Clear(); foreach (var message in kept) telegramQueue.Enqueue(message);
        if (currentSocialNotice != null && !NotificationSources.Enabled(settings, currentSocialNotice.SourceId, currentSocialNotice.AppId)) ShowNextTelegramNotice();
        RefreshNotificationFilter();
    }
}
