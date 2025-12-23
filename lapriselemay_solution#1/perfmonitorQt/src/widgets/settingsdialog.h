#pragma once

#include <QDialog>
#include <QTabWidget>
#include <QSpinBox>
#include <QCheckBox>
#include <QComboBox>
#include <QSlider>
#include <QLabel>
#include <QGroupBox>
#include <QSettings>
#include <map>

/**
 * @brief Application settings structure
 */
struct AppSettings {
    // General
    int updateInterval{1000};           // ms (500-5000)
    bool startWithWindows{false};
    bool minimizeToTray{true};
    bool startMinimized{false};
    bool showSplashScreen{false};
    
    // Appearance
    QString theme{"system"};            // "light", "dark", "system"
    bool showCpuTab{true};
    bool showGpuTab{true};
    bool showMemoryTab{true};
    bool showDiskTab{true};
    bool showNetworkTab{true};
    bool showBatteryTab{true};
    bool showProcessTab{true};
    
    // Alerts
    bool alertsEnabled{true};
    int cpuAlertThreshold{90};          // %
    int memoryAlertThreshold{85};       // %
    int batteryAlertThreshold{15};      // %
    int temperatureAlertThreshold{85};  // °C
    bool alertSound{true};
    int alertCooldown{60};              // seconds between same alerts
    
    // Floating Widget
    double floatingOpacity{0.9};
    bool floatingShowCpu{true};
    bool floatingShowMemory{true};
    bool floatingShowGpu{false};
    bool floatingShowBattery{false};
    bool floatingShowGraphs{true};
    bool floatingShowTemps{false};
};

/**
 * @brief Complete settings dialog for PerfMonitorQt
 */
class SettingsDialog : public QDialog
{
    Q_OBJECT

public:
    explicit SettingsDialog(QWidget *parent = nullptr);
    ~SettingsDialog() override;

    /// Get current settings
    AppSettings settings() const { return m_settings; }
    
    /// Load settings from QSettings
    static AppSettings loadSettings();
    
    /// Save settings to QSettings
    static void saveSettings(const AppSettings& settings);

signals:
    void settingsChanged(const AppSettings& settings);
    void themeChanged(const QString& theme);

private slots:
    void onAccept();
    void onApply();
    void onRestoreDefaults();
    void onThemeChanged(int index);
    void updateIntervalLabel(int value);

private:
    void setupUi();
    QWidget* createGeneralTab();
    QWidget* createAppearanceTab();
    QWidget* createAlertsTab();
    QWidget* createFloatingTab();
    
    void loadCurrentSettings();
    void applySettingsToUi();
    void collectSettingsFromUi();
    
    void updateStartupRegistry(bool enable);
    bool isInStartupRegistry() const;

    // UI Components
    QTabWidget* m_tabWidget{nullptr};
    
    // General Tab
    QSlider* m_intervalSlider{nullptr};
    QLabel* m_intervalValueLabel{nullptr};
    QCheckBox* m_startWithWindowsCheck{nullptr};
    QCheckBox* m_minimizeToTrayCheck{nullptr};
    QCheckBox* m_startMinimizedCheck{nullptr};
    
    // Appearance Tab
    QComboBox* m_themeCombo{nullptr};
    QCheckBox* m_showCpuTabCheck{nullptr};
    QCheckBox* m_showGpuTabCheck{nullptr};
    QCheckBox* m_showMemoryTabCheck{nullptr};
    QCheckBox* m_showDiskTabCheck{nullptr};
    QCheckBox* m_showNetworkTabCheck{nullptr};
    QCheckBox* m_showBatteryTabCheck{nullptr};
    QCheckBox* m_showProcessTabCheck{nullptr};
    
    // Alerts Tab
    QCheckBox* m_alertsEnabledCheck{nullptr};
    QSpinBox* m_cpuAlertSpin{nullptr};
    QSpinBox* m_memoryAlertSpin{nullptr};
    QSpinBox* m_batteryAlertSpin{nullptr};
    QSpinBox* m_tempAlertSpin{nullptr};
    QCheckBox* m_alertSoundCheck{nullptr};
    QSpinBox* m_alertCooldownSpin{nullptr};
    
    // Floating Widget Tab
    QSlider* m_opacitySlider{nullptr};
    QLabel* m_opacityValueLabel{nullptr};
    QCheckBox* m_floatingCpuCheck{nullptr};
    QCheckBox* m_floatingMemoryCheck{nullptr};
    QCheckBox* m_floatingGpuCheck{nullptr};
    QCheckBox* m_floatingBatteryCheck{nullptr};
    QCheckBox* m_floatingGraphsCheck{nullptr};
    QCheckBox* m_floatingTempsCheck{nullptr};
    
    AppSettings m_settings;
    AppSettings m_originalSettings;
};
