; Inno Setup script for InkDot on Windows (https://jrsoftware.org/isinfo.php).
; 1. Run scripts/package-win.sh (it fills dist\InkDot-win-x64).
; 2. Open this file in Inno Setup on Windows and press Compile → dist\InkDot-Setup-<version>.exe
; Installs per user (no admin prompt), adds a Start menu entry and an optional start-at-login.

#define AppName "InkDot"
#define AppVersion "0.2.0"
#define AppPublisher "Lveno"
#define AppURL "https://lveno.agency/inkdot"
#define SourceDir "..\dist\InkDot-win-x64"

[Setup]
AppId={{6B3F1E2A-8C4D-4F7A-9E21-1D5C0B9A7E44}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=InkDot-Setup-{#AppVersion}
SetupIconFile=..\src\Lveno.App\Assets\inkdot.ico
UninstallDisplayIcon={app}\InkDot.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "startup"; Description: "Start InkDot when I sign in"; GroupDescription: "Extras:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Extras:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\InkDot.exe"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\InkDot.exe"; Tasks: desktopicon

[Registry]
; Same value InkDot's own "Start at login" switch uses.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "InkDot"; ValueData: """{app}\InkDot.exe"" --background"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\InkDot.exe"; Description: "Open InkDot"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM InkDot.exe"; Flags: runhidden; RunOnceId: "StopApp"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM OpenTabletDriver.Daemon.exe"; Flags: runhidden; RunOnceId: "StopDriver"
