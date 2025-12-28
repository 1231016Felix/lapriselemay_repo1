#include "servicehistorydialog.h"
#include "interactivechart.h"
#include "comparisonchart.h"
#include "timerangeselector.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QFormLayout>
#include <QGroupBox>
#include <QSplitter>
#include <QScrollArea>
#include <QMessageBox>
#include <QFileDialog>
#include <QHeaderView>
#include <QTextStream>
#include <QJsonDocument>
#include <QJsonObject>
#include <QJsonArray>
#include <QApplication>
#include <QStyle>
#include <QDebug>

ServiceHistoryDialog::ServiceHistoryDialog(QWidget* parent)
    : QDialog(parent)
{
    setWindowTitle(tr("Service History & Analytics"));
    setMinimumSize(1400, 900);
    resize(1600, 1000);
    
    // Initialize history manager
    m_historyManager = std::make_unique<ServiceHistoryManager>();
    if (!m_historyManager->initialize()) {
        QMessageBox::critical(this, tr("Error"), 
            tr("Failed to initialize service history database"));
        return;
    }
    
    // Set default time range (last 24 hours)
    m_endTime = QDateTime::currentDateTime();
    m_startTime = m_endTime.addDays(-1);
    
    setupUi();
    loadServices();
}

ServiceHistoryDialog::~ServiceHistoryDialog() = default;

void ServiceHistoryDialog::setupUi()
{
    QVBoxLayout* mainLayout = new QVBoxLayout(this);
    mainLayout->setSpacing(10);
    
    createToolbar();
    
    // Main content tabs
    m_tabWidget = new QTabWidget();
    
    createChartsTab();
    createStatisticsTab();
    createCrashHistoryTab();
    createComparisonTab();
    createTopServicesTab();
    
    mainLayout->addWidget(m_tabWidget, 1);
    
    // Status bar
    QHBoxLayout* statusLayout = new QHBoxLayout();
    m_statusLabel = new QLabel(tr("Ready"));
    statusLayout->addWidget(m_statusLabel);
    statusLayout->addStretch();
    
    mainLayout->addLayout(statusLayout);
}

void ServiceHistoryDialog::createToolbar()
{
    QHBoxLayout* toolbarLayout = new QHBoxLayout();
    
    // Service selector
    QLabel* serviceLabel = new QLabel(tr("Service:"));
    m_serviceCombo = new QComboBox();
    m_serviceCombo->setMinimumWidth(300);
    connect(m_serviceCombo, QOverload<int>::of(&QComboBox::currentIndexChanged),
            this, &ServiceHistoryDialog::onServiceChanged);
    
    // Time range selector
    m_timeRangeSelector = new TimeRangeSelector();
    m_timeRangeSelector->setTimeRange(m_startTime, m_endTime);
    connect(m_timeRangeSelector, &TimeRangeSelector::timeRangeChanged,
            this, &ServiceHistoryDialog::onTimeRangeChanged);
    
    // Refresh button
    QPushButton* refreshButton = new QPushButton(tr("Refresh"));
    refreshButton->setIcon(style()->standardIcon(QStyle::SP_BrowserReload));
    connect(refreshButton, &QPushButton::clicked, this, &ServiceHistoryDialog::onRefreshRequested);
    
    // Export button
    m_exportButton = new QPushButton(tr("Export..."));
    m_exportButton->setIcon(style()->standardIcon(QStyle::SP_DialogSaveButton));
    connect(m_exportButton, &QPushButton::clicked, this, &ServiceHistoryDialog::onExportClicked);
    
    toolbarLayout->addWidget(serviceLabel);
    toolbarLayout->addWidget(m_serviceCombo);
    toolbarLayout->addSpacing(20);
    toolbarLayout->addWidget(m_timeRangeSelector);
    toolbarLayout->addStretch();
    toolbarLayout->addWidget(refreshButton);
    toolbarLayout->addWidget(m_exportButton);
    
    static_cast<QVBoxLayout*>(layout())->addLayout(toolbarLayout);
}

