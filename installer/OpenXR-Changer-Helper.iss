#ifndef HelperBuildDir
  #error HelperBuildDir is required
#endif

[Setup]
AppId={{A3AA1E51-9DCF-42CF-8306-7DAEDB56F56B}
AppName=OpenXR Changer Runtime Helper
AppVersion=0.1.8
AppPublisher=Urs Caviezel
DefaultDirName={autopf}\OpenXR Changer Helper
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=OpenXR-Changer-Helper-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\OpenXR-Changer.Helper.exe

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Files]
Source: "{#HelperBuildDir}\OpenXR-Changer.Helper.exe"; DestDir: "{app}"; Flags: ignoreversion

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Code: Integer;
begin
  // Run only AFTER the user confirms uninstall. Failure aborts before deleting files.
  if CurUninstallStep = usUninstall then
    if not Exec(ExpandConstant('{app}\OpenXR-Changer.Helper.exe'), '--remove', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      RaiseException('Could not remove the service. Files have been retained; retry uninstall.');
end;

function InitializeSetup(): Boolean;
var Sid: String; I: Integer;
begin
  Sid := ExpandConstant('{param:AllowedSid|}');
  Result := (Pos('S-1-5-', Sid) = 1) and (Length(Sid) < 180);
  for I := 7 to Length(Sid) do
    if not ((Sid[I] >= '0') and (Sid[I] <= '9')) and (Sid[I] <> '-') then Result := False;
  if not Result then MsgBox('Start this setup from OpenXR Changer > Settings.', mbError, MB_OK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer; Exe: String;
begin
  Result := '';
  Exe := ExpandConstant('{app}\OpenXR-Changer.Helper.exe');
  if FileExists(Exe) then
    if not Exec(Exe, '--stop', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      Result := 'Could not stop the existing runtime helper.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Code: Integer;
begin
  if CurStep = ssPostInstall then
    if not Exec(ExpandConstant('{app}\OpenXR-Changer.Helper.exe'),
      '--install "' + ExpandConstant('{param:AllowedSid}') + '"', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      RaiseException('Runtime helper configuration failed. Run setup again or remove the helper through Windows Apps.');
end;
