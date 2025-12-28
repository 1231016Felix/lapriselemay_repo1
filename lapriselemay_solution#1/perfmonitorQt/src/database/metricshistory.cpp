#include "metricshistory.h"

#include <QSqlError>
#include <QSqlRecord>
#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QJsonDocument>
#include <QJsonObject>
#include <QJsonArray>
#include <QTextStream>
#include <QStandardPaths>
#include <QTimer>
#include <QDebug>

#include <algorithm>
#include <cmath>

MetricsHistory::MetricsHistory(QObject *parent)
    : QObject(parent)
    , m_lastFlush(QDateTime::currentDateTime())
{
}

MetricsHistory::~MetricsHistory()
{
    if (m_isReady) {
        flush();
        m_db.close();
    }
}

bool MetricsHistory::initialize(const QString& dbPath)
{
    // Determine database path
    if (dbPath.isEmpty()) {
        QString dataDir = QStandardPaths::writableLocation(QStandardPaths::AppDataLocation);
        QDir().mkpath(dataDir);
        m_dbPath = dataDir + "/metrics_history.db";
    } else {
        m_dbPath = dbPath;
    }
    
    // Open database
    m_db = QSqlDatabase::addDatabase("QSQLITE", "MetricsHistoryConnection");
    m_db.setDatabaseName(m_dbPath);
    
    if (!m_db.open()) {
        emit databaseError(QString("Failed to open database: %1").arg(m_db.lastError().text()));
        return false;
    }
    
    // Create tables and indexes
    if (!createTables() || !createIndexes()) {
        return false;
    }
    
    m_isReady = true;
    
    // Schedule periodic maintenance
    QTimer* maintenanceTimer = new QTimer(this);
    connect(maintenanceTimer, &QTimer::timeout, this, &MetricsHistory::performMaintenance);
    maintenanceTimer->start(3600000); // Every hour
    
    // Schedule periodic flush
    QTimer* flushTimer = new QTimer(this);
    connect(flushTimer, &QTimer::timeout, this, &MetricsHistory::flush);
    flushTimer->start(m_flushIntervalMs);
    
    qDebug() << "MetricsHistory initialized:" << m_dbPath;
    return true;
}

bool MetricsHistory::createTables()
{
    QSqlQuery query(m_db);
    
    // Main metrics table with partitioning by type
    QString createMetricsTable = R"(
        CREATE TABLE IF NOT EXISTS metrics (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            metric_type INTEGER NOT NULL,
            timestamp INTEGER NOT NULL,
            value REAL NOT NULL,
            label TEXT DEFAULT '',
            UNIQUE(metric_type, timestamp, label)
        )
    )";
    
    if (!query.exec(createMetricsTable)) {
        emit databaseError(QString("Failed to create metrics table: %1").arg(query.lastError().text()));
        return false;
    }
    
    // Aggregated data table (hourly summaries)
    QString createAggregatesTable = R"(
        CREATE TABLE IF NOT EXISTS metrics_hourly (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            metric_type INTEGER NOT NULL,
            hour_timestamp INTEGER NOT NULL,
            label TEXT DEFAULT '',
            min_value REAL,
            max_value REAL,
            avg_value REAL,
            sample_count INTEGER,
            UNIQUE(metric_type, hour_timestamp, label)
        )
    )";
    
    if (!query.exec(createAggregatesTable)) {
        emit databaseError(QString("Failed to create aggregates table: %1").arg(query.lastError().text()));
        return false;
    }
    
    // Daily aggregates
    QString createDailyTable = R"(
        CREATE TABLE IF NOT EXISTS metrics_daily (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            metric_type INTEGER NOT NULL,
            day_timestamp INTEGER NOT NULL,
            label TEXT DEFAULT '',
            min_value REAL,
            max_value REAL,
            avg_value REAL,
            sample_count INTEGER,
            UNIQUE(metric_type, day_timestamp, label)
        )
    )";
    
    if (!query.exec(createDailyTable)) {
        emit databaseError(QString("Failed to create daily table: %1").arg(query.lastError().text()));
        return false;
    }
    
    // Metadata table for settings
    QString createMetadataTable = R"(
        CREATE TABLE IF NOT EXISTS metadata (
            key TEXT PRIMARY KEY,
            value TEXT
        )
    )";
    
    if (!query.exec(createMetadataTable)) {
        emit databaseError(QString("Failed to create metadata table: %1").arg(query.lastError().text()));
        return false;
    }
    
    return true;
}

