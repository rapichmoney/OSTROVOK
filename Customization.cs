using System.Windows.Controls;

namespace DesktopIsland;

public partial class MainWindow
{
    internal static readonly (int Id, string Title)[] CustomTabs = [(1,"Файлы"),(2,"Буфер"),(3,"Заметки"),(4,"Задачи"),(5,"Календарь"),(6,"Загрузки"),(7,"Монитор"),(8,"События"),(10,"Погода"),(11,"Уведомления")];
    private bool TabEnabled(int id) => id == 0 || !settings.HiddenTabs.Contains(id);
    internal void ApplyTabVisibility()
    {
        settings.HiddenTabs = Services.DisplayLayout.NormalizeHidden(settings.HiddenTabs);
        foreach (var (id, item) in toolbar) item.Button.Visibility = TabEnabled(id) ? Visibility.Visible : Visibility.Collapsed;
        if (!TabEnabled(Navigation.SelectedIndex)) Navigation.SelectedIndex = 0;
        animatedTab = -1; UpdateToolbar();
    }
}
