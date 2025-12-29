#pragma once

#include <QObject>
#include <QString>
#include <QDateTime>
#include <QTimer>
#include <QMutex>
#include <vector>
#include <map>
#include <deque>
#include <memory>
#include <optional>

#ifdef _WIN32
#include <Windows.h>
#endif

#include <QIcon>

/**
 * @brief Single sample of process resource usage
 */
struct ProcessSample {
    QDateTime timestamp;
    double cpuPercent{0.0};
    qint64 memoryBytes{0};
    qint64 diskReadBytes{0};
    qint64 diskWriteBytes{0};
    qint64 networkSentBytes{0};
    qint64 networkRecvBytes{0};
};

/**
 * @brief Aggregated impact data for a process over time
 */
struct ProcessImpact {
    // Process identification
    DWORD pid{0};
    QString name;
    QString displayName;        // User-friendly name
    QString executablePath;
    QString description;
    QIcon icon;                 // Process icon
    QDateTime firstSeen;
    QDateTime lastSeen;
    
    // CPU Impact
    double avgCpuPercent{0.0};
    double peakCpuPercent{0.0};
    double totalCpuTimeSeconds{0.0};
    double totalCpuSeconds{0.0}; // Alias for totalCpuTimeSeconds
    int cpuSpikeCount{0};  // Times CPU > 50%
    
    // Memory Impact
    qint64 currentMemoryBytes{0};
    qint64 peakMemoryBytes{0};
    qint64 avgMemoryBytes{0};
    qint64 memoryGrowth{0};     // Memory increase over time
    
    // Disk Impact
    qint64 totalDiskReadBytes{0};
    qint64 totalDiskWriteBytes{0};
    qint64 totalReadBytes{0};   // Alias for totalDiskReadBytes
    qint64 totalWriteBytes{0};  // Alias for totalDiskWriteBytes
    qint64 avgDiskReadBytesPerSec{0};
    qint64 avgDiskWriteBytesPerSec{0};
    qint64 avgReadBytesPerSec{0};   // Alias
    qint64 avgWriteBytesPerSec{0};  // Alias
    qint64 peakDiskReadBytesPerSec{0};
    qint64 peakDiskWriteBytesPerSec{0};
    qint64 peakReadBytesPerSec{0};  // Alias
    qint64 peakWriteBytesPerSec{0}; // Alias
    double diskImpactScore{0.0};
    
    // Network Impact
    qint64 totalNetworkSentBytes{0};
    qint64 totalNetworkRecvBytes{0};
    qint64 avgNetworkBytesPerSec{0};
    qint64 peakNetworkBytesPerSec{0};
    
    // GPU Impact
    double avgGpuPercent{0.0};
    double peakGpuPercent{0.0};
    
    // Battery Impact (computed score 0-100)
    double batteryImpactScore{0.0};
    
    // Activity metrics
    double activityPercent{0.0};
    int wakeCount{0};
    double activeSeconds{0.0};
    
    // Overall Impact Score (weighted combination)
    double overallImpactScore{0.0};
    
    // Historical samples (last N minutes)
    std::deque<ProcessSample> samples;
    
    // Status
    bool isRunning{true};
    bool isSystem{false};
    bool isSystemProcess{false}; // Alias for isSystem
    bool isBackground{false};
};

/**
 * @brief Category for sorting/filtering
 */
enum class ImpactCategory {
    BatteryDrainer,
    BatteryDrain,   // Alias for BatteryDrainer
    DiskHog,
    DiskIO,         // Alias for DiskHog
    DiskRead,       // Read-specific
    DiskWrite,      // Write-specific
    MemoryHog,
    MemoryUsage,    // Alias for MemoryHog
    CpuHog,
    CpuUsage,       // Alias for CpuHog
    NetworkHog,
    NetworkUsage,   // Alias for NetworkHog
    GpuUsage,       // GPU usage category
    OverallImpact
};

/**
 * @brief Configuration for the impact monitor
 */
struct ImpactMonitorConfig {
    int sampleIntervalMs{2000};          // How often to sample
    int historyMinutes{5};                // How much history to keep
    int maxTrackedProcesses{100};         // Max processes to track
    double cpuSpikeThreshold{50.0};       // % for spike detection
    bool trackSystemProcesses{false};     // Include system processes
    bool trackBackgroundProcesses{true};  // Include background processes
};

/**
 * @brief Monitors process resource usage and calculates impact scores
 */