bool MetricsHistory::createIndexes()
{
    QSqlQuery query(m_db);
    
    // Index for efficient time-based queries
    query.exec("CREATE INDEX IF NOT EXISTS idx_metrics_type_time ON metrics(metric_type, timestamp)");
    query.exec("CREATE INDEX IF NOT EXISTS idx_metrics_type_label_time ON metrics(metric_type, label, timestamp)");
    query.exec("CREATE INDEX IF NOT EXISTS idx_hourly_type_time ON metrics_hourly(metric_type, hour_timestamp)");
    query.exec("CREATE INDEX IF NOT EXISTS idx_daily_type_time ON metrics_daily(metric_type, day_timestamp)");
    
    return true;
}

void MetricsHistory::recordMetric(MetricType type, double value, const QString& label)
{
    if (!m_isReady) return;
    
    QDateTime now = QDateTime::currentDateTime();
    
    // Check recording interval
    auto key = std::make_pair(type, label);
    if (m_lastRecordTimes.contains(key)) {
        qint64 elapsedSec = m_lastRecordTimes[key].secsTo(now);
        if (elapsedSec < m_recordingIntervalSec) {
            return;  // Skip this sample
        }
    }
    m_lastRecordTimes[key] = now;
    
    // Add to buffer
    {
        QMutexLocker locker(&m_bufferMutex);
        m_writeBuffer.emplace_back(type, value, label, now);
    }
    
    // Check if we should flush
    if (m_lastFlush.msecsTo(now) >= m_flushIntervalMs) {
        flush();
    }
}

void MetricsHistory::recordMetrics(const std::vector<std::tuple<MetricType, double, QString>>& metrics)
{
    for (const auto& [type, value, label] : metrics) {
        recordMetric(type, value, label);
    }
}

void MetricsHistory::flush()
{
    if (!m_isReady) return;
    
    std::vector<std::tuple<MetricType, double, QString, QDateTime>> buffer;
    {
        QMutexLocker locker(&m_bufferMutex);
        buffer.swap(m_writeBuffer);
    }
    
    if (buffer.empty()) return;
    
    m_db.transaction();
    
    QSqlQuery query(m_db);
    query.prepare(R"(
        INSERT OR REPLACE INTO metrics (metric_type, timestamp, value, label)
        VALUES (?, ?, ?, ?)
    )");
    
    for (const auto& [type, value, label, timestamp] : buffer) {
        query.addBindValue(static_cast<int>(type));
        query.addBindValue(timestamp.toSecsSinceEpoch());
        query.addBindValue(value);
        query.addBindValue(label);
        query.exec();
    }
    
    m_db.commit();
    m_lastFlush = QDateTime::currentDateTime();
    
    emit dataRecorded(static_cast<int>(buffer.size()));
}

std::vector<MetricDataPoint> MetricsHistory::getMetricData(
    MetricType type,
    const QDateTime& from,
    const QDateTime& to,
    const QString& label,
    int maxPoints)
{
    std::vector<MetricDataPoint> result;
    if (!m_isReady) return result;
    
    QSqlQuery query(m_db);
    
    QString sql = R"(
        SELECT timestamp, value, label FROM metrics
        WHERE metric_type = ? AND timestamp >= ? AND timestamp <= ?
    )";
    
    if (!label.isEmpty()) {
        sql += " AND label = ?";
    }
    sql += " ORDER BY timestamp ASC";
    
    query.prepare(sql);
    query.addBindValue(static_cast<int>(type));
    query.addBindValue(from.toSecsSinceEpoch());
    query.addBindValue(to.toSecsSinceEpoch());
    if (!label.isEmpty()) {
        query.addBindValue(label);
    }
    
    if (!query.exec()) {
        emit databaseError(query.lastError().text());
        return result;
    }
    
    while (query.next()) {
        MetricDataPoint point;
        point.timestamp = QDateTime::fromSecsSinceEpoch(query.value(0).toLongLong());
        point.value = query.value(1).toDouble();
        point.label = query.value(2).toString();
        result.push_back(point);
    }
    
    // Downsample if necessary
    if (static_cast<int>(result.size()) > maxPoints) {
        result = downsample(result, maxPoints);
    }
    
    return result;
}

