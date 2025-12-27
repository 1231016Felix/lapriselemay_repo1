#include "diskscannermonitor.h"

#ifdef _WIN32
#include <shellapi.h>
#include <shlobj.h>
#include <fileapi.h>
#pragma comment(lib, "shell32.lib")
#endif

#include <QDir>
#include <QFileInfo>
#include <QFileIconProvider>
#include <QDesktopServices>
#include <QUrl>
#include <QStorageInfo>
#include <QDateTime>
#include <QDebug>
#include <QElapsedTimer>
#include <QColor>
#include <QBrush>
#include <algorithm>

// ============================================================================
// DiskScannerTreeModel Implementation
// ============================================================================

DiskScannerTreeModel::DiskScannerTreeModel(QObject* parent)
    : QAbstractItemModel(parent)
{
}

DiskScannerTreeModel::~DiskScannerTreeModel() = default;

void DiskScannerTreeModel::setRootItem(std::unique_ptr<FileSystemItem> root)
{
    beginResetModel();
    m_rootItem = std::move(root);
    endResetModel();
}

void DiskScannerTreeModel::clear()
{
    beginResetModel();
    m_rootItem.reset();
    endResetModel();
}

FileSystemItem* DiskScannerTreeModel::getItem(const QModelIndex& index) const
{
    if (!index.isValid()) return nullptr;
    return static_cast<FileSystemItem*>(index.internalPointer());
}

QModelIndex DiskScannerTreeModel::index(int row, int column, const QModelIndex& parent) const
{
    if (!m_rootItem || column < 0 || column >= ColCount) {
        return {};
    }
    
    FileSystemItem* parentItem = parent.isValid() 
        ? static_cast<FileSystemItem*>(parent.internalPointer())
        : m_rootItem.get();
    
    if (!parentItem || row < 0 || row >= static_cast<int>(parentItem->children.size())) {
        return {};
    }
    
    return createIndex(row, column, parentItem->children[row].get());
}

QModelIndex DiskScannerTreeModel::parent(const QModelIndex& index) const
{
    if (!index.isValid() || !m_rootItem) {
        return {};
    }
    
    auto* item = static_cast<FileSystemItem*>(index.internalPointer());
    if (!item || !item->parent || item->parent == m_rootItem.get()) {
        return {};
    }
    
    // Find row of parent in grandparent
    FileSystemItem* grandParent = item->parent->parent;
    if (!grandParent) {
        grandParent = m_rootItem.get();
    }
    
    for (int i = 0; i < static_cast<int>(grandParent->children.size()); ++i) {
        if (grandParent->children[i].get() == item->parent) {
            return createIndex(i, 0, item->parent);
        }
    }
    
    return {};
}

int DiskScannerTreeModel::rowCount(const QModelIndex& parent) const
{
    if (!m_rootItem) return 0;
    
    if (!parent.isValid()) {
        return static_cast<int>(m_rootItem->children.size());
    }
    
    auto* item = static_cast<FileSystemItem*>(parent.internalPointer());
    return item ? static_cast<int>(item->children.size()) : 0;
}

int DiskScannerTreeModel::columnCount(const QModelIndex&) const
{
    return ColCount;
}

QVariant DiskScannerTreeModel::data(const QModelIndex& index, int role) const
{
    if (!index.isValid()) return {};
    
    auto* item = static_cast<FileSystemItem*>(index.internalPointer());
    if (!item) return {};
    
    if (role == Qt::DisplayRole) {
        switch (index.column()) {
            case ColName: return item->name;
            case ColSize: return formatSize(item->size);
            case ColAllocated: return formatSize(item->allocatedSize);
            case ColPercent: return QString("%1%").arg(item->percentOfParent, 0, 'f', 1);
            case ColFiles: 
                if (item->isDirectory) {
                    return QString("%1 files, %2 folders").arg(item->fileCount).arg(item->dirCount);
                }
                return "-";
            case ColLastModified: return item->lastModified.toString("yyyy-MM-dd hh:mm");
        }
    }
    else if (role == Qt::DecorationRole && index.column() == ColName) {
        return item->icon;
    }
    else if (role == Qt::ToolTipRole) {
        QString tooltip = QString("<b>%1</b><br>").arg(item->path);
        tooltip += QString("Size: %1<br>").arg(formatSize(item->size));
        if (item->isDirectory) {
            tooltip += QString("Files: %1<br>Folders: %2").arg(item->fileCount).arg(item->dirCount);
        }
        return tooltip;
    }
    else if (role == Qt::TextAlignmentRole) {
        if (index.column() >= ColSize && index.column() <= ColPercent) {
            return static_cast<int>(Qt::AlignRight | Qt::AlignVCenter);
        }
    }
    else if (role == Qt::BackgroundRole) {
        // Color code by percentage
        if (item->percentOfParent > 50) {
            return QColor(255, 200, 200, 100); // Light red
        } else if (item->percentOfParent > 25) {
            return QColor(255, 255, 200, 100); // Light yellow
        }
    }
    else if (role == Qt::UserRole) {
        // Return size for sorting
        return item->size;
    }
    else if (role == Qt::UserRole + 1) {
        // Return path
        return item->path;
    }
    
    return {};
}

