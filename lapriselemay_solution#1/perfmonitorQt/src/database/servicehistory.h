#pragma once

#include <QObject>
#include <QDateTime>
#include <QString>
#include <QSqlDatabase>
#include <QMutex>
#include <vector>
#include <map>
#include <memory>

#include "../monitors/servicemonitor.h"

/**
 * @brief Service resource snapshot for history
 */
struct ServiceResourceSnapshot {
    QString serviceName;
    QString displayName;
    QDateTime timestamp;
    ServiceState state;
    double cpuUsagePercent{0.0};
    qint64 memoryUsageBytes{0};
    int threadCount{0};
    int handleCount{0};
};

/**
 * @brief Service crash event for history
 */
struct ServiceCrashRecord {
    QString serviceName;
    QString displayName;
    QDateTime timestamp;
    QString failureReason;
    ServiceState previousState;
    int eventId{0};
};

/**
 * @brief Aggregated service metrics for a time period
 */
struct ServiceMetricsAggregate {
    QString serviceName;
    QDateTime periodStart;
    QDateTime periodEnd;
    
    // CPU
    double avgCpuUsage{0.0};
    double maxCpuUsage{0.0};
    double minCpuUsage{0.0};
    
    // Memory
    qint64 avgMemoryUsage{0};
    qint64 maxMemoryUsage{0};
    qint64 minMemoryUsage{0};
    
    // Availability
    int runningCount{0};      // Number of samples where service was running
    int totalSamples{0};
    double availabilityPercent{0.0};
    
    // Crashes
    int crashCount{0};
};

/**
 * @brief Service history database manager
 * 
 * Stores and retrieves historical data about Windows services:
 * - Resource usage over time (CPU, memory, threads)
 * - State changes and availability
 * - Crash events
 */
class ServiceHistoryManager : public QObject
{
    Q_OBJECT

public:
    explicit ServiceHistoryManager(QObject* parent = nullptr);
    ~ServiceHistoryManager() override;

    /// Initialize database
    bool initialize(const QString& dbPath = QString());
    
    /// Check if ready
    bool isReady() const { return m_isReady; }
    
    /// Get database path
    QString databasePath() const { return m_dbPath; }

    // ==================== Recording ====================
    
    /// Record current state of all services
    void recordServiceSnapshots(const std::vector<ServiceInfo>& services);
    
    /// Record a single service snapshot
    void recordServiceSnapshot(const ServiceResourceSnapshot& snapshot);
    
    /// Record a crash event
    void recordCrashEvent(const ServiceCrashEvent& event);
    
    /// Flush pending writes
    void flush();

    // ==================== Querying ====================
    
    /// Get resource history for a service
    std::vector<ServiceResourceSnapshot> getServiceHistory(
        const QString& serviceName,
        const QDateTime& from,
        const QDateTime& to,
        int maxSamples = 1000
    );
    
    /// Get aggregated metrics for a service
    ServiceMetricsAggregate getAggregatedMetrics(
        const QString& serviceName,
        const QDateTime& from,
        const QDateTime& to
    );
    
    /// Get aggregated metrics for all services
    std::vector<ServiceMetricsAggregate> getAllServicesAggregates(
        const QDateTime& from,
        const QDateTime& to
    );
    
    /// Get crash history
    std::vector<ServiceCrashRecord> getCrashHistory(
        const QString& serviceName = QString(),  // Empty = all services
        const QDateTime& from = QDateTime(),
        const QDateTime& to = QDateTime(),
        int maxRecords = 1000
    );
    
    /// Get services with most crashes
    std::vector<std::pair<QString, int>> getTopCrashingServices(
        int topN = 10,
        const QDateTime& from = QDateTime(),
        const QDateTime& to = QDateTime()
    );
    
    /// Get services with highest resource usage
    std::vector<std::pair<QString, double>> getTopCpuServices(
        int topN = 10,
        const QDateTime& from = QDateTime(),
        const QDateTime& to = QDateTime()
    );
    
    std::vector<std::pair<QString, qint64>> getTopMemoryServices(
        int topN = 10,
        const QDateTime& from = QDateTime(),
        const QDateTime& to = QDateTime()
    );
    
    /// Get service availability percentage
    double getServiceAvailability(
        const QString& serviceName,
        const QDateTime& from,
        const QDateTime& to
    );
    
    /// Get all recorded services
    QStringList getAllRecordedServices();
    
    /// Get time range of available data
    std::pair<QDateTime, QDateTime> getDataTimeRange();

    // ==================== Maintenance ====================
    
    /// Delete old data
    void purgeOldData(int olderThanDays = 30);
    
    /// Compact database
    void compactDatabase();
    
    /// Get database size
    qint64 databaseSize() const;
    
    /// Get total record count
    qint64 totalRecordCount() const;
    
    /// Set retention period
    void setRetentionDays(int days) { m_retentionDays = days; }
    int retentionDays() const { return m_retentionDays; }

    // ==================== Export ====================
    
    /// Export to CSV
    bool exportToCsv(
        const QString& filePath,
        const QString& serviceName,
        const QDateTime& from,
        const QDateTime& to
    );
    
    /// Export to JSON
    bool exportToJson(
        const QString& filePath,
        const QString& serviceName,
        const QDateTime& from,
        const QDateTime& to
    );

signals:
    void dataRecorded(int count);
    void crashRecorded(const QString& serviceName);
    void databaseError(const QString& error);

private:
    bool createTables();
    bool createIndexes();
    void scheduleFlush();
    void performMaintenance();

    QSqlDatabase m_db;
    QString m_dbPath;
    bool m_isReady{false};
    
    // Buffered writes
    std::vector<ServiceResourceSnapshot> m_snapshotBuffer;
    std::vector<ServiceCrashRecord> m_crashBuffer;
    QMutex m_bufferMutex;
    QDateTime m_lastFlush;
    int m_flushIntervalMs{5000};
    
    // Settings
    int m_retentionDays{30};
    int m_recordingIntervalSec{5};
    
    // Track last record time per service
    std::map<QString, QDateTime> m_lastRecordTimes;
};
