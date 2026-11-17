namespace DesktopIsland.Services;

// Reads metadata only. Existing files are not enumerated and contents are never opened.
public sealed class DownloadsService : IDisposable
{
    private FileSystemWatcher? watcher;
    private readonly System.Windows.Threading.Dispatcher dispatcher;
    public ObservableCollection<DownloadEntry> Items { get; } = [];
    public event Action<string, string>? Activity;
    public bool Running => watcher != null;
    public DownloadsService(System.Windows.Threading.Dispatcher dispatcher) => this.dispatcher = dispatcher;
    public void Start(string folder)
    {
        Dispose();
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Выберите существующую папку загрузок.");
        watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite, IncludeSubdirectories = false };
        watcher.Created += Changed; watcher.Changed += Changed; watcher.Renamed += Renamed;
        watcher.Error += (_, e) => dispatcher.BeginInvoke(() => Activity?.Invoke("Наблюдение за загрузками", e.GetException().Message));
        watcher.EnableRaisingEvents = true;
    }
    private void Changed(object sender, FileSystemEventArgs e) => Queue(e.FullPath);
    private void Renamed(object sender, RenamedEventArgs e)
    {
        dispatcher.BeginInvoke(() =>
        {
            var old = Items.FirstOrDefault(x => x.Path.Equals(e.OldFullPath, StringComparison.OrdinalIgnoreCase));
            if (old != null) Items.Remove(old);
            Observe(e.FullPath);
        });
    }
    private void Queue(string path) => dispatcher.BeginInvoke(() => Observe(path));
    private void Observe(string path)
    {
        if (!Running || !File.Exists(path)) return;
        var item = Items.FirstOrDefault(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (item == null) { item = new DownloadEntry { Path = path }; Items.Insert(0, item); while (Items.Count > 50) Items.RemoveAt(Items.Count - 1); }
        item.LastChanged = DateTime.Now; item.Complete = false;
        item.Status = DownloadEntry.IsTemporary(path) ? "Загрузка · размер растёт" : "Файл изменяется";
        try { item.Bytes = new FileInfo(path).Length; } catch (IOException) { }
    }
    public void Tick()
    {
        if (!Running) return;
        foreach (var item in Items.Where(x => !x.Complete && !DownloadEntry.IsTemporary(x.Path)).ToArray())
        {
            if (!File.Exists(item.Path)) { item.Status = "Файл перемещён или удалён"; item.Complete = true; continue; }
            if ((DateTime.Now - item.LastChanged).TotalSeconds < 5) continue;
            // Filesystem stability is not a browser completion signal; label it honestly.
            item.Status = "Файл не менялся 5 секунд"; item.Complete = true;
            Activity?.Invoke("Файл в папке загрузок", item.Name);
        }
    }
    public void Dispose() { watcher?.Dispose(); watcher = null; }
}
