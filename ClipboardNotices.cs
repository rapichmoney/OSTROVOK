using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DesktopIsland.Services;

namespace DesktopIsland;

public partial class MainWindow
{
    private readonly TranslationService translator = new();
    private readonly ClipboardTranslationGate translationGate = new();
    private string? verificationClipboardText;
    private readonly DispatcherTimer clipboardNoticeTimer = new();
    private CancellationTokenSource? translationCancellation;
    private ClipboardEntry? noticeEntry;
    private bool clipboardNoticeActive;
    private int lastNonClipboardTab;
    private int? clipboardReturnTab;
    private bool navigatingForClipboard;

    private void InitializeClipboardNotices() => clipboardNoticeTimer.Tick += DismissClipboardNotice;

    private void HandleCopiedText(ClipboardEntry entry)
    {
        copiedNoticeFiles = [];
        StopClipboardTranslation(); noticeEntry = entry;
        string? target = translationGate.SelectTarget(entry.Text, settings.AutoTranslateClipboard);
        bool translate = target != null;
        entry.TranslationState = translate ? target == "en" ? "Переводим на английский…" : "Переводим на русский…" : entry.Text.Length > 4000 && settings.AutoTranslateClipboard ? "Перевод доступен для текста до 4 000 символов" : "";
        ShowClipboardNotice(entry, "Текст скопирован", entry.Text, 5);
        if (translate)
        {
            translationCancellation = new CancellationTokenSource();
            _ = TranslateClipboardAsync(entry, translationCancellation.Token, target!);
        }
    }

