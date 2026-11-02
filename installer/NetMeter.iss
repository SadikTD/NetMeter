; Per-user installer (no admin prompt). Build: iscc /DMyAppVersion=1.0.0 installer\NetMeter.iss
; Expects the self-contained app in ..\publish (see .github/workflows/release.yml).

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

[Setup]
AppId={{B1C7E5A2-6F1D-4C55-9A0E-3D2F8C1A7E44}
AppName=NetMeter
AppVersion={#MyAppVersion}
AppPublisher=SadikTD
AppPublisherURL=https://github.com/SadikTD/NetMeter
AppSupportURL=https://github.com/SadikTD/NetMeter/issues
DefaultDirName={localappdata}\Programs\NetMeter
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=NetMeter-{#MyAppVersion}-Setup
SetupIconFile=..\NetMeter\Assets\NetMeter.ico
UninstallDisplayIcon={app}\NetMeter.exe
UninstallDisplayName=NetMeter
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
RestartApplications=no

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[InstallDelete]
; Clear files from the previous version so removed DLLs don't linger.
Type: filesandordirs; Name: "{app}\*"

[Icons]
Name: "{userprograms}\NetMeter"; Filename: "{app}\NetMeter.exe"; AppUserModelID: "SadikTD.NetMeter"
Name: "{userdesktop}\NetMeter"; Filename: "{app}\NetMeter.exe"; Tasks: desktopicon

[Run]
; Also runs after silent installs, which is how in-app updates relaunch NetMeter.
Filename: "{app}\NetMeter.exe"; Description: "Launch NetMeter"; Flags: nowait postinstall

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM NetMeter.exe /F"; Flags: runhidden; RunOnceId: "StopApp"
Filename: "{sys}\reg.exe"; Parameters: "delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v NetMeter /f"; Flags: runhidden; RunOnceId: "RemoveRunKey"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN NetMeter /F"; Flags: runhidden; RunOnceId: "RemoveLogonTask"