std::vector<MetricDataPoint> MetricsHistory::getMetricData(
    MetricType type,
    TimeRange range,
    const QString& label,
    int maxPoints)
{
    auto [from, to] = timeRangeToDateTime(range);
    return getMetricData(type, from, to, label, maxPoints);
}

std::vector<MetricAggregate> MetricsHistory::getAggregatedData(
    MetricType type,
    const QDateTime& from,
    const QDateTime& to,
    int bucketMinutes,
    const QString& label)
{
    std::vector<MetricAggregate> result;
    if (!m_isReady) return result;
    
    qint64 bucketSeconds = bucketMinutes * 60;
    
    QSqlQuery query(m_db);
    QString sql = R"(
        SELECT 
            (timestamp / ?) * ? as bucket_start,
            MIN(value) as min_val,
            MAX(value) as max_val,
            AVG(value) as avg_val,
            COUNT(*) as cnt
        FROM metrics
        WHERE metric_type = ? AND timestamp >= ? AND timestamp <= ?
    )";
    
    if (!label.isEmpty()) {
        sql += " AND label = ?";
    }
    sql += " GROUP BY bucket_start ORDER BY bucket_start ASC";
    
    query.prepare(sql);
    query.addBindValue(bucketSeconds);
    query.addBindValue(bucketSeconds);
    query.addBindValue(static_cast<int>(type));
    query.addBindValue(from.toSecsSinceEpoch());
    query.addBindValue(to.toSecsSinceEpoch());
    if (!label.isEmpty()) {
        query.addBindValue(label);
    }
    
    if (!query.exec()) {
        emit databaseError(query.lastError().text());
        return result;
    }
    
    while (query.next()) {
        MetricAggregate agg;
        qint64 bucketStart = query.value(0).toLongLong();
        agg.periodStart = QDateTime::fromSecsSinceEpoch(bucketStart);
        agg.periodEnd = QDateTime::fromSecsSinceEpoch(bucketStart + bucketSeconds);
        agg.minimum = query.value(1).toDouble();
        agg.maximum = query.value(2).toDouble();
        agg.average = query.value(3).toDouble();
        agg.sampleCount = query.value(4).toInt();
        result.push_back(agg);
    }
    
    return result;
}

QStringList MetricsHistory::getLabelsForMetric(MetricType type)
{
    QStringList labels;
    if (!m_isReady) return labels;
    
    QSqlQuery query(m_db);
    query.prepare("SELECT DISTINCT label FROM metrics WHERE metric_type = ? AND label != ''");
    query.addBindValue(static_cast<int>(type));
    
    if (query.exec()) {
        while (query.next()) {
            labels.append(query.value(0).toString());
        }
    }
    
    return labels;
}

std::pair<QDateTime, QDateTime> MetricsHistory::getDataTimeRange(MetricType type)
{
    if (!m_isReady) return {QDateTime(), QDateTime()};
    
    QSqlQuery query(m_db);
    query.prepare("SELECT MIN(timestamp), MAX(timestamp) FROM metrics WHERE metric_type = ?");
    query.addBindValue(static_cast<int>(type));
    
    if (query.exec() && query.next()) {
        return {
            QDateTime::fromSecsSinceEpoch(query.value(0).toLongLong()),
            QDateTime::fromSecsSinceEpoch(query.value(1).toLongLong())
        };
    }
    
    return {QDateTime(), QDateTime()};
}

