#pragma once

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#endif

#include <QObject>
#include <QString>
#include <QStringList>
#include <QDateTime>
#include <QIcon>
#include <QAbstractItemModel>
#include <QThread>
#include <QMutex>
#include <vector>
#include <memory>
#include <atomic>
#include <unordered_map>

/**
 * @brief Information about a file or directory
 */
struct FileSystemItem {
    QString name;
    QString path;
    qint64 size{0};              // Size in bytes (for dirs: cumulative)
    qint64 allocatedSize{0};     // Actual disk allocation
    bool isDirectory{false};
    int fileCount{0};            // Number of files (for directories)
    int dirCount{0};             // Number of subdirectories
    int depth{0};                // Depth in tree
    QDateTime lastModified;
    
    // For tree structure
    FileSystemItem* parent{nullptr};
    std::vector<std::unique_ptr<FileSystemItem>> children;
    
    // Percentage of parent
    double percentOfParent{0.0};
    
    // For display
    QIcon icon;
    QString extension;
};

/**
 * @brief Information about a large file found during scan
 */
struct LargeFileInfo {
    QString path;
    QString name;
    QString extension;
    qint64 size{0};
    QDateTime lastModified;
    QDateTime lastAccessed;
    bool isReadOnly{false};
    bool isSystem{false};
    bool isHidden{false};
};

/**
 * @brief Scan statistics
 */
struct ScanStatistics {
    qint64 totalSize{0};
    qint64 totalAllocated{0};
    int totalFiles{0};
    int totalDirectories{0};
    int filesScanned{0};
    int directoriesScanned{0};
    double scanDurationSeconds{0.0};
    QString rootPath;
    
    // Size distribution
    int filesUnder1MB{0};
    int files1to10MB{0};
    int files10to100MB{0};
    int files100MBto1GB{0};
    int filesOver1GB{0};
    
    // Top extensions by size
    std::vector<std::pair<QString, qint64>> topExtensions;
};

/**
 * @brief Tree model for displaying directory structure with sizes
 */
class DiskScannerTreeModel : public QAbstractItemModel
{
    Q_OBJECT

public:
    enum Column {
        ColName = 0,
        ColSize,
        ColAllocated,
        ColPercent,
        ColFiles,
        ColLastModified,
        ColCount
    };

    explicit DiskScannerTreeModel(QObject* parent = nullptr);
    ~DiskScannerTreeModel() override;
    
    void setRootItem(std::unique_ptr<FileSystemItem> root);
    void clear();
    
    FileSystemItem* getItem(const QModelIndex& index) const;
    QModelIndex findIndex(const QString& path) const;
    
    // QAbstractItemModel interface
    QModelIndex index(int row, int column, const QModelIndex& parent = QModelIndex()) const override;
    QModelIndex parent(const QModelIndex& index) const override;
    int rowCount(const QModelIndex& parent = QModelIndex()) const override;
    int columnCount(const QModelIndex& parent = QModelIndex()) const override;
    QVariant data(const QModelIndex& index, int role = Qt::DisplayRole) const override;
    QVariant headerData(int section, Qt::Orientation orientation, int role) const override;
    Qt::ItemFlags flags(const QModelIndex& index) const override;
    
    void sortChildren(FileSystemItem* item, int column, Qt::SortOrder order);

private:
    QString formatSize(qint64 bytes) const;
    QModelIndex findIndexRecursive(FileSystemItem* item, const QString& path, const QModelIndex& parentIndex) const;
    
    std::unique_ptr<FileSystemItem> m_rootItem;
};

/**
 * @brief Worker thread for scanning directories
 */
class DiskScannerWorker : public QObject
{
    Q_OBJECT

public:
    explicit DiskScannerWorker(QObject* parent = nullptr);
    
    void setPath(const QString& path) { m_path = path; }
    void setMinFileSize(qint64 size) { m_minFileSize = size; }
    void setMaxDepth(int depth) { m_maxDepth = depth; }
    void cancel() { m_cancelled = true; }
    bool isCancelled() const { return m_cancelled; }

public slots:
    void process();

signals:
    void started();
    void progress(int filesScanned, int dirsScanned, const QString& currentPath);
    void largeFileFound(const LargeFileInfo& file);
    void finished(FileSystemItem* root, const ScanStatistics& stats);
    void error(const QString& message);

private:
    void scanDirectory(FileSystemItem* parent, int currentDepth);
    void collectExtensionStats();
    
    QString m_path;
    qint64 m_minFileSize{1024 * 1024}; // 1 MB default
    int m_maxDepth{-1}; // -1 = unlimited
    std::atomic<bool> m_cancelled{false};
    
    // Statistics
    ScanStatistics m_stats;
    std::unordered_map<QString, qint64> m_extensionSizes;
    
    // Large files collection
    std::vector<LargeFileInfo> m_largeFiles;
    qint64 m_largeFileThreshold{10 * 1024 * 1024}; // 10 MB
};

/**
 * @brief Main disk scanner monitor class
 */
class DiskScannerMonitor : public QObject
{
    Q_OBJECT

public:
    explicit DiskScannerMonitor(QObject* parent = nullptr);
    ~DiskScannerMonitor() override;

    /// Start scanning a path
    void startScan(const QString& path);
    
    /// Cancel ongoing scan
    void cancelScan();
    
    /// Check if currently scanning
    bool isScanning() const { return m_isScanning; }
    
    /// Get the tree model
    DiskScannerTreeModel* model() { return m_model.get(); }
    
    /// Get scan statistics
    const ScanStatistics& statistics() const { return m_statistics; }
    
    /// Get large files found
    const std::vector<LargeFileInfo>& largeFiles() const { return m_largeFiles; }
    
    /// Settings
    void setMinFileSize(qint64 size) { m_minFileSize = size; }
    void setLargeFileThreshold(qint64 size) { m_largeFileThreshold = size; }
    void setMaxDepth(int depth) { m_maxDepth = depth; }
    
    /// File operations
    static bool deleteFile(const QString& path);
    static bool deleteDirectory(const QString& path);
    static bool moveToRecycleBin(const QString& path);
    static bool openInExplorer(const QString& path);
    static bool openFile(const QString& path);
    
    /// Utility
    static QString formatSize(qint64 bytes);
    static QString formatSizeShort(qint64 bytes);
    static QStringList getAvailableDrives();
    static qint64 getDriveTotal(const QString& drive);
    static qint64 getDriveFree(const QString& drive);

signals:
    void scanStarted(const QString& path);
    void scanProgress(int filesScanned, int dirsScanned, const QString& currentPath);
    void scanFinished(const ScanStatistics& stats);
    void scanCancelled();
    void scanError(const QString& error);
    void largeFileFound(const LargeFileInfo& file);

private slots:
    void onWorkerFinished(FileSystemItem* root, const ScanStatistics& stats);
    void onWorkerError(const QString& error);
    void onLargeFileFound(const LargeFileInfo& file);

private:
    std::unique_ptr<DiskScannerTreeModel> m_model;
    std::unique_ptr<QThread> m_workerThread;
    DiskScannerWorker* m_worker{nullptr};
    
    ScanStatistics m_statistics;
    std::vector<LargeFileInfo> m_largeFiles;
    
    bool m_isScanning{false};
    qint64 m_minFileSize{1024 * 1024};
    qint64 m_largeFileThreshold{10 * 1024 * 1024};
    int m_maxDepth{-1};
};

// Register types for Qt signals/slots
Q_DECLARE_METATYPE(ScanStatistics)
Q_DECLARE_METATYPE(LargeFileInfo)
Q_DECLARE_METATYPE(FileSystemItem*)
