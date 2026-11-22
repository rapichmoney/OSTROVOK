using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DesktopIsland;

public partial class MainWindow
{
    private AnimationClock? noticeLifetimeClock;
    private void UpdateNotificationQueue()
    {
        bool queued = telegramNoticeActive && telegramQueue.Count > 0;
        NoticeQueueCount.Text = queued ? "ещё " + telegramQueue.Count : "";
        CompactCardAction.VerticalAlignment = queued ? VerticalAlignment.Top : VerticalAlignment.Center;
        CompactCardAction.Margin = new Thickness(12, 0, 0, 0);
    }
    private void StartNoticeLifetime(TimeSpan duration)
    {
        NoticeLifetimeScale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, null);
        NoticeLifetimeScale.ScaleX = 1;
        noticeLifetimeClock = null;
        NoticeLifetime.Visibility = Motion.Enabled && !expanded ? Visibility.Visible : Visibility.Collapsed;
        if (!Motion.Enabled) return;
        noticeLifetimeClock = new DoubleAnimation(1, 0, duration) { FillBehavior = FillBehavior.HoldEnd }.CreateClock();
        NoticeLifetimeScale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, noticeLifetimeClock);
    }
}
