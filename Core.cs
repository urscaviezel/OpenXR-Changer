using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenXRChanger;

public sealed class RuntimeEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Logo { get; set; } = "";
    public bool Visible { get; set; } = true;
    public bool Custom { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string ResolvedPath => Store.Resolve(Path);
    [System.Text.Json.Serialization.JsonIgnore] public bool Installed => RuntimeRegistry.Validate(ResolvedPath, out _);
}
public sealed class Preferences
{
    public string Language { get; set; } = "de";
    public bool CheckUpdates { get; set; } = true;
    public bool StartInTray { get; set; }
    public bool CloseToTray { get; set; } = true;
    public List<RuntimeEntry> Runtimes { get; set; } = [];
}
public static class Store
{
    public static string Root = AppContext.BaseDirectory;
    public static string Data => System.IO.Path.Combine(Root, "portable");
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string Resolve(string path) => string.IsNullOrEmpty(path) ? "" : System.IO.Path.GetFullPath(path, Root);
    public static string PortablePath(string path) => path.StartsWith(Root, StringComparison.OrdinalIgnoreCase) ? System.IO.Path.GetRelativePath(Root, path) : path;
    public static Preferences Load()
    {
        Directory.CreateDirectory(Data);
        var path = System.IO.Path.Combine(Data, "settings.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? throw new InvalidDataException(path) : new();
    }
    public static void Save(Preferences p)
    {
        Directory.CreateDirectory(Data);
        var path = System.IO.Path.Combine(Data, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(p, Json));
        if (File.Exists(path)) File.Replace(path + ".tmp", path, path + ".bak");
        else File.Move(path + ".tmp", path);
    }
}
public sealed class Language
{
    Dictionary<string, string> words = [];
    Dictionary<string, string> fallback = [];
    public void Load(string id)
    {
        fallback = Read("en"); words = Read(id);
    }
    Dictionary<string, string> Read(string id)
    {
        if (System.IO.Path.GetFileName(id) != id) return [];
        var path = System.IO.Path.Combine(Store.Root, "lang", id + ".json");
        using var builtIn = typeof(Language).Assembly.GetManifestResourceStream("OpenXRChanger.Languages." + id + ".json");
        var values = builtIn == null ? new Dictionary<string, string>() : JsonSerializer.Deserialize<Dictionary<string, string>>(builtIn) ?? [];
        if (File.Exists(path)) foreach (var pair in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? []) if (!LegacyTranslations.IsDamaged(id, pair.Key, pair.Value)) values[pair.Key] = pair.Value;
        return values;
    }
    public string this[string key] => words.GetValueOrDefault(key, fallback.GetValueOrDefault(key, key));
}
public static class RuntimeRegistry
{
    public const string Key = @"SOFTWARE\Khronos\OpenXR\1";
    public static string Active
    {
        get { using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64); using var key = hive.OpenSubKey(Key); return key?.GetValue("ActiveRuntime") as string ?? ""; }
    }
    public static bool Same(string a, string b) => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    public static bool Validate(string path, out string error)
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var dll = json.RootElement.GetProperty("runtime").GetProperty("library_path").GetString();
            if (string.IsNullOrWhiteSpace(dll) || !File.Exists(System.IO.Path.GetFullPath(dll, System.IO.Path.GetDirectoryName(path)!))) throw new InvalidDataException("runtime.library_path: DLL missing");
            // Reject 32-bit binaries for the 64-bit registry view.
            using var reader = new BinaryReader(File.OpenRead(System.IO.Path.GetFullPath(dll, System.IO.Path.GetDirectoryName(path)!)));
            if (reader.ReadUInt16() != 0x5A4D) throw new InvalidDataException("Invalid DLL");
            reader.BaseStream.Position = 0x3c; var offset = reader.ReadInt32(); reader.BaseStream.Position = offset;
            if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664) throw new InvalidDataException("x64 DLL required");
            error = ""; return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }
    public static void Set(string path)
    {
        if (!Validate(path, out var error)) throw new InvalidDataException(error);
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hive.CreateSubKey(Key, true);
        key.SetValue("ActiveRuntime", System.IO.Path.GetFullPath(path), RegistryValueKind.String);
        if (!Same(Active, path)) throw new IOException("Registry verification failed");
    }
    static string Read(RegistryHive hive, RegistryView view, string key, string value)
    { using var root = RegistryKey.OpenBaseKey(hive, view); using var sub = root.OpenSubKey(key); return sub?.GetValue(value) as string ?? ""; }
    public static string? Identify(string path) => System.IO.Path.GetFileName(path).ToLowerInvariant() switch
    {
        "virtualdesktop-openxr.json" => "Virtual Desktop · VDXR",
        "steamxr_win64.json" => "SteamVR",
        "oculus_openxr_64.json" => "Meta",
        "varjoopenxr.json" => "Varjo",
        "viveopenxr.json" => "ViveVR",
        "mixedrealityruntime.json" => "Windows Mixed Reality",
        "pimax-openxr.json" or "pimaxxr.json" or "pimaxxrruntime.json" => "PimaxXR",
        _ => null
    };
    public static void Discover(Preferences prefs)
    {
        // Keep existing entries, paths and visibility while updating the old display name.
        foreach (var entry in prefs.Runtimes.Where(r => !r.Custom && r.Name == "Meta / Oculus")) entry.Name = "Meta";
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Root(string path) { if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) roots.Add(path); }
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        Root(System.IO.Path.Combine(pf, "Virtual Desktop Streamer"));
        Root(Read(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Virtual Desktop, Inc.\Virtual Desktop Streamer", "Path"));
        Root(Read(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Varjo\Runtime", "InstallDir"));
        Root(Read(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\HtcVive\Updater", "AppPath"));
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall == null) continue;
            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var app = uninstall.OpenSubKey(name);
                if (Regex.IsMatch(app?.GetValue("DisplayName") as string ?? "", "Virtual Desktop|SteamVR|Oculus|Meta Quest|Vive|Varjo|Pimax", RegexOptions.IgnoreCase)) Root(app?.GetValue("InstallLocation") as string ?? "");
            }
        }
        var steam = Read(RegistryHive.CurrentUser, RegistryView.Registry64, @"SOFTWARE\Valve\Steam", "SteamPath");
        if (Directory.Exists(steam))
        {
            Root(System.IO.Path.Combine(steam, "steamapps/common/SteamVR"));
            var vdf = System.IO.Path.Combine(steam, "steamapps/libraryfolders.vdf");
            if (File.Exists(vdf)) foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\"")) Root(System.IO.Path.Combine(m.Groups[1].Value.Replace(@"\\", @"\"), "steamapps/common/SteamVR"));
        }
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Scan(string folder, int depth)
        {
            try { foreach (var f in Directory.EnumerateFiles(folder, "*.json")) if (Validate(f, out _)) files.Add(f);
                if (depth > 0) foreach (var d in Directory.EnumerateDirectories(folder)) if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0) Scan(d, depth - 1);
            } catch (UnauthorizedAccessException) { } catch (IOException) { }
        }
        foreach (var root in roots) Scan(root, 4);
        var wmr = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "MixedRealityRuntime.json");
        if (Validate(wmr, out _)) files.Add(wmr);
        using (var hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        using (var available = hk.OpenSubKey(Key + @"\AvailableRuntimes"))
            if (available != null) foreach (var f in available.GetValueNames()) if (Validate(f, out _)) files.Add(f);
        if (!string.IsNullOrWhiteSpace(Active)) files.Add(Active);
        foreach (var name in new[] { "Virtual Desktop · VDXR", "SteamVR", "Meta", "Varjo", "ViveVR", "Windows Mixed Reality", "PimaxXR" })
            if (!prefs.Runtimes.Any(r => r.Name == name)) prefs.Runtimes.Add(new() { Name = name, Visible = false });
        foreach (var path in files)
        {
            if (prefs.Runtimes.Any(r => Same(r.ResolvedPath, path))) continue;
            var name = Identify(path);
            // Only supported public manifests become automatic entries. Other runtimes
            // can be configured explicitly by the user through the generic custom editor.
            if (name == null) continue;
            var entry = prefs.Runtimes.FirstOrDefault(r => !r.Custom && r.Name == name && string.IsNullOrEmpty(r.Path));
            if (entry == null) prefs.Runtimes.Add(new() { Name = name, Path = path });
            else { entry.Path = path; entry.Visible = true; }
        }
    }
}
