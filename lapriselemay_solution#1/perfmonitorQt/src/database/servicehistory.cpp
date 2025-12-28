#include "servicehistory.h"

#include <QSqlQuery>
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

ServiceHistoryManager::ServiceHistoryManager(QObject* parent)
    : QObject(parent)
    , m_lastFlush(QDateTime::currentDateTime())
{
}

ServiceHistoryManager::~ServiceHistoryManager()
{
    if (m_isReady) {
        flush();
        m_db.close();
    }
}

bool ServiceHistoryManager::initialize(const QString& dbPath)
{
    // Determine database path
    if (dbPath.isEmpty()) {
        QString dataDir = QStandardPaths::writableLocation(QStandardPaths::AppDataLocation);
        QDir().mkpath(dataDir);
        m_dbPath = dataDir + "/service_history.db";
    } else {
        m_dbPath = dbPath;
    }
    
    // Open database with unique connection name
    QString connName = QString("ServiceHistoryConnection_%1").arg(quintptr(this));
    m_db = QSqlDatabase::addDatabase("QSQLITE", connName);
    m_db.setDatabaseName(m_dbPath);
    
    if (!m_db.open()) {
        emit databaseError(QString("Failed to open database: %1").arg(m_db.lastError().text()));
        return false;
    }
    
    if (!createTables() || !createIndexes()) {
        return false;
    }
    
    m_isReady = true;
    
    // Schedule periodic maintenance
    QTimer* maintenanceTimer = new QTimer(this);
    connect(maintenanceTimer, &QTimer::timeout, this, &ServiceHistoryManager::performMaintenance);
    maintenanceTimer->start(3600000); // Every hour
    
    // Schedule periodic flush
    QTimer* flushTimer = new QTimer(this);
    connect(flushTimer, &QTimer::timeout, this, &ServiceHistoryManager::flush);
    flushTimer->start(m_flushIntervalMs);
    
    qDebug() << "ServiceHistoryManager initialized:" << m_dbPath;
    return true;
}

bool ServiceHistoryManager::createTables()
{
    QSqlQuery query(m_db);
    
    // Service snapshots table
    QString createSnapshotsTable = R"(
        CREATE TABLE IF NOT EXISTS service_snapshots (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            service_name TEXT NOT NULL,
            display_name TEXT,
            timestamp INTEGER NOT NULL,
            state INTEGER NOT NULL,
            cpu_usage REAL DEFAULT 0,
            memory_usage INTEGER DEFAULT 0,
            thread_count INTEGER DEFAULT 0,
            handle_count INTEGER DEFAULT 0
        )
    )";
    
    if (!query.exec(createSnapshotsTable)) {
        emit databaseError(QString("Failed to create snapshots table: %1").arg(query.lastError().text()));
        return false;
    }
    
    // Crash events table
    QString createCrashesTable = R"(
        CREATE TABLE IF NOT EXISTS service_crashes (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            service_name TEXT NOT NULL,
            display_name TEXT,
            timestamp INTEGER NOT NULL,
            failure_reason TEXT,
            previous_state INTEGER,
            event_id INTEGER DEFAULT 0
        )
    )";
    
    if (!query.exec(createCrashesTable)) {
        emit databaseError(QString("Failed to create crashes table: %1").arg(query.lastError().text()));
        return false;
    }
    
    // Hourly aggregates table
    QString createHourlyTable = R"(
        CREATE TABLE IF NOT EXISTS service_hourly (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            service_name TEXT NOT NULL,
            hour_timestamp INTEGER NOT NULL,
            avg_cpu REAL,
            max_cpu REAL,
            min_cpu REAL,
            avg_memory INTEGER,
            max_memory INTEGER,
            min_memory INTEGER,
            running_count INTEGER,
            total_samples INTEGER,
            crash_count INTEGER,
            UNIQUE(service_name, hour_timestamp)
        )
    )";
    
    if (!query.exec(createHourlyTable)) {
        emit databaseError(QString("Failed to create hourly table: %1").arg(query.lastError().text()));
        return false;
    }
    
    return true;
}

bool ServiceHistoryManager::createIndexes()
{
    QSqlQuery query(m_db);
    
    query.exec("CREATE INDEX IF NOT EXISTS idx_snapshots_service_time ON service_snapshots(service_name, timestamp)");
    query.exec("CREATE INDEX IF NOT EXISTS idx_snapshots_time ON service_snapshots(timestamp)");
    query.exec("CREATE INDEX IF NOT EXISTS idx_crashes_service_time ON service_crashes(service_name, timestamp)");
    query.exec("CREATE INDEX IF NOT EXISTS idx_crashes_time ON service_crashes(timestamp)");
    query.exec("CREATE INDEX IF NOT EXISTS idx_hourly_service_time ON service_hourly(service_name, hour_timestamp)");
    
    return true;
}

void ServiceHistoryManager::recordServiceSnapshots(const std::vector<ServiceInfo>& services)
{
    QDateTime now = QDateTime::currentDateTime();
    
    for (const auto& svc : services) {
        // Only record running services with resource data
        if (svc.state != ServiceState::Running || svc.processId == 0) {
            continue;
        }
        
        // Check recording interval
        auto it = m_lastRecordTimes.find(svc.serviceName);
        if (it != m_lastRecordTimes.end()) {
            if (it->second.secsTo(now) < m_recordingIntervalSec) {
                continue;
            }
        }
        m_lastRecordTimes[svc.serviceName] = now;
        
        ServiceResourceSnapshot snapshot;
        snapshot.serviceName = svc.serviceName;
        snapshot.displayName = svc.displayName;
        snapshot.timestamp = now;
        snapshot.state = svc.state;
        snapshot.cpuUsagePercent = svc.resources.cpuUsagePercent;
        snapshot.memoryUsageBytes = svc.resources.memoryUsageBytes;
        snapshot.threadCount = svc.resources.threadCount;
        snapshot.handleCount = svc.resources.handleCount;
        
        recordServiceSnapshot(snapshot);
    }
}

void ServiceHistoryManager::recordServiceSnapshot(const ServiceResourceSnapshot& snapshot)
{
    QMutexLocker locker(&m_bufferMutex);
    m_snapshotBuffer.push_back(snapshot);
    
    // Check if we should flush
    if (m_lastFlush.msecsTo(QDateTime::currentDateTime()) >= m_flushIntervalMs) {
        locker.unlock();
        flush();
    }
}

void ServiceHistoryManager::recordCrashEvent(const ServiceCrashEvent& event)
{
    ServiceCrashRecord record;
    record.serviceName = event.serviceName;
    record.displayName = event.displayName;
    record.timestamp = event.timestamp;
    record.failureReason = event.failureReason;
    record.previousState = event.previousState;
    record.eventId = event.eventId;
    
    {
        QMutexLocker locker(&m_bufferMutex);
        m_crashBuffer.push_back(record);
    }
    
    emit crashRecorded(event.serviceName);
}

void ServiceHistoryManager::flush()
{
    if (!m_isReady) return;
    
    std::vector<ServiceResourceSnapshot> snapshots;
    std::vector<ServiceCrashRecord> crashes;
    
    {
        QMutexLocker locker(&m_bufferMutex);
        snapshots.swap(m_snapshotBuffer);
        crashes.swap(m_crashBuffer);
    }
    
    if (snapshots.empty() && crashes.empty()) {
        return;
    }
    
    m_db.transaction();
    
    // Insert snapshots
    if (!snapshots.empty()) {
        QSqlQuery query(m_db);
        query.prepare(R"(
            INSERT INTO service_snapshots 
            (service_name, display_name, timestamp, state, cpu_usage, memory_usage, thread_count, handle_count)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
        )");
        
        for (const auto& snap : snapshots) {
            query.addBindValue(snap.serviceName);
            query.addBindValue(snap.displayName);
            query.addBindValue(snap.timestamp.toSecsSinceEpoch());
            query.addBindValue(static_cast<int>(snap.state));
            query.addBindValue(snap.cpuUsagePercent);
            query.addBindValue(snap.memoryUsageBytes);
            query.addBindValue(snap.threadCount);
            query.addBindValue(snap.handleCount);
            query.exec();
        }
    }
    
    // Insert crashes
    if (!crashes.empty()) {
        QSqlQuery query(m_db);
        query.prepare(R"(
            INSERT INTO service_crashes 
            (service_name, display_name, timestamp, failure_reason, previous_state, event_id)
            VALUES (?, ?, ?, ?, ?, ?)
        )");
        
        for (const auto& crash : crashes) {
            query.addBindValue(crash.serviceName);
            query.addBindValue(crash.displayName);
            query.addBindValue(crash.timestamp.toSecsSinceEpoch());
            query.addBindValue(crash.failureReason);
            query.addBindValue(static_cast<int>(crash.previousState));
            query.addBindValue(crash.eventId);
            query.exec();
        }
    }
    
    m_db.commit();
    m_lastFlush = QDateTime::currentDateTime();
    
    emit dataRecorded(static_cast<int>(snapshots.size() + crashes.size()));
}

std::vector<ServiceResourceSnapshot> ServiceHistoryManager::getServiceHistory(
    const QString& serviceName,
    const QDateTime& from,
    const QDateTime& to,
    int maxSamples)
{
    std::vector<ServiceResourceSnapshot> result;
    if (!m_isReady) return result;
    
    QSqlQuery query(m_db);
    QString sql = R"(
        SELECT service_name, display_name, timestamp, state, 
               cpu_usage, memory_usage, thread_count, handle_count
        FROM service_snapshots
        WHERE service_name = ? AND timestamp >= ? AND timestamp <= ?
        ORDER BY timestamp ASC
        LIMIT ?
    )";
    
    query.prepare(sql);
    query.addBindValue(serviceName);
    query.addBindValue(from.toSecsSinceEpoch());
    query.addBindValue(to.toSecsSinceEpoch());
    query.addBindValue(maxSamples);
    
    if (!query.exec()) {
        emit databaseError(query.lastError().text());
        return result;
    }
    
    while (query.next()) {
        ServiceResourceSnapshot snap;
        snap.serviceName = query.value(0).toString();
        snap.displayName = query.value(1).toString();
        snap.timestamp = QDateTime::fromSecsSinceEpoch(query.value(2).toLongLong());
        snap.state = static_cast<ServiceState>(query.value(3).toInt());
        snap.cpuUsagePercent = query.value(4).toDouble();
        snap.memoryUsageBytes = query.value(5).toLongLong();
        snap.threadCount = query.value(6).toInt();
        snap.handleCount = query.value(7).toInt();
        result.push_back(snap);
    }
    
    return result;
}

ServiceMetricsAggregate ServiceHistoryManager::getAggregatedMetrics(
    const QString& serviceName,
    const QDateTime& from,
    const QDateTime& to)
{
    ServiceMetricsAggregate agg;
    agg.serviceName = serviceName;
    agg.periodStart = from;
    agg.periodEnd = to;
    
    if (!m_isReady) return agg;
    
    QSqlQuery query(m_db);
    
    // Get resource stats
    QString sql = R"(
        SELECT 
            AVG(cpu_usage) as avg_cpu,
            MAX(cpu_usage) as max_cpu,
            MIN(cpu_usage) as min_cpu,
            AVG(memory_usage) as avg_mem,
            MAX(memory_usage) as max_mem,
            MIN(memory_usage) as min_mem,
            SUM(CASE WHEN state = 4 THEN 1 ELSE 0 END) as running_count,
            COUNT(*) as total_samples
        FROM service_snapshots
        WHERE service_name = ? AND timestamp >= ? AND timestamp <= ?
    )";
    
    query.prepare(sql);
    query.addBindValue(serviceName);
    query.addBindValue(from.toSecsSinceEpoch());
    query.addBindValue(to.toSecsSinceEpoch());
    
    if (query.exec() && query.next()) {
        agg.avgCpuUsage = query.value(0).toDouble();
        agg.maxCpuUsage = query.value(1).toDouble();
        agg.minCpuUsage = query.value(2).toDouble();
        agg.avgMemoryUsage = query.value(3).toLongLong();
        agg.maxMemoryUsage = query.value(4).toLongLong();
        agg.minMemoryUsage = query.value(5).toLongLong();
        agg.runningCount = query.value(6).toInt();
        agg.totalSamples = query.value(7).toInt();
        
        if (agg.totalSamples > 0) {
            agg.availabilityPercent = (agg.runningCount * 100.0) / agg.totalSamples;
        }
    }
    
    // Get crash count
    query.prepare(R"(
        SELECT COUNT(*) FROM service_crashes
        WHERE service_name = ? AND timestamp >= ? AND timestamp <= ?
    )");
    query.addBindValue(serviceName);
    query.addBindValue(from.toSecsSinceEpoch());
    query.addBindValue(to.toSecsSinceEpoch());
    
    if (query.exec() && query.next()) {
        agg.crashCount = query.value(0).toInt();
    }
    
    return agg;
}

