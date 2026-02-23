namespace QuickLauncher.Models;

/// <summary>
/// Commandes système par défaut, séparées d'AppSettings pour lisibilité.
/// Contient aussi la logique de migration (ajout/suppression/réordonnancement).
/// </summary>
public static class DefaultSystemCommands
{
    public static List<SystemControlCommand> Create() =>
    [
        // Productivité
        new() { Type = SystemControlType.Timer, Name = "Minuterie", Prefix = "timer", Icon = "⏱️", Category = "Productivité",
                Description = "Créer une minuterie (ex: :timer 5m Pause café)", RequiresArgument = true, ArgumentHint = "[durée] [label]" },
        new() { Type = SystemControlType.Note, Name = "Nouvelle note", Prefix = "note", Icon = "📝", Category = "Productivité",
                Description = "Créer une note sur le bureau", RequiresArgument = true, ArgumentHint = "[contenu]" },
        new() { Type = SystemControlType.SystemSearch, Name = "Recherche système", Prefix = "find", Icon = "🔎", Category = "Productivité",
                Description = "Rechercher des fichiers sur tout le système", RequiresArgument = true, ArgumentHint = "[terme]" },
        new() { Type = SystemControlType.Screenshot, Name = "Capture d'écran", Prefix = "screenshot", Icon = "📸", Category = "Productivité",
                Description = "Prendre une capture d'écran", ArgumentHint = "[snip|primary]" },
        // Intégrations web
        new() { Type = SystemControlType.Weather, Name = "Météo", Prefix = "weather", Icon = "🌤️", Category = "Intégrations web",
                Description = "Afficher la météo actuelle (ex: :weather ou :weather Paris)", ArgumentHint = "[ville]" },
        new() { Type = SystemControlType.Translate, Name = "Traduction", Prefix = "translate", Icon = "🌐", Category = "Intégrations web",
                Description = "Traduire du texte (ex: :translate hello ou :translate fr bonjour)", RequiresArgument = true, ArgumentHint = "[lang] <texte>" },
        new() { Type = SystemControlType.AiChat, Name = "Assistant IA", Prefix = "ai", Icon = "🤖", Category = "Intégrations web",
                Description = "Poser une question à l'IA (ex: :ai qu'est-ce qu'une API REST?)", RequiresArgument = true, ArgumentHint = "<question>" },
        // Multimédia
        new() { Type = SystemControlType.Volume, Name = "Volume", Prefix = "volume", Icon = "🔊", Category = "Multimédia",
                Description = "Régler le volume (0-100, up, down)", RequiresArgument = true, ArgumentHint = "[0-100|up|down]" },
        new() { Type = SystemControlType.Mute, Name = "Muet", Prefix = "mute", Icon = "🔇", Category = "Multimédia",
                Description = "Basculer le mode muet" },
        new() { Type = SystemControlType.Brightness, Name = "Luminosité", Prefix = "brightness", Icon = "☀️", Category = "Multimédia",
                Description = "Régler la luminosité (0-100)", RequiresArgument = true, ArgumentHint = "[0-100]" },
        // Réseau
        new() { Type = SystemControlType.Wifi, Name = "WiFi", Prefix = "wifi", Icon = "📶", Category = "Réseau",
                Description = "Contrôler le WiFi", RequiresArgument = true, ArgumentHint = "[on|off|status]" },
        new() { Type = SystemControlType.FlushDns, Name = "Vider DNS", Prefix = "flushdns", Icon = "🌐", Category = "Réseau",
                Description = "Vider le cache DNS" },
        // Session
        new() { Type = SystemControlType.Lock, Name = "Verrouiller", Prefix = "lock", Icon = "🔒", Category = "Session", Description = "Verrouiller la session" },
        new() { Type = SystemControlType.Logoff, Name = "Déconnexion", Prefix = "logoff", Icon = "🚪", Category = "Session", Description = "Déconnecter la session" },
        new() { Type = SystemControlType.Sleep, Name = "Veille", Prefix = "sleep", Icon = "😴", Category = "Session", Description = "Mettre en veille" },
        new() { Type = SystemControlType.Hibernate, Name = "Hibernation", Prefix = "hibernate", Icon = "💤", Category = "Session", Description = "Mettre en hibernation" },
        new() { Type = SystemControlType.Shutdown, Name = "Éteindre", Prefix = "shutdown", Icon = "🔌", Category = "Session", Description = "Éteindre l'ordinateur" },
        new() { Type = SystemControlType.Restart, Name = "Redémarrer", Prefix = "restart", Icon = "🔄", Category = "Session", Description = "Redémarrer l'ordinateur" },
        // Système
        new() { Type = SystemControlType.OpenTaskManager, Name = "Gestionnaire tâches", Prefix = "taskmgr", Icon = "📊", Category = "Système", Description = "Ouvrir le Gestionnaire des tâches" },
        new() { Type = SystemControlType.OpenWindowsSettings, Name = "Paramètres Windows", Prefix = "winsettings", Icon = "⚙️", Category = "Système", Description = "Ouvrir les Paramètres Windows" },
        new() { Type = SystemControlType.OpenControlPanel, Name = "Panneau config.", Prefix = "control", Icon = "🎛️", Category = "Système", Description = "Ouvrir le Panneau de configuration" },
        new() { Type = SystemControlType.EmptyRecycleBin, Name = "Vider corbeille", Prefix = "emptybin", Icon = "🗑️", Category = "Système", Description = "Vider la corbeille" },
        new() { Type = SystemControlType.EmptyTemp, Name = "Vider Temp", Prefix = "emptytemp", Icon = "🧹", Category = "Système", Description = "Vider le dossier temporaire" },
        new() { Type = SystemControlType.OpenCmdAdmin, Name = "CMD Admin", Prefix = "cmd", Icon = "💻", Category = "Système", Description = "Ouvrir l'invite de commandes (admin)" },
        new() { Type = SystemControlType.OpenPowerShellAdmin, Name = "PowerShell Admin", Prefix = "powershell", Icon = "🔵", Category = "Système", Description = "Ouvrir PowerShell (admin)" },
        new() { Type = SystemControlType.RestartExplorer, Name = "Redém. Explorer", Prefix = "restartexplorer", Icon = "📁", Category = "Système", Description = "Redémarrer l'Explorateur Windows" },
        new() { Type = SystemControlType.OpenStartupFolder, Name = "Démarrage", Prefix = "startup", Icon = "🚀", Category = "Système", Description = "Ouvrir le dossier de démarrage Windows" },
        new() { Type = SystemControlType.OpenHostsFile, Name = "Fichier hosts", Prefix = "hosts", Icon = "📝", Category = "Système", Description = "Ouvrir le fichier hosts (admin)" },
        new() { Type = SystemControlType.ProcessKill, Name = "Tuer processus", Prefix = "process", Icon = "💀", Category = "Système",
                Description = "Tuer un processus par nom (ex: :process kill notepad)", RequiresArgument = true, ArgumentHint = "kill <nom>" },
        new() { Type = SystemControlType.DiskInfo, Name = "Espace disque", Prefix = "disk", Icon = "💾", Category = "Système",
                Description = "Afficher l'espace disque disponible" },
        // Application
        new() { Type = SystemControlType.AppSettings, Name = "Paramètres", Prefix = "settings", Icon = "⚙️", Category = "Application",
                Description = "Ouvrir les paramètres de QuickLauncher" },
        new() { Type = SystemControlType.AppQuit, Name = "Quitter", Prefix = "quit", Icon = "🚪", Category = "Application",
                Description = "Fermer QuickLauncher" },
        new() { Type = SystemControlType.AppReindex, Name = "Réindexer", Prefix = "reload", Icon = "🔄", Category = "Application",
                Description = "Reconstruire l'index des fichiers" },
        new() { Type = SystemControlType.AppHistory, Name = "Historique", Prefix = "history", Icon = "📜", Category = "Application",
                Description = "Afficher l'historique de recherche" },
        new() { Type = SystemControlType.AppClearHistory, Name = "Effacer historique", Prefix = "clear", Icon = "🗑️", Category = "Application",
                Description = "Effacer l'historique de recherche" },
        new() { Type = SystemControlType.AppHelp, Name = "Aide", Prefix = "help", Icon = "❓", Category = "Application",
                Description = "Afficher les commandes disponibles" }
    ];

