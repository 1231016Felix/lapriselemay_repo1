#include "mainwindow.h"

#ifdef _WIN32
#include <Windows.h>
#include <shellapi.h>
#endif

#include "mainwindow.h"
#include "widgets/energymodedialog.h"
#include "widgets/startupdialog.h"
#include "widgets/settingsdialog.h"
#include "widgets/cleanerdialog.h"
#include "widgets/storagehealthdialog.h"
#include "widgets/detailedmemorydialog.h"
#include "widgets/toolswidget.h"
#include "widgets/diskscannerdialog.h"
#include "widgets/servicesdialog.h"
#include "widgets/historydialog.h"
#include "widgets/networkspeedtestdialog.h"
#include "widgets/processimpactdialog.h"
#include "monitors/processimpactmonitor.h"
#include "database/metricshistory.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QGroupBox>
#include <QMenuBar>
#include <QMenu>
#include <QAction>
#include <QStatusBar>
#include <QCloseEvent>
#include <QSettings>
#include <QMessageBox>
#include <QFileDialog>
#include <QTableView>
#include <QTreeView>
#include <QHeaderView>
#include <QLineEdit>
#include <QPushButton>
#include <QApplication>
#include <QClipboard>
#include <QStandardItemModel>
#include <QDateTime>
#include <QStyleFactory>
#include <QMenu>
#include <QDesktopServices>
#include <QUrl>

MainWindow::MainWindow(QWidget *parent)
    : QMainWindow(parent)
{
    setWindowTitle("PerfMonitorQt - Performance Monitor");
    setMinimumSize(900, 700);
    resize(1100, 800);
    
    // Check admin privileges first
    m_isAdmin = MemoryMonitor::isAdministrator();
    
    // Initialize Energy Mode Manager (stays in main thread)
    m_energyModeManager = std::make_unique<EnergyModeManager>();
    
    // Initialize Metrics History for persistent recording
    setupMetricsHistory();
    
    setupUi();
    setupMenuBar();
    setupStatusBar();
    setupTrayIcon();
    loadSettings();
    
    // Initialize background monitor worker
    m_monitorWorker = std::make_unique<MonitorWorker>();
    
    // Connect worker signal to UI update slot (Qt::QueuedConnection for thread safety)
    connect(m_monitorWorker.get(), &MonitorWorker::dataReady,
            this, &MainWindow::onMonitorDataReady, Qt::QueuedConnection);
    
    // Start the worker thread
    m_monitorWorker->start(m_updateInterval);
    
    // Show admin warning if not running as admin
    checkAdminPrivileges();
}

MainWindow::~MainWindow()
{
    saveSettings();
    
    // Stop the monitor worker
    if (m_monitorWorker) {
        m_monitorWorker->stop();
    }
    
    // Flush metrics history
    if (m_metricsHistory) {
        m_metricsHistory->flush();
    }
}

void MainWindow::setupMetricsHistory()
{
    m_metricsHistory = std::make_unique<MetricsHistory>();
    
    if (!m_metricsHistory->initialize()) {
        qWarning() << "Failed to initialize metrics history database";
    } else {
        qDebug() << "Metrics history initialized:" << m_metricsHistory->databasePath();
        
        // Set retention to 30 days by default
        m_metricsHistory->setRetentionDays(30);
    }
}

void MainWindow::recordMetrics()
{
    if (!m_metricsHistory || !m_metricsHistory->isReady()) {
        return;
    }
    
    // Record all metrics from the current monitor data
    std::vector<std::tuple<MetricType, double, QString>> metrics;
    
    // CPU
    metrics.emplace_back(MetricType::CpuUsage, m_monitorData.cpu.usage, QString());
    
    // CPU Temperature (if available)
    if (m_monitorData.temperature.hasTemperature) {
        metrics.emplace_back(MetricType::CpuTemperature, m_monitorData.temperature.cpuTemperature, QString());
    }
    
    // Memory
    metrics.emplace_back(MetricType::MemoryUsed, m_monitorData.memory.usedGB, QString());
    metrics.emplace_back(MetricType::MemoryAvailable, m_monitorData.memory.availableGB, QString());
    metrics.emplace_back(MetricType::MemoryCommit, m_monitorData.memory.committedGB, QString());
    
    // GPU
    metrics.emplace_back(MetricType::GpuUsage, m_monitorData.primaryGpu.usage, QString());
    metrics.emplace_back(MetricType::GpuMemory, m_monitorData.primaryGpu.memoryUsagePercent, QString());
    if (m_monitorData.primaryGpu.temperature > -900) {
        metrics.emplace_back(MetricType::GpuTemperature, m_monitorData.primaryGpu.temperature, QString());
    }
    
    // Disk I/O
    double diskReadMB = m_monitorData.diskActivity.readBytesPerSec / (1024.0 * 1024.0);
    double diskWriteMB = m_monitorData.diskActivity.writeBytesPerSec / (1024.0 * 1024.0);
    metrics.emplace_back(MetricType::DiskRead, diskReadMB, QString());
    metrics.emplace_back(MetricType::DiskWrite, diskWriteMB, QString());
    
    // Network
    double netSendMB = m_monitorData.networkActivity.sentBytesPerSec / (1024.0 * 1024.0);
    double netRecvMB = m_monitorData.networkActivity.receivedBytesPerSec / (1024.0 * 1024.0);
    metrics.emplace_back(MetricType::NetworkSend, netSendMB, QString());
    metrics.emplace_back(MetricType::NetworkReceive, netRecvMB, QString());
    
    // Battery (if available)
    if (m_monitorData.battery.hasBattery) {
        metrics.emplace_back(MetricType::BatteryPercent, m_monitorData.battery.percentage, QString());
        metrics.emplace_back(MetricType::BatteryHealth, m_monitorData.battery.healthPercent, QString());
    }
    
    m_metricsHistory->recordMetrics(metrics);
}

void MainWindow::setupUi()
{
    m_tabWidget = new QTabWidget(this);
    m_tabWidget->setDocumentMode(true);
    
    createCpuTab();
    createGpuTab();
    createMemoryTab();
    createDiskTab();
    createNetworkTab();
    createBatteryTab();
    createProcessTab();
    createToolsTab();
    
    setCentralWidget(m_tabWidget);
}

void MainWindow::setupMenuBar()
{
    // File menu
    auto fileMenu = menuBar()->addMenu(tr("&File"));
    
    auto exportAction = fileMenu->addAction(tr("&Export Report..."));
    exportAction->setShortcut(QKeySequence::Save);
    connect(exportAction, &QAction::triggered, this, &MainWindow::exportReport);
    
    fileMenu->addSeparator();
    
    auto exitAction = fileMenu->addAction(tr("E&xit"));
    exitAction->setShortcut(QKeySequence::Quit);
    connect(exitAction, &QAction::triggered, this, &QMainWindow::close);
    
    // View menu
    auto viewMenu = menuBar()->addMenu(tr("&View"));
    
    auto alwaysOnTopAction = viewMenu->addAction(tr("&Always on Top"));
    alwaysOnTopAction->setCheckable(true);
    alwaysOnTopAction->setChecked(m_alwaysOnTop);
    connect(alwaysOnTopAction, &QAction::triggered, this, &MainWindow::toggleAlwaysOnTop);
    
    viewMenu->addSeparator();
    
    m_floatingWidgetAction = viewMenu->addAction(tr("&Floating Widget"));
    m_floatingWidgetAction->setCheckable(true);
    m_floatingWidgetAction->setShortcut(QKeySequence(Qt::CTRL | Qt::Key_F));
    connect(m_floatingWidgetAction, &QAction::triggered, this, &MainWindow::toggleFloatingWidget);
    
    viewMenu->addSeparator();
    
    // Metrics History in View menu
    auto historyAction = viewMenu->addAction(tr("📊 &Metrics History..."));
    historyAction->setShortcut(QKeySequence(Qt::CTRL | Qt::Key_H));
    connect(historyAction, &QAction::triggered, this, &MainWindow::showMetricsHistory);
    
    // Tools menu
    auto toolsMenu = menuBar()->addMenu(tr("&Tools"));
    
    // === System Optimization ===
    m_energyModeAction = toolsMenu->addAction(tr("⚡ &Energy Mode"));
    m_energyModeAction->setShortcut(QKeySequence(Qt::CTRL | Qt::Key_E));
    m_energyModeAction->setCheckable(true);
    m_energyModeAction->setChecked(m_energyModeManager->isActive());
    connect(m_energyModeAction, &QAction::triggered, this, &MainWindow::toggleEnergyMode);
    
    auto energyModeConfigAction = toolsMenu->addAction(tr("    Configure Energy Mode..."));
    connect(energyModeConfigAction, &QAction::triggered, this, &MainWindow::showEnergyModeDialog);
    
    auto purgeMemoryAction = toolsMenu->addAction(tr("🧹 &Purge Memory"));
    purgeMemoryAction->setToolTip(tr("Free up system memory (requires Admin)"));
    connect(purgeMemoryAction, &QAction::triggered, this, &MainWindow::purgeMemory);
    
    toolsMenu->addSeparator();
    
    // === Services Manager (NEW) ===
    auto servicesAction = toolsMenu->addAction(tr("⚙️ &Services Manager..."));
    servicesAction->setShortcut(QKeySequence(Qt::CTRL | Qt::SHIFT | Qt::Key_V));
    servicesAction->setToolTip(tr("View and manage Windows services"));
    connect(servicesAction, &QAction::triggered, this, &MainWindow::showServicesManager);
    
    // === Startup & Cleaning ===
    auto startupManagerAction = toolsMenu->addAction(tr("🚀 &Startup Manager..."));
    startupManagerAction->setShortcut(QKeySequence(Qt::CTRL | Qt::SHIFT | Qt::Key_S));
    startupManagerAction->setToolTip(tr("Manage programs that run at Windows startup"));
    connect(startupManagerAction, &QAction::triggered, this, [this]() {
        StartupDialog dialog(this);
        dialog.exec();
    });
    
    auto cleanerAction = toolsMenu->addAction(tr("🗑️ System &Cleaner..."));
    cleanerAction->setShortcut(QKeySequence(Qt::CTRL | Qt::Key_L));
    cleanerAction->setToolTip(tr("Clean temporary files, browser cache, and other junk"));
    connect(cleanerAction, &QAction::triggered, this, [this]() {
        CleanerDialog dialog(this);
        dialog.exec();
    });
    
    auto diskScannerAction = toolsMenu->addAction(tr("📁 &Disk Scanner..."));
    diskScannerAction->setToolTip(tr("Analyze disk usage and find large files"));
    connect(diskScannerAction, &QAction::triggered, this, &MainWindow::showDiskScanner);
    
    // === Network Tools ===
    auto networkSpeedTestAction = toolsMenu->addAction(tr("🌐 &Network Speed Test..."));
    networkSpeedTestAction->setShortcut(QKeySequence(Qt::CTRL | Qt::Key_T));
    networkSpeedTestAction->setToolTip(tr("Test internet download, upload and latency"));
    connect(networkSpeedTestAction, &QAction::triggered, this, &MainWindow::showNetworkSpeedTest);
    
    toolsMenu->addSeparator();
    
    // === Analysis & Diagnostics ===
    auto storageHealthAction = toolsMenu->addAction(tr("💾 Storage &Health..."));
    storageHealthAction->setToolTip(tr("Check SSD/HDD health with S.M.A.R.T. data"));
    connect(storageHealthAction, &QAction::triggered, this, [this]() {
        StorageHealthDialog dialog(this);
        dialog.exec();
    });
    
    auto detailedMemoryAction = toolsMenu->addAction(tr("🧠 Detailed &Memory..."));
    detailedMemoryAction->setShortcut(QKeySequence(Qt::CTRL | Qt::Key_M));
    detailedMemoryAction->setToolTip(tr("Detailed RAM usage, working set analysis, and memory leak detection"));
    connect(detailedMemoryAction, &QAction::triggered, this, [this]() {
        DetailedMemoryDialog dialog(this);
        dialog.exec();
    });
    
    // Settings menu (direct access to settings)
    auto settingsMenu = menuBar()->addMenu(tr("&Settings"));
    auto settingsAction = settingsMenu->addAction(tr("⚙️ &Preferences..."));
    settingsAction->setShortcut(QKeySequence(Qt::CTRL | Qt::Key_Comma));
    connect(settingsAction, &QAction::triggered, this, &MainWindow::showSettings);
    
    // Help menu
    auto helpMenu = menuBar()->addMenu(tr("&Help"));
    
    auto aboutAction = helpMenu->addAction(tr("&About"));
    connect(aboutAction, &QAction::triggered, this, &MainWindow::showAbout);
    
    auto aboutQtAction = helpMenu->addAction(tr("About &Qt"));
    connect(aboutQtAction, &QAction::triggered, qApp, &QApplication::aboutQt);
}

