#pragma once

// Windows header must come first to avoid conflicts
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
#include <QAbstractItemModel>
#include <QSortFilterProxyModel>
#include <QString>
#include <QIcon>
#include <QDateTime>
#include <QTimer>
#include <vector>
#include <memory>
#include <unordered_map>
#include <deque>

/**
 * @brief Process category for grouping
 */
enum class ProcessCategory {
    Apps,           // User applications with windows
    Background,     // Background processes
    Windows,        // Windows system processes
    Services,       // Windows services
    Unknown
};

/**
 * @brief Process state
 */
enum class ProcessState {
    Running,
    Suspended,
    NotResponding,
    Terminated
};

/**
 * @brief Extended process information
 */
struct AdvancedProcessInfo {
    // Basic info
    quint32 pid{0};
    quint32 parentPid{0};
    QString name;
    QString displayName;
    QString executablePath;
    QString commandLine;
    QString userName;
    QString description;
    
    // Performance
    double cpuUsage{0.0};
    double cpuUsageKernel{0.0};
    double cpuUsageUser{0.0};
    qint64 memoryBytes{0};
    qint64 privateBytes{0};
    qint64 virtualBytes{0};
    qint64 peakMemoryBytes{0};
    
    // I/O
    qint64 ioReadBytes{0};
    qint64 ioWriteBytes{0};
    qint64 ioReadBytesPerSec{0};
    qint64 ioWriteBytesPerSec{0};
    
    // Counts
    int threadCount{0};
    int handleCount{0};
    int gdiObjects{0};
    int userObjects{0};
    
    // State
    ProcessState state{ProcessState::Running};
    ProcessCategory category{ProcessCategory::Unknown};
    bool isElevated{false};
    bool is64Bit{false};
    bool hasWindow{false};
    
    // Timing
    QDateTime startTime;
    qint64 cpuTimeMs{0};
    
    // Children (for tree view)
    std::vector<quint32> childPids;
    
    // GPU (if available)
    double gpuUsage{0.0};
    qint64 gpuMemoryBytes{0};
    
    // Network (estimated)
    qint64 networkSentBytes{0};
    qint64 networkRecvBytes{0};
    
    // Icon
    QIcon icon;
};

/**
 * @brief Historical record of a terminated process
 */
struct ProcessHistoryEntry {
    quint32 pid;
    QString name;
    QString executablePath;
    QDateTime startTime;
    QDateTime endTime;
    qint64 peakMemoryBytes;
    qint64 totalCpuTimeMs;
    QString terminationReason; // "User", "System", "Crash", "Unknown"
    int exitCode;
};

/**
 * @brief Tree model for advanced process display with parent/child relationships
 */
class AdvancedProcessTreeModel : public QAbstractItemModel
{
    Q_OBJECT

public:
    enum Column {
        ColName = 0,
        ColPID,
        ColStatus,
        ColCPU,
        ColMemory,
        ColDisk,
        ColNetwork,
        ColGPU,
        ColThreads,
        ColHandles,
        ColUser,
        ColCount
    };
    
    enum class GroupingMode {
        None,           // Flat list
        ByCategory,     // Apps, Background, Windows
        ByParent,       // Process tree (parent/child)
        ByName          // Group same executables
    };

    explicit AdvancedProcessTreeModel(QObject *parent = nullptr);
    
    void setProcesses(const std::vector<AdvancedProcessInfo>& processes);
    void updateProcesses(const std::vector<AdvancedProcessInfo>& processes);
    void setGroupingMode(GroupingMode mode);
    GroupingMode groupingMode() const { return m_groupingMode; }
    
    // QAbstractItemModel interface
    QModelIndex index(int row, int column, const QModelIndex &parent = QModelIndex()) const override;
    QModelIndex parent(const QModelIndex &index) const override;
    int rowCount(const QModelIndex &parent = QModelIndex()) const override;
    int columnCount(const QModelIndex &parent = QModelIndex()) const override;
    QVariant data(const QModelIndex &index, int role = Qt::DisplayRole) const override;
    QVariant headerData(int section, Qt::Orientation orientation, int role) const override;
    Qt::ItemFlags flags(const QModelIndex &index) const override;
    
    // Process access
    AdvancedProcessInfo* getProcess(const QModelIndex& index);
    const AdvancedProcessInfo* getProcess(const QModelIndex& index) const;
    quint32 getPid(const QModelIndex& index) const;
    QModelIndex findIndexByPid(quint32 pid) const;
    std::vector<quint32> getChildPids(quint32 parentPid) const;

private:
    struct TreeNode {
        AdvancedProcessInfo* process{nullptr};
        QString groupName;
        ProcessCategory category{ProcessCategory::Unknown};
        std::vector<int> childIndices;
        int parentIndex{-1};
        bool isGroup{false};
        
        // Aggregated values for groups
        double totalCpu{0.0};
        qint64 totalMemory{0};
        int processCount{0};
    };
    
    void buildTree();
    void buildFlatTree();
    void buildCategoryTree();
    void buildParentChildTree();
    void buildNameGroupTree();
    
    QString formatBytes(qint64 bytes) const;
    QString formatBytesPerSec(qint64 bytesPerSec) const;
    QString getCategoryName(ProcessCategory cat) const;
    QColor getStateColor(ProcessState state) const;

    std::vector<AdvancedProcessInfo> m_processes;
    std::vector<TreeNode> m_nodes;
    std::vector<int> m_rootIndices;
    GroupingMode m_groupingMode{GroupingMode::ByCategory};
    
