; Radial Convert setup (Inno Setup 6). Built by publish.ps1, which passes AppVersion:
;   ISCC.exe /DAppVersion=0.2.0 installer\RadialConvert.iss
;
; Per-user install: no administrator prompt. The app must not run elevated anyway, because an
; elevated app can't read the selection in normal Explorer windows.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppName "Radial Convert"
#define AppExe "RadialConvert.exe"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"
; Same value name the app's tray "Start with Windows" toggle uses, so both stay in sync.
#define RunValue "RadialConvert"

[Setup]
AppId={{7D3C2B4E-8F1A-4C6B-9E2D-5A1F0B3C7E94}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Pranshu Thakkar
AppPublisherURL=https://github.com/pranshu1606/RadialConvert
AppSupportURL=https://github.com/pranshu1606/RadialConvert/issues
VersionInfoVersion={#AppVersion}
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
WizardImageFile=wizard-large.bmp,wizard-large@2x.bmp
WizardSmallImageFile=wizard-small.bmp,wizard-small@2x.bmp
SetupIconFile=..\src\RadialConvert\Assets\logo.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\dist
OutputBaseFilename=RadialConvert-Setup
Compression=lzma2/max
SolidCompression=yes
; The app is a tray app with no window, so stop it ourselves (see [Code]) rather than via Restart Manager.
CloseApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Shortcuts:"
Name: "startup"; Description: "&Launch Radial Convert when Windows starts"; GroupDescription: "Startup:"

[Files]
Source: "..\dist\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Hold Ctrl on a file to convert it"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Hold Ctrl on a file to convert it"; Tasks: desktopicon

[Registry]
; Current user only. --startup makes the app start quietly (no welcome notification).
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "{#RunValue}"; \
    ValueData: """{app}\{#AppExe}"" --startup"; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName} now"; Flags: nowait postinstall skipifsilent

[Messages]
FinishedLabel=Radial Convert is installed and lives in the system tray.%n%nTo use it: select a file on the Desktop or in File Explorer, hold Ctrl, drag toward a format and let go.

[Code]
procedure StopRunningApp();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  // Upgrading over a running copy: stop it so its exe can be replaced.
  StopRunningApp();
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Re-running setup with "launch at startup" unticked turns it off again.
  if (CurStep = ssPostInstall) and not WizardIsTaskSelected('startup') then
    RegDeleteValue(HKEY_CURRENT_USER, '{#RunKey}', '{#RunValue}');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    StopRunningApp();
  if CurUninstallStep = usPostUninstall then
  begin
    // Also covers startup turned on later from the tray menu, which setup didn't create.
    RegDeleteValue(HKEY_CURRENT_USER, '{#RunKey}', '{#RunValue}');
    // Remove the log folder the app created.
    DelTree(ExpandConstant('{localappdata}\RadialConvert'), True, True, True);
  end;
end;
