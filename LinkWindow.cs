using System.Windows.Controls;

namespace DesktopIsland;

internal sealed class LinkWindow : Window
{
    private readonly TextBox input = new() { MinWidth = 300 };
    public string Address => input.Text.Trim();
    public LinkWindow()
    {
        Title = "Добавить ссылку"; Width = 390; Height = 190; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = System.Windows.Media.Brushes.Black;
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Адрес HTTP или HTTPS", Margin = new Thickness(0, 0, 0, 12) }); panel.Children.Add(input);
        var error = new TextBlock { Foreground = System.Windows.Media.Brushes.LightGray, FontSize = 11, Margin = new Thickness(0, 6, 0, 6) }; panel.Children.Add(error);
        var button = new Button { Content = "Добавить", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        button.Click += (_, _) => { if (ShelfItem.IsWebLink(Address)) DialogResult = true; else error.Text = "Введите адрес, начинающийся с http:// или https://"; };
        panel.Children.Add(button); Content = panel; Loaded += (_, _) => input.Focus();
    }
}
