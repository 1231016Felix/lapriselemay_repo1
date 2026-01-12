#include "tempcleaner.h"
#include "systeminfo.h"

#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QDirIterator>
#include <QStandardPaths>
#include <QProcess>
#include <QSettings>
#include <QClipboard>
#include <QApplication>
#include <QRandomGenerator>
#include <QThread>
#include <QRegularExpression>

#ifdef _WIN32
#include <Windows.h>
#include <ShlObj.h>
#include <shellapi.h>
#include <shlwapi.h>
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "shlwapi.lib")
#endif

TempCleaner::TempCleaner(QObject *parent)
    : QObject(parent)
{
    initializeCategories();
}

TempCleaner::~TempCleaner()
{
    stop();
}

void TempCleaner::initializeCategories()
{
    m_categories.clear();
    
    // === Windows System ===
    m_categories.push_back({
        CleanCategory::WindowsTemp,
        tr("Windows Temp Files"),
        tr("Temporary files in %TEMP% folder"),
        "🗑️", CleanRiskLevel::Safe, false, true, false, 0, 0, {}, "Windows"
    });
    
    m_categories.push_back({
        CleanCategory::WindowsSystemTemp,
        tr("System Temp Files"),
        tr("Temporary files in C:\\Windows\\Temp"),
        "🗑️", CleanRiskLevel::Safe, true, true, false, 0, 0, {}, "Windows"
    });
    
    m_categories.push_back({
        CleanCategory::WindowsPrefetch,
        tr("Prefetch Files"),
        tr("Application prefetch data (may slow first launch)"),
        "⚡", CleanRiskLevel::Low, true, false, false, 0, 0, {}, "Windows"
    });
    
    m_categories.push_back({
        CleanCategory::WindowsUpdate,
        tr("Windows Update Cache"),
        tr("Downloaded Windows Update files"),
        "🔄", CleanRiskLevel::Low, true, true, false, 0, 0, {}, "Windows"
    });
    
    m_categories.push_back({
        CleanCategory::WindowsInstaller,
        tr("Windows Installer Cache"),
        tr("Windows Installer patch cache files"),
        "📦", CleanRiskLevel::Medium, true, false, false, 0, 0, {}, "Windows"
    });
    
    m_categories.push_back({
        CleanCategory::WindowsLogs,
        tr("Windows Log Files"),
        tr("System and application log files"),
        "📋", CleanRiskLevel::Safe, true, true, false, 0, 0, {}, "Windows"
    });
    
    m_categories.push_back({
        CleanCategory::WindowsErrorReports,
        tr("Error Reports"),
        tr("Windows Error Reporting data"),
        "⚠️", CleanRiskLevel::Safe, true, true, false, 0, 0, {}, "Windows"
    });
    
    m_categories.push_back({
        CleanCategory::WindowsDeliveryOptim,
        tr("Delivery Optimization"),
        tr("Windows Update delivery optimization cache"),
        "📡", CleanRiskLevel::Safe, true, true, false, 0, 0, {}, "Windows"
    });
    
    m_categories.push_back({
        CleanCategory::WindowsThumbnails,
        tr("Thumbnail Cache"),
        tr("Explorer thumbnail cache files"),
        "🖼️", CleanRiskLevel::Safe, false, true, false, 0, 0, {}, "Windows"
    });
    
    m_categories.push_back({
        CleanCategory::WindowsIconCache,
        tr("Icon Cache"),
        tr("Windows icon cache files"),
        "🎨", CleanRiskLevel::Safe, false, false, false, 0, 0, {}, "Windows"
    });
    
    m_categories.push_back({
        CleanCategory::WindowsFontCache,
        tr("Font Cache"),
        tr("Windows font cache files"),
        "🔤", CleanRiskLevel::Low, true, false, false, 0, 0, {}, "Windows"
    });
    
    m_categories.push_back({
        CleanCategory::RecycleBin,
        tr("Recycle Bin"),
        tr("Empty the Recycle Bin"),
        "🗑️", CleanRiskLevel::Medium, false, true, false, 0, 0, {}, "Windows"
    });
    
    // === Chrome ===
    m_categories.push_back({
        CleanCategory::ChromeCache,
        tr("Chrome Cache"),
        tr("Google Chrome browser cache"),
        "🌐", CleanRiskLevel::Safe, false, true, false, 0, 0, {}, "Google Chrome"
    });
    
    m_categories.push_back({
        CleanCategory::ChromeCookies,
        tr("Chrome Cookies"),
        tr("Chrome cookies (will log out of websites)"),
        "🍪", CleanRiskLevel::Medium, false, false, false, 0, 0, {}, "Google Chrome"
    });
    
    m_categories.push_back({
        CleanCategory::ChromeHistory,
        tr("Chrome History"),
        tr("Browsing history"),
        "📜", CleanRiskLevel::Low, false, false, false, 0, 0, {}, "Google Chrome"
    });
    
    m_categories.push_back({
        CleanCategory::ChromeDownloads,
        tr("Chrome Download History"),
        tr("Download history (not the files themselves)"),
        "📥", CleanRiskLevel::Low, false, false, false, 0, 0, {}, "Google Chrome"
    });
    
    m_categories.push_back({
        CleanCategory::ChromeSession,
        tr("Chrome Session Data"),
        tr("Session and tab data"),
        "📑", CleanRiskLevel::Medium, false, false, false, 0, 0, {}, "Google Chrome"
    });
    
    // === Firefox ===
    m_categories.push_back({
        CleanCategory::FirefoxCache,
        tr("Firefox Cache"),
        tr("Mozilla Firefox browser cache"),
        "🦊", CleanRiskLevel::Safe, false, true, false, 0, 0, {}, "Mozilla Firefox"
    });
    
    m_categories.push_back({
        CleanCategory::FirefoxCookies,
        tr("Firefox Cookies"),
        tr("Firefox cookies (will log out of websites)"),
        "🍪", CleanRiskLevel::Medium, false, false, false, 0, 0, {}, "Mozilla Firefox"
    });
    
    m_categories.push_back({
        CleanCategory::FirefoxHistory,
        tr("Firefox History"),
        tr("Browsing history"),
        "📜", CleanRiskLevel::Low, false, false, false, 0, 0, {}, "Mozilla Firefox"
    });
    
    m_categories.push_back({
        CleanCategory::FirefoxSession,
        tr("Firefox Session"),
        tr("Session and tab data"),
        "📑", CleanRiskLevel::Medium, false, false, false, 0, 0, {}, "Mozilla Firefox"
    });
    
    // === Edge ===
    m_categories.push_back({
        CleanCategory::EdgeCache,
        tr("Edge Cache"),
        tr("Microsoft Edge browser cache"),
        "🌊", CleanRiskLevel::Safe, false, true, false, 0, 0, {}, "Microsoft Edge"
    });
    
    m_categories.push_back({
        CleanCategory::EdgeCookies,
        tr("Edge Cookies"),
        tr("Edge cookies (will log out of websites)"),
        "🍪", CleanRiskLevel::Medium, false, false, false, 0, 0, {}, "Microsoft Edge"
    });
    
    m_categories.push_back({
        CleanCategory::EdgeHistory,
        tr("Edge History"),
        tr("Browsing history"),
        "📜", CleanRiskLevel::Low, false, false, false, 0, 0, {}, "Microsoft Edge"
    });
    
    // === Applications ===
    m_categories.push_back({
        CleanCategory::SpotifyCache,
        tr("Spotify Cache"),
        tr("Spotify streaming cache"),
        "🎵", CleanRiskLevel::Safe, false, true, false, 0, 0, {}, "Applications"
    });
    
    m_categories.push_back({
        CleanCategory::DiscordCache,
        tr("Discord Cache"),
        tr("Discord cache files"),
        "💬", CleanRiskLevel::Safe, false, true, false, 0, 0, {}, "Applications"
    });
    
    m_categories.push_back({
        CleanCategory::TeamsCache,
        tr("Teams Cache"),
        tr("Microsoft Teams cache"),
        "👥", CleanRiskLevel::Safe, false, true, false, 0, 0, {}, "Applications"
    });
    
    m_categories.push_back({
        CleanCategory::SlackCache,
        tr("Slack Cache"),
        tr("Slack cache files"),
        "💼", CleanRiskLevel::Safe, false, true, false, 0, 0, {}, "Applications"
    });
    
    m_categories.push_back({
        CleanCategory::SteamCache,
        tr("Steam Cache"),
        tr("Steam download cache"),
        "🎮", CleanRiskLevel::Safe, false, false, false, 0, 0, {}, "Applications"
    });
    
    m_categories.push_back({
        CleanCategory::VSCodeCache,
        tr("VS Code Cache"),
        tr("Visual Studio Code cache"),
        "💻", CleanRiskLevel::Safe, false, true, false, 0, 0, {}, "Development"
    });
    
    m_categories.push_back({
        CleanCategory::NpmCache,
        tr("npm Cache"),
        tr("Node.js npm package cache"),
        "📦", CleanRiskLevel::Safe, false, true, false, 0, 0, {}, "Development"
    });
    
    m_categories.push_back({
        CleanCategory::PipCache,
        tr("pip Cache"),
        tr("Python pip package cache"),
        "🐍", CleanRiskLevel::Safe, false, true, false, 0, 0, {}, "Development"
    });
    
    m_categories.push_back({
        CleanCategory::NuGetCache,
        tr("NuGet Cache"),
        tr(".NET NuGet package cache"),
        "📦", CleanRiskLevel::Safe, false, true, false, 0, 0, {}, "Development"
    });
    
    m_categories.push_back({
        CleanCategory::GradleCache,
        tr("Gradle Cache"),
        tr("Gradle build cache"),
        "🐘", CleanRiskLevel::Safe, false, false, false, 0, 0, {}, "Development"
    });
    
    // === System ===
    m_categories.push_back({
        CleanCategory::RecentDocuments,
        tr("Recent Documents"),
        tr("Recent documents list"),
        "📄", CleanRiskLevel::Low, false, false, false, 0, 0, {}, "System"
    });
    
    m_categories.push_back({
        CleanCategory::DNSCache,
        tr("DNS Cache"),
        tr("DNS resolver cache"),
        "🌐", CleanRiskLevel::Safe, true, false, false, 0, 0, {}, "System"
    });
}

