#include "settingsdialog.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QFormLayout>
#include <QPushButton>
#include <QDialogButtonBox>
#include <QMessageBox>
#include <QApplication>
#include <QStyle>
#include <QStyleFactory>
#include <QFrame>

#ifdef _WIN32
#include <Windows.h>
#include <ShlObj.h>
#endif

SettingsDialog::SettingsDialog(QWidget *parent)
    : QDialog(parent)
{
    setWindowTitle(tr("Settings"));
    setMinimumSize(500, 450);
    resize(550, 500);
    setWindowFlags(windowFlags() & ~Qt::WindowContextHelpButtonHint);
    
    setupUi();
    loadCurrentSettings();
    applySettingsToUi();
    
    m_originalSettings = m_settings;
}

SettingsDialog::~SettingsDialog() = default;

void SettingsDialog::setupUi()
{
    auto mainLayout = new QVBoxLayout(this);
    mainLayout->setSpacing(10);
    
    // Tab widget
    m_tabWidget = new QTabWidget();
    m_tabWidget->addTab(createGeneralTab(), tr("General"));
    m_tabWidget->addTab(createAppearanceTab(), tr("Appearance"));
    m_tabWidget->addTab(createAlertsTab(), tr("Alerts"));
    m_tabWidget->addTab(createFloatingTab(), tr("Floating Widget"));
    
    mainLayout->addWidget(m_tabWidget);
    
    // Separator line
    auto separator = new QFrame();
    separator->setFrameShape(QFrame::HLine);
    separator->setFrameShadow(QFrame::Sunken);
    mainLayout->addWidget(separator);
    
    // Buttons
    auto buttonLayout = new QHBoxLayout();
    
    auto restoreBtn = new QPushButton(tr("Restore Defaults"));
    restoreBtn->setIcon(style()->standardIcon(QStyle::SP_DialogResetButton));
    connect(restoreBtn, &QPushButton::clicked, this, &SettingsDialog::onRestoreDefaults);
    buttonLayout->addWidget(restoreBtn);
    
    buttonLayout->addStretch();
    
    auto applyBtn = new QPushButton(tr("Apply"));
    connect(applyBtn, &QPushButton::clicked, this, &SettingsDialog::onApply);
    buttonLayout->addWidget(applyBtn);
    
    auto cancelBtn = new QPushButton(tr("Cancel"));
    connect(cancelBtn, &QPushButton::clicked, this, &QDialog::reject);
    buttonLayout->addWidget(cancelBtn);
    
    auto okBtn = new QPushButton(tr("OK"));
    okBtn->setDefault(true);
    connect(okBtn, &QPushButton::clicked, this, &SettingsDialog::onAccept);
    buttonLayout->addWidget(okBtn);
    
    mainLayout->addLayout(buttonLayout);
}

