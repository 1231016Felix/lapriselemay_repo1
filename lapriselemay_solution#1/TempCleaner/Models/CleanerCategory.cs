namespace TempCleaner.Models;

/// <summary>
/// Catégories de nettoyage
/// </summary>
public enum CleanerCategory
{
    General,
    
    // Fichiers temporaires
    WindowsTemp,
    UserTemp,
    
    // Navigateurs
    BrowserCache,
    BrowserHistory,
    BrowserCookies,
    
    // Caches Windows
    WindowsCache,
    Thumbnails,
    Prefetch,
    
    // Mises à jour et maintenance
    WindowsUpdate,
    DeliveryOptimization,
    WindowsStore,
    
    // Logs et rapports
    WindowsLogs,
    ErrorReports,
    MemoryDumps,
    
    // Applications
    ApplicationCache,
    GamingCache,
    CommunicationApps,
    MediaApps,
    AdobeApps,
    CloudSync,
    
    // Confidentialité
    RecentDocs,
    DNSCache,
    Clipboard,
    RecycleBin,
    
    // Système avancé
    OldWindowsInstall,
    SystemAdvanced,
    
    Custom
}
