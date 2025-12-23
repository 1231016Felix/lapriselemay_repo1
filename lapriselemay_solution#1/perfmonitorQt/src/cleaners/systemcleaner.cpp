#include "systemcleaner.h"

#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QDirIterator>
#include <QStandardPaths>
#include <QApplication>
#include <QClipboard>
#include <QThread>
#include <QProcess>
#include <QElapsedTimer>
#include <QRegularExpression>

#ifdef _WIN32
#include <Windows.h>
#include <ShlObj.h>
#include <shellapi.h>
#pragma comment(lib, "shell32.lib")
#endif

SystemCleaner::SystemCleaner(QObject *parent)
    : QObject(parent)
{
}

SystemCleaner::~SystemCleaner()
{
    m_cancelRequested = true;
}

void SystemCleaner::initialize()
{
    m_items.clear();
    initializeWindowsItems();
    initializeBrowserItems();
    initializeApplicationItems();
    initializePrivacyItems();
}

QString SystemCleaner::expandPath(const QString& path)
{
    QString result = path;
    
    // Expand environment variables
    result.replace("%TEMP%", QDir::tempPath());
    result.replace("%USERPROFILE%", QDir::homePath());
    result.replace("%LOCALAPPDATA%", QStandardPaths::writableLocation(QStandardPaths::GenericDataLocation));
    result.replace("%APPDATA%", QStandardPaths::writableLocation(QStandardPaths::AppDataLocation).replace("/" + QCoreApplication::applicationName(), ""));
    
#ifdef _WIN32
    // Windows-specific paths
    wchar_t winPath[MAX_PATH];
    
    if (result.contains("%WINDIR%")) {
        GetWindowsDirectoryW(winPath, MAX_PATH);
        result.replace("%WINDIR%", QString::fromWCharArray(winPath));
    }
    
    if (result.contains("%SYSTEMROOT%")) {
        GetSystemDirectoryW(winPath, MAX_PATH);
        result.replace("%SYSTEMROOT%", QString::fromWCharArray(winPath));
    }
    
    if (result.contains("%PROGRAMDATA%")) {
        if (SUCCEEDED(SHGetFolderPathW(NULL, CSIDL_COMMON_APPDATA, NULL, 0, winPath))) {
            result.replace("%PROGRAMDATA%", QString::fromWCharArray(winPath));
        }
    }
#endif
    
    return QDir::toNativeSeparators(result);
}

QString SystemCleaner::formatSize(qint64 bytes)
{
    if (bytes < 1024) {
        return QString("%1 B").arg(bytes);
    } else if (bytes < 1024 * 1024) {
        return QString("%1 KB").arg(bytes / 1024.0, 0, 'f', 1);
    } else if (bytes < 1024 * 1024 * 1024) {
        return QString("%1 MB").arg(bytes / (1024.0 * 1024.0), 0, 'f', 2);
    } else {
        return QString("%1 GB").arg(bytes / (1024.0 * 1024.0 * 1024.0), 0, 'f', 2);
    }
}

bool SystemCleaner::isAdmin()
{
#ifdef _WIN32
    BOOL isAdmin = FALSE;
    PSID adminGroup = NULL;
    SID_IDENTIFIER_AUTHORITY ntAuthority = SECURITY_NT_AUTHORITY;
    
    if (AllocateAndInitializeSid(&ntAuthority, 2, SECURITY_BUILTIN_DOMAIN_RID,
                                  DOMAIN_ALIAS_RID_ADMINS, 0, 0, 0, 0, 0, 0, &adminGroup)) {
        CheckTokenMembership(NULL, adminGroup, &isAdmin);
        FreeSid(adminGroup);
    }
    return isAdmin;
#else
    return false;
#endif
}