QStringList TempCleaner::getBrowserProfiles(const QString& browserPath)
{
    QStringList profiles;
    QDir dir(browserPath);
    
    if (!dir.exists()) return profiles;
    
    // Look for profile directories
    for (const auto& entry : dir.entryList(QDir::Dirs | QDir::NoDotAndDotDot)) {
        if (entry.startsWith("Profile ") || entry == "Default") {
            profiles << dir.absoluteFilePath(entry);
        }
    }
    
    return profiles;
}

// === Analysis Functions ===

void TempCleaner::analyzeAll()
{
    if (m_isRunning) return;
    
    m_isRunning = true;
    m_stopRequested = false;
    
    int total = static_cast<int>(m_categories.size());
    int current = 0;
    
    for (auto& cat : m_categories) {
        if (m_stopRequested) break;
        
        current++;
        emit analysisProgress(current, total, cat.name);
        
        analyzeCategory(cat.category);
    }
    
    m_isRunning = false;
    emit analysisComplete();
}

void TempCleaner::analyzeCategory(CleanCategory category)
{
    auto it = std::find_if(m_categories.begin(), m_categories.end(),
        [category](const CleanCategoryInfo& c) { return c.category == category; });
    
    if (it == m_categories.end()) return;
    
    it->estimatedSize = 0;
    it->fileCount = 0;
    it->paths.clear();
    
    QStringList paths = resolvePaths(category);
    
    for (const QString& path : paths) {
        if (m_stopRequested) break;
        
        QFileInfo fi(path);
        
        if (fi.isFile()) {
            if (fi.exists() && !matchesExclusion(path)) {
                it->estimatedSize += fi.size();
                it->fileCount++;
                it->paths << path;
            }
        } else if (fi.isDir() && fi.exists()) {
            int fileCount = 0;
            qint64 size = analyzeDirectory(path, {"*"}, fileCount, true);
            it->estimatedSize += size;
            it->fileCount += fileCount;
            it->paths << path;
        }
    }
    
    emit categoryAnalyzed(category, it->estimatedSize, it->fileCount);
}

