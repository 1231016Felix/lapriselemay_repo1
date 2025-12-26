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
#include <QAbstractTableModel>
#include <QString>
#include <QDateTime>
#include <QTimer>
#include <vector>
#include <deque>
#include <unordered_map>
#include <memory>

/**
 * @brief Detailed memory information for a single process
 */
struct ProcessMemoryInfo {
    quint32 pid{0};
    QString name;
    QString executablePath;
    
    // Memory metrics (in bytes)
    qint64 workingSetSize{0};           // Physical memory currently in use
    qint64 privateWorkingSet{0};        // Private physical memory (not shared)
    qint64 sharedWorkingSet{0};         // Shared physical memory
    qint64 peakWorkingSet{0};           // Maximum working set since start
    
    qint64 privateBytes{0};             // Committed private memory (physical + paged)
    qint64 virtualBytes{0};             // Total virtual address space reserved
    qint64 peakVirtualBytes{0};         // Peak virtual memory
    
    qint64 pagedPoolBytes{0};           // Paged pool usage
    qint64 nonPagedPoolBytes{0};        // Non-paged pool usage
    
    qint64 pageFaultCount{0};           // Number of page faults
    qint64 pageFaultsDelta{0};          // Page faults since last update
    
    // Memory leak detection
    qint64 privateBytesDelta{0};        // Change since last sample
    qint64 workingSetDelta{0};          // Change since last sample
    double growthRateMBPerMin{0.0};     // Average growth rate
    int consecutiveGrowthCount{0};      // How many samples in a row it grew
    bool isPotentialLeak{false};        // Flagged as potential leak
    
    // Timestamps
    QDateTime processStartTime;
    QDateTime lastUpdated;
};

/**
 * @brief System-wide detailed memory information
 */
struct SystemMemoryDetails {
    // Physical memory
    qint64 totalPhysical{0};
    qint64 availablePhysical{0};
    qint64 usedPhysical{0};
    
    // Commit charge
    qint64 commitTotal{0};
    qint64 commitLimit{0};
    qint64 commitPeak{0};
    
    // Kernel pools
    qint64 kernelPaged{0};
    qint64 kernelNonPaged{0};
    qint64 kernelTotal{0};
    
    // System cache
    qint64 systemCache{0};
    qint64 systemCacheTransition{0};
    qint64 standbyCache{0};
    qint64 modifiedPages{0};
    qint64 freePages{0};
    qint64 zeroPages{0};
    
    // Page file
    qint64 pageFileTotal{0};
    qint64 pageFileUsed{0};
    
    // Counts
    qint64 handleCount{0};
    qint64 processCount{0};
    qint64 threadCount{0};
    
    // Page size
    qint64 pageSize{4096};
};

/**
 * @brief Memory snapshot for historical tracking
 */
struct MemorySnapshot {
    QDateTime timestamp;
    qint64 usedPhysical{0};
    qint64 commitCharge{0};
    qint64 systemCache{0};
    std::unordered_map<quint32, qint64> processPrivateBytes;
};

/**
 * @brief Table model for displaying process memory details
 */
class ProcessMemoryModel : public QAbstractTableModel
{
    Q_OBJECT

public:
    enum Column {
        ColName = 0,
        ColPID,
        ColWorkingSet,
        ColPrivateWS,
        ColSharedWS,
        ColPrivateBytes,
        ColVirtualBytes,
        ColPageFaults,
        ColGrowthRate,
        ColLeakStatus,
        ColCount
    };

    explicit ProcessMemoryModel(QObject *parent = nullptr);
    
    void setProcesses(const std::vector<ProcessMemoryInfo>& processes);
    const ProcessMemoryInfo* getProcess(int row) const;
    
