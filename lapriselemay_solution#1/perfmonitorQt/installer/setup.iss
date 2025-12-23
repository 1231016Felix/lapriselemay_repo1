; ============================================================================
; PerfMonitorQt Installer Script
; Inno Setup 6.x
; ============================================================================
; 
; INSTRUCTIONS POUR MODIFIER:
; 
; 1. Changer la version:
;    - Modifier #define MyAppVersion "1.1.0" ci-dessous
;
; 2. Apres avoir compile le projet:
;    - Ouvrir ce fichier avec Inno Setup Compiler
;    - Cliquer sur "Compile" (Ctrl+F9)
;    - L'installeur sera cree dans: installer\output\
;
; 3. Pour ajouter des fichiers:
;    - Ajouter une ligne dans la section [Files]
;
; ============================================================================

; ==== CONFIGURATION - MODIFIER ICI ====
#define MyAppName "PerfMonitorQt"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Felix-Antoine"
#define MyAppURL "https://github.com/felix-antoine/perfmonitorqt"
#define MyAppExeName "PerfMonitorQt.exe"

; Chemin vers le dossier Release (relatif au dossier installer)
#define BuildDir "..\build\Release"
#define ProjectDir ".."

[Setup]
; Identifiant unique de l'application (ne pas changer apres la premiere release)
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}

; Informations de l'application
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Repertoires d'installation
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}

; Fichiers de sortie
OutputDir=output
OutputBaseFilename=PerfMonitorQt_Setup_v{#MyAppVersion}

; Options d'installation
AllowNoIcons=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog

; Apparence
WizardStyle=modern
WizardSizePercent=120
UninstallDisplayIcon={app}\{#MyAppExeName}

; Compression
Compression=lzma2/ultra64
SolidCompression=yes

; Architecture
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Licence (optionnel - commenter si pas de licence)
LicenseFile={#ProjectDir}\LICENSE.txt

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startupicon"; Description: "Demarrer avec Windows"; GroupDescription: "Options de demarrage:"; Flags: unchecked

; ============================================================================
; FICHIERS A INSTALLER
; ============================================================================
[Files]
; === Application principale ===
Source: "{#BuildDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; === DLLs Qt principales ===
Source: "{#BuildDir}\Qt6Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\Qt6Gui.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\Qt6Widgets.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\Qt6Network.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\Qt6Svg.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

; === DLLs supplementaires ===
Source: "{#BuildDir}\icuuc.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#BuildDir}\dxcompiler.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#BuildDir}\dxil.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

; === Plugins Qt (dossiers) ===
Source: "{#BuildDir}\platforms\*"; DestDir: "{app}\platforms"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#BuildDir}\styles\*"; DestDir: "{app}\styles"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#BuildDir}\imageformats\*"; DestDir: "{app}\imageformats"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#BuildDir}\iconengines\*"; DestDir: "{app}\iconengines"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#BuildDir}\generic\*"; DestDir: "{app}\generic"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#BuildDir}\networkinformation\*"; DestDir: "{app}\networkinformation"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#BuildDir}\tls\*"; DestDir: "{app}\tls"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

; === Documentation ===
Source: "{#ProjectDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#ProjectDir}\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

; === Visual C++ Runtime (optionnel) ===
; Telecharger depuis: https://aka.ms/vs/17/release/vc_redist.x64.exe
; Placer dans le dossier installer\redist\
Source: "redist\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist

[Icons]
; Menu Demarrer
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "Moniteur de performance Windows"
Name: "{group}\Desinstaller {#MyAppName}"; Filename: "{uninstallexe}"

; Bureau
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; Comment: "Moniteur de performance Windows"

[Registry]
; Demarrage automatique avec Windows
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"" --minimized"; Flags: uninsdeletevalue; Tasks: startupicon

; Parametres de l'application
Root: HKCU; Subkey: "Software\{#MyAppPublisher}\{#MyAppName}"; Flags: uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\{#MyAppPublisher}\{#MyAppName}\Settings"; Flags: uninsdeletekeyifempty

[Run]
; Installer Visual C++ Runtime si present
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installation de Visual C++ Runtime..."; Flags: waituntilterminated skipifdoesntexist

; Lancer l'application apres installation
Filename: "{app}\{#MyAppExeName}"; Description: "Lancer {#MyAppName}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallDelete]
; Nettoyer les dossiers crees
Type: filesandordirs; Name: "{app}\platforms"
Type: filesandordirs; Name: "{app}\styles"
Type: filesandordirs; Name: "{app}\imageformats"
Type: filesandordirs; Name: "{app}\iconengines"
Type: filesandordirs; Name: "{app}\generic"
Type: filesandordirs; Name: "{app}\networkinformation"
Type: filesandordirs; Name: "{app}\tls"
Type: dirifempty; Name: "{app}"

[Messages]
; Messages personnalises
BeveledLabel=PerfMonitorQt v{#MyAppVersion}

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Taches post-installation
  end;
end;

// Demande de fermer l'application avant desinstallation
function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  // Essayer de fermer l'application proprement
  Exec('taskkill', '/IM {#MyAppExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