QWidget* SettingsDialog::createGeneralTab()
{
    auto widget = new QWidget();
    auto layout = new QVBoxLayout(widget);
    layout->setSpacing(15);
    
    // Update Interval Group
    auto intervalGroup = new QGroupBox(tr("Update Interval"));
    auto intervalLayout = new QVBoxLayout(intervalGroup);
    
    auto intervalTopLayout = new QHBoxLayout();
    intervalTopLayout->addWidget(new QLabel(tr("Refresh rate:")));
    m_intervalValueLabel = new QLabel("1000 ms");
    m_intervalValueLabel->setStyleSheet("font-weight: bold;");
    intervalTopLayout->addWidget(m_intervalValueLabel);
    intervalTopLayout->addStretch();
    intervalLayout->addLayout(intervalTopLayout);
    
    auto sliderLayout = new QHBoxLayout();
    sliderLayout->addWidget(new QLabel(tr("Fast (500ms)")));
    
    m_intervalSlider = new QSlider(Qt::Horizontal);
    m_intervalSlider->setRange(500, 5000);
    m_intervalSlider->setSingleStep(100);
    m_intervalSlider->setPageStep(500);
    m_intervalSlider->setTickPosition(QSlider::TicksBelow);
    m_intervalSlider->setTickInterval(500);
    connect(m_intervalSlider, &QSlider::valueChanged, 
            this, &SettingsDialog::updateIntervalLabel);
    sliderLayout->addWidget(m_intervalSlider);
    
    sliderLayout->addWidget(new QLabel(tr("Slow (5s)")));
    intervalLayout->addLayout(sliderLayout);
    
    auto intervalNote = new QLabel(tr("Lower values use more CPU but update faster."));
    intervalNote->setStyleSheet("color: gray; font-size: 11px;");
    intervalLayout->addWidget(intervalNote);
    
    layout->addWidget(intervalGroup);
    
    // Startup Group
    auto startupGroup = new QGroupBox(tr("Startup"));
    auto startupLayout = new QVBoxLayout(startupGroup);
    
    m_startWithWindowsCheck = new QCheckBox(tr("Start PerfMonitorQt with Windows"));
    m_startWithWindowsCheck->setToolTip(tr("Automatically launch when you log in"));
    startupLayout->addWidget(m_startWithWindowsCheck);
    
    m_startMinimizedCheck = new QCheckBox(tr("Start minimized to system tray"));
    m_startMinimizedCheck->setToolTip(tr("Start hidden in the system tray"));
    startupLayout->addWidget(m_startMinimizedCheck);
    
    layout->addWidget(startupGroup);
    
    // System Tray Group
    auto trayGroup = new QGroupBox(tr("System Tray"));
    auto trayLayout = new QVBoxLayout(trayGroup);
    
    m_minimizeToTrayCheck = new QCheckBox(tr("Minimize to system tray instead of taskbar"));
    m_minimizeToTrayCheck->setToolTip(tr("When minimized, the app will hide in the system tray"));
    trayLayout->addWidget(m_minimizeToTrayCheck);
    
    layout->addWidget(trayGroup);
    
    layout->addStretch();
    return widget;
}

QWidget* SettingsDialog::createAppearanceTab()
{
    auto widget = new QWidget();
    auto layout = new QVBoxLayout(widget);
    layout->setSpacing(15);
    
    // Theme Group
    auto themeGroup = new QGroupBox(tr("Theme"));
    auto themeLayout = new QFormLayout(themeGroup);
    
    m_themeCombo = new QComboBox();
    m_themeCombo->addItem(tr("System Default"), "system");
    m_themeCombo->addItem(tr("Light"), "light");
    m_themeCombo->addItem(tr("Dark"), "dark");
    connect(m_themeCombo, QOverload<int>::of(&QComboBox::currentIndexChanged),
            this, &SettingsDialog::onThemeChanged);
    themeLayout->addRow(tr("Application theme:"), m_themeCombo);
    
    auto themeNote = new QLabel(tr("Theme changes require application restart."));
    themeNote->setStyleSheet("color: gray; font-size: 11px;");
    themeLayout->addRow("", themeNote);
    
    layout->addWidget(themeGroup);
    
    // Visible Tabs Group
    auto tabsGroup = new QGroupBox(tr("Visible Tabs"));
    auto tabsLayout = new QGridLayout(tabsGroup);
    
    m_showCpuTabCheck = new QCheckBox(tr("CPU"));
    m_showGpuTabCheck = new QCheckBox(tr("GPU"));
    m_showMemoryTabCheck = new QCheckBox(tr("Memory"));
    m_showDiskTabCheck = new QCheckBox(tr("Disk"));
    m_showNetworkTabCheck = new QCheckBox(tr("Network"));
    m_showBatteryTabCheck = new QCheckBox(tr("Battery"));
    m_showProcessTabCheck = new QCheckBox(tr("Processes"));
    
    tabsLayout->addWidget(m_showCpuTabCheck, 0, 0);
    tabsLayout->addWidget(m_showGpuTabCheck, 0, 1);
    tabsLayout->addWidget(m_showMemoryTabCheck, 0, 2);
    tabsLayout->addWidget(m_showDiskTabCheck, 1, 0);
    tabsLayout->addWidget(m_showNetworkTabCheck, 1, 1);
    tabsLayout->addWidget(m_showBatteryTabCheck, 1, 2);
    tabsLayout->addWidget(m_showProcessTabCheck, 2, 0);
    
    auto tabsNote = new QLabel(tr("Select which monitoring tabs to display."));
    tabsNote->setStyleSheet("color: gray; font-size: 11px;");
    tabsLayout->addWidget(tabsNote, 3, 0, 1, 3);
    
    layout->addWidget(tabsGroup);
    
    layout->addStretch();
    return widget;
}

