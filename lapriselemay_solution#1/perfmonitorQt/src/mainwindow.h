#pragma once

#include <QMainWindow>
#include <QTabWidget>
#include <QTimer>
#include <QSystemTrayIcon>
#include <QLabel>
#include <QProgressBar>
#include <QTableView>
#include <QTreeView>
#include <QLineEdit>
#include <memory>

#include "monitors/cpumonitor.h"
#include "monitors/memorymonitor.h"
#include "monitors/gpumonitor.h"
#include "monitors/diskmonitor.h"
#include "monitors/networkmonitor.h"
#include "monitors/batterymonitor.h"
#include "monitors/temperaturemonitor.h"
#include "monitors/advancedprocessmonitor.h"
#include "widgets/sparklinegraph.h"
#include "widgets/advancedprocesswidget.h"
#include "widgets/systemtray.h"
#include "widgets/floatingwidget.h"
#include "widgets/settingsdialog.h"
#include "utils/energymode.h"
#include "utils/monitorworker.h"

class ToolsWidget;
class MetricsHistory;

class MainWindow : public QMainWindow
{
    Q_OBJECT

public:
    explicit MainWindow(QWidget *parent = nullptr);
    ~MainWindow() override;

protected:
    void closeEvent(QCloseEvent *event) override;
    void changeEvent(QEvent *event) override;

private slots:
    void onMonitorDataReady(const MonitorData& data);
    void onTrayActivated(QSystemTrayIcon::ActivationReason reason);
    void toggleAlwaysOnTop();
    void showSettings();
    void showAbout();
    void exportReport();
    void purgeMemory();
    void checkAdminPrivileges();
    void toggleFloatingWidget();
    void onFloatingWidgetClosed();
    void showEnergyModeDialog();
    void toggleEnergyMode();
    void onTrayExitRequested();
    
    // New features
    void showServicesManager();
    void showMetricsHistory();
    void showDiskScanner();
    void showNetworkSpeedTest();

private:
    void setupUi();
    void setupMenuBar();
    void setupStatusBar();
    void setupTrayIcon();
    void setupMetricsHistory();
    void createCpuTab();
    void createGpuTab();
    void createMemoryTab();
    void createDiskTab();
    void createNetworkTab();
    void createBatteryTab();
    void createProcessTab();
    void createToolsTab();
    void loadSettings();
    void saveSettings();
    void applyTabVisibility(const AppSettings& settings);
    void applyTheme(const QString& theme);
    void checkAlerts(double cpu, double memory, int battery, double gpuTemp);
    void recordMetrics();
    static QString formatBytes(qint64 bytes);

    // UI Components
    QTabWidget* m_tabWidget{nullptr};
    
    // Status bar widgets
    QLabel* m_cpuStatusLabel{nullptr};
    QLabel* m_gpuStatusLabel{nullptr};
    QLabel* m_memStatusLabel{nullptr};
    QLabel* m_batteryStatusLabel{nullptr};
    QLabel* m_tempStatusLabel{nullptr};
    
    // CPU Tab
    QWidget* m_cpuTab{nullptr};
    QLabel* m_cpuNameLabel{nullptr};
    QLabel* m_cpuUsageLabel{nullptr};
    QLabel* m_cpuSpeedLabel{nullptr};
    QLabel* m_cpuCoresLabel{nullptr};
    QLabel* m_cpuProcessesLabel{nullptr};
    QLabel* m_cpuThreadsLabel{nullptr};
    QLabel* m_cpuUptimeLabel{nullptr};
    QLabel* m_cpuTempLabel{nullptr};
    QLabel* m_chassisTempLabel{nullptr};
    QProgressBar* m_cpuProgressBar{nullptr};
    SparklineGraph* m_cpuGraph{nullptr};
    
    // Memory Tab
    QWidget* m_memoryTab{nullptr};
    QLabel* m_memUsageLabel{nullptr};
    QLabel* m_memAvailableLabel{nullptr};
    QLabel* m_memCommittedLabel{nullptr};
    QLabel* m_memCachedLabel{nullptr};
    QLabel* m_memPagedLabel{nullptr};
    QProgressBar* m_memProgressBar{nullptr};
    SparklineGraph* m_memGraph{nullptr};
    