qint64 TempCleaner::analyzeDirectory(const QString& path, const QStringList& patterns,
                                      int& fileCount, bool recursive)
{
    qint64 totalSize = 0;
    
    QDir dir(path);
    if (!dir.exists()) return 0;
    
    QDirIterator::IteratorFlags flags = recursive 
        ? QDirIterator::Subdirectories 
        : QDirIterator::NoIteratorFlags;
    
    QDirIterator it(path, patterns, QDir::Files | QDir::Hidden | QDir::System, flags);
    
    while (it.hasNext() && !m_stopRequested) {
        it.next();
        
        if (matchesExclusion(it.filePath())) continue;
        if (isFileTooNew(it.filePath())) continue;
        
        totalSize += it.fileInfo().size();
        fileCount++;
    }
    
    return totalSize;
}

bool TempCleaner::matchesExclusion(const QString& path)
{
    for (const QString& pattern : m_exclusions) {
        QRegularExpression rx(QRegularExpression::wildcardToRegularExpression(pattern),
                              QRegularExpression::CaseInsensitiveOption);
        if (rx.match(path).hasMatch()) {
            return true;
        }
    }
    return false;
}

bool TempCleaner::isFileTooNew(const QString& path)
{
    if (m_minFileAgeDays <= 0) return false;
    
    QFileInfo fi(path);
    QDateTime minAge = QDateTime::currentDateTime().addDays(-m_minFileAgeDays);
    return fi.lastModified() > minAge;
}