QWidget* SettingsDialog::createAlertsTab()
{
    auto widget = new QWidget();
    auto layout = new QVBoxLayout(widget);
    layout->setSpacing(15);
    
    // Enable Alerts
    m_alertsEnabledCheck = new QCheckBox(tr("Enable system alerts"));
    m_alertsEnabledCheck->setStyleSheet("font-weight: bold;");
    layout->addWidget(m_alertsEnabledCheck);
    
    // Thresholds Group
    auto thresholdGroup = new QGroupBox(tr("Alert Thresholds"));
    auto thresholdLayout = new QGridLayout(thresholdGroup);
    
    // CPU Alert
    thresholdLayout->addWidget(new QLabel(tr("CPU usage above:")), 0, 0);
    m_cpuAlertSpin = new QSpinBox();
    m_cpuAlertSpin->setRange(50, 100);
    m_cpuAlertSpin->setSuffix(" %");
    m_cpuAlertSpin->setToolTip(tr("Alert when CPU usage exceeds this value"));
    thresholdLayout->addWidget(m_cpuAlertSpin, 0, 1);
    
    // Memory Alert
    thresholdLayout->addWidget(new QLabel(tr("Memory usage above:")), 1, 0);
    m_memoryAlertSpin = new QSpinBox();
    m_memoryAlertSpin->setRange(50, 100);
    m_memoryAlertSpin->setSuffix(" %");
    m_memoryAlertSpin->setToolTip(tr("Alert when RAM usage exceeds this value"));
    thresholdLayout->addWidget(m_memoryAlertSpin, 1, 1);
    
    // Battery Alert
    thresholdLayout->addWidget(new QLabel(tr("Battery below:")), 2, 0);
    m_batteryAlertSpin = new QSpinBox();
    m_batteryAlertSpin->setRange(5, 50);
    m_batteryAlertSpin->setSuffix(" %");
    m_batteryAlertSpin->setToolTip(tr("Alert when battery drops below this value"));
    thresholdLayout->addWidget(m_batteryAlertSpin, 2, 1);
    
    // Temperature Alert
    thresholdLayout->addWidget(new QLabel(tr("Temperature above:")), 3, 0);
    m_tempAlertSpin = new QSpinBox();
    m_tempAlertSpin->setRange(60, 105);
    m_tempAlertSpin->setSuffix(" °C");
    m_tempAlertSpin->setToolTip(tr("Alert when CPU/GPU temperature exceeds this value"));
    thresholdLayout->addWidget(m_tempAlertSpin, 3, 1);
    
    layout->addWidget(thresholdGroup);
    
    // Notification Options Group
    auto notifGroup = new QGroupBox(tr("Notification Options"));
    auto notifLayout = new QVBoxLayout(notifGroup);
    
    m_alertSoundCheck = new QCheckBox(tr("Play notification sound"));
    notifLayout->addWidget(m_alertSoundCheck);
    
    auto cooldownLayout = new QHBoxLayout();
    cooldownLayout->addWidget(new QLabel(tr("Minimum time between alerts:")));
    m_alertCooldownSpin = new QSpinBox();
    m_alertCooldownSpin->setRange(10, 300);
    m_alertCooldownSpin->setSuffix(tr(" sec"));
    m_alertCooldownSpin->setToolTip(tr("Prevent alert spam by setting a cooldown"));
    cooldownLayout->addWidget(m_alertCooldownSpin);
    cooldownLayout->addStretch();
    notifLayout->addLayout(cooldownLayout);
    
    layout->addWidget(notifGroup);
    
    // Connect enable checkbox
    connect(m_alertsEnabledCheck, &QCheckBox::toggled, [this](bool enabled) {
        m_cpuAlertSpin->setEnabled(enabled);
        m_memoryAlertSpin->setEnabled(enabled);
        m_batteryAlertSpin->setEnabled(enabled);
        m_tempAlertSpin->setEnabled(enabled);
        m_alertSoundCheck->setEnabled(enabled);
        m_alertCooldownSpin->setEnabled(enabled);
    });
    
    layout->addStretch();
    return widget;
}

