using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using TempCleaner.Helpers;

namespace TempCleaner.Models;

public partial class CleanerProfile : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _detailedWarning = string.Empty;

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

    public string TotalSizeFormatted => FileSizeHelper.Format(TotalSize);

    public string GetDetailedWarning()
    {
        var warning = new StringBuilder();
        
        if (!string.IsNullOrWhiteSpace(DetailedWarning))
        {
            warning.AppendLine(DetailedWarning.Trim());
            warning.AppendLine();
        }
        
        warning.AppendLine($"📁 {Name}");
        warning.AppendLine(new string('═', 50));
        warning.AppendLine();
        warning.AppendLine($"📝 Description:\n   {Description}");
        warning.AppendLine();
        warning.AppendLine($"📂 Emplacement:\n   {FolderPath}");
        warning.AppendLine();
        
        if (SearchPattern != "*.*")
        {
            warning.AppendLine($"🔍 Fichiers ciblés:\n   {SearchPattern}");
            warning.AppendLine();
        }
        
        warning.AppendLine("⚙️ Options:");
        warning.AppendLine($"   • Sous-dossiers inclus: {(IncludeSubdirectories ? "Oui" : "Non")}");
        warning.AppendLine(MinAgeDays > 0 
            ? $"   • Fichiers de plus de {MinAgeDays} jour(s) uniquement"
            : "   • Tous les fichiers (aucune limite d'âge)");
        
        warning.AppendLine();
        warning.AppendLine(new string('─', 50));
        
        AppendSafetyWarnings(warning);
        warning.AppendLine();
        warning.AppendLine(GetCategoryWarning());
        
        return warning.ToString();
    }
    
    private void AppendSafetyWarnings(StringBuilder warning)
    {
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
    }


    private string GetCategoryWarning() => Category switch
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
            // FICHIERS TEMPORAIRES SYSTÈME
            CreateProfile("Fichiers temporaires Windows", "Dossier TEMP système", tempPath, "🗑️", CleanerCategory.WindowsTemp, minAgeDays: 1),
            CreateProfile("Temp utilisateur", "Dossier TEMP local utilisateur", Path.Combine(localAppData, "Temp"), "🗑️", CleanerCategory.UserTemp, minAgeDays: 1),
            CreateProfile("Windows Temp système", "Dossier Temp système Windows", Path.Combine(windowsPath, "Temp"), "🔒", CleanerCategory.WindowsTemp, minAgeDays: 1, requiresAdmin: true),


            // NAVIGATEURS - CACHE
            CreateProfile("Google Chrome - Cache", "Pages web, images et scripts en cache", Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache"), "🌐", CleanerCategory.BrowserCache),
            CreateProfile("Chrome - Code Cache", "Cache de code JavaScript compilé", Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Code Cache"), "🌐", CleanerCategory.BrowserCache),
            CreateProfile("Chrome - GPU Cache", "Cache GPU pour l'accélération matérielle", Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "GPUCache"), "🌐", CleanerCategory.BrowserCache),
            CreateProfile("Chrome - Shader Cache", "Cache des shaders graphiques", Path.Combine(localAppData, "Google", "Chrome", "User Data", "ShaderCache"), "🌐", CleanerCategory.BrowserCache),
            CreateProfile("Microsoft Edge - Cache", "Cache du navigateur Edge", Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache"), "🌐", CleanerCategory.BrowserCache),
            CreateProfile("Edge - Code Cache", "Cache de code JavaScript Edge", Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Code Cache"), "🌐", CleanerCategory.BrowserCache),
            CreateProfile("Edge - GPU Cache", "Cache GPU Edge", Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "GPUCache"), "🌐", CleanerCategory.BrowserCache),
            CreateProfile("Mozilla Firefox - Cache", "Cache du navigateur Firefox", Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles"), "🦊", CleanerCategory.BrowserCache, searchPattern: "cache2*"),
            CreateProfile("Firefox - Offline Cache", "Cache hors-ligne Firefox", Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles"), "🦊", CleanerCategory.BrowserCache, searchPattern: "OfflineCache*"),
            CreateProfile("Opera - Cache", "Cache du navigateur Opera", Path.Combine(appData, "Opera Software", "Opera Stable", "Cache"), "🔴", CleanerCategory.BrowserCache),
            CreateProfile("Opera GX - Cache", "Cache du navigateur Opera GX", Path.Combine(appData, "Opera Software", "Opera GX Stable", "Cache"), "🔴", CleanerCategory.BrowserCache),
            CreateProfile("Brave - Cache", "Cache du navigateur Brave", Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Cache"), "🦁", CleanerCategory.BrowserCache),
            CreateProfile("Brave - Code Cache", "Cache de code Brave", Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Code Cache"), "🦁", CleanerCategory.BrowserCache),

            // NAVIGATEURS - CONFIDENTIALITÉ
            CreateProfile("Chrome - Historique", "Historique de navigation (vous déconnectera)", Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default"), "🔒", CleanerCategory.BrowserHistory, searchPattern: "History*", isPrivacy: true, includeSubdirectories: false),
            CreateProfile("Chrome - Cookies", "Cookies des sites web (vous déconnectera)", Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Network"), "🍪", CleanerCategory.BrowserCookies, searchPattern: "Cookies*", isPrivacy: true, includeSubdirectories: false),
            CreateProfile("Edge - Historique", "Historique de navigation Edge", Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default"), "🔒", CleanerCategory.BrowserHistory, searchPattern: "History*", isPrivacy: true, includeSubdirectories: false),
            CreateProfile("Edge - Cookies", "Cookies Edge", Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Network"), "🍪", CleanerCategory.BrowserCookies, searchPattern: "Cookies*", isPrivacy: true, includeSubdirectories: false),
            CreateProfile("Firefox - Historique", "Historique Firefox (places.sqlite)", Path.Combine(appData, "Mozilla", "Firefox", "Profiles"), "🔒", CleanerCategory.BrowserHistory, searchPattern: "places.sqlite*", isPrivacy: true),
            CreateProfile("Firefox - Cookies", "Cookies Firefox", Path.Combine(appData, "Mozilla", "Firefox", "Profiles"), "🍪", CleanerCategory.BrowserCookies, searchPattern: "cookies.sqlite*", isPrivacy: true),

            // APPLICATIONS DE DÉVELOPPEMENT
            CreateProfile("VS Code - Cache", "Cache Visual Studio Code", Path.Combine(appData, "Code", "Cache"), "💻", CleanerCategory.ApplicationCache),
            CreateProfile("VS Code - Cached Data", "Données en cache VS Code", Path.Combine(appData, "Code", "CachedData"), "💻", CleanerCategory.ApplicationCache),
            CreateProfile("VS Code - Extensions Cache", "Cache des extensions VS Code", Path.Combine(appData, "Code", "CachedExtensions"), "💻", CleanerCategory.ApplicationCache),
            CreateProfile("VS Code - Logs", "Journaux VS Code (7+ jours)", Path.Combine(appData, "Code", "logs"), "📋", CleanerCategory.ApplicationCache, minAgeDays: 7),
            CreateProfile("Cache Visual Studio", "Fichiers temporaires Visual Studio", Path.Combine(localAppData, "Microsoft", "VisualStudio"), "💻", CleanerCategory.ApplicationCache, searchPattern: "*.tmp", minAgeDays: 7),
            CreateProfile("Cache NuGet", "Packages NuGet en cache", Path.Combine(localAppData, "NuGet", "v3-cache"), "📦", CleanerCategory.ApplicationCache),
            CreateProfile("Packages NuGet anciens", "Packages NuGet non utilisés depuis 30+ jours", Path.Combine(userProfile, ".nuget", "packages"), "📦", CleanerCategory.ApplicationCache, minAgeDays: 30),
            CreateProfile("Cache npm", "Cache des packages Node.js", Path.Combine(appData, "npm-cache"), "📦", CleanerCategory.ApplicationCache),
            CreateProfile("Cache npm (Local)", "Cache npm local", Path.Combine(localAppData, "npm-cache"), "📦", CleanerCategory.ApplicationCache),
            CreateProfile("Cache pip", "Cache des packages Python", Path.Combine(localAppData, "pip", "cache"), "🐍", CleanerCategory.ApplicationCache),


            // JEUX
            CreateProfile("Steam - Logs", "Journaux du client Steam (7+ jours)", @"C:\Program Files (x86)\Steam\logs", "🎮", CleanerCategory.GamingCache, searchPattern: "*.txt", minAgeDays: 7),
            CreateProfile("Steam - Dumps", "Fichiers de crash Steam", @"C:\Program Files (x86)\Steam\dumps", "🎮", CleanerCategory.GamingCache),
            CreateProfile("Steam - HTML Cache", "Cache web du client Steam", Path.Combine(localAppData, "Steam", "htmlcache"), "🎮", CleanerCategory.GamingCache),
            CreateProfile("Epic Games - Web Cache", "Cache web Epic Games Launcher", Path.Combine(localAppData, "EpicGamesLauncher", "Saved", "webcache"), "🎮", CleanerCategory.GamingCache),
            CreateProfile("Epic Games - Logs", "Journaux Epic Games (7+ jours)", Path.Combine(localAppData, "EpicGamesLauncher", "Saved", "Logs"), "🎮", CleanerCategory.GamingCache, searchPattern: "*.log", minAgeDays: 7),
            CreateProfile("Origin - Cache", "Cache EA Origin", Path.Combine(appData, "Origin", "Logs"), "🎮", CleanerCategory.GamingCache),
            CreateProfile("Ubisoft Connect - Cache", "Cache Ubisoft Connect", Path.Combine(localAppData, "Ubisoft Game Launcher", "cache"), "🎮", CleanerCategory.GamingCache),
            CreateProfile("GOG Galaxy - Cache", "Cache GOG Galaxy", Path.Combine(localAppData, "GOG.com", "Galaxy", "webcache"), "🎮", CleanerCategory.GamingCache),
            CreateProfile("Riot Games - Logs", "Logs Riot Client (League, Valorant)", Path.Combine(localAppData, "Riot Games", "Riot Client", "Logs"), "🎮", CleanerCategory.GamingCache, searchPattern: "*.log", minAgeDays: 7),
            CreateProfile("Battle.net - Cache", "Cache Battle.net", Path.Combine(appData, "Battle.net", "Cache"), "🎮", CleanerCategory.GamingCache),
            CreateProfile("NVIDIA - Shader Cache", "Cache des shaders NVIDIA", Path.Combine(localAppData, "NVIDIA", "DXCache"), "🖥️", CleanerCategory.GamingCache),
            CreateProfile("NVIDIA - GLCache", "Cache OpenGL NVIDIA", Path.Combine(localAppData, "NVIDIA", "GLCache"), "🖥️", CleanerCategory.GamingCache),
            CreateProfile("AMD - Shader Cache", "Cache des shaders AMD", Path.Combine(localAppData, "AMD", "DxCache"), "🖥️", CleanerCategory.GamingCache),
            CreateProfile("DirectX Shader Cache", "Cache shaders DirectX système", Path.Combine(localAppData, "D3DSCache"), "🖥️", CleanerCategory.GamingCache),

            // SYSTÈME WINDOWS - CACHES
            CreateProfile("Miniatures Windows", "Cache des miniatures Explorer", Path.Combine(localAppData, "Microsoft", "Windows", "Explorer"), "🖼️", CleanerCategory.WindowsCache, searchPattern: "thumbcache_*.db", includeSubdirectories: false),
            CreateProfile("Cache icônes", "Cache des icônes Windows", Path.Combine(localAppData, "Microsoft", "Windows", "Explorer"), "🎨", CleanerCategory.WindowsCache, searchPattern: "iconcache_*.db", includeSubdirectories: false),
            CreateProfile("Cache fonts", "Cache des polices Windows", Path.Combine(windowsPath, "ServiceProfiles", "LocalService", "AppData", "Local"), "🔤", CleanerCategory.WindowsCache, searchPattern: "FontCache*", requiresAdmin: true),

            // SYSTÈME WINDOWS - LOGS ET RAPPORTS
            CreateProfile("Logs Windows", "Fichiers journaux Windows (7+ jours)", Path.Combine(windowsPath, "Logs"), "📋", CleanerCategory.WindowsLogs, searchPattern: "*.log", minAgeDays: 7, requiresAdmin: true),
            CreateProfile("Logs Panther", "Logs d'installation Windows (30+ jours)", Path.Combine(windowsPath, "Panther"), "📋", CleanerCategory.WindowsLogs, searchPattern: "*.log", minAgeDays: 30, requiresAdmin: true),
            CreateProfile("Logs CBS", "Journaux Component Based Servicing", Path.Combine(windowsPath, "Logs", "CBS"), "📋", CleanerCategory.WindowsLogs, searchPattern: "*.log", minAgeDays: 14, requiresAdmin: true),
            CreateProfile("Logs DISM", "Journaux de maintenance Windows", Path.Combine(windowsPath, "Logs", "DISM"), "📋", CleanerCategory.WindowsLogs, minAgeDays: 7, requiresAdmin: true),
            CreateProfile("Crash dumps utilisateur", "Fichiers de crash dumps locaux", Path.Combine(localAppData, "CrashDumps"), "💥", CleanerCategory.MemoryDumps),
            CreateProfile("Memory dumps système", "Dumps mémoire Windows", windowsPath, "💾", CleanerCategory.MemoryDumps, searchPattern: "*.dmp", includeSubdirectories: false, requiresAdmin: true),
            CreateProfile("Minidumps Windows", "Mini dumps de crash système", Path.Combine(windowsPath, "Minidump"), "💾", CleanerCategory.MemoryDumps, searchPattern: "*.dmp", requiresAdmin: true),
            CreateProfile("Windows Error Reports", "Rapports d'erreurs WER locaux", Path.Combine(localAppData, "Microsoft", "Windows", "WER"), "⚠️", CleanerCategory.ErrorReports, minAgeDays: 7),
            CreateProfile("System Error Reports", "Rapports d'erreurs système", Path.Combine(programData, "Microsoft", "Windows", "WER"), "⚠️", CleanerCategory.ErrorReports, requiresAdmin: true),


            // SYSTÈME WINDOWS - MISES À JOUR ET MAINTENANCE
            CreateProfile("Cache Windows Update", "Fichiers de mise à jour téléchargés", Path.Combine(windowsPath, "SoftwareDistribution", "Download"), "🔄", CleanerCategory.WindowsUpdate, requiresAdmin: true),
            CreateProfile("Delivery Optimization", "Cache P2P Windows Update", Path.Combine(windowsPath, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache"), "🔄", CleanerCategory.DeliveryOptimization, requiresAdmin: true),
            CreateProfile("Prefetch Windows", "Fichiers de préchargement (7+ jours)", Path.Combine(windowsPath, "Prefetch"), "⚡", CleanerCategory.Prefetch, searchPattern: "*.pf", minAgeDays: 7, includeSubdirectories: false, requiresAdmin: true),
            CreateProfile("Windows Installer Cache", "Cache des installations Windows", Path.Combine(windowsPath, "Installer", "$PatchCache$"), "📦", CleanerCategory.WindowsUpdate, minAgeDays: 30, requiresAdmin: true),
            CreateProfile("Windows Defender Scans", "Historique des analyses Defender", Path.Combine(programData, "Microsoft", "Windows Defender", "Scans", "History"), "🛡️", CleanerCategory.WindowsCache, minAgeDays: 30, requiresAdmin: true),

            // APPLICATIONS DE COMMUNICATION
            CreateProfile("Microsoft Teams - Cache", "Cache de l'application Teams", Path.Combine(appData, "Microsoft", "Teams", "Cache"), "💬", CleanerCategory.CommunicationApps),
            CreateProfile("Teams - Service Worker", "Cache Service Worker Teams", Path.Combine(appData, "Microsoft", "Teams", "Service Worker", "CacheStorage"), "💬", CleanerCategory.CommunicationApps),
            CreateProfile("Teams - Blob Storage", "Stockage blob Teams", Path.Combine(appData, "Microsoft", "Teams", "blob_storage"), "💬", CleanerCategory.CommunicationApps),
            CreateProfile("Teams - GPU Cache", "Cache GPU Teams", Path.Combine(appData, "Microsoft", "Teams", "GPUCache"), "💬", CleanerCategory.CommunicationApps),
            CreateProfile("Discord - Cache", "Cache de Discord", Path.Combine(appData, "discord", "Cache"), "🎮", CleanerCategory.CommunicationApps),
            CreateProfile("Discord - Code Cache", "Cache de code Discord", Path.Combine(appData, "discord", "Code Cache"), "🎮", CleanerCategory.CommunicationApps),
            CreateProfile("Discord - GPU Cache", "Cache GPU Discord", Path.Combine(appData, "discord", "GPUCache"), "🎮", CleanerCategory.CommunicationApps),
            CreateProfile("Slack - Cache", "Cache de Slack", Path.Combine(appData, "Slack", "Cache"), "💼", CleanerCategory.CommunicationApps),
            CreateProfile("Slack - Service Worker", "Cache Service Worker Slack", Path.Combine(appData, "Slack", "Service Worker", "CacheStorage"), "💼", CleanerCategory.CommunicationApps),
            CreateProfile("Zoom - Cache", "Cache de Zoom", Path.Combine(appData, "Zoom", "data"), "📹", CleanerCategory.CommunicationApps),
            CreateProfile("Telegram - Cache", "Cache de Telegram Desktop", Path.Combine(appData, "Telegram Desktop", "tdata", "user_data"), "✈️", CleanerCategory.CommunicationApps),
            CreateProfile("WhatsApp - Cache", "Cache de WhatsApp Desktop", Path.Combine(localAppData, "Packages", "5319275A.WhatsAppDesktop_cv1g1gvanyjgm", "LocalCache"), "📱", CleanerCategory.CommunicationApps),

            // APPLICATIONS MÉDIAS & STREAMING
            CreateProfile("Spotify - Cache", "Cache musique Spotify (peut être volumineux)", Path.Combine(localAppData, "Spotify", "Storage"), "🎵", CleanerCategory.MediaApps),
            CreateProfile("Spotify - Data", "Données en cache Spotify", Path.Combine(localAppData, "Spotify", "Data"), "🎵", CleanerCategory.MediaApps),
            CreateProfile("VLC - Cache", "Cache du lecteur VLC", Path.Combine(appData, "vlc"), "🎬", CleanerCategory.MediaApps, searchPattern: "*.dat"),
            CreateProfile("iTunes - Cache", "Cache iTunes", Path.Combine(localAppData, "Apple Computer", "iTunes"), "🎵", CleanerCategory.MediaApps),

            // ADOBE CREATIVE CLOUD
            CreateProfile("Adobe - Cache Média", "Cache média commun Adobe", Path.Combine(appData, "Adobe", "Common", "Media Cache Files"), "🎨", CleanerCategory.AdobeApps),
            CreateProfile("Adobe - Cache Base de données", "Cache base de données média Adobe", Path.Combine(appData, "Adobe", "Common", "Media Cache"), "🎨", CleanerCategory.AdobeApps),
            CreateProfile("Photoshop - Temp", "Fichiers temporaires Photoshop", Path.Combine(localAppData, "Temp", "Photoshop Temp"), "🖼️", CleanerCategory.AdobeApps),
            CreateProfile("Premiere Pro - Cache Média", "Cache média Premiere Pro", Path.Combine(appData, "Adobe", "Common", "Peak Files"), "🎬", CleanerCategory.AdobeApps),
            CreateProfile("After Effects - Cache", "Cache disque After Effects", Path.Combine(localAppData, "Adobe", "After Effects"), "🎬", CleanerCategory.AdobeApps, searchPattern: "*Cache*"),
            CreateProfile("Adobe - Logs", "Journaux Adobe (7+ jours)", Path.Combine(localAppData, "Adobe", "Logs"), "📋", CleanerCategory.AdobeApps, searchPattern: "*.log", minAgeDays: 7),


            // CLOUD & SYNCHRONISATION
            CreateProfile("OneDrive - Cache", "Cache local OneDrive", Path.Combine(localAppData, "Microsoft", "OneDrive", "logs"), "☁️", CleanerCategory.CloudSync),
            CreateProfile("Dropbox - Cache", "Cache Dropbox", Path.Combine(localAppData, "Dropbox", "host.dbx"), "📦", CleanerCategory.CloudSync),
            CreateProfile("Google Drive - Cache", "Cache Google Drive", Path.Combine(localAppData, "Google", "DriveFS"), "📁", CleanerCategory.CloudSync, searchPattern: "*.log"),
            CreateProfile("iCloud - Cache", "Cache iCloud pour Windows", Path.Combine(localAppData, "Apple Inc", "iCloud"), "☁️", CleanerCategory.CloudSync),

            // MICROSOFT STORE & APPS
            CreateProfile("Microsoft Store - Cache", "Cache du Windows Store", Path.Combine(localAppData, "Packages", "Microsoft.WindowsStore_8wekyb3d8bbwe", "LocalCache"), "🛒", CleanerCategory.WindowsStore),
            CreateProfile("Xbox - Cache", "Cache de l'application Xbox", Path.Combine(localAppData, "Packages", "Microsoft.XboxApp_8wekyb3d8bbwe", "LocalCache"), "🎮", CleanerCategory.WindowsStore),
            CreateProfile("Courrier - Cache", "Cache de l'application Courrier", Path.Combine(localAppData, "Packages", "microsoft.windowscommunicationsapps_8wekyb3d8bbwe", "LocalCache"), "📧", CleanerCategory.WindowsStore),
            CreateProfile("Photos - Cache", "Cache de l'application Photos", Path.Combine(localAppData, "Packages", "Microsoft.Windows.Photos_8wekyb3d8bbwe", "LocalCache"), "🖼️", CleanerCategory.WindowsStore),

            // SYSTÈME AVANCÉ
            CreateProfile("Windows Defender - Définitions anciennes", "Anciennes définitions antivirus", Path.Combine(programData, "Microsoft", "Windows Defender", "Definition Updates", "Backup"), "🛡️", CleanerCategory.SystemAdvanced, requiresAdmin: true),
            CreateProfile("Windows Search - Index", "⚠️ Index de recherche Windows (sera reconstruit)", Path.Combine(programData, "Microsoft", "Search", "Data", "Applications", "Windows"), "🔍", CleanerCategory.SystemAdvanced, requiresAdmin: true, isSafe: false),

            // CONFIDENTIALITÉ - HISTORIQUE SYSTÈME
            CreateProfile("Documents récents", "Liste des fichiers récemment ouverts (raccourcis .lnk)", Environment.GetFolderPath(Environment.SpecialFolder.Recent), "📂", CleanerCategory.RecentDocs, searchPattern: "*.lnk", includeSubdirectories: false, isPrivacy: true),
            CreateProfile("Jump Lists automatiques", "Listes de raccourcis automatiques", Path.Combine(appData, "Microsoft", "Windows", "Recent", "AutomaticDestinations"), "📂", CleanerCategory.RecentDocs, isPrivacy: true),
            CreateProfileWithWarning("Jump Lists personnalisées", "Listes de raccourcis personnalisées", 
                Path.Combine(appData, "Microsoft", "Windows", "Recent", "CustomDestinations"), "📌", CleanerCategory.RecentDocs, isPrivacy: true,
                """
                ╔══════════════════════════════════════════════════════════════╗
                ║           ⚠️  ATTENTION - ACCÈS RAPIDE  ⚠️                  ║
                ╠══════════════════════════════════════════════════════════════╣
                ║                                                              ║
                ║  Cette option va SUPPRIMER tous les dossiers et fichiers    ║
                ║  que vous avez ÉPINGLÉS MANUELLEMENT dans l'Accès rapide    ║
                ║  de l'Explorateur Windows !                                  ║
                ║                                                              ║
                ║  📌 Dossiers épinglés → SUPPRIMÉS                           ║
                ║  📌 Fichiers épinglés → SUPPRIMÉS                           ║
                ║                                                              ║
                ║  Vous devrez ré-épingler manuellement tous vos favoris.     ║
                ║                                                              ║
                ╚══════════════════════════════════════════════════════════════╝
                """),

            // NETTOYAGE AVANCÉ (DANGEREUX)
            CreateProfile("Windows.old", "⚠️ Ancienne installation Windows (TRÈS VOLUMINEUX)", @"C:\Windows.old", "🔒", CleanerCategory.OldWindowsInstall, requiresAdmin: true, isSafe: false),
            CreateProfile("$Windows.~BT", "⚠️ Fichiers de mise à niveau Windows", @"C:\$Windows.~BT", "🔒", CleanerCategory.OldWindowsInstall, requiresAdmin: true, isSafe: false),
            CreateProfile("$Windows.~WS", "⚠️ Fichiers de mise à niveau Windows", @"C:\$Windows.~WS", "🔒", CleanerCategory.OldWindowsInstall, requiresAdmin: true, isSafe: false),
            CreateProfile("Téléchargements anciens", "Fichiers téléchargés il y a plus de 30 jours", Path.Combine(userProfile, "Downloads"), "📥", CleanerCategory.General, minAgeDays: 30)
        ];
    }

    private static CleanerProfile CreateProfile(
        string name, string description, string folderPath, string icon, CleanerCategory category,
        string searchPattern = "*.*", int minAgeDays = 0, bool includeSubdirectories = true,
        bool requiresAdmin = false, bool isPrivacy = false, bool isSafe = true)
    {
        return new CleanerProfile
        {
            Name = name,
            Description = description,
            FolderPath = folderPath,
            Icon = icon,
            Category = category,
            SearchPattern = searchPattern,
            MinAgeDays = minAgeDays,
            IncludeSubdirectories = includeSubdirectories,
            RequiresAdmin = requiresAdmin,
            IsPrivacy = isPrivacy,
            IsSafe = isSafe,
            IsEnabled = !isPrivacy && isSafe && !requiresAdmin
        };
    }

    private static CleanerProfile CreateProfileWithWarning(
        string name, string description, string folderPath, string icon, CleanerCategory category,
        bool isPrivacy, string warning)
    {
        return new CleanerProfile
        {
            Name = name,
            Description = description,
            FolderPath = folderPath,
            Icon = icon,
            Category = category,
            IsPrivacy = isPrivacy,
            DetailedWarning = warning,
            IsEnabled = false
        };
    }
}