std::vector<CleanFileInfo> TempCleaner::getFilesForCategory(CleanCategory category, int maxFiles)
{
    std::vector<CleanFileInfo> files;
    
    QStringList paths = resolvePaths(category);
    
    for (const QString& path : paths) {
        if (static_cast<int>(files.size()) >= maxFiles) break;
        
        QFileInfo fi(path);
        
        if (fi.isFile()) {
            if (fi.exists()) {
                files.push_back({
                    path,
                    fi.size(),
                    fi.lastModified(),
                    false,
                    category
                });
            }
        } else if (fi.isDir() && fi.exists()) {
            QDirIterator it(path, {"*"}, QDir::Files | QDir::Hidden, QDirIterator::Subdirectories);
            
            while (it.hasNext() && static_cast<int>(files.size()) < maxFiles) {
                it.next();
                files.push_back({
                    it.filePath(),
                    it.fileInfo().size(),
                    it.fileInfo().lastModified(),
                    false,
                    category
                });
            }
        }
    }
    
    return files;
}

// === Cleaning Functions ===

void TempCleaner::cleanSelected()
{
    if (m_isRunning) return;
    
    m_isRunning = true;
    m_stopRequested = false;
    m_lastSummary = CleanSummary{};
    m_lastSummary.startTime = QDateTime::currentDateTime();
    
    std::vector<CleanCategoryInfo*> selectedCats;
    for (auto& cat : m_categories) {
        if (cat.isSelected) {
            selectedCats.push_back(&cat);
        }
    }
    
    int total = static_cast<int>(selectedCats.size());
    int current = 0;
    
    for (auto* cat : selectedCats) {
        if (m_stopRequested) break;
        
        current++;
        emit logMessage(tr("Cleaning %1...").arg(cat->name));
        
        cleanCategory(cat->category);
    }
    
    m_lastSummary.endTime = QDateTime::currentDateTime();
    m_isRunning = false;
    emit cleanComplete(m_lastSummary);
}