QVariant DiskScannerTreeModel::headerData(int section, Qt::Orientation orientation, int role) const
{
    if (orientation != Qt::Horizontal || role != Qt::DisplayRole) {
        return {};
    }
    
    switch (section) {
        case ColName: return tr("Name");
        case ColSize: return tr("Size");
        case ColAllocated: return tr("Allocated");
        case ColPercent: return tr("%");
        case ColFiles: return tr("Contents");
        case ColLastModified: return tr("Modified");
    }
    return {};
}

Qt::ItemFlags DiskScannerTreeModel::flags(const QModelIndex& index) const
{
    if (!index.isValid()) return Qt::NoItemFlags;
    return Qt::ItemIsEnabled | Qt::ItemIsSelectable;
}

void DiskScannerTreeModel::sortChildren(FileSystemItem* item, int column, Qt::SortOrder order)
{
    if (!item) return;
    
    std::sort(item->children.begin(), item->children.end(),
        [column, order](const std::unique_ptr<FileSystemItem>& a, 
                        const std::unique_ptr<FileSystemItem>& b) {
            bool less = false;
            switch (column) {
                case ColName:
                    less = a->name.toLower() < b->name.toLower();
                    break;
                case ColSize:
                case ColAllocated:
                case ColPercent:
                    less = a->size < b->size;
                    break;
                case ColFiles:
                    less = a->fileCount < b->fileCount;
                    break;
                case ColLastModified:
                    less = a->lastModified < b->lastModified;
                    break;
                default:
                    less = a->size < b->size;
            }
            return order == Qt::AscendingOrder ? less : !less;
        });
    
    // Recursively sort children
    for (auto& child : item->children) {
        if (child->isDirectory) {
            sortChildren(child.get(), column, order);
        }
    }
}

QString DiskScannerTreeModel::formatSize(qint64 bytes) const
{
    return DiskScannerMonitor::formatSize(bytes);
}

QModelIndex DiskScannerTreeModel::findIndex(const QString& path) const
{
    if (!m_rootItem) return {};
    return findIndexRecursive(m_rootItem.get(), path, QModelIndex());
}

QModelIndex DiskScannerTreeModel::findIndexRecursive(FileSystemItem* item, const QString& path, 
                                                      const QModelIndex& parentIndex) const
{
    for (int i = 0; i < static_cast<int>(item->children.size()); ++i) {
        auto& child = item->children[i];
        if (child->path == path) {
            return index(i, 0, parentIndex);
        }
        if (path.startsWith(child->path) && child->isDirectory) {
            QModelIndex childIndex = index(i, 0, parentIndex);
            auto result = findIndexRecursive(child.get(), path, childIndex);
            if (result.isValid()) return result;
        }
    }
    return {};
}

// ============================================================================
// DiskScannerWorker Implementation
// ============================================================================

DiskScannerWorker::DiskScannerWorker(QObject* parent)
    : QObject(parent)
{
    // Register types for cross-thread signals
    qRegisterMetaType<ScanStatistics>("ScanStatistics");
    qRegisterMetaType<LargeFileInfo>("LargeFileInfo");
    qRegisterMetaType<FileSystemItem*>("FileSystemItem*");
}