PeriodComparison MetricsHistory::comparePeriods(
    MetricType type,
    const QDateTime& period1Start, const QDateTime& period1End,
    const QDateTime& period2Start, const QDateTime& period2End,
    const QString& label)
{
    PeriodComparison result;
    result.metricType = type;
    result.label = label;
    result.period1Start = period1Start;
    result.period1End = period1End;
    result.period2Start = period2Start;
    result.period2End = period2End;
    
    if (!m_isReady) return result;
    
    auto queryPeriodStats = [this, type, &label](const QDateTime& start, const QDateTime& end) 
        -> std::tuple<double, double, double> {
        QSqlQuery query(m_db);
        QString sql = R"(
            SELECT AVG(value), MIN(value), MAX(value) FROM metrics
            WHERE metric_type = ? AND timestamp >= ? AND timestamp <= ?
        )";
        if (!label.isEmpty()) sql += " AND label = ?";
        
        query.prepare(sql);
        query.addBindValue(static_cast<int>(type));
        query.addBindValue(start.toSecsSinceEpoch());
        query.addBindValue(end.toSecsSinceEpoch());
        if (!label.isEmpty()) query.addBindValue(label);
        
        if (query.exec() && query.next()) {
            return {query.value(0).toDouble(), query.value(1).toDouble(), query.value(2).toDouble()};
        }
        return {0.0, 0.0, 0.0};
    };
    
    auto [avg1, min1, max1] = queryPeriodStats(period1Start, period1End);
    auto [avg2, min2, max2] = queryPeriodStats(period2Start, period2End);
    
    result.period1Avg = avg1;
    result.period1Min = min1;
    result.period1Max = max1;
    result.period2Avg = avg2;
    result.period2Min = min2;
    result.period2Max = max2;
    
    result.avgDifference = avg2 - avg1;
    if (std::abs(avg1) > 0.0001) {
        result.avgDifferencePercent = (result.avgDifference / avg1) * 100.0;
    }
    
    return result;
}

PeriodComparison MetricsHistory::compareTodayWithYesterday(MetricType type, const QString& label)
{
    QDateTime now = QDateTime::currentDateTime();
    QDateTime todayStart = QDateTime(now.date(), QTime(0, 0));
    QDateTime yesterdayStart = todayStart.addDays(-1);
    
    return comparePeriods(type, yesterdayStart, todayStart, todayStart, now, label);
}

PeriodComparison MetricsHistory::compareThisWeekWithLastWeek(MetricType type, const QString& label)
{
    QDateTime now = QDateTime::currentDateTime();
    int dayOfWeek = now.date().dayOfWeek();
    QDateTime thisWeekStart = QDateTime(now.date().addDays(-dayOfWeek + 1), QTime(0, 0));
    QDateTime lastWeekStart = thisWeekStart.addDays(-7);
    
    return comparePeriods(type, lastWeekStart, thisWeekStart, thisWeekStart, now, label);
}

// ==================== Export Functions ====================

bool MetricsHistory::exportData(
    const QString& filePath,
    ExportFormat format,
    const QDateTime& from,
    const QDateTime& to,
    const std::vector<MetricType>& metricTypes)
{
    switch (format) {
        case ExportFormat::CSV:
            return exportToCsv(filePath, from, to, metricTypes);
        case ExportFormat::JSON:
            return exportToJson(filePath, from, to, metricTypes);
        case ExportFormat::SQLite:
            // TODO: Export as separate SQLite file
            return false;
    }
    return false;
}

bool MetricsHistory::exportToCsv(const QString& filePath, const QDateTime& from, const QDateTime& to,
                                  const std::vector<MetricType>& types)
{
    if (!m_isReady) return false;
    
    QFile file(filePath);
    if (!file.open(QIODevice::WriteOnly | QIODevice::Text)) {
        emit exportFailed(QString("Cannot open file: %1").arg(filePath));
        return false;
    }
    
    QTextStream out(&file);
    
    // Header
    out << "Timestamp,MetricType,Label,Value\n";
    
    // Determine which types to export
    std::vector<MetricType> exportTypes = types;
    if (exportTypes.empty()) {
        for (int i = 0; i <= static_cast<int>(MetricType::BatteryHealth); ++i) {
            exportTypes.push_back(static_cast<MetricType>(i));
        }
    }
    
    // Query and write data
    for (MetricType type : exportTypes) {
        auto data = getMetricData(type, from, to, QString(), 1000000);
        
        for (const auto& point : data) {
            out << point.timestamp.toString(Qt::ISODate) << ","
                << metricTypeToString(type) << ","
                << "\"" << point.label << "\","
                << point.value << "\n";
        }
    }
    
    file.close();
    emit exportCompleted(filePath);
    return true;
}