QWidget* SettingsDialog::createFloatingTab()
{
    auto widget = new QWidget();
    auto layout = new QVBoxLayout(widget);
    layout->setSpacing(15);
    
    // Opacity Group
    auto opacityGroup = new QGroupBox(tr("Widget Opacity"));
    auto opacityLayout = new QVBoxLayout(opacityGroup);
    
    auto opacityTopLayout = new QHBoxLayout();
    opacityTopLayout->addWidget(new QLabel(tr("Opacity:")));
    m_opacityValueLabel = new QLabel("90%");
    m_opacityValueLabel->setStyleSheet("font-weight: bold;");
    opacityTopLayout->addWidget(m_opacityValueLabel);
    opacityTopLayout->addStretch();
    opacityLayout->addLayout(opacityTopLayout);
    
    auto opacitySliderLayout = new QHBoxLayout();
    opacitySliderLayout->addWidget(new QLabel(tr("Transparent")));
    
    m_opacitySlider = new QSlider(Qt::Horizontal);
    m_opacitySlider->setRange(30, 100);
    m_opacitySlider->setSingleStep(5);
    m_opacitySlider->setTickPosition(QSlider::TicksBelow);
    m_opacitySlider->setTickInterval(10);
    connect(m_opacitySlider, &QSlider::valueChanged, [this](int value) {
        m_opacityValueLabel->setText(QString("%1%").arg(value));
    });
    opacitySliderLayout->addWidget(m_opacitySlider);
    
    opacitySliderLayout->addWidget(new QLabel(tr("Opaque")));
    opacityLayout->addLayout(opacitySliderLayout);
    
    layout->addWidget(opacityGroup);
    
    // Metrics Group
    auto metricsGroup = new QGroupBox(tr("Displayed Metrics"));
    auto metricsLayout = new QGridLayout(metricsGroup);
    
    m_floatingCpuCheck = new QCheckBox(tr("CPU Usage"));
    m_floatingMemoryCheck = new QCheckBox(tr("Memory Usage"));
    m_floatingGpuCheck = new QCheckBox(tr("GPU Usage"));
    m_floatingBatteryCheck = new QCheckBox(tr("Battery Level"));
    m_floatingGraphsCheck = new QCheckBox(tr("Show mini graphs"));
    m_floatingTempsCheck = new QCheckBox(tr("Show temperatures"));
    
    metricsLayout->addWidget(m_floatingCpuCheck, 0, 0);
    metricsLayout->addWidget(m_floatingMemoryCheck, 0, 1);
    metricsLayout->addWidget(m_floatingGpuCheck, 1, 0);
    metricsLayout->addWidget(m_floatingBatteryCheck, 1, 1);
    metricsLayout->addWidget(m_floatingGraphsCheck, 2, 0);
    metricsLayout->addWidget(m_floatingTempsCheck, 2, 1);
    
    layout->addWidget(metricsGroup);
    
    // Preview note
    auto previewNote = new QLabel(tr("💡 Tip: Double-click the floating widget to open the main window."));
    previewNote->setStyleSheet("color: #0078d7; font-size: 11px;");
    previewNote->setWordWrap(true);
    layout->addWidget(previewNote);
    
    layout->addStretch();
    return widget;
}

void SettingsDialog::loadCurrentSettings()
{
    m_settings = loadSettings();
}

