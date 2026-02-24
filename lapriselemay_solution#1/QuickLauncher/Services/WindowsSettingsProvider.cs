using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Fournit la liste des pages de paramètres Windows accessibles via la recherche.
/// Extrait de IndexingService pour réduire ses responsabilités (Amélioration #3).
/// </summary>
public static class WindowsSettingsProvider
{
    /// <summary>
    /// Retourne une liste de pages de paramètres Windows courantes
    /// pour les rendre accessibles via la recherche primaire (sans préfixe :).
    /// Chaque item utilise un URI ms-settings: ou une commande control panel.
    /// </summary>
    public static List<IndexedItem> GetItems()
    {
        return
        [
            // === Système ===
            WinSetting("⚙️ Paramètres Windows", "ms-settings:", "Ouvrir les paramètres Windows"),
            WinSetting("🖥️ Affichage", "ms-settings:display", "Résolution, mise à l'échelle, écrans multiples"),
            WinSetting("🔊 Son", "ms-settings:sound", "Volume, périphériques audio, sortie sonore"),
            WinSetting("🔔 Notifications", "ms-settings:notifications", "Notifications et actions rapides"),
            WinSetting("⚡ Alimentation et batterie", "ms-settings:powersleep", "Mode veille, économie d'énergie, alimentation"),
            WinSetting("💾 Stockage", "ms-settings:storagesense", "Espace disque, nettoyage, assistant de stockage"),
            WinSetting("📱 Multitâche", "ms-settings:multitasking", "Bureaux virtuels, ancrage des fenêtres"),
            WinSetting("ℹ️ Informations système", "ms-settings:about", "À propos de votre PC, nom d'ordinateur, spécifications"),
            
            // === Réseau ===
            WinSetting("🌐 Réseau et Internet", "ms-settings:network", "Wi-Fi, Ethernet, VPN, proxy, état du réseau"),
            WinSetting("📶 Wi-Fi", "ms-settings:network-wifi", "Connexions Wi-Fi, réseaux connus"),
            WinSetting("🔒 VPN", "ms-settings:network-vpn", "Connexions VPN"),
            WinSetting("🌐 Proxy", "ms-settings:network-proxy", "Configuration du proxy réseau"),
            
            // === Personnalisation ===
            WinSetting("🎨 Personnalisation", "ms-settings:personalization", "Thème, couleurs, fond d'écran, verrouillage"),
            WinSetting("🖼️ Arrière-plan", "ms-settings:personalization-background", "Fond d'écran, diaporama"),
            WinSetting("🎨 Couleurs", "ms-settings:personalization-colors", "Couleur d'accentuation, mode sombre/clair"),
            WinSetting("🔒 Écran de verrouillage", "ms-settings:lockscreen", "Écran de verrouillage, notifications"),
            WinSetting("📌 Barre des tâches", "ms-settings:taskbar", "Barre des tâches, icônes système"),
            WinSetting("🗔️ Menu Démarrer", "ms-settings:personalization-start", "Disposition du menu Démarrer"),
            
            // === Applications ===
            WinSetting("📦 Applications installées", "ms-settings:appsfeatures", "Désinstaller, déplacer, paramètres d'applications"),
            WinSetting("📦 Applications par défaut", "ms-settings:defaultapps", "Navigateur, lecteur PDF, musique par défaut"),
            WinSetting("🚀 Applications au démarrage", "ms-settings:startupapps", "Gérer les applications qui se lancent au démarrage"),
            
            // === Comptes ===
            WinSetting("👤 Comptes", "ms-settings:yourinfo", "Informations de compte, photo de profil"),
            WinSetting("👥 Famille et autres utilisateurs", "ms-settings:otherusers", "Ajouter des utilisateurs"),
            WinSetting("🔑 Options de connexion", "ms-settings:signinoptions", "Mot de passe, PIN, Windows Hello, empreinte"),
            
            // === Heure et langue ===
            WinSetting("🕒 Date et heure", "ms-settings:dateandtime", "Fuseau horaire, horloge, format de date"),
            WinSetting("🌐 Langue et région", "ms-settings:regionlanguage", "Langue d'affichage, format régional"),
            WinSetting("⌨️ Clavier", "ms-settings:typing", "Saisie, correction automatique, clavier tactile"),
            
            // === Mise à jour et sécurité ===
            WinSetting("🔄 Windows Update", "ms-settings:windowsupdate", "Mises à jour, historique, options avancées"),
            WinSetting("🛡️ Sécurité Windows", "ms-settings:windowsdefender", "Antivirus, pare-feu, protection"),
            WinSetting("💾 Sauvegarde", "ms-settings:backup", "Sauvegarde de fichiers, OneDrive"),
            WinSetting("🔧 Récupération", "ms-settings:recovery", "Réinitialiser le PC, démarrage avancé"),
            
            // === Accessibilité ===
            WinSetting("♿ Accessibilité", "ms-settings:easeofaccess", "Vision, audition, interaction, accessibilité"),
            
            // === Confidentialité ===
            WinSetting("🔒 Confidentialité", "ms-settings:privacy", "Autorisations, diagnostics, historique d'activité"),
            
            // === Périphériques ===
            WinSetting("🖨️ Imprimantes et scanners", "ms-settings:printers", "Ajouter une imprimante, gérer les périphériques d'impression"),
            WinSetting("🖱️ Souris", "ms-settings:mousetouchpad", "Vitesse du curseur, boutons, pavé tactile"),
            WinSetting("📱 Bluetooth", "ms-settings:bluetooth", "Appareils Bluetooth, couplage"),
            
            // === Recherche et indexation ===
            WinSetting("🔍 Options d'indexation", "control|srchadmin.dll", "Indexation Windows, emplacements indexés, reconstruction d'index"),
            WinSetting("🔍 Recherche Windows", "ms-settings:search-permissions", "Autorisations de recherche, indexation, recherche améliorée"),
            WinSetting("🔎 Paramètres de recherche", "ms-settings:cortana-windowssearch", "Recherche Windows, historique de recherche"),
            
            // === Panneau de configuration classique ===
            WinSetting("🛠️ Panneau de configuration", "control|", "Panneau de configuration classique Windows"),
            WinSetting("💻 Gestionnaire de périphériques", "devmgmt.msc", "Pilotes, matériel, périphériques"),
            WinSetting("📀 Gestion des disques", "diskmgmt.msc", "Partitions, volumes, formatage de disques"),
            WinSetting("🔧 Services Windows", "services.msc", "Gérer les services système"),
            WinSetting("📊 Moniteur de performances", "perfmon.msc", "Performances système, compteurs"),
            WinSetting("📃 Événements Windows", "eventvwr.msc", "Observateur d'événements, journaux système"),
            WinSetting("🔥 Pare-feu Windows", "control|firewall.cpl", "Règles de pare-feu, exceptions"),
            WinSetting("🌐 Connexions réseau", "control|ncpa.cpl", "Adaptateurs réseau, IP, DNS"),
            WinSetting("🖥️ Programmes et fonctionnalités", "control|appwiz.cpl", "Désinstaller des programmes, fonctionnalités Windows"),
            WinSetting("👤 Comptes utilisateurs", "control|nusrmgr.cpl", "Gérer les comptes, mots de passe"),
            WinSetting("⚡ Options d'alimentation", "control|powercfg.cpl", "Plans d'alimentation, veille, écran"),
            WinSetting("📡 Centre Réseau et partage", "control|/name Microsoft.NetworkAndSharingCenter", "Partage réseau, groupe résidentiel"),
            WinSetting("📅 Région", "control|intl.cpl", "Format de date, heure, devise, région"),
            WinSetting("⏰ Planificateur de tâches", "taskschd.msc", "Tâches planifiées, automatisation"),
            WinSetting("📦 Fonctionnalités Windows", "control|optionalfeatures", "Activer ou désactiver des fonctionnalités Windows"),
            WinSetting("🎧 Périphériques audio", "control|mmsys.cpl", "Lecture, enregistrement, sons système"),
            WinSetting("🛰️ Connexion Bureau à distance", "mstsc", "Bureau à distance, Remote Desktop"),
            
            // === Paramètres système avancés et outils ===
            WinSetting("⚙️ Paramètres système avancés", "control|sysdm.cpl,,3", "Variables d'environnement, performances, profils utilisateurs, démarrage, mémoire virtuelle"),
            WinSetting("🌐 Propriétés Internet", "control|inetcpl.cpl", "Options Internet, proxy navigateur, sécurité web, cookies, certificats"),
            WinSetting("🔧 Éditeur du registre", "regedit", "Registre Windows, clés, valeurs système, regedit"),
            WinSetting("📊 Informations système détaillées", "msinfo32", "Matériel, composants, BIOS, mémoire RAM, processeur, carte mère"),
            WinSetting("🧹 Nettoyage de disque", "cleanmgr", "Libérer espace disque, fichiers temporaires, cache, corbeille"),
            WinSetting("🔐 Stratégie de sécurité locale", "secpol.msc", "Stratégies de sécurité, audit, droits utilisateurs, mot de passe"),
            WinSetting("📋 Éditeur de stratégie de groupe", "gpedit.msc", "Stratégies de groupe, GPO, configuration Windows, modèles d'administration"),
            WinSetting("🎨 ClearType", "control|cttune", "Réglage ClearType, lissage des polices, texte net"),
            WinSetting("📺 Résolution d'écran", "control|desk.cpl", "Affichage, résolution, orientation, taille du texte"),
            WinSetting("🔊 Mixeur audio", "sndvol", "Volume par application, mixeur de volume, sorties audio"),
            WinSetting("🖨️ Gestion d'impression", "printmanagement.msc", "Imprimantes, files d'attente, serveurs d'impression"),
            WinSetting("💻 Propriétés système", "control|sysdm.cpl", "Nom d'ordinateur, groupe de travail, domaine, matériel, restauration système"),
            WinSetting("🔄 Restauration du système", "rstrui", "Points de restauration, restauration système, récupération"),
            WinSetting("💻 Moniteur de ressources", "resmon", "CPU, mémoire, disque, réseau en temps réel, processus"),
            WinSetting("🔒 Windows Defender Firewall avancé", "wf.msc", "Règles entrantes, sortantes, sécurité connexion, pare-feu avancé"),
            WinSetting("📁 Options des dossiers", "control|folders", "Affichage fichiers cachés, extensions, explorateur de fichiers"),
            WinSetting("⏱️ Diagnostics mémoire", "mdsched", "Test mémoire RAM, diagnostic, erreurs mémoire"),
            WinSetting("📱 Téléphone", "ms-settings:mobile-devices", "Lier téléphone, notifications mobiles, photos"),
            WinSetting("🌍 Paramètres proxy", "ms-settings:network-proxy", "Configuration proxy, proxy automatique, proxy manuel"),
            WinSetting("🔋 Batterie", "ms-settings:batterysaver", "Économie de batterie, utilisation batterie, autonomie"),
        ];
    }

    private static IndexedItem WinSetting(string name, string path, string description)
    {
        return IndexedItem.Create(
            path: path,
            name: name,
            description: $"⚙️ {description}",
            type: ResultType.SystemControl);
    }
}
