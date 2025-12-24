# Windows Registry Cleaner

Un nettoyeur de registre Windows moderne écrit en C++20 pour Visual Studio 2022.

## Fonctionnalités

- **Analyse complète du registre** : Détecte les entrées orphelines et invalides
- **Sauvegarde automatique** : Crée une sauvegarde .reg avant chaque nettoyage
- **Protection système** : Liste blanche de clés critiques pour éviter les dommages
- **Interface console** : Interface utilisateur claire et intuitive
- **Restauration** : Possibilité de restaurer les sauvegardes

## Catégories d'analyse

| Catégorie | Description |
|-----------|-------------|
| Entrées de désinstallation | Programmes désinstallés avec entrées résiduelles |
| Extensions de fichiers | Associations de fichiers vers des programmes inexistants |
| Fichiers récents (MRU) | Historique des fichiers récemment utilisés |
| Programmes au démarrage | Entrées de démarrage pointant vers des fichiers manquants |
| DLLs partagées | Références à des DLLs supprimées ou orphelines |

## Prérequis

- Windows 10/11 (64-bit)
- Visual Studio 2022 (v143 toolset)
- C++20 support activé
- Droits administrateur (recommandé)

## Compilation

1. Ouvrez `RegistryCleaner.sln` dans Visual Studio 2022
2. Sélectionnez la configuration `Release | x64`
3. Compilez avec `Ctrl+Shift+B`

L'exécutable sera généré dans `bin\Release\RegistryCleaner.exe`

## Utilisation

```bash
# Exécution normale (demande élévation admin)
RegistryCleaner.exe

# Exécution sans droits admin (fonctionnalités limitées)
RegistryCleaner.exe --no-admin
```

### Menu principal

1. **Sélectionner les analyses** - Active/désactive les différents scanners
2. **Analyser le registre** - Lance l'analyse complète
3. **Voir les résultats** - Affiche et sélectionne les problèmes trouvés
4. **Nettoyer** - Supprime les entrées sélectionnées (avec sauvegarde)
5. **Gérer les sauvegardes** - Liste et restaure les sauvegardes
6. **À propos** - Informations sur l'application

## Structure du projet

```
RegistryCleaner/
├── src/
│   ├── backup/           # Gestion des sauvegardes
│   │   ├── BackupManager.h
│   │   └── BackupManager.cpp
│   ├── cleaners/         # Logique de nettoyage
│   │   ├── RegistryCleaner.h
│   │   └── RegistryCleaner.cpp
│   ├── core/             # Configuration et constantes
│   │   ├── Config.h
│   │   └── ProtectedKeys.h
│   ├── registry/         # Wrappers RAII pour le registre
│   │   ├── RegistryKey.h/.cpp
│   │   ├── RegistryValue.h/.cpp
│   │   └── RegistryUtils.h/.cpp
│   ├── scanners/         # Détecteurs de problèmes
│   │   ├── BaseScanner.h/.cpp
│   │   ├── UninstallScanner.h/.cpp
│   │   ├── FileExtensionScanner.h/.cpp
│   │   ├── MRUScanner.h/.cpp
│   │   ├── StartupScanner.h/.cpp
│   │   └── SharedDllScanner.h/.cpp
│   ├── ui/               # Interface utilisateur
│   │   ├── ConsoleUI.h
│   │   └── ConsoleUI.cpp
│   ├── main.cpp
│   ├── pch.h
│   └── pch.cpp
├── RegistryCleaner.sln
├── RegistryCleaner.vcxproj
└── README.md
```

## Sécurité

### Clés protégées

Le programme protège automatiquement les clés système critiques :
- `HKEY_LOCAL_MACHINE\SYSTEM`
- `HKEY_LOCAL_MACHINE\SECURITY`
- `HKEY_LOCAL_MACHINE\SAM`
- Extensions essentielles (.exe, .dll, .bat, etc.)
- Entrées Microsoft Windows

### Sauvegardes

- Toutes les sauvegardes sont stockées dans `Documents\RegistryBackups\`
- Format `.reg` compatible avec l'éditeur de registre Windows
- Maximum 10 sauvegardes conservées (les plus anciennes sont supprimées)

## Avertissement

⚠️ **ATTENTION** : La modification du registre Windows peut rendre votre système instable ou inutilisable. Utilisez cet outil à vos propres risques. Assurez-vous toujours d'avoir une sauvegarde complète de votre système avant d'effectuer des modifications.

## Licence

Ce projet est fourni "tel quel" sans garantie. Utilisation à vos propres risques.

## Contribution

Les contributions sont les bienvenues ! N'hésitez pas à soumettre des issues ou des pull requests.