void TempCleaner::cleanCategory(CleanCategory category)
{
    CleanResult result;
    result.category = category;
    result.success = true;
    
    // Special cases
    if (category == CleanCategory::RecycleBin) {
        result.success = cleanRecycleBin();
        if (result.success) {
            result.filesDeleted = 1;
            emit logMessage(tr("Recycle Bin emptied successfully"));
        }
        m_lastSummary.results.push_back(result);
        emit categoryCleaned(category, result);
        return;
    }
    
    if (category == CleanCategory::DNSCache) {
        result.success = flushDnsCache();
        emit logMessage(result.success ? tr("DNS cache flushed") : tr("Failed to flush DNS cache"));
        m_lastSummary.results.push_back(result);
        emit categoryCleaned(category, result);
        return;
    }
    
    // Regular file cleaning
    QStringList paths = resolvePaths(category);
    
    for (const QString& path : paths) {
        if (m_stopRequested) break;
        
        QFileInfo fi(path);
        
        if (fi.isFile()) {
            if (fi.exists()) {
                qint64 size = fi.size();
                bool deleted = m_dryRun ? true : deleteFile(path);
                
                if (deleted) {
                    result.filesDeleted++;
                    result.bytesFreed += size;
                    result.deletedFiles << path;
                } else {
                    result.filesFailed++;
                    result.errors << tr("Failed to delete: %1").arg(path);
                }
            }
        } else if (fi.isDir() && fi.exists()) {
            CleanResult dirResult = cleanDirectory(path, {"*"}, true);
            result.filesDeleted += dirResult.filesDeleted;
            result.filesFailed += dirResult.filesFailed;
            result.bytesFreed += dirResult.bytesFreed;
            result.errors << dirResult.errors;
            result.deletedFiles << dirResult.deletedFiles;
        }
    }
    
    if (result.filesFailed > 0) {
        result.success = false;
    }
    
    m_lastSummary.totalBytesFreed += result.bytesFreed;
    m_lastSummary.totalFilesDeleted += result.filesDeleted;
    m_lastSummary.totalFilesFailed += result.filesFailed;
    
    if (result.success) {
        m_lastSummary.categoriesCleaned++;
    } else {
        m_lastSummary.categoriesFailed++;
    }
    
    m_lastSummary.results.push_back(result);
    emit categoryCleaned(category, result);
}

void TempCleaner::cleanAll()
{
    selectAll(true);
    cleanSelected();
}

CleanResult TempCleaner::cleanDirectory(const QString& path, const QStringList& patterns, bool recursive)
{
    CleanResult result;
    result.success = true;
    
    QDir dir(path);
    if (!dir.exists()) return result;
    
    // First, delete files
    QDirIterator::IteratorFlags flags = recursive 
        ? QDirIterator::Subdirectories 
        : QDirIterator::NoIteratorFlags;
    
    QDirIterator it(path, patterns, QDir::Files | QDir::Hidden | QDir::System, flags);
    
    while (it.hasNext() && !m_stopRequested) {
        it.next();
        QString filePath = it.filePath();
        
        if (matchesExclusion(filePath)) continue;
        if (isFileTooNew(filePath)) continue;
        
        qint64 size = it.fileInfo().size();
        bool deleted = m_dryRun ? true : deleteFile(filePath);
        
        if (deleted) {
            result.filesDeleted++;
            result.bytesFreed += size;
            result.deletedFiles << filePath;
            emit cleanProgress(result.filesDeleted, -1, filePath);
        } else {
            result.filesFailed++;
            result.errors << tr("Failed to delete: %1").arg(filePath);
        }
    }
    
    // Then, try to remove empty directories (bottom-up)
    if (recursive && !m_dryRun) {
        QDirIterator dirIt(path, QDir::Dirs | QDir::NoDotAndDotDot, QDirIterator::Subdirectories);
        QStringList dirs;
        while (dirIt.hasNext()) {
            dirs << dirIt.next();
        }
        
        // Sort by length descending to delete deepest first
        std::sort(dirs.begin(), dirs.end(), [](const QString& a, const QString& b) {
            return a.length() > b.length();
        });
        
        for (const QString& dirPath : dirs) {
            QDir d(dirPath);
            if (d.isEmpty()) {
                d.rmdir(dirPath);
            }
        }
    }
    
    return result;
}

bool TempCleaner::deleteFile(const QString& path)
{
    QFileInfo fi(path);
    
    if (!fi.exists()) return true;
    
    // Handle read-only files
    if (!fi.isWritable()) {
        if (!m_deleteReadOnly) {
            return false;
        }
        
#ifdef _WIN32
        SetFileAttributesW(path.toStdWString().c_str(), FILE_ATTRIBUTE_NORMAL);
#endif
    }
    
    if (m_secureDelete) {
        return secureDeleteFile(path);
    }
    
    return QFile::remove(path);
}