AppSettings SettingsDialog::loadSettings()
{
    QSettings qsettings("Félix-Antoine", "PerfMonitorQt");
    AppSettings settings;
    
    // General
    settings.updateInterval = qsettings.value("updateInterval", 1000).toInt();
    settings.startWithWindows = qsettings.value("startWithWindows", false).toBool();
    settings.minimizeToTray = qsettings.value("minimizeToTray", true).toBool();
    settings.startMinimized = qsettings.value("startMinimized", false).toBool();
    
    // Appearance
    settings.theme = qsettings.value("theme", "system").toString();
    settings.showCpuTab = qsettings.value("showCpuTab", true).toBool();
    settings.showGpuTab = qsettings.value("showGpuTab", true).toBool();
    settings.showMemoryTab = qsettings.value("showMemoryTab", true).toBool();
    settings.showDiskTab = qsettings.value("showDiskTab", true).toBool();
    settings.showNetworkTab = qsettings.value("showNetworkTab", true).toBool();
    settings.showBatteryTab = qsettings.value("showBatteryTab", true).toBool();
    settings.showProcessTab = qsettings.value("showProcessTab", true).toBool();
    
    // Alerts
    settings.alertsEnabled = qsettings.value("alertsEnabled", true).toBool();
    settings.cpuAlertThreshold = qsettings.value("cpuAlertThreshold", 90).toInt();
    settings.memoryAlertThreshold = qsettings.value("memoryAlertThreshold", 85).toInt();
    settings.batteryAlertThreshold = qsettings.value("batteryAlertThreshold", 15).toInt();
    settings.temperatureAlertThreshold = qsettings.value("temperatureAlertThreshold", 85).toInt();
    settings.alertSound = qsettings.value("alertSound", true).toBool();
    settings.alertCooldown = qsettings.value("alertCooldown", 60).toInt();
    
    // Floating Widget
    settings.floatingOpacity = qsettings.value("floatingOpacity", 0.9).toDouble();
    settings.floatingShowCpu = qsettings.value("floatingShowCpu", true).toBool();
    settings.floatingShowMemory = qsettings.value("floatingShowMemory", true).toBool();
    settings.floatingShowGpu = qsettings.value("floatingShowGpu", false).toBool();
    settings.floatingShowBattery = qsettings.value("floatingShowBattery", false).toBool();
    settings.floatingShowGraphs = qsettings.value("floatingShowGraphs", true).toBool();
    settings.floatingShowTemps = qsettings.value("floatingShowTemps", false).toBool();
    
    return settings;
}

void SettingsDialog::saveSettings(const AppSettings& settings)
{
    QSettings qsettings("Félix-Antoine", "PerfMonitorQt");
    
    // General
    qsettings.setValue("updateInterval", settings.updateInterval);
    qsettings.setValue("startWithWindows", settings.startWithWindows);
    qsettings.setValue("minimizeToTray", settings.minimizeToTray);
    qsettings.setValue("startMinimized", settings.startMinimized);
    
    // Appearance
    qsettings.setValue("theme", settings.theme);
    qsettings.setValue("showCpuTab", settings.showCpuTab);
    qsettings.setValue("showGpuTab", settings.showGpuTab);
    qsettings.setValue("showMemoryTab", settings.showMemoryTab);
    qsettings.setValue("showDiskTab", settings.showDiskTab);
    qsettings.setValue("showNetworkTab", settings.showNetworkTab);
    qsettings.setValue("showBatteryTab", settings.showBatteryTab);
    qsettings.setValue("showProcessTab", settings.showProcessTab);
    
    // Alerts
    qsettings.setValue("alertsEnabled", settings.alertsEnabled);
    qsettings.setValue("cpuAlertThreshold", settings.cpuAlertThreshold);
    qsettings.setValue("memoryAlertThreshold", settings.memoryAlertThreshold);
    qsettings.setValue("batteryAlertThreshold", settings.batteryAlertThreshold);
    qsettings.setValue("temperatureAlertThreshold", settings.temperatureAlertThreshold);
    qsettings.setValue("alertSound", settings.alertSound);
    qsettings.setValue("alertCooldown", settings.alertCooldown);
    
    // Floating Widget
    qsettings.setValue("floatingOpacity", settings.floatingOpacity);
    qsettings.setValue("floatingShowCpu", settings.floatingShowCpu);
    qsettings.setValue("floatingShowMemory", settings.floatingShowMemory);
    qsettings.setValue("floatingShowGpu", settings.floatingShowGpu);
    qsettings.setValue("floatingShowBattery", settings.floatingShowBattery);
    qsettings.setValue("floatingShowGraphs", settings.floatingShowGraphs);
    qsettings.setValue("floatingShowTemps", settings.floatingShowTemps);
}

