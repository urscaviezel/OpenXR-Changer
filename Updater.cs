using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenXRChanger;
public record Release(string Version, string Url, string Digest);
public static class Updater
{
    public const string Repository = "https://github.com/urscaviezel/OpenXR-Changer";
    public const string Version = "0.1.8";
    static HttpClient Client()
    { var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) }; client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenXR-Changer/" + Version); return client; }
    public static async Task<Release?> Check()
    {
        using var client = Client();
        using var response = await client.GetAsync("https://api.github.com/repos/urscaviezel/OpenXR-Changer/releases/latest");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString()!.TrimStart('v');
        if (!System.Version.TryParse(tag, out var version) || version <= System.Version.Parse(Version)) return null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
            if (asset.GetProperty("name").GetString() == "OpenXR-Changer-win-x64.zip")
                return new(tag, asset.GetProperty("browser_download_url").GetString()!, asset.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "");
        throw new InvalidDataException("Release: OpenXR-Changer-win-x64.zip missing");
    }
    public static void Extract(string zip, string target)
    {
        using var archive = ZipFile.OpenRead(zip);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrEmpty(entry.Name)) continue;
            if (!(name == "OpenXR-Changer.exe" || name == "LICENSE.txt" || name == "README.txt" || name.StartsWith("lang/") || name.StartsWith("assets/"))) throw new InvalidDataException("Unexpected update file: " + name);
            var path = Path.GetFullPath(Path.Combine(target, name));
            if (!path.StartsWith(Path.GetFullPath(target) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe archive path");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); entry.ExtractToFile(path, false);
        }
        if (!File.Exists(Path.Combine(target, "OpenXR-Changer.exe"))) throw new InvalidDataException("Update executable missing");
    }
    public static async Task Install(Release release)
    {
        var uri = new Uri(release.Url);
        if (uri.Scheme != "https" || uri.Host != "github.com" || !uri.AbsolutePath.StartsWith("/urscaviezel/OpenXR-Changer/releases/download/")) throw new InvalidDataException("Invalid release URL");
        var dir = Path.Combine(Path.GetTempPath(), "OpenXR-Changer-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var zip = Path.Combine(dir, "release.zip"); using var client = Client();
        var bytes = await client.GetByteArrayAsync(uri);
        if (!release.Digest.StartsWith("sha256:") || !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), release.Digest[7..], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SHA256 verification failed");
        await File.WriteAllBytesAsync(zip, bytes); var stage = Path.Combine(dir, "stage"); Extract(zip, stage);
        var helper = Path.Combine(dir, "updater.exe"); File.Copy(Environment.ProcessPath!, helper);
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "--apply-update", stage, Store.Root, Environment.ProcessId.ToString() }) start.ArgumentList.Add(a);
        _ = Process.Start(start) ?? throw new IOException("Updater failed to start");
    }
    public static void Apply(string stage, string target, int parent)
    {
        try { using var process = Process.GetProcessById(parent); if (!process.WaitForExit(60000)) throw new IOException("Application still running"); } catch (ArgumentException) { }
        target = Path.GetFullPath(target); stage = Path.GetFullPath(stage);
        var backup = Path.Combine(target, "portable", "update-backup", DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
        var changed = new List<(string dest, string? backup)>();
        try
        {
            foreach (var source in Directory.GetFiles(stage, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(stage, source);
                var dest = Path.Combine(target, relative);
                if (relative.StartsWith("lang" + Path.DirectorySeparatorChar) && File.Exists(dest)) continue;
                string? old = null;
                if (File.Exists(dest)) { old = Path.Combine(backup, relative); Directory.CreateDirectory(Path.GetDirectoryName(old)!); File.Copy(dest, old); }
                changed.Add((dest, old)); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(source, dest, true);
            }
        }
        catch
        {
            foreach (var item in changed.AsEnumerable().Reverse()) { if (item.backup != null) File.Copy(item.backup, item.dest, true); else if (File.Exists(item.dest)) File.Delete(item.dest); }
            throw;
        }
        Process.Start(new ProcessStartInfo(Path.Combine(target, "OpenXR-Changer.exe")) { UseShellExecute = true, WorkingDirectory = target });
    }
}