void DiskScannerWorker::process()
{
    emit started();
    
    m_cancelled = false;
    m_stats = ScanStatistics{};
    m_stats.rootPath = m_path;
    m_extensionSizes.clear();
    m_largeFiles.clear();
    
    QElapsedTimer timer;
    timer.start();
    
    // Create root item
    auto root = std::make_unique<FileSystemItem>();
    QFileInfo rootInfo(m_path);
    root->name = rootInfo.fileName().isEmpty() ? m_path : rootInfo.fileName();
    root->path = m_path;
    root->isDirectory = true;
    root->depth = 0;
    
    QFileIconProvider iconProvider;
    root->icon = iconProvider.icon(rootInfo);
    
    // Scan
    scanDirectory(root.get(), 0);
    
    if (m_cancelled) {
        return;
    }
    
    // Calculate statistics
    m_stats.totalSize = root->size;
    m_stats.totalAllocated = root->allocatedSize;
    m_stats.totalFiles = root->fileCount;
    m_stats.totalDirectories = root->dirCount;
    m_stats.scanDurationSeconds = timer.elapsed() / 1000.0;
    
    // Collect extension statistics
    collectExtensionStats();
    
    emit finished(root.release(), m_stats);
}

void DiskScannerWorker::scanDirectory(FileSystemItem* parent, int currentDepth)
{
    if (m_cancelled) return;
    if (m_maxDepth >= 0 && currentDepth > m_maxDepth) return;
    
    QDir dir(parent->path);
    if (!dir.exists()) return;
    
    dir.setFilter(QDir::AllEntries | QDir::NoDotAndDotDot | QDir::Hidden | QDir::System);
    dir.setSorting(QDir::Size | QDir::Reversed);
    
    QFileIconProvider iconProvider;
    QFileInfoList entries = dir.entryInfoList();
    
    for (const QFileInfo& info : entries) {
        if (m_cancelled) return;
        
        auto item = std::make_unique<FileSystemItem>();
        item->name = info.fileName();
        item->path = info.absoluteFilePath();
        item->isDirectory = info.isDir();
        item->lastModified = info.lastModified();
        item->parent = parent;
        item->depth = currentDepth + 1;
        item->icon = iconProvider.icon(info);
        
        if (info.isDir()) {
            // Skip junction points and symlinks to avoid infinite loops
            if (info.isSymLink()) {
                continue;
            }
            
            m_stats.directoriesScanned++;
            parent->dirCount++;
            
            // Emit progress every 100 directories
            if (m_stats.directoriesScanned % 100 == 0) {
                emit progress(m_stats.filesScanned, m_stats.directoriesScanned, info.absoluteFilePath());
            }
            
            // Recursively scan subdirectory
            scanDirectory(item.get(), currentDepth + 1);
            
            // Aggregate counts from children
            parent->fileCount += item->fileCount;
            parent->dirCount += item->dirCount;
        }
        else {
            // File
            item->size = info.size();
            item->extension = info.suffix().toLower();
            
            m_stats.filesScanned++;
            parent->fileCount++;
            
            // Track extension sizes
            m_extensionSizes[item->extension] += item->size;
            
            // Size distribution
            if (item->size < 1024 * 1024) {
                m_stats.filesUnder1MB++;
            } else if (item->size < 10 * 1024 * 1024) {
                m_stats.files1to10MB++;
            } else if (item->size < 100 * 1024 * 1024) {
                m_stats.files10to100MB++;
            } else if (item->size < 1024 * 1024 * 1024) {
                m_stats.files100MBto1GB++;
            } else {
                m_stats.filesOver1GB++;
            }
            
            // Check for large files
            if (item->size >= m_largeFileThreshold) {
                LargeFileInfo largeFile;
                largeFile.path = item->path;
                largeFile.name = item->name;
                largeFile.extension = item->extension;
                largeFile.size = item->size;
                largeFile.lastModified = item->lastModified;
                largeFile.lastAccessed = info.lastRead();
                largeFile.isReadOnly = !info.isWritable();
                largeFile.isHidden = info.isHidden();
                
                m_largeFiles.push_back(largeFile);
                emit largeFileFound(largeFile);
            }
            
#ifdef _WIN32
            // Get allocated size (cluster-aligned)
            DWORD sectorsPerCluster, bytesPerSector, freeClusters, totalClusters;
            QString drive = item->path.left(3);
            if (GetDiskFreeSpaceW(reinterpret_cast<LPCWSTR>(drive.utf16()),
                                   &sectorsPerCluster, &bytesPerSector,
                                   &freeClusters, &totalClusters)) {
                qint64 clusterSize = sectorsPerCluster * bytesPerSector;
                item->allocatedSize = ((item->size + clusterSize - 1) / clusterSize) * clusterSize;
            } else {
                item->allocatedSize = item->size;
            }
#else
            item->allocatedSize = item->size;
#endif
        }
        
        // Add size to parent
        parent->size += item->size;
        parent->allocatedSize += item->allocatedSize;
        
        // Only keep items above minimum size for display
        if (item->size >= m_minFileSize || item->isDirectory) {
            parent->children.push_back(std::move(item));
        }
    }
    
    // Sort children by size (largest first)
    std::sort(parent->children.begin(), parent->children.end(),
        [](const std::unique_ptr<FileSystemItem>& a, const std::unique_ptr<FileSystemItem>& b) {
            return a->size > b->size;
        });
    
    // Calculate percentages
    for (auto& child : parent->children) {
        if (parent->size > 0) {
            child->percentOfParent = (static_cast<double>(child->size) / parent->size) * 100.0;
        }
    }
}