std::vector<ServiceMetricsAggregate> ServiceHistoryManager::getAllServicesAggregates(
    const QDateTime& from,
    const QDateTime& to)
{
    std::vector<ServiceMetricsAggregate> result;
    if (!m_isReady) return result;
    
    QStringList services = getAllRecordedServices();
    for (const QString& svc : services) {
        result.push_back(getAggregatedMetrics(svc, from, to));
    }
    
    return result;
}

std::vector<ServiceCrashRecord> ServiceHistoryManager::getCrashHistory(
    const QString& serviceName,
    const QDateTime& from,
    const QDateTime& to,
    int maxRecords)
{
    std::vector<ServiceCrashRecord> result;
    if (!m_isReady) return result;
    
    QSqlQuery query(m_db);
    QString sql = R"(
        SELECT service_name, display_name, timestamp, failure_reason, previous_state, event_id
        FROM service_crashes
        WHERE 1=1
    )";
    
    if (!serviceName.isEmpty()) {
        sql += " AND service_name = ?";
    }
    if (from.isValid()) {
        sql += " AND timestamp >= ?";
    }
    if (to.isValid()) {
        sql += " AND timestamp <= ?";
    }
    sql += " ORDER BY timestamp DESC LIMIT ?";
    
    query.prepare(sql);
    
    if (!serviceName.isEmpty()) {
        query.addBindValue(serviceName);
    }
    if (from.isValid()) {
        query.addBindValue(from.toSecsSinceEpoch());
    }
    if (to.isValid()) {
        query.addBindValue(to.toSecsSinceEpoch());
    }
    query.addBindValue(maxRecords);
    
    if (!query.exec()) {
        emit databaseError(query.lastError().text());
        return result;
    }
    
    while (query.next()) {
        ServiceCrashRecord crash;
        crash.serviceName = query.value(0).toString();
        crash.displayName = query.value(1).toString();
        crash.timestamp = QDateTime::fromSecsSinceEpoch(query.value(2).toLongLong());
        crash.failureReason = query.value(3).toString();
        crash.previousState = static_cast<ServiceState>(query.value(4).toInt());
        crash.eventId = query.value(5).toInt();
        result.push_back(crash);
    }
    
    return result;
}

std::vector<std::pair<QString, int>> ServiceHistoryManager::getTopCrashingServices(
    int topN,
    const QDateTime& from,
    const QDateTime& to)
{
    std::vector<std::pair<QString, int>> result;
    if (!m_isReady) return result;
    
    QSqlQuery query(m_db);
    QString sql = R"(
        SELECT service_name, COUNT(*) as crash_count
        FROM service_crashes
        WHERE 1=1
    )";
    
    if (from.isValid()) {
        sql += QString(" AND timestamp >= %1").arg(from.toSecsSinceEpoch());
    }
    if (to.isValid()) {
        sql += QString(" AND timestamp <= %1").arg(to.toSecsSinceEpoch());
    }
    sql += " GROUP BY service_name ORDER BY crash_count DESC LIMIT ?";
    
    query.prepare(sql);
    query.addBindValue(topN);
    
    if (query.exec()) {
        while (query.next()) {
            result.emplace_back(query.value(0).toString(), query.value(1).toInt());
        }
    }
    
    return result;
}