void MainWindow::setupStatusBar()
{
    // Admin status indicator
    m_adminStatusLabel = new QLabel();
    if (m_isAdmin) {
        m_adminStatusLabel->setText(QString::fromUtf8("\xF0\x9F\x9B\xA1 Admin"));
        m_adminStatusLabel->setStyleSheet("color: #00aa00; font-weight: bold;");
        m_adminStatusLabel->setToolTip(tr("Running with administrator privileges"));
    } else {
        m_adminStatusLabel->setText(QString::fromUtf8("\xE2\x9A\xA0 No Admin"));
        m_adminStatusLabel->setStyleSheet("color: #ffaa00; font-weight: bold;");
        m_adminStatusLabel->setToolTip(tr("Running without administrator privileges - some features limited"));
    }
    
    m_cpuStatusLabel = new QLabel("CPU: ---%");
    m_gpuStatusLabel = new QLabel("GPU: ---%");
    m_memStatusLabel = new QLabel("Memory: ---%");
    m_tempStatusLabel = new QLabel("Temp: ---");
    m_batteryStatusLabel = new QLabel("Battery: ---%");
    
    // Energy Mode status
    m_energyModeStatusLabel = new QLabel();
    if (m_energyModeManager->isActive()) {
        m_energyModeStatusLabel->setText("⚡ Mode Énergie");
        m_energyModeStatusLabel->setStyleSheet("color: #00cc00; font-weight: bold;");
    } else {
        m_energyModeStatusLabel->setText("");
    }
    m_energyModeStatusLabel->setToolTip(tr("Mode Énergie - Cliquez pour activer/désactiver"));
    
    statusBar()->addWidget(m_adminStatusLabel);
    statusBar()->addWidget(m_energyModeStatusLabel);
    statusBar()->addPermanentWidget(m_cpuStatusLabel);
    statusBar()->addPermanentWidget(m_gpuStatusLabel);
    statusBar()->addPermanentWidget(m_memStatusLabel);
    statusBar()->addPermanentWidget(m_tempStatusLabel);
    statusBar()->addPermanentWidget(m_batteryStatusLabel);
}

void MainWindow::setupTrayIcon()
{
    m_trayManager = std::make_unique<SystemTrayManager>(this);
    
    connect(m_trayManager.get(), &SystemTrayManager::activated,
            this, &MainWindow::onTrayActivated);
    connect(m_trayManager.get(), &SystemTrayManager::showRequested,
            this, &QMainWindow::show);
    connect(m_trayManager.get(), &SystemTrayManager::exitRequested,
            this, &MainWindow::onTrayExitRequested);
}

void MainWindow::onTrayExitRequested()
{
    m_forceQuit = true;
    close();
}

void MainWindow::createCpuTab()
{
    m_cpuTab = new QWidget();
    auto layout = new QVBoxLayout(m_cpuTab);
    layout->setSpacing(15);
    
    // CPU Info Group
    auto infoGroup = new QGroupBox(tr("Processor Information"));
    auto infoLayout = new QGridLayout(infoGroup);
    
    m_cpuNameLabel = new QLabel("---");
    m_cpuNameLabel->setStyleSheet("font-size: 14px; font-weight: bold;");
    infoLayout->addWidget(new QLabel(tr("Name:")), 0, 0);
    infoLayout->addWidget(m_cpuNameLabel, 0, 1, 1, 3);
    
    m_cpuCoresLabel = new QLabel("---");
    infoLayout->addWidget(new QLabel(tr("Cores:")), 1, 0);
    infoLayout->addWidget(m_cpuCoresLabel, 1, 1);
    
    m_cpuSpeedLabel = new QLabel("---");
    infoLayout->addWidget(new QLabel(tr("Speed:")), 1, 2);
    infoLayout->addWidget(m_cpuSpeedLabel, 1, 3);
    
    m_cpuProcessesLabel = new QLabel("---");
    infoLayout->addWidget(new QLabel(tr("Processes:")), 2, 0);
    infoLayout->addWidget(m_cpuProcessesLabel, 2, 1);
    
    m_cpuThreadsLabel = new QLabel("---");
    infoLayout->addWidget(new QLabel(tr("Threads:")), 2, 2);
    infoLayout->addWidget(m_cpuThreadsLabel, 2, 3);
    
    m_cpuUptimeLabel = new QLabel("---");
    infoLayout->addWidget(new QLabel(tr("Uptime:")), 3, 0);
    infoLayout->addWidget(m_cpuUptimeLabel, 3, 1);
    
    // Temperature (if available)
    m_cpuTempLabel = new QLabel("---");
    m_cpuTempLabel->setStyleSheet("font-weight: bold;");
    infoLayout->addWidget(new QLabel(tr("CPU Temp:")), 3, 2);
    infoLayout->addWidget(m_cpuTempLabel, 3, 3);
    
    m_chassisTempLabel = new QLabel("---");
    m_chassisTempLabel->setStyleSheet("font-weight: bold;");
    infoLayout->addWidget(new QLabel(tr("Chassis:")), 4, 0);
    infoLayout->addWidget(m_chassisTempLabel, 4, 1);
    
    layout->addWidget(infoGroup);
    
    // Usage Group
    auto usageGroup = new QGroupBox(tr("CPU Usage"));
    auto usageLayout = new QVBoxLayout(usageGroup);
    
    auto usageTopLayout = new QHBoxLayout();
    m_cpuUsageLabel = new QLabel("0%");
    m_cpuUsageLabel->setStyleSheet("font-size: 36px; font-weight: bold; color: #0078d7;");
    usageTopLayout->addWidget(m_cpuUsageLabel);
    usageTopLayout->addStretch();
    usageLayout->addLayout(usageTopLayout);
    
    m_cpuProgressBar = new QProgressBar();
    m_cpuProgressBar->setRange(0, 100);
    m_cpuProgressBar->setTextVisible(true);
    m_cpuProgressBar->setMinimumHeight(25);
    usageLayout->addWidget(m_cpuProgressBar);
    
    m_cpuGraph = new SparklineGraph(60, QColor(0, 120, 215));
    m_cpuGraph->setMinimumHeight(150);
    usageLayout->addWidget(m_cpuGraph);
    
    layout->addWidget(usageGroup);
    layout->addStretch();
    
    m_tabWidget->addTab(m_cpuTab, tr("CPU"));
}