void ServiceHistoryDialog::createChartsTab()
{
    QWidget* chartsTab = new QWidget();
    QVBoxLayout* chartsLayout = new QVBoxLayout(chartsTab);
    
    // CPU Chart
    QGroupBox* cpuGroup = new QGroupBox(tr("CPU Usage History"));
    QVBoxLayout* cpuLayout = new QVBoxLayout(cpuGroup);
    
    m_cpuChart = new InteractiveChart();
    m_cpuChart->setTitle(tr("CPU Usage Over Time"));
    m_cpuChart->setAxisTitles(tr("Time"), tr("CPU %"));
    m_cpuChart->setValueSuffix("%");
    m_cpuChart->setYAxisRange(0, 100);
    m_cpuChart->setDarkTheme(true);
    cpuLayout->addWidget(m_cpuChart);
    
    chartsLayout->addWidget(cpuGroup);
    
    // Memory Chart
    QGroupBox* memGroup = new QGroupBox(tr("Memory Usage History"));
    QVBoxLayout* memLayout = new QVBoxLayout(memGroup);
    
    m_memoryChart = new InteractiveChart();
    m_memoryChart->setTitle(tr("Memory Usage Over Time"));
    m_memoryChart->setAxisTitles(tr("Time"), tr("Memory"));
    m_memoryChart->setValueSuffix(" MB");
    m_memoryChart->setAutoYAxisRange(true);
    m_memoryChart->setDarkTheme(true);
    memLayout->addWidget(m_memoryChart);
    
    chartsLayout->addWidget(memGroup);
    
    m_tabWidget->addTab(chartsTab, tr("Resource Usage"));
}

void ServiceHistoryDialog::createStatisticsTab()
{
    QWidget* statsTab = new QWidget();
    QVBoxLayout* statsLayout = new QVBoxLayout(statsTab);
    
    // Summary stats
    QGroupBox* summaryGroup = new QGroupBox(tr("Summary Statistics"));
    QGridLayout* summaryLayout = new QGridLayout(summaryGroup);
    
    int row = 0;
    
    // Availability
    summaryLayout->addWidget(new QLabel(tr("<b>Availability:</b>")), row, 0);
    m_availabilityLabel = new QLabel("-");
    m_availabilityLabel->setStyleSheet("font-size: 14pt; font-weight: bold; color: #4CAF50;");
    summaryLayout->addWidget(m_availabilityLabel, row, 1);
    
    summaryLayout->addWidget(new QLabel(tr("<b>Total Samples:</b>")), row, 2);
    m_totalSamplesLabel = new QLabel("-");
    summaryLayout->addWidget(m_totalSamplesLabel, row++, 3);
    
    // CPU stats
    summaryLayout->addWidget(new QLabel(tr("<b>CPU Usage:</b>")), row, 0);
    summaryLayout->addWidget(new QLabel(tr("Average:")), row, 2);
    m_avgCpuLabel = new QLabel("-");
    summaryLayout->addWidget(m_avgCpuLabel, row++, 3);
    
    summaryLayout->addWidget(new QLabel(), row, 0);
    summaryLayout->addWidget(new QLabel(tr("Peak:")), row, 2);
    m_maxCpuLabel = new QLabel("-");
    summaryLayout->addWidget(m_maxCpuLabel, row++, 3);
    
    // Memory stats
    summaryLayout->addWidget(new QLabel(tr("<b>Memory Usage:</b>")), row, 0);
    summaryLayout->addWidget(new QLabel(tr("Average:")), row, 2);
    m_avgMemoryLabel = new QLabel("-");
    summaryLayout->addWidget(m_avgMemoryLabel, row++, 3);
    
    summaryLayout->addWidget(new QLabel(), row, 0);
    summaryLayout->addWidget(new QLabel(tr("Peak:")), row, 2);
    m_maxMemoryLabel = new QLabel("-");
    summaryLayout->addWidget(m_maxMemoryLabel, row++, 3);
    
    // Reliability stats
    summaryLayout->addWidget(new QLabel(tr("<b>Reliability:</b>")), row, 0);
    summaryLayout->addWidget(new QLabel(tr("Crashes:")), row, 2);
    m_crashCountLabel = new QLabel("-");
    summaryLayout->addWidget(m_crashCountLabel, row++, 3);
    
    summaryLayout->addWidget(new QLabel(), row, 0);
    summaryLayout->addWidget(new QLabel(tr("Uptime:")), row, 2);
    m_uptimeLabel = new QLabel("-");
    summaryLayout->addWidget(m_uptimeLabel, row++, 3);
    
    summaryLayout->setColumnStretch(1, 1);
    summaryLayout->setColumnStretch(3, 1);
    
    statsLayout->addWidget(summaryGroup);
    statsLayout->addStretch();
    
    m_tabWidget->addTab(statsTab, tr("Statistics"));
}

