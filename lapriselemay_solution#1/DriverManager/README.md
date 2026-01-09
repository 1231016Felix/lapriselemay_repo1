# Driver Manager

Gestionnaire de pilotes Windows avec interface graphique Dear ImGui.

## Fonctionnalités

- **Scanner les pilotes** : Énumère tous les pilotes installés sur le système
- **Affichage par catégorie** : Système, Affichage, Audio, Réseau, Stockage, USB, Bluetooth, etc.
- **Détails complets** : Version, fabricant, date, Hardware ID, status
- **Gestion des pilotes** :
  - Activer/Désactiver un pilote
  - Désinstaller un pilote
  - Exporter la liste des pilotes
- **Recherche** : Filtrer les pilotes par nom ou fabricant
- **Indicateurs visuels** : Status coloré (OK, Avertissement, Erreur, Désactivé)

## Technologies

- **C++20** avec Visual Studio 2025/2026
- **Dear ImGui** pour l'interface graphique (immediate mode GUI)
- **DirectX 11** pour le rendu
- **Windows SetupAPI** pour l'énumération des périphériques
- **Configuration Manager API** pour le status des pilotes

## Structure du projet

```
DriverManager/
├── main.cpp                 # Point d'entrée, setup ImGui/DX11, UI principale
├── src/
│   ├── DriverInfo.h         # Structures de données
│   └── DriverScanner.h/.cpp # Logique de scan des pilotes
├── imgui/                   # Bibliothèque Dear ImGui
│   ├── imgui.cpp/h          # Core ImGui
│   ├── imgui_impl_win32.*   # Backend plateforme
│   └── imgui_impl_dx11.*    # Backend rendu
└── DriverManager.vcxproj    # Projet Visual Studio
```

## Compilation

1. Ouvrir la solution `lapriselemay_solution#1.slnx` dans Visual Studio 2025+
2. Sélectionner la configuration `Release|x64`
3. Compiler le projet `DriverManager`

## Utilisation

1. Lancer l'application (nécessite les droits administrateur pour certaines opérations)
2. Cliquer sur **Scanner** ou appuyer sur **F5** pour détecter les pilotes
3. Parcourir les catégories dans le panneau de gauche
4. Cliquer sur un pilote pour voir ses détails
5. Utiliser les boutons **Activer/Désactiver/Désinstaller** selon les besoins

## Raccourcis clavier

- **F5** : Scanner les pilotes
- **Ctrl+E** : Exporter la liste
- **Alt+F4** : Quitter

## Notes

- Certaines opérations (désactiver, désinstaller) nécessitent les droits administrateur
- La désinstallation d'un pilote système peut rendre le périphérique inutilisable
- Un redémarrage peut être nécessaire après certaines modifications

## Licence

Projet personnel - Usage libre