void MainWindow::createGpuTab()
{
    m_gpuTab = new QWidget();
    auto layout = new QVBoxLayout(m_gpuTab);
    layout->setSpacing(15);
    
    // GPU Info Group
    auto infoGroup = new QGroupBox(tr("Graphics Card Information"));
    auto infoLayout = new QGridLayout(infoGroup);
    
    m_gpuNameLabel = new QLabel("---");
    m_gpuNameLabel->setStyleSheet("font-size: 14px; font-weight: bold;");
    m_gpuNameLabel->setWordWrap(true);
    infoLayout->addWidget(new QLabel(tr("Name:")), 0, 0);
    infoLayout->addWidget(m_gpuNameLabel, 0, 1, 1, 3);
    
    m_gpuVendorLabel = new QLabel("---");
    infoLayout->addWidget(new QLabel(tr("Vendor:")), 1, 0);
    infoLayout->addWidget(m_gpuVendorLabel, 1, 1);
    
    m_gpuTempLabel = new QLabel("N/A");
    infoLayout->addWidget(new QLabel(tr("Temperature:")), 1, 2);
    infoLayout->addWidget(m_gpuTempLabel, 1, 3);
    
    layout->addWidget(infoGroup);
    
    // GPU Usage Group
    auto usageGroup = new QGroupBox(tr("GPU Usage"));
    auto usageLayout = new QVBoxLayout(usageGroup);
    
    auto usageTopLayout = new QHBoxLayout();
    m_gpuUsageLabel = new QLabel("0%");
    m_gpuUsageLabel->setStyleSheet("font-size: 36px; font-weight: bold; color: #76b900;");
    usageTopLayout->addWidget(m_gpuUsageLabel);
    usageTopLayout->addStretch();
    usageLayout->addLayout(usageTopLayout);
    
    m_gpuUsageProgressBar = new QProgressBar();
    m_gpuUsageProgressBar->setRange(0, 100);
    m_gpuUsageProgressBar->setTextVisible(true);
    m_gpuUsageProgressBar->setMinimumHeight(25);
    m_gpuUsageProgressBar->setStyleSheet(
        "QProgressBar { border: 1px solid grey; border-radius: 3px; text-align: center; }"
        "QProgressBar::chunk { background-color: #76b900; }"
    );
    usageLayout->addWidget(m_gpuUsageProgressBar);
    
    m_gpuUsageGraph = new SparklineGraph(60, QColor(118, 185, 0));
    m_gpuUsageGraph->setMinimumHeight(120);
    usageLayout->addWidget(m_gpuUsageGraph);
    
    layout->addWidget(usageGroup);
    
    // GPU Memory Group
    auto memoryGroup = new QGroupBox(tr("Video Memory (VRAM)"));
    auto memoryLayout = new QVBoxLayout(memoryGroup);
    
    auto memInfoLayout = new QHBoxLayout();
    m_gpuMemoryUsedLabel = new QLabel("0 MB");
    m_gpuMemoryUsedLabel->setStyleSheet("font-weight: bold;");
    memInfoLayout->addWidget(new QLabel(tr("Used:")));
    memInfoLayout->addWidget(m_gpuMemoryUsedLabel);
    memInfoLayout->addSpacing(20);
    m_gpuMemoryTotalLabel = new QLabel("0 MB");
    m_gpuMemoryTotalLabel->setStyleSheet("font-weight: bold;");
    memInfoLayout->addWidget(new QLabel(tr("Total:")));
    memInfoLayout->addWidget(m_gpuMemoryTotalLabel);
    memInfoLayout->addStretch();
    memoryLayout->addLayout(memInfoLayout);
    
    m_gpuMemoryProgressBar = new QProgressBar();
    m_gpuMemoryProgressBar->setRange(0, 100);
    m_gpuMemoryProgressBar->setTextVisible(true);
    m_gpuMemoryProgressBar->setMinimumHeight(25);
    m_gpuMemoryProgressBar->setStyleSheet(
        "QProgressBar { border: 1px solid grey; border-radius: 3px; text-align: center; }"
        "QProgressBar::chunk { background-color: #e535ab; }"
    );
    memoryLayout->addWidget(m_gpuMemoryProgressBar);
    
    m_gpuMemoryGraph = new SparklineGraph(60, QColor(229, 53, 171));
    m_gpuMemoryGraph->setMinimumHeight(120);
    memoryLayout->addWidget(m_gpuMemoryGraph);
    
    layout->addWidget(memoryGroup);
    
    // GPU List (if multiple GPUs)
    auto listGroup = new QGroupBox(tr("All Graphics Adapters"));
    auto listLayout = new QVBoxLayout(listGroup);
    
    m_gpuTableView = new QTableView();
    m_gpuTableView->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_gpuTableView->setAlternatingRowColors(true);
    m_gpuTableView->horizontalHeader()->setStretchLastSection(true);
    m_gpuTableView->verticalHeader()->setVisible(false);
    m_gpuTableView->setMaximumHeight(120);
    listLayout->addWidget(m_gpuTableView);
    
    layout->addWidget(listGroup);
    layout->addStretch();
    
    m_tabWidget->addTab(m_gpuTab, tr("GPU"));
}

void MainWindow::createMemoryTab()
{
    m_memoryTab = new QWidget();
    auto layout = new QVBoxLayout(m_memoryTab);
    layout->setSpacing(15);
    
    // Memory Info Group
    auto infoGroup = new QGroupBox(tr("Memory Information"));
    auto infoLayout = new QGridLayout(infoGroup);
    
    m_memUsageLabel = new QLabel("---");
    m_memUsageLabel->setStyleSheet("font-size: 14px; font-weight: bold;");
    infoLayout->addWidget(new QLabel(tr("In Use:")), 0, 0);
    infoLayout->addWidget(m_memUsageLabel, 0, 1);
    
    m_memAvailableLabel = new QLabel("---");
    infoLayout->addWidget(new QLabel(tr("Available:")), 0, 2);
    infoLayout->addWidget(m_memAvailableLabel, 0, 3);
    
    m_memCommittedLabel = new QLabel("---");
    infoLayout->addWidget(new QLabel(tr("Committed:")), 1, 0);
    infoLayout->addWidget(m_memCommittedLabel, 1, 1);
    
    m_memCachedLabel = new QLabel("---");
    infoLayout->addWidget(new QLabel(tr("Cached:")), 1, 2);
    infoLayout->addWidget(m_memCachedLabel, 1, 3);
    
    m_memPagedLabel = new QLabel("---");
    infoLayout->addWidget(new QLabel(tr("Paged Pool:")), 2, 0);
    infoLayout->addWidget(m_memPagedLabel, 2, 1);
    
    layout->addWidget(infoGroup);
    
    // Usage Group
    auto usageGroup = new QGroupBox(tr("Memory Usage"));
    auto usageLayout = new QVBoxLayout(usageGroup);
    
    m_memProgressBar = new QProgressBar();
    m_memProgressBar->setRange(0, 100);
    m_memProgressBar->setTextVisible(true);
    m_memProgressBar->setMinimumHeight(25);
    usageLayout->addWidget(m_memProgressBar);
    
    m_memGraph = new SparklineGraph(60, QColor(139, 0, 139));
    m_memGraph->setMinimumHeight(150);
    usageLayout->addWidget(m_memGraph);
    
    layout->addWidget(usageGroup);
    layout->addStretch();
    
    m_tabWidget->addTab(m_memoryTab, tr("Memory"));
}

void MainWindow::createDiskTab()
{
    m_diskTab = new QWidget();
    auto layout = new QVBoxLayout(m_diskTab);
    layout->setSpacing(15);
    
    // Disk List
    auto listGroup = new QGroupBox(tr("Disk Drives"));
    auto listLayout = new QVBoxLayout(listGroup);
    
    m_diskTableView = new QTableView();
    m_diskTableView->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_diskTableView->setAlternatingRowColors(true);
    m_diskTableView->horizontalHeader()->setStretchLastSection(true);
    m_diskTableView->verticalHeader()->setVisible(false);
    listLayout->addWidget(m_diskTableView);
    
    layout->addWidget(listGroup);
    
    // Activity Group
    auto activityGroup = new QGroupBox(tr("Disk Activity"));
    auto activityLayout = new QGridLayout(activityGroup);
    
    activityLayout->addWidget(new QLabel(tr("Read:")), 0, 0);
    m_diskReadLabel = new QLabel("0 B/s");
    m_diskReadLabel->setStyleSheet("font-weight: bold; color: #00aa00;");
    activityLayout->addWidget(m_diskReadLabel, 0, 1);
    
    m_diskReadGraph = new SparklineGraph(60, QColor(0, 170, 0));
    m_diskReadGraph->setMinimumHeight(80);
    activityLayout->addWidget(m_diskReadGraph, 1, 0, 1, 2);
    
    activityLayout->addWidget(new QLabel(tr("Write:")), 2, 0);
    m_diskWriteLabel = new QLabel("0 B/s");
    m_diskWriteLabel->setStyleSheet("font-weight: bold; color: #cc6600;");
    activityLayout->addWidget(m_diskWriteLabel, 2, 1);
    
    m_diskWriteGraph = new SparklineGraph(60, QColor(204, 102, 0));
    m_diskWriteGraph->setMinimumHeight(80);
    activityLayout->addWidget(m_diskWriteGraph, 3, 0, 1, 2);
    
    layout->addWidget(activityGroup);
    
    m_tabWidget->addTab(m_diskTab, tr("Disk"));
}

void MainWindow::createNetworkTab()
{
    m_networkTab = new QWidget();
    auto layout = new QVBoxLayout(m_networkTab);
    layout->setSpacing(15);
    
    // Network Adapters
    auto adaptersGroup = new QGroupBox(tr("Network Adapters"));
    auto adaptersLayout = new QVBoxLayout(adaptersGroup);
    
    m_networkTableView = new QTableView();
    m_networkTableView->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_networkTableView->setAlternatingRowColors(true);
    m_networkTableView->horizontalHeader()->setStretchLastSection(true);
    m_networkTableView->verticalHeader()->setVisible(false);
    adaptersLayout->addWidget(m_networkTableView);
    
    layout->addWidget(adaptersGroup);
    
    // Network Activity
    auto activityGroup = new QGroupBox(tr("Network Activity"));
    auto activityLayout = new QGridLayout(activityGroup);
    
    activityLayout->addWidget(new QLabel(tr("Send:")), 0, 0);
    m_netSendLabel = new QLabel("0 B/s");
    m_netSendLabel->setStyleSheet("font-weight: bold; color: #cc6600;");
    activityLayout->addWidget(m_netSendLabel, 0, 1);
    
    m_netSendGraph = new SparklineGraph(60, QColor(204, 102, 0));
    m_netSendGraph->setMinimumHeight(80);
    activityLayout->addWidget(m_netSendGraph, 1, 0, 1, 2);
    
    activityLayout->addWidget(new QLabel(tr("Receive:")), 2, 0);
    m_netRecvLabel = new QLabel("0 B/s");
    m_netRecvLabel->setStyleSheet("font-weight: bold; color: #00aa00;");
    activityLayout->addWidget(m_netRecvLabel, 2, 1);
    
    m_netRecvGraph = new SparklineGraph(60, QColor(0, 170, 0));
    m_netRecvGraph->setMinimumHeight(80);
    activityLayout->addWidget(m_netRecvGraph, 3, 0, 1, 2);
    
    layout->addWidget(activityGroup);
    
    m_tabWidget->addTab(m_networkTab, tr("Network"));
}