void ServiceHistoryDialog::createCrashHistoryTab()
{
    QWidget* crashTab = new QWidget();
    QVBoxLayout* crashLayout = new QVBoxLayout(crashTab);
    
    QLabel* crashInfo = new QLabel(tr("Service crash and failure events:"));
    crashLayout->addWidget(crashInfo);
    
    m_crashTable = new QTableWidget();
    m_crashTable->setColumnCount(4);
    m_crashTable->setHorizontalHeaderLabels({
        tr("Timestamp"), tr("Previous State"), tr("Reason"), tr("Event ID")
    });
    m_crashTable->horizontalHeader()->setStretchLastSection(true);
    m_crashTable->setAlternatingRowColors(true);
    m_crashTable->setEditTriggers(QAbstractItemView::NoEditTriggers);
    m_crashTable->setSelectionBehavior(QAbstractItemView::SelectRows);
    
    crashLayout->addWidget(m_crashTable);
    
    m_tabWidget->addTab(crashTab, tr("Crash History"));
}

void ServiceHistoryDialog::createComparisonTab()
{
    QWidget* compTab = new QWidget();
    QVBoxLayout* compLayout = new QVBoxLayout(compTab);
    
    // Comparison controls
    QHBoxLayout* compToolbar = new QHBoxLayout();
    
    compToolbar->addWidget(new QLabel(tr("Compare:")));
    m_comparisonTypeCombo = new QComboBox();
    m_comparisonTypeCombo->addItem(tr("Today vs Yesterday"), 0);
    m_comparisonTypeCombo->addItem(tr("This Week vs Last Week"), 1);
    m_comparisonTypeCombo->addItem(tr("This Month vs Last Month"), 2);
    connect(m_comparisonTypeCombo, QOverload<int>::of(&QComboBox::currentIndexChanged),
            this, &ServiceHistoryDialog::updateComparisonChart);
    
    compToolbar->addWidget(m_comparisonTypeCombo);
    compToolbar->addSpacing(20);
    
    compToolbar->addWidget(new QLabel(tr("Metric:")));
    m_comparisonMetricCombo = new QComboBox();
    m_comparisonMetricCombo->addItem(tr("CPU Usage"), 0);
    m_comparisonMetricCombo->addItem(tr("Memory Usage"), 1);
    connect(m_comparisonMetricCombo, QOverload<int>::of(&QComboBox::currentIndexChanged),
            this, &ServiceHistoryDialog::updateComparisonChart);
    
    compToolbar->addWidget(m_comparisonMetricCombo);
    compToolbar->addStretch();
    
    QPushButton* compareButton = new QPushButton(tr("Compare"));
    connect(compareButton, &QPushButton::clicked, this, &ServiceHistoryDialog::onCompareClicked);
    compToolbar->addWidget(compareButton);
    
    compLayout->addLayout(compToolbar);
    
    // Comparison chart
    m_comparisonChart = new ComparisonChart();
    m_comparisonChart->setDarkTheme(true);
    compLayout->addWidget(m_comparisonChart, 1);
    
    m_tabWidget->addTab(compTab, tr("Period Comparison"));
}

