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
#include <QMutex>
#include <QColor>
#include <vector>
#include <memory>
#include <map>
#include <deque>

/**
 * @brief Service state enumeration
 */
enum class ServiceState {
    Unknown = 0,
    Stopped = 1,
    StartPending = 2,
    StopPending = 3,
    Running = 4,
    ContinuePending = 5,
    PausePending = 6,
    Paused = 7
};

/**
 * @brief Service start type enumeration
 */
enum class ServiceStartType {
    Boot = 0,           // Driver started by system loader
    System = 1,         // Driver started during kernel initialization
    Automatic = 2,      // Started automatically at boot
    Manual = 3,         // Started manually
    Disabled = 4,       // Cannot be started
    AutomaticDelayed = 5  // Automatic (Delayed Start)
};

/**
 * @brief Resource usage for a service
 */
struct ServiceResourceUsage {
    quint32 processId{0};
    double cpuUsagePercent{0.0};
    qint64 memoryUsageBytes{0};
    qint64 workingSetBytes{0};
    int threadCount{0};
    int handleCount{0};
    
    // Historical data
    double avgCpuUsage1Min{0.0};
    double avgCpuUsage5Min{0.0};
    qint64 peakMemoryUsage{0};
};

/**
 * @brief Service crash/failure event
 */
struct ServiceCrashEvent {
    QString serviceName;
    QString displayName;
    QDateTime timestamp;
    int eventId{0};
    QString failureReason;
    ServiceState previousState{ServiceState::Unknown};
    int crashCount{0};      // Total crashes in last 24h
    bool wasAutoRestarted{false};
};

/**
 * @brief Complete service information
 */
struct ServiceInfo {
    QString serviceName;        // Internal name (e.g., "wuauserv")
    QString displayName;        // User-friendly name (e.g., "Windows Update")
    QString description;        // Service description
    QString imagePath;          // Executable path
    QString serviceType;        // Win32 own/share, driver, etc.
    QString account;            // Account running the service
    
    ServiceState state{ServiceState::Unknown};
    ServiceStartType startType{ServiceStartType::Manual};
    
    quint32 processId{0};
    bool canStop{false};
    bool canPause{false};
    bool isDelayedStart{false};
    
    // Dependencies
    QStringList dependencies;       // Services this depends on
    QStringList dependents;         // Services that depend on this
    
    // Resource usage (if running)
    ServiceResourceUsage resources;
    
    // Crash history
    int crashCount24h{0};
    QDateTime lastCrashTime;
    
    // Metadata
    QDateTime lastStateChange;
    bool isSystemCritical{false};
    bool isWindowsService{false};  // Part of Windows
    
    // UI helpers
    QString stateString() const;
    QString startTypeString() const;
    QString memoryString() const;
    QColor stateColor() const;
};

/**
 * @brief Filter for service list
 */
struct ServiceFilter {
    QString searchText;
    bool showRunning{true};
    bool showStopped{true};
    bool showDisabled{true};
    bool showDrivers{false};
    bool showWindowsOnly{false};
    bool showThirdPartyOnly{false};
    bool showHighResourceOnly{false};
    double highCpuThreshold{5.0};       // %
    qint64 highMemoryThreshold{100 * 1024 * 1024};  // 100 MB
};

/**
 * @brief Table model for displaying services
 */
class ServiceTableModel : public QAbstractTableModel
{
    Q_OBJECT

public:
    enum Column {
        ColName = 0,
        ColDisplayName,
        ColState,
        ColStartType,
        ColPID,
        ColCPU,
        ColMemory,
        ColDescription,
        ColCount
    };

    explicit ServiceTableModel(QObject* parent = nullptr);
    
    void setServices(const std::vector<ServiceInfo>& services);
    void updateService(const ServiceInfo& service);
    void setFilter(const ServiceFilter& filter);
    const ServiceInfo* getService(int row) const;
    const ServiceInfo* getServiceByName(const QString& name) const;
    
    int rowCount(const QModelIndex& parent = QModelIndex()) const override;
    int columnCount(const QModelIndex& parent = QModelIndex()) const override;
    QVariant data(const QModelIndex& index, int role = Qt::DisplayRole) const override;
    QVariant headerData(int section, Qt::Orientation orientation, int role) const override;
    void sort(int column, Qt::SortOrder order = Qt::AscendingOrder) override;

private:
    void applyFilter();
    QString formatBytes(qint64 bytes) const;
    
    std::vector<ServiceInfo> m_allServices;
    std::vector<ServiceInfo> m_filteredServices;
    ServiceFilter m_filter;
    int m_sortColumn{ColDisplayName};
    Qt::SortOrder m_sortOrder{Qt::AscendingOrder};
};

/**
 * @brief Windows Service Monitor
 * 
 * Monitors Windows services, their resource usage, and crash history.
 * Provides functionality to start, stop, and restart services.
 */
class ServiceMonitor : public QObject
{
    Q_OBJECT

public:
    explicit ServiceMonitor(QObject* parent = nullptr);
    ~ServiceMonitor() override;

    /// Initialize the monitor
    bool initialize();
    