bool MetricsHistory::exportToJson(const QString& filePath, const QDateTime& from, const QDateTime& to,
                                   const std::vector<MetricType>& types)
{
    if (!m_isReady) return false;
    
    QJsonObject root;
    root["exportDate"] = QDateTime::currentDateTime().toString(Qt::ISODate);
    root["fromDate"] = from.toString(Qt::ISODate);
    root["toDate"] = to.toString(Qt::ISODate);
    
    std::vector<MetricType> exportTypes = types;
    if (exportTypes.empty()) {
        for (int i = 0; i <= static_cast<int>(MetricType::BatteryHealth); ++i) {
            exportTypes.push_back(static_cast<MetricType>(i));
        }
    }
    
    QJsonObject metricsObj;
    
    for (MetricType type : exportTypes) {
        auto data = getMetricData(type, from, to, QString(), 1000000);
        
        QJsonArray dataArray;
        for (const auto& point : data) {
            QJsonObject pointObj;
            pointObj["timestamp"] = point.timestamp.toString(Qt::ISODate);
            pointObj["value"] = point.value;
            if (!point.label.isEmpty()) {
                pointObj["label"] = point.label;
            }
            dataArray.append(pointObj);
        }
        
        metricsObj[metricTypeToString(type)] = dataArray;
    }
    
    root["metrics"] = metricsObj;
    
    QFile file(filePath);
    if (!file.open(QIODevice::WriteOnly)) {
        emit exportFailed(QString("Cannot open file: %1").arg(filePath));
        return false;
    }
    
    QJsonDocument doc(root);
    file.write(doc.toJson(QJsonDocument::Indented));
    file.close();
    
    emit exportCompleted(filePath);
    return true;
}

// ==================== Maintenance ====================

qint64 MetricsHistory::databaseSize() const
{
    return QFileInfo(m_dbPath).size();
}

qint64 MetricsHistory::totalRecordCount() const
{
    if (!m_isReady) return 0;
    
    QSqlQuery query(m_db);
    if (query.exec("SELECT COUNT(*) FROM metrics") && query.next()) {
        return query.value(0).toLongLong();
    }
    return 0;
}

void MetricsHistory::purgeOldData(int olderThanDays)
{
    if (!m_isReady) return;
    
    qint64 cutoffTime = QDateTime::currentDateTime().addDays(-olderThanDays).toSecsSinceEpoch();
    
    QSqlQuery query(m_db);
    query.prepare("DELETE FROM metrics WHERE timestamp < ?");
    query.addBindValue(cutoffTime);
    query.exec();
    
    query.prepare("DELETE FROM metrics_hourly WHERE hour_timestamp < ?");
    query.addBindValue(cutoffTime);
    query.exec();
    
    query.prepare("DELETE FROM metrics_daily WHERE day_timestamp < ?");
    query.addBindValue(cutoffTime);
    query.exec();
    
    qDebug() << "Purged data older than" << olderThanDays << "days";
}

void MetricsHistory::compactDatabase()
{
    if (!m_isReady) return;
    
    QSqlQuery query(m_db);
    query.exec("VACUUM");
    qDebug() << "Database compacted";
}

void MetricsHistory::performMaintenance()
{
    purgeOldData(m_retentionDays);
    qDebug() << "Maintenance completed. DB size:" << databaseSize() / 1024 << "KB";
}

// ==================== Utility ====================

