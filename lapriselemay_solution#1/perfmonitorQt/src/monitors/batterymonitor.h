#pragma once

#include <QObject>
#include <QString>

struct BatteryInfo {
    bool hasBattery{false};
    int percentage{0};
    QString status;
    QString timeRemaining;
    bool isCharging{false};
    bool isPluggedIn{false};
    
    // Extended info (Surface-specific)
    double healthPercent{100.0};
    int cycleCount{0};
    int designCapacity{0};      // mWh
    int fullChargeCapacity{0};  // mWh
    int currentCapacity{0};     // mWh
    int voltage{0};             // mV
    int chargeRate{0};          // mW (positive = charging, negative = discharging)
    double temperature{0.0};    // Celsius
    QString manufacturer;
    QString chemistry;
    QString serialNumber;
};

class BatteryMonitor : public QObject
{
    Q_OBJECT

public:
    explicit BatteryMonitor(QObject *parent = nullptr);
    ~BatteryMonitor() override;

    void update();
    [[nodiscard]] const BatteryInfo& info() const { return m_info; }
    [[nodiscard]] bool isSurfaceDevice() const { return m_isSurface; }

private:
    void queryBasicInfo();
    void queryExtendedInfo();
    void detectSurfaceDevice();
    QString formatTime(int seconds);

    BatteryInfo m_info;
    bool m_isSurface{false};
    
#ifdef _WIN32
    void* m_batteryHandle{nullptr};
#endif
};