    /// Start automatic refresh
    void startAutoRefresh(int intervalMs = 5000);
    void stopAutoRefresh();
    bool isAutoRefreshing() const;
    
    /// Manual refresh
    void refresh();
    
    /// Get all services
    const std::vector<ServiceInfo>& services() const { return m_services; }
    
    /// Get table model
    ServiceTableModel* model() { return m_model.get(); }
    
    /// Get service by name
    const ServiceInfo* getService(const QString& serviceName) const;
    
    // ==================== Service Control ====================
    
    /// Start a service
    bool startService(const QString& serviceName);
    
    /// Stop a service
    bool stopService(const QString& serviceName);
    
    /// Restart a service (stop then start)
    bool restartService(const QString& serviceName);
    
    /// Pause a service
    bool pauseService(const QString& serviceName);
    
    /// Resume a paused service
    bool resumeService(const QString& serviceName);
    
    /// Change service start type
    bool setStartType(const QString& serviceName, ServiceStartType startType);
    
    // ==================== Resource Monitoring ====================
    
    /// Get services using high resources
    std::vector<ServiceInfo> getHighCpuServices(double threshold = 5.0) const;
    std::vector<ServiceInfo> getHighMemoryServices(qint64 threshold = 100 * 1024 * 1024) const;
    
    /// Get top N services by CPU usage
    std::vector<ServiceInfo> getTopByCpu(int count = 10) const;
    
    /// Get top N services by memory usage
    std::vector<ServiceInfo> getTopByMemory(int count = 10) const;
    
    // ==================== Crash History ====================
    
    /// Get crash events
    const std::deque<ServiceCrashEvent>& crashEvents() const { return m_crashEvents; }
    
    /// Get crash events for a specific service
    std::vector<ServiceCrashEvent> getCrashEvents(const QString& serviceName) const;
    
    /// Get services that crashed in last N hours
    std::vector<ServiceInfo> getRecentlyCrashedServices(int hours = 24) const;
    
    /// Clear crash history
    void clearCrashHistory();
    
    /// Load crash history from event log
    void loadCrashHistoryFromEventLog(int days = 7);
    
    // ==================== Utility ====================
    
    /// Check if running as administrator (required for service control)
    static bool isAdmin();
    
    /// Get service state string
    static QString stateToString(ServiceState state);
    
    /// Get start type string
    static QString startTypeToString(ServiceStartType type);
    
    /// Format bytes to human-readable string
    static QString formatBytes(qint64 bytes);
    
    /// Check if service is Windows built-in
    static bool isWindowsService(const QString& serviceName);
    
    /// Check if service is critical (dangerous to stop)
    static bool isSystemCritical(const QString& serviceName);
    
    /// Get last error message
    QString lastError() const { return m_lastError; }

signals:
    void aboutToRefresh();
    void servicesRefreshed();
    void serviceStateChanged(const QString& serviceName, ServiceState oldState, ServiceState newState);
    void serviceStarted(const QString& serviceName);
    void serviceStopped(const QString& serviceName);
    void serviceRestarted(const QString& serviceName);
    void serviceCrashed(const ServiceCrashEvent& event);
    void highResourceServiceDetected(const QString& serviceName, double cpu, qint64 memory);
    void errorOccurred(const QString& error);
    void operationProgress(const QString& operation, int percent);

private slots:
    void onRefreshTimer();
    void checkForCrashes();

private:
    void enumerateServices();
    void queryServiceDetails(ServiceInfo& service);
    void updateResourceUsage();
    void updateResourceUsageForService(ServiceInfo& service);
    void detectCrashes();
    void loadEventLogCrashes();
    
#ifdef _WIN32
    bool controlService(const QString& serviceName, DWORD control);
    bool waitForServiceState(SC_HANDLE hService, DWORD desiredState, int timeoutMs = 30000);
    void queryServiceConfig(SC_HANDLE hService, ServiceInfo& service);
    void queryServiceStatus(SC_HANDLE hService, ServiceInfo& service);
#endif
    
    void setError(const QString& error);

    std::vector<ServiceInfo> m_services;
    std::unique_ptr<ServiceTableModel> m_model;
    std::deque<ServiceCrashEvent> m_crashEvents;
    
    // Previous states for crash detection
    std::map<QString, ServiceState> m_previousStates;
    std::map<QString, QDateTime> m_lastStateChangeTimes;
    
    // Resource usage history (for averaging)
    std::map<QString, std::deque<double>> m_cpuHistory;
    std::map<QString, std::deque<qint64>> m_memoryHistory;
    
    std::unique_ptr<QTimer> m_refreshTimer;
    std::unique_ptr<QTimer> m_crashCheckTimer;
    
    QString m_lastError;
    bool m_isAdmin{false};
    
    // Constants
    static constexpr int MAX_CPU_HISTORY = 60;      // 5 minutes at 5s intervals
    static constexpr int MAX_CRASH_EVENTS = 1000;
};

// Register types for Qt signals
Q_DECLARE_METATYPE(ServiceState)
Q_DECLARE_METATYPE(ServiceInfo)
Q_DECLARE_METATYPE(ServiceCrashEvent)
