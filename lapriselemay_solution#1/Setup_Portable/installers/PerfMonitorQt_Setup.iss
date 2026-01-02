; PerfMonitorQt Inno Setup Script
; Version 1.0.0

#define MyAppName "PerfMonitorQt"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Felix-Antoine"
#define MyAppExeName "PerfMonitorQt.exe"
#define MyAppDescription "Moniteur de performances système avancé"

[Setup]
AppId={{D4E5F6A7-B8C9-0123-DEFA-456789012345}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\output
OutputBaseFilename=PerfMonitorQt_v{#MyAppVersion}_Setup
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
; Application principale
Source: "..\..\perfmonitorQt\build\Release\PerfMonitorQt.exe"; DestDir: "{app}"; Flags: ignoreversion
; DLLs Qt Core
Source: "..\..\perfmonitorQt\build\Release\Qt6Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\perfmonitorQt\build\Release\Qt6Gui.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\perfmonitorQt\build\Release\Qt6Widgets.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\perfmonitorQt\build\Release\Qt6Charts.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\perfmonitorQt\build\Release\Qt6Network.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\perfmonitorQt\build\Release\Qt6Sql.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\perfmonitorQt\build\Release\Qt6OpenGL.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\perfmonitorQt\build\Release\Qt6OpenGLWidgets.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\perfmonitorQt\build\Release\Qt6Svg.dll"; DestDir: "{app}"; Flags: ignoreversion
; DLLs système
Source: "..\..\perfmonitorQt\build\Release\dxcompiler.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\perfmonitorQt\build\Release\dxil.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\perfmonitorQt\build\Release\icuuc.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
; Plugins Qt
Source: "..\..\perfmonitorQt\build\Release\platforms\*"; DestDir: "{app}\platforms"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\perfmonitorQt\build\Release\imageformats\*"; DestDir: "{app}\imageformats"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\perfmonitorQt\build\Release\iconengines\*"; DestDir: "{app}\iconengines"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\perfmonitorQt\build\Release\styles\*"; DestDir: "{app}\styles"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\perfmonitorQt\build\Release\sqldrivers\*"; DestDir: "{app}\sqldrivers"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\perfmonitorQt\build\Release\networkinformation\*"; DestDir: "{app}\networkinformation"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\perfmonitorQt\build\Release\tls\*"; DestDir: "{app}\tls"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\perfmonitorQt\build\Release\generic\*"; DestDir: "{app}\generic"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