    // GPU Tab
    QWidget* m_gpuTab{nullptr};
    QLabel* m_gpuNameLabel{nullptr};
    QLabel* m_gpuVendorLabel{nullptr};
    QLabel* m_gpuUsageLabel{nullptr};
    QLabel* m_gpuMemoryUsedLabel{nullptr};
    QLabel* m_gpuMemoryTotalLabel{nullptr};
    QLabel* m_gpuTempLabel{nullptr};
    QProgressBar* m_gpuUsageProgressBar{nullptr};
    QProgressBar* m_gpuMemoryProgressBar{nullptr};
    SparklineGraph* m_gpuUsageGraph{nullptr};
    SparklineGraph* m_gpuMemoryGraph{nullptr};
    QTableView* m_gpuTableView{nullptr};
    
    // Disk Tab
    QWidget* m_diskTab{nullptr};
    QTableView* m_diskTableView{nullptr};
    SparklineGraph* m_diskReadGraph{nullptr};
    SparklineGraph* m_diskWriteGraph{nullptr};
    QLabel* m_diskReadLabel{nullptr};
    QLabel* m_diskWriteLabel{nullptr};
    
    // Network Tab
    QWidget* m_networkTab{nullptr};
    QTableView* m_networkTableView{nullptr};
    SparklineGraph* m_netSendGraph{nullptr};
    SparklineGraph* m_netRecvGraph{nullptr};
    QLabel* m_netSendLabel{nullptr};
    QLabel* m_netRecvLabel{nullptr};
    
    // Battery Tab
    QWidget* m_batteryTab{nullptr};
    QLabel* m_batteryPercentLabel{nullptr};
    QLabel* m_batteryStatusLabel2{nullptr};
    QLabel* m_batteryTimeLabel{nullptr};
    QLabel* m_batteryHealthLabel{nullptr};
    QLabel* m_batteryCyclesLabel{nullptr};
    QLabel* m_batteryCapacityLabel{nullptr};
    QLabel* m_batteryVoltageLabel{nullptr};
    QLabel* m_batteryTempLabel{nullptr};
    QProgressBar* m_batteryProgressBar{nullptr};
    SparklineGraph* m_batteryGraph{nullptr};
    
    // Process Tab (unified - was Advanced Process Tab)
    QWidget* m_processTab{nullptr};
    AdvancedProcessWidget* m_processWidget{nullptr};
    
    // Tools Tab
    QWidget* m_toolsTab{nullptr};
    
    // Background monitor worker (runs in separate thread)
    std::unique_ptr<MonitorWorker> m_monitorWorker;
    
    // Cached monitor data (updated from worker thread)
    MonitorData m_monitorData;
    
    // System tray
    std::unique_ptr<SystemTrayManager> m_trayManager;
    
    // Floating widget
    std::unique_ptr<FloatingWidget> m_floatingWidget;
    QAction* m_floatingWidgetAction{nullptr};
    
    // Energy Mode
    std::unique_ptr<EnergyModeManager> m_energyModeManager;
    QAction* m_energyModeAction{nullptr};
    QLabel* m_energyModeStatusLabel{nullptr};
    
    // Metrics History (for persistent data recording)
    std::unique_ptr<MetricsHistory> m_metricsHistory;
    
    // Settings
    bool m_minimizeToTray{true};
    bool m_alwaysOnTop{false};
    bool m_forceQuit{false};
    int m_updateInterval{1000};
    bool m_isAdmin{false};
    
    // App settings (from SettingsDialog)
    AppSettings m_alertSettings;
    
    // Alert cooldown tracking
    qint64 m_lastCpuAlertTime{0};
    qint64 m_lastMemoryAlertTime{0};
    qint64 m_lastBatteryAlertTime{0};
    qint64 m_lastTempAlertTime{0};
    
    // Admin status indicator
    QLabel* m_adminStatusLabel{nullptr};
};
