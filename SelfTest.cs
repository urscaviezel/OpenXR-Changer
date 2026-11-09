using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace OpenXRChanger;
public static class SelfTest
{
    public static void Run(string output)
    {
        var results = new List<string>();
        void Assert(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); results.Add("PASS: " + name); }
        var root = Path.Combine(Path.GetDirectoryName(output)!, "test-data-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var originalRoot = Store.Root;
        try
        {
            Store.Root = root + Path.DirectorySeparatorChar;
            var previousCulture = System.Globalization.CultureInfo.CurrentUICulture;
            try
            {
                foreach (var (culture, expected) in new[] { ("de-CH", "de"), ("de-DE", "de"), ("en-US", "en"), ("en-GB", "en"), ("fr-CA", "fr"), ("it-CH", "en"), ("ja-JP", "en") })
                {
                    System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
                    Assert(Store.Load().Language == expected, "first-start UI language: " + culture);
                }
                System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
                Store.Save(new Preferences { Language = "fr" });
                Assert(Store.Load().Language == "fr", "saved language overrides system language");
                Store.Save(new Preferences { Language = "it" });
                Assert(Store.Load().Language == "it", "custom saved language retained");
            }
            finally { System.Globalization.CultureInfo.CurrentUICulture = previousCulture; }
            var p = new Preferences { Language = "fr", CheckUpdates = false, StartInTray = true, CloseToTray = false, Runtimes = [new() { Name = "Test", Path = "runtime.json", Logo = "portable/logos/test.png", Visible = false, Custom = true }] };
            Store.Save(p); Store.Save(p);
            var loaded = Store.Load(); Assert(loaded.Language == "fr" && !loaded.CheckUpdates && !loaded.Runtimes[0].Visible, "portable settings round trip");
            Assert(File.Exists(Path.Combine(Store.Data, "settings.json.bak")), "atomic settings backup");
            Assert(loaded.StartInTray && !loaded.CloseToTray, "tray preferences persist");
            var legacy = JsonSerializer.Deserialize<Preferences>("{\"Language\":\"en\"}")!;
            Assert(!legacy.StartInTray && legacy.CloseToTray, "legacy preferences receive tray defaults");
            var moved = root + "-moved"; Directory.Move(root, moved); root = moved; Store.Root = moved + Path.DirectorySeparatorChar;
            Assert(Store.Load().Runtimes[0].ResolvedPath == Path.Combine(moved, "runtime.json"), "moved folder preserves relative paths and entries");
            Assert(!RuntimeRegistry.Validate(Path.Combine(root, "missing.json"), out _), "missing manifest rejected");
            var invalid = Path.Combine(root, "layer.json"); File.WriteAllText(invalid, "{\"api_layer\":{\"library_path\":\"test.dll\"}}");
            Assert(!RuntimeRegistry.Validate(invalid, out _), "API layer rejected");
            var zip = Path.Combine(root, "unsafe.zip"); using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) { using var w = new StreamWriter(z.CreateEntry("lang/../../escape.txt").Open()); w.Write("bad"); }
            bool rejected = false; try { Updater.Extract(zip, Path.Combine(root, "extract")); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected && !File.Exists(Path.Combine(root, "escape.txt")), "update ZIP path traversal rejected");
            var validZip = Path.Combine(root, "valid.zip"); using (var z = ZipFile.Open(validZip, ZipArchiveMode.Create)) { using var w = new StreamWriter(z.CreateEntry("OpenXR-Changer.exe").Open()); w.Write("fixture"); }
            Updater.Extract(validZip, Path.Combine(root, "valid")); Assert(File.Exists(Path.Combine(root, "valid", "OpenXR-Changer.exe")), "valid update layout extracted");
            Directory.CreateDirectory(Path.Combine(Store.Root, "lang"));
            File.WriteAllText(Path.Combine(Store.Root, "lang", "en.json"), "{\"save\":\"My save\"}");
            var oldLanguage = new Language(); oldLanguage.Load("en");
            Assert(oldLanguage["trayOpen"] == "Open OpenXR Changer" && oldLanguage["save"] == "My save", "old translation files retain edits and receive built-in new keys");
            Store.Root = originalRoot;
            var english = JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(Store.Root, "lang", "en.json")))!;
            foreach (var lang in new[] { "de", "fr" }) { var values = JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(Store.Root, "lang", lang + ".json")))!; Assert(english.Keys.All(values.ContainsKey), lang + " translation coverage"); }
            foreach (var name in new[] { "SteamVR", "Meta", "PimaxXR", "ViveVR", "Virtual Desktop \u00b7 VDXR", "Windows Mixed Reality", "Varjo" })
            {
                using var icon = RuntimeLogo.CreateTrayBitmap(new RuntimeEntry { Name = name });
                Assert(icon != null && icon.Width == 48 && icon.Height == 48 && Enumerable.Range(0, 48).Any(x => Enumerable.Range(0, 48).Any(y => icon.GetPixel(x, y).A > 0)), "tray logo renders: " + name);
            }
            using (var customIcon = RuntimeLogo.CreateTrayBitmap(new RuntimeEntry { Name = "Custom", Logo = Path.Combine(Store.Root, "assets", "transparent-meta.png") }))
                Assert(customIcon != null, "custom tray logo renders");
            Assert(RuntimeLogo.CreateTrayBitmap(new RuntimeEntry { Name = "Custom", Logo = Path.Combine(root, "absent.png") }) == null, "missing tray logo is tolerated");
            var language = new Language(); language.Load("absent"); Assert(language["save"] == "Save", "English fallback");
            var detected = new Preferences(); RuntimeRegistry.Discover(detected);
            Assert(RuntimeRegistry.Identify(@"C:\Apps\OpenXR\virtualdesktop-openxr.json") == "Virtual Desktop · VDXR", "public manifest identification");
            Assert(RuntimeRegistry.Identify(@"C:\SteamVR\preview-runtime.json") == null, "unknown manifest is not inferred from its folder name");
            Assert(RuntimeRegistry.Identify(@"C:\Apps\xr\unreleased_xr.json") == null, "unlisted manifests excluded from automatic discovery");
            Assert(detected.Runtimes.Count == 7 && detected.Runtimes.All(r => string.IsNullOrEmpty(r.Path) || RuntimeRegistry.Identify(r.Path) != null), "seven public presets and supported automatic entries only");
            var count = detected.Runtimes.Count; RuntimeRegistry.Discover(detected); Assert(count == detected.Runtimes.Count, "discovery is idempotent");
            File.WriteAllLines(output, results);
        }
        finally { Store.Root = originalRoot; }
    }
}