void SystemCleaner::initializeWindowsItems()
{
    // Windows Temporary Files
    {
        CleanerItem item;
        item.category = CleanerCategory::WindowsTemp;
        item.name = tr("Windows Temp Files");
        item.description = tr("Temporary files in Windows temp folder");
        item.isSafe = true;
        item.locations.push_back({"%WINDIR%\\Temp", "*", true, true, 0, 0});
        m_items.push_back(item);
    }
    
    // User Temporary Files
    {
        CleanerItem item;
        item.category = CleanerCategory::UserTemp;
        item.name = tr("User Temp Files");
        item.description = tr("Temporary files in your user temp folder");
        item.isSafe = true;
        item.locations.push_back({"%TEMP%", "*", true, true, 0, 0});
        m_items.push_back(item);
    }
    
    // Thumbnail Cache
    {
        CleanerItem item;
        item.category = CleanerCategory::Thumbnails;
        item.name = tr("Thumbnail Cache");
        item.description = tr("Windows Explorer thumbnail cache files");
        item.isSafe = true;
        item.locations.push_back({"%LOCALAPPDATA%\\Microsoft\\Windows\\Explorer", "thumbcache_*.db", false, false, 0, 0});
        m_items.push_back(item);
    }
    
    // Windows Prefetch
    {
        CleanerItem item;
        item.category = CleanerCategory::Prefetch;
        item.name = tr("Prefetch Files");
        item.description = tr("Windows prefetch cache (may slightly slow first app launches)");
        item.isSafe = true;
        item.requiresAdmin = true;
        item.locations.push_back({"%WINDIR%\\Prefetch", "*.pf", false, false, 7, 0}); // Files older than 7 days
        m_items.push_back(item);
    }
    
    // Recycle Bin
    {
        CleanerItem item;
        item.category = CleanerCategory::RecycleBin;
        item.name = tr("Recycle Bin");
        item.description = tr("Empty the Windows Recycle Bin");
        item.isSafe = true;
        // Special handling - uses SHEmptyRecycleBin
        m_items.push_back(item);
    }
    
    // Windows Logs
    {
        CleanerItem item;
        item.category = CleanerCategory::WindowsLogs;
        item.name = tr("Windows Log Files");
        item.description = tr("Windows system and application log files");
        item.isSafe = true;
        item.locations.push_back({"%WINDIR%\\Logs", "*.log", true, true, 7, 0});
        item.locations.push_back({"%WINDIR%\\Panther", "*.log", true, false, 30, 0});
        item.locations.push_back({"%LOCALAPPDATA%\\Temp", "*.log", true, false, 7, 0});
        m_items.push_back(item);
    }
    
    // Windows Update Cache
    {
        CleanerItem item;
        item.category = CleanerCategory::WindowsUpdate;
        item.name = tr("Windows Update Cache");
        item.description = tr("Downloaded Windows Update files (can be re-downloaded if needed)");
        item.isSafe = true;
        item.requiresAdmin = true;
        item.locations.push_back({"%WINDIR%\\SoftwareDistribution\\Download", "*", true, true, 0, 0});
        m_items.push_back(item);
    }
    
    // Memory Dumps
    {
        CleanerItem item;
        item.category = CleanerCategory::MemoryDumps;
        item.name = tr("Memory Dump Files");
        item.description = tr("Crash dump files (*.dmp)");
        item.isSafe = true;
        item.locations.push_back({"%WINDIR%", "*.dmp", false, false, 0, 0});
        item.locations.push_back({"%WINDIR%\\Minidump", "*.dmp", false, false, 0, 0});
        item.locations.push_back({"%LOCALAPPDATA%\\CrashDumps", "*.dmp", true, true, 0, 0});
        m_items.push_back(item);
    }
    
    // Icon Cache
    {
        CleanerItem item;
        item.category = CleanerCategory::IconCache;
        item.name = tr("Icon Cache");
        item.description = tr("Windows icon cache (will be rebuilt automatically)");
        item.isSafe = true;
        item.locations.push_back({"%LOCALAPPDATA%", "IconCache.db", false, false, 0, 0});
        item.locations.push_back({"%LOCALAPPDATA%\\Microsoft\\Windows\\Explorer", "iconcache_*.db", false, false, 0, 0});
        m_items.push_back(item);
    }
    
    // Font Cache
    {
        CleanerItem item;
        item.category = CleanerCategory::FontCache;
        item.name = tr("Font Cache");
        item.description = tr("Windows font cache files");
        item.isSafe = true;
        item.requiresAdmin = true;
        item.locations.push_back({"%WINDIR%\\ServiceProfiles\\LocalService\\AppData\\Local", "FontCache*.dat", false, false, 0, 0});
        m_items.push_back(item);
    }
    
    // Error Reports
    {
        CleanerItem item;
        item.category = CleanerCategory::ErrorReports;
        item.name = tr("Error Reports");
        item.description = tr("Windows Error Reporting files");
        item.isSafe = true;
        item.locations.push_back({"%LOCALAPPDATA%\\Microsoft\\Windows\\WER", "*", true, true, 0, 0});
        item.locations.push_back({"%PROGRAMDATA%\\Microsoft\\Windows\\WER", "*", true, true, 0, 0});
        m_items.push_back(item);
    }
    
    // Delivery Optimization
    {
        CleanerItem item;
        item.category = CleanerCategory::DeliveryOptimization;
        item.name = tr("Delivery Optimization Cache");
        item.description = tr("Windows Update delivery optimization cache");
        item.isSafe = true;
        item.requiresAdmin = true;
        item.locations.push_back({"%WINDIR%\\ServiceProfiles\\NetworkService\\AppData\\Local\\Microsoft\\Windows\\DeliveryOptimization\\Cache", "*", true, true, 0, 0});
        m_items.push_back(item);
    }
    
    // Old Windows Installation
    {
        CleanerItem item;
        item.category = CleanerCategory::OldWindowsInstall;
        item.name = tr("Previous Windows Installation");
        item.description = tr("Windows.old folder from previous Windows versions (LARGE!)");
        item.isSafe = false; // User should be careful
        item.requiresAdmin = true;
        item.locations.push_back({"C:\\Windows.old", "*", true, true, 0, 0});
        item.locations.push_back({"C:\\$Windows.~BT", "*", true, true, 0, 0});
        item.locations.push_back({"C:\\$Windows.~WS", "*", true, true, 0, 0});
        m_items.push_back(item);
    }
}


