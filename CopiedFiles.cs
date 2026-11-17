namespace DesktopIsland;

public partial class MainWindow
{
    private string[] copiedNoticeFiles = [];
    private void HandleCopiedFiles(string[] paths)
    {
        if (paths.Length == 0) return;
        StopClipboardTranslation();
        copiedNoticeFiles = paths;
        string caption = paths.Length == 1 ? "Файл скопирован" : $"Скопировано файлов: {paths.Length}";
        string names = string.Join(", ", paths.Take(3).Select(System.IO.Path.GetFileName));
        if (paths.Length > 3) names += $" и ещё {paths.Length - 3}";
        ShowClipboardNotice(new ClipboardEntry { Text = names }, caption, names, 5);
    }
}
