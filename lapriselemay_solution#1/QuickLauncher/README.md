# 🚀 QuickLauncher - Guide d'utilisation

> **QuickLauncher** est un lanceur d'applications rapide inspiré de Spotlight (macOS) et PowerToys Run (Windows). Il permet de rechercher et lancer des applications, fichiers, dossiers et effectuer des recherches web en quelques frappes.

---

## 📑 Table des matières

1. [Installation et démarrage](#installation-et-démarrage)
2. [Utilisation de base](#utilisation-de-base)
3. [Raccourcis clavier](#raccourcis-clavier)
4. [Commandes système](#commandes-système)
5. [Recherche web](#recherche-web)
6. [Actions rapides (Menu contextuel)](#actions-rapides-menu-contextuel)
7. [Contrôle système rapide](#contrôle-système-rapide)
8. [Icônes natives](#icônes-natives)
9. [Accéder aux paramètres](#accéder-aux-paramètres)
10. [Configuration des paramètres](#configuration-des-paramètres)
11. [Conseils et astuces](#conseils-et-astuces)
12. [Dépannage](#dépannage)

---

## Installation et démarrage

### Prérequis
- Windows 10/11
- .NET 9.0 Runtime

### Premier lancement
1. Lancez `QuickLauncher.exe`
2. L'application démarre minimisée dans la zone de notification (près de l'horloge)
3. Une icône apparaît dans la barre des tâches système
4. L'indexation des fichiers commence automatiquement en arrière-plan

### Démarrage automatique avec Windows
Par défaut, QuickLauncher est configuré pour démarrer avec Windows. Vous pouvez désactiver cette option dans les paramètres.

---

## Utilisation de base

### Ouvrir QuickLauncher
Appuyez sur **`Alt + Espace`** (raccourci par défaut) pour faire apparaître la fenêtre de recherche.

### Rechercher et lancer
1. Commencez à taper le nom de l'application ou du fichier
2. Les résultats apparaissent instantanément
3. Utilisez les flèches **↑↓** pour naviguer
4. Appuyez sur **Entrée** pour lancer l'élément sélectionné

### Fermer la fenêtre
- Appuyez sur **Échap**
- Ou cliquez en dehors de la fenêtre

### Types de résultats

| Icône | Type | Description |
|:-----:|------|-------------|
| 🚀 | Application | Fichiers exécutables (.exe, .lnk) |
| 📄 | Fichier | Documents, médias, etc. |
| 📁 | Dossier | Répertoires |
| ⚡ | Script | Scripts (.bat, .cmd, .ps1) |
| 🔍 | Recherche web | Recherche sur un moteur web |
| ⚙️ | Commande système | Commandes intégrées |
| 🕐 | Historique | Recherches récentes |
| 🧮 | Calculatrice | Résultats de calcul |

---

## Raccourcis clavier

### Raccourci global
| Raccourci | Action |
|-----------|--------|
| `Alt + Espace` | Ouvrir/Fermer QuickLauncher (configurable) |

### Dans la fenêtre de recherche

| Raccourci | Action |
|-----------|--------|
| `Entrée` | Lancer l'élément sélectionné |
| `↑` / `↓` | Naviguer dans les résultats |
| `Tab` | Sélection suivante |
| `Shift + Tab` | Sélection précédente |
| `Échap` | Fermer la fenêtre |
| `Ctrl + ,` | Ouvrir les paramètres |
| `Ctrl + R` | Réindexer les fichiers |
| `Ctrl + Q` | Quitter l'application |

### Personnaliser le raccourci global
Vous pouvez modifier le raccourci global dans **Paramètres → Raccourcis**. Options disponibles :
- Modificateurs : `Alt`, `Ctrl`, `Shift`, `Win`
- Touches : `Space`, `Enter`, `Tab`, `Q`, `L`, `R`, `F1`, `F2`, `F12`

> ⚠️ Le changement de raccourci nécessite un redémarrage de l'application.

---

## Commandes système

QuickLauncher intègre des commandes système accessibles en tapant directement dans la barre de recherche :

| Commande | Alternative | Description |
|----------|-------------|-------------|
| `:settings` | `settings` | Ouvrir les paramètres |
| `:reload` | `:reindex` | Réindexer tous les fichiers |
| `:history` | - | Afficher l'historique de recherche |
| `:clear` | - | Effacer l'historique de recherche |
| `:help` | `?` | Afficher l'aide et les commandes |
| `:quit` | `:exit` | Quitter QuickLauncher |

### Exemple d'utilisation
```
:settings    → Ouvre la fenêtre des paramètres
:reload      → Lance une réindexation complète
?            → Affiche toutes les commandes disponibles
```

---

## Recherche web

QuickLauncher permet d'effectuer des recherches web directement depuis la barre de recherche en utilisant des préfixes.

### Moteurs de recherche intégrés

| Préfixe | Moteur | Exemple |
|---------|--------|---------|
| `g` | Google | `g recette pizza` |
| `yt` | YouTube | `yt tutoriel python` |
| `gh` | GitHub | `gh awesome react` |
| `so` | Stack Overflow | `so c# async await` |

### Comment utiliser
1. Tapez le préfixe suivi d'un espace
2. Entrez votre recherche
3. Appuyez sur Entrée

```
g météo Montréal        → Recherche Google "météo Montréal"
yt learn javascript     → Recherche YouTube "learn javascript"
gh dotnet wpf           → Recherche GitHub "dotnet wpf"
```

---

## Actions rapides (Menu contextuel)

Faites un **clic droit** sur n'importe quel résultat de recherche pour accéder à des actions supplémentaires.

### Actions sur les fichiers

| Action | Description |
|--------|-------------|
| 📂 Ouvrir l'emplacement | Ouvre l'Explorateur avec le fichier sélectionné |
| 📋 Copier le chemin | Copie le chemin complet dans le presse-papiers |
| 📝 Copier le nom | Copie uniquement le nom du fichier |
| ✏️ Renommer... | Ouvre une boîte de dialogue pour renommer |
| 📁 Déplacer vers... | Déplace le fichier vers un autre dossier |
| 🗑️ Supprimer | Envoie le fichier à la corbeille |

### Actions sur les applications

| Action | Description |
|--------|-------------|
| 🔑 Exécuter en admin | Lance l'application avec les droits administrateur |
| 📎 Ouvrir avec... | Choisir une application pour ouvrir le fichier |

### Actions Terminal

| Action | Description |
|--------|-------------|
| 💻 Terminal ici | Ouvre Windows Terminal dans le dossier du fichier |
| 📟 PowerShell ici | Ouvre PowerShell dans le dossier du fichier |

---

## Contrôle système rapide

Contrôlez votre système directement depuis la barre de recherche en tapant des commandes commençant par `:`.

### 🔊 Audio

| Commande | Description |
|----------|-------------|
| `:volume 50` | Règle le volume à 50% |
| `:volume up` | Augmente le volume de 10% |
| `:volume down` | Diminue le volume de 10% |
| `:mute` | Bascule le mode muet |

### ☀️ Écran

| Commande | Description |
|----------|-------------|
| `:brightness 80` | Règle la luminosité à 80% (laptops uniquement) |

### 📶 Réseau

| Commande | Description |
|----------|-------------|
| `:wifi on` | Active le WiFi |
| `:wifi off` | Désactive le WiFi |
| `:wifi status` | Affiche l'état du WiFi |

### 🔒 Système

| Commande | Description |
|----------|-------------|
| `:lock` | Verrouille la session Windows |
| `:sleep` | Met l'ordinateur en veille |
| `:hibernate` | Met en hibernation |
| `:shutdown` | Éteint l'ordinateur |
| `:restart` | Redémarre l'ordinateur |

### 📸 Capture d'écran

| Commande | Description |
|----------|-------------|
| `:screenshot` | Capture tous les écrans |
| `:screenshot snip` | Ouvre l'outil de capture Windows |
| `:ss primary` | Capture l'écran principal uniquement |

> 💡 Les captures sont sauvegardées dans `Images\Screenshots`

---

## Icônes natives

QuickLauncher affiche automatiquement les **vraies icônes** de vos applications au lieu des emojis génériques.

### Types supportés

- ✅ Applications (.exe)
- ✅ Raccourcis (.lnk)
- ✅ Fichiers (icône selon le type)
- ✅ Dossiers
- ✅ Applications du Microsoft Store

> 💡 Les icônes sont mises en cache pour des performances optimales. Si une icône ne s'affiche pas, un emoji de fallback est utilisé.

---

## Accéder aux paramètres

Plusieurs méthodes pour ouvrir les paramètres :

| Méthode | Comment faire |
|---------|---------------|
| 🖱️ **Icône système** | Clic droit sur l'icône → "⚙️ Paramètres..." |
| ⌨️ **Commande** | Taper `:settings` ou `settings` dans la recherche |
| ⌨️ **Raccourci** | `Ctrl + ,` depuis la fenêtre de recherche |
| 🖱️ **Bouton** | Cliquer sur ⚙️ en haut à droite de la barre de recherche |

---

## Configuration des paramètres

### 🏠 Onglet Général

#### Démarrage
| Option | Description |
|--------|-------------|
| Démarrer avec Windows | Lance QuickLauncher au démarrage de Windows |
| Démarrer minimisé | Démarre dans la zone de notification |
| Afficher dans la barre des tâches | Icône visible dans la taskbar |

#### Comportement
| Option | Description |
|--------|-------------|
| Fermer après lancement | Masque la fenêtre après avoir lancé un élément |
| Afficher le statut d'indexation | Montre la progression de l'indexation |
| Afficher le bouton ⚙️ | Bouton paramètres dans la barre de recherche |

#### Position de la fenêtre
| Option | Description |
|--------|-------------|
| Centré sur l'écran | Position par défaut au centre |
| En haut de l'écran | Fenêtre positionnée en haut |
| Mémoriser la position | Garde la dernière position utilisée |

#### Résultats de recherche
- **Nombre maximum** : 3 à 15 résultats affichés (défaut: 8)

#### Historique de recherche
| Option | Description |
|--------|-------------|
| Activer l'historique | Mémorise vos recherches récentes |
| Nombre max d'entrées | 5 à 30 entrées (défaut: 10) |
| Effacer l'historique | Supprime tout l'historique |

---

### 🎨 Onglet Apparence

#### Thème
- 🌙 **Sombre** (par défaut)
- ☀️ **Clair** (à venir)
- 💻 **Système** (suit le thème Windows)

#### Transparence
- Ajustez l'opacité de la fenêtre de 50% à 100%

#### Couleur d'accent
Couleurs disponibles :
- 🔵 Bleu (par défaut)
- 🟢 Vert
- 🔴 Rouge
- 🟠 Orange
- 🟣 Violet
- 🩷 Rose
- 🩵 Turquoise

#### Animations
- Activer/désactiver les animations de transition

---

### ⌨️ Onglet Raccourcis

#### Raccourci clavier global
Configurez la combinaison de touches pour ouvrir QuickLauncher :

**Modificateurs disponibles :**
- `Alt` ✓
- `Ctrl`
- `Shift`
- `Win`

**Touches disponibles :**
- `Space` (défaut), `Enter`, `Tab`
- `Q`, `L`, `R`
- `F1`, `F2`, `F12`

#### Raccourcis intégrés (non modifiables)
| Raccourci | Action |
|-----------|--------|
| `Ctrl + ,` | Paramètres |
| `Ctrl + R` | Réindexer |
| `Ctrl + Q` | Quitter |
| `Échap` | Fermer |

---

### 📁 Onglet Indexation

#### Dossiers indexés
Par défaut, QuickLauncher indexe :
- Menu Démarrer (utilisateur)
- Menu Démarrer (commun)
- Bureau
- Mes Documents

**Actions :**
- ➕ **Ajouter** : Ajouter un nouveau dossier
- ➖ **Supprimer** : Retirer un dossier (minimum 1 requis)

#### Extensions de fichiers
Extensions indexées par défaut :
```
.exe, .lnk, .bat, .cmd, .ps1, .msi,
.txt, .pdf, .docx, .xlsx, .pptx,
.png, .jpg, .jpeg, .gif, .mp3, .mp4
```

Modifiez la liste en séparant les extensions par des virgules.

#### Options d'indexation
| Option | Description |
|--------|-------------|
| Profondeur de recherche | 1 à 10 niveaux de sous-dossiers (défaut: 5) |
| Indexer les dossiers cachés | Inclut les dossiers masqués |

#### Réindexer
Cliquez sur **Réindexer** pour reconstruire l'index complet. Utile après avoir ajouté de nouveaux dossiers ou fichiers.

---

### 🌐 Onglet Recherche Web

Affiche la liste des moteurs de recherche configurés et les commandes spéciales disponibles.

---

### ℹ️ Onglet À propos

#### Statistiques
- Taille de l'index
- Dernière indexation
- Nombre de dossiers surveillés
- Extensions indexées
- Moteurs de recherche
- Entrées dans l'historique

#### Emplacement des données
Chemin vers le fichier de configuration :
```
%APPDATA%\QuickLauncher\settings.json
```

#### Zone dangereuse
- **Réinitialiser les paramètres** : Remet tous les paramètres par défaut (irréversible)

---

## Conseils et astuces

### 💡 Recherche efficace
1. **Tapez peu, trouvez vite** : Quelques lettres suffisent souvent
2. **Utilisez l'historique** : Vos recherches récentes apparaissent automatiquement
3. **Apprenez les préfixes web** : `g`, `yt`, `gh`, `so` pour des recherches rapides

### 💡 Productivité
1. **Alt + Espace** devient un réflexe : Plus rapide que chercher dans le menu Démarrer
2. **Ctrl + ,** pour les paramètres : Sans quitter le clavier
3. **Tapez `?`** si vous oubliez une commande

### 💡 Organisation
1. **Ajoutez vos dossiers de projets** : Indexez vos dossiers de travail fréquents
2. **Personnalisez les extensions** : Ajoutez les types de fichiers que vous utilisez
3. **Ajustez la profondeur** : Augmentez si vos fichiers sont profondément imbriqués

### 💡 Déplacement de la fenêtre
Cliquez et maintenez n'importe où sur la fenêtre (hors champ de texte et liste) pour la déplacer.

---

## Dépannage

### QuickLauncher ne s'ouvre pas avec Alt + Espace
1. Vérifiez que l'application est bien lancée (icône dans la zone de notification)
2. Un autre programme utilise peut-être ce raccourci
3. Changez le raccourci dans les paramètres

### Les fichiers ne sont pas trouvés
1. Vérifiez que le dossier contenant le fichier est indexé
2. Vérifiez que l'extension du fichier est dans la liste
3. Lancez une réindexation (`:reload` ou `Ctrl + R`)

### L'indexation est lente
1. Réduisez la profondeur de recherche
2. Retirez les dossiers avec beaucoup de fichiers non pertinents
3. Désactivez l'indexation des dossiers cachés

### Réinitialiser en cas de problème
1. Tapez `:settings` → Onglet "À propos"
2. Cliquez sur "Réinitialiser les paramètres"
3. L'application redémarre avec les valeurs par défaut

### Emplacement des fichiers de données
```
%APPDATA%\QuickLauncher\
├── settings.json    # Configuration
├── index.db         # Base de données d'index
└── app.log          # Journal d'erreurs
```

---

## Résumé des raccourcis

| Contexte | Raccourci | Action |
|----------|-----------|--------|
| Global | `Alt + Espace` | Ouvrir QuickLauncher |
| Recherche | `Entrée` | Lancer |
| Recherche | `↑` / `↓` | Naviguer |
| Recherche | `Échap` | Fermer |
| Recherche | `Ctrl + ,` | Paramètres |
| Recherche | `Ctrl + R` | Réindexer |
| Recherche | `Ctrl + Q` | Quitter |

---

## Résumé des commandes

### Commandes de base

| Commande | Action |
|----------|--------|
| `:settings` | Paramètres |
| `:reload` | Réindexer |
| `:history` | Historique |
| `:clear` | Effacer historique |
| `:help` / `?` | Aide |
| `:quit` | Quitter |

### Recherche web

| Commande | Action |
|----------|--------|
| `g [texte]` | Google |
| `yt [texte]` | YouTube |
| `gh [texte]` | GitHub |
| `so [texte]` | Stack Overflow |

### Contrôle système

| Commande | Action |
|----------|--------|
| `:volume [0-100]` | Régler le volume |
| `:mute` | Basculer muet |
| `:brightness [0-100]` | Régler luminosité |
| `:wifi [on/off]` | WiFi on/off |
| `:lock` | Verrouiller PC |
| `:sleep` | Mise en veille |
| `:screenshot` | Capture d'écran |
| `:shutdown` | Éteindre |
| `:restart` | Redémarrer |

---

<div align="center">

**QuickLauncher** v1.0.0  
Développé par Felix-Antoine

</div>