void SystemCleaner::initializeBrowserItems()
{
    // Google Chrome Cache
    {
        CleanerItem item;
        item.category = CleanerCategory::ChromeCache;
        item.name = tr("Google Chrome - Cache");
        item.description = tr("Cached web pages, images, and scripts");
        item.isSafe = true;
        item.locations.push_back({"%LOCALAPPDATA%\\Google\\Chrome\\User Data\\Default\\Cache", "*", true, true, 0, 0});
        item.locations.push_back({"%LOCALAPPDATA%\\Google\\Chrome\\User Data\\Default\\Code Cache", "*", true, true, 0, 0});
        item.locations.push_back({"%LOCALAPPDATA%\\Google\\Chrome\\User Data\\Default\\GPUCache", "*", true, true, 0, 0});
        item.locations.push_back({"%LOCALAPPDATA%\\Google\\Chrome\\User Data\\ShaderCache", "*", true, true, 0, 0});
        m_items.push_back(item);
    }
    
    // Chrome History (Privacy)
    {
        CleanerItem item;
        item.category = CleanerCategory::ChromeHistory;
        item.name = tr("Google Chrome - History");
        item.description = tr("Browsing history and download history");
        item.isSafe = true;
        item.isPrivacy = true;
        item.locations.push_back({"%LOCALAPPDATA%\\Google\\Chrome\\User Data\\Default", "History", false, false, 0, 0});
        item.locations.push_back({"%LOCALAPPDATA%\\Google\\Chrome\\User Data\\Default", "History-journal", false, false, 0, 0});
        item.locations.push_back({"%LOCALAPPDATA%\\Google\\Chrome\\User Data\\Default", "Visited Links", false, false, 0, 0});
        m_items.push_back(item);
    }
    
    // Chrome Cookies (Privacy)
    {
        CleanerItem item;
        item.category = CleanerCategory::ChromeCookies;
        item.name = tr("Google Chrome - Cookies");
        item.description = tr("Website cookies (will log you out of websites)");
        item.isSafe = true;
        item.isPrivacy = true;
        item.locations.push_back({"%LOCALAPPDATA%\\Google\\Chrome\\User Data\\Default\\Network", "Cookies", false, false, 0, 0});
        item.locations.push_back({"%LOCALAPPDATA%\\Google\\Chrome\\User Data\\Default\\Network", "Cookies-journal", false, false, 0, 0});
        m_items.push_back(item);
    }
    
    // Microsoft Edge Cache
    {
        CleanerItem item;
        item.category = CleanerCategory::EdgeCache;
        item.name = tr("Microsoft Edge - Cache");
        item.description = tr("Cached web content from Edge browser");
        item.isSafe = true;
        item.locations.push_back({"%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\Default\\Cache", "*", true, true, 0, 0});
        item.locations.push_back({"%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\Default\\Code Cache", "*", true, true, 0, 0});
        item.locations.push_back({"%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\Default\\GPUCache", "*", true, true, 0, 0});
        item.locations.push_back({"%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\ShaderCache", "*", true, true, 0, 0});
        m_items.push_back(item);
    }
    
    // Edge History (Privacy)
    {
        CleanerItem item;
        item.category = CleanerCategory::EdgeHistory;
        item.name = tr("Microsoft Edge - History");
        item.description = tr("Edge browsing history");
        item.isSafe = true;
        item.isPrivacy = true;
        item.locations.push_back({"%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\Default", "History", false, false, 0, 0});
        item.locations.push_back({"%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\Default", "History-journal", false, false, 0, 0});
        m_items.push_back(item);
    }
    
    // Edge Cookies (Privacy)
    {
        CleanerItem item;
        item.category = CleanerCategory::EdgeCookies;
        item.name = tr("Microsoft Edge - Cookies");
        item.description = tr("Edge cookies (will log you out of websites)");
        item.isSafe = true;
        item.isPrivacy = true;
        item.locations.push_back({"%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\Default\\Network", "Cookies", false, false, 0, 0});
        item.locations.push_back({"%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\Default\\Network", "Cookies-journal", false, false, 0, 0});
        m_items.push_back(item);
    }
    
    // Mozilla Firefox Cache
    {
        CleanerItem item;
        item.category = CleanerCategory::FirefoxCache;
        item.name = tr("Mozilla Firefox - Cache");
        item.description = tr("Firefox browser cache");
        item.isSafe = true;
        item.locations.push_back({"%LOCALAPPDATA%\\Mozilla\\Firefox\\Profiles", "cache2", true, true, 0, 0});
        item.locations.push_back({"%LOCALAPPDATA%\\Mozilla\\Firefox\\Profiles", "OfflineCache", true, true, 0, 0});
        m_items.push_back(item);
    }
    
    // Firefox History (Privacy)
    {
        CleanerItem item;
        item.category = CleanerCategory::FirefoxHistory;
        item.name = tr("Mozilla Firefox - History");
        item.description = tr("Firefox browsing history");
        item.isSafe = true;
        item.isPrivacy = true;
        item.locations.push_back({"%APPDATA%\\Mozilla\\Firefox\\Profiles", "places.sqlite", true, false, 0, 0});
        m_items.push_back(item);
    }
    
    // Firefox Cookies (Privacy)
    {
        CleanerItem item;
        item.category = CleanerCategory::FirefoxCookies;
        item.name = tr("Mozilla Firefox - Cookies");
        item.description = tr("Firefox cookies");
        item.isSafe = true;
        item.isPrivacy = true;
        item.locations.push_back({"%APPDATA%\\Mozilla\\Firefox\\Profiles", "cookies.sqlite", true, false, 0, 0});
        m_items.push_back(item);
    }
    
    // Opera Cache
    {
        CleanerItem item;
        item.category = CleanerCategory::OperaCache;
        item.name = tr("Opera - Cache");
        item.description = tr("Opera browser cache");
        item.isSafe = true;
        item.locations.push_back({"%APPDATA%\\Opera Software\\Opera Stable\\Cache", "*", true, true, 0, 0});
        item.locations.push_back({"%APPDATA%\\Opera Software\\Opera GX Stable\\Cache", "*", true, true, 0, 0});
        m_items.push_back(item);
    }
    
    // Brave Cache
    {
        CleanerItem item;
        item.category = CleanerCategory::BraveCache;
        item.name = tr("Brave - Cache");
        item.description = tr("Brave browser cache");
        item.isSafe = true;
        item.locations.push_back({"%LOCALAPPDATA%\\BraveSoftware\\Brave-Browser\\User Data\\Default\\Cache", "*", true, true, 0, 0});
        item.locations.push_back({"%LOCALAPPDATA%\\BraveSoftware\\Brave-Browser\\User Data\\Default\\Code Cache", "*", true, true, 0, 0});
        m_items.push_back(item);
    }
}


