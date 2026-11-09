#ifndef BuildDir
  #error BuildDir is required
#endif
#ifndef AppVersion
  #define AppVersion "0.1.8"
#endif

[Setup]
AppId={{52436D3C-B36E-4973-A446-459C7B9F818B}
AppName=OpenXR Changer
AppVersion={#AppVersion}
AppPublisher=Urs Caviezel
AppPublisherURL=https://github.com/urscaviezel/OpenXR-Changer
DefaultDirName={localappdata}\Programs\OpenXR Changer
DefaultGroupName=OpenXR Changer
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=OpenXR-Changer-Setup-win-x64
SetupIconFile=..\assets\OpenXR-Changer.ico
UninstallDisplayIcon={app}\OpenXR-Changer.exe
LicenseFile=..\LICENSE.txt
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex=Local\OpenXRChanger-AppRunning

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[CustomMessages]
en.Startup=Start in the tray when I sign in to Windows
de.Startup=Bei der Windows-Anmeldung im Tray starten
fr.Startup=Démarrer dans la zone de notification à la connexion Windows

en.RemoveData=Also permanently remove settings, custom logos and language files in this app folder?%n%nYes: remove portable and lang.%nNo (recommended): keep them for a later installation.%n%nThe active runtime and the separately installed helper are not changed.
de.RemoveData=Auch Einstellungen, eigene Logos und Sprachdateien in diesem App-Ordner dauerhaft entfernen?%n%nJa: portable und lang entfernen.%nNein (empfohlen): für eine spätere Installation behalten.%n%nDie aktive Runtime und der separat installierte Helper bleiben unverändert.
fr.RemoveData=Supprimer également les paramètres, logos personnels et fichiers de langue de ce dossier ?%n%nOui : supprimer portable et lang.%nNon (recommandé) : les conserver pour une prochaine installation.%n%nLe runtime actif et le service installé séparément restent inchangés.

en.AppRunning=OpenXR Changer is still running, possibly in the tray. Choose Exit in the app, then click OK to retry. Cancel stops uninstall without deleting files.
de.AppRunning=OpenXR Changer läuft noch, eventuell im Tray. Bitte die App über Beenden schliessen und mit OK erneut prüfen. Abbrechen beendet die Deinstallation ohne Dateien zu löschen.
fr.AppRunning=OpenXR Changer est encore ouvert. Quittez l’application, puis cliquez sur OK pour réessayer. Annuler arrête la désinstallation sans supprimer de fichiers.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked
Name: "startup"; Description: "{cm:Startup}"; Flags: unchecked

[Files]
Source: "{#BuildDir}\OpenXR-Changer.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\assets\*"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "{#BuildDir}\lang\*.json"; DestDir: "{app}\lang"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#BuildDir}\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\README.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\OpenXR Changer"; Filename: "{app}\OpenXR-Changer.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\OpenXR Changer"; Filename: "{app}\OpenXR-Changer.exe"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{userstartup}\OpenXR Changer"; Filename: "{app}\OpenXR-Changer.exe"; Parameters: "--tray"; WorkingDir: "{app}"; Tasks: startup

[Run]
Filename: "{app}\OpenXR-Changer.exe"; Description: "{cm:LaunchProgram,OpenXR Changer}"; Flags: nowait postinstall skipifsilent

[Code]
var RemoveUserData: Boolean;

function AppStillRunning(): Boolean;
var Locator, Services, Processes: Variant;
begin
  Result := CheckForMutexes('Local\OpenXRChanger-AppRunning');
  if Result then Exit;
  // Also detect earlier app builds without the uninstall mutex.
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Services := Locator.ConnectServer('', 'root\CIMV2');
    Processes := Services.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''OpenXR-Changer.exe''');
    Result := Processes.Count > 0;
  except
    RaiseException('Could not check running applications. Uninstall cancelled.');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usAppMutexCheck then
    while AppStillRunning() do
    begin
      if UninstallSilent then RaiseException(CustomMessage('AppRunning'));
      if MsgBox(CustomMessage('AppRunning'), mbInformation, MB_OKCANCEL) <> IDOK then Abort;
    end;
  if CurUninstallStep = usUninstall then
  begin
    RemoveUserData := False;
    if UninstallSilent then
      RemoveUserData := ExpandConstant('{param:REMOVEUSERDATA|0}') = '1'
    else
      RemoveUserData := MsgBox(CustomMessage('RemoveData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
    if RemoveUserData then Log('Removing portable settings and language files by explicit choice.')
    else Log('Keeping portable settings and language files.');
  end;
  if (CurUninstallStep = usPostUninstall) and RemoveUserData then
  begin
    // Fixed app-owned subdirectories only; never recursively delete the app root.
    if DirExists(ExpandConstant('{app}\portable')) then
      if not DelTree(ExpandConstant('{app}\portable'), True, True, True) then
        RaiseException('Could not remove all settings. Close the app and remove the portable folder manually.');
    if DirExists(ExpandConstant('{app}\lang')) then
      if not DelTree(ExpandConstant('{app}\lang'), True, True, True) then
        RaiseException('Could not remove all translations. Remove the lang folder manually.');
    RemoveDir(ExpandConstant('{app}'));
  end;
end;
