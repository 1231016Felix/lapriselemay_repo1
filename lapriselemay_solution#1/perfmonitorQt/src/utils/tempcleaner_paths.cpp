
// === Path Resolution ===

QString TempCleaner::expandEnvironmentPath(const QString& path)
{
    QString result = path;
    
#ifdef _WIN32
    // Check cache first
    if (m_envCache.contains(path)) {
        return m_envCache[path];
    }
    
    wchar_t expanded[MAX_PATH];
    if (ExpandEnvironmentStringsW(path.toStdWString().c_str(), expanded, MAX_PATH)) {
        result = QString::fromWCharArray(expanded);
    }
    
    m_envCache[path] = result;
#endif
    
    return result;
}

QStringList TempCleaner::resolvePaths(CleanCategory category)
{
    QStringList paths;
    QString userProfile = expandEnvironmentPath("%USERPROFILE%");
    QString appData = expandEnvironmentPath("%APPDATA%");
    QString localAppData = expandEnvironmentPath("%LOCALAPPDATA%");
    QString temp = expandEnvironmentPath("%TEMP%");
    QString winDir = expandEnvironmentPath("%WINDIR%");
    QString programData = expandEnvironmentPath("%PROGRAMDATA%");
    
    switch (category) {
    case CleanCategory::WindowsTemp:
        paths << temp;
        break;
        
    case CleanCategory::WindowsSystemTemp:
        paths << winDir + "\\Temp";
        break;
        
    case CleanCategory::WindowsPrefetch:
        paths << winDir + "\\Prefetch";
        break;
        
    case CleanCategory::WindowsUpdate:
        paths << winDir + "\\SoftwareDistribution\\Download";
        break;
        
    case CleanCategory::WindowsInstaller:
        paths << winDir + "\\Installer\\$PatchCache$";
        break;
        
    case CleanCategory::WindowsLogs:
        paths << winDir + "\\Logs"
              << winDir + "\\Panther"
              << programData + "\\Microsoft\\Windows\\WER\\ReportArchive"
              << localAppData + "\\CrashDumps";
        break;
        
    case CleanCategory::WindowsErrorReports:
        paths << programData + "\\Microsoft\\Windows\\WER"
              << localAppData + "\\Microsoft\\Windows\\WER";
        break;
        
    case CleanCategory::WindowsDeliveryOptim:
        paths << winDir + "\\ServiceProfiles\\NetworkService\\AppData\\Local\\Microsoft\\Windows\\DeliveryOptimization\\Cache";
        break;
        
    case CleanCategory::WindowsThumbnails:
        paths << localAppData + "\\Microsoft\\Windows\\Explorer";
        break;
        
    case CleanCategory::WindowsIconCache:
        paths << localAppData + "\\IconCache.db"
              << localAppData + "\\Microsoft\\Windows\\Explorer\\iconcache*";
        break;
        
    case CleanCategory::WindowsFontCache:
        paths << winDir + "\\ServiceProfiles\\LocalService\\AppData\\Local\\FontCache";
        break;
        
    // Chrome
    case CleanCategory::ChromeCache:
        paths << localAppData + "\\Google\\Chrome\\User Data\\Default\\Cache"
              << localAppData + "\\Google\\Chrome\\User Data\\Default\\Code Cache"
              << localAppData + "\\Google\\Chrome\\User Data\\Default\\GPUCache"
              << localAppData + "\\Google\\Chrome\\User Data\\ShaderCache";
        break;
        
    case CleanCategory::ChromeCookies:
        paths << localAppData + "\\Google\\Chrome\\User Data\\Default\\Cookies"
              << localAppData + "\\Google\\Chrome\\User Data\\Default\\Cookies-journal";
        break;
        
    case CleanCategory::ChromeHistory:
        paths << localAppData + "\\Google\\Chrome\\User Data\\Default\\History"
              << localAppData + "\\Google\\Chrome\\User Data\\Default\\History-journal"
              << localAppData + "\\Google\\Chrome\\User Data\\Default\\Visited Links";
        break;
        
    case CleanCategory::ChromeSession:
        paths << localAppData + "\\Google\\Chrome\\User Data\\Default\\Sessions"
              << localAppData + "\\Google\\Chrome\\User Data\\Default\\Session Storage"
              << localAppData + "\\Google\\Chrome\\User Data\\Default\\Current Session"
              << localAppData + "\\Google\\Chrome\\User Data\\Default\\Current Tabs";
        break;
        
    // Firefox
    case CleanCategory::FirefoxCache:
        paths << localAppData + "\\Mozilla\\Firefox\\Profiles";
        break;
        
    case CleanCategory::FirefoxCookies:
        paths << appData + "\\Mozilla\\Firefox\\Profiles";
        break;
        
    case CleanCategory::FirefoxHistory:
        paths << appData + "\\Mozilla\\Firefox\\Profiles";
        break;
        
    // Edge
    case CleanCategory::EdgeCache:
        paths << localAppData + "\\Microsoft\\Edge\\User Data\\Default\\Cache"
              << localAppData + "\\Microsoft\\Edge\\User Data\\Default\\Code Cache"
              << localAppData + "\\Microsoft\\Edge\\User Data\\Default\\GPUCache"
              << localAppData + "\\Microsoft\\Edge\\User Data\\ShaderCache";
        break;
        
    case CleanCategory::EdgeCookies:
        paths << localAppData + "\\Microsoft\\Edge\\User Data\\Default\\Cookies";
        break;
        
    case CleanCategory::EdgeHistory:
        paths << localAppData + "\\Microsoft\\Edge\\User Data\\Default\\History";
        break;
        
    // Applications
    case CleanCategory::SpotifyCache:
        paths << localAppData + "\\Spotify\\Storage"
              << localAppData + "\\Spotify\\Data";
        break;
        
    case CleanCategory::DiscordCache:
        paths << appData + "\\discord\\Cache"
              << appData + "\\discord\\Code Cache"
              << appData + "\\discord\\GPUCache";
        break;
        
    case CleanCategory::TeamsCache:
        paths << appData + "\\Microsoft\\Teams\\Cache"
              << appData + "\\Microsoft\\Teams\\blob_storage"
              << appData + "\\Microsoft\\Teams\\databases"
              << appData + "\\Microsoft\\Teams\\GPUCache"
              << appData + "\\Microsoft\\Teams\\IndexedDB"
              << appData + "\\Microsoft\\Teams\\Local Storage"
              << appData + "\\Microsoft\\Teams\\tmp";
        break;
        
    case CleanCategory::SlackCache:
        paths << appData + "\\Slack\\Cache"
              << appData + "\\Slack\\Code Cache"
              << appData + "\\Slack\\GPUCache";
        break;
        
    case CleanCategory::SteamCache:
        paths << "C:\\Program Files (x86)\\Steam\\appcache"
              << "C:\\Program Files (x86)\\Steam\\depotcache";
        break;
        
    case CleanCategory::VSCodeCache:
        paths << appData + "\\Code\\Cache"
              << appData + "\\Code\\CachedData"
              << appData + "\\Code\\CachedExtensions"
              << appData + "\\Code\\CachedExtensionVSIXs"
              << appData + "\\Code\\Code Cache"
              << appData + "\\Code\\GPUCache";
        break;
        
    case CleanCategory::NpmCache:
        paths << appData + "\\npm-cache"
              << localAppData + "\\npm-cache";
        break;
        
    case CleanCategory::PipCache:
        paths << localAppData + "\\pip\\Cache";
        break;
        
    case CleanCategory::NuGetCache:
        paths << userProfile + "\\.nuget\\packages";
        break;
        
    case CleanCategory::GradleCache:
        paths << userProfile + "\\.gradle\\caches";
        break;
        
    case CleanCategory::RecentDocuments:
        paths << appData + "\\Microsoft\\Windows\\Recent";
        break;
        
    default:
        break;
    }
    
    return paths;
}