void SystemCleaner::initializeApplicationItems()
{
    // VS Code Cache
    {
        CleanerItem item;
        item.category = CleanerCategory::VSCodeCache;
        item.name = tr("Visual Studio Code - Cache");
        item.description = tr("VS Code cache and backup files");
        item.isSafe = true;
        item.locations.push_back({"%APPDATA%\\Code\\Cache", "*", true, true, 0, 0});
        item.locations.push_back({"%APPDATA%\\Code\\CachedData", "*", true, true, 0, 0});
        item.locations.push_back({"%APPDATA%\\Code\\CachedExtensions", "*", true, true, 0, 0});
        item.locations.push_back({"%APPDATA%\\Code\\CachedExtensionVSIXs", "*", true, true, 0, 0});
        item.locations.push_back({"%APPDATA%\\Code\\logs", "*", true, true, 7, 0});
        m_items.push_back(item);
    }
    
    // NPM Cache
    {
        CleanerItem item;
        item.category = CleanerCategory::NPMCache;
        item.name = tr("NPM Cache");
        item.description = tr("Node.js package manager cache");
        item.isSafe = true;
        item.locations.push_back({"%APPDATA%\\npm-cache", "*", true, true, 0, 0});
        item.locations.push_back({"%LOCALAPPDATA%\\npm-cache", "*", true, true, 0, 0});
        m_items.push_back(item);
    }
    
    // NuGet Cache
    {
        CleanerItem item;
        item.category = CleanerCategory::NuGetCache;
        item.name = tr("NuGet Cache");
        item.description = tr(".NET package manager cache");
        item.isSafe = true;
        item.locations.push_back({"%LOCALAPPDATA%\\NuGet\\v3-cache", "*", true, true, 0, 0});
        item.locations.push_back({"%USERPROFILE%\\.nuget\\packages", "*", true, true, 30, 0}); // Only old packages
        m_items.push_back(item);
    }
    
    // Pip Cache
    {
        CleanerItem item;
        item.category = CleanerCategory::PipCache;
        item.name = tr("Python Pip Cache");
        item.description = tr("Python package installer cache");
        item.isSafe = true;
        item.locations.push_back({"%LOCALAPPDATA%\\pip\\cache", "*", true, true, 0, 0});
        item.locations.push_back({"%APPDATA%\\pip\\cache", "*", true, true, 0, 0});
        m_items.push_back(item);
    }
    
    // Steam Cache
    {
        CleanerItem item;
        item.category = CleanerCategory::SteamCache;
        item.name = tr("Steam - Cache & Logs");
        item.description = tr("Steam client cache and log files (not game files)");
        item.isSafe = true;
        item.locations.push_back({"C:\\Program Files (x86)\\Steam\\logs", "*.txt", true, false, 7, 0});
        item.locations.push_back({"C:\\Program Files (x86)\\Steam\\dumps", "*", true, true, 0, 0});
        item.locations.push_back({"%LOCALAPPDATA%\\Steam\\htmlcache", "*", true, true, 0, 0});
        m_items.push_back(item);
    }
    
    // Epic Games Cache
    {
        CleanerItem item;
        item.category = CleanerCategory::EpicGamesCache;
        item.name = tr("Epic Games - Cache");
        item.description = tr("Epic Games Launcher cache");
        item.isSafe = true;
        item.locations.push_back({"%LOCALAPPDATA%\\EpicGamesLauncher\\Saved\\webcache", "*", true, true, 0, 0});
        item.locations.push_back({"%LOCALAPPDATA%\\EpicGamesLauncher\\Saved\\Logs", "*.log", true, false, 7, 0});
        m_items.push_back(item);
    }
}