    private async Task TranslateClipboardAsync(ClipboardEntry entry, CancellationToken token, string target = "ru")
    {
        try
        {
            await Task.Delay(400, token);
            if (ReferenceEquals(noticeEntry, entry)) ShowClipboardNotice(entry, target == "en" ? "Переводим на английский…" : "Переводим на русский…", entry.Text, 45, busy: true);
            var result = await translator.TranslateAsync(entry.Text, token, target);
            if (closing || token.IsCancellationRequested || !settings.AutoTranslateClipboard || !settings.ClipboardEnabled) return;
            if (result.Language == target) { entry.TranslationState = ""; ShowClipboardNotice(entry, "Текст скопирован", entry.Text, 5); return; }
            entry.Translation = result.Text; entry.TranslationState = (target == "en" ? "Перевод на английский · " : "Перевод на русский · ") + result.Provider;
            if (target == "ru") translationGate.Complete(target, result.Language);
            if (ReferenceEquals(noticeEntry, entry))
            {
                if (target == "en") await CopyReverseTranslationAsync(entry, token);
                else ShowClipboardNotice(entry, "Перевод на русский", result.Text, 5);
            }
        }
        catch (OperationCanceledException) { if (!token.IsCancellationRequested) TranslationFailed(entry, "Сервис не ответил вовремя. Исходный текст сохранён."); }
        catch (TranslationUnavailableException ex) { if (!token.IsCancellationRequested) TranslationFailed(entry, ex.Message); }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException or IndexOutOfRangeException)
        { if (!token.IsCancellationRequested) TranslationFailed(entry); }
    }

    private async Task CopyReverseTranslationAsync(ClipboardEntry entry, CancellationToken token)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(noticeEntry, entry) || closing) return;
            try
            {
                string current = App.VerificationMode ? verificationClipboardText ?? "" : System.Windows.Clipboard.GetText();
                if (current != entry.Text)
                {
                    ShowClipboardNotice(entry, "Перевод на английский готов", "Буфер уже изменён. Перевод доступен в истории.", 5); return;
                }
                string previous = lastClipboard; lastClipboard = entry.Translation;
                try
                {
                    if (App.VerificationMode) verificationClipboardText = entry.Translation;
                    else System.Windows.Clipboard.SetText(entry.Translation);
                }
                catch { lastClipboard = previous; throw; }
                translationGate.Reset();
                ShowClipboardNotice(entry, "На английский · скопировано", entry.Translation, 5); return;
            }
            catch (System.Runtime.InteropServices.ExternalException) { await Task.Delay(80, token); }
        }
        entry.TranslationState += " · буфер занят";
        ShowClipboardNotice(entry, "Перевод на английский готов", "Буфер занят. Скопируйте перевод из истории.", 7);
    }

    private void TranslationFailed(ClipboardEntry entry, string reason = "Перевод недоступен. Исходный текст сохранён.")
    {
        if (closing) return;
        entry.TranslationState = reason;
        if (ReferenceEquals(noticeEntry, entry)) ShowClipboardNotice(entry, "Не удалось перевести", reason, 7);
    }

    private void ShowClipboardNotice(ClipboardEntry entry, string title, string text, int seconds, bool busy = false)
    {
        if (!settings.ClipboardNotifications) return;
        noticeEntry = entry; clipboardNoticeActive = true;
        if (entry.Translation.Length > 0 && Navigation.SelectedIndex == 2 && clipboardReturnTab == null) clipboardReturnTab = lastNonClipboardTab;
        ClipboardNoticeTitle.Text = title; ClipboardNoticeText.Text = text.Length > 1200 ? text[..1200] + "…" : text;
        CompactNotice.Text = entry.Translation.Length > 0 ? "Перевод: " + entry.Translation.Replace('\n', ' ') : title;
        CompactNotice.ToolTip = ClipboardNoticeText.Text;
        CopyNoticeTranslation.Visibility = entry.Translation.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        clipboardNoticeTimer.Stop(); clipboardNoticeTimer.Interval = TimeSpan.FromSeconds(seconds); clipboardNoticeTimer.Start();
        AnimateClipboardNotice(busy, entry.Translation.Length > 0);
        UpdateClipboardNoticeVisibility();
    }

    private void AnimateClipboardNotice(bool busy, bool translated)
    {
        StopNoticeIndicator();
        string path = busy ? "M 12,2 A 10,10 0 1 1 2,12" : translated ? "M 2,5 L 15,5 M 8,2 L 8,5 M 5,5 C 5,11 9,15 14,17 M 12,5 C 12,11 8,15 2,17 M 14,22 L 19,10 L 24,22 M 16,18 L 22,18" : "M 7,7 L 21,7 L 21,23 L 7,23 Z M 4,17 L 2,17 L 2,2 L 16,2 L 16,4";
        foreach (var icon in new[] { CompactNoticeIcon, ClipboardNoticeIcon })
        {
            icon.Data = Geometry.Parse(path);
            if (busy)
            {
                var rotation = new RotateTransform(); icon.RenderTransform = rotation;
                if (Motion.Enabled) rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(850)) { RepeatBehavior = RepeatBehavior.Forever });
            }
            else Motion.Pop(icon, .68, 460);
        }
        Motion.Enter(CompactNotice, 5, 220);
        Motion.Enter(ClipboardNoticeText, 5, 220);
    }

    private void StopNoticeIndicator()
    {
        foreach (var icon in new[] { CompactNoticeIcon, ClipboardNoticeIcon, CompactCardIcon })
        {
            if (icon.RenderTransform is RotateTransform rotation) rotation.BeginAnimation(RotateTransform.AngleProperty, null);
            icon.RenderTransform = Transform.Identity;
        }
    }

    private void UpdateClipboardNoticeVisibility(bool resizeShell = true)
    {
        if (CompactNotice == null) return;
        UpdateCompactNotification(resizeShell);
        bool active = clipboardNoticeActive && settings.ClipboardNotifications && !telegramNoticeActive;
        CompactNoticeHost.Visibility = active && !expanded ? Visibility.Visible : Visibility.Collapsed;
        CompactTrack.Visibility = !expanded && !active && !telegramNoticeActive ? Visibility.Visible : Visibility.Collapsed;
        ClipboardNotice.Visibility = active && expanded ? Visibility.Visible : Visibility.Collapsed;
        UpdateTelegramNoticeVisibility();
    }

    private void DismissClipboardNotice(object? sender, EventArgs e)
    {
        clipboardNoticeTimer.Stop(); clipboardNoticeActive = false; StopNoticeIndicator(); UpdateClipboardNoticeVisibility();
        int? returnTab = clipboardReturnTab; clipboardReturnTab = null;
        if (!closing && returnTab is int tab && Navigation.SelectedIndex == 2) Navigation.SelectedIndex = tab;
    }

    private void OpenClipboardNotice(object sender, MouseButtonEventArgs e)
    {
        if (clipboardNoticeActive && copiedNoticeFiles.Length > 0) { SetExpanded(true); Navigation.SelectedIndex = 1; e.Handled = true; return; }
        clipboardReturnTab ??= Navigation.SelectedIndex == 2 ? lastNonClipboardTab : Navigation.SelectedIndex;
        navigatingForClipboard = true;
        try { SetExpanded(true); Navigation.SelectedIndex = 2; }
        finally { navigatingForClipboard = false; }
        if (noticeEntry?.Translation.Length > 0) { clipboardNoticeTimer.Stop(); clipboardNoticeTimer.Interval = TimeSpan.FromSeconds(5); clipboardNoticeTimer.Start(); }
        e.Handled = true;
    }

    private void CopyCurrentTranslation(object sender, RoutedEventArgs e) => CopyTranslation(noticeEntry);
    private void CopySelectedTranslation(object sender, RoutedEventArgs e) => CopyTranslation(ClipboardList.SelectedItem as ClipboardEntry);
    private void CopyTranslation(ClipboardEntry? entry)
    {
        if (string.IsNullOrWhiteSpace(entry?.Translation)) return;
        try { lastClipboard = entry.Translation; System.Windows.Clipboard.SetText(entry.Translation); ShowClipboardNotice(entry, "Перевод скопирован", entry.Translation, 5); }
        catch (System.Runtime.InteropServices.ExternalException) { Status.Text = "Буфер занят. Повторите копирование."; }
    }

    private void StopClipboardTranslation()
    {
        if (noticeEntry?.TranslationState.StartsWith("Переводим на ", StringComparison.Ordinal) == true) noticeEntry.TranslationState = "Перевод отменён";
        StopNoticeIndicator();
        translationCancellation?.Cancel(); translationCancellation?.Dispose(); translationCancellation = null;
    }
}