void SettingsDialog::applySettingsToUi()
{
    // General
    m_intervalSlider->setValue(m_settings.updateInterval);
    updateIntervalLabel(m_settings.updateInterval);
    m_startWithWindowsCheck->setChecked(m_settings.startWithWindows);
    m_minimizeToTrayCheck->setChecked(m_settings.minimizeToTray);
    m_startMinimizedCheck->setChecked(m_settings.startMinimized);
    
    // Appearance
    int themeIndex = m_themeCombo->findData(m_settings.theme);
    m_themeCombo->setCurrentIndex(themeIndex >= 0 ? themeIndex : 0);
    m_showCpuTabCheck->setChecked(m_settings.showCpuTab);
    m_showGpuTabCheck->setChecked(m_settings.showGpuTab);
    m_showMemoryTabCheck->setChecked(m_settings.showMemoryTab);
    m_showDiskTabCheck->setChecked(m_settings.showDiskTab);
    m_showNetworkTabCheck->setChecked(m_settings.showNetworkTab);
    m_showBatteryTabCheck->setChecked(m_settings.showBatteryTab);
    m_showProcessTabCheck->setChecked(m_settings.showProcessTab);
    
    // Alerts
    m_alertsEnabledCheck->setChecked(m_settings.alertsEnabled);
    m_cpuAlertSpin->setValue(m_settings.cpuAlertThreshold);
    m_memoryAlertSpin->setValue(m_settings.memoryAlertThreshold);
    m_batteryAlertSpin->setValue(m_settings.batteryAlertThreshold);
    m_tempAlertSpin->setValue(m_settings.temperatureAlertThreshold);
    m_alertSoundCheck->setChecked(m_settings.alertSound);
    m_alertCooldownSpin->setValue(m_settings.alertCooldown);
    
    // Enable/disable based on alerts enabled
    m_cpuAlertSpin->setEnabled(m_settings.alertsEnabled);
    m_memoryAlertSpin->setEnabled(m_settings.alertsEnabled);
    m_batteryAlertSpin->setEnabled(m_settings.alertsEnabled);
    m_tempAlertSpin->setEnabled(m_settings.alertsEnabled);
    m_alertSoundCheck->setEnabled(m_settings.alertsEnabled);
    m_alertCooldownSpin->setEnabled(m_settings.alertsEnabled);
    
    // Floating Widget
    m_opacitySlider->setValue(static_cast<int>(m_settings.floatingOpacity * 100));
    m_opacityValueLabel->setText(QString("%1%").arg(static_cast<int>(m_settings.floatingOpacity * 100)));
    m_floatingCpuCheck->setChecked(m_settings.floatingShowCpu);
    m_floatingMemoryCheck->setChecked(m_settings.floatingShowMemory);
    m_floatingGpuCheck->setChecked(m_settings.floatingShowGpu);
    m_floatingBatteryCheck->setChecked(m_settings.floatingShowBattery);
    m_floatingGraphsCheck->setChecked(m_settings.floatingShowGraphs);
    m_floatingTempsCheck->setChecked(m_settings.floatingShowTemps);
}

void SettingsDialog::collectSettingsFromUi()
{
    // General
    m_settings.updateInterval = m_intervalSlider->value();
    m_settings.startWithWindows = m_startWithWindowsCheck->isChecked();
    m_settings.minimizeToTray = m_minimizeToTrayCheck->isChecked();
    m_settings.startMinimized = m_startMinimizedCheck->isChecked();
    
    // Appearance
    m_settings.theme = m_themeCombo->currentData().toString();
    m_settings.showCpuTab = m_showCpuTabCheck->isChecked();
    m_settings.showGpuTab = m_showGpuTabCheck->isChecked();
    m_settings.showMemoryTab = m_showMemoryTabCheck->isChecked();
    m_settings.showDiskTab = m_showDiskTabCheck->isChecked();
    m_settings.showNetworkTab = m_showNetworkTabCheck->isChecked();
    m_settings.showBatteryTab = m_showBatteryTabCheck->isChecked();
    m_settings.showProcessTab = m_showProcessTabCheck->isChecked();
    
    // Alerts
    m_settings.alertsEnabled = m_alertsEnabledCheck->isChecked();
    m_settings.cpuAlertThreshold = m_cpuAlertSpin->value();
    m_settings.memoryAlertThreshold = m_memoryAlertSpin->value();
    m_settings.batteryAlertThreshold = m_batteryAlertSpin->value();
    m_settings.temperatureAlertThreshold = m_tempAlertSpin->value();
    m_settings.alertSound = m_alertSoundCheck->isChecked();
    m_settings.alertCooldown = m_alertCooldownSpin->value();
    
    // Floating Widget
    m_settings.floatingOpacity = m_opacitySlider->value() / 100.0;
    m_settings.floatingShowCpu = m_floatingCpuCheck->isChecked();
    m_settings.floatingShowMemory = m_floatingMemoryCheck->isChecked();
    m_settings.floatingShowGpu = m_floatingGpuCheck->isChecked();
    m_settings.floatingShowBattery = m_floatingBatteryCheck->isChecked();
    m_settings.floatingShowGraphs = m_floatingGraphsCheck->isChecked();
    m_settings.floatingShowTemps = m_floatingTempsCheck->isChecked();
}

