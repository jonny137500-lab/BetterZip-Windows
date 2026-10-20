#define MyAppName "ArcZip Windows"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "ArcZip Windows contributors"
#define MyAppExeName "ArcZipWindows.exe"

[Setup]
AppId={{A3C4E09B-AD7E-4E8D-B8B5-AB3E71C3C5B2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\ArcZip Windows
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=ArcZip-Windows-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\ArcZipWindows-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ArcZip Windows"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\ArcZip Windows"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch ArcZip Windows"; Flags: postinstall nowait skipifsilent