void SystemCleaner::initializePrivacyItems()
{
    // Recent Documents
    {
        CleanerItem item;
        item.category = CleanerCategory::RecentDocs;
        item.name = tr("Recent Documents List");
        item.description = tr("Clear the list of recently opened files");
        item.isSafe = true;
        item.isPrivacy = true;
        item.locations.push_back({"%APPDATA%\\Microsoft\\Windows\\Recent", "*", false, false, 0, 0});
        item.locations.push_back({"%APPDATA%\\Microsoft\\Windows\\Recent\\AutomaticDestinations", "*", false, false, 0, 0});
        item.locations.push_back({"%APPDATA%\\Microsoft\\Windows\\Recent\\CustomDestinations", "*", false, false, 0, 0});
        m_items.push_back(item);
    }
    
    // DNS Cache
    {
        CleanerItem item;
        item.category = CleanerCategory::DNSCache;
        item.name = tr("DNS Cache");
        item.description = tr("Flush the DNS resolver cache");
        item.isSafe = true;
        item.isPrivacy = true;
        // Special handling - uses ipconfig /flushdns
        m_items.push_back(item);
    }
    
    // Clipboard
    {
        CleanerItem item;
        item.category = CleanerCategory::Clipboard;
        item.name = tr("Clipboard");
        item.description = tr("Clear clipboard contents");
        item.isSafe = true;
        item.isPrivacy = true;
        // Special handling - uses QClipboard
        m_items.push_back(item);
    }
}