void SettingsDialog::onAccept()
{
    collectSettingsFromUi();
    saveSettings(m_settings);
    
    // Handle startup registry
    if (m_settings.startWithWindows != m_originalSettings.startWithWindows) {
        updateStartupRegistry(m_settings.startWithWindows);
    }
    
    // Check if theme changed
    if (m_settings.theme != m_originalSettings.theme) {
        emit themeChanged(m_settings.theme);
    }
    
    emit settingsChanged(m_settings);
    accept();
}

void SettingsDialog::onApply()
{
    collectSettingsFromUi();
    saveSettings(m_settings);
    
    // Handle startup registry
    if (m_settings.startWithWindows != m_originalSettings.startWithWindows) {
        updateStartupRegistry(m_settings.startWithWindows);
        m_originalSettings.startWithWindows = m_settings.startWithWindows;
    }
    
    // Check if theme changed
    if (m_settings.theme != m_originalSettings.theme) {
        emit themeChanged(m_settings.theme);
        m_originalSettings.theme = m_settings.theme;
    }
    
    emit settingsChanged(m_settings);
}

void SettingsDialog::onRestoreDefaults()
{
    auto reply = QMessageBox::question(this, tr("Restore Defaults"),
        tr("Are you sure you want to restore all settings to their default values?"),
        QMessageBox::Yes | QMessageBox::No, QMessageBox::No);
    
    if (reply == QMessageBox::Yes) {
        m_settings = AppSettings();
        applySettingsToUi();
    }
}

void SettingsDialog::onThemeChanged(int index)
{
    Q_UNUSED(index);
    // Preview is optional, could show a note about restart
}

void SettingsDialog::updateIntervalLabel(int value)
{
    if (value >= 1000) {
        m_intervalValueLabel->setText(QString("%1 s").arg(value / 1000.0, 0, 'f', 1));
    } else {
        m_intervalValueLabel->setText(QString("%1 ms").arg(value));
    }
}

void SettingsDialog::updateStartupRegistry(bool enable)
{
#ifdef _WIN32
    HKEY hKey;
    const wchar_t* keyPath = L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
    const wchar_t* valueName = L"PerfMonitorQt";
    
    if (RegOpenKeyExW(HKEY_CURRENT_USER, keyPath, 0, KEY_SET_VALUE | KEY_QUERY_VALUE, &hKey) == ERROR_SUCCESS) {
        if (enable) {
            wchar_t exePath[MAX_PATH];
            GetModuleFileNameW(nullptr, exePath, MAX_PATH);
            
            // Add --minimized flag if start minimized is enabled
            std::wstring command = L"\"";
            command += exePath;
            command += L"\"";
            if (m_settings.startMinimized) {
                command += L" --minimized";
            }
            
            RegSetValueExW(hKey, valueName, 0, REG_SZ, 
                          reinterpret_cast<const BYTE*>(command.c_str()),
                          static_cast<DWORD>((command.length() + 1) * sizeof(wchar_t)));
        } else {
            RegDeleteValueW(hKey, valueName);
        }
        RegCloseKey(hKey);
    }
#endif
}

bool SettingsDialog::isInStartupRegistry() const
{
#ifdef _WIN32
    HKEY hKey;
    const wchar_t* keyPath = L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
    const wchar_t* valueName = L"PerfMonitorQt";
    
    if (RegOpenKeyExW(HKEY_CURRENT_USER, keyPath, 0, KEY_QUERY_VALUE, &hKey) == ERROR_SUCCESS) {
        DWORD type;
        DWORD size = 0;
        bool exists = (RegQueryValueExW(hKey, valueName, nullptr, &type, nullptr, &size) == ERROR_SUCCESS);
        RegCloseKey(hKey);
        return exists;
    }
#endif
    return false;
}
