using System.Text;
using System.Text.Json;

namespace DesktopIsland.Services;

public sealed class LocalStore
{
    public string Root { get; }
    public event Action<string>? Error;
    public LocalStore(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopIsland");
        Directory.CreateDirectory(Root);
    }
    public T Load<T>(string name, Func<T> fallback)
    {
        var path = Path.Combine(Root, name);
        if (!File.Exists(path)) return fallback();
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? fallback(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Keep the malformed original for recovery. Do not overwrite it on startup.
            try { File.Copy(path, path + ".damaged-" + DateTime.Now.ToString("yyyyMMddHHmmssfff")); } catch (IOException) { }
            Error?.Invoke($"Не удалось прочитать {name}: {ex.Message}");
            return fallback();
        }
    }
    public string LoadText(string name)
    {
        try { var path = Path.Combine(Root, name); return File.Exists(path) ? File.ReadAllText(path) : ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Error?.Invoke(ex.Message); return ""; }
    }
    public bool Save<T>(string name, T value) => SaveText(name, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    public bool SaveText(string name, string text)
    {
        var path = Path.Combine(Root, name);
        var temp = path + ".writing";
        try
        {
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            File.Move(temp, path, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Error?.Invoke($"Не удалось сохранить {name}: {ex.Message}"); return false; }
    }
}