    /// <summary>
    /// Ajoute les commandes manquantes, purge les obsolètes et réordonne.
    /// </summary>
    public static void Migrate(List<SystemControlCommand> commands)
    {
        var defaults = Create();
        var defaultTypes = defaults.Select(c => c.Type).ToHashSet();
        var existingTypes = commands.Select(c => c.Type).ToHashSet();
        
        // Purger les commandes qui n'existent plus dans les défauts
        commands.RemoveAll(c => !defaultTypes.Contains(c.Type));
        
        // Ajouter les nouvelles commandes manquantes
        foreach (var cmd in defaults)
        {
            if (!existingTypes.Contains(cmd.Type))
                commands.Add(cmd);
        }
        
        // Remplir les catégories vides
        foreach (var existingCmd in commands)
        {
            if (string.IsNullOrEmpty(existingCmd.Category))
            {
                var defaultCmd = defaults.FirstOrDefault(d => d.Type == existingCmd.Type);
                if (defaultCmd != null)
                    existingCmd.Category = defaultCmd.Category;
            }
        }
        
        // Réordonner selon l'ordre par défaut
        var orderedTypes = defaults.Select(c => c.Type).ToList();
        var sorted = commands
            .OrderBy(c => orderedTypes.IndexOf(c.Type))
            .ThenBy(c => c.Category)
            .ToList();
        
        commands.Clear();
        commands.AddRange(sorted);
    }
}