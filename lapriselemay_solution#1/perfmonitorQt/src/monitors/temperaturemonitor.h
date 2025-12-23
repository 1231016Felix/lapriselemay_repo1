#pragma once

#include <QObject>
#include <QString>
#include <QAbstractTableModel>
#include <vector>
#include <memory>

/**
 * @brief Information about a thermal zone
 */
struct ThermalZoneInfo {
    QString name;               // Zone name (e.g., "\_SB.TZ09")
    QString friendlyName;       // User-friendly name (e.g., "CPU", "Chassis")
    double temperatureC{0.0};   // Temperature in Celsius
    double temperatureK{0.0};   // Temperature in Kelvin
    bool isValid{false};        // Whether the reading is valid
};

/**
 * @brief Aggregated temperature information
 */
struct TemperatureInfo {
    double cpuTemperature{-999.0};      // Main CPU/SoC temperature (highest)
    double chassisTemperature{-999.0};  // Chassis/motherboard temperature
    double maxTemperature{-999.0};      // Maximum across all zones
    double avgTemperature{-999.0};      // Average of valid zones
    int validZoneCount{0};              // Number of valid thermal zones
    bool hasTemperature{false};         // Whether any temperature is available
};

/**
 * @brief Table model for displaying thermal zones
 */
class ThermalZoneTableModel : public QAbstractTableModel
{
    Q_OBJECT

public:
    explicit ThermalZoneTableModel(QObject *parent = nullptr);
    
    void setZones(const std::vector<ThermalZoneInfo>& zones);
    
    int rowCount(const QModelIndex &parent = QModelIndex()) const override;
    int columnCount(const QModelIndex &parent = QModelIndex()) const override;
    QVariant data(const QModelIndex &index, int role = Qt::DisplayRole) const override;
    QVariant headerData(int section, Qt::Orientation orientation, int role) const override;

private:
    std::vector<ThermalZoneInfo> m_zones;
};

/**
 * @brief Monitor for system temperatures via WMI
 * 
 * Uses Win32_PerfFormattedData_Counters_ThermalZoneInformation
 * which doesn't require administrator privileges.
 * 
 * Works on Surface devices and most modern laptops.
 */
class TemperatureMonitor : public QObject
{
    Q_OBJECT

public:
    explicit TemperatureMonitor(QObject *parent = nullptr);
    ~TemperatureMonitor() override;

    /// Update temperature readings
    void update();
    
    /// Get aggregated temperature info
    [[nodiscard]] const TemperatureInfo& info() const { return m_info; }
    
    /// Get all thermal zones
    [[nodiscard]] const std::vector<ThermalZoneInfo>& zones() const { return m_zones; }
    
    /// Get table model for UI
    [[nodiscard]] QAbstractTableModel* model() { return m_model.get(); }
    
    /// Check if temperature monitoring is available on this system
    [[nodiscard]] bool isAvailable() const { return m_isAvailable; }
    
    /// Format temperature for display
    static QString formatTemperature(double celsius);

private:
    void initializeWmi();
    void queryTemperatures();
    QString mapZoneToFriendlyName(const QString& zoneName);
    void calculateAggregates();

    TemperatureInfo m_info;
    std::vector<ThermalZoneInfo> m_zones;
    std::unique_ptr<ThermalZoneTableModel> m_model;
    bool m_isAvailable{false};
    bool m_wmiInitialized{false};
    
#ifdef _WIN32
    void* m_wbemLocator{nullptr};
    void* m_wbemServices{nullptr};
#endif
};