std::vector<MetricDataPoint> MetricsHistory::downsample(
    const std::vector<MetricDataPoint>& data,
    int targetPoints)
{
    if (data.size() <= static_cast<size_t>(targetPoints)) {
        return data;
    }
    
    std::vector<MetricDataPoint> result;
    result.reserve(targetPoints);
    
    double step = static_cast<double>(data.size()) / targetPoints;
    
    for (int i = 0; i < targetPoints; ++i) {
        size_t startIdx = static_cast<size_t>(i * step);
        size_t endIdx = static_cast<size_t>((i + 1) * step);
        endIdx = std::min(endIdx, data.size());
        
        if (startIdx >= data.size()) break;
        
        double sum = 0.0;
        for (size_t j = startIdx; j < endIdx; ++j) {
            sum += data[j].value;
        }
        
        MetricDataPoint point;
        size_t midIdx = (startIdx + endIdx) / 2;
        point.timestamp = data[midIdx].timestamp;
        point.value = sum / (endIdx - startIdx);
        point.label = data[startIdx].label;
        result.push_back(point);
    }
    
    return result;
}

QString MetricsHistory::metricTypeToString(MetricType type)
{
    switch (type) {
        case MetricType::CpuUsage: return "cpu_usage";
        case MetricType::CpuTemperature: return "cpu_temperature";
        case MetricType::CpuCoreUsage: return "cpu_core_usage";
        case MetricType::MemoryUsed: return "memory_used";
        case MetricType::MemoryAvailable: return "memory_available";
        case MetricType::MemoryCommit: return "memory_commit";
        case MetricType::GpuUsage: return "gpu_usage";
        case MetricType::GpuMemory: return "gpu_memory";
        case MetricType::GpuTemperature: return "gpu_temperature";
        case MetricType::DiskRead: return "disk_read";
        case MetricType::DiskWrite: return "disk_write";
        case MetricType::NetworkSend: return "network_send";
        case MetricType::NetworkReceive: return "network_receive";
        case MetricType::BatteryPercent: return "battery_percent";
        case MetricType::BatteryHealth: return "battery_health";
    }
    return "unknown";
}

MetricType MetricsHistory::stringToMetricType(const QString& str)
{
    static const QMap<QString, MetricType> map = {
        {"cpu_usage", MetricType::CpuUsage},
        {"cpu_temperature", MetricType::CpuTemperature},
        {"cpu_core_usage", MetricType::CpuCoreUsage},
        {"memory_used", MetricType::MemoryUsed},
        {"memory_available", MetricType::MemoryAvailable},
        {"memory_commit", MetricType::MemoryCommit},
        {"gpu_usage", MetricType::GpuUsage},
        {"gpu_memory", MetricType::GpuMemory},
        {"gpu_temperature", MetricType::GpuTemperature},
        {"disk_read", MetricType::DiskRead},
        {"disk_write", MetricType::DiskWrite},
        {"network_send", MetricType::NetworkSend},
        {"network_receive", MetricType::NetworkReceive},
        {"battery_percent", MetricType::BatteryPercent},
        {"battery_health", MetricType::BatteryHealth}
    };
    return map.value(str, MetricType::CpuUsage);
}

QString MetricsHistory::timeRangeToString(TimeRange range)
{
    switch (range) {
        case TimeRange::Last1Hour: return "Last 1 Hour";
        case TimeRange::Last6Hours: return "Last 6 Hours";
        case TimeRange::Last24Hours: return "Last 24 Hours";
        case TimeRange::Last7Days: return "Last 7 Days";
        case TimeRange::Last30Days: return "Last 30 Days";
        case TimeRange::Custom: return "Custom";
    }
    return "Unknown";
}

std::pair<QDateTime, QDateTime> MetricsHistory::timeRangeToDateTime(TimeRange range)
{
    QDateTime now = QDateTime::currentDateTime();
    QDateTime from;
    
    switch (range) {
        case TimeRange::Last1Hour:
            from = now.addSecs(-3600);
            break;
        case TimeRange::Last6Hours:
            from = now.addSecs(-6 * 3600);
            break;
        case TimeRange::Last24Hours:
            from = now.addDays(-1);
            break;
        case TimeRange::Last7Days:
            from = now.addDays(-7);
            break;
        case TimeRange::Last30Days:
            from = now.addDays(-30);
            break;
        case TimeRange::Custom:
            return {QDateTime(), QDateTime()};
    }
    
    return {from, now};
}
