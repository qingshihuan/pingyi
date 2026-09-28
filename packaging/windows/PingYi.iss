#ifndef Edition
#define Edition "Complete"
#endif
#ifndef MyAppVersion
#define MyAppVersion "0.1.0"
#endif
#if Edition != "Complete"
#error Only the Complete edition is supported
#endif
#define MyAppName "截屏释义 完全版"
#define MyAppId "{{AD4A31EC-4A26-41B1-B86A-D4A7360C8687}"
#define MyAppDirectory "PingYi Complete"
#define MyOutputName "PingYi-Complete-" + MyAppVersion + "-win-x64-setup"
#ifndef SourceDir
#define SourceDir "..\..\artifacts\publish\win-x64-0.1.0-complete"
#endif
#define MyAppPublisher "PingYi contributors"
#define MyAppExeName "PingYi.App.exe"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppDirectory}
DefaultGroupName={#MyAppName}
OutputBaseFilename={#MyOutputName}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#ifdef SignToolName
SignTool={#SignToolName}
SignedUninstaller=yes
#endif

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
