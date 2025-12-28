#pragma once

#include <QObject>
#include <QString>
#include <QStringList>
#include <QSettings>
#include <vector>
#include <map>

#ifdef _WIN32
#include <Windows.h>
#endif

/**
 * @brief Service information structure for Energy Mode
 */
struct EnergyServiceInfo {
    QString name;           // Internal service name
    QString displayName;    // Friendly display name
    QString description;    // What this service does
    bool wasRunning{false}; // State before Energy Mode was enabled
    bool isEssential{false};// If true, never stop this service
    bool isSelected{true};  // User selected for stopping
};

/**
 * @brief Energy Mode categories
 */
enum class ServiceCategory {
    Telemetry,      // Data collection services
    Search,         // Windows Search, indexing
    Updates,        // Windows Update related
    Printing,       // Print spooler, fax
    Xbox,           // Xbox services (if not gaming on Xbox)
    Network,        // Non-essential network services
    Maintenance,    // Defrag, diagnostics
    Other           // Miscellaneous
};

/**
 * @brief Manages Energy Mode - stops non-essential Windows services
 * 
 * When activated, this mode:
 * 1. Saves the current state of all target services
 * 2. Stops non-essential services to free resources
 * 3. Sets power plan to High Performance
 * 4. Optionally disables visual effects
 * 
 * When deactivated:
 * 1. Restores services to their previous state
 * 2. Restores power plan
 * 3. Restores visual effects
 */
class EnergyModeManager : public QObject
{
    Q_OBJECT

public:
    explicit EnergyModeManager(QObject *parent = nullptr);
    ~EnergyModeManager() override;

    /// Check if Energy Mode is currently active
    bool isActive() const { return m_isActive; }
    
    /// Activate Energy Mode (requires admin)
    bool activate();
    
    /// Deactivate and restore previous state
    bool deactivate();
    
    /// Toggle Energy Mode
    bool toggle();
    
    /// Get list of services that will be affected
    std::vector<EnergyServiceInfo>& services() { return m_services; }
    const std::vector<EnergyServiceInfo>& services() const { return m_services; }
    
    /// Check if running as admin (required for service control)
    static bool isRunningAsAdmin();
    
    /// Get estimated RAM that could be freed
    qint64 estimatedMemorySavings() const;
    
    /// Get count of services that will be stopped
    int servicesToStopCount() const;
    
    /// Enable/disable specific service in the list
    void setServiceEnabled(const QString& serviceName, bool enabled);
    
    /// Power plan control
    bool setHighPerformancePowerPlan();
    bool restorePowerPlan();
    
    /// Get current status message
    QString statusMessage() const { return m_statusMessage; }

signals:
    void activationChanged(bool active);
    void serviceStateChanged(const QString& serviceName, bool running);
    void statusMessageChanged(const QString& message);
    void progressChanged(int current, int total);

private:
    void initializeServiceList();
    void loadSettings();
    void saveSettings();
    
    bool stopService(const QString& serviceName);
    bool startService(const QString& serviceName);
    bool isServiceRunning(const QString& serviceName);
    DWORD getServiceStartType(const QString& serviceName);
    
    void setStatus(const QString& message);

    std::vector<EnergyServiceInfo> m_services;
    bool m_isActive{false};
    QString m_statusMessage;
    
    // Saved states for restoration
    QString m_previousPowerPlan;
    std::map<QString, bool> m_previousServiceStates;
};