void ServiceHistoryDialog::createTopServicesTab()
{
    QWidget* topTab = new QWidget();
    QVBoxLayout* topLayout = new QVBoxLayout(topTab);
    
    // Top CPU Services
    QGroupBox* cpuGroup = new QGroupBox(tr("Top Services by CPU Usage"));
    QVBoxLayout* cpuLayout = new QVBoxLayout(cpuGroup);
    
    m_topCpuTable = new QTableWidget();
    m_topCpuTable->setColumnCount(3);
    m_topCpuTable->setHorizontalHeaderLabels({tr("Service"), tr("Display Name"), tr("Avg CPU %")});
    m_topCpuTable->horizontalHeader()->setStretchLastSection(true);
    m_topCpuTable->setAlternatingRowColors(true);
    m_topCpuTable->setEditTriggers(QAbstractItemView::NoEditTriggers);
    m_topCpuTable->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_topCpuTable->setMaximumHeight(200);
    cpuLayout->addWidget(m_topCpuTable);
    
    topLayout->addWidget(cpuGroup);
    
    // Top Memory Services
    QGroupBox* memGroup = new QGroupBox(tr("Top Services by Memory Usage"));
    QVBoxLayout* memLayout = new QVBoxLayout(memGroup);
    
    m_topMemoryTable = new QTableWidget();
    m_topMemoryTable->setColumnCount(3);
    m_topMemoryTable->setHorizontalHeaderLabels({tr("Service"), tr("Display Name"), tr("Avg Memory")});
    m_topMemoryTable->horizontalHeader()->setStretchLastSection(true);
    m_topMemoryTable->setAlternatingRowColors(true);
    m_topMemoryTable->setEditTriggers(QAbstractItemView::NoEditTriggers);
    m_topMemoryTable->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_topMemoryTable->setMaximumHeight(200);
    memLayout->addWidget(m_topMemoryTable);
    
    topLayout->addWidget(memGroup);
    
    // Top Crashing Services
    QGroupBox* crashGroup = new QGroupBox(tr("Most Frequently Crashing Services"));
    QVBoxLayout* crashLayout = new QVBoxLayout(crashGroup);
    
    m_topCrashTable = new QTableWidget();
    m_topCrashTable->setColumnCount(3);
    m_topCrashTable->setHorizontalHeaderLabels({tr("Service"), tr("Display Name"), tr("Crash Count")});
    m_topCrashTable->horizontalHeader()->setStretchLastSection(true);
    m_topCrashTable->setAlternatingRowColors(true);
    m_topCrashTable->setEditTriggers(QAbstractItemView::NoEditTriggers);
    m_topCrashTable->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_topCrashTable->setMaximumHeight(200);
    crashLayout->addWidget(m_topCrashTable);
    
    topLayout->addWidget(crashGroup);
    topLayout->addStretch();
    
    m_tabWidget->addTab(topTab, tr("Top Services"));
}

void ServiceHistoryDialog::loadServices()
{
    m_serviceCombo->clear();
    
    if (!m_historyManager || !m_historyManager->isReady()) {
        return;
    }
    
    // Add "All Services" option first
    m_serviceCombo->addItem(tr("-- All Services --"), QString());
    
    // Load all recorded services from history
    QStringList services = m_historyManager->getAllRecordedServices();
    for (const QString& svc : services) {
        m_serviceCombo->addItem(svc, svc);
    }
    
    m_statusLabel->setText(tr("Loaded %1 services from history").arg(services.size()));
}

void ServiceHistoryDialog::setService(const QString& serviceName)
{
    int index = m_serviceCombo->findData(serviceName);
    if (index >= 0) {
        m_serviceCombo->setCurrentIndex(index);
    }
}

void ServiceHistoryDialog::onServiceChanged(int index)
{
    Q_UNUSED(index)
    m_currentService = m_serviceCombo->currentData().toString();
    onRefreshRequested();
}

void ServiceHistoryDialog::onTimeRangeChanged(const QDateTime& start, const QDateTime& end)
{
    m_startTime = start;
    m_endTime = end;
    onRefreshRequested();
}

void ServiceHistoryDialog::onRefreshRequested()
{
    m_statusLabel->setText(tr("Loading data..."));
    QApplication::processEvents();
    
    updateCharts();
    updateStatistics();
    updateCrashTable();
    updateTopServicesTable();
    updateComparisonChart();
    
    m_statusLabel->setText(tr("Data loaded for %1 - %2")
        .arg(m_startTime.toString("dd/MM/yyyy HH:mm"))
        .arg(m_endTime.toString("dd/MM/yyyy HH:mm")));
}

