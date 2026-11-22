using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace DesktopIsland.Services;

internal sealed record AppRelease(Version Version, Uri Archive, long Size, string Digest, Uri Checksum);
internal sealed class AppUpdateService
{
    private static readonly HttpClient shared = CreateHttp();
    private readonly HttpClient http;
    internal AppUpdateService(HttpClient? client = null) => http = client ?? shared;
    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OSTROVOK-Updater/1.0"); return client;
    }
    internal static Version CurrentVersion => typeof(App).Assembly.GetName().Version!;
    internal static AppRelease? ParseRelease(JsonElement root, Version current)
    {
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$") || !Version.TryParse(tag[1..], out var version) || version <= new Version(current.Major, current.Minor, current.Build)) return null;
        string name = $"OSTROVOK-{version}-win-x64.zip";
        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        var archive = assets.FirstOrDefault(x => x.GetProperty("name").GetString() == name);
        var checksum = assets.FirstOrDefault(x => x.GetProperty("name").GetString() == name + ".sha256");
        if (archive.ValueKind != JsonValueKind.Object || checksum.ValueKind != JsonValueKind.Object) return null;
        Uri? Url(JsonElement element)
        {
            if (!Uri.TryCreate(element.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var uri)) return null;
            return uri.Scheme == "https" && uri.Host == "github.com" && uri.AbsolutePath == $"/rapichmoney/OSTROVOK/releases/download/{tag}/{element.GetProperty("name").GetString()}" ? uri : null;
        }
        var url = Url(archive); var hashUrl = Url(checksum);
        long size = archive.GetProperty("size").GetInt64();
        if (url == null || hashUrl == null || size < 1024 || size > 600L * 1024 * 1024) return null;
        string digest = archive.TryGetProperty("digest", out var value) ? value.GetString() ?? "" : "";
        return new(version, url, size, digest, hashUrl);
    }
    internal async Task<AppRelease?> CheckAsync(CancellationToken cancellation = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await http.GetAsync("https://api.github.com/repos/rapichmoney/OSTROVOK/releases/latest", deadline.Token);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token));
        return ParseRelease(json.RootElement, CurrentVersion);
    }
    internal static string SafeEntry(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':')) throw new InvalidDataException("Недопустимый путь в архиве");
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(Path.Combine(prefix, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Путь выходит за папку обновления");
        return target;
    }
    internal async Task<string> DownloadAsync(AppRelease release, IProgress<int> progress, CancellationToken token = default)
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopIsland", "Updates", release.Version + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); string archive = Path.Combine(root, "download.zip");
        string checksum = await http.GetStringAsync(release.Checksum, token);
        var match = Regex.Match(checksum.Trim(), @"^([a-fA-F0-9]{64})\s+OSTROVOK-" + Regex.Escape(release.Version.ToString()) + @"-win-x64\.zip$");
        if (!match.Success) throw new InvalidDataException("Не найдена контрольная сумма релиза");
        using (var response = await http.GetAsync(release.Archive, HttpCompletionOption.ResponseHeadersRead, token))
        {
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(token);
            await using var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
            byte[] buffer = new byte[131072]; long received = 0; int count;
            while ((count = await input.ReadAsync(buffer, token)) > 0)
            { received += count; if (received > release.Size) throw new InvalidDataException("Размер архива не совпадает с релизом"); await output.WriteAsync(buffer.AsMemory(0, count), token); progress.Report((int)(received * 100 / release.Size)); }
            if (received != release.Size) throw new InvalidDataException("Архив скачан не полностью");
        }
        await using (var file = File.OpenRead(archive))
        {
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(file, token)).ToLowerInvariant();
            if (!actual.Equals(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase) || release.Digest.Length > 0 && !release.Digest.Equals("sha256:" + actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Контрольная сумма архива не совпала");
        }
        string stage = Path.Combine(root, "stage"); Directory.CreateDirectory(stage);
        await Task.Run(() =>
        {
            using var zip = ZipFile.OpenRead(archive); long expanded = 0;
            if (zip.Entries.Count > 10000) throw new InvalidDataException("Слишком много файлов в архиве");
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested(); expanded += entry.Length;
                if (expanded > 1500L * 1024 * 1024) throw new InvalidDataException("Архив слишком большой");
                string destination = SafeEntry(stage, entry.FullName);
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!); entry.ExtractToFile(destination, false);
            }
        }, token);
        string exe = Path.Combine(stage, "DesktopIsland.exe");
        var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe);
        if (new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart) != release.Version) throw new InvalidDataException("Версия EXE не совпадает с релизом");
        return stage;
    }
}