bool TempCleaner::deleteDirectory(const QString& path)
{
    QDir dir(path);
    return dir.removeRecursively();
}

bool TempCleaner::secureDeleteFile(const QString& path)
{
    QFile file(path);
    
    if (!file.open(QIODevice::WriteOnly)) {
        return QFile::remove(path);
    }
    
    qint64 size = file.size();
    
    // Overwrite with random data 3 times
    for (int pass = 0; pass < 3; pass++) {
        file.seek(0);
        
        QByteArray buffer(4096, 0);
        qint64 remaining = size;
        
        while (remaining > 0) {
            for (int i = 0; i < buffer.size(); i++) {
                buffer[i] = static_cast<char>(QRandomGenerator::global()->generate());
            }
            
            qint64 toWrite = qMin(remaining, static_cast<qint64>(buffer.size()));
            file.write(buffer.constData(), toWrite);
            remaining -= toWrite;
        }
        
        file.flush();
    }
    
    file.close();
    return QFile::remove(path);
}

void TempCleaner::stop()
{
    m_stopRequested = true;
}


// === Selection Functions ===

void TempCleaner::setSelected(CleanCategory category, bool selected)
{
    for (auto& cat : m_categories) {
        if (cat.category == category) {
            cat.isSelected = selected;
            break;
        }
    }
}

void TempCleaner::selectGroup(const QString& group, bool selected)
{
    for (auto& cat : m_categories) {
        if (cat.group == group) {
            cat.isSelected = selected;
        }
    }
}

void TempCleaner::selectSafeOnly()
{
    for (auto& cat : m_categories) {
        cat.isSelected = (cat.riskLevel == CleanRiskLevel::Safe);
    }
}

void TempCleaner::selectAll(bool selected)
{
    for (auto& cat : m_categories) {
        cat.isSelected = selected;
    }
}

int TempCleaner::selectedCount() const
{
    int count = 0;
    for (const auto& cat : m_categories) {
        if (cat.isSelected) count++;
    }
    return count;
}

// === Configuration ===

void TempCleaner::addCustomPath(const QString& path, const QString& pattern)
{
    Q_UNUSED(pattern);
    if (!m_customPaths.contains(path)) {
        m_customPaths << path;
    }
}

void TempCleaner::removeCustomPath(const QString& path)
{
    m_customPaths.removeAll(path);
}

void TempCleaner::addExclusion(const QString& pattern)
{
    if (!m_exclusions.contains(pattern)) {
        m_exclusions << pattern;
    }
}

void TempCleaner::removeExclusion(const QString& pattern)
{
    m_exclusions.removeAll(pattern);
}

// === Category Access ===

CleanCategoryInfo& TempCleaner::categoryInfo(CleanCategory category)
{
    for (auto& cat : m_categories) {
        if (cat.category == category) {
            return cat;
        }
    }
    static CleanCategoryInfo empty;
    return empty;
}

const CleanCategoryInfo& TempCleaner::categoryInfo(CleanCategory category) const
{
    for (const auto& cat : m_categories) {
        if (cat.category == category) {
            return cat;
        }
    }
    static CleanCategoryInfo empty;
    return empty;
}

std::vector<CleanCategoryInfo*> TempCleaner::getCategoriesByGroup(const QString& group)
{
    std::vector<CleanCategoryInfo*> result;
    for (auto& cat : m_categories) {
        if (cat.group == group) {
            result.push_back(&cat);
        }
    }
    return result;
}

qint64 TempCleaner::totalEstimatedSize() const
{
    qint64 total = 0;
    for (const auto& cat : m_categories) {
        total += cat.estimatedSize;
    }
    return total;
}

qint64 TempCleaner::selectedSize() const
{
    qint64 total = 0;
    for (const auto& cat : m_categories) {
        if (cat.isSelected) {
            total += cat.estimatedSize;
        }
    }
    return total;
}

// === Special Cleaners ===

