#pragma once

#include <QObject>
#include <QString>
#include <QStringList>
#include <QDateTime>
#include <QMutex>
#include <vector>
#include <map>
#include <memory>
#include <atomic>
#include <functional>

/**
 * @brief Category of cleanable items
 */
enum class CleanCategory {
    // Windows System
    WindowsTemp,            // %TEMP% folder
    WindowsSystemTemp,      // C:\Windows\Temp
    WindowsPrefetch,        // C:\Windows\Prefetch
    WindowsUpdate,          // Windows Update cache
    WindowsInstaller,       // Windows Installer cache ($PatchCache$)
    WindowsLogs,            // Windows log files
    WindowsErrorReports,    // Windows Error Reporting
    WindowsDeliveryOptim,   // Delivery Optimization cache
    WindowsThumbnails,      // Thumbnail cache
    WindowsIconCache,       // Icon cache
    WindowsFontCache,       // Font cache
    WindowsUpdateCleanup,   // Old Windows Update files
    
    // Recycle Bin
    RecycleBin,             // Recycle Bin contents
    
    // Browsers
    ChromeCache,
    ChromeCookies,
    ChromeHistory,
    ChromeDownloads,
    ChromePasswords,
    ChromeFormData,
    ChromeSession,
    
    FirefoxCache,
    FirefoxCookies,
    FirefoxHistory,
    FirefoxDownloads,
    FirefoxSession,
    
    EdgeCache,
    EdgeCookies,
    EdgeHistory,
    EdgeDownloads,
    EdgeSession,
    
    BraveCache,
    BraveCookies,
    OperaCache,
    OperaCookies,
    
    // Applications
    AdobeCache,             // Adobe Reader/Acrobat cache
    OfficeCache,            // Microsoft Office cache
    SpotifyCache,           // Spotify cache
    DiscordCache,           // Discord cache
    TeamsCache,             // Microsoft Teams cache
    SlackCache,             // Slack cache
    SteamCache,             // Steam download cache
    EpicGamesCache,         // Epic Games cache
    VSCodeCache,            // VS Code cache
    JetBrainsCache,         // JetBrains IDEs cache
    NpmCache,               // npm cache
    PipCache,               // Python pip cache
    NuGetCache,             // NuGet cache
    MavenCache,             // Maven cache
    GradleCache,            // Gradle cache
    
    // System
    RecentDocuments,        // Recent documents list
    ClipboardData,          // Clipboard contents
    DNSCache,               // DNS resolver cache
    ARPCache,               // ARP cache
    
    // Developer
    VisualStudioCache,      // Visual Studio cache
    SymbolCache,            // Debug symbols cache
    
    // Custom
    CustomPath              // User-defined paths
};

/**
 * @brief Risk level for cleaning operations
 */
enum class CleanRiskLevel {
    Safe,       // No risk, always safe to clean
    Low,        // Minimal risk, may need re-login to some sites
    Medium,     // May lose some preferences or history
    High,       // May affect functionality (passwords, sessions)
    Critical    // Requires admin, may affect system
};

/**
 * @brief Information about a cleanable category
 */
struct CleanCategoryInfo {
    CleanCategory category;
    QString name;               // Display name
    QString description;        // Detailed description
    QString icon;               // Icon resource or emoji
    CleanRiskLevel riskLevel;
    bool requiresAdmin{false};
    bool isSelected{false};     // User selection
    bool isExpanded{false};     // UI state
    qint64 estimatedSize{0};    // Estimated bytes to clean
    int fileCount{0};           // Number of files found
    QStringList paths;          // Actual paths to clean
    QString group;              // Category group (Windows, Browsers, etc.)
};

/**
 * @brief Result of a cleaning operation
 */
struct CleanResult {
    CleanCategory category;
    bool success{false};
    qint64 bytesFreed{0};
    int filesDeleted{0};
    int filesFailed{0};
    QStringList errors;
    QStringList deletedFiles;   // Optional: list of deleted files
};

/**
 * @brief Overall cleaning summary
 */
struct CleanSummary {
    qint64 totalBytesFreed{0};
    int totalFilesDeleted{0};
    int totalFilesFailed{0};
    int categoriesCleaned{0};
    int categoriesFailed{0};
    QDateTime startTime;
    QDateTime endTime;
    std::vector<CleanResult> results;
};

/**
 * @brief File information for preview
 */
struct CleanFileInfo {
    QString path;
    qint64 size{0};
    QDateTime lastModified;
    bool isDirectory{false};
    CleanCategory category;
};

/**
 * @brief Powerful temporary files cleaner
 * 
 * Features:
 * - Multiple categories (Windows, browsers, applications)
 * - Size estimation before cleaning
 * - Safe and deep cleaning modes
 * - Exclusion patterns
 * - Dry run mode
 * - Detailed logging
 * - Admin operations support
 */
class TempCleaner : public QObject
{
    Q_OBJECT

public:
    explicit TempCleaner(QObject *parent = nullptr);
    ~TempCleaner() override;

    // === Analysis ===
    
    /// Analyze all categories and estimate sizes
    void analyzeAll();
    
    /// Analyze specific category
    void analyzeCategory(CleanCategory category);
    
    /// Get category info
    CleanCategoryInfo& categoryInfo(CleanCategory category);
    const CleanCategoryInfo& categoryInfo(CleanCategory category) const;
    