void MainWindow::createBatteryTab()
{
    m_batteryTab = new QWidget();
    auto layout = new QVBoxLayout(m_batteryTab);
    layout->setSpacing(15);
    
    // Battery Status
    auto statusGroup = new QGroupBox(tr("Battery Status"));
    auto statusLayout = new QGridLayout(statusGroup);
    
    m_batteryPercentLabel = new QLabel("---%");
    m_batteryPercentLabel->setStyleSheet("font-size: 48px; font-weight: bold; color: #00aa00;");
    statusLayout->addWidget(m_batteryPercentLabel, 0, 0, 2, 1);
    
    m_batteryStatusLabel2 = new QLabel("---");
    statusLayout->addWidget(new QLabel(tr("Status:")), 0, 1);
    statusLayout->addWidget(m_batteryStatusLabel2, 0, 2);
    
    m_batteryTimeLabel = new QLabel("---");
    statusLayout->addWidget(new QLabel(tr("Time Remaining:")), 1, 1);
    statusLayout->addWidget(m_batteryTimeLabel, 1, 2);
    
    m_batteryProgressBar = new QProgressBar();
    m_batteryProgressBar->setRange(0, 100);
    m_batteryProgressBar->setTextVisible(true);
    m_batteryProgressBar->setMinimumHeight(30);
    statusLayout->addWidget(m_batteryProgressBar, 2, 0, 1, 3);
    
    layout->addWidget(statusGroup);
    
    // Battery Details (Surface-specific)
    auto detailsGroup = new QGroupBox(tr("Battery Details (Surface)"));
    auto detailsLayout = new QGridLayout(detailsGroup);
    
    m_batteryHealthLabel = new QLabel("---");
    detailsLayout->addWidget(new QLabel(tr("Health:")), 0, 0);
    detailsLayout->addWidget(m_batteryHealthLabel, 0, 1);
    
    m_batteryCyclesLabel = new QLabel("---");
    detailsLayout->addWidget(new QLabel(tr("Cycle Count:")), 0, 2);
    detailsLayout->addWidget(m_batteryCyclesLabel, 0, 3);
    
    m_batteryCapacityLabel = new QLabel("---");
    detailsLayout->addWidget(new QLabel(tr("Capacity:")), 1, 0);
    detailsLayout->addWidget(m_batteryCapacityLabel, 1, 1);
    
    m_batteryVoltageLabel = new QLabel("---");
    detailsLayout->addWidget(new QLabel(tr("Voltage:")), 1, 2);
    detailsLayout->addWidget(m_batteryVoltageLabel, 1, 3);
    
    m_batteryTempLabel = new QLabel("---");
    detailsLayout->addWidget(new QLabel(tr("Temperature:")), 2, 0);
    detailsLayout->addWidget(m_batteryTempLabel, 2, 1);
    
    layout->addWidget(detailsGroup);
    
    // Battery Graph
    auto graphGroup = new QGroupBox(tr("Battery History"));
    auto graphLayout = new QVBoxLayout(graphGroup);
    
    m_batteryGraph = new SparklineGraph(120, QColor(0, 170, 0));
    m_batteryGraph->setMinimumHeight(150);
    graphLayout->addWidget(m_batteryGraph);
    
    layout->addWidget(graphGroup);
    layout->addStretch();
    
    m_tabWidget->addTab(m_batteryTab, tr("Battery"));
}

void MainWindow::createProcessTab()
{
    m_processTab = new QWidget();
    auto layout = new QVBoxLayout(m_processTab);
    layout->setContentsMargins(5, 5, 5, 5);
    
    m_processWidget = new AdvancedProcessWidget();
    layout->addWidget(m_processWidget);
    
    // Connect status messages to status bar
    connect(m_processWidget, &AdvancedProcessWidget::statusMessage,
            this, [this](const QString& msg, int timeout) {
        statusBar()->showMessage(msg, timeout);
    });
    
    m_tabWidget->addTab(m_processTab, tr("Processes"));
}

void MainWindow::createToolsTab()
{
    m_toolsTab = new QWidget();
    m_toolsTab->setStyleSheet("background-color: #1e1e24;");
    
    auto layout = new QVBoxLayout(m_toolsTab);
    layout->setContentsMargins(0, 0, 0, 0);
    
    auto toolsWidget = new ToolsWidget();
    layout->addWidget(toolsWidget);
    
    // Connect tool signals
    connect(toolsWidget, &ToolsWidget::startupManagerRequested, this, [this]() {
        StartupDialog dialog(this);
        dialog.exec();
    });
    
    connect(toolsWidget, &ToolsWidget::systemCleanerRequested, this, [this]() {
        CleanerDialog dialog(this);
        dialog.exec();
    });
    
    connect(toolsWidget, &ToolsWidget::storageHealthRequested, this, [this]() {
        StorageHealthDialog dialog(this);
        dialog.exec();
    });
    
    connect(toolsWidget, &ToolsWidget::detailedMemoryRequested, this, [this]() {
        DetailedMemoryDialog dialog(this);
        dialog.exec();
    });
    
    connect(toolsWidget, &ToolsWidget::energyModeRequested, this, &MainWindow::toggleEnergyMode);
    
    connect(toolsWidget, &ToolsWidget::energyModeConfigRequested, this, &MainWindow::showEnergyModeDialog);
    
    connect(toolsWidget, &ToolsWidget::purgeMemoryRequested, this, &MainWindow::purgeMemory);
    
    // New features connections
    connect(toolsWidget, &ToolsWidget::servicesManagerRequested, this, &MainWindow::showServicesManager);
    connect(toolsWidget, &ToolsWidget::metricsHistoryRequested, this, &MainWindow::showMetricsHistory);
    connect(toolsWidget, &ToolsWidget::diskScannerRequested, this, &MainWindow::showDiskScanner);
    connect(toolsWidget, &ToolsWidget::networkSpeedTestRequested, this, &MainWindow::showNetworkSpeedTest);
    connect(toolsWidget, &ToolsWidget::processImpactRequested, this, &MainWindow::showProcessImpact);
    
    m_tabWidget->addTab(m_toolsTab, tr("🧰 Tools"));
}

// ============= New Feature Slots =============

void MainWindow::showServicesManager()
{
    ServicesDialog dialog(this);
    dialog.exec();
}

void MainWindow::showMetricsHistory()
{
    if (!m_metricsHistory || !m_metricsHistory->isReady()) {
        QMessageBox::warning(this, tr("Metrics History"),
            tr("Metrics history is not available. The database may not be initialized."));
        return;
    }
    
    HistoryDialog dialog(m_metricsHistory.get(), this);
    dialog.exec();
}

void MainWindow::showDiskScanner()
{
    DiskScannerDialog dialog(this);
    dialog.exec();
}

void MainWindow::showNetworkSpeedTest()
{
    NetworkSpeedTestDialog dialog(this);
    dialog.exec();
}

void MainWindow::showProcessImpact()
{
    ProcessImpactDialog dialog(this);
    dialog.exec();
}

// ============================================