std::vector<CleanerItem*> SystemCleaner::getItemsByType(bool privacy, bool requiresAdmin)
{
    std::vector<CleanerItem*> result;
    for (auto& item : m_items) {
        if (privacy && !item.isPrivacy) continue;
        if (requiresAdmin && !item.requiresAdmin) continue;
        result.push_back(&item);
    }
    return result;
}

void SystemCleaner::setItemEnabled(CleanerCategory category, bool enabled)
{
    for (auto& item : m_items) {
        if (item.category == category) {
            item.isEnabled = enabled;
            break;
        }
    }
}

void SystemCleaner::setAllEnabled(bool enabled)
{
    for (auto& item : m_items) {
        item.isEnabled = enabled;
    }
}

void SystemCleaner::setPrivacyItemsEnabled(bool enabled)
{
    for (auto& item : m_items) {
        if (item.isPrivacy) {
            item.isEnabled = enabled;
        }
    }
}

qint64 SystemCleaner::totalCleanableSize() const
{
    qint64 total = 0;
    for (const auto& item : m_items) {
        if (item.isEnabled) {
            total += item.sizeBytes;
        }
    }
    return total;
}

int SystemCleaner::totalCleanableFiles() const
{
    int total = 0;
    for (const auto& item : m_items) {
        if (item.isEnabled) {
            total += item.fileCount;
        }
    }
    return total;
}

void SystemCleaner::startScan()
{
    if (m_isScanning) return;
    
    m_isScanning = true;
    m_cancelRequested = false;
    
    emit scanStarted();
    
    int total = static_cast<int>(m_items.size());
    int current = 0;
    
    for (auto& item : m_items) {
        if (m_cancelRequested) {
            m_isScanning = false;
            emit scanCancelled();
            return;
        }
        
        item.sizeBytes = 0;
        item.fileCount = 0;
        item.files.clear();
        item.errors.clear();
        item.errorCount = 0;
        
        if (item.isEnabled) {
            emit scanProgress(current, total, item.name);
            scanItem(item);
            emit scanItemCompleted(item.category, item.sizeBytes, item.fileCount);
        }
        
        current++;
    }
    
    m_isScanning = false;
    emit scanCompleted(totalCleanableSize(), totalCleanableFiles());
}

void SystemCleaner::cancelScan()
{
    m_cancelRequested = true;
}

void SystemCleaner::scanItem(CleanerItem& item)
{
    // Handle special cases
    switch (item.category) {
        case CleanerCategory::RecycleBin:
            // Estimate recycle bin size using SHQueryRecycleBin
#ifdef _WIN32
            {
                SHQUERYRBINFO rbInfo = { sizeof(SHQUERYRBINFO) };
                if (SUCCEEDED(SHQueryRecycleBinW(NULL, &rbInfo))) {
                    item.sizeBytes = rbInfo.i64Size;
                    item.fileCount = static_cast<int>(rbInfo.i64NumItems);
                }
            }
#endif
            return;
            
        case CleanerCategory::DNSCache:
        case CleanerCategory::Clipboard:
            // These don't have a measurable size
            item.sizeBytes = 0;
            item.fileCount = 1; // Indicate there's something to clean
            return;
            
        default:
            break;
    }
    
    // Scan locations
    for (const auto& location : item.locations) {
        if (m_cancelRequested) return;
        scanLocation(item, location);
    }
}

