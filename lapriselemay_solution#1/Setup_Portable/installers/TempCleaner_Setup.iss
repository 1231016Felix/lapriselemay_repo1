; TempCleaner Inno Setup Script
; Version 1.0.0

#define MyAppName "TempCleaner"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Felix-Antoine"
#define MyAppExeName "TempCleaner.exe"
#define MyAppDescription "Nettoyeur de fichiers temporaires intelligent avec prévisualisation pour Windows 11"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=
OutputDir=..\output
OutputBaseFilename=TempCleaner_v{#MyAppVersion}_Setup
SetupIconFile=..\..\TempCleaner\Resources\app.ico
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
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1; Check: not IsAdminInstallMode

[Files]
Source: "..\..\TempCleaner\bin\Release\net9.0-windows\TempCleaner.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\TempCleaner\bin\Release\net9.0-windows\TempCleaner.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\TempCleaner\bin\Release\net9.0-windows\TempCleaner.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\TempCleaner\bin\Release\net9.0-windows\TempCleaner.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\TempCleaner\bin\Release\net9.0-windows\CommunityToolkit.Mvvm.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\TempCleaner\bin\Release\net9.0-windows\Newtonsoft.Json.dll"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

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