    /// Get all categories
    std::vector<CleanCategoryInfo>& categories() { return m_categories; }
    const std::vector<CleanCategoryInfo>& categories() const { return m_categories; }
    
    /// Get categories by group
    std::vector<CleanCategoryInfo*> getCategoriesByGroup(const QString& group);
    
    /// Get total estimated size
    qint64 totalEstimatedSize() const;
    
    /// Get selected categories size
    qint64 selectedSize() const;
    
    /// Get file list for preview
    std::vector<CleanFileInfo> getFilesForCategory(CleanCategory category, int maxFiles = 1000);

    // === Cleaning ===
    
    /// Clean selected categories
    void cleanSelected();
    
    /// Clean specific category
    void cleanCategory(CleanCategory category);
    
    /// Clean all categories
    void cleanAll();
    
    /// Stop current operation
    void stop();
    
    /// Check if operation is running
    bool isRunning() const { return m_isRunning; }
    
    // === Selection ===
    
    /// Select/deselect category
    void setSelected(CleanCategory category, bool selected);
    
    /// Select all in group
    void selectGroup(const QString& group, bool selected);
    
    /// Select all safe categories
    void selectSafeOnly();
    
    /// Select all categories
    void selectAll(bool selected);
    
    /// Get selected count
    int selectedCount() const;

    // === Configuration ===
    
    /// Add custom path to clean
    void addCustomPath(const QString& path, const QString& pattern = "*");
    
    /// Remove custom path
    void removeCustomPath(const QString& path);
    
    /// Get custom paths
    QStringList customPaths() const { return m_customPaths; }
    
    /// Add exclusion pattern
    void addExclusion(const QString& pattern);
    
    /// Remove exclusion
    void removeExclusion(const QString& pattern);
    
    /// Get exclusions
    QStringList exclusions() const { return m_exclusions; }
    
    /// Set dry run mode (don't actually delete)
    void setDryRun(bool dryRun) { m_dryRun = dryRun; }
    bool isDryRun() const { return m_dryRun; }
    
    /// Set whether to delete read-only files
    void setDeleteReadOnly(bool del) { m_deleteReadOnly = del; }
    bool deleteReadOnly() const { return m_deleteReadOnly; }
    
    /// Set minimum file age (days)
    void setMinFileAge(int days) { m_minFileAgeDays = days; }
    int minFileAge() const { return m_minFileAgeDays; }
    
    /// Set secure delete (overwrite before delete)
    void setSecureDelete(bool secure) { m_secureDelete = secure; }
    bool secureDelete() const { return m_secureDelete; }

    // === Utilities ===
    
    /// Format bytes to human readable
    static QString formatBytes(qint64 bytes);
    
    /// Check if running as admin
    static bool isAdmin();
    
    /// Empty recycle bin
    static bool emptyRecycleBin();
    
    /// Flush DNS cache
    static bool flushDnsCache();
    
    /// Clear clipboard
    static bool clearClipboard();
    
    /// Get last clean summary
    const CleanSummary& lastSummary() const { return m_lastSummary; }

signals:
    /// Analysis progress
    void analysisProgress(int current, int total, const QString& category);
    
    /// Analysis complete
    void analysisComplete();
    
    /// Category analyzed
    void categoryAnalyzed(CleanCategory category, qint64 size, int fileCount);
    
    /// Cleaning progress
    void cleanProgress(int current, int total, const QString& currentFile);
    
    /// Category cleaned
    void categoryCleaned(CleanCategory category, const CleanResult& result);
    
    /// Cleaning complete
    void cleanComplete(const CleanSummary& summary);
    
    /// Error occurred
    void errorOccurred(const QString& error);
    
    /// Log message
    void logMessage(const QString& message);

private:
    // Initialization
    void initializeCategories();
    
    // Path resolution
    QStringList resolvePaths(CleanCategory category);
    QString expandEnvironmentPath(const QString& path);
    QStringList getBrowserProfiles(const QString& browserPath);
    
    // Analysis
    qint64 analyzeDirectory(const QString& path, const QStringList& patterns, 
                            int& fileCount, bool recursive = true);
    bool matchesExclusion(const QString& path);
    bool isFileTooNew(const QString& path);
    
    // Cleaning
    CleanResult cleanDirectory(const QString& path, const QStringList& patterns,
                               bool recursive = true);
    bool deleteFile(const QString& path);
    bool deleteDirectory(const QString& path);
    bool secureDeleteFile(const QString& path);
    
    // Special cleaners
    bool cleanRecycleBin();
    bool cleanWindowsUpdate();
    bool cleanPrefetch();
    bool cleanThumbnailCache();
    bool cleanIconCache();
    bool cleanFontCache();
    bool cleanDnsCache();
    bool cleanArpCache();

    // Data
    std::vector<CleanCategoryInfo> m_categories;
    QStringList m_customPaths;
    QStringList m_exclusions;
    CleanSummary m_lastSummary;
    
    // Configuration
    bool m_dryRun{false};
    bool m_deleteReadOnly{false};
    bool m_secureDelete{false};
    int m_minFileAgeDays{0};
    
    // State
    std::atomic<bool> m_isRunning{false};
    std::atomic<bool> m_stopRequested{false};
    mutable QMutex m_mutex;
    
    // Cache
    std::map<QString, QString> m_envCache;
};