void SystemCleaner::scanLocation(CleanerItem& item, const CleanerLocation& location)
{
    QString basePath = expandPath(location.path);
    
    QDir dir(basePath);
    if (!dir.exists()) {
        return;
    }
    
    QStringList files;
    int count = 0;
    qint64 size = calculateDirectorySize(basePath, location, files, count);
    
    item.sizeBytes += size;
    item.fileCount += count;
    item.files.append(files);
}

qint64 SystemCleaner::calculateDirectorySize(const QString& path, const CleanerLocation& location,
                                              QStringList& outFiles, int& outCount)
{
    qint64 totalSize = 0;
    QDir dir(path);
    
    if (!dir.exists() || !dir.isReadable()) {
        return 0;
    }
    
    // Set up filters
    QDir::Filters filters = QDir::Files | QDir::NoDotAndDotDot;
    if (location.recursive) {
        filters |= QDir::AllDirs;
    }
    
    // Set up name filters
    QStringList nameFilters;
    if (location.filePattern != "*") {
        nameFilters << location.filePattern;
    }
    
    QDirIterator::IteratorFlags iteratorFlags = QDirIterator::NoIteratorFlags;
    if (location.recursive) {
        iteratorFlags = QDirIterator::Subdirectories;
    }
    
    QDirIterator it(path, nameFilters, filters, iteratorFlags);
    
    QDateTime now = QDateTime::currentDateTime();
    
    while (it.hasNext() && !m_cancelRequested) {
        QString filePath = it.next();
        QFileInfo info(filePath);
        
        if (info.isFile()) {
            // Check minimum age
            if (location.minAgeDays > 0) {
                qint64 daysDiff = info.lastModified().daysTo(now);
                if (daysDiff < location.minAgeDays) {
                    continue;
                }
            }
            
            // Check minimum size
            if (location.minSizeBytes > 0 && info.size() < location.minSizeBytes) {
                continue;
            }
            
            totalSize += info.size();
            outCount++;
            outFiles.append(filePath);
        }
    }
    
    return totalSize;
}


void SystemCleaner::startCleaning()
{
    if (m_isCleaning) return;
    
    m_isCleaning = true;
    m_cancelRequested = false;
    m_lastResult = CleaningResult();
    
    QElapsedTimer timer;
    timer.start();
    
    emit cleaningStarted();
    
    int totalFiles = totalCleanableFiles();
    int currentFile = 0;
    
    for (auto& item : m_items) {
        if (m_cancelRequested) {
            m_isCleaning = false;
            emit cleaningCancelled();
            return;
        }
        
        if (!item.isEnabled || item.fileCount == 0) {
            continue;
        }
        
        // Handle special cases first
        switch (item.category) {
            case CleanerCategory::RecycleBin:
                if (emptyRecycleBin()) {
                    m_lastResult.bytesFreed += item.sizeBytes;
                    m_lastResult.filesDeleted += item.fileCount;
                    emit cleaningItemCompleted(item.category, item.sizeBytes, item.fileCount);
                }
                continue;
                
            case CleanerCategory::DNSCache:
                clearDNSCache();
                emit cleaningItemCompleted(item.category, 0, 1);
                continue;
                
            case CleanerCategory::Clipboard:
                clearClipboard();
                emit cleaningItemCompleted(item.category, 0, 1);
                continue;
                
            case CleanerCategory::RecentDocs:
                clearRecentDocs();
                // Fall through to also clean the files
                break;
                
            default:
                break;
        }
        
        // Clean files
        CleaningResult itemResult;
        cleanItem(item, itemResult);
        
        m_lastResult.bytesFreed += itemResult.bytesFreed;
        m_lastResult.filesDeleted += itemResult.filesDeleted;
        m_lastResult.directoriesDeleted += itemResult.directoriesDeleted;
        m_lastResult.errors += itemResult.errors;
        m_lastResult.errorMessages.append(itemResult.errorMessages);
        
        emit cleaningItemCompleted(item.category, itemResult.bytesFreed, itemResult.filesDeleted);
        
        currentFile += item.fileCount;
    }
    
    m_lastResult.durationSeconds = timer.elapsed() / 1000.0;
    m_isCleaning = false;
    emit cleaningCompleted(m_lastResult);
}

