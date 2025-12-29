# 🧹 TempCleaner

Nettoyeur de fichiers temporaires intelligent avec prévisualisation pour Windows 11.

## Fonctionnalités

- **Analyse intelligente** : Scan de multiples catégories de fichiers temporaires
- **Prévisualisation** : Voir tous les fichiers avant suppression
- **Filtres avancés** : Recherche par nom, catégorie, taille
- **Sélection flexible** : Tout sélectionner, inverser, sélection manuelle
- **Interface moderne** : Design Windows 11 avec thème Fluent
- **Annulation** : Possibilité d'annuler l'opération en cours

## Catégories analysées

| Catégorie | Description |
|-----------|-------------|
| 🗑️ Fichiers temporaires | Dossier TEMP Windows |
| 🔄 Cache Windows Update | Fichiers de mise à jour |
| ⚡ Prefetch | Fichiers de préchargement |
| 🌐 Cache navigateurs | Cache des navigateurs web |
| ♻️ Corbeille | Fichiers supprimés |
| 📋 Logs système | Journaux Windows |
| 🖼️ Miniatures | Cache des miniatures |
| 📥 Téléchargements anciens | Fichiers > 30 jours |
| 💥 Crash dumps | Rapports d'erreurs |

## Prérequis

- Windows 10/11
- .NET 9.0

## Architecture

```
TempCleaner/
├── Models/
│   ├── TempFileInfo.cs       # Info fichier temporaire
│   ├── CleanerProfile.cs     # Profil de nettoyage
│   └── ScanResult.cs         # Résultat d'analyse
├── Services/
│   ├── ScannerService.cs     # Service d'analyse
│   └── CleanerService.cs     # Service de nettoyage
├── ViewModels/
│   └── MainViewModel.cs      # ViewModel principal
├── Views/
│   └── MainWindow.xaml       # Fenêtre principale
├── Converters/
│   └── FileSizeConverter.cs  # Conversion taille
└── Resources/
    └── Styles.xaml           # Styles Windows 11
```

## Compilation

```powershell
cd TempCleaner
dotnet build
dotnet run
```

## Utilisation

1. **Sélectionner les catégories** à analyser (cocher/décocher)
2. **Cliquer sur "Analyser"** pour scanner les fichiers
3. **Filtrer et sélectionner** les fichiers à supprimer
4. **Cliquer sur "Nettoyer"** pour supprimer les fichiers sélectionnés

## Licence

MIT License
