# Clean Uninstaller

Un désinstalleur intelligent et puissant pour Windows, avec interface moderne WinUI 3.

![.NET](https://img.shields.io/badge/.NET-9.0-purple)
![WinUI](https://img.shields.io/badge/WinUI-3-blue)
![Windows](https://img.shields.io/badge/Windows-11-0078D4)

## ✨ Fonctionnalités

### 🔍 Détection avancée des programmes
- **Programmes classiques** : Scan complet du registre Windows (HKLM + HKCU, 32-bit + 64-bit)
- **Applications Windows Store** : Détection des apps MSIX/AppX via PowerShell
- **Extraction d'icônes** : Affichage des icônes des programmes
- **Calcul des tailles réelles** : Analyse du dossier d'installation
- **Détection du type d'installeur** : MSI, Inno Setup, NSIS, InstallShield, etc.

### 🚀 Désinstallation intelligente
- **Désinstallation silencieuse** : Mode automatique avec arguments adaptés au type d'installeur
- **Désinstallation forcée** : Suppression complète même si le désinstalleur échoue
- **Désinstallation en lot** : Plusieurs programmes à la fois avec point de restauration
- **Élévation UAC automatique** : Demande des droits administrateur si nécessaire

### 🧹 Nettoyage des résidus puissant
- **Fichiers et dossiers** : Scan de Program Files, AppData, ProgramData, Temp
- **Registre Windows** : Détection des clés orphelines avec analyse en profondeur
- **Services Windows** : Identification des services liés au programme
- **Tâches planifiées** : Détection des tâches créées par le programme
- **Entrées de démarrage** : Scan du dossier Startup et des clés Run
- **Règles de pare-feu** : Identification des règles réseau
- **Niveau de confiance** : Score de confiance visuel pour chaque résidu

### 🛡️ Sécurité
- **Sauvegarde du registre** : Export automatique avant nettoyage
- **Point de restauration** : Création optionnelle avant désinstallation en lot
- **Confirmation** : Validation avant toute action destructive
- **Indicateurs visuels** : Badges système/Store, confiance par couleur

### 🎨 Interface moderne
- **WinUI 3** avec effet Mica
- **Thème adaptatif** : Suit les préférences système (clair/sombre)
- **Recherche instantanée** avec filtres multiples
- **Tri multi-critères** : Nom, éditeur, taille, date
- **Export** : CSV, JSON, TXT
- **InfoBar** : Notifications intégrées

## 📋 Prérequis

- Windows 10 version 1809 ou supérieur
- Windows 11 (recommandé)
- .NET 9.0 Runtime
- Droits administrateur (pour certaines opérations)

## 🚀 Installation

### Depuis Visual Studio 2022/2026
1. Ouvrir la solution `lapriselemay_solution#1.slnx`
2. Définir `CleanUninstaller` comme projet de démarrage
3. Compiler en mode Release (x64)
4. Exécuter

### Build en ligne de commande
```powershell
cd C:\git\lapriselemay_solution#1\CleanUninstaller
dotnet build -c Release
dotnet run
```

### Publication autonome
```powershell
dotnet publish -c Release -r win-x64 --self-contained
```

## 🎯 Utilisation

### Scan initial
Au lancement, l'application scanne automatiquement tous les programmes installés.

### Désinstaller un programme
1. Sélectionner le programme dans la liste
2. Cliquer sur **Désinstaller** (ou double-cliquer)
3. Choisir le mode de désinstallation si nécessaire :
   - **Standard** : Avec interface du désinstalleur
   - **Silencieuse** : Sans interaction (recommandé)
   - **Forcée** : Suppression complète même si le désinstalleur échoue
4. Les résidus sont automatiquement détectés

### Nettoyer les résidus
1. Après désinstallation, les résidus apparaissent dans le panneau de droite
2. Vérifier les éléments sélectionnés (code couleur de confiance)
3. Cliquer sur **Nettoyer les résidus sélectionnés**

### Scanner les résidus d'un programme existant
1. Sélectionner un programme (sans le désinstaller)
2. Cliquer sur **Scanner les résidus**
3. Utile pour vérifier si un programme a laissé des traces

### Exporter la liste des programmes
1. Cliquer sur **Exporter**
2. Choisir le format (CSV, JSON, TXT)
3. Sélectionner l'emplacement de sauvegarde

## ⚙️ Configuration

Accessible via le bouton **Paramètres** (⚙️) :

| Option | Description | Défaut |
|--------|-------------|--------|
| Point de restauration | Créer avant désinstallation en lot | ✅ Activé |
| Sauvegarde registre | Exporter avant nettoyage | ✅ Activé |
| Mode silencieux | Utiliser /quiet quand possible | ✅ Activé |
| Scan auto résidus | Scanner après chaque désinstallation | ✅ Activé |
| Confiance minimum | Seuil pour sélection automatique | 70% |

## 🗂️ Structure du projet

```
CleanUninstaller/
├── Assets/
│   ├── app.ico              # Icône de l'application
│   ├── app.png              # Icône source
│   └── Styles.xaml          # Styles globaux
├── Converters/
│   └── Converters.cs        # Convertisseurs XAML
├── Models/
│   ├── InstalledProgram.cs  # Modèle programme
│   ├── ResidualItem.cs      # Modèle résidu
│   ├── ScanProgress.cs      # Progression et options
│   └── UninstallResult.cs   # Résultats d'opération
├── Services/
│   ├── AdvancedDetectionService.cs  # Détection avancée
│   ├── ProgramScannerService.cs     # Scan des programmes
│   ├── RegistryService.cs           # Opérations registre
│   ├── ResidualScannerService.cs    # Détection résidus
│   ├── SettingsService.cs           # Gestion paramètres
│   ├── UninstallService.cs          # Désinstallation
│   └── WindowsAppService.cs         # Apps Windows Store
├── ViewModels/
│   └── MainViewModel.cs     # ViewModel principal
├── Views/
│   ├── MainWindow.xaml      # Fenêtre principale
│   ├── MainWindow.xaml.cs
│   ├── SettingsDialog.xaml  # Dialogue paramètres
│   └── SettingsDialog.xaml.cs
├── App.xaml                 # Application WinUI
├── App.xaml.cs
└── app.manifest             # Manifest (admin)
```

## 🔧 Technologies utilisées

- **UI Framework** : WinUI 3 (Windows App SDK 1.6)
- **Architecture** : MVVM avec CommunityToolkit.Mvvm
- **Plateforme** : .NET 9.0
- **APIs** : Registry, WMI, PowerShell, Task Scheduler

## 📊 Comparaison avec BCUninstaller

| Fonctionnalité | Clean Uninstaller | BCUninstaller |
|----------------|-------------------|---------------|
| Interface | WinUI 3 moderne | WinForms |
| Scan registre | ✅ | ✅ |
| Apps Windows Store | ✅ | ✅ |
| Résidus fichiers | ✅ | ✅ |
| Résidus registre | ✅ | ✅ |
| Services liés | ✅ | ✅ |
| Tâches planifiées | ✅ | ✅ |
| Pare-feu | ✅ | ❌ |
| Mode silencieux | ✅ | ✅ |
| Export liste | ✅ | ✅ |
| Thème sombre | ✅ Auto | ❌ |
| Effet Mica | ✅ | ❌ |

## 📝 Notes de version

### v1.0.0
- Première version complète
- Scan des programmes Win32 et Windows Store
- Détection des résidus multi-sources
- Interface WinUI 3 moderne avec effet Mica
- Export CSV/JSON/TXT
- Désinstallation silencieuse et forcée
- Système de sauvegarde du registre

## 📜 Licence

Ce projet est développé à des fins personnelles et éducatives.

---

*Développé avec ❤️ par Felix-Antoine*