void DiskScannerWorker::collectExtensionStats()
{
    // Convert map to vector and sort by size
    std::vector<std::pair<QString, qint64>> extensions(m_extensionSizes.begin(), m_extensionSizes.end());
    std::sort(extensions.begin(), extensions.end(),
        [](const auto& a, const auto& b) { return a.second > b.second; });
    
    // Keep top 20
    if (extensions.size() > 20) {
        extensions.resize(20);
    }
    
    m_stats.topExtensions = std::move(extensions);
}

// ============================================================================
// DiskScannerMonitor Implementation
// ============================================================================

DiskScannerMonitor::DiskScannerMonitor(QObject* parent)
    : QObject(parent)
    , m_model(std::make_unique<DiskScannerTreeModel>())
{
}

DiskScannerMonitor::~DiskScannerMonitor()
{
    cancelScan();
}

void DiskScannerMonitor::startScan(const QString& path)
{
    if (m_isScanning) {
        cancelScan();
    }
    
    m_largeFiles.clear();
    m_model->clear();
    m_isScanning = true;
    
    // Create worker thread
    m_workerThread = std::make_unique<QThread>();
    m_worker = new DiskScannerWorker();
    m_worker->setPath(path);
    m_worker->setMinFileSize(m_minFileSize);
    m_worker->setMaxDepth(m_maxDepth);
    m_worker->moveToThread(m_workerThread.get());
    
    // Connect signals
    connect(m_workerThread.get(), &QThread::started, m_worker, &DiskScannerWorker::process);
    connect(m_worker, &DiskScannerWorker::progress, this, &DiskScannerMonitor::scanProgress);
    connect(m_worker, &DiskScannerWorker::largeFileFound, this, &DiskScannerMonitor::onLargeFileFound);
    connect(m_worker, &DiskScannerWorker::finished, this, &DiskScannerMonitor::onWorkerFinished);
    connect(m_worker, &DiskScannerWorker::error, this, &DiskScannerMonitor::onWorkerError);
    
    // Cleanup connections
    connect(m_worker, &DiskScannerWorker::finished, m_workerThread.get(), &QThread::quit);
    connect(m_worker, &DiskScannerWorker::finished, m_worker, &QObject::deleteLater);
    connect(m_workerThread.get(), &QThread::finished, this, [this]() {
        m_isScanning = false;
    });
    
    emit scanStarted(path);
    m_workerThread->start();
}

void DiskScannerMonitor::cancelScan()
{
    if (!m_isScanning || !m_worker) return;
    
    m_worker->cancel();
    
    if (m_workerThread && m_workerThread->isRunning()) {
        m_workerThread->quit();
        m_workerThread->wait(5000);
    }
    
    m_isScanning = false;
    emit scanCancelled();
}

void DiskScannerMonitor::onWorkerFinished(FileSystemItem* root, const ScanStatistics& stats)
{
    m_statistics = stats;
    m_model->setRootItem(std::unique_ptr<FileSystemItem>(root));
    m_isScanning = false;
    emit scanFinished(stats);
}

void DiskScannerMonitor::onWorkerError(const QString& error)
{
    m_isScanning = false;
    emit scanError(error);
}

