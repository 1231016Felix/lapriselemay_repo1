#pragma once

#include <QObject>
#include <QString>
#include <QDateTime>
#include <QVariant>
#include <QSqlDatabase>
#include <QSqlQuery>
#include <QMutex>
#include <vector>
#include <memory>
#include <optional>

/**
 * @brief Time range for querying historical data
 */
enum class TimeRange {
    Last1Hour,
    Last6Hours,
    Last24Hours,
    Last7Days,
    Last30Days,
    Custom
};

/**
 * @brief Metric type enumeration
 */
enum class MetricType {
    CpuUsage,
    CpuTemperature,
    CpuCoreUsage,
    MemoryUsed,
    MemoryAvailable,
    MemoryCommit,
    GpuUsage,
    GpuMemory,
    GpuTemperature,
    DiskRead,
    DiskWrite,
    NetworkSend,
    NetworkReceive,
    BatteryPercent,
    BatteryHealth
};

/**
 * @brief Single data point for a metric
 */
struct MetricDataPoint {
    QDateTime timestamp;
    double value{0.0};
    QString label;  // Optional label (e.g., core number, disk name)
};

/**
 * @brief Aggregated data for a time period
 */
struct MetricAggregate {
    QDateTime periodStart;
    QDateTime periodEnd;
    double minimum{0.0};
    double maximum{0.0};
    double average{0.0};
    int sampleCount{0};
};

/**
 * @brief Comparison result between two time periods
 */
struct PeriodComparison {
    MetricType metricType;
    QString label;
    
    // Period 1 stats
    QDateTime period1Start;
    QDateTime period1End;
    double period1Avg{0.0};
    double period1Min{0.0};
    double period1Max{0.0};
    
    // Period 2 stats
    QDateTime period2Start;
    QDateTime period2End;
    double period2Avg{0.0};
    double period2Min{0.0};
    double period2Max{0.0};
    
    // Difference
    double avgDifference{0.0};      // period2 - period1
    double avgDifferencePercent{0.0};
};

/**
 * @brief Export format options
 */
enum class ExportFormat {
    CSV,
    JSON,
    SQLite  // Export as separate SQLite file
};

/**
 * @brief Persistent metrics history using SQLite
 * 
 * Stores system metrics over time with automatic data aggregation
 * and cleanup of old data to manage database size.
 */
class MetricsHistory : public QObject
{
    Q_OBJECT

public:
    explicit MetricsHistory(QObject *parent = nullptr);
    ~MetricsHistory() override;

    /// Initialize database (call once at startup)
    bool initialize(const QString& dbPath = QString());
    
    /// Check if database is ready
    bool isReady() const { return m_isReady; }
    
    /// Get database path
    QString databasePath() const { return m_dbPath; }
    
    // ==================== Recording ====================
    
    /// Record a single metric value
    void recordMetric(MetricType type, double value, const QString& label = QString());
    
    /// Record multiple metrics at once (more efficient)
    void recordMetrics(const std::vector<std::tuple<MetricType, double, QString>>& metrics);
    
    /// Flush pending writes to database
    void flush();
    
    // ==================== Querying ====================
    
    /// Get raw data points for a metric
    std::vector<MetricDataPoint> getMetricData(
        MetricType type,
        const QDateTime& from,
        const QDateTime& to,
        const QString& label = QString(),
        int maxPoints = 1000  // Downsample if more points
    );
    
    /// Get data for predefined time range
    std::vector<MetricDataPoint> getMetricData(
        MetricType type,
        TimeRange range,
        const QString& label = QString(),
        int maxPoints = 1000
    );
    
    /// Get aggregated data (for long time ranges)
    std::vector<MetricAggregate> getAggregatedData(
        MetricType type,
        const QDateTime& from,
        const QDateTime& to,
        int bucketMinutes = 60,  // Aggregate into N-minute buckets
        const QString& label = QString()
    );
    
    /// Get available labels for a metric type (e.g., disk names, core numbers)
    QStringList getLabelsForMetric(MetricType type);
    
    /// Get time range of available data
    std::pair<QDateTime, QDateTime> getDataTimeRange(MetricType type);
    
    // ==================== Comparison ====================
    
    /// Compare two time periods
    PeriodComparison comparePeriods(
        MetricType type,
        const QDateTime& period1Start, const QDateTime& period1End,
        const QDateTime& period2Start, const QDateTime& period2End,
        const QString& label = QString()
    );
    
    /// Compare today with yesterday
    PeriodComparison compareTodayWithYesterday(MetricType type, const QString& label = QString());
    
    /// Compare this week with last week
    PeriodComparison compareThisWeekWithLastWeek(MetricType type, const QString& label = QString());
    
    // ==================== Export ====================
    
    /// Export data to file
    bool exportData(
        const QString& filePath,
        ExportFormat format,
        const QDateTime& from,
        const QDateTime& to,
        const std::vector<MetricType>& metricTypes = {}  // Empty = all
    );
    
    /// Export to CSV
    bool exportToCsv(const QString& filePath, const QDateTime& from, const QDateTime& to,
                     const std::vector<MetricType>& types);
    
    /// Export to JSON
    bool exportToJson(const QString& filePath, const QDateTime& from, const QDateTime& to,
                      const std::vector<MetricType>& types);
    
    // ==================== Maintenance ====================
    
    /// Get database size in bytes
    qint64 databaseSize() const;
    
    /// Get total record count
    qint64 totalRecordCount() const;
    
    /// Delete data older than specified days
    void purgeOldData(int olderThanDays = 30);
    
    /// Compact database (VACUUM)
    void compactDatabase();
    
    /// Set data retention period
    void setRetentionDays(int days) { m_retentionDays = days; }
    int retentionDays() const { return m_retentionDays; }
    
    /// Set recording interval (minimum time between samples)
    void setRecordingInterval(int seconds) { m_recordingIntervalSec = seconds; }
    int recordingInterval() const { return m_recordingIntervalSec; }
    
    // ==================== Utility ====================
    
    static QString metricTypeToString(MetricType type);
    static MetricType stringToMetricType(const QString& str);
    static QString timeRangeToString(TimeRange range);
    static std::pair<QDateTime, QDateTime> timeRangeToDateTime(TimeRange range);

signals:
    void databaseError(const QString& error);
    void dataRecorded(int count);
    void exportCompleted(const QString& filePath);
    void exportFailed(const QString& error);

private:
    bool createTables();
    bool createIndexes();
    void scheduleFlush();
    void performMaintenance();
    QString getTableName(MetricType type);
    
    // Downsampling for large datasets
    std::vector<MetricDataPoint> downsample(
        const std::vector<MetricDataPoint>& data,
        int targetPoints
    );

    QSqlDatabase m_db;
    QString m_dbPath;
    bool m_isReady{false};
    
    // Buffered writes for efficiency
    std::vector<std::tuple<MetricType, double, QString, QDateTime>> m_writeBuffer;
    QMutex m_bufferMutex;
    int m_flushIntervalMs{5000};  // Flush every 5 seconds
    QDateTime m_lastFlush;
    
    // Settings
    int m_retentionDays{30};
    int m_recordingIntervalSec{1};
    
    // Last recorded times (to respect recording interval)
    std::map<std::pair<MetricType, QString>, QDateTime> m_lastRecordTimes;
};