std::vector<std::pair<QString, double>> ServiceHistoryManager::getTopCpuServices(
    int topN,
    const QDateTime& from,
    const QDateTime& to)
{
    std::vector<std::pair<QString, double>> result;
    if (!m_isReady) return result;
    
    QSqlQuery query(m_db);
    QString sql = R"(
        SELECT service_name, AVG(cpu_usage) as avg_cpu
        FROM service_snapshots
        WHERE cpu_usage > 0
    )";
    
    if (from.isValid()) {
        sql += QString(" AND timestamp >= %1").arg(from.toSecsSinceEpoch());
    }
    if (to.isValid()) {
        sql += QString(" AND timestamp <= %1").arg(to.toSecsSinceEpoch());
    }
    sql += " GROUP BY service_name ORDER BY avg_cpu DESC LIMIT ?";
    
    query.prepare(sql);
    query.addBindValue(topN);
    
    if (query.exec()) {
        while (query.next()) {
            result.emplace_back(query.value(0).toString(), query.value(1).toDouble());
        }
    }
    
    return result;
}

std::vector<std::pair<QString, qint64>> ServiceHistoryManager::getTopMemoryServices(
    int topN,
    const QDateTime& from,
    const QDateTime& to)
{
    std::vector<std::pair<QString, qint64>> result;
    if (!m_isReady) return result;
    
    QSqlQuery query(m_db);
    QString sql = R"(
        SELECT service_name, AVG(memory_usage) as avg_mem
        FROM service_snapshots
        WHERE memory_usage > 0
    )";
    
    if (from.isValid()) {
        sql += QString(" AND timestamp >= %1").arg(from.toSecsSinceEpoch());
    }
    if (to.isValid()) {
        sql += QString(" AND timestamp <= %1").arg(to.toSecsSinceEpoch());
    }
    sql += " GROUP BY service_name ORDER BY avg_mem DESC LIMIT ?";
    
    query.prepare(sql);
    query.addBindValue(topN);
    
    if (query.exec()) {
        while (query.next()) {
            result.emplace_back(query.value(0).toString(), query.value(1).toLongLong());
        }
    }
    
    return result;
}

double ServiceHistoryManager::getServiceAvailability(
    const QString& serviceName,
    const QDateTime& from,
    const QDateTime& to)
{
    if (!m_isReady) return 0.0;
    
    auto agg = getAggregatedMetrics(serviceName, from, to);
    return agg.availabilityPercent;
}

QStringList ServiceHistoryManager::getAllRecordedServices()
{
    QStringList result;
    if (!m_isReady) return result;
    
    QSqlQuery query(m_db);
    if (query.exec("SELECT DISTINCT service_name FROM service_snapshots ORDER BY service_name")) {
        while (query.next()) {
            result.append(query.value(0).toString());
        }
    }
    
    return result;
}

std::pair<QDateTime, QDateTime> ServiceHistoryManager::getDataTimeRange()
{
    if (!m_isReady) return {QDateTime(), QDateTime()};
    
    QSqlQuery query(m_db);
    if (query.exec("SELECT MIN(timestamp), MAX(timestamp) FROM service_snapshots") && query.next()) {
        return {
            QDateTime::fromSecsSinceEpoch(query.value(0).toLongLong()),
            QDateTime::fromSecsSinceEpoch(query.value(1).toLongLong())
        };
    }
    
    return {QDateTime(), QDateTime()};
}

void ServiceHistoryManager::purgeOldData(int olderThanDays)
{
    if (!m_isReady) return;
    
    qint64 cutoff = QDateTime::currentDateTime().addDays(-olderThanDays).toSecsSinceEpoch();
    
    QSqlQuery query(m_db);
    query.prepare("DELETE FROM service_snapshots WHERE timestamp < ?");
    query.addBindValue(cutoff);
    query.exec();
    
    query.prepare("DELETE FROM service_crashes WHERE timestamp < ?");
    query.addBindValue(cutoff);
    query.exec();
    
    query.prepare("DELETE FROM service_hourly WHERE hour_timestamp < ?");
    query.addBindValue(cutoff);
    query.exec();
    
    qDebug() << "Purged service history older than" << olderThanDays << "days";
}

