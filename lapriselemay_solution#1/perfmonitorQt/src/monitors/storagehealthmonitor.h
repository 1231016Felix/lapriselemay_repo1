#pragma once

#include <QObject>
#include <QString>
#include <QStringList>
#include <QDateTime>
#include <QAbstractTableModel>
#include <QColor>
#include <vector>
#include <map>
#include <memory>

/**
 * @brief S.M.A.R.T. attribute IDs and their meanings
 */
enum class SmartAttribute : uint8_t {
    ReadErrorRate = 0x01,
    SpinUpTime = 0x03,
    StartStopCount = 0x04,
    ReallocatedSectorCount = 0x05,
    SeekErrorRate = 0x07,
    PowerOnHours = 0x09,
    SpinRetryCount = 0x0A,
    PowerCycleCount = 0x0C,
    SsdWearLevelingCount = 0xAD,
    UnexpectedPowerLoss = 0xAE,
    ReportedUncorrectableErrors = 0xBB,
    Temperature = 0xC2,
    TemperatureAlt = 0xBE,
    ReallocationEventCount = 0xC4,
    CurrentPendingSectorCount = 0xC5,
    UncorrectableSectorCount = 0xC6,
    UltraDmaCrcErrorCount = 0xC7,
    SsdLifeLeft = 0xE7,
    AvailableReservedSpace = 0xE8,
    MediaWearoutIndicator = 0xE9,
    TotalLbasWritten = 0xF1,
    TotalLbasRead = 0xF2,
    Unknown = 0xFF
};

/**
 * @brief Single S.M.A.R.T. attribute data
 */
struct SmartAttributeData {
    uint8_t id{0};
    QString name;
    uint8_t currentValue{0};
    uint8_t worstValue{0};
    uint8_t threshold{0};
    uint64_t rawValue{0};
    QString rawValueString;
    bool isCritical{false};
    bool isPrefail{false};
    bool isOk{true};
};

/**
 * @brief Drive health status
 */
enum class DriveHealthStatus {
    Excellent,
    Good,
    Fair,
    Poor,
    Critical,
    Unknown
};

/**
 * @brief NVMe specific health data
 */
struct NvmeHealthInfo {
    bool isValid{false};
    uint8_t availableSpare{0};
    uint8_t availableSpareThreshold{0};
    uint8_t percentageUsed{0};
    uint64_t dataUnitsRead{0};
    uint64_t dataUnitsWritten{0};
    uint64_t hostReadCommands{0};
    uint64_t hostWriteCommands{0};
    uint64_t controllerBusyTime{0};
    uint64_t powerCycles{0};
    uint64_t powerOnHours{0};
    uint64_t unsafeShutdowns{0};
    uint64_t mediaErrors{0};
    uint64_t errorLogEntries{0};
    int16_t temperature{0};
    int16_t warningTempTime{0};
    int16_t criticalTempTime{0};
};

/**
 * @brief Complete disk health information
 */
struct DiskHealthInfo {
    QString devicePath;
    QString model;
    QString serialNumber;
    QString firmwareVersion;
    QString interfaceType;
    bool isNvme{false};
    bool isSsd{false};
    bool isRemovable{false};
    
    uint64_t totalBytes{0};
    uint64_t freeBytes{0};
    QString totalFormatted;
    QString freeFormatted;
    
    DriveHealthStatus healthStatus{DriveHealthStatus::Unknown};
    int healthPercent{-1};
    QString healthDescription;
    
    bool smartSupported{false};
    bool smartEnabled{false};
    bool smartPassed{true};
    std::vector<SmartAttributeData> smartAttributes;
    
    NvmeHealthInfo nvmeHealth;
    
    int temperatureCelsius{-1};
    
    uint64_t powerOnHours{0};
    uint64_t powerCycles{0};
    double estimatedLifeRemainingPercent{-1};
    QString estimatedLifeDescription;
    
    QDateTime lastUpdated;
    
    QStringList warnings;
    QStringList criticalAlerts;
};

/**
 * @brief Model for displaying S.M.A.R.T. attributes in a table
 */
class SmartAttributeModel : public QAbstractTableModel
{
    Q_OBJECT

public:
    explicit SmartAttributeModel(QObject *parent = nullptr);
    
    void setAttributes(const std::vector<SmartAttributeData>& attributes);
    void clear();
    
    int rowCount(const QModelIndex &parent = QModelIndex()) const override;
    int columnCount(const QModelIndex &parent = QModelIndex()) const override;
    QVariant data(const QModelIndex &index, int role = Qt::DisplayRole) const override;
    QVariant headerData(int section, Qt::Orientation orientation, int role = Qt::DisplayRole) const override;

private:
    std::vector<SmartAttributeData> m_attributes;
};

/**
 * @brief Storage Health Monitor using S.M.A.R.T. and NVMe health data
 */
class StorageHealthMonitor : public QObject
{
    Q_OBJECT

public:
    explicit StorageHealthMonitor(QObject *parent = nullptr);
    ~StorageHealthMonitor() override;

    void update();
    
    const std::vector<DiskHealthInfo>& disks() const { return m_disks; }
    
    const DiskHealthInfo* getDiskInfo(const QString& devicePath) const;
    
    static bool isAdmin();
    static QString formatBytes(uint64_t bytes);
    static QString getAttributeName(uint8_t id);
    static bool isAttributeCritical(uint8_t id);
    static QString healthStatusToString(DriveHealthStatus status);
    static QString healthStatusColor(DriveHealthStatus status);

signals:
    void updated();
    void diskHealthWarning(const QString& diskModel, const QString& warning);
    void diskHealthCritical(const QString& diskModel, const QString& alert);

private:
    void enumerateDisks();
    void readSmartData(DiskHealthInfo& disk);
    void readNvmeHealth(DiskHealthInfo& disk);
    void calculateHealthStatus(DiskHealthInfo& disk);
    void checkAlerts(DiskHealthInfo& disk);
    
    void parseSmartAttributes(const uint8_t* data, size_t length, 
                               std::vector<SmartAttributeData>& attributes);
    void parseSmartThresholds(const uint8_t* data, size_t length,
                               std::vector<SmartAttributeData>& attributes);

    std::vector<DiskHealthInfo> m_disks;
    std::map<QString, DiskHealthInfo> m_previousState;
    bool m_isAdmin{false};
};