void MainWindow::onMonitorDataReady(const MonitorData& data)
{
    // Store the data for other uses
    m_monitorData = data;
    
    // Record metrics to history database
    recordMetrics();
    
    // Update CPU UI
    const auto& cpuInfo = data.cpu;
    m_cpuNameLabel->setText(cpuInfo.name);
    m_cpuUsageLabel->setText(QString("%1%").arg(cpuInfo.usage, 0, 'f', 1));
    m_cpuSpeedLabel->setText(QString("%1 GHz").arg(cpuInfo.currentSpeed, 0, 'f', 2));
    m_cpuCoresLabel->setText(QString("%1 / %2").arg(cpuInfo.cores).arg(cpuInfo.logicalProcessors));
    m_cpuProcessesLabel->setText(QString::number(cpuInfo.processCount));
    m_cpuThreadsLabel->setText(QString::number(cpuInfo.threadCount));
    m_cpuUptimeLabel->setText(cpuInfo.uptime);
    m_cpuProgressBar->setValue(static_cast<int>(cpuInfo.usage));
    m_cpuGraph->addValue(cpuInfo.usage);
    m_cpuStatusLabel->setText(QString("CPU: %1%").arg(cpuInfo.usage, 0, 'f', 0));
    
    // Update Temperature UI
    const auto& tempInfo = data.temperature;
    if (tempInfo.hasTemperature) {
        QString cpuTempColor;
        if (tempInfo.cpuTemperature >= 80) {
            cpuTempColor = "color: #ff0000;";
        } else if (tempInfo.cpuTemperature >= 60) {
            cpuTempColor = "color: #ff8c00;";
        } else {
            cpuTempColor = "color: #00aa00;";
        }
        m_cpuTempLabel->setText(QString("%1 °C").arg(tempInfo.cpuTemperature, 0, 'f', 1));
        m_cpuTempLabel->setStyleSheet(QString("font-weight: bold; %1").arg(cpuTempColor));
        
        if (tempInfo.chassisTemperature > -900) {
            m_chassisTempLabel->setText(QString("%1 °C").arg(tempInfo.chassisTemperature, 0, 'f', 1));
        } else {
            m_chassisTempLabel->setText("N/A");
        }
        m_tempStatusLabel->setText(QString("🌡 %1°C").arg(tempInfo.cpuTemperature, 0, 'f', 0));
        m_tempStatusLabel->setStyleSheet(cpuTempColor);
    } else {
        m_cpuTempLabel->setText("N/A");
        m_chassisTempLabel->setText("N/A");
        m_tempStatusLabel->setText("🌡 N/A");
    }
    
    // Update GPU UI
    const auto& gpuInfo = data.primaryGpu;
    m_gpuNameLabel->setText(gpuInfo.name);
    m_gpuVendorLabel->setText(gpuInfo.vendor);
    m_gpuUsageLabel->setText(QString("%1%").arg(gpuInfo.usage, 0, 'f', 1));
    m_gpuUsageProgressBar->setValue(static_cast<int>(gpuInfo.usage));
    m_gpuUsageProgressBar->setFormat(QString("%1%").arg(gpuInfo.usage, 0, 'f', 1));
    m_gpuUsageGraph->addValue(gpuInfo.usage);
    
    m_gpuMemoryUsedLabel->setText(GpuMonitor::formatMemory(gpuInfo.dedicatedMemoryUsed));
    m_gpuMemoryTotalLabel->setText(GpuMonitor::formatMemory(gpuInfo.dedicatedMemoryTotal));
    m_gpuMemoryProgressBar->setValue(static_cast<int>(gpuInfo.memoryUsagePercent));
    m_gpuMemoryProgressBar->setFormat(QString("%1% (%2 / %3)")
        .arg(gpuInfo.memoryUsagePercent, 0, 'f', 1)
        .arg(GpuMonitor::formatMemory(gpuInfo.dedicatedMemoryUsed))
        .arg(GpuMonitor::formatMemory(gpuInfo.dedicatedMemoryTotal)));
    m_gpuMemoryGraph->addValue(gpuInfo.memoryUsagePercent);
    
    if (gpuInfo.temperature > -900) {
        m_gpuTempLabel->setText(QString("%1 °C").arg(gpuInfo.temperature, 0, 'f', 0));
    } else {
        m_gpuTempLabel->setText("N/A");
    }
    m_gpuStatusLabel->setText(QString("GPU: %1%").arg(gpuInfo.usage, 0, 'f', 0));
    
    // Update Memory UI
    const auto& memInfo = data.memory;
    m_memUsageLabel->setText(QString("%1 GB / %2 GB")
        .arg(memInfo.usedGB, 0, 'f', 1)
        .arg(memInfo.totalGB, 0, 'f', 1));
    m_memAvailableLabel->setText(QString("%1 GB").arg(memInfo.availableGB, 0, 'f', 1));
    m_memCommittedLabel->setText(QString("%1 / %2 GB")
        .arg(memInfo.committedGB, 0, 'f', 1)
        .arg(memInfo.commitLimitGB, 0, 'f', 1));
    m_memCachedLabel->setText(QString("%1 GB").arg(memInfo.cachedGB, 0, 'f', 1));
    m_memPagedLabel->setText(QString("%1 MB").arg(memInfo.pagedPoolMB, 0, 'f', 0));
    m_memProgressBar->setValue(static_cast<int>(memInfo.usagePercent));
    m_memProgressBar->setFormat(QString("%1% (%2 GB / %3 GB)")
        .arg(memInfo.usagePercent, 0, 'f', 0)
        .arg(memInfo.usedGB, 0, 'f', 1)
        .arg(memInfo.totalGB, 0, 'f', 1));
    m_memGraph->addValue(memInfo.usagePercent);
    m_memStatusLabel->setText(QString("Memory: %1%").arg(memInfo.usagePercent, 0, 'f', 0));
    
    // Update Disk UI
    const auto& diskActivity = data.diskActivity;
    m_diskReadLabel->setText(formatBytes(diskActivity.readBytesPerSec) + "/s");
    m_diskWriteLabel->setText(formatBytes(diskActivity.writeBytesPerSec) + "/s");
    m_diskReadGraph->addValue(diskActivity.readBytesPerSec / 1048576.0);  // MB/s
    m_diskWriteGraph->addValue(diskActivity.writeBytesPerSec / 1048576.0);
    
    // Update Network UI
    const auto& netActivity = data.networkActivity;
    m_netSendLabel->setText(formatBytes(netActivity.sentBytesPerSec) + "/s");
    m_netRecvLabel->setText(formatBytes(netActivity.receivedBytesPerSec) + "/s");
    m_netSendGraph->addValue(netActivity.sentBytesPerSec / 1048576.0);
    m_netRecvGraph->addValue(netActivity.receivedBytesPerSec / 1048576.0);
    
    // Update Battery UI
    const auto& batteryInfo = data.battery;
    if (batteryInfo.hasBattery) {
        m_batteryPercentLabel->setText(QString("%1%").arg(batteryInfo.percentage));
        m_batteryStatusLabel2->setText(batteryInfo.status);
        m_batteryTimeLabel->setText(batteryInfo.timeRemaining);
        m_batteryHealthLabel->setText(QString("%1%").arg(batteryInfo.healthPercent, 0, 'f', 1));
        m_batteryCyclesLabel->setText(QString::number(batteryInfo.cycleCount));
        m_batteryCapacityLabel->setText(QString("%1 mWh (%2 mWh)")
            .arg(batteryInfo.fullChargeCapacity)
            .arg(batteryInfo.designCapacity));
        m_batteryVoltageLabel->setText(QString("%1 mV").arg(batteryInfo.voltage));
        if (batteryInfo.temperature > -900) {
            m_batteryTempLabel->setText(QString("%1 °C").arg(batteryInfo.temperature, 0, 'f', 1));
        } else {
            m_batteryTempLabel->setText("N/A");
        }
        m_batteryProgressBar->setValue(batteryInfo.percentage);
        m_batteryGraph->addValue(batteryInfo.percentage);
        
        QString color = batteryInfo.percentage > 50 ? "#00aa00" : 
                       (batteryInfo.percentage > 20 ? "#ffaa00" : "#ff0000");
        m_batteryPercentLabel->setStyleSheet(
            QString("font-size: 48px; font-weight: bold; color: %1;").arg(color));
        m_batteryStatusLabel->setText(QString("Battery: %1%").arg(batteryInfo.percentage));
    } else {
        m_batteryPercentLabel->setText("N/A");
        m_batteryStatusLabel2->setText("No battery detected");
        m_batteryStatusLabel->setText("Battery: N/A");
    }
    
    // Update Tray
    m_trayManager->updateTooltip(cpuInfo.usage, memInfo.usagePercent);
    
    // Update Floating Widget
    if (m_floatingWidget && m_floatingWidget->isVisible()) {
        m_floatingWidget->updateMetrics(
            cpuInfo.usage,
            memInfo.usagePercent,
            gpuInfo.usage,
            batteryInfo.hasBattery ? batteryInfo.percentage : -1,
            tempInfo.hasTemperature ? tempInfo.cpuTemperature : -1,
            gpuInfo.temperature > -900 ? gpuInfo.temperature : -1
        );
    }
    
    // Check alerts
    checkAlerts(
        cpuInfo.usage,
        memInfo.usagePercent,
        batteryInfo.hasBattery ? batteryInfo.percentage : -1,
        tempInfo.hasTemperature ? tempInfo.cpuTemperature : (gpuInfo.temperature > -900 ? gpuInfo.temperature : -1)
    );
}

void MainWindow::closeEvent(QCloseEvent *event)
{
    // If force quit (from tray Exit), skip the dialog
    if (m_forceQuit) {
        saveSettings();
        event->accept();
        QApplication::quit();
        return;
    }
    
    if (m_trayManager && m_trayManager->isVisible()) {
        QMessageBox msgBox(this);
        msgBox.setWindowTitle(tr("Close Application"));
        msgBox.setText(tr("What do you want to do?"));
        msgBox.setIcon(QMessageBox::Question);
        
        QPushButton* minimizeBtn = msgBox.addButton(tr("Minimize to Tray"), QMessageBox::ActionRole);
        QPushButton* quitBtn = msgBox.addButton(tr("Quit"), QMessageBox::DestructiveRole);
        QPushButton* cancelBtn = msgBox.addButton(QMessageBox::Cancel);
        
        msgBox.setDefaultButton(minimizeBtn);
        msgBox.exec();
        
        if (msgBox.clickedButton() == minimizeBtn) {
            hide();
            event->ignore();
        } else if (msgBox.clickedButton() == quitBtn) {
            saveSettings();
            event->accept();
            QApplication::quit();
        } else {
            // Cancel
            event->ignore();
        }
    } else {
        saveSettings();
        event->accept();
    }
}

void MainWindow::changeEvent(QEvent *event)
{
    if (event->type() == QEvent::WindowStateChange) {
        if (isMinimized() && m_minimizeToTray) {
            hide();
        }
    }
    QMainWindow::changeEvent(event);
}

void MainWindow::onTrayActivated(QSystemTrayIcon::ActivationReason reason)
{
    if (reason == QSystemTrayIcon::DoubleClick) {
        show();
        setWindowState(windowState() & ~Qt::WindowMinimized);
        activateWindow();
    }
}

void MainWindow::toggleAlwaysOnTop()
{
    m_alwaysOnTop = !m_alwaysOnTop;
    setWindowFlag(Qt::WindowStaysOnTopHint, m_alwaysOnTop);
    show();
}

void MainWindow::toggleFloatingWidget()
{
    if (!m_floatingWidget) {
        m_floatingWidget = std::make_unique<FloatingWidget>();
        
        // Connect signals
        connect(m_floatingWidget.get(), &FloatingWidget::closeRequested, 
                this, &MainWindow::onFloatingWidgetClosed);
        connect(m_floatingWidget.get(), &FloatingWidget::mainWindowRequested, 
                this, [this]() {
            show();
            setWindowState(windowState() & ~Qt::WindowMinimized);
            activateWindow();
        });
    }
    
    if (m_floatingWidget->isVisible()) {
        m_floatingWidget->hide();
        m_floatingWidgetAction->setChecked(false);
    } else {
        m_floatingWidget->show();
        m_floatingWidgetAction->setChecked(true);
        
        // Trigger immediate update using cached data
        const auto& cpuInfo = m_monitorData.cpu;
        const auto& memInfo = m_monitorData.memory;
        const auto& gpuInfo = m_monitorData.primaryGpu;
        const auto& batteryInfo = m_monitorData.battery;
        const auto& tempInfo = m_monitorData.temperature;
        
        m_floatingWidget->updateMetrics(
            cpuInfo.usage,
            memInfo.usagePercent,
            gpuInfo.usage,
            batteryInfo.hasBattery ? batteryInfo.percentage : -1,
            tempInfo.hasTemperature ? tempInfo.cpuTemperature : -1,
            gpuInfo.temperature > -900 ? gpuInfo.temperature : -1
        );
    }
}

void MainWindow::onFloatingWidgetClosed()
{
    if (m_floatingWidget) {
        m_floatingWidget->hide();
    }
    if (m_floatingWidgetAction) {
        m_floatingWidgetAction->setChecked(false);
    }
}

void MainWindow::showSettings()
{
    SettingsDialog dialog(this);
    
    connect(&dialog, &SettingsDialog::settingsChanged, this, [this](const AppSettings& settings) {
        // Apply update interval
        if (m_updateInterval != settings.updateInterval) {
            m_updateInterval = settings.updateInterval;
            if (m_monitorWorker) {
                m_monitorWorker->setInterval(m_updateInterval);
            }
        }
        
        // Apply minimize to tray
        m_minimizeToTray = settings.minimizeToTray;
        
        // Apply tab visibility
        applyTabVisibility(settings);
        
        // Apply floating widget settings
        if (m_floatingWidget) {
            m_floatingWidget->setWidgetOpacity(settings.floatingOpacity);
            m_floatingWidget->setShowCpu(settings.floatingShowCpu);
            m_floatingWidget->setShowMemory(settings.floatingShowMemory);
            m_floatingWidget->setShowGpu(settings.floatingShowGpu);
            m_floatingWidget->setShowBattery(settings.floatingShowBattery);
            m_floatingWidget->setShowGraphs(settings.floatingShowGraphs);
        }
        
        // Store alert settings
        m_alertSettings = settings;
        
        statusBar()->showMessage(tr("Settings applied"), 3000);
    });
    
    connect(&dialog, &SettingsDialog::themeChanged, this, [this](const QString& theme) {
        applyTheme(theme);
    });
    
    dialog.exec();
}

void MainWindow::showAbout()
{
    QMessageBox::about(this, tr("About PerfMonitorQt"),
        tr("<h2>PerfMonitorQt</h2>"
           "<p>Version 1.0.0</p>"
           "<p>A modern Windows 11 Performance Monitor</p>"
           "<p>Built with Qt 6 and C++20</p>"
           "<p>Copyright © 2024 Félix-Antoine</p>"));
}