void ServiceHistoryManager::compactDatabase()
{
    if (!m_isReady) return;
    QSqlQuery query(m_db);
    query.exec("VACUUM");
    qDebug() << "Service history database compacted";
}

qint64 ServiceHistoryManager::databaseSize() const
{
    return QFileInfo(m_dbPath).size();
}

qint64 ServiceHistoryManager::totalRecordCount() const
{
    if (!m_isReady) return 0;
    
    QSqlQuery query(m_db);
    qint64 total = 0;
    
    if (query.exec("SELECT COUNT(*) FROM service_snapshots") && query.next()) {
        total += query.value(0).toLongLong();
    }
    if (query.exec("SELECT COUNT(*) FROM service_crashes") && query.next()) {
        total += query.value(0).toLongLong();
    }
    
    return total;
}

void ServiceHistoryManager::performMaintenance()
{
    purgeOldData(m_retentionDays);
    qDebug() << "Service history maintenance done. Size:" << databaseSize() / 1024 << "KB";
}

bool ServiceHistoryManager::exportToCsv(
    const QString& filePath,
    const QString& serviceName,
    const QDateTime& from,
    const QDateTime& to)
{
    if (!m_isReady) return false;
    
    QFile file(filePath);
    if (!file.open(QIODevice::WriteOnly | QIODevice::Text)) {
        return false;
    }
    
    QTextStream out(&file);
    out << "Timestamp,ServiceName,DisplayName,State,CPU%,MemoryMB,Threads,Handles\n";
    
    auto history = getServiceHistory(serviceName, from, to, 1000000);
    for (const auto& snap : history) {
        out << snap.timestamp.toString(Qt::ISODate) << ","
            << "\"" << snap.serviceName << "\","
            << "\"" << snap.displayName << "\","
            << static_cast<int>(snap.state) << ","
            << snap.cpuUsagePercent << ","
            << (snap.memoryUsageBytes / (1024.0 * 1024.0)) << ","
            << snap.threadCount << ","
            << snap.handleCount << "\n";
    }
    
    file.close();
    return true;
}

bool ServiceHistoryManager::exportToJson(
    const QString& filePath,
    const QString& serviceName,
    const QDateTime& from,
    const QDateTime& to)
{
    if (!m_isReady) return false;
    
    QJsonObject root;
    root["exportDate"] = QDateTime::currentDateTime().toString(Qt::ISODate);
    root["serviceName"] = serviceName;
    root["fromDate"] = from.toString(Qt::ISODate);
    root["toDate"] = to.toString(Qt::ISODate);
    
    QJsonArray dataArray;
    auto history = getServiceHistory(serviceName, from, to, 1000000);
    for (const auto& snap : history) {
        QJsonObject obj;
        obj["timestamp"] = snap.timestamp.toString(Qt::ISODate);
        obj["state"] = static_cast<int>(snap.state);
        obj["cpuPercent"] = snap.cpuUsagePercent;
        obj["memoryBytes"] = snap.memoryUsageBytes;
        obj["threads"] = snap.threadCount;
        obj["handles"] = snap.handleCount;
        dataArray.append(obj);
    }
    root["data"] = dataArray;
    
    // Add aggregate stats
    auto agg = getAggregatedMetrics(serviceName, from, to);
    QJsonObject stats;
    stats["avgCpu"] = agg.avgCpuUsage;
    stats["maxCpu"] = agg.maxCpuUsage;
    stats["avgMemoryMB"] = agg.avgMemoryUsage / (1024.0 * 1024.0);
    stats["maxMemoryMB"] = agg.maxMemoryUsage / (1024.0 * 1024.0);
    stats["availabilityPercent"] = agg.availabilityPercent;
    stats["crashCount"] = agg.crashCount;
    root["statistics"] = stats;
    
    QFile file(filePath);
    if (!file.open(QIODevice::WriteOnly)) {
        return false;
    }
    
    QJsonDocument doc(root);
    file.write(doc.toJson(QJsonDocument::Indented));
    file.close();
    return true;
}
