; Inno Setup template for the Windows x64 build.
; Build the app first with: ./build.sh windows
; Then compile this script on Windows with Inno Setup Compiler.
; To change the version, edit MyAppVersion below or pass it with ISCC /D.

#define MyAppName "Anime App"
#define MyAppExeName "AnimeApp.exe"
#define MyAppPublisher "Anime App"
#define MyAppId "{{8D2F2E5F-71C5-49D9-A23A-6F777A089E5B}"
#define PublishDir "..\..\publish\win-x64"
#define PublishExe "..\..\publish\win-x64\AnimeApp.exe"

#ifndef MyAppVersion
  #define MyAppVersion "1.0.2"
#endif

#if !FileExists(PublishExe)
  #error Build Windows output first: ./build.sh windows
#endif

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\..\dist\windows-installer
OutputBaseFilename=AnimeAppSetup-{#MyAppVersion}-win-x64
SetupLogging=yes
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
