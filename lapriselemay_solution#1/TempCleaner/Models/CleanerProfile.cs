using CommunityToolkit.Mvvm.ComponentModel;

namespace TempCleaner.Models;

public partial class CleanerProfile : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _folderPath = string.Empty;

    [ObservableProperty]
    private string _searchPattern = "*.*";

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _includeSubdirectories = true;

    [ObservableProperty]
    private int _minAgeDays;

    [ObservableProperty]
    private string _icon = "📁";

    [ObservableProperty]
    private bool _requiresAdmin;

    [ObservableProperty]
    private long _totalSize;

    [ObservableProperty]
    private int _fileCount;

    public string TotalSizeFormatted => FormatSize(TotalSize);

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
        var tempPath = System.IO.Path.GetTempPath();
        var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        return
        [
            // === FICHIERS TEMPORAIRES ===
            new CleanerProfile
            {
                Name = "Fichiers temporaires Windows",
                Description = "Dossier TEMP système",
                FolderPath = tempPath,
                Icon = "🗑️",
                MinAgeDays = 1
            },
            new CleanerProfile
            {
                Name = "Temp utilisateur",
                Description = "Dossier TEMP local utilisateur",
                FolderPath = System.IO.Path.Combine(localAppData, "Temp"),
                Icon = "🗑️",
                MinAgeDays = 1
            },
            
            // === WINDOWS UPDATE & SYSTÈME (ADMIN) ===
            new CleanerProfile
            {
                Name = "Cache Windows Update",
                Description = "Fichiers de mise à jour Windows",
                FolderPath = System.IO.Path.Combine(windowsPath, "SoftwareDistribution", "Download"),
                Icon = "🔄",
                MinAgeDays = 7,
                RequiresAdmin = true
            },
            new CleanerProfile
            {
                Name = "Prefetch Windows",
                Description = "Fichiers de préchargement",
                FolderPath = System.IO.Path.Combine(windowsPath, "Prefetch"),
                Icon = "⚡",
                MinAgeDays = 30,
                RequiresAdmin = true
            },
            new CleanerProfile
            {
                Name = "Windows Installer Cache",
                Description = "Cache des installations Windows",
                FolderPath = System.IO.Path.Combine(windowsPath, "Installer", "$PatchCache$"),
                Icon = "📦",
                MinAgeDays = 30,
                RequiresAdmin = true
            },
            
            // === NAVIGATEURS ===
            new CleanerProfile
            {
                Name = "Cache Chrome",
                Description = "Cache du navigateur Google Chrome",
                FolderPath = System.IO.Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache"),
                Icon = "🌐",
                MinAgeDays = 0
            },
            new CleanerProfile
            {
                Name = "Cache Edge",
                Description = "Cache du navigateur Microsoft Edge",
                FolderPath = System.IO.Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache"),
                Icon = "🌐",
                MinAgeDays = 0
            },
            new CleanerProfile
            {
                Name = "Cache Firefox",
                Description = "Cache du navigateur Firefox",
                FolderPath = System.IO.Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles"),
                SearchPattern = "cache2*",
                Icon = "🦊",
                MinAgeDays = 0
            },
            
            // === APPLICATIONS ===
            new CleanerProfile
            {
                Name = "Cache Visual Studio",
                Description = "Cache et fichiers temporaires VS",
                FolderPath = System.IO.Path.Combine(localAppData, "Microsoft", "VisualStudio"),
                SearchPattern = "*.tmp",
                Icon = "💻",
                MinAgeDays = 7
            },
            new CleanerProfile
            {
                Name = "Cache NuGet",
                Description = "Packages NuGet en cache",
                FolderPath = System.IO.Path.Combine(userProfile, ".nuget", "packages"),
                Icon = "📦",
                MinAgeDays = 90,
                IsEnabled = false // Désactivé par défaut (peut casser des builds)
            },
            new CleanerProfile
            {
                Name = "Cache npm",
                Description = "Cache des packages npm",
                FolderPath = System.IO.Path.Combine(localAppData, "npm-cache"),
                Icon = "📦",
                MinAgeDays = 30,
                IsEnabled = false
            },
            new CleanerProfile
            {
                Name = "Cache pip",
                Description = "Cache des packages Python",
                FolderPath = System.IO.Path.Combine(localAppData, "pip", "cache"),
                Icon = "🐍",
                MinAgeDays = 30,
                IsEnabled = false
            },
            
            // === LOGS & RAPPORTS ===
            new CleanerProfile
            {
                Name = "Logs système",
                Description = "Fichiers journaux Windows",
                FolderPath = System.IO.Path.Combine(windowsPath, "Logs"),
                SearchPattern = "*.log",
                Icon = "📋",
                MinAgeDays = 30,
                RequiresAdmin = true
            },
            new CleanerProfile
            {
                Name = "Logs CBS",
                Description = "Journaux Component Based Servicing",
                FolderPath = System.IO.Path.Combine(windowsPath, "Logs", "CBS"),
                SearchPattern = "*.log",
                Icon = "📋",
                MinAgeDays = 14,
                RequiresAdmin = true
            },
            new CleanerProfile
            {
                Name = "Crash dumps",
                Description = "Fichiers de rapport d'erreurs",
                FolderPath = System.IO.Path.Combine(localAppData, "CrashDumps"),
                Icon = "💥",
                MinAgeDays = 0
            },
            new CleanerProfile
            {
                Name = "Windows Error Reports",
                Description = "Rapports d'erreurs Windows",
                FolderPath = System.IO.Path.Combine(localAppData, "Microsoft", "Windows", "WER"),
                Icon = "⚠️",
                MinAgeDays = 7
            },
            new CleanerProfile
            {
                Name = "Memory dumps",
                Description = "Dumps mémoire système",
                FolderPath = windowsPath,
                SearchPattern = "*.dmp",
                Icon = "💾",
                MinAgeDays = 0,
                IncludeSubdirectories = false,
                RequiresAdmin = true
            },
            
            // === MINIATURES & CACHES WINDOWS ===
            new CleanerProfile
            {
                Name = "Miniatures Windows",
                Description = "Cache des miniatures",
                FolderPath = System.IO.Path.Combine(localAppData, "Microsoft", "Windows", "Explorer"),
                SearchPattern = "thumbcache_*.db",
                Icon = "🖼️",
                MinAgeDays = 0,
                IncludeSubdirectories = false
            },
            new CleanerProfile
            {
                Name = "Cache icônes",
                Description = "Cache des icônes Windows",
                FolderPath = System.IO.Path.Combine(localAppData, "Microsoft", "Windows", "Explorer"),
                SearchPattern = "iconcache_*.db",
                Icon = "🎨",
                MinAgeDays = 0,
                IncludeSubdirectories = false
            },
            new CleanerProfile
            {
                Name = "Cache fonts",
                Description = "Cache des polices Windows",
                FolderPath = System.IO.Path.Combine(windowsPath, "ServiceProfiles", "LocalService", "AppData", "Local"),
                SearchPattern = "FontCache*",
                Icon = "🔤",
                MinAgeDays = 0,
                RequiresAdmin = true
            },
            
            // === CORBEILLE & TÉLÉCHARGEMENTS ===
            new CleanerProfile
            {
                Name = "Corbeille",
                Description = "Fichiers dans la corbeille",
                FolderPath = @"C:\$Recycle.Bin",
                Icon = "♻️",
                MinAgeDays = 0,
                RequiresAdmin = true
            },
            new CleanerProfile
            {
                Name = "Téléchargements anciens",
                Description = "Fichiers téléchargés il y a plus de 30 jours",
                FolderPath = System.IO.Path.Combine(userProfile, "Downloads"),
                Icon = "📥",
                MinAgeDays = 30
            },
            
            // === HISTORIQUES ===
            new CleanerProfile
            {
                Name = "Historique récent",
                Description = "Éléments récents Windows",
                FolderPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Recent)),
                Icon = "📂",
                MinAgeDays = 30
            },
            new CleanerProfile
            {
                Name = "Fichiers .tmp anciens",
                Description = "Tous les fichiers .tmp de l'utilisateur",
                FolderPath = userProfile,
                SearchPattern = "*.tmp",
                Icon = "📄",
                MinAgeDays = 7
            },
            
            // === NETTOYAGE AVANCÉ (ADMIN REQUIS) ===
            new CleanerProfile
            {
                Name = "Windows Temp système",
                Description = "Dossier Temp système Windows",
                FolderPath = System.IO.Path.Combine(windowsPath, "Temp"),
                Icon = "🔒",
                MinAgeDays = 1,
                RequiresAdmin = true
            },
            new CleanerProfile
            {
                Name = "Delivery Optimization",
                Description = "Cache de téléchargement Windows Update P2P",
                FolderPath = System.IO.Path.Combine(windowsPath, "SoftwareDistribution", "DeliveryOptimization"),
                Icon = "🔒",
                MinAgeDays = 7,
                RequiresAdmin = true
            },
            new CleanerProfile
            {
                Name = "Windows.old",
                Description = "Ancienne installation Windows",
                FolderPath = @"C:\Windows.old",
                Icon = "🔒",
                MinAgeDays = 0,
                RequiresAdmin = true,
                IsEnabled = false // Désactivé par défaut - dangereux
            },
            new CleanerProfile
            {
                Name = "DISM/CBS Logs",
                Description = "Journaux de maintenance Windows",
                FolderPath = System.IO.Path.Combine(windowsPath, "Logs", "DISM"),
                Icon = "🔒",
                MinAgeDays = 7,
                RequiresAdmin = true
            },
            new CleanerProfile
            {
                Name = "Windows Defender Scans",
                Description = "Historique des analyses Defender",
                FolderPath = System.IO.Path.Combine(programData, "Microsoft", "Windows Defender", "Scans", "History"),
                Icon = "🛡️",
                MinAgeDays = 30,
                RequiresAdmin = true
            },
            new CleanerProfile
            {
                Name = "System Error Memory Dump",
                Description = "Dumps mémoire d'erreur système",
                FolderPath = @"C:\",
                SearchPattern = "*.dmp",
                Icon = "🔒",
                MinAgeDays = 0,
                IncludeSubdirectories = false,
                RequiresAdmin = true
            }
        ];
    }
}
