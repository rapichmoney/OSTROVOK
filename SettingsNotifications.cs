using System.Windows.Controls;
using System.Windows.Media;
using DesktopIsland.Services;

namespace DesktopIsland;

internal sealed partial class SettingsWindow
{
    private void NotificationPage(StackPanel content)
    {
        Hint(content, "Приём через Windows. Пароли и токены соцсетей не нужны. В самих приложениях должны быть включены системные уведомления.");
        var status = new TextBlock { Text = island.TelegramNotificationStatus, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) };
        status.SetResourceReference(TextBlock.ForegroundProperty, "Secondary"); content.Children.Add(status);
        var connect = new Button { Content = "Подключить уведомления Windows", HorizontalAlignment = HorizontalAlignment.Left };
        connect.Click += async (_, _) =>
        {
            connect.IsEnabled = false; status.Text = "Подключаем…";
            try { status.Text = await island.ConnectTelegramNotificationsAsync(); }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.ExternalException or UnauthorizedAccessException)
            { status.Text = "Не удалось подключиться. Проверьте разрешение Windows."; }
            finally { connect.IsEnabled = true; }
        };
        content.Children.Add(connect);
        Action(content, "Разрешения Windows", () =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:privacy-notifications") { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception) { status.Text = "Откройте Параметры Windows → Конфиденциальность → Уведомления."; }
        });
        Action(content, "Отключить приём и очистить историю", () => { island.DisableTelegramNotifications(); status.Text = island.TelegramNotificationStatus; });
        Section(content, "Соцсети и мессенджеры");
        foreach (var source in NotificationSources.All)
        {
            string id = source.Id;
            SourceToggle(content, source.Name, settings.NotificationSources.Contains(id), enabled => island.SetNotificationSource(id, enabled), NotificationBrandIcons.Get(id));
        }
        Hint(content, "Переключатель разрешает источник, но не входит в аккаунт. При отключении источника его сообщения удаляются из ленты и очереди острова.");
        Section(content, "Другие приложения Windows");
        Hint(content, "Браузер может передавать уведомления разных сайтов под одним именем. Выбор браузера разрешает все его системные уведомления; источник сайта не подменяется названием соцсети.");
        var apps = island.NotificationApplications.Where(x => NotificationSources.Identify(x.Id, x.Name) == null).ToList();
        foreach (string id in settings.AdditionalNotificationApps.Where(id => apps.All(x => !x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))) apps.Add(new(id, "Сохранённое приложение Windows"));
        if (apps.Count == 0) Hint(content, "Список появится после системного уведомления. Затем нажмите «Обновить список».");
        foreach (var app in apps)
        {
            string id = app.Id;
            SourceToggle(content, app.Name, settings.AdditionalNotificationApps.Contains(id, StringComparer.OrdinalIgnoreCase), enabled => island.SetAdditionalNotificationApp(id, enabled), app.Icon);
        }
        Action(content, "Обновить список", BuildPage);
        Action(content, "Открыть ленту уведомлений", () => { Hide(); island.ShowNotificationInbox(); });
        Hint(content, "До 100 сообщений в памяти. История очищается при закрытии приложения и не передаётся в переводчик или Discord. Уже имеющиеся в центре уведомлений сообщения повторно не показываются.");
    }

    private void SourceToggle(StackPanel parent, string title, bool enabled, Action<bool> set, ImageSource? icon)
    {
        Toggle(parent, title, enabled, set);
        var check = (CheckBox)parent.Children[parent.Children.Count - 1];
        System.Windows.Automation.AutomationProperties.SetName(check, title);
        if (icon == null) return;
        check.Tag = "NotificationSource";
        var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(new Image { Source = icon, Width = 24, Height = 24, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 10, 0) });
        label.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        check.Content = label;
    }
}
