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
Name: "fileassoc"; Description: "Add Open with ArcZip Windows to supported archive types (does not change the default app)"; GroupDescription: "Windows integration:"; Flags: unchecked

[Files]
Source: "..\artifacts\ArcZipWindows-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ArcZip Windows"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\ArcZip Windows"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCR; Subkey: "ArcZipWindows.Archive"; ValueType: string; ValueName: ""; ValueData: "ArcZip Windows Archive"; Tasks: fileassoc; Flags: uninsdeletekey
Root: HKCR; Subkey: "ArcZipWindows.Archive\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\ArcZipWindows.exe,0"; Tasks: fileassoc
Root: HKCR; Subkey: "ArcZipWindows.Archive\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ArcZipWindows.exe"" ""%1"""; Tasks: fileassoc
Root: HKCR; Subkey: ".zip\OpenWithProgids"; ValueType: string; ValueName: "ArcZipWindows.Archive"; ValueData: ""; Tasks: fileassoc; Flags: uninsdeletevalue
Root: HKCR; Subkey: "SystemFileAssociations\.zip\shell\ArcZipWindows"; ValueType: string; ValueName: ""; ValueData: "Open with ArcZip Windows"; Tasks: fileassoc; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.zip\shell\ArcZipWindows\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ArcZipWindows.exe"" ""%1"""; Tasks: fileassoc
Root: HKCR; Subkey: ".7z\OpenWithProgids"; ValueType: string; ValueName: "ArcZipWindows.Archive"; ValueData: ""; Tasks: fileassoc; Flags: uninsdeletevalue
Root: HKCR; Subkey: "SystemFileAssociations\.7z\shell\ArcZipWindows"; ValueType: string; ValueName: ""; ValueData: "Open with ArcZip Windows"; Tasks: fileassoc; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.7z\shell\ArcZipWindows\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ArcZipWindows.exe"" ""%1"""; Tasks: fileassoc
Root: HKCR; Subkey: ".rar\OpenWithProgids"; ValueType: string; ValueName: "ArcZipWindows.Archive"; ValueData: ""; Tasks: fileassoc; Flags: uninsdeletevalue
Root: HKCR; Subkey: "SystemFileAssociations\.rar\shell\ArcZipWindows"; ValueType: string; ValueName: ""; ValueData: "Open with ArcZip Windows"; Tasks: fileassoc; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.rar\shell\ArcZipWindows\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ArcZipWindows.exe"" ""%1"""; Tasks: fileassoc
Root: HKCR; Subkey: ".tar\OpenWithProgids"; ValueType: string; ValueName: "ArcZipWindows.Archive"; ValueData: ""; Tasks: fileassoc; Flags: uninsdeletevalue
Root: HKCR; Subkey: "SystemFileAssociations\.tar\shell\ArcZipWindows"; ValueType: string; ValueName: ""; ValueData: "Open with ArcZip Windows"; Tasks: fileassoc; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.tar\shell\ArcZipWindows\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ArcZipWindows.exe"" ""%1"""; Tasks: fileassoc
Root: HKCR; Subkey: ".tgz\OpenWithProgids"; ValueType: string; ValueName: "ArcZipWindows.Archive"; ValueData: ""; Tasks: fileassoc; Flags: uninsdeletevalue
Root: HKCR; Subkey: "SystemFileAssociations\.tgz\shell\ArcZipWindows"; ValueType: string; ValueName: ""; ValueData: "Open with ArcZip Windows"; Tasks: fileassoc; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.tgz\shell\ArcZipWindows\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ArcZipWindows.exe"" ""%1"""; Tasks: fileassoc
Root: HKCR; Subkey: ".gz\OpenWithProgids"; ValueType: string; ValueName: "ArcZipWindows.Archive"; ValueData: ""; Tasks: fileassoc; Flags: uninsdeletevalue
Root: HKCR; Subkey: "SystemFileAssociations\.gz\shell\ArcZipWindows"; ValueType: string; ValueName: ""; ValueData: "Open with ArcZip Windows"; Tasks: fileassoc; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.gz\shell\ArcZipWindows\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ArcZipWindows.exe"" ""%1"""; Tasks: fileassoc
Root: HKCR; Subkey: ".bz2\OpenWithProgids"; ValueType: string; ValueName: "ArcZipWindows.Archive"; ValueData: ""; Tasks: fileassoc; Flags: uninsdeletevalue
Root: HKCR; Subkey: "SystemFileAssociations\.bz2\shell\ArcZipWindows"; ValueType: string; ValueName: ""; ValueData: "Open with ArcZip Windows"; Tasks: fileassoc; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.bz2\shell\ArcZipWindows\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ArcZipWindows.exe"" ""%1"""; Tasks: fileassoc
Root: HKCR; Subkey: ".xz\OpenWithProgids"; ValueType: string; ValueName: "ArcZipWindows.Archive"; ValueData: ""; Tasks: fileassoc; Flags: uninsdeletevalue
Root: HKCR; Subkey: "SystemFileAssociations\.xz\shell\ArcZipWindows"; ValueType: string; ValueName: ""; ValueData: "Open with ArcZip Windows"; Tasks: fileassoc; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.xz\shell\ArcZipWindows\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ArcZipWindows.exe"" ""%1"""; Tasks: fileassoc
Root: HKCR; Subkey: ".ar\OpenWithProgids"; ValueType: string; ValueName: "ArcZipWindows.Archive"; ValueData: ""; Tasks: fileassoc; Flags: uninsdeletevalue
Root: HKCR; Subkey: "SystemFileAssociations\.ar\shell\ArcZipWindows"; ValueType: string; ValueName: ""; ValueData: "Open with ArcZip Windows"; Tasks: fileassoc; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.ar\shell\ArcZipWindows\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ArcZipWindows.exe"" ""%1"""; Tasks: fileassoc
Root: HKCR; Subkey: ".arj\OpenWithProgids"; ValueType: string; ValueName: "ArcZipWindows.Archive"; ValueData: ""; Tasks: fileassoc; Flags: uninsdeletevalue
Root: HKCR; Subkey: "SystemFileAssociations\.arj\shell\ArcZipWindows"; ValueType: string; ValueName: ""; ValueData: "Open with ArcZip Windows"; Tasks: fileassoc; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.arj\shell\ArcZipWindows\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ArcZipWindows.exe"" ""%1"""; Tasks: fileassoc
Root: HKCR; Subkey: ".ace\OpenWithProgids"; ValueType: string; ValueName: "ArcZipWindows.Archive"; ValueData: ""; Tasks: fileassoc; Flags: uninsdeletevalue
Root: HKCR; Subkey: "SystemFileAssociations\.ace\shell\ArcZipWindows"; ValueType: string; ValueName: ""; ValueData: "Open with ArcZip Windows"; Tasks: fileassoc; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.ace\shell\ArcZipWindows\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ArcZipWindows.exe"" ""%1"""; Tasks: fileassoc
Root: HKCR; Subkey: ".lzw\OpenWithProgids"; ValueType: string; ValueName: "ArcZipWindows.Archive"; ValueData: ""; Tasks: fileassoc; Flags: uninsdeletevalue
Root: HKCR; Subkey: "SystemFileAssociations\.lzw\shell\ArcZipWindows"; ValueType: string; ValueName: ""; ValueData: "Open with ArcZip Windows"; Tasks: fileassoc; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.lzw\shell\ArcZipWindows\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ArcZipWindows.exe"" ""%1"""; Tasks: fileassoc

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch ArcZip Windows"; Flags: postinstall nowait skipifsilent