class ProcessImpactMonitor : public QObject
{
    Q_OBJECT

public:
    explicit ProcessImpactMonitor(QObject* parent = nullptr);
    ~ProcessImpactMonitor() override;

    /// Start monitoring with optional interval
    void start(int intervalMs = 0);
    
    /// Stop monitoring
    void stop();
    
    /// Check if running
    bool isRunning() const { return m_isRunning; }
    
    /// Get configuration
    ImpactMonitorConfig& config() { return m_config; }
    const ImpactMonitorConfig& config() const { return m_config; }
    
    /// Get all tracked processes
    std::vector<ProcessImpact> getAllProcesses() const;
    
    /// Get all impacts with optional system process filter
    std::vector<ProcessImpact> getAllImpacts(bool includeSystem = false) const;
    
    /// Get top N processes by category
    std::vector<ProcessImpact> getTopProcesses(ImpactCategory category, int count = 5, bool includeSystem = false) const;
    
    /// Get impacts sorted by category
    std::vector<ProcessImpact> getImpactsSorted(ImpactCategory category, bool ascending = false, bool includeSystem = false) const;
    
    /// Get impact data for specific process
    std::optional<ProcessImpact> getProcessImpact(DWORD pid) const;
    
    /// Get historical samples for a process
    std::vector<ProcessSample> getProcessHistory(DWORD pid) const;
    
    /// Clear all tracking data
    void clearHistory();
    
    /// Force immediate update
    void refresh();
    
    /// Recalculate all impact scores
    void recalculateImpacts();
    
    /// Check if system has battery
    bool hasBattery() const { return m_hasBattery; }
    
    /// Get/set analysis window in seconds
    int analysisWindow() const { return m_analysisWindowSecs; }
    void setAnalysisWindow(int seconds);
    
    /// Get window coverage (0.0-1.0)
    double windowCoverage() const;
    
    /// Get total samples collected
    int totalSamples() const { return m_totalSamples; }
    
    /// Format bytes to human-readable string (e.g., "1.5 GB")
    static QString formatBytes(qint64 bytes);
    
    /// Format bytes per second to human-readable string (e.g., "1.5 MB/s")
    static QString formatBytesPerSec(qint64 bytesPerSec);

signals:
    void dataUpdated();
    void impactsUpdated();  // Alias for dataUpdated
    void processStarted(DWORD pid, const QString& name);
    void processStopped(DWORD pid, const QString& name);
    void highImpactDetected(DWORD pid, const QString& name, ImpactCategory category, double score);

private slots:
    void onSampleTimer();

private:
    void initializeBatteryDetection();
    void sampleAllProcesses();
    void updateProcessSample(ProcessImpact& impact);
    void calculateImpactScores(ProcessImpact& impact);
    double calculateBatteryImpact(const ProcessImpact& impact);
    double calculateOverallImpact(const ProcessImpact& impact);
    void pruneOldSamples();
    void pruneDeadProcesses();
    
    bool isSystemProcess(const QString& name, const QString& path);
    bool isBackgroundProcess(DWORD pid);
    QString getProcessDescription(const QString& path);
    
    // Sorting comparators
    static bool compareByCpu(const ProcessImpact& a, const ProcessImpact& b);
    static bool compareByMemory(const ProcessImpact& a, const ProcessImpact& b);
    static bool compareByDisk(const ProcessImpact& a, const ProcessImpact& b);
    static bool compareByNetwork(const ProcessImpact& a, const ProcessImpact& b);
    static bool compareByBattery(const ProcessImpact& a, const ProcessImpact& b);
    static bool compareByOverall(const ProcessImpact& a, const ProcessImpact& b);

    ImpactMonitorConfig m_config;
    std::map<DWORD, ProcessImpact> m_processes;
    mutable QMutex m_mutex;
    
    QTimer* m_sampleTimer{nullptr};
    bool m_isRunning{false};
    bool m_hasBattery{false};
    int m_analysisWindowSecs{300};  // Default 5 minutes
    int m_totalSamples{0};
    QDateTime m_startTime;
    
    // Previous sample data for delta calculations
    struct PrevProcessData {
        ULARGE_INTEGER cpuTime;
        qint64 diskRead;
        qint64 diskWrite;
        qint64 netSent;
        qint64 netRecv;
        QDateTime timestamp;
    };
    std::map<DWORD, PrevProcessData> m_prevData;
    ULARGE_INTEGER m_prevSystemTime{};
};
