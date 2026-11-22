using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DesktopIsland.Services;

namespace DesktopIsland;

internal static class UpdateTests
{
    internal static async Task RunAsync(string root, Action<string, bool> check)
    {
        using var json = JsonDocument.Parse("""{"tag_name":"v99.1.2","draft":false,"prerelease":false,"assets":[{"name":"OSTROVOK-99.1.2-win-x64.zip","size":2048,"browser_download_url":"https://github.com/rapichmoney/OSTROVOK/releases/download/v99.1.2/OSTROVOK-99.1.2-win-x64.zip"},{"name":"OSTROVOK-99.1.2-win-x64.zip.sha256","size":96,"browser_download_url":"https://github.com/rapichmoney/OSTROVOK/releases/download/v99.1.2/OSTROVOK-99.1.2-win-x64.zip.sha256"}]}""");
        check("new stable release is offered", AppUpdateService.ParseRelease(json.RootElement, new Version(1,0,20))?.Version == new Version(99,1,2));
        check("same release is not reinstalled", AppUpdateService.ParseRelease(json.RootElement, new Version(99,1,2)) == null);
        using var foreign = JsonDocument.Parse(json.RootElement.GetRawText().Replace("github.com/rapichmoney/", "github.com/other/"));
        check("update rejects assets from a different repository", AppUpdateService.ParseRelease(foreign.RootElement, new Version(1,0,0)) == null);
        using var preview = JsonDocument.Parse(json.RootElement.GetRawText().Replace("\"prerelease\":false", "\"prerelease\":true"));
        check("automatic updates exclude prereleases", AppUpdateService.ParseRelease(preview.RootElement, new Version(1,0,0)) == null);
        foreach (string path in new[] { "../outside.txt", @"..\outside.txt", @"C:\outside.txt", "file:stream" })
        {
            bool rejected = false; try { AppUpdateService.SafeEntry(root, path); } catch (InvalidDataException) { rejected = true; }
            check("archive extraction rejects " + path, rejected);
        }
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        { var entry = archive.CreateEntry("DesktopIsland.exe"); using var writer = entry.Open(); await using var exe = File.OpenRead(Environment.ProcessPath!); await exe.CopyToAsync(writer); }
        byte[] zip = buffer.ToArray(); string digest = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
        var current = AppUpdateService.CurrentVersion; var version = new Version(current.Major, current.Minor, current.Build);
        var release = new AppRelease(version, new Uri("https://example.test/archive"), zip.Length, "sha256:"+digest, new Uri("https://example.test/checksum"));
        using var client = new HttpClient(new Fixture(zip, digest, version));
        var updater = new AppUpdateService(client);
        string stage = await updater.DownloadAsync(release, new Progress<int>());
        check("verified archive extracts expected application version", File.Exists(Path.Combine(stage,"DesktopIsland.exe")));
        bool invalid = false;
        try { await updater.DownloadAsync(release with { Digest = "sha256:" + new string('0',64) }, new Progress<int>()); } catch (InvalidDataException) { invalid = true; }
        check("checksum mismatch blocks installation", invalid);
        string helper = Path.Combine(root,"apply-update.ps1");
        using (var resource = typeof(App).Assembly.GetManifestResourceStream("DesktopIsland.tools.apply-update.ps1")!)
        using (var file = File.Create(helper)) resource.CopyTo(file);
        string fixtureRoot = Path.Combine(root,"update-fixture"); Directory.CreateDirectory(fixtureRoot);
        string source = Path.Combine(fixtureRoot,"stage"), target = Path.Combine(fixtureRoot,"installed");
        Directory.CreateDirectory(source); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(source,"DesktopIsland.exe"),"new binary fixture");
        File.WriteAllText(Path.Combine(target,"DesktopIsland.exe"),"old binary fixture");
        File.WriteAllText(Path.Combine(target,"settings.json"),"user settings fixture");
        string plan = Path.Combine(fixtureRoot,"plan.json");
        File.WriteAllText(plan,JsonSerializer.Serialize(new { stage=source,target,pid=0,started=DateTime.UtcNow.ToString("O"),version="99.1.2",restart=false }));
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute=false, CreateNoWindow=true };
        foreach (string arg in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",helper,"-PlanFile",plan}) start.ArgumentList.Add(arg);
        using (var process = Process.Start(start)!)
        {
            if (!process.WaitForExit(15000)) { process.Kill(); throw new TimeoutException("Update fixture timed out"); }
            check("update helper installs without launching fixture binaries",process.ExitCode==0 && File.ReadAllText(Path.Combine(target,"DesktopIsland.exe"))=="new binary fixture");
        }
        check("update keeps user files and backup",File.ReadAllText(Path.Combine(target,"settings.json"))=="user settings fixture" && File.ReadAllText(Path.Combine(fixtureRoot,"backup","DesktopIsland.exe"))=="old binary fixture");
        // A locked destination must fail without stopping or killing its owner.
        using (var locked=File.Open(Path.Combine(target,"DesktopIsland.exe"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        using (var process=Process.Start(start)!)
        { if(!process.WaitForExit(15000)){process.Kill();throw new TimeoutException();} check("locked installation reports failure",process.ExitCode!=0); }
        check("failed replacement keeps the previous working file",File.ReadAllText(Path.Combine(target,"DesktopIsland.exe"))=="new binary fixture");
        File.WriteAllText(Path.Combine(source,"DesktopIsland.exe"),"next binary fixture");
        File.WriteAllText(Path.Combine(source,"zz-locked.txt"),"new secondary fixture");
        File.WriteAllText(Path.Combine(target,"zz-locked.txt"),"old secondary fixture");
        using (var locked=File.Open(Path.Combine(target,"zz-locked.txt"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        using (var process=Process.Start(start)!)
        { if(!process.WaitForExit(15000)){process.Kill();throw new TimeoutException();} check("partial update reports failure",process.ExitCode!=0); }
        check("partial update rolls back replaced application",File.ReadAllText(Path.Combine(target,"DesktopIsland.exe"))=="new binary fixture");
    }
    private sealed class Fixture(byte[] zip,string digest,Version version):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,System.Threading.CancellationToken token)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=request.RequestUri!.AbsolutePath.Contains("checksum") ? new StringContent(digest+"  OSTROVOK-"+version+"-win-x64.zip\n",Encoding.UTF8) : new ByteArrayContent(zip) });
    }
}
