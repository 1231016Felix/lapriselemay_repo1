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

        return
        [
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
                Name = "Cache Windows Update",
                Description = "Fichiers de mise à jour Windows",
                FolderPath = System.IO.Path.Combine(windowsPath, "SoftwareDistribution", "Download"),
                Icon = "🔄",
                MinAgeDays = 7
            },
            new CleanerProfile
            {
                Name = "Prefetch Windows",
                Description = "Fichiers de préchargement",
                FolderPath = System.IO.Path.Combine(windowsPath, "Prefetch"),
                Icon = "⚡",
                MinAgeDays = 30
            },
            new CleanerProfile
            {
                Name = "Cache navigateurs",
                Description = "Cache des navigateurs web",
                FolderPath = localAppData,
                SearchPattern = "Cache*",
                Icon = "🌐",
                MinAgeDays = 7
            },
            new CleanerProfile
            {
                Name = "Corbeille",
                Description = "Fichiers dans la corbeille",
                FolderPath = @"C:\$Recycle.Bin",
                Icon = "♻️",
                MinAgeDays = 0
            },
            new CleanerProfile
            {
                Name = "Logs système",
                Description = "Fichiers journaux Windows",
                FolderPath = System.IO.Path.Combine(windowsPath, "Logs"),
                SearchPattern = "*.log",
                Icon = "📋",
                MinAgeDays = 30
            },
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
                Name = "Téléchargements anciens",
                Description = "Fichiers téléchargés il y a plus de 30 jours",
                FolderPath = System.IO.Path.Combine(userProfile, "Downloads"),
                Icon = "📥",
                MinAgeDays = 30
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
            }
        ];
    }
}
