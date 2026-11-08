# OpenXR Changer

**A compact, portable OpenXR runtime switcher for Windows x64.**  
**Ein kompakter, portabler OpenXR-Runtime-Wechsler für Windows x64.**

[Download / Herunterladen](https://github.com/urscaviezel/OpenXR-Changer/releases/latest) · [Report an issue / Problem melden](https://github.com/urscaviezel/OpenXR-Changer/issues)

[English](#english) · [Deutsch](#deutsch)

![OpenXR Changer – runtime overview / Runtime-Übersicht](Screenshot.png)

## English

OpenXR Changer shows your installed OpenXR runtimes in a compact two-column view and lets you change the Windows system default. The active runtime is highlighted, and the window adjusts its height to the visible entries.

### Download and start

1. Download **OpenXR-Changer-win-x64.zip** from the [latest release](https://github.com/urscaviezel/OpenXR-Changer/releases/latest).
2. Extract the **entire ZIP** into a writable folder.
3. Start **OpenXR-Changer.exe**.

No installer or separate .NET runtime is required. To move the app, move its entire folder, including `assets`, `lang` and the `portable` folder created on first use.

### Features

- Built-in entries for **Virtual Desktop (VDXR), SteamVR, Meta, PimaxXR, ViveVR, Windows Mixed Reality and Varjo**.
- Automatic detection through known installation locations, Windows registry entries and Steam libraries. Nonstandard locations can be added manually.
- Add custom runtimes and edit their name, manifest path and logo at any time.
- Show or hide entries through **Manage runtimes**. Missing presets are initially hidden.
- Compact dark interface, transparent runtime logos and automatic window heights.
- **English, German and French**, with immediate language switching. Extendable JSON translations in `lang`.
- Optional update checks at startup and a manual check under **Info**. Available updates are installed only after confirmation; downloads are verified against GitHub's SHA-256 digest.
- Settings and imported logos stay in the portable folder.

### Usage

Select **Activate** beside an installed runtime. Windows requests administrator approval for the switch. Restart running VR applications afterwards.

- **Settings → Language settings:** select the interface language; changes are saved immediately.
- **Settings → Manage runtimes:** add, edit, show or hide entries.
- **Settings → Check for updates at startup:** enable or disable automatic checks.
- **Info → About:** credits and the project link.

The app changes the **64-bit Windows system default** at `HKLM\SOFTWARE\Khronos\OpenXR\1\ActiveRuntime`. It does not modify the 32-bit setting. Games and `XR_RUNTIME_JSON` can override the system default. Runtime software must already be installed; this app does not install VR runtimes. Only known supported manifests are added automatically; an unlisted active runtime is shown generically.

### Portable data and translations

| Location | Contents |
| --- | --- |
| `portable/settings.json` | Language, update preferences and runtime entries |
| `portable/settings.json.bak` | Previous settings backup |
| `portable/logos/` | Imported custom logos |
| `lang/en.json`, `de.json`, `fr.json` | Interface translations |

To add a language, copy a JSON file, translate its values and save it as UTF-8 with a language-code filename, such as `it.json`. Reopen the language dialog to select it. Missing keys fall back to English.

Updates preserve portable data and existing language files. If a release adds translation keys, merge them into your existing files when needed. Installed runtime paths still point to the software on the current PC; moving the app does not move the runtimes themselves.

### Build and checks

Requires Windows and the **.NET 10 SDK** for building only:

```powershell
.\build.ps1
```

The script produces a fresh self-contained x64 build and `artifacts/OpenXR-Changer-win-x64.zip`, excluding personal settings. A development build is also possible with `dotnet build -c Release`.

To run the included checks, supply an absolute report path in an existing directory:

```powershell
.\OpenXR-Changer.exe --self-test C:\Temp\openxr-checks.txt
```

The checks do not switch your runtime. Version 0.1.6 passed 15 checks covering portable settings, language fallback, manifest filtering, repeatable discovery and update-archive validation. End-to-end installation of a future release remains to be verified.

## Deutsch

OpenXR Changer zeigt deine installierten OpenXR-Runtimes in einer kompakten zweispaltigen Ansicht und lässt dich den Windows-Systemstandard wechseln. Die aktive Runtime ist hervorgehoben, und die Fensterhöhe passt sich den sichtbaren Einträgen an.

### Herunterladen und starten

1. **OpenXR-Changer-win-x64.zip** aus dem [aktuellen Release](https://github.com/urscaviezel/OpenXR-Changer/releases/latest) herunterladen.
2. Das **gesamte ZIP** in einen beschreibbaren Ordner entpacken.
3. **OpenXR-Changer.exe** starten.

Kein Installer und keine separate .NET-Installation erforderlich. Zum Verschieben der App den ganzen Ordner mitnehmen, einschliesslich `assets`, `lang` und des beim ersten Start angelegten Ordners `portable`.

### Funktionen

- Vorgaben für **Virtual Desktop (VDXR), SteamVR, Meta, PimaxXR, ViveVR, Windows Mixed Reality und Varjo**.
- Automatische Erkennung über bekannte Installationsorte, Windows-Registry und Steam-Bibliotheken. Abweichende Pfade können manuell ergänzt werden.
- Eigene Runtimes hinzufügen und Namen, Manifestpfad und Logo jederzeit bearbeiten.
- Einträge über **Runtimes verwalten** ein- und ausblenden. Nicht gefundene Vorgaben sind zunächst ausgeblendet.
- Kompakte dunkle Oberfläche, transparente Logos und automatische Fensterhöhen.
- **Deutsch, Englisch und Französisch**, sofort umschaltbar. Erweiterbare JSON-Sprachdateien im Ordner `lang`.
- Optionale Update-Prüfung beim Start sowie manuell unter **Info**. Verfügbare Updates werden erst nach Bestätigung installiert; Downloads werden anhand des SHA-256-Prüfwerts von GitHub geprüft.
- Einstellungen und importierte Logos bleiben im portablen Ordner.

### Bedienung

Neben einer installierten Runtime **Aktivieren** wählen. Windows fragt für den Wechsel nach Administratorrechten. Laufende VR-Anwendungen danach neu starten.

- **Settings → Spracheinstellung:** Sprache auswählen; Änderungen werden sofort gespeichert.
- **Settings → Runtimes verwalten:** Einträge hinzufügen, bearbeiten, ein- oder ausblenden.
- **Settings → Beim Start nach Updates suchen:** automatische Prüfung aktivieren oder deaktivieren.
- **Info → Über OpenXR Changer:** Credits und Projektlink.

Die App ändert den **64-Bit-Windows-Systemstandard** unter `HKLM\SOFTWARE\Khronos\OpenXR\1\ActiveRuntime`. Die 32-Bit-Einstellung bleibt unverändert. Spiele und `XR_RUNTIME_JSON` können den Systemstandard übersteuern. Die Runtime-Software muss bereits installiert sein; diese App installiert keine VR-Runtimes. Automatisch übernommen werden nur bekannte unterstützte Manifeste. Eine nicht gelistete aktive Runtime wird neutral angezeigt.

### Portable Daten und Sprachen

| Speicherort | Inhalt |
| --- | --- |
| `portable/settings.json` | Sprache, Update-Einstellung und Runtime-Einträge |
| `portable/settings.json.bak` | Sicherung des vorherigen Einstellungsstands |
| `portable/logos/` | Importierte eigene Logos |
| `lang/en.json`, `de.json`, `fr.json` | Übersetzungen der Oberfläche |

Für eine weitere Sprache eine JSON-Datei kopieren, die Werte übersetzen und als UTF-8 mit einem Sprachkürzel als Dateiname speichern, zum Beispiel `it.json`. Den Sprachdialog erneut öffnen, um sie auszuwählen. Fehlende Texte werden auf Englisch angezeigt.

Updates behalten portable Daten und vorhandene Sprachdateien bei. Neue Übersetzungsschlüssel bei Bedarf in die vorhandenen Dateien übernehmen. Installierte Runtime-Pfade verweisen weiterhin auf die Software auf dem jeweiligen PC; beim Verschieben der App werden die Runtimes selbst nicht mitverschoben.

### Bauen und prüfen

Zum Bauen werden Windows und das **.NET 10 SDK** benötigt:

```powershell
.\build.ps1
```

Das Skript erstellt einen eigenständig lauffähigen x64-Build und `artifacts/OpenXR-Changer-win-x64.zip` ohne persönliche Einstellungen. Ein Entwicklungsbuild ist mit `dotnet build -c Release` möglich.

Die integrierten Prüfungen benötigen einen absoluten Berichtspfad in einem vorhandenen Ordner:

```powershell
.\OpenXR-Changer.exe --self-test C:\Temp\openxr-checks.txt
```

Die Prüfungen wechseln keine Runtime. Version 0.1.6 hat 15 Prüfungen bestanden, unter anderem für portable Einstellungen, Sprachfallback, Manifestfilterung, wiederholte Erkennung und die Prüfung von Update-Archiven. Die vollständige Installation eines zukünftigen Releases muss noch praktisch geprüft werden.

## Credits / Lizenz

Created by / Erstellt von **Urs Caviezel**.

Based on / Basierend auf [OpenXR Runtime Switcher by WaGi-Coding](https://github.com/WaGi-Coding/OpenXR-Runtime-Switcher). Original runtime-detection credits / Ursprüngliche Runtime-Erkennung: **KOGA Mitsuhiro**.

MIT license / MIT-Lizenz: [LICENSE.txt](LICENSE.txt).

Product names and logos belong to their respective owners. Some logo assets were prepared with AI-assisted background removal. / Produktnamen und Logos gehören ihren jeweiligen Rechteinhabern. Einige Logos wurden mit KI-Unterstützung freigestellt.
