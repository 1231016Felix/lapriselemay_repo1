using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TempCleaner.Models;

public partial class CleanerProfile : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _detailedWarning = string.Empty;  // Avertissement détaillé

    [ObservableProperty]
    private string _folderPath = string.Empty;

    [ObservableProperty]
    private string _searchPattern = "*.*";

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _includeSubdirectories = true;

    [ObservableProperty]
    private int _minAgeDays;

    [ObservableProperty]
    private string _icon = "📁";

    [ObservableProperty]
    private bool _requiresAdmin;

    [ObservableProperty]
    private bool _isPrivacy;

    [ObservableProperty]
    private bool _isSafe = true;

    [ObservableProperty]
    private long _totalSize;

    [ObservableProperty]
    private int _fileCount;

    [ObservableProperty]
    private CleanerCategory _category = CleanerCategory.General;

    public string TotalSizeFormatted => FormatSize(TotalSize);

    /// <summary>
    /// Génère un avertissement détaillé pour l'utilisateur
    /// </summary>
    public string GetDetailedWarning()
    {
        var warning = new System.Text.StringBuilder();
        
        warning.AppendLine($"📁 {Name}");
        warning.AppendLine(new string('═', 50));
        warning.AppendLine();
        warning.AppendLine($"📝 Description:");
        warning.AppendLine($"   {Description}");
        warning.AppendLine();
        warning.AppendLine($"📂 Emplacement:");
        warning.AppendLine($"   {FolderPath}");
        warning.AppendLine();
        
        if (SearchPattern != "*.*")
        {
            warning.AppendLine($"🔍 Fichiers ciblés:");
            warning.AppendLine($"   {SearchPattern}");
            warning.AppendLine();
        }
        
        warning.AppendLine($"⚙️ Options:");
        warning.AppendLine($"   • Sous-dossiers inclus: {(IncludeSubdirectories ? "Oui" : "Non")}");
        
        if (MinAgeDays > 0)
            warning.AppendLine($"   • Fichiers de plus de {MinAgeDays} jour(s) uniquement");
        else
            warning.AppendLine($"   • Tous les fichiers (aucune limite d'âge)");
        
        warning.AppendLine();
        warning.AppendLine(new string('─', 50));
        
        // Avertissements spécifiques
        if (!IsSafe)
        {
            warning.AppendLine();
            warning.AppendLine("⚠️ ATTENTION - OPÉRATION RISQUÉE:");
            warning.AppendLine("   Cette catégorie peut affecter le fonctionnement");
            warning.AppendLine("   du système ou de certaines applications.");
        }
        
        if (IsPrivacy)
        {
            warning.AppendLine();
            warning.AppendLine("🔒 CONFIDENTIALITÉ:");
            warning.AppendLine("   Ces fichiers contiennent des traces d'activité.");
            warning.AppendLine("   Leur suppression peut vous déconnecter de sites web.");
        }
        
        if (RequiresAdmin)
        {
            warning.AppendLine();
            warning.AppendLine("🔐 DROITS ADMINISTRATEUR REQUIS:");
            warning.AppendLine("   Cette catégorie nécessite des privilèges élevés.");
        }
        
        // Avertissements par catégorie
        warning.AppendLine();
        warning.AppendLine(GetCategoryWarning());
        
        return warning.ToString();
    }
    
    private string GetCategoryWarning()
    {
        return Category switch
        {
            CleanerCategory.WindowsTemp or CleanerCategory.UserTemp =>
                "ℹ️ Fichiers temporaires:\n   Généralement sans risque. Peuvent être recréés automatiquement.",
            
            CleanerCategory.BrowserCache =>
                "ℹ️ Cache navigateur:\n   Les sites web chargeront plus lentement après le nettoyage.\n   Le cache sera reconstruit automatiquement.",
            
            CleanerCategory.BrowserHistory =>
                "⚠️ Historique navigateur:\n   Votre historique de navigation sera perdu.\n   Vous ne pourrez plus retrouver les sites visités.",
            
            CleanerCategory.BrowserCookies =>
                "⚠️ Cookies:\n   Vous serez DÉCONNECTÉ de tous les sites web.\n   Vos préférences de sites seront perdues.",
            
            CleanerCategory.WindowsCache or CleanerCategory.Thumbnails =>
                "ℹ️ Cache Windows:\n   Les miniatures seront régénérées.\n   Peut ralentir temporairement l'explorateur.",
            
            CleanerCategory.Prefetch =>
                "⚠️ Prefetch:\n   Le démarrage des applications peut être plus lent\n   jusqu'à ce que Windows réapprenne vos habitudes.",
            
            CleanerCategory.WindowsUpdate or CleanerCategory.DeliveryOptimization =>
                "ℹ️ Mises à jour:\n   Fichiers de mise à jour téléchargés.\n   Seront retéléchargés si nécessaire.",
            
            CleanerCategory.WindowsLogs =>
                "ℹ️ Journaux:\n   Fichiers de diagnostic Windows.\n   Peut compliquer le dépannage de problèmes.",
            
            CleanerCategory.ErrorReports =>
                "ℹ️ Rapports d'erreurs:\n   Rapports de plantage d'applications.\n   Microsoft ne recevra plus ces informations.",
            
            CleanerCategory.MemoryDumps =>
                "ℹ️ Dumps mémoire:\n   Fichiers de débogage volumineux.\n   Utiles uniquement pour les développeurs.",
            
            CleanerCategory.ApplicationCache =>
                "ℹ️ Cache application:\n   Données en cache des logiciels.\n   Seront recréées automatiquement.",
            
            CleanerCategory.GamingCache =>
                "ℹ️ Cache jeux:\n   Cache des launchers et shaders.\n   Les jeux peuvent avoir un premier démarrage plus lent.",
            
            CleanerCategory.CommunicationApps =>
                "⚠️ Apps de communication:\n   Cache de Teams, Discord, Slack, etc.\n   Historique de conversation local peut être perdu.",
            
            CleanerCategory.MediaApps =>
                "ℹ️ Apps média:\n   Cache de Spotify, VLC, etc.\n   La musique/vidéos hors-ligne seront supprimées.",
            
            CleanerCategory.AdobeApps =>
                "⚠️ Adobe:\n   Cache des applications Creative Cloud.\n   Fichiers de prévisualisation et rendu perdus.",
            
            CleanerCategory.CloudSync =>
                "⚠️ Synchronisation cloud:\n   Cache local OneDrive, Dropbox, etc.\n   Les fichiers seront resynchronisés.",
            
            CleanerCategory.WindowsStore =>
                "ℹ️ Windows Store:\n   Cache du Microsoft Store.\n   Les apps peuvent nécessiter un rechargement.",
            
            CleanerCategory.RecentDocs =>
                "🔒 Documents récents:\n   Liste des fichiers récemment ouverts.\n   Trace de votre activité sera effacée.",
            
            CleanerCategory.OldWindowsInstall =>
                "⚠️ CRITIQUE - Ancienne installation:\n   Vous ne pourrez PLUS revenir à la version précédente\n   de Windows après suppression!",
            
            CleanerCategory.SystemAdvanced =>
                "⚠️ Système avancé:\n   Fichiers système sensibles.\n   À utiliser avec précaution.",
            
            _ => "ℹ️ Catégorie générale:\n   Vérifiez le contenu avant suppression."
        };
    }

    private static string FormatSize(long bytes)
    {
        if (bytes == 0) return "0 B";
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int suffixIndex = 0;
        double size = bytes;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return $"{size:N2} {suffixes[suffixIndex]}";
    }

    public static List<CleanerProfile> GetDefaultProfiles()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var tempPath = Path.GetTempPath();
        var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        return
        [
            // ══════════════════════════════════════════════════════════════
            // FICHIERS TEMPORAIRES SYSTÈME
            // ══════════════════════════════════════════════════════════════
            new CleanerProfile
            {
                Name = "Fichiers temporaires Windows",
                Description = "Dossier TEMP système",
                FolderPath = tempPath,
                Icon = "🗑️",
                MinAgeDays = 1,
                Category = CleanerCategory.WindowsTemp
            },
            new CleanerProfile
            {
                Name = "Temp utilisateur",
                Description = "Dossier TEMP local utilisateur",
                FolderPath = Path.Combine(localAppData, "Temp"),
                Icon = "🗑️",
                MinAgeDays = 1,
                Category = CleanerCategory.UserTemp
            },
            new CleanerProfile
            {
                Name = "Windows Temp système",
                Description = "Dossier Temp système Windows",
                FolderPath = Path.Combine(windowsPath, "Temp"),
                Icon = "🔒",
                MinAgeDays = 1,
                RequiresAdmin = true,
                Category = CleanerCategory.WindowsTemp
            },

            // ══════════════════════════════════════════════════════════════
            // NAVIGATEURS - CACHE
            // ══════════════════════════════════════════════════════════════
            new CleanerProfile
            {
                Name = "Google Chrome - Cache",
                Description = "Pages web, images et scripts en cache",
                FolderPath = Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache"),
                Icon = "🌐",
                Category = CleanerCategory.BrowserCache
            },
            new CleanerProfile
            {
                Name = "Chrome - Code Cache",
                Description = "Cache de code JavaScript compilé",
                FolderPath = Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Code Cache"),
                Icon = "🌐",
                Category = CleanerCategory.BrowserCache
            },
            new CleanerProfile
            {
                Name = "Chrome - GPU Cache",
                Description = "Cache GPU pour l'accélération matérielle",
                FolderPath = Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "GPUCache"),
                Icon = "🌐",
                Category = CleanerCategory.BrowserCache
            },
            new CleanerProfile
            {
                Name = "Chrome - Shader Cache",
                Description = "Cache des shaders graphiques",
                FolderPath = Path.Combine(localAppData, "Google", "Chrome", "User Data", "ShaderCache"),
                Icon = "🌐",
                Category = CleanerCategory.BrowserCache
            },
            new CleanerProfile
            {
                Name = "Microsoft Edge - Cache",
                Description = "Cache du navigateur Edge",
                FolderPath = Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache"),
                Icon = "🌐",
                Category = CleanerCategory.BrowserCache
            },
            new CleanerProfile
            {
                Name = "Edge - Code Cache",
                Description = "Cache de code JavaScript Edge",
                FolderPath = Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Code Cache"),
                Icon = "🌐",
                Category = CleanerCategory.BrowserCache
            },
            new CleanerProfile
            {
                Name = "Edge - GPU Cache",
                Description = "Cache GPU Edge",
                FolderPath = Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "GPUCache"),
                Icon = "🌐",
                Category = CleanerCategory.BrowserCache
            },
            new CleanerProfile
            {
                Name = "Mozilla Firefox - Cache",
                Description = "Cache du navigateur Firefox",
                FolderPath = Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles"),
                SearchPattern = "cache2*",
                Icon = "🦊",
                Category = CleanerCategory.BrowserCache
            },
            new CleanerProfile
            {
                Name = "Firefox - Offline Cache",
                Description = "Cache hors-ligne Firefox",
                FolderPath = Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles"),
                SearchPattern = "OfflineCache*",
                Icon = "🦊",
                Category = CleanerCategory.BrowserCache
            },
            // NOUVEAUX: Opera et Brave (du C++)
            new CleanerProfile
            {
                Name = "Opera - Cache",
                Description = "Cache du navigateur Opera",
                FolderPath = Path.Combine(appData, "Opera Software", "Opera Stable", "Cache"),
                Icon = "🔴",
                Category = CleanerCategory.BrowserCache
            },
            new CleanerProfile
            {
                Name = "Opera GX - Cache",
                Description = "Cache du navigateur Opera GX",
                FolderPath = Path.Combine(appData, "Opera Software", "Opera GX Stable", "Cache"),
                Icon = "🔴",
                Category = CleanerCategory.BrowserCache
            },
            new CleanerProfile
            {
                Name = "Brave - Cache",
                Description = "Cache du navigateur Brave",
                FolderPath = Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Cache"),
                Icon = "🦁",
                Category = CleanerCategory.BrowserCache
            },
            new CleanerProfile
            {
                Name = "Brave - Code Cache",
                Description = "Cache de code Brave",
                FolderPath = Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Code Cache"),
                Icon = "🦁",
                Category = CleanerCategory.BrowserCache
            },

            // ══════════════════════════════════════════════════════════════
            // NAVIGATEURS - CONFIDENTIALITÉ (isPrivacy = true)
            // ══════════════════════════════════════════════════════════════
            new CleanerProfile
            {
                Name = "Chrome - Historique",
                Description = "Historique de navigation (vous déconnectera)",
                FolderPath = Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default"),
                SearchPattern = "History*",
                Icon = "🔒",
                IsPrivacy = true,
                IsEnabled = false,
                IncludeSubdirectories = false,
                Category = CleanerCategory.BrowserHistory
            },
            new CleanerProfile
            {
                Name = "Chrome - Cookies",
                Description = "Cookies des sites web (vous déconnectera)",
                FolderPath = Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Network"),
                SearchPattern = "Cookies*",
                Icon = "🍪",
                IsPrivacy = true,
                IsEnabled = false,
                IncludeSubdirectories = false,
                Category = CleanerCategory.BrowserCookies
            },
            new CleanerProfile
            {
                Name = "Edge - Historique",
                Description = "Historique de navigation Edge",
                FolderPath = Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default"),
                SearchPattern = "History*",
                Icon = "🔒",
                IsPrivacy = true,
                IsEnabled = false,
                IncludeSubdirectories = false,
                Category = CleanerCategory.BrowserHistory
            },
            new CleanerProfile
            {
                Name = "Edge - Cookies",
                Description = "Cookies Edge",
                FolderPath = Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Network"),
                SearchPattern = "Cookies*",
                Icon = "🍪",
                IsPrivacy = true,
                IsEnabled = false,
                IncludeSubdirectories = false,
                Category = CleanerCategory.BrowserCookies
            },
            new CleanerProfile
            {
                Name = "Firefox - Historique",
                Description = "Historique Firefox (places.sqlite)",
                FolderPath = Path.Combine(appData, "Mozilla", "Firefox", "Profiles"),
                SearchPattern = "places.sqlite*",
                Icon = "🔒",
                IsPrivacy = true,
                IsEnabled = false,
                Category = CleanerCategory.BrowserHistory
            },
            new CleanerProfile
            {
                Name = "Firefox - Cookies",
                Description = "Cookies Firefox",
                FolderPath = Path.Combine(appData, "Mozilla", "Firefox", "Profiles"),
                SearchPattern = "cookies.sqlite*",
                Icon = "🍪",
                IsPrivacy = true,
                IsEnabled = false,
                Category = CleanerCategory.BrowserCookies
            },

            // ══════════════════════════════════════════════════════════════
            // APPLICATIONS DE DÉVELOPPEMENT
            // ══════════════════════════════════════════════════════════════
            new CleanerProfile
            {
                Name = "VS Code - Cache",
                Description = "Cache Visual Studio Code",
                FolderPath = Path.Combine(appData, "Code", "Cache"),
                Icon = "💻",
                Category = CleanerCategory.ApplicationCache
            },
            new CleanerProfile
            {
                Name = "VS Code - Cached Data",
                Description = "Données en cache VS Code",
                FolderPath = Path.Combine(appData, "Code", "CachedData"),
                Icon = "💻",
                Category = CleanerCategory.ApplicationCache
            },
            new CleanerProfile
            {
                Name = "VS Code - Extensions Cache",
                Description = "Cache des extensions VS Code",
                FolderPath = Path.Combine(appData, "Code", "CachedExtensions"),
                Icon = "💻",
                Category = CleanerCategory.ApplicationCache
            },
            new CleanerProfile
            {
                Name = "VS Code - Logs",
                Description = "Journaux VS Code (7+ jours)",
                FolderPath = Path.Combine(appData, "Code", "logs"),
                MinAgeDays = 7,
                Icon = "📋",
                Category = CleanerCategory.ApplicationCache
            },
            new CleanerProfile
            {
                Name = "Cache Visual Studio",
                Description = "Fichiers temporaires Visual Studio",
                FolderPath = Path.Combine(localAppData, "Microsoft", "VisualStudio"),
                SearchPattern = "*.tmp",
                Icon = "💻",
                MinAgeDays = 7,
                Category = CleanerCategory.ApplicationCache
            },
            new CleanerProfile
            {
                Name = "Cache NuGet",
                Description = "Packages NuGet en cache",
                FolderPath = Path.Combine(localAppData, "NuGet", "v3-cache"),
                Icon = "📦",
                Category = CleanerCategory.ApplicationCache
            },
            new CleanerProfile
            {
                Name = "Packages NuGet anciens",
                Description = "Packages NuGet non utilisés depuis 30+ jours",
                FolderPath = Path.Combine(userProfile, ".nuget", "packages"),
                Icon = "📦",
                MinAgeDays = 30,
                IsEnabled = false,
                Category = CleanerCategory.ApplicationCache
            },
            new CleanerProfile
            {
                Name = "Cache npm",
                Description = "Cache des packages Node.js",
                FolderPath = Path.Combine(appData, "npm-cache"),
                Icon = "📦",
                Category = CleanerCategory.ApplicationCache
            },
            new CleanerProfile
            {
                Name = "Cache npm (Local)",
                Description = "Cache npm local",
                FolderPath = Path.Combine(localAppData, "npm-cache"),
                Icon = "📦",
                Category = CleanerCategory.ApplicationCache
            },
            new CleanerProfile
            {
                Name = "Cache pip",
                Description = "Cache des packages Python",
                FolderPath = Path.Combine(localAppData, "pip", "cache"),
                Icon = "🐍",
                Category = CleanerCategory.ApplicationCache
            },

            // ══════════════════════════════════════════════════════════════
            // JEUX (NOUVEAU - du C++)
            // ══════════════════════════════════════════════════════════════
            new CleanerProfile
            {
                Name = "Steam - Logs",
                Description = "Journaux du client Steam (7+ jours)",
                FolderPath = @"C:\Program Files (x86)\Steam\logs",
                SearchPattern = "*.txt",
                Icon = "🎮",
                MinAgeDays = 7,
                Category = CleanerCategory.GamingCache
            },
            new CleanerProfile
            {
                Name = "Steam - Dumps",
                Description = "Fichiers de crash Steam",
                FolderPath = @"C:\Program Files (x86)\Steam\dumps",
                Icon = "🎮",
                Category = CleanerCategory.GamingCache
            },
            new CleanerProfile
            {
                Name = "Steam - HTML Cache",
                Description = "Cache web du client Steam",
                FolderPath = Path.Combine(localAppData, "Steam", "htmlcache"),
                Icon = "🎮",
                Category = CleanerCategory.GamingCache
            },
            new CleanerProfile
            {
                Name = "Epic Games - Web Cache",
                Description = "Cache web Epic Games Launcher",
                FolderPath = Path.Combine(localAppData, "EpicGamesLauncher", "Saved", "webcache"),
                Icon = "🎮",
                Category = CleanerCategory.GamingCache
            },
            new CleanerProfile
            {
                Name = "Epic Games - Logs",
                Description = "Journaux Epic Games (7+ jours)",
                FolderPath = Path.Combine(localAppData, "EpicGamesLauncher", "Saved", "Logs"),
                SearchPattern = "*.log",
                Icon = "🎮",
                MinAgeDays = 7,
                Category = CleanerCategory.GamingCache
            },

            // ══════════════════════════════════════════════════════════════
            // SYSTÈME WINDOWS - CACHES
            // ══════════════════════════════════════════════════════════════
            new CleanerProfile
            {
                Name = "Miniatures Windows",
                Description = "Cache des miniatures Explorer",
                FolderPath = Path.Combine(localAppData, "Microsoft", "Windows", "Explorer"),
                SearchPattern = "thumbcache_*.db",
                Icon = "🖼️",
                IncludeSubdirectories = false,
                Category = CleanerCategory.WindowsCache
            },
            new CleanerProfile
            {
                Name = "Cache icônes",
                Description = "Cache des icônes Windows",
                FolderPath = Path.Combine(localAppData, "Microsoft", "Windows", "Explorer"),
                SearchPattern = "iconcache_*.db",
                Icon = "🎨",
                IncludeSubdirectories = false,
                Category = CleanerCategory.WindowsCache
            },
            new CleanerProfile
            {
                Name = "Cache fonts",
                Description = "Cache des polices Windows",
                FolderPath = Path.Combine(windowsPath, "ServiceProfiles", "LocalService", "AppData", "Local"),
                SearchPattern = "FontCache*",
                Icon = "🔤",
                RequiresAdmin = true,
                Category = CleanerCategory.WindowsCache
            },

            // ══════════════════════════════════════════════════════════════
            // SYSTÈME WINDOWS - LOGS ET RAPPORTS
            // ══════════════════════════════════════════════════════════════
            new CleanerProfile
            {
                Name = "Logs Windows",
                Description = "Fichiers journaux Windows (7+ jours)",
                FolderPath = Path.Combine(windowsPath, "Logs"),
                SearchPattern = "*.log",
                Icon = "📋",
                MinAgeDays = 7,
                RequiresAdmin = true,
                Category = CleanerCategory.WindowsLogs
            },
            new CleanerProfile
            {
                Name = "Logs Panther",
                Description = "Logs d'installation Windows (30+ jours)",
                FolderPath = Path.Combine(windowsPath, "Panther"),
                SearchPattern = "*.log",
                Icon = "📋",
                MinAgeDays = 30,
                RequiresAdmin = true,
                Category = CleanerCategory.WindowsLogs
            },
            new CleanerProfile
            {
                Name = "Logs CBS",
                Description = "Journaux Component Based Servicing",
                FolderPath = Path.Combine(windowsPath, "Logs", "CBS"),
                SearchPattern = "*.log",
                Icon = "📋",
                MinAgeDays = 14,
                RequiresAdmin = true,
                Category = CleanerCategory.WindowsLogs
            },
            new CleanerProfile
            {
                Name = "Logs DISM",
                Description = "Journaux de maintenance Windows",
                FolderPath = Path.Combine(windowsPath, "Logs", "DISM"),
                Icon = "📋",
                MinAgeDays = 7,
                RequiresAdmin = true,
                Category = CleanerCategory.WindowsLogs
            },
            new CleanerProfile
            {
                Name = "Crash dumps utilisateur",
                Description = "Fichiers de crash dumps locaux",
                FolderPath = Path.Combine(localAppData, "CrashDumps"),
                Icon = "💥",
                Category = CleanerCategory.MemoryDumps
            },
            new CleanerProfile
            {
                Name = "Memory dumps système",
                Description = "Dumps mémoire Windows",
                FolderPath = windowsPath,
                SearchPattern = "*.dmp",
                Icon = "💾",
                IncludeSubdirectories = false,
                RequiresAdmin = true,
                Category = CleanerCategory.MemoryDumps
            },
            new CleanerProfile
            {
                Name = "Minidumps Windows",
                Description = "Mini dumps de crash système",
                FolderPath = Path.Combine(windowsPath, "Minidump"),
                SearchPattern = "*.dmp",
                Icon = "💾",
                RequiresAdmin = true,
                Category = CleanerCategory.MemoryDumps
            },
            new CleanerProfile
            {
                Name = "Windows Error Reports",
                Description = "Rapports d'erreurs WER locaux",
                FolderPath = Path.Combine(localAppData, "Microsoft", "Windows", "WER"),
                Icon = "⚠️",
                MinAgeDays = 7,
                Category = CleanerCategory.ErrorReports
            },
            new CleanerProfile
            {
                Name = "System Error Reports",
                Description = "Rapports d'erreurs système",
                FolderPath = Path.Combine(programData, "Microsoft", "Windows", "WER"),
                Icon = "⚠️",
                RequiresAdmin = true,
                Category = CleanerCategory.ErrorReports
            },

            // ══════════════════════════════════════════════════════════════
            // SYSTÈME WINDOWS - MISES À JOUR ET MAINTENANCE
            // ══════════════════════════════════════════════════════════════
            new CleanerProfile
            {
                Name = "Cache Windows Update",
                Description = "Fichiers de mise à jour téléchargés",
                FolderPath = Path.Combine(windowsPath, "SoftwareDistribution", "Download"),
                Icon = "🔄",
                RequiresAdmin = true,
                Category = CleanerCategory.WindowsUpdate
            },
            new CleanerProfile
            {
                Name = "Delivery Optimization",
                Description = "Cache P2P Windows Update",
                FolderPath = Path.Combine(windowsPath, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache"),
                Icon = "🔄",
                RequiresAdmin = true,
                Category = CleanerCategory.DeliveryOptimization
            },
            new CleanerProfile
            {
                Name = "Prefetch Windows",
                Description = "Fichiers de préchargement (7+ jours)",
                FolderPath = Path.Combine(windowsPath, "Prefetch"),
                SearchPattern = "*.pf",
                Icon = "⚡",
                MinAgeDays = 7,
                RequiresAdmin = true,
                IncludeSubdirectories = false,
                Category = CleanerCategory.Prefetch
            },
            new CleanerProfile
            {
                Name = "Windows Installer Cache",
                Description = "Cache des installations Windows",
                FolderPath = Path.Combine(windowsPath, "Installer", "$PatchCache$"),
                Icon = "📦",
                MinAgeDays = 30,
                RequiresAdmin = true,
                Category = CleanerCategory.WindowsUpdate
            },
            new CleanerProfile
            {
                Name = "Windows Defender Scans",
                Description = "Historique des analyses Defender",
                FolderPath = Path.Combine(programData, "Microsoft", "Windows Defender", "Scans", "History"),
                Icon = "🛡️",
                MinAgeDays = 30,
                RequiresAdmin = true,
                Category = CleanerCategory.WindowsCache
            },

            // ══════════════════════════════════════════════════════════════
            // APPLICATIONS DE COMMUNICATION
            // ══════════════════════════════════════════════════════════════
            new CleanerProfile
            {
                Name = "Microsoft Teams - Cache",
                Description = "Cache de l'application Teams",
                FolderPath = Path.Combine(appData, "Microsoft", "Teams", "Cache"),
                Icon = "💬",
                Category = CleanerCategory.CommunicationApps
            },
            new CleanerProfile
            {
                Name = "Teams - Service Worker",
                Description = "Cache Service Worker Teams",
                FolderPath = Path.Combine(appData, "Microsoft", "Teams", "Service Worker", "CacheStorage"),
                Icon = "💬",
                Category = CleanerCategory.CommunicationApps
            },
            new CleanerProfile
            {
                Name = "Teams - Blob Storage",
                Description = "Stockage blob Teams",
                FolderPath = Path.Combine(appData, "Microsoft", "Teams", "blob_storage"),
                Icon = "💬",
                Category = CleanerCategory.CommunicationApps
            },
            new CleanerProfile
            {
                Name = "Teams - GPU Cache",
                Description = "Cache GPU Teams",
                FolderPath = Path.Combine(appData, "Microsoft", "Teams", "GPUCache"),
                Icon = "💬",
                Category = CleanerCategory.CommunicationApps
            },
            new CleanerProfile
            {
                Name = "Discord - Cache",
                Description = "Cache de Discord",
                FolderPath = Path.Combine(appData, "discord", "Cache"),
                Icon = "🎮",
                Category = CleanerCategory.CommunicationApps
            },
            new CleanerProfile
            {
                Name = "Discord - Code Cache",
                Description = "Cache de code Discord",
                FolderPath = Path.Combine(appData, "discord", "Code Cache"),
                Icon = "🎮",
                Category = CleanerCategory.CommunicationApps
            },
            new CleanerProfile
            {
                Name = "Discord - GPU Cache",
                Description = "Cache GPU Discord",
                FolderPath = Path.Combine(appData, "discord", "GPUCache"),
                Icon = "🎮",
                Category = CleanerCategory.CommunicationApps
            },
            new CleanerProfile
            {
                Name = "Slack - Cache",
                Description = "Cache de Slack",
                FolderPath = Path.Combine(appData, "Slack", "Cache"),
                Icon = "💼",
                Category = CleanerCategory.CommunicationApps
            },
            new CleanerProfile
            {
                Name = "Slack - Service Worker",
                Description = "Cache Service Worker Slack",
                FolderPath = Path.Combine(appData, "Slack", "Service Worker", "CacheStorage"),
                Icon = "💼",
                Category = CleanerCategory.CommunicationApps
            },
            new CleanerProfile
            {
                Name = "Zoom - Cache",
                Description = "Cache de Zoom",
                FolderPath = Path.Combine(appData, "Zoom", "data"),
                Icon = "📹",
                Category = CleanerCategory.CommunicationApps
            },
            new CleanerProfile
            {
                Name = "Telegram - Cache",
                Description = "Cache de Telegram Desktop",
                FolderPath = Path.Combine(appData, "Telegram Desktop", "tdata", "user_data"),
                Icon = "✈️",
                Category = CleanerCategory.CommunicationApps
            },
            new CleanerProfile
            {
                Name = "WhatsApp - Cache",
                Description = "Cache de WhatsApp Desktop",
                FolderPath = Path.Combine(localAppData, "Packages", "5319275A.WhatsAppDesktop_cv1g1gvanyjgm", "LocalCache"),
                Icon = "📱",
                Category = CleanerCategory.CommunicationApps
            },

            // ══════════════════════════════════════════════════════════════
            // APPLICATIONS MÉDIAS & STREAMING
            // ══════════════════════════════════════════════════════════════
            new CleanerProfile
            {
                Name = "Spotify - Cache",
                Description = "Cache musique Spotify (peut être volumineux)",
                FolderPath = Path.Combine(localAppData, "Spotify", "Storage"),
                Icon = "🎵",
                Category = CleanerCategory.MediaApps
            },
            new CleanerProfile
            {
                Name = "Spotify - Data",
                Description = "Données en cache Spotify",
                FolderPath = Path.Combine(localAppData, "Spotify", "Data"),
                Icon = "🎵",
                Category = CleanerCategory.MediaApps
            },
            new CleanerProfile
            {
                Name = "VLC - Cache",
                Description = "Cache du lecteur VLC",
                FolderPath = Path.Combine(appData, "vlc"),
                SearchPattern = "*.dat",
                Icon = "🎬",
                Category = CleanerCategory.MediaApps
            },
            new CleanerProfile
            {
                Name = "iTunes - Cache",
                Description = "Cache iTunes",
                FolderPath = Path.Combine(localAppData, "Apple Computer", "iTunes"),
                Icon = "🎵",
                Category = CleanerCategory.MediaApps
            },

            // ══════════════════════════════════════════════════════════════
            // ADOBE CREATIVE CLOUD
            // ══════════════════════════════════════════════════════════════
            new CleanerProfile
            {
                Name = "Adobe - Cache Média",
                Description = "Cache média commun Adobe",
                FolderPath = Path.Combine(appData, "Adobe", "Common", "Media Cache Files"),
                Icon = "🎨",
                Category = CleanerCategory.AdobeApps
            },
            new CleanerProfile
            {
                Name = "Adobe - Cache Base de données",
                Description = "Cache base de données média Adobe",
                FolderPath = Path.Combine(appData, "Adobe", "Common", "Media Cache"),
                Icon = "🎨",
                Category = CleanerCategory.AdobeApps
            },
            new CleanerProfile
            {
                Name = "Photoshop - Temp",
                Description = "Fichiers temporaires Photoshop",
                FolderPath = Path.Combine(localAppData, "Temp", "Photoshop Temp"),
                Icon = "🖼️",
                Category = CleanerCategory.AdobeApps
            },
            new CleanerProfile
            {
                Name = "Premiere Pro - Cache Média",
                Description = "Cache média Premiere Pro",
                FolderPath = Path.Combine(appData, "Adobe", "Common", "Peak Files"),
                Icon = "🎬",
                Category = CleanerCategory.AdobeApps
            },
            new CleanerProfile
            {
                Name = "After Effects - Cache",
                Description = "Cache disque After Effects",
                FolderPath = Path.Combine(localAppData, "Adobe", "After Effects"),
                SearchPattern = "*Cache*",
                Icon = "🎬",
                Category = CleanerCategory.AdobeApps
            },
            new CleanerProfile
            {
                Name = "Adobe - Logs",
                Description = "Journaux Adobe (7+ jours)",
                FolderPath = Path.Combine(localAppData, "Adobe", "Logs"),
                SearchPattern = "*.log",
                MinAgeDays = 7,
                Icon = "📋",
                Category = CleanerCategory.AdobeApps
            },

            // ══════════════════════════════════════════════════════════════
            // CLOUD & SYNCHRONISATION
            // ══════════════════════════════════════════════════════════════
            new CleanerProfile
            {
                Name = "OneDrive - Cache",
                Description = "Cache local OneDrive",
                FolderPath = Path.Combine(localAppData, "Microsoft", "OneDrive", "logs"),
                Icon = "☁️",
                Category = CleanerCategory.CloudSync
            },
            new CleanerProfile
            {
                Name = "Dropbox - Cache",
                Description = "Cache Dropbox",
                FolderPath = Path.Combine(localAppData, "Dropbox", "host.dbx"),
                Icon = "📦",
                Category = CleanerCategory.CloudSync
            },
            new CleanerProfile
            {
                Name = "Google Drive - Cache",
                Description = "Cache Google Drive",
                FolderPath = Path.Combine(localAppData, "Google", "DriveFS"),
                SearchPattern = "*.log",
                Icon = "📁",
                Category = CleanerCategory.CloudSync
            },
            new CleanerProfile
            {
                Name = "iCloud - Cache",
                Description = "Cache iCloud pour Windows",
                FolderPath = Path.Combine(localAppData, "Apple Inc", "iCloud"),
                Icon = "☁️",
                Category = CleanerCategory.CloudSync
            },

            // ══════════════════════════════════════════════════════════════
            // MICROSOFT STORE & APPS
            // ══════════════════════════════════════════════════════════════
            new CleanerProfile
            {
                Name = "Microsoft Store - Cache",
                Description = "Cache du Windows Store",
                FolderPath = Path.Combine(localAppData, "Packages", "Microsoft.WindowsStore_8wekyb3d8bbwe", "LocalCache"),
                Icon = "🛒",
                Category = CleanerCategory.WindowsStore
            },
            new CleanerProfile
            {
                Name = "Xbox - Cache",
                Description = "Cache de l'application Xbox",
                FolderPath = Path.Combine(localAppData, "Packages", "Microsoft.XboxApp_8wekyb3d8bbwe", "LocalCache"),
                Icon = "🎮",
                Category = CleanerCategory.WindowsStore
            },
            new CleanerProfile
            {
                Name = "Courrier - Cache",
                Description = "Cache de l'application Courrier",
                FolderPath = Path.Combine(localAppData, "Packages", "microsoft.windowscommunicationsapps_8wekyb3d8bbwe", "LocalCache"),
                Icon = "📧",
                Category = CleanerCategory.WindowsStore
            },
            new CleanerProfile
            {
                Name = "Photos - Cache",
                Description = "Cache de l'application Photos",
                FolderPath = Path.Combine(localAppData, "Packages", "Microsoft.Windows.Photos_8wekyb3d8bbwe", "LocalCache"),
                Icon = "🖼️",
                Category = CleanerCategory.WindowsStore
            },

            // ══════════════════════════════════════════════════════════════
            // JEUX - ÉTENDU
            // ══════════════════════════════════════════════════════════════
            new CleanerProfile
            {
                Name = "Origin - Cache",
                Description = "Cache EA Origin",
                FolderPath = Path.Combine(appData, "Origin", "Logs"),
                Icon = "🎮",
                Category = CleanerCategory.GamingCache
            },
            new CleanerProfile
            {
                Name = "Ubisoft Connect - Cache",
                Description = "Cache Ubisoft Connect",
                FolderPath = Path.Combine(localAppData, "Ubisoft Game Launcher", "cache"),
                Icon = "🎮",
                Category = CleanerCategory.GamingCache
            },
            new CleanerProfile
            {
                Name = "GOG Galaxy - Cache",
                Description = "Cache GOG Galaxy",
                FolderPath = Path.Combine(localAppData, "GOG.com", "Galaxy", "webcache"),
                Icon = "🎮",
                Category = CleanerCategory.GamingCache
            },
            new CleanerProfile
            {
                Name = "Riot Games - Logs",
                Description = "Logs Riot Client (League, Valorant)",
                FolderPath = Path.Combine(localAppData, "Riot Games", "Riot Client", "Logs"),
                SearchPattern = "*.log",
                MinAgeDays = 7,
                Icon = "🎮",
                Category = CleanerCategory.GamingCache
            },
            new CleanerProfile
            {
                Name = "Battle.net - Cache",
                Description = "Cache Battle.net",
                FolderPath = Path.Combine(appData, "Battle.net", "Cache"),
                Icon = "🎮",
                Category = CleanerCategory.GamingCache
            },
            new CleanerProfile
            {
                Name = "NVIDIA - Shader Cache",
                Description = "Cache des shaders NVIDIA",
                FolderPath = Path.Combine(localAppData, "NVIDIA", "DXCache"),
                Icon = "🖥️",
                Category = CleanerCategory.GamingCache
            },
            new CleanerProfile
            {
                Name = "NVIDIA - GLCache",
                Description = "Cache OpenGL NVIDIA",
                FolderPath = Path.Combine(localAppData, "NVIDIA", "GLCache"),
                Icon = "🖥️",
                Category = CleanerCategory.GamingCache
            },
            new CleanerProfile
            {
                Name = "AMD - Shader Cache",
                Description = "Cache des shaders AMD",
                FolderPath = Path.Combine(localAppData, "AMD", "DxCache"),
                Icon = "🖥️",
                Category = CleanerCategory.GamingCache
            },
            new CleanerProfile
            {
                Name = "DirectX Shader Cache",
                Description = "Cache shaders DirectX système",
                FolderPath = Path.Combine(localAppData, "D3DSCache"),
                Icon = "🖥️",
                Category = CleanerCategory.GamingCache
            },

            // ══════════════════════════════════════════════════════════════
            // SYSTÈME AVANCÉ
            // ══════════════════════════════════════════════════════════════
            new CleanerProfile
            {
                Name = "Windows Defender - Définitions anciennes",
                Description = "Anciennes définitions antivirus",
                FolderPath = Path.Combine(programData, "Microsoft", "Windows Defender", "Definition Updates", "Backup"),
                Icon = "🛡️",
                RequiresAdmin = true,
                Category = CleanerCategory.SystemAdvanced
            },
            new CleanerProfile
            {
                Name = "Windows Search - Index",
                Description = "⚠️ Index de recherche Windows (sera reconstruit)",
                FolderPath = Path.Combine(programData, "Microsoft", "Search", "Data", "Applications", "Windows"),
                Icon = "🔍",
                RequiresAdmin = true,
                IsSafe = false,
                IsEnabled = false,
                Category = CleanerCategory.SystemAdvanced
            },

            // ══════════════════════════════════════════════════════════════
            // CONFIDENTIALITÉ - HISTORIQUE SYSTÈME
            // ══════════════════════════════════════════════════════════════
            new CleanerProfile
            {
                Name = "Documents récents",
                Description = "Liste des fichiers récemment ouverts",
                FolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Recent),
                Icon = "📂",
                IsPrivacy = true,
                Category = CleanerCategory.RecentDocs
            },
            new CleanerProfile
            {
                Name = "Jump Lists automatiques",
                Description = "Listes de raccourcis automatiques",
                FolderPath = Path.Combine(appData, "Microsoft", "Windows", "Recent", "AutomaticDestinations"),
                Icon = "📂",
                IsPrivacy = true,
                Category = CleanerCategory.RecentDocs
            },
            new CleanerProfile
            {
                Name = "Jump Lists personnalisées",
                Description = "Listes de raccourcis personnalisées",
                FolderPath = Path.Combine(appData, "Microsoft", "Windows", "Recent", "CustomDestinations"),
                Icon = "📂",
                IsPrivacy = true,
                Category = CleanerCategory.RecentDocs
            },

            // ══════════════════════════════════════════════════════════════
            // NETTOYAGE AVANCÉ (DANGEREUX)
            // ══════════════════════════════════════════════════════════════
            new CleanerProfile
            {
                Name = "Windows.old",
                Description = "⚠️ Ancienne installation Windows (TRÈS VOLUMINEUX)",
                FolderPath = @"C:\Windows.old",
                Icon = "🔒",
                RequiresAdmin = true,
                IsSafe = false,
                IsEnabled = false,
                Category = CleanerCategory.OldWindowsInstall
            },
            new CleanerProfile
            {
                Name = "$Windows.~BT",
                Description = "⚠️ Fichiers de mise à niveau Windows",
                FolderPath = @"C:\$Windows.~BT",
                Icon = "🔒",
                RequiresAdmin = true,
                IsSafe = false,
                IsEnabled = false,
                Category = CleanerCategory.OldWindowsInstall
            },
            new CleanerProfile
            {
                Name = "$Windows.~WS",
                Description = "⚠️ Fichiers de mise à niveau Windows",
                FolderPath = @"C:\$Windows.~WS",
                Icon = "🔒",
                RequiresAdmin = true,
                IsSafe = false,
                IsEnabled = false,
                Category = CleanerCategory.OldWindowsInstall
            },
            new CleanerProfile
            {
                Name = "Téléchargements anciens",
                Description = "Fichiers téléchargés il y a plus de 30 jours",
                FolderPath = Path.Combine(userProfile, "Downloads"),
                Icon = "📥",
                MinAgeDays = 30,
                IsEnabled = false,
                Category = CleanerCategory.General
            }
        ];
    }
}