    int rowCount(const QModelIndex &parent = QModelIndex()) const override;
    int columnCount(const QModelIndex &parent = QModelIndex()) const override;
    QVariant data(const QModelIndex &index, int role = Qt::DisplayRole) const override;
    QVariant headerData(int section, Qt::Orientation orientation, int role) const override;
    void sort(int column, Qt::SortOrder order = Qt::AscendingOrder) override;

private:
    QString formatBytes(qint64 bytes) const;
    QString formatGrowthRate(double mbPerMin) const;
    
    std::vector<ProcessMemoryInfo> m_processes;
    int m_sortColumn{ColPrivateBytes};
    Qt::SortOrder m_sortOrder{Qt::DescendingOrder};
};

/**
 * @brief Detailed Memory Monitor with leak detection
 */
class DetailedMemoryMonitor : public QObject
{
    Q_OBJECT

public:
    explicit DetailedMemoryMonitor(QObject *parent = nullptr);
    ~DetailedMemoryMonitor() override;

    /// Refresh all memory data
    void refresh();
    
    /// Start/stop automatic refresh
    void startAutoRefresh(int intervalMs = 2000);
    void stopAutoRefresh();
    bool isAutoRefreshing() const;
    
    /// Get system memory details
    [[nodiscard]] const SystemMemoryDetails& systemMemory() const { return m_systemMemory; }
    
    /// Get process memory list
    [[nodiscard]] const std::vector<ProcessMemoryInfo>& processes() const { return m_processes; }
    [[nodiscard]] QAbstractTableModel* model() { return m_model.get(); }
    
    /// Get specific process memory info
    [[nodiscard]] const ProcessMemoryInfo* getProcessByPid(quint32 pid) const;
    
    /// Get top memory consumers
    [[nodiscard]] std::vector<ProcessMemoryInfo> getTopByWorkingSet(int count = 10) const;
    [[nodiscard]] std::vector<ProcessMemoryInfo> getTopByPrivateBytes(int count = 10) const;
    [[nodiscard]] std::vector<ProcessMemoryInfo> getPotentialLeaks() const;
    
    /// Memory history
    [[nodiscard]] const std::deque<MemorySnapshot>& history() const { return m_history; }
    [[nodiscard]] int historySize() const { return static_cast<int>(m_history.size()); }
    void setMaxHistorySize(int size);
    void clearHistory();
    
    /// Leak detection settings
    void setLeakDetectionEnabled(bool enabled);
    bool isLeakDetectionEnabled() const { return m_leakDetectionEnabled; }
    void setLeakThresholdMBPerMin(double threshold);
    void setMinConsecutiveGrowth(int count);
    
    /// Utility functions
    static QString formatBytes(qint64 bytes);
    static QString formatBytesShort(qint64 bytes);

signals:
    void refreshed();
    void potentialLeakDetected(quint32 pid, const QString& processName, double growthRate);
    void systemMemoryLow(double usagePercent);

private:
    void querySystemMemory();
    void queryProcessMemory();
    void updateLeakDetection(ProcessMemoryInfo& proc);
    void takeSnapshot();
    void checkSystemMemoryThresholds();
    
#ifdef _WIN32
    bool queryProcessMemoryInfo(HANDLE hProcess, ProcessMemoryInfo& info);
    void queryExtendedMemoryInfo();
#endif

    SystemMemoryDetails m_systemMemory;
    std::vector<ProcessMemoryInfo> m_processes;
    std::unique_ptr<ProcessMemoryModel> m_model;
    
    // Historical data
    std::deque<MemorySnapshot> m_history;
    int m_maxHistorySize{180}; // 6 minutes at 2s intervals
    
    // Previous values for delta calculation
    std::unordered_map<quint32, ProcessMemoryInfo> m_previousProcesses;
    
    // Leak detection
    bool m_leakDetectionEnabled{true};
    double m_leakThresholdMBPerMin{10.0};
    int m_minConsecutiveGrowth{5};
    
    // Auto-refresh
    std::unique_ptr<QTimer> m_refreshTimer;
    
    // System memory warning threshold
    double m_lowMemoryThreshold{90.0};
    bool m_lowMemoryWarningIssued{false};
};
