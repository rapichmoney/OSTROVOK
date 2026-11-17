namespace DesktopIsland;

public partial class MainWindow
{
    private void UpdateIdleClockHands(DateTime time)
    {
        double seconds = time.Second + time.Millisecond / 1000d;
        IdleSecondHand.Angle = (Motion.Enabled ? seconds : time.Second) * 6;
        IdleMinuteHand.Angle = (time.Minute + seconds / 60) * 6;
        IdleHourHand.Angle = (time.Hour % 12 + time.Minute / 60d + seconds / 3600d) * 30;
    }

    private void OpenCompactCalendar(object sender, RoutedEventArgs e)
    {
        Navigation.SelectedIndex = 5; SetExpanded(true); e.Handled = true;
    }
}
