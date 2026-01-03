; QuickLauncher Inno Setup Script
; Version 1.0.0

#define MyAppName "QuickLauncher"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Felix-Antoine"
#define MyAppExeName "QuickLauncher.exe"
#define MyAppDescription "Application Launcher style Spotlight/PowerToys Run"

[Setup]
AppId={{B2C3D4E5-F6A7-8901-BCDE-F23456789012}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\output
OutputBaseFilename=QuickLauncher_v{#MyAppVersion}_Setup
SetupIconFile=..\..\QuickLauncher\Resources\app.ico
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

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Lancer au démarrage de Windows"; GroupDescription: "Options:"; Flags: unchecked

[Files]
Source: "..\..\QuickLauncher\bin\Release\net9.0-windows\QuickLauncher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\QuickLauncher\bin\Release\net9.0-windows\QuickLauncher.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\QuickLauncher\bin\Release\net9.0-windows\QuickLauncher.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\QuickLauncher\bin\Release\net9.0-windows\QuickLauncher.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\QuickLauncher\bin\Release\net9.0-windows\CommunityToolkit.Mvvm.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\QuickLauncher\bin\Release\net9.0-windows\System.Data.SQLite.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\QuickLauncher\bin\Release\net9.0-windows\H.NotifyIcon.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\QuickLauncher\bin\Release\net9.0-windows\H.NotifyIcon.Wpf.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\QuickLauncher\bin\Release\net9.0-windows\H.GeneratedIcons.System.Drawing.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\QuickLauncher\bin\Release\net9.0-windows\runtimes\win-x64\native\*"; DestDir: "{app}\runtimes\win-x64\native"; Flags: ignoreversion recursesubdirs createallsubdirs

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