void SystemCleaner::cancelCleaning()
{
    m_cancelRequested = true;
}

void SystemCleaner::cleanItem(CleanerItem& item, CleaningResult& result)
{
    int total = item.files.size();
    int current = 0;
    
    for (const QString& filePath : item.files) {
        if (m_cancelRequested) return;
        
        emit cleaningProgress(current, total, filePath);
        
        QFileInfo info(filePath);
        qint64 fileSize = info.size();
        
        if (deleteFile(filePath)) {
            result.bytesFreed += fileSize;
            result.filesDeleted++;
        } else {
            result.errors++;
            result.errorMessages.append(QString("Failed to delete: %1").arg(filePath));
        }
        
        current++;
    }
    
    // Clean empty directories for each location
    for (const auto& location : item.locations) {
        if (location.deleteEmptyDirs) {
            QString basePath = expandPath(location.path);
            deleteEmptyDirectories(basePath);
        }
    }
}

bool SystemCleaner::deleteFile(const QString& filePath)
{
    QFile file(filePath);
    
    // Try to remove read-only attribute
    if (!file.permissions().testFlag(QFileDevice::WriteUser)) {
        file.setPermissions(file.permissions() | QFileDevice::WriteUser);
    }
    
    if (!file.remove()) {
        // Try with Windows API for stubborn files
#ifdef _WIN32
        std::wstring wpath = filePath.toStdWString();
        // First try to clear attributes
        SetFileAttributesW(wpath.c_str(), FILE_ATTRIBUTE_NORMAL);
        if (DeleteFileW(wpath.c_str())) {
            return true;
        }
#endif
        return false;
    }
    
    return true;
}

bool SystemCleaner::deleteDirectory(const QString& dirPath)
{
    QDir dir(dirPath);
    if (!dir.exists()) return true;
    
#ifdef _WIN32
    // Use SHFileOperation for directories (handles read-only and system files better)
    std::wstring wpath = dirPath.toStdWString();
    wpath.push_back(L'\0'); // Double null terminated
    
    SHFILEOPSTRUCTW op = {};
    op.wFunc = FO_DELETE;
    op.pFrom = wpath.c_str();
    op.fFlags = FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT;
    
    return SHFileOperationW(&op) == 0;
#else
    return dir.removeRecursively();
#endif
}

void SystemCleaner::deleteEmptyDirectories(const QString& basePath)
{
    QDir dir(basePath);
    if (!dir.exists()) return;
    
    // Get all subdirectories (depth-first)
    QDirIterator it(basePath, QDir::Dirs | QDir::NoDotAndDotDot, 
                    QDirIterator::Subdirectories);
    
    QStringList dirs;
    while (it.hasNext()) {
        dirs.prepend(it.next()); // Prepend to process deepest first
    }
    
    // Delete empty directories from deepest to shallowest
    for (const QString& dirPath : dirs) {
        QDir d(dirPath);
        if (d.isEmpty()) {
            d.rmdir(dirPath);
        }
    }
}

bool SystemCleaner::emptyRecycleBin()
{
#ifdef _WIN32
    HRESULT hr = SHEmptyRecycleBinW(NULL, NULL, 
        SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
    return SUCCEEDED(hr) || hr == S_FALSE; // S_FALSE means already empty
#else
    return false;
#endif
}

bool SystemCleaner::clearDNSCache()
{
#ifdef _WIN32
    QProcess process;
    process.start("ipconfig", QStringList() << "/flushdns");
    process.waitForFinished(5000);
    return process.exitCode() == 0;
#else
    return false;
#endif
}

bool SystemCleaner::clearClipboard()
{
    QClipboard* clipboard = QApplication::clipboard();
    if (clipboard) {
        clipboard->clear();
        return true;
    }
    return false;
}

bool SystemCleaner::clearRecentDocs()
{
#ifdef _WIN32
    // Clear recent documents using Shell API
    SHAddToRecentDocs(SHARD_PIDL, NULL);
    return true;
#else
    return false;
#endif
}