void MainWindow::exportReport()
{
    QString filename = QFileDialog::getSaveFileName(this,
        tr("Export Report"), QString(), tr("Text Files (*.txt);;HTML Files (*.html)"));
    
    if (filename.isEmpty()) return;
    
    QFile file(filename);
    if (!file.open(QIODevice::WriteOnly | QIODevice::Text)) {
        QMessageBox::warning(this, tr("Error"), 
            tr("Cannot write to file: %1").arg(file.errorString()));
        return;
    }
    
    QTextStream out(&file);
    bool isHtml = filename.endsWith(".html", Qt::CaseInsensitive);
    
    // Get current timestamp
    QString timestamp = QDateTime::currentDateTime().toString("dd/MM/yyyy HH:mm:ss");
    
    if (isHtml) {
        // HTML Report
        out << "<!DOCTYPE html>\n";
        out << "<html><head>\n";
        out << "<meta charset=\"UTF-8\">\n";
        out << "<title>PerfMonitorQt - System Report</title>\n";
        out << "<style>\n";
        out << "body { font-family: 'Segoe UI', Arial, sans-serif; background: #1e1e24; color: #fff; margin: 40px; }\n";
        out << "h1 { color: #0078d7; border-bottom: 2px solid #0078d7; padding-bottom: 10px; }\n";
        out << "h2 { color: #00c864; margin-top: 30px; }\n";
        out << ".section { background: #2d2d35; padding: 20px; border-radius: 8px; margin: 15px 0; }\n";
        out << ".metric { display: inline-block; margin: 10px 20px; }\n";
        out << ".metric-label { color: #888; font-size: 12px; }\n";
        out << ".metric-value { font-size: 24px; font-weight: bold; }\n";
        out << ".good { color: #00c864; }\n";
        out << ".warning { color: #ffaa00; }\n";
        out << ".critical { color: #ff4444; }\n";
        out << "table { width: 100%; border-collapse: collapse; margin: 15px 0; }\n";
        out << "th, td { text-align: left; padding: 12px; border-bottom: 1px solid #3d3d45; }\n";
        out << "th { background: #3d3d45; color: #0078d7; }\n";
        out << "</style>\n";
        out << "</head><body>\n";
        out << "<h1>🖥️ PerfMonitorQt - System Report</h1>\n";
        out << QString("<p>Generated: %1</p>\n").arg(timestamp);
        out << QString("<p>System Uptime: %1</p>\n").arg(m_monitorData.cpu.uptime);
        
        // CPU Section
        out << "<h2>⚡ CPU</h2>\n";
        out << "<div class=\"section\">\n";
        out << QString("<p><strong>Processor:</strong> %1</p>\n").arg(m_monitorData.cpu.name);
        out << QString("<p><strong>Cores:</strong> %1 Physical / %2 Logical</p>\n")
               .arg(m_monitorData.cpu.cores).arg(m_monitorData.cpu.logicalProcessors);
        QString cpuClass = m_monitorData.cpu.usage > 80 ? "critical" : (m_monitorData.cpu.usage > 50 ? "warning" : "good");
        out << QString("<div class=\"metric\"><div class=\"metric-label\">Usage</div><div class=\"metric-value %1\">%2%</div></div>\n")
               .arg(cpuClass).arg(m_monitorData.cpu.usage, 0, 'f', 1);
        out << QString("<div class=\"metric\"><div class=\"metric-label\">Speed</div><div class=\"metric-value\">%1 GHz</div></div>\n")
               .arg(m_monitorData.cpu.currentSpeed, 0, 'f', 2);
        out << QString("<div class=\"metric\"><div class=\"metric-label\">Processes</div><div class=\"metric-value\">%1</div></div>\n")
               .arg(m_monitorData.cpu.processCount);
        out << QString("<div class=\"metric\"><div class=\"metric-label\">Threads</div><div class=\"metric-value\">%1</div></div>\n")
               .arg(m_monitorData.cpu.threadCount);
        if (m_monitorData.temperature.hasTemperature) {
            QString tempClass = m_monitorData.temperature.cpuTemperature > 80 ? "critical" : 
                               (m_monitorData.temperature.cpuTemperature > 60 ? "warning" : "good");
            out << QString("<div class=\"metric\"><div class=\"metric-label\">Temperature</div><div class=\"metric-value %1\">%2°C</div></div>\n")
                   .arg(tempClass).arg(m_monitorData.temperature.cpuTemperature, 0, 'f', 1);
        }
        out << "</div>\n";
        
        // Memory Section
        out << "<h2>🧠 Memory</h2>\n";
        out << "<div class=\"section\">\n";
        QString memClass = m_monitorData.memory.usagePercent > 85 ? "critical" : 
                          (m_monitorData.memory.usagePercent > 70 ? "warning" : "good");
        out << QString("<div class=\"metric\"><div class=\"metric-label\">Usage</div><div class=\"metric-value %1\">%2%</div></div>\n")
               .arg(memClass).arg(m_monitorData.memory.usagePercent, 0, 'f', 1);
        out << QString("<div class=\"metric\"><div class=\"metric-label\">Used</div><div class=\"metric-value\">%1 GB</div></div>\n")
               .arg(m_monitorData.memory.usedGB, 0, 'f', 1);
        out << QString("<div class=\"metric\"><div class=\"metric-label\">Available</div><div class=\"metric-value\">%1 GB</div></div>\n")
               .arg(m_monitorData.memory.availableGB, 0, 'f', 1);
        out << QString("<div class=\"metric\"><div class=\"metric-label\">Total</div><div class=\"metric-value\">%1 GB</div></div>\n")
               .arg(m_monitorData.memory.totalGB, 0, 'f', 1);
        out << "</div>\n";
        
        // GPU Section
        out << "<h2>🎮 GPU</h2>\n";
        out << "<div class=\"section\">\n";
        out << QString("<p><strong>Graphics Card:</strong> %1</p>\n").arg(m_monitorData.primaryGpu.name);
        out << QString("<p><strong>Vendor:</strong> %1</p>\n").arg(m_monitorData.primaryGpu.vendor);
        out << QString("<div class=\"metric\"><div class=\"metric-label\">GPU Usage</div><div class=\"metric-value\">%1%</div></div>\n")
               .arg(m_monitorData.primaryGpu.usage, 0, 'f', 1);
        out << QString("<div class=\"metric\"><div class=\"metric-label\">VRAM Used</div><div class=\"metric-value\">%1</div></div>\n")
               .arg(GpuMonitor::formatMemory(m_monitorData.primaryGpu.dedicatedMemoryUsed));
        if (m_monitorData.primaryGpu.temperature > -900) {
            out << QString("<div class=\"metric\"><div class=\"metric-label\">Temperature</div><div class=\"metric-value\">%1°C</div></div>\n")
                   .arg(m_monitorData.primaryGpu.temperature, 0, 'f', 0);
        }
        out << "</div>\n";
        
        // Disk Section
        out << "<h2>💾 Disks</h2>\n";
        out << "<div class=\"section\">\n";
        out << "<table><tr><th>Drive</th><th>Label</th><th>Type</th><th>Used</th><th>Free</th><th>Total</th><th>Usage</th></tr>\n";
        for (const auto& disk : m_monitorData.disks) {
            QString usageClass = disk.usagePercent > 90 ? "critical" : (disk.usagePercent > 75 ? "warning" : "good");
            out << QString("<tr><td>%1</td><td>%2</td><td>%3</td><td>%4</td><td>%5</td><td>%6</td><td class=\"%7\">%8%</td></tr>\n")
                   .arg(disk.driveLetter)
                   .arg(disk.label.isEmpty() ? "-" : disk.label)
                   .arg(disk.fileSystem)
                   .arg(formatBytes(disk.usedBytes))
                   .arg(formatBytes(disk.freeBytes))
                   .arg(formatBytes(disk.totalBytes))
                   .arg(usageClass)
                   .arg(disk.usagePercent, 0, 'f', 1);
        }
        out << "</table>\n";
        out << "</div>\n";
        
        // Network Section
        out << "<h2>🌐 Network</h2>\n";
        out << "<div class=\"section\">\n";
        out << "<table><tr><th>Adapter</th><th>Status</th><th>IPv4</th><th>Speed</th></tr>\n";
        for (const auto& adapter : m_monitorData.networkAdapters) {
            QString statusClass = adapter.isConnected ? "good" : "critical";
            out << QString("<tr><td>%1</td><td class=\"%2\">%3</td><td>%4</td><td>%5</td></tr>\n")
                   .arg(adapter.description)
                   .arg(statusClass)
                   .arg(adapter.isConnected ? tr("Connected") : tr("Disconnected"))
                   .arg(adapter.ipv4Address.isEmpty() ? "-" : adapter.ipv4Address)
                   .arg(adapter.speed > 0 ? QString("%1 Mbps").arg(adapter.speed / 1000000) : "-");
        }
        out << "</table>\n";
        out << "</div>\n";
        
        // Battery Section (if available)
        if (m_monitorData.battery.hasBattery) {
            out << "<h2>🔋 Battery</h2>\n";
            out << "<div class=\"section\">\n";
            QString batClass = m_monitorData.battery.percentage < 20 ? "critical" : 
                              (m_monitorData.battery.percentage < 50 ? "warning" : "good");
            out << QString("<div class=\"metric\"><div class=\"metric-label\">Charge</div><div class=\"metric-value %1\">%2%</div></div>\n")
                   .arg(batClass).arg(m_monitorData.battery.percentage);
            out << QString("<div class=\"metric\"><div class=\"metric-label\">Status</div><div class=\"metric-value\">%1</div></div>\n")
                   .arg(m_monitorData.battery.status);
            out << QString("<div class=\"metric\"><div class=\"metric-label\">Health</div><div class=\"metric-value\">%1%</div></div>\n")
                   .arg(m_monitorData.battery.healthPercent, 0, 'f', 1);
            out << QString("<div class=\"metric\"><div class=\"metric-label\">Cycles</div><div class=\"metric-value\">%1</div></div>\n")
                   .arg(m_monitorData.battery.cycleCount);
            out << "</div>\n";
        }
        
        out << "<hr style=\"border-color: #3d3d45; margin-top: 40px;\">\n";
        out << "<p style=\"color: #666; font-size: 12px;\">Generated by PerfMonitorQt v1.0.0</p>\n";
        out << "</body></html>\n";
    } else {
        // Plain Text Report
        out << "═══════════════════════════════════════════════════════════════\n";
        out << "           PERFMONITORQT - SYSTEM REPORT\n";
        out << "═══════════════════════════════════════════════════════════════\n\n";
        out << QString("Generated: %1\n").arg(timestamp);
        out << QString("System Uptime: %1\n\n").arg(m_monitorData.cpu.uptime);
        
        // CPU
        out << "───────────────────────────────────────────────────────────────\n";
        out << "  CPU\n";
        out << "───────────────────────────────────────────────────────────────\n";
        out << QString("  Processor:     %1\n").arg(m_monitorData.cpu.name);
        out << QString("  Cores:         %1 Physical / %2 Logical\n")
               .arg(m_monitorData.cpu.cores).arg(m_monitorData.cpu.logicalProcessors);
        out << QString("  Usage:         %1%\n").arg(m_monitorData.cpu.usage, 0, 'f', 1);
        out << QString("  Speed:         %1 GHz\n").arg(m_monitorData.cpu.currentSpeed, 0, 'f', 2);
        out << QString("  Processes:     %1\n").arg(m_monitorData.cpu.processCount);
        out << QString("  Threads:       %1\n").arg(m_monitorData.cpu.threadCount);
        if (m_monitorData.temperature.hasTemperature) {
            out << QString("  Temperature:   %1°C\n").arg(m_monitorData.temperature.cpuTemperature, 0, 'f', 1);
        }
        out << "\n";
        
        // Memory
        out << "───────────────────────────────────────────────────────────────\n";
        out << "  MEMORY\n";
        out << "───────────────────────────────────────────────────────────────\n";
        out << QString("  Usage:         %1%\n").arg(m_monitorData.memory.usagePercent, 0, 'f', 1);
        out << QString("  Used:          %1 GB\n").arg(m_monitorData.memory.usedGB, 0, 'f', 1);
        out << QString("  Available:     %1 GB\n").arg(m_monitorData.memory.availableGB, 0, 'f', 1);
        out << QString("  Total:         %1 GB\n").arg(m_monitorData.memory.totalGB, 0, 'f', 1);
        out << "\n";
        
        // GPU
        out << "───────────────────────────────────────────────────────────────\n";
        out << "  GPU\n";
        out << "───────────────────────────────────────────────────────────────\n";
        out << QString("  Graphics Card: %1\n").arg(m_monitorData.primaryGpu.name);
        out << QString("  Vendor:        %1\n").arg(m_monitorData.primaryGpu.vendor);
        out << QString("  Usage:         %1%\n").arg(m_monitorData.primaryGpu.usage, 0, 'f', 1);
        out << QString("  VRAM Used:     %1\n").arg(GpuMonitor::formatMemory(m_monitorData.primaryGpu.dedicatedMemoryUsed));
        if (m_monitorData.primaryGpu.temperature > -900) {
            out << QString("  Temperature:   %1°C\n").arg(m_monitorData.primaryGpu.temperature, 0, 'f', 0);
        }
        out << "\n";
        
        // Disks
        out << "───────────────────────────────────────────────────────────────\n";
        out << "  DISKS\n";
        out << "───────────────────────────────────────────────────────────────\n";
        for (const auto& disk : m_monitorData.disks) {
            out << QString("  %1 %2\n").arg(disk.driveLetter).arg(disk.label.isEmpty() ? "" : QString("(%1)").arg(disk.label));
            out << QString("      Type:      %1\n").arg(disk.fileSystem);
            out << QString("      Used:      %1 / %2 (%3%)\n")
                   .arg(formatBytes(disk.usedBytes))
                   .arg(formatBytes(disk.totalBytes))
                   .arg(disk.usagePercent, 0, 'f', 1);
            out << QString("      Free:      %1\n").arg(formatBytes(disk.freeBytes));
        }
        out << "\n";
        
        // Network
        out << "───────────────────────────────────────────────────────────────\n";
        out << "  NETWORK\n";
        out << "───────────────────────────────────────────────────────────────\n";
        for (const auto& adapter : m_monitorData.networkAdapters) {
            out << QString("  %1\n").arg(adapter.description);
            out << QString("      Status:    %1\n").arg(adapter.isConnected ? tr("Connected") : tr("Disconnected"));
            if (!adapter.ipv4Address.isEmpty()) {
                out << QString("      IPv4:      %1\n").arg(adapter.ipv4Address);
            }
            if (adapter.speed > 0) {
                out << QString("      Speed:     %1 Mbps\n").arg(adapter.speed / 1000000);
            }
        }
        out << "\n";
        
        // Battery
        if (m_monitorData.battery.hasBattery) {
            out << "───────────────────────────────────────────────────────────────\n";
            out << "  BATTERY\n";
            out << "───────────────────────────────────────────────────────────────\n";
            out << QString("  Charge:        %1%\n").arg(m_monitorData.battery.percentage);
            out << QString("  Status:        %1\n").arg(m_monitorData.battery.status);
            out << QString("  Health:        %1%\n").arg(m_monitorData.battery.healthPercent, 0, 'f', 1);
            out << QString("  Cycles:        %1\n").arg(m_monitorData.battery.cycleCount);
            out << "\n";
        }
        
        out << "═══════════════════════════════════════════════════════════════\n";
        out << "  Generated by PerfMonitorQt v1.0.0\n";
        out << "═══════════════════════════════════════════════════════════════\n";
    }
    
    file.close();
    
    QMessageBox::information(this, tr("Export Complete"), 
        tr("System report exported successfully to:\n%1").arg(filename));
    
    // Offer to open the file
    auto reply = QMessageBox::question(this, tr("Open Report"),
        tr("Do you want to open the exported report?"),
        QMessageBox::Yes | QMessageBox::No, QMessageBox::No);
    
    if (reply == QMessageBox::Yes) {
        QDesktopServices::openUrl(QUrl::fromLocalFile(filename));
    }
}

