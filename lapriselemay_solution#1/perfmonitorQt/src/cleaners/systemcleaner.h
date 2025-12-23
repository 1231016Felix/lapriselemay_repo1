#pragma once

#include <QObject>
#include <QString>
#include <QStringList>
#include <QDateTime>
#include <QIcon>
#include <vector>
#include <memory>
#include <functional>
#include <atomic>

/**
 * @brief Category of cleanable items
 */
enum class CleanerCategory {
    WindowsTemp,        // Windows temporary files
    UserTemp,           // User temporary files
    BrowserCache,       // All browsers cache
    BrowserHistory,     // Browser history (optional, privacy)
    BrowserCookies,     // Browser cookies (optional, privacy)
    Thumbnails,         // Windows thumbnail cache
    Prefetch,           // Windows prefetch files
    RecycleBin,         // Recycle Bin
    WindowsLogs,        // Windows log files
    WindowsUpdate,      // Windows Update cache
    MemoryDumps,        // Crash dumps
    DNSCache,           // DNS resolver cache
    FontCache,          // Font cache
    IconCache,          // Icon cache
    RecentDocs,         // Recent documents list
    Clipboard,          // Clipboard contents
    
    // Application-specific
    ChromeCache,
    ChromeHistory,
    ChromeCookies,
    FirefoxCache,
    FirefoxHistory,
    FirefoxCookies,
    EdgeCache,
    EdgeHistory,
    EdgeCookies,
    OperaCache,
    BraveCache,
    
    // Development
    VSCodeCache,
    NPMCache,
    NuGetCache,
    PipCache,
    
    // Gaming
    SteamCache,
    EpicGamesCache,
    
    // System
    OldWindowsInstall,  // Windows.old folder
    DeliveryOptimization,
    ErrorReports,
    
    Custom              // User-defined paths
};

/**
 * @brief Information about a cleanable location
 */
struct CleanerLocation {
    QString path;               // Path pattern (can include %TEMP%, etc.)
    QString filePattern;        // File pattern (*.tmp, *.log, etc.) or "*" for all
    bool recursive{true};       // Search subdirectories
    bool deleteEmptyDirs{true}; // Delete empty directories after cleaning
    int minAgeDays{0};          // Minimum file age in days (0 = all)
    qint64 minSizeBytes{0};     // Minimum file size (0 = all)
};

/**
 * @brief A cleanable item category with metadata
 */
struct CleanerItem {
    CleanerCategory category;
    QString name;               // Display name
    QString description;        // Description of what gets cleaned
    QIcon icon;                 // Category icon
    std::vector<CleanerLocation> locations;
    
    bool isEnabled{true};       // User wants to clean this
    bool requiresAdmin{false};  // Needs admin privileges
    bool isSafe{true};          // Safe to clean (won't break anything)
    bool isPrivacy{false};      // Privacy-related (history, cookies)
    
    // Scan results
    qint64 sizeBytes{0};        // Total size found
    int fileCount{0};           // Number of files found
    int errorCount{0};          // Errors during scan
    QStringList files;          // List of files to delete (populated during scan)
    QStringList errors;         // Error messages
};

/**
 * @brief Result of a cleaning operation
 */
struct CleaningResult {
    qint64 bytesFreed{0};
    int filesDeleted{0};
    int directoriesDeleted{0};
    int errors{0};
    QStringList errorMessages;
    double durationSeconds{0.0};
};

/**
 * @brief Powerful system cleaner similar to CCleaner/BleachBit
 */
class SystemCleaner : public QObject
{
    Q_OBJECT

public:
    explicit SystemCleaner(QObject *parent = nullptr);
    ~SystemCleaner() override;

    /// Initialize cleaner categories
    void initialize();
    
    /// Get all cleaner items
    std::vector<CleanerItem>& items() { return m_items; }
    const std::vector<CleanerItem>& items() const { return m_items; }
    
    /// Get items by category type
    std::vector<CleanerItem*> getItemsByType(bool privacy, bool requiresAdmin);
    
    /// Enable/disable specific category
    void setItemEnabled(CleanerCategory category, bool enabled);
    void setAllEnabled(bool enabled);
    void setPrivacyItemsEnabled(bool enabled);
    
    /// Scan for files to clean (doesn't delete anything)
    void startScan();
    void cancelScan();
    bool isScanning() const { return m_isScanning; }
    
    /// Get total size that can be cleaned
    qint64 totalCleanableSize() const;
    int totalCleanableFiles() const;
    
    /// Perform the actual cleaning
    void startCleaning();
    void cancelCleaning();
    bool isCleaning() const { return m_isCleaning; }
    
    /// Get last cleaning result
    const CleaningResult& lastResult() const { return m_lastResult; }
    
    /// Utility functions
    static QString formatSize(qint64 bytes);
    static QString expandPath(const QString& path);
    static bool isAdmin();
    
    /// Special cleaning operations
    bool emptyRecycleBin();
    bool clearDNSCache();
    bool clearClipboard();
    bool clearRecentDocs();

signals:
    void scanStarted();
    void scanProgress(int current, int total, const QString& currentItem);
    void scanItemCompleted(CleanerCategory category, qint64 size, int files);
    void scanCompleted(qint64 totalSize, int totalFiles);
    void scanCancelled();
    
    void cleaningStarted();
    void cleaningProgress(int current, int total, const QString& currentFile);
    void cleaningItemCompleted(CleanerCategory category, qint64 freedSize, int deletedFiles);
    void cleaningCompleted(const CleaningResult& result);
    void cleaningCancelled();
    
    void errorOccurred(const QString& error);

private:
    void initializeWindowsItems();
    void initializeBrowserItems();
    void initializeApplicationItems();
    void initializePrivacyItems();
    
    void scanItem(CleanerItem& item);
    void scanLocation(CleanerItem& item, const CleanerLocation& location);
    qint64 calculateDirectorySize(const QString& path, const CleanerLocation& location,
                                   QStringList& outFiles, int& outCount);
    
    void cleanItem(CleanerItem& item, CleaningResult& result);
    bool deleteFile(const QString& filePath);
    bool deleteDirectory(const QString& dirPath);
    void deleteEmptyDirectories(const QString& basePath);
    
    QString getCategoryIcon(CleanerCategory category);
    
    std::vector<CleanerItem> m_items;
    CleaningResult m_lastResult;
    
    std::atomic<bool> m_isScanning{false};
    std::atomic<bool> m_isCleaning{false};
    std::atomic<bool> m_cancelRequested{false};
};
