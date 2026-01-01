# Wallpaper Manager

Gestionnaire de fonds d'écran moderne pour Windows avec rotation automatique, support des fonds animés et intégration Unsplash.

## ✨ Fonctionnalités

### 📚 Bibliothèque
- Gestion des fonds d'écran (images, GIF, vidéos)
- Aperçu en miniature
- Favoris
- Informations détaillées (résolution, taille)

### 🔄 Rotation automatique
- Changement automatique à intervalle configurable
- Mode aléatoire ou séquentiel
- Contrôles précédent/suivant
- Pause/reprise

### 🌐 Intégration Unsplash
- Recherche de photos haute qualité
- Photos aléatoires
- Téléchargement et application en un clic
- Attribution automatique des auteurs

### 🎬 Fonds d'écran animés
- Support des GIF animés
- Support des vidéos (MP4, WebM, AVI)
- Lecture en boucle
- Contrôle du volume

### 📁 Collections
- Organisation par collections personnalisées
- Rotation par collection
- Gestion facile

## 🚀 Installation

1. Cloner le repository
2. Ouvrir la solution dans Visual Studio 2022+
3. Restaurer les packages NuGet
4. Compiler et exécuter

### Dépendances
- .NET 9.0
- CommunityToolkit.Mvvm
- H.NotifyIcon.Wpf
- Newtonsoft.Json
- LibVLCSharp (pour les fonds animés)

## ⚙️ Configuration

### Clé API Unsplash
1. Créer un compte sur [unsplash.com/developers](https://unsplash.com/developers)
2. Créer une nouvelle application
3. Copier l'Access Key
4. Coller dans Paramètres > Unsplash API

### Fonds animés
Pour les fonds d'écran vidéo, assurez-vous que VLC Media Player est installé sur votre système.

## 🎨 Thème

L'application utilise un thème sombre moderne avec des couleurs primaires indigo/violet.

## 📝 Raccourcis clavier

| Raccourci | Action |
|-----------|--------|
| Ctrl+Alt+Droite | Fond d'écran suivant |
| Ctrl+Alt+Gauche | Fond d'écran précédent |
| Ctrl+Alt+Espace | Pause/reprise rotation |

## 🔧 Architecture

```
WallpaperManager/
├── Models/          # Modèles de données
├── ViewModels/      # MVVM ViewModels
├── Views/           # Interface utilisateur XAML
├── Services/        # Services métier
├── Native/          # API Windows natives
├── Converters/      # Convertisseurs WPF
└── Resources/       # Ressources (icônes, etc.)
```

## 📄 Licence

MIT License - Voir LICENSE pour plus de détails.

## 👤 Auteur

Felix-Antoine - 2025