void MainWindow::loadSettings()
{
    QSettings settings("Félix-Antoine", "PerfMonitorQt");
    
    m_updateInterval = settings.value("updateInterval", 1000).toInt();
    m_minimizeToTray = settings.value("minimizeToTray", true).toBool();
    m_alwaysOnTop = settings.value("alwaysOnTop", false).toBool();
    
    restoreGeometry(settings.value("geometry").toByteArray());
    restoreState(settings.value("windowState").toByteArray());
    
    if (m_alwaysOnTop) {
        setWindowFlag(Qt::WindowStaysOnTopHint, true);
    }
    
    // Load app settings (alerts, appearance, etc.)
    m_alertSettings = SettingsDialog::loadSettings();
    
    // Apply tab visibility from settings
    applyTabVisibility(m_alertSettings);
    
    // Apply theme from settings
    if (m_alertSettings.theme != "system") {
        QString styleSheet;
        if (m_alertSettings.theme == "dark") {
            qApp->setStyle(QStyleFactory::create("Fusion"));
            QPalette darkPalette;
            darkPalette.setColor(QPalette::Window, QColor(53, 53, 53));
            darkPalette.setColor(QPalette::WindowText, Qt::white);
            darkPalette.setColor(QPalette::Base, QColor(25, 25, 25));
            darkPalette.setColor(QPalette::AlternateBase, QColor(53, 53, 53));
            darkPalette.setColor(QPalette::ToolTipBase, Qt::white);
            darkPalette.setColor(QPalette::ToolTipText, Qt::white);
            darkPalette.setColor(QPalette::Text, Qt::white);
            darkPalette.setColor(QPalette::Button, QColor(53, 53, 53));
            darkPalette.setColor(QPalette::ButtonText, Qt::white);
            darkPalette.setColor(QPalette::BrightText, Qt::red);
            darkPalette.setColor(QPalette::Link, QColor(42, 130, 218));
            darkPalette.setColor(QPalette::Highlight, QColor(42, 130, 218));
            darkPalette.setColor(QPalette::HighlightedText, Qt::black);
            qApp->setPalette(darkPalette);
            styleSheet = R"(
                QToolTip { color: #ffffff; background-color: #2a82da; border: 1px solid white; }
                QGroupBox { border: 1px solid #555; border-radius: 5px; margin-top: 1ex; padding-top: 10px; }
                QGroupBox::title { subcontrol-origin: margin; left: 10px; padding: 0 5px; }
            )";
        } else if (m_alertSettings.theme == "light") {
            qApp->setStyle(QStyleFactory::create("Fusion"));
            qApp->setPalette(qApp->style()->standardPalette());
        }
        qApp->setStyleSheet(styleSheet);
    }
}

void MainWindow::saveSettings()
{
    QSettings settings("Félix-Antoine", "PerfMonitorQt");
    
    settings.setValue("updateInterval", m_updateInterval);
    settings.setValue("minimizeToTray", m_minimizeToTray);
    settings.setValue("alwaysOnTop", m_alwaysOnTop);
    settings.setValue("geometry", saveGeometry());
    settings.setValue("windowState", saveState());
}

// Helper function (static)
QString MainWindow::formatBytes(qint64 bytes)
{
    const char* units[] = {"B", "KB", "MB", "GB", "TB"};
    int unitIndex = 0;
    double size = bytes;
    
    while (size >= 1024.0 && unitIndex < 4) {
        size /= 1024.0;
        unitIndex++;
    }
    
    return QString("%1 %2").arg(size, 0, 'f', unitIndex > 0 ? 1 : 0).arg(units[unitIndex]);
}

void MainWindow::purgeMemory()
{
    if (!m_isAdmin) {
        QMessageBox::warning(this, tr("Administrator Required"),
            tr("Memory purge requires administrator privileges.\n\n"
               "Please restart the application as Administrator to use this feature."));
        return;
    }
    
    // Confirm action
    auto reply = QMessageBox::question(this, tr("Purge Memory"),
        tr("This will free up system memory by:\n\n"
           "1. Emptying working sets of all processes\n"
           "2. Purging the standby memory list\n\n"
           "This may temporarily slow down some applications.\n\n"
           "Do you want to continue?"),
        QMessageBox::Yes | QMessageBox::No, QMessageBox::No);
    
    if (reply != QMessageBox::Yes) {
        return;
    }
    
    // Get memory before purge (use cached data)
    double memBefore = m_monitorData.memory.usedGB;
    
    // Perform purge
    QApplication::setOverrideCursor(Qt::WaitCursor);
    bool success = MemoryMonitor::purgeAllMemory();
    QApplication::restoreOverrideCursor();
    
    // Get memory after purge - use a temporary monitor for immediate reading
    MemoryMonitor tempMonitor;
    tempMonitor.update();
    double memAfter = tempMonitor.info().usedGB;
    double freed = memBefore - memAfter;
    
    if (success && freed > 0) {
        QMessageBox::information(this, tr("Memory Purged"),
            tr("Memory purge completed successfully!\n\n"
               "Memory freed: %1 GB\n"
               "Memory usage: %2 GB -> %3 GB")
            .arg(freed, 0, 'f', 2)
            .arg(memBefore, 0, 'f', 2)
            .arg(memAfter, 0, 'f', 2));
    } else if (success) {
        QMessageBox::information(this, tr("Memory Purged"),
            tr("Memory purge completed.\n\n"
               "No significant memory was freed. The system may already be optimized."));
    } else {
        QMessageBox::warning(this, tr("Purge Failed"),
            tr("Memory purge failed or only partially completed.\n\n"
               "Some system processes may have denied access."));
    }
    
    // Request immediate update from worker
    if (m_monitorWorker) {
        m_monitorWorker->requestUpdate();
    }
}

void MainWindow::checkAdminPrivileges()
{
    if (!m_isAdmin) {
        QMessageBox msgBox(this);
        msgBox.setWindowTitle(tr("Administrator Privileges Recommended"));
        msgBox.setIcon(QMessageBox::Warning);
        msgBox.setText(tr("PerfMonitorQt is not running as Administrator."));
        msgBox.setInformativeText(
            tr("Some features will be limited:\n\n"
               "- Memory purge will not work\n"
               "- Service control will not work\n"
               "- Some process information may be unavailable\n"
               "- Battery details may be incomplete\n\n"
               "For full functionality, please restart as Administrator."));
        
        QPushButton* restartBtn = msgBox.addButton(tr("Restart as Admin"), QMessageBox::ActionRole);
        QPushButton* continueBtn = msgBox.addButton(tr("Continue Anyway"), QMessageBox::RejectRole);
        msgBox.setDefaultButton(continueBtn);
        
        msgBox.exec();
        
        if (msgBox.clickedButton() == restartBtn) {
#ifdef _WIN32
            wchar_t path[MAX_PATH];
            GetModuleFileNameW(nullptr, path, MAX_PATH);
            
            SHELLEXECUTEINFOW sei = {0};
            sei.cbSize = sizeof(sei);
            sei.lpVerb = L"runas";
            sei.lpFile = path;
            sei.hwnd = nullptr;
            sei.nShow = SW_NORMAL;
            sei.fMask = SEE_MASK_NOASYNC;
            
            if (ShellExecuteExW(&sei)) {
                ::ExitProcess(0);
            } else {
                QMessageBox::warning(this, tr("Error"),
                    tr("Failed to restart as Administrator.\n"
                       "Please manually run the application as Administrator."));
            }
#endif
        }
    }
}

void MainWindow::showEnergyModeDialog()
{
    EnergyModeDialog dialog(m_energyModeManager.get(), this);
    
    connect(m_energyModeManager.get(), &EnergyModeManager::activationChanged,
            this, [this](bool active) {
        m_energyModeAction->setChecked(active);
        if (active) {
            m_energyModeStatusLabel->setText("⚡ Mode Énergie");
            m_energyModeStatusLabel->setStyleSheet("color: #00cc00; font-weight: bold;");
        } else {
            m_energyModeStatusLabel->setText("");
        }
    });
    
    dialog.exec();
}

void MainWindow::toggleEnergyMode()
{
    if (!EnergyModeManager::isRunningAsAdmin()) {
        QMessageBox::warning(this, tr("Droits insuffisants"),
            tr("Le Mode Énergie nécessite les droits administrateur.\n\n"
               "Relancez PerfMonitorQt en tant qu'administrateur."));
        m_energyModeAction->setChecked(m_energyModeManager->isActive());
        return;
    }
    
    bool success = m_energyModeManager->toggle();
    
    if (success) {
        bool active = m_energyModeManager->isActive();
        m_energyModeAction->setChecked(active);
        
        if (active) {
            m_energyModeStatusLabel->setText("⚡ Mode Énergie");
            m_energyModeStatusLabel->setStyleSheet("color: #00cc00; font-weight: bold;");
            statusBar()->showMessage(tr("Mode Énergie activé"), 3000);
        } else {
            m_energyModeStatusLabel->setText("");
            statusBar()->showMessage(tr("Mode Énergie désactivé"), 3000);
        }
    } else {
        m_energyModeAction->setChecked(m_energyModeManager->isActive());
        QMessageBox::warning(this, tr("Erreur"),
            tr("Impossible de changer l'état du Mode Énergie.\n\n%1")
            .arg(m_energyModeManager->statusMessage()));
    }
}

void MainWindow::applyTabVisibility(const AppSettings& settings)
{
    int currentIndex = m_tabWidget->currentIndex();
    QString currentTabName;
    if (currentIndex >= 0) {
        currentTabName = m_tabWidget->tabText(currentIndex);
    }
    
    struct TabInfo {
        QWidget* widget;
        QString name;
        bool visible;
    };
    
    std::vector<TabInfo> tabs = {
        {m_cpuTab, tr("CPU"), settings.showCpuTab},
        {m_gpuTab, tr("GPU"), settings.showGpuTab},
        {m_memoryTab, tr("Memory"), settings.showMemoryTab},
        {m_diskTab, tr("Disk"), settings.showDiskTab},
        {m_networkTab, tr("Network"), settings.showNetworkTab},
        {m_batteryTab, tr("Battery"), settings.showBatteryTab},
        {m_processTab, tr("Processes"), settings.showProcessTab},
        {m_toolsTab, tr("🧰 Tools"), true},
    };
    
    while (m_tabWidget->count() > 0) {
        m_tabWidget->removeTab(0);
    }
    
    int newCurrentIndex = 0;
    for (const auto& tab : tabs) {
        if (tab.visible && tab.widget) {
            int index = m_tabWidget->addTab(tab.widget, tab.name);
            if (tab.name == currentTabName) {
                newCurrentIndex = index;
            }
        }
    }
    
    if (m_tabWidget->count() > 0) {
        m_tabWidget->setCurrentIndex(newCurrentIndex);
    }
}

void MainWindow::applyTheme(const QString& theme)
{
    QString styleSheet;
    
    if (theme == "dark") {
        qApp->setStyle(QStyleFactory::create("Fusion"));
        QPalette darkPalette;
        darkPalette.setColor(QPalette::Window, QColor(53, 53, 53));
        darkPalette.setColor(QPalette::WindowText, Qt::white);
        darkPalette.setColor(QPalette::Base, QColor(25, 25, 25));
        darkPalette.setColor(QPalette::AlternateBase, QColor(53, 53, 53));
        darkPalette.setColor(QPalette::ToolTipBase, Qt::white);
        darkPalette.setColor(QPalette::ToolTipText, Qt::white);
        darkPalette.setColor(QPalette::Text, Qt::white);
        darkPalette.setColor(QPalette::Button, QColor(53, 53, 53));
        darkPalette.setColor(QPalette::ButtonText, Qt::white);
        darkPalette.setColor(QPalette::BrightText, Qt::red);
        darkPalette.setColor(QPalette::Link, QColor(42, 130, 218));
        darkPalette.setColor(QPalette::Highlight, QColor(42, 130, 218));
        darkPalette.setColor(QPalette::HighlightedText, Qt::black);
        qApp->setPalette(darkPalette);
        
        styleSheet = R"(
            QToolTip { color: #ffffff; background-color: #2a82da; border: 1px solid white; }
            QGroupBox { border: 1px solid #555; border-radius: 5px; margin-top: 1ex; padding-top: 10px; }
            QGroupBox::title { subcontrol-origin: margin; left: 10px; padding: 0 5px; }
        )";
    } else if (theme == "light") {
        qApp->setStyle(QStyleFactory::create("Fusion"));
        qApp->setPalette(qApp->style()->standardPalette());
        styleSheet = "";
    } else {
        qApp->setStyle(QStyleFactory::create("windowsvista"));
        qApp->setPalette(qApp->style()->standardPalette());
        styleSheet = "";
    }
    
    qApp->setStyleSheet(styleSheet);
    
    QMessageBox::information(this, tr("Theme Changed"),
        tr("Theme has been changed. Some changes may require restarting the application."));
}

void MainWindow::checkAlerts(double cpu, double memory, int battery, double gpuTemp)
{
    if (!m_alertSettings.alertsEnabled) {
        return;
    }
    
    qint64 currentTime = QDateTime::currentMSecsSinceEpoch();
    qint64 cooldownMs = m_alertSettings.alertCooldown * 1000;
    
    // CPU Alert
    if (cpu >= m_alertSettings.cpuAlertThreshold) {
        if (currentTime - m_lastCpuAlertTime > cooldownMs) {
            m_lastCpuAlertTime = currentTime;
            m_trayManager->showNotification(
                tr("High CPU Usage"),
                tr("CPU usage is at %1%").arg(cpu, 0, 'f', 1),
                QSystemTrayIcon::Warning
            );
        }
    }
    
    // Memory Alert
    if (memory >= m_alertSettings.memoryAlertThreshold) {
        if (currentTime - m_lastMemoryAlertTime > cooldownMs) {
            m_lastMemoryAlertTime = currentTime;
            m_trayManager->showNotification(
                tr("High Memory Usage"),
                tr("Memory usage is at %1%").arg(memory, 0, 'f', 1),
                QSystemTrayIcon::Warning
            );
        }
    }
    
    // Battery Alert (only if battery exists and discharging)
    if (battery > 0 && battery <= m_alertSettings.batteryAlertThreshold) {
        if (currentTime - m_lastBatteryAlertTime > cooldownMs) {
            const auto& batteryInfo = m_monitorData.battery;
            if (!batteryInfo.isCharging) {
                m_lastBatteryAlertTime = currentTime;
                m_trayManager->showNotification(
                    tr("Low Battery"),
                    tr("Battery is at %1%. Consider plugging in.").arg(battery),
                    QSystemTrayIcon::Critical
                );
            }
        }
    }
    
    // Temperature Alert
    if (gpuTemp > 0 && gpuTemp >= m_alertSettings.temperatureAlertThreshold) {
        if (currentTime - m_lastTempAlertTime > cooldownMs) {
            m_lastTempAlertTime = currentTime;
            m_trayManager->showNotification(
                tr("High Temperature"),
                tr("GPU temperature is at %1°C").arg(gpuTemp, 0, 'f', 0),
                QSystemTrayIcon::Warning
            );
        }
    }
}
