#pragma once

#include <QObject>
#include <QThread>
#include <QMutex>
#include <QWaitCondition>
#include <memory>
#include <atomic>

#include "../monitors/cpumonitor.h"
#include "../monitors/memorymonitor.h"
#include "../monitors/gpumonitor.h"
#include "../monitors/diskmonitor.h"
#include "../monitors/networkmonitor.h"
#include "../monitors/batterymonitor.h"
#include "../monitors/temperaturemonitor.h"

/**
 * @brief Aggregated monitor data for thread-safe transfer to UI
 */
struct MonitorData {
    // CPU
    CpuInfo cpu;
    
    // Memory
    MemoryInfo memory;
    
    // GPU
    std::vector<GpuInfo> gpus;
    GpuInfo primaryGpu;
    
    // Disk
    std::vector<DiskInfo> disks;
    DiskActivity diskActivity;
    
    // Network
    std::vector<NetworkAdapterInfo> networkAdapters;
    NetworkActivity networkActivity;
    
    // Battery
    BatteryInfo battery;
    
    // Temperature
    TemperatureInfo temperature;
    
    // Timestamp
    qint64 timestamp{0};
};

/**
 * @brief Worker that runs monitor updates in a background thread
 */
class MonitorWorker : public QObject
{
    Q_OBJECT

public:
    explicit MonitorWorker(QObject* parent = nullptr);
    ~MonitorWorker() override;

    /// Start the worker thread
    void start(int intervalMs = 1000);
    
    /// Stop the worker thread
    void stop();
    
    /// Check if running
    bool isRunning() const { return m_running.load(); }
    
    /// Set update interval
    void setInterval(int intervalMs);
    
    /// Force immediate update
    void requestUpdate();

signals:
    /// Emitted when new data is available (connect with Qt::QueuedConnection)
    void dataReady(const MonitorData& data);
    
    /// Emitted on error
    void errorOccurred(const QString& error);

private slots:
    void doWork();

private:
    void initializeMonitors();
    void collectData(MonitorData& data);

    std::unique_ptr<QThread> m_thread;
    
    // Monitors (owned by worker, run in worker thread)
    std::unique_ptr<CpuMonitor> m_cpuMonitor;
    std::unique_ptr<MemoryMonitor> m_memoryMonitor;
    std::unique_ptr<GpuMonitor> m_gpuMonitor;
    std::unique_ptr<DiskMonitor> m_diskMonitor;
    std::unique_ptr<NetworkMonitor> m_networkMonitor;
    std::unique_ptr<BatteryMonitor> m_batteryMonitor;
    std::unique_ptr<TemperatureMonitor> m_temperatureMonitor;
    
    // Thread control
    std::atomic<bool> m_running{false};
    std::atomic<int> m_intervalMs{1000};
    QMutex m_mutex;
    QWaitCondition m_condition;
    bool m_updateRequested{false};
};

// Register MonitorData for queued connections
Q_DECLARE_METATYPE(MonitorData)