void ServiceHistoryDialog::onExportClicked()
{
    QString filter = tr("CSV Files (*.csv);;JSON Files (*.json)");
    QString filePath = QFileDialog::getSaveFileName(this, tr("Export Service History"), QString(), filter);
    
    if (filePath.isEmpty()) {
        return;
    }
    
    bool success = false;
    
    if (filePath.endsWith(".json", Qt::CaseInsensitive)) {
        success = m_historyManager->exportToJson(filePath, m_currentService, m_startTime, m_endTime);
    } else {
        // Default to CSV
        if (!filePath.endsWith(".csv", Qt::CaseInsensitive)) {
            filePath += ".csv";
        }
        success = m_historyManager->exportToCsv(filePath, m_currentService, m_startTime, m_endTime);
    }
    
    if (success) {
        QMessageBox::information(this, tr("Export"), tr("Data exported successfully to:\n%1").arg(filePath));
    } else {
        QMessageBox::warning(this, tr("Export"), tr("Failed to export data."));
    }
}

void ServiceHistoryDialog::onCompareClicked()
{
    updateComparisonChart();
}

void ServiceHistoryDialog::updateCharts()
{
    if (!m_historyManager || !m_historyManager->isReady()) {
        return;
    }
    
    m_cpuChart->clear();
    m_memoryChart->clear();
    
    QString serviceName = m_currentService;
    
    // If no specific service selected, show aggregate or skip
    if (serviceName.isEmpty()) {
        // For "All Services", we can show aggregated data
        // For now, just show a message
        return;
    }
    
    // Get history data
    auto history = m_historyManager->getServiceHistory(serviceName, m_startTime, m_endTime, 2000);
    
    if (history.empty()) {
        return;
    }
    
    // Prepare data points for charts
    std::vector<QPointF> cpuPoints;
    std::vector<QPointF> memoryPoints;
    cpuPoints.reserve(history.size());
    memoryPoints.reserve(history.size());
    
    for (const auto& snapshot : history) {
        qint64 timestamp = snapshot.timestamp.toMSecsSinceEpoch();
        cpuPoints.emplace_back(timestamp, snapshot.cpuUsagePercent);
        memoryPoints.emplace_back(timestamp, snapshot.memoryUsageBytes / (1024.0 * 1024.0)); // Convert to MB
    }
    
    m_cpuChart->addSeries(serviceName + " CPU", QColor(0, 120, 215), cpuPoints);
    m_memoryChart->addSeries(serviceName + " Memory", QColor(156, 39, 176), memoryPoints);
    
    m_cpuChart->resetZoom();
    m_memoryChart->resetZoom();
}

void ServiceHistoryDialog::updateStatistics()
{
    if (!m_historyManager || !m_historyManager->isReady() || m_currentService.isEmpty()) {
        m_totalSamplesLabel->setText("-");
        m_availabilityLabel->setText("-");
        m_avgCpuLabel->setText("-");
        m_maxCpuLabel->setText("-");
        m_avgMemoryLabel->setText("-");
        m_maxMemoryLabel->setText("-");
        m_crashCountLabel->setText("-");
        m_uptimeLabel->setText("-");
        return;
    }
    
    // Get aggregated metrics
    auto metrics = m_historyManager->getAggregatedMetrics(m_currentService, m_startTime, m_endTime);
    
    m_totalSamplesLabel->setText(QString::number(metrics.totalSamples));
    
    // Availability
    QString availColor;
    if (metrics.availabilityPercent >= 99.0) {
        availColor = "#4CAF50";  // Green
    } else if (metrics.availabilityPercent >= 90.0) {
        availColor = "#FF9800";  // Orange
    } else {
        availColor = "#f44336";  // Red
    }
    m_availabilityLabel->setText(QString("%1%").arg(metrics.availabilityPercent, 0, 'f', 2));
    m_availabilityLabel->setStyleSheet(QString("font-size: 14pt; font-weight: bold; color: %1;").arg(availColor));
    
    // CPU stats
    m_avgCpuLabel->setText(QString("%1%").arg(metrics.avgCpuUsage, 0, 'f', 2));
    m_maxCpuLabel->setText(QString("%1%").arg(metrics.maxCpuUsage, 0, 'f', 2));
    
    // Memory stats
    m_avgMemoryLabel->setText(formatBytes(metrics.avgMemoryUsage));
    m_maxMemoryLabel->setText(formatBytes(metrics.maxMemoryUsage));
    
    // Crash count
    m_crashCountLabel->setText(QString::number(metrics.crashCount));
    if (metrics.crashCount > 0) {
        m_crashCountLabel->setStyleSheet("color: #f44336; font-weight: bold;");
    } else {
        m_crashCountLabel->setStyleSheet("color: #4CAF50; font-weight: bold;");
    }
    
    // Uptime (estimated from running samples)
    if (metrics.totalSamples > 0) {
        // Estimate based on recording interval (assume 5 seconds)
        qint64 uptimeSeconds = (metrics.runningCount * 5);
        m_uptimeLabel->setText(formatDuration(uptimeSeconds));
    } else {
        m_uptimeLabel->setText("-");
    }
}

