; WallpaperManager Inno Setup Script
; Version 1.0.0

#define MyAppName "WallpaperManager"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Felix-Antoine"
#define MyAppExeName "WallpaperManager.exe"
#define MyAppDescription "Gestionnaire de fonds d'écran avec rotation automatique, fonds animés et intégration Unsplash"

[Setup]
AppId={{C3D4E5F6-A7B8-9012-CDEF-345678901234}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\output
OutputBaseFilename=WallpaperManager_v{#MyAppVersion}_Setup
SetupIconFile=..\..\WallpaperManager\Resources\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoDescription={#MyAppDescription}
MinVersion=10.0.17763
; Taille approximative avec LibVLC
ExtraDiskSpaceRequired=150000000

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Lancer au démarrage de Windows"; GroupDescription: "Options:"; Flags: unchecked

[Files]
; Application principale
Source: "..\..\WallpaperManager\bin\Release\net9.0-windows\WallpaperManager.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\WallpaperManager\bin\Release\net9.0-windows\WallpaperManager.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\WallpaperManager\bin\Release\net9.0-windows\WallpaperManager.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\WallpaperManager\bin\Release\net9.0-windows\WallpaperManager.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
; Dépendances
Source: "..\..\WallpaperManager\bin\Release\net9.0-windows\CommunityToolkit.Mvvm.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\WallpaperManager\bin\Release\net9.0-windows\H.NotifyIcon.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\WallpaperManager\bin\Release\net9.0-windows\H.NotifyIcon.Wpf.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\WallpaperManager\bin\Release\net9.0-windows\H.GeneratedIcons.System.Drawing.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\WallpaperManager\bin\Release\net9.0-windows\LibVLCSharp.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\WallpaperManager\bin\Release\net9.0-windows\LibVLCSharp.WPF.dll"; DestDir: "{app}"; Flags: ignoreversion
; LibVLC native (x64)
Source: "..\..\WallpaperManager\bin\Release\net9.0-windows\libvlc\win-x64\*"; DestDir: "{app}\libvlc\win-x64"; Flags: ignoreversion recursesubdirs createallsubdirs
; Resources
Source: "..\..\WallpaperManager\bin\Release\net9.0-windows\Resources\*"; DestDir: "{app}\Resources"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: DirExists(ExpandConstant('..\..\WallpaperManager\bin\Release\net9.0-windows\Resources'))

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function IsDotNetInstalled(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('dotnet', '--list-runtimes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsDotNetInstalled() then
  begin
    MsgBox('Ce programme nécessite .NET 9.0 Runtime. Veuillez l''installer depuis https://dotnet.microsoft.com/download', mbError, MB_OK);
  end;
end;