    mutable std::unordered_map<QString, QIcon> m_iconCache;
};

/**
 * @brief Sort/filter proxy for advanced process model
 */
class AdvancedProcessSortFilterProxy : public QSortFilterProxyModel
{
    Q_OBJECT
    
public:
    explicit AdvancedProcessSortFilterProxy(QObject* parent = nullptr);
    
    void setShowSystemProcesses(bool show);
    void setCategoryFilter(ProcessCategory cat);
    void clearCategoryFilter();
    
    QModelIndex findProxyIndexByPid(quint32 pid) const;

protected:
    bool lessThan(const QModelIndex &left, const QModelIndex &right) const override;
    bool filterAcceptsRow(int source_row, const QModelIndex &source_parent) const override;

private:
    bool m_showSystemProcesses{true};
    bool m_hasCategoryFilter{false};
    ProcessCategory m_categoryFilter{ProcessCategory::Unknown};
};

/**
 * @brief Process history manager - tracks terminated processes
 */
class ProcessHistoryManager : public QObject
{
    Q_OBJECT

public:
    explicit ProcessHistoryManager(QObject* parent = nullptr);
    
    void recordProcessStart(const AdvancedProcessInfo& proc);
    void recordProcessEnd(quint32 pid, const QString& reason, int exitCode);
    
    const std::deque<ProcessHistoryEntry>& history() const { return m_history; }
    void clearHistory();
    
    int maxHistorySize() const { return m_maxHistorySize; }
    void setMaxHistorySize(int size);
    
signals:
    void processEnded(const ProcessHistoryEntry& entry);
    void historyCleared();

private:
    std::deque<ProcessHistoryEntry> m_history;
    std::unordered_map<quint32, AdvancedProcessInfo> m_runningProcesses;
    int m_maxHistorySize{100};
};

/**
 * @brief Advanced Process Monitor with extended capabilities
 */
class AdvancedProcessMonitor : public QObject
{
    Q_OBJECT

public:
    explicit AdvancedProcessMonitor(QObject *parent = nullptr);
    ~AdvancedProcessMonitor() override;

    void refresh();
    void startAutoRefresh(int intervalMs = 1000);
    void stopAutoRefresh();
    
    // Process control
    bool terminateProcess(quint32 pid);
    bool terminateProcessTree(quint32 pid);
    bool suspendProcess(quint32 pid);
    bool resumeProcess(quint32 pid);
    bool setProcessPriority(quint32 pid, int priority);
    bool setProcessAffinity(quint32 pid, quint64 affinityMask);
    
    // Getters
    [[nodiscard]] QAbstractItemModel* model() { return m_proxyModel.get(); }
    [[nodiscard]] AdvancedProcessTreeModel* treeModel() { return m_model.get(); }
    [[nodiscard]] ProcessHistoryManager* historyManager() { return m_historyManager.get(); }
    [[nodiscard]] const std::vector<AdvancedProcessInfo>& processes() const { return m_processes; }
    [[nodiscard]] const AdvancedProcessInfo* getProcessByPid(quint32 pid) const;
    
    // Process tree
    std::vector<quint32> getChildProcesses(quint32 parentPid) const;
    std::vector<quint32> getProcessAncestors(quint32 pid) const;
    
    // Statistics
    int totalProcessCount() const { return static_cast<int>(m_processes.size()); }
    int totalThreadCount() const;
    double totalCpuUsage() const;
    qint64 totalMemoryUsage() const;
    
    // Grouping
    void setGroupingMode(AdvancedProcessTreeModel::GroupingMode mode);
    void setShowSystemProcesses(bool show);

public slots:
    void setFilter(const QString& filter);

signals:
    void aboutToRefresh();      // Emitted BEFORE model update - use to save selection
    void processesUpdated();    // Emitted AFTER model update - use to restore selection
    void processStarted(quint32 pid, const QString& name);
    void processEnded(quint32 pid, const QString& name);

private:
    void queryProcesses();
    void detectNewAndEndedProcesses();
    QString getProcessDescription(const QString& exePath);
#ifdef _WIN32
    QString getProcessCommandLine(HANDLE hProcess);
    QString getProcessUserName(HANDLE hProcess);
#endif
    ProcessCategory categorizeProcess(const AdvancedProcessInfo& proc);
    bool isProcessResponding(quint32 pid);
    QIcon getProcessIcon(const QString& exePath);
    
#ifdef _WIN32
    bool suspendResumeProcess(quint32 pid, bool suspend);
    
    struct ProcessTimes {
        FILETIME kernelTime;
        FILETIME userTime;
        qint64 ioReadBytes;
        qint64 ioWriteBytes;
    };
    std::unordered_map<quint32, ProcessTimes> m_previousTimes;
    FILETIME m_lastSystemKernelTime{};
    FILETIME m_lastSystemUserTime{};
#endif

    std::vector<AdvancedProcessInfo> m_processes;
    std::unordered_map<quint32, AdvancedProcessInfo> m_previousProcesses;
    
    std::unique_ptr<AdvancedProcessTreeModel> m_model;
    std::unique_ptr<AdvancedProcessSortFilterProxy> m_proxyModel;
    std::unique_ptr<ProcessHistoryManager> m_historyManager;
    std::unique_ptr<QTimer> m_refreshTimer;
    
    mutable std::unordered_map<QString, QIcon> m_iconCache;
};