void ServiceHistoryDialog::updateCrashTable()
{
    m_crashTable->setRowCount(0);
    
    if (!m_historyManager || !m_historyManager->isReady()) {
        return;
    }
    
    auto crashes = m_historyManager->getCrashHistory(m_currentService, m_startTime, m_endTime, 100);
    
    for (const auto& crash : crashes) {
        int row = m_crashTable->rowCount();
        m_crashTable->insertRow(row);
        
        m_crashTable->setItem(row, 0, new QTableWidgetItem(
            crash.timestamp.toString("dd/MM/yyyy HH:mm:ss")));
        
        QString prevState;
        switch (crash.previousState) {
            case ServiceState::Running: prevState = tr("Running"); break;
            case ServiceState::Stopped: prevState = tr("Stopped"); break;
            case ServiceState::Paused: prevState = tr("Paused"); break;
            default: prevState = tr("Unknown"); break;
        }
        m_crashTable->setItem(row, 1, new QTableWidgetItem(prevState));
        m_crashTable->setItem(row, 2, new QTableWidgetItem(crash.failureReason));
        m_crashTable->setItem(row, 3, new QTableWidgetItem(QString::number(crash.eventId)));
    }
    
    m_crashTable->resizeColumnsToContents();
}

void ServiceHistoryDialog::updateTopServicesTable()
{
    if (!m_historyManager || !m_historyManager->isReady()) {
        return;
    }
    
    // Top CPU Services
    m_topCpuTable->setRowCount(0);
    auto topCpu = m_historyManager->getTopCpuServices(10, m_startTime, m_endTime);
    for (const auto& [serviceName, cpuUsage] : topCpu) {
        int row = m_topCpuTable->rowCount();
        m_topCpuTable->insertRow(row);
        
        m_topCpuTable->setItem(row, 0, new QTableWidgetItem(serviceName));
        m_topCpuTable->setItem(row, 1, new QTableWidgetItem(serviceName)); // TODO: Get display name
        m_topCpuTable->setItem(row, 2, new QTableWidgetItem(QString("%1%").arg(cpuUsage, 0, 'f', 2)));
    }
    m_topCpuTable->resizeColumnsToContents();
    
    // Top Memory Services
    m_topMemoryTable->setRowCount(0);
    auto topMem = m_historyManager->getTopMemoryServices(10, m_startTime, m_endTime);
    for (const auto& [serviceName, memUsage] : topMem) {
        int row = m_topMemoryTable->rowCount();
        m_topMemoryTable->insertRow(row);
        
        m_topMemoryTable->setItem(row, 0, new QTableWidgetItem(serviceName));
        m_topMemoryTable->setItem(row, 1, new QTableWidgetItem(serviceName)); // TODO: Get display name
        m_topMemoryTable->setItem(row, 2, new QTableWidgetItem(formatBytes(memUsage)));
    }
    m_topMemoryTable->resizeColumnsToContents();
    
    // Top Crashing Services
    m_topCrashTable->setRowCount(0);
    auto topCrash = m_historyManager->getTopCrashingServices(10, m_startTime, m_endTime);
    for (const auto& [serviceName, crashCount] : topCrash) {
        int row = m_topCrashTable->rowCount();
        m_topCrashTable->insertRow(row);
        
        m_topCrashTable->setItem(row, 0, new QTableWidgetItem(serviceName));
        m_topCrashTable->setItem(row, 1, new QTableWidgetItem(serviceName)); // TODO: Get display name
        m_topCrashTable->setItem(row, 2, new QTableWidgetItem(QString::number(crashCount)));
    }
    m_topCrashTable->resizeColumnsToContents();
}