void DiskScannerMonitor::onLargeFileFound(const LargeFileInfo& file)
{
    m_largeFiles.push_back(file);
    emit largeFileFound(file);
}

// Static utility functions

QString DiskScannerMonitor::formatSize(qint64 bytes)
{
    if (bytes < 0) return "-";
    
    const double KB = 1024.0;
    const double MB = KB * 1024.0;
    const double GB = MB * 1024.0;
    const double TB = GB * 1024.0;
    
    if (bytes >= TB) {
        return QString("%1 TB").arg(bytes / TB, 0, 'f', 2);
    } else if (bytes >= GB) {
        return QString("%1 GB").arg(bytes / GB, 0, 'f', 2);
    } else if (bytes >= MB) {
        return QString("%1 MB").arg(bytes / MB, 0, 'f', 1);
    } else if (bytes >= KB) {
        return QString("%1 KB").arg(bytes / KB, 0, 'f', 0);
    }
    return QString("%1 B").arg(bytes);
}

QString DiskScannerMonitor::formatSizeShort(qint64 bytes)
{
    if (bytes < 0) return "-";
    
    const double KB = 1024.0;
    const double MB = KB * 1024.0;
    const double GB = MB * 1024.0;
    
    if (bytes >= GB) {
        return QString("%1G").arg(bytes / GB, 0, 'f', 1);
    } else if (bytes >= MB) {
        return QString("%1M").arg(bytes / MB, 0, 'f', 0);
    } else if (bytes >= KB) {
        return QString("%1K").arg(bytes / KB, 0, 'f', 0);
    }
    return QString("%1B").arg(bytes);
}

QStringList DiskScannerMonitor::getAvailableDrives()
{
    QStringList drives;
    for (const QStorageInfo& storage : QStorageInfo::mountedVolumes()) {
        if (storage.isValid() && storage.isReady()) {
            drives << storage.rootPath();
        }
    }
    return drives;
}

qint64 DiskScannerMonitor::getDriveTotal(const QString& drive)
{
    QStorageInfo info(drive);
    return info.bytesTotal();
}

qint64 DiskScannerMonitor::getDriveFree(const QString& drive)
{
    QStorageInfo info(drive);
    return info.bytesAvailable();
}

bool DiskScannerMonitor::deleteFile(const QString& path)
{
    QFileInfo info(path);
    if (!info.exists()) return false;
    
    if (info.isDir()) {
        return deleteDirectory(path);
    }
    
    return QFile::remove(path);
}

bool DiskScannerMonitor::deleteDirectory(const QString& path)
{
    QDir dir(path);
    return dir.removeRecursively();
}

bool DiskScannerMonitor::moveToRecycleBin(const QString& path)
{
#ifdef _WIN32
    // Use Windows Shell API for recycle bin
    std::wstring wpath = path.toStdWString();
    wpath.push_back(L'\0'); // Double null terminated
    wpath.push_back(L'\0');
    
    SHFILEOPSTRUCTW fileOp = {};
    fileOp.wFunc = FO_DELETE;
    fileOp.pFrom = wpath.c_str();
    fileOp.fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT;
    
    return SHFileOperationW(&fileOp) == 0;
#else
    // On non-Windows, just delete
    return deleteFile(path);
#endif
}

bool DiskScannerMonitor::openInExplorer(const QString& path)
{
#ifdef _WIN32
    QFileInfo info(path);
    QString explorerPath;
    
    if (info.isDir()) {
        explorerPath = path;
    } else {
        // Select the file in explorer
        QString cmd = QString("/select,\"%1\"").arg(QDir::toNativeSeparators(path));
        return reinterpret_cast<intptr_t>(ShellExecuteW(nullptr, L"open", L"explorer.exe",
            reinterpret_cast<LPCWSTR>(cmd.utf16()), nullptr, SW_SHOWNORMAL)) > 32;
    }
    
    return reinterpret_cast<intptr_t>(ShellExecuteW(nullptr, L"open",
        reinterpret_cast<LPCWSTR>(QDir::toNativeSeparators(explorerPath).utf16()),
        nullptr, nullptr, SW_SHOWNORMAL)) > 32;
#else
    return QDesktopServices::openUrl(QUrl::fromLocalFile(path));
#endif
}

bool DiskScannerMonitor::openFile(const QString& path)
{
    return QDesktopServices::openUrl(QUrl::fromLocalFile(path));
}