bool TempCleaner::cleanRecycleBin()
{
#ifdef _WIN32
    HRESULT hr = SHEmptyRecycleBinW(nullptr, nullptr, 
        SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
    return SUCCEEDED(hr) || hr == S_FALSE; // S_FALSE means already empty
#else
    return false;
#endif
}

bool TempCleaner::emptyRecycleBin()
{
#ifdef _WIN32
    HRESULT hr = SHEmptyRecycleBinW(nullptr, nullptr, 
        SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
    return SUCCEEDED(hr) || hr == S_FALSE;
#else
    return false;
#endif
}

bool TempCleaner::flushDnsCache()
{
#ifdef _WIN32
    QProcess process;
    process.start("ipconfig", {"/flushdns"});
    process.waitForFinished(5000);
    return process.exitCode() == 0;
#else
    return false;
#endif
}

bool TempCleaner::clearClipboard()
{
    QClipboard* clipboard = QApplication::clipboard();
    if (clipboard) {
        clipboard->clear();
        return true;
    }
    return false;
}

bool TempCleaner::cleanWindowsUpdate()
{
#ifdef _WIN32
    // Stop Windows Update service
    QProcess::execute("net", {"stop", "wuauserv", "/y"});
    
    // Clean the folder
    QString path = expandEnvironmentPath("%WINDIR%") + "\\SoftwareDistribution\\Download";
    bool result = deleteDirectory(path);
    
    // Restart service
    QProcess::execute("net", {"start", "wuauserv"});
    
    return result;
#else
    return false;
#endif
}

bool TempCleaner::cleanPrefetch()
{
#ifdef _WIN32
    QString path = expandEnvironmentPath("%WINDIR%") + "\\Prefetch";
    QDir dir(path);
    
    for (const QString& file : dir.entryList({"*.pf"}, QDir::Files)) {
        QFile::remove(dir.absoluteFilePath(file));
    }
    
    return true;
#else
    return false;
#endif
}

bool TempCleaner::cleanThumbnailCache()
{
#ifdef _WIN32
    QString path = expandEnvironmentPath("%LOCALAPPDATA%") + "\\Microsoft\\Windows\\Explorer";
    QDir dir(path);
    
    for (const QString& file : dir.entryList({"thumbcache_*.db", "iconcache_*.db"}, QDir::Files | QDir::Hidden)) {
        QFile::remove(dir.absoluteFilePath(file));
    }
    
    return true;
#else
    return false;
#endif
}

bool TempCleaner::cleanIconCache()
{
#ifdef _WIN32
    QString localAppData = expandEnvironmentPath("%LOCALAPPDATA%");
    QFile::remove(localAppData + "\\IconCache.db");
    
    QDir dir(localAppData + "\\Microsoft\\Windows\\Explorer");
    for (const QString& file : dir.entryList({"iconcache*.db"}, QDir::Files | QDir::Hidden)) {
        QFile::remove(dir.absoluteFilePath(file));
    }
    
    return true;
#else
    return false;
#endif
}

bool TempCleaner::cleanFontCache()
{
#ifdef _WIN32
    // Stop font cache service
    QProcess::execute("net", {"stop", "FontCache", "/y"});
    
    QString path = expandEnvironmentPath("%WINDIR%") + 
                   "\\ServiceProfiles\\LocalService\\AppData\\Local\\FontCache";
    
    QDir dir(path);
    for (const QString& file : dir.entryList({"*"}, QDir::Files)) {
        QFile::remove(dir.absoluteFilePath(file));
    }
    
    // Restart service
    QProcess::execute("net", {"start", "FontCache"});
    
    return true;
#else
    return false;
#endif
}

bool TempCleaner::cleanDnsCache()
{
    return flushDnsCache();
}

bool TempCleaner::cleanArpCache()
{
#ifdef _WIN32
    QProcess process;
    process.start("netsh", {"interface", "ip", "delete", "arpcache"});
    process.waitForFinished(5000);
    return process.exitCode() == 0;
#else
    return false;
#endif
}

// === Utilities ===

QString TempCleaner::formatBytes(qint64 bytes)
{
    return SystemInfo::formatBytes(bytes);
}

bool TempCleaner::isAdmin()
{
    return SystemInfo::isAdministrator();
}