void ServiceHistoryDialog::updateComparisonChart()
{
    if (!m_historyManager || !m_historyManager->isReady() || m_currentService.isEmpty()) {
        return;
    }
    
    int compType = m_comparisonTypeCombo->currentIndex();
    int metricType = m_comparisonMetricCombo->currentIndex();
    
    QDateTime period1Start, period1End, period2Start, period2End;
    QString period1Name, period2Name;
    
    QDateTime now = QDateTime::currentDateTime();
    
    switch (compType) {
        case 0:  // Today vs Yesterday
            period1Start = QDateTime(now.date(), QTime(0, 0));
            period1End = now;
            period2Start = period1Start.addDays(-1);
            period2End = period1End.addDays(-1);
            period1Name = tr("Today");
            period2Name = tr("Yesterday");
            break;
        case 1:  // This Week vs Last Week
            {
                int daysToMonday = now.date().dayOfWeek() - 1;
                period1Start = QDateTime(now.date().addDays(-daysToMonday), QTime(0, 0));
                period1End = now;
                period2Start = period1Start.addDays(-7);
                period2End = period1End.addDays(-7);
                period1Name = tr("This Week");
                period2Name = tr("Last Week");
            }
            break;
        case 2:  // This Month vs Last Month
            period1Start = QDateTime(QDate(now.date().year(), now.date().month(), 1), QTime(0, 0));
            period1End = now;
            period2Start = period1Start.addMonths(-1);
            period2End = period2Start.addMonths(1).addDays(-1);
            period1Name = tr("This Month");
            period2Name = tr("Last Month");
            break;
    }
    
    // Get raw history data for both periods
    auto history1 = m_historyManager->getServiceHistory(m_currentService, period1Start, period1End, 500);
    auto history2 = m_historyManager->getServiceHistory(m_currentService, period2Start, period2End, 500);
    
    // Convert to chart data
    std::vector<QPointF> data1, data2;
    
    if (metricType == 0) {  // CPU
        m_comparisonChart->setValueSuffix("%");
        m_comparisonChart->setYAxisRange(0, 100);
        
        for (const auto& snap : history1) {
            data1.emplace_back(snap.timestamp.toMSecsSinceEpoch(), snap.cpuUsagePercent);
        }
        for (const auto& snap : history2) {
            data2.emplace_back(snap.timestamp.toMSecsSinceEpoch(), snap.cpuUsagePercent);
        }
    } else {  // Memory
        m_comparisonChart->setValueSuffix(" MB");
        m_comparisonChart->setAutoYAxisRange(true);
        
        for (const auto& snap : history1) {
            data1.emplace_back(snap.timestamp.toMSecsSinceEpoch(), 
                               snap.memoryUsageBytes / (1024.0 * 1024.0));
        }
        for (const auto& snap : history2) {
            data2.emplace_back(snap.timestamp.toMSecsSinceEpoch(),
                               snap.memoryUsageBytes / (1024.0 * 1024.0));
        }
    }
    
    m_comparisonChart->clear();
    m_comparisonChart->setPeriod1(period1Name, period1Start, period1End, data1, QColor(0, 120, 215));
    m_comparisonChart->setPeriod2(period2Name, period2Start, period2End, data2, QColor(255, 127, 14));
}

QString ServiceHistoryDialog::formatBytes(qint64 bytes) const
{
    const char* units[] = {"B", "KB", "MB", "GB", "TB"};
    int unitIndex = 0;
    double size = static_cast<double>(bytes);
    
    while (size >= 1024.0 && unitIndex < 4) {
        size /= 1024.0;
        unitIndex++;
    }
    
    return QString("%1 %2").arg(size, 0, 'f', unitIndex > 0 ? 1 : 0).arg(units[unitIndex]);
}

QString ServiceHistoryDialog::formatDuration(qint64 seconds) const
{
    if (seconds < 60) {
        return tr("%1 seconds").arg(seconds);
    }
    
    qint64 minutes = seconds / 60;
    if (minutes < 60) {
        return tr("%1 minutes").arg(minutes);
    }
    
    qint64 hours = minutes / 60;
    minutes = minutes % 60;
    if (hours < 24) {
        return tr("%1h %2m").arg(hours).arg(minutes);
    }
    
    qint64 days = hours / 24;
    hours = hours % 24;
    return tr("%1d %2h %3m").arg(days).arg(hours).arg(minutes);
}
