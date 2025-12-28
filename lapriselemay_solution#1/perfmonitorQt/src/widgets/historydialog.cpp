#include "historydialog.h"
#include "interactivechart.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QFormLayout>
#include <QToolBar>
#include <QSplitter>
#include <QScrollArea>
#include <QFileDialog>
#include <QMessageBox>
#include <QHeaderView>
#include <QLineEdit>
#include <QDebug>

// ==================== HistoryDialog ====================

HistoryDialog::HistoryDialog(MetricsHistory* history, QWidget* parent)
    : QDialog(parent)
    , m_history(history)
{
    setWindowTitle(tr("Metrics History"));
    setMinimumSize(1200, 800);
    resize(1400, 900);
    
    setupUi();
    
    // Set default selections
    m_selectedMetrics = {MetricType::CpuUsage, MetricType::MemoryUsed};
    for (auto type : m_selectedMetrics) {
        if (m_metricChecks.count(type)) {
            m_metricChecks[type]->setChecked(true);
        }
    }
    
    loadData();
}

HistoryDialog::~HistoryDialog() = default;

void HistoryDialog::setupUi()
{
    QVBoxLayout* mainLayout = new QVBoxLayout(this);
    mainLayout->setSpacing(10);
    
    createToolbar();
    
    // Main content area with splitter
    QSplitter* splitter = new QSplitter(Qt::Horizontal, this);
    
    // Left panel - metric selection
    QWidget* leftPanel = new QWidget();
    QVBoxLayout* leftLayout = new QVBoxLayout(leftPanel);
    leftLayout->setContentsMargins(0, 0, 0, 0);
    
    m_metricGroup = new QGroupBox(tr("Metrics"));
    QVBoxLayout* metricLayout = new QVBoxLayout(m_metricGroup);
    
    // Create checkboxes for each metric type
    auto addMetricCheck = [this, metricLayout](MetricType type, const QString& name) {
        QCheckBox* check = new QCheckBox(name);
        connect(check, &QCheckBox::toggled, this, &HistoryDialog::onMetricSelectionChanged);
        metricLayout->addWidget(check);
        m_metricChecks[type] = check;
    };
    
    QLabel* cpuLabel = new QLabel(tr("<b>CPU</b>"));
    metricLayout->addWidget(cpuLabel);
    addMetricCheck(MetricType::CpuUsage, tr("CPU Usage"));
    addMetricCheck(MetricType::CpuTemperature, tr("CPU Temperature"));
    
    QLabel* memLabel = new QLabel(tr("<b>Memory</b>"));
    metricLayout->addWidget(memLabel);
    addMetricCheck(MetricType::MemoryUsed, tr("Memory Used"));
    addMetricCheck(MetricType::MemoryAvailable, tr("Memory Available"));
    
    QLabel* gpuLabel = new QLabel(tr("<b>GPU</b>"));
    metricLayout->addWidget(gpuLabel);
    addMetricCheck(MetricType::GpuUsage, tr("GPU Usage"));
    addMetricCheck(MetricType::GpuMemory, tr("GPU Memory"));
    addMetricCheck(MetricType::GpuTemperature, tr("GPU Temperature"));
    
    QLabel* diskLabel = new QLabel(tr("<b>Disk</b>"));
    metricLayout->addWidget(diskLabel);
    addMetricCheck(MetricType::DiskRead, tr("Disk Read"));
    addMetricCheck(MetricType::DiskWrite, tr("Disk Write"));
    
    QLabel* netLabel = new QLabel(tr("<b>Network</b>"));
    metricLayout->addWidget(netLabel);
    addMetricCheck(MetricType::NetworkSend, tr("Network Send"));
    addMetricCheck(MetricType::NetworkReceive, tr("Network Receive"));
    
    QLabel* battLabel = new QLabel(tr("<b>Battery</b>"));
    metricLayout->addWidget(battLabel);
    addMetricCheck(MetricType::BatteryPercent, tr("Battery %"));
    addMetricCheck(MetricType::BatteryHealth, tr("Battery Health"));
    
    metricLayout->addStretch();
    leftLayout->addWidget(m_metricGroup);
    
    // Right panel - chart and stats
    QWidget* rightPanel = new QWidget();
    QVBoxLayout* rightLayout = new QVBoxLayout(rightPanel);
    rightLayout->setContentsMargins(0, 0, 0, 0);
    
    createChartArea();
    rightLayout->addWidget(m_tabWidget, 1);
    
    createStatisticsPanel();
    
    splitter->addWidget(leftPanel);
    splitter->addWidget(rightPanel);
    splitter->setStretchFactor(0, 0);
    splitter->setStretchFactor(1, 1);
    splitter->setSizes({250, 1150});
    
    mainLayout->addWidget(splitter, 1);
}

void HistoryDialog::createToolbar()
{
    QHBoxLayout* toolbarLayout = new QHBoxLayout();
    
    // Time range selector
    QLabel* rangeLabel = new QLabel(tr("Time Range:"));
    m_timeRangeCombo = new QComboBox();
    m_timeRangeCombo->addItem(tr("Last 1 Hour"), static_cast<int>(TimeRange::Last1Hour));
    m_timeRangeCombo->addItem(tr("Last 6 Hours"), static_cast<int>(TimeRange::Last6Hours));
    m_timeRangeCombo->addItem(tr("Last 24 Hours"), static_cast<int>(TimeRange::Last24Hours));
    m_timeRangeCombo->addItem(tr("Last 7 Days"), static_cast<int>(TimeRange::Last7Days));
    m_timeRangeCombo->addItem(tr("Last 30 Days"), static_cast<int>(TimeRange::Last30Days));
    m_timeRangeCombo->addItem(tr("Custom..."), static_cast<int>(TimeRange::Custom));
    m_timeRangeCombo->setCurrentIndex(2);  // Default to 24 hours
    connect(m_timeRangeCombo, QOverload<int>::of(&QComboBox::currentIndexChanged),
            this, &HistoryDialog::onTimeRangeChanged);
    
    // Custom date range
    m_startDateEdit = new QDateTimeEdit(QDateTime::currentDateTime().addDays(-1));
    m_startDateEdit->setDisplayFormat("dd/MM/yyyy HH:mm");
    m_startDateEdit->setCalendarPopup(true);
    m_startDateEdit->setEnabled(false);
    
    m_endDateEdit = new QDateTimeEdit(QDateTime::currentDateTime());
    m_endDateEdit->setDisplayFormat("dd/MM/yyyy HH:mm");
    m_endDateEdit->setCalendarPopup(true);
    m_endDateEdit->setEnabled(false);
    
    // Buttons
    m_refreshButton = new QPushButton(tr("Refresh"));
    m_refreshButton->setIcon(style()->standardIcon(QStyle::SP_BrowserReload));
    connect(m_refreshButton, &QPushButton::clicked, this, &HistoryDialog::onRefreshClicked);
    
    m_exportButton = new QPushButton(tr("Export..."));
    m_exportButton->setIcon(style()->standardIcon(QStyle::SP_DialogSaveButton));
    connect(m_exportButton, &QPushButton::clicked, this, &HistoryDialog::onExportClicked);
    
    m_compareButton = new QPushButton(tr("Compare Periods"));
    connect(m_compareButton, &QPushButton::clicked, this, &HistoryDialog::onCompareClicked);
    
    toolbarLayout->addWidget(rangeLabel);
    toolbarLayout->addWidget(m_timeRangeCombo);
    toolbarLayout->addWidget(new QLabel(tr("From:")));
    toolbarLayout->addWidget(m_startDateEdit);
    toolbarLayout->addWidget(new QLabel(tr("To:")));
    toolbarLayout->addWidget(m_endDateEdit);
    toolbarLayout->addStretch();
    toolbarLayout->addWidget(m_refreshButton);
    toolbarLayout->addWidget(m_exportButton);
    toolbarLayout->addWidget(m_compareButton);
    
    static_cast<QVBoxLayout*>(layout())->addLayout(toolbarLayout);
}

void HistoryDialog::createChartArea()
{
    m_tabWidget = new QTabWidget();
    
    // Chart tab
    QWidget* chartTab = new QWidget();
    QVBoxLayout* chartLayout = new QVBoxLayout(chartTab);
    chartLayout->setContentsMargins(0, 0, 0, 0);
    
    m_chart = new InteractiveChart();
    m_chart->setTitle(tr("System Metrics History"));
    m_chart->setAxisTitles(tr("Time"), tr("Value"));
    m_chart->setDarkTheme(true);
    
    connect(m_chart, &InteractiveChart::timeRangeSelected,
            this, &HistoryDialog::onChartTimeRangeSelected);
    
    chartLayout->addWidget(m_chart);
    
    m_tabWidget->addTab(chartTab, tr("Chart"));
    
    // Comparison tab
    createComparisonTab();
}

void HistoryDialog::createStatisticsPanel()
{
    QGroupBox* statsGroup = new QGroupBox(tr("Statistics"));
    QGridLayout* statsLayout = new QGridLayout(statsGroup);
    
    statsLayout->addWidget(new QLabel(tr("Time Range:")), 0, 0);
    m_statsTimeRangeLabel = new QLabel("-");
    statsLayout->addWidget(m_statsTimeRangeLabel, 0, 1);
    
    statsLayout->addWidget(new QLabel(tr("Samples:")), 0, 2);
    m_statsSamplesLabel = new QLabel("-");
    statsLayout->addWidget(m_statsSamplesLabel, 0, 3);
    
    statsLayout->addWidget(new QLabel(tr("Minimum:")), 1, 0);
    m_statsMinLabel = new QLabel("-");
    statsLayout->addWidget(m_statsMinLabel, 1, 1);
    
    statsLayout->addWidget(new QLabel(tr("Maximum:")), 1, 2);
    m_statsMaxLabel = new QLabel("-");
    statsLayout->addWidget(m_statsMaxLabel, 1, 3);
    
    statsLayout->addWidget(new QLabel(tr("Average:")), 2, 0);
    m_statsAvgLabel = new QLabel("-");
    statsLayout->addWidget(m_statsAvgLabel, 2, 1);
    
    static_cast<QVBoxLayout*>(static_cast<QWidget*>(m_tabWidget->parent())->layout())->addWidget(statsGroup);
}

void HistoryDialog::createComparisonTab()
{
    QWidget* compTab = new QWidget();
    QVBoxLayout* compLayout = new QVBoxLayout(compTab);
    
    // Comparison type selector
    QHBoxLayout* compToolbar = new QHBoxLayout();
    compToolbar->addWidget(new QLabel(tr("Compare:")));
    
    m_comparisonTypeCombo = new QComboBox();
    m_comparisonTypeCombo->addItem(tr("Today vs Yesterday"));
    m_comparisonTypeCombo->addItem(tr("This Week vs Last Week"));
    connect(m_comparisonTypeCombo, QOverload<int>::of(&QComboBox::currentIndexChanged),
            this, &HistoryDialog::updateComparisonTable);
    compToolbar->addWidget(m_comparisonTypeCombo);
    compToolbar->addStretch();
    
    compLayout->addLayout(compToolbar);
    
    // Comparison table
    m_comparisonTable = new QTableWidget();
    m_comparisonTable->setColumnCount(7);
    m_comparisonTable->setHorizontalHeaderLabels({
        tr("Metric"), tr("Period 1 Avg"), tr("Period 1 Min"), tr("Period 1 Max"),
        tr("Period 2 Avg"), tr("Change"), tr("Change %")
    });
    m_comparisonTable->horizontalHeader()->setStretchLastSection(true);
    m_comparisonTable->setAlternatingRowColors(true);
    m_comparisonTable->setEditTriggers(QAbstractItemView::NoEditTriggers);
    
    compLayout->addWidget(m_comparisonTable);
    
    m_tabWidget->addTab(compTab, tr("Period Comparison"));
}

void HistoryDialog::setDefaultMetric(MetricType type)
{
    m_selectedMetrics = {type};
    for (auto& [t, check] : m_metricChecks) {
        check->setChecked(t == type);
    }
}

void HistoryDialog::setDefaultTimeRange(TimeRange range)
{
    m_currentTimeRange = range;
    m_timeRangeCombo->setCurrentIndex(static_cast<int>(range));
}

void HistoryDialog::onTimeRangeChanged()
{
    int index = m_timeRangeCombo->currentIndex();
    m_currentTimeRange = static_cast<TimeRange>(m_timeRangeCombo->currentData().toInt());
    
    bool isCustom = (m_currentTimeRange == TimeRange::Custom);
    m_startDateEdit->setEnabled(isCustom);
    m_endDateEdit->setEnabled(isCustom);
    
    if (!isCustom) {
        loadData();
    }
}

void HistoryDialog::onMetricSelectionChanged()
{
    m_selectedMetrics.clear();
    
    for (const auto& [type, check] : m_metricChecks) {
        if (check->isChecked()) {
            m_selectedMetrics.push_back(type);
        }
    }
    
    updateChart();
    updateStatistics();
}

void HistoryDialog::onRefreshClicked()
{
    if (m_currentTimeRange == TimeRange::Custom) {
        m_customStart = m_startDateEdit->dateTime();
        m_customEnd = m_endDateEdit->dateTime();
    }
    loadData();
}

void HistoryDialog::onExportClicked()
{
    ExportDialog dlg(m_history, this);
    
    auto [start, end] = MetricsHistory::timeRangeToDateTime(m_currentTimeRange);
    if (m_currentTimeRange == TimeRange::Custom) {
        start = m_customStart;
        end = m_customEnd;
    }
    
    dlg.setTimeRange(start, end);
    dlg.setSelectedMetrics(m_selectedMetrics);
    dlg.exec();
}

void HistoryDialog::onCompareClicked()
{
    m_tabWidget->setCurrentIndex(1);  // Switch to comparison tab
    updateComparisonTable();
}

void HistoryDialog::onChartTimeRangeSelected(const QDateTime& start, const QDateTime& end)
{
    m_currentTimeRange = TimeRange::Custom;
    m_timeRangeCombo->setCurrentIndex(m_timeRangeCombo->count() - 1);
    m_startDateEdit->setDateTime(start);
    m_endDateEdit->setDateTime(end);
    m_startDateEdit->setEnabled(true);
    m_endDateEdit->setEnabled(true);
    m_customStart = start;
    m_customEnd = end;
}

void HistoryDialog::loadData()
{
    if (!m_history || !m_history->isReady()) return;
    
    updateChart();
    updateStatistics();
}

void HistoryDialog::updateChart()
{
    m_chart->clear();
    
    auto [start, end] = MetricsHistory::timeRangeToDateTime(m_currentTimeRange);
    if (m_currentTimeRange == TimeRange::Custom) {
        start = m_customStart;
        end = m_customEnd;
    }
    
    for (MetricType type : m_selectedMetrics) {
        auto data = m_history->getMetricData(type, start, end, QString(), 2000);
        
        if (data.empty()) continue;
        
        std::vector<QPointF> points;
        points.reserve(data.size());
        
        for (const auto& pt : data) {
            points.emplace_back(pt.timestamp.toMSecsSinceEpoch(), pt.value);
        }
        
        QString name = MetricsHistory::metricTypeToString(type);
        m_chart->addSeries(name, getMetricColor(type), points);
    }
    
    m_chart->resetZoom();
}

void HistoryDialog::updateStatistics()
{
    if (m_selectedMetrics.empty()) {
        m_statsMinLabel->setText("-");
        m_statsMaxLabel->setText("-");
        m_statsAvgLabel->setText("-");
        m_statsSamplesLabel->setText("-");
        m_statsTimeRangeLabel->setText("-");
        return;
    }
    
    auto [start, end] = MetricsHistory::timeRangeToDateTime(m_currentTimeRange);
    if (m_currentTimeRange == TimeRange::Custom) {
        start = m_customStart;
        end = m_customEnd;
    }
    
    m_statsTimeRangeLabel->setText(QString("%1 - %2")
        .arg(start.toString("dd/MM/yyyy HH:mm"))
        .arg(end.toString("dd/MM/yyyy HH:mm")));
    
    // Get stats for first selected metric
    MetricType type = m_selectedMetrics[0];
    auto data = m_history->getMetricData(type, start, end);
    
    if (data.empty()) {
        m_statsMinLabel->setText("-");
        m_statsMaxLabel->setText("-");
        m_statsAvgLabel->setText("-");
        m_statsSamplesLabel->setText("0");
        return;
    }
    
    double minVal = data[0].value;
    double maxVal = data[0].value;
    double sum = 0;
    
    for (const auto& pt : data) {
        minVal = qMin(minVal, pt.value);
        maxVal = qMax(maxVal, pt.value);
        sum += pt.value;
    }
    
    double avg = sum / data.size();
    QString unit = getMetricUnit(type);
    
    m_statsMinLabel->setText(QString("%1%2").arg(minVal, 0, 'f', 1).arg(unit));
    m_statsMaxLabel->setText(QString("%1%2").arg(maxVal, 0, 'f', 1).arg(unit));
    m_statsAvgLabel->setText(QString("%1%2").arg(avg, 0, 'f', 1).arg(unit));
    m_statsSamplesLabel->setText(QString::number(data.size()));
}

void HistoryDialog::updateComparisonTable()
{
    m_comparisonTable->setRowCount(0);
    
    if (!m_history || !m_history->isReady()) return;
    
    // Compare all metric types
    std::vector<MetricType> allTypes = {
        MetricType::CpuUsage, MetricType::MemoryUsed, MetricType::GpuUsage,
        MetricType::DiskRead, MetricType::DiskWrite,
        MetricType::NetworkSend, MetricType::NetworkReceive
    };
    
    for (MetricType type : allTypes) {
        PeriodComparison comp;
        
        if (m_comparisonTypeCombo->currentIndex() == 0) {
            comp = m_history->compareTodayWithYesterday(type);
        } else {
            comp = m_history->compareThisWeekWithLastWeek(type);
        }
        
        int row = m_comparisonTable->rowCount();
        m_comparisonTable->insertRow(row);
        
        QString unit = getMetricUnit(type);
        
        m_comparisonTable->setItem(row, 0, new QTableWidgetItem(
            MetricsHistory::metricTypeToString(type)));
        m_comparisonTable->setItem(row, 1, new QTableWidgetItem(
            QString("%1%2").arg(comp.period1Avg, 0, 'f', 1).arg(unit)));
        m_comparisonTable->setItem(row, 2, new QTableWidgetItem(
            QString("%1%2").arg(comp.period1Min, 0, 'f', 1).arg(unit)));
        m_comparisonTable->setItem(row, 3, new QTableWidgetItem(
            QString("%1%2").arg(comp.period1Max, 0, 'f', 1).arg(unit)));
        m_comparisonTable->setItem(row, 4, new QTableWidgetItem(
            QString("%1%2").arg(comp.period2Avg, 0, 'f', 1).arg(unit)));
        
        // Change
        QString changeStr = QString("%1%2%3")
            .arg(comp.avgDifference >= 0 ? "+" : "")
            .arg(comp.avgDifference, 0, 'f', 1)
            .arg(unit);
        QTableWidgetItem* changeItem = new QTableWidgetItem(changeStr);
        changeItem->setForeground(comp.avgDifference >= 0 ? Qt::red : Qt::green);
        m_comparisonTable->setItem(row, 5, changeItem);
        
        // Change %
        QString changePctStr = QString("%1%2%")
            .arg(comp.avgDifferencePercent >= 0 ? "+" : "")
            .arg(comp.avgDifferencePercent, 0, 'f', 1);
        QTableWidgetItem* changePctItem = new QTableWidgetItem(changePctStr);
        changePctItem->setForeground(comp.avgDifferencePercent >= 0 ? Qt::red : Qt::green);
        m_comparisonTable->setItem(row, 6, changePctItem);
    }
    
    m_comparisonTable->resizeColumnsToContents();
}

QString HistoryDialog::formatValue(MetricType type, double value) const
{
    return QString::number(value, 'f', 1) + getMetricUnit(type);
}

QString HistoryDialog::getMetricUnit(MetricType type) const
{
    switch (type) {
        case MetricType::CpuUsage:
        case MetricType::GpuUsage:
        case MetricType::BatteryPercent:
        case MetricType::BatteryHealth:
            return "%";
        case MetricType::CpuTemperature:
        case MetricType::GpuTemperature:
            return "°C";
        case MetricType::MemoryUsed:
        case MetricType::MemoryAvailable:
        case MetricType::GpuMemory:
            return " GB";
        case MetricType::DiskRead:
        case MetricType::DiskWrite:
        case MetricType::NetworkSend:
        case MetricType::NetworkReceive:
            return " MB/s";
        default:
            return "";
    }
}

QColor HistoryDialog::getMetricColor(MetricType type) const
{
    switch (type) {
        case MetricType::CpuUsage: return QColor(0, 120, 215);
        case MetricType::CpuTemperature: return QColor(255, 87, 34);
        case MetricType::MemoryUsed: return QColor(156, 39, 176);
        case MetricType::MemoryAvailable: return QColor(103, 58, 183);
        case MetricType::GpuUsage: return QColor(76, 175, 80);
        case MetricType::GpuMemory: return QColor(139, 195, 74);
        case MetricType::GpuTemperature: return QColor(255, 152, 0);
        case MetricType::DiskRead: return QColor(33, 150, 243);
        case MetricType::DiskWrite: return QColor(3, 169, 244);
        case MetricType::NetworkSend: return QColor(0, 188, 212);
        case MetricType::NetworkReceive: return QColor(0, 150, 136);
        case MetricType::BatteryPercent: return QColor(255, 235, 59);
        case MetricType::BatteryHealth: return QColor(205, 220, 57);
        default: return QColor(158, 158, 158);
    }
}

// ==================== ExportDialog ====================

ExportDialog::ExportDialog(MetricsHistory* history, QWidget* parent)
    : QDialog(parent)
    , m_history(history)
{
    setWindowTitle(tr("Export Metrics Data"));
    setMinimumSize(600, 500);
    
    setupUi();
}

void ExportDialog::setupUi()
{
    QVBoxLayout* mainLayout = new QVBoxLayout(this);
    
    // Time range
    QGroupBox* rangeGroup = new QGroupBox(tr("Time Range"));
    QHBoxLayout* rangeLayout = new QHBoxLayout(rangeGroup);
    
    m_startDateEdit = new QDateTimeEdit(QDateTime::currentDateTime().addDays(-1));
    m_startDateEdit->setDisplayFormat("dd/MM/yyyy HH:mm");
    m_startDateEdit->setCalendarPopup(true);
    
    m_endDateEdit = new QDateTimeEdit(QDateTime::currentDateTime());
    m_endDateEdit->setDisplayFormat("dd/MM/yyyy HH:mm");
    m_endDateEdit->setCalendarPopup(true);
    
    rangeLayout->addWidget(new QLabel(tr("From:")));
    rangeLayout->addWidget(m_startDateEdit);
    rangeLayout->addWidget(new QLabel(tr("To:")));
    rangeLayout->addWidget(m_endDateEdit);
    rangeLayout->addStretch();
    
    mainLayout->addWidget(rangeGroup);
    
    // Metrics selection
    QGroupBox* metricsGroup = new QGroupBox(tr("Metrics to Export"));
    QGridLayout* metricsLayout = new QGridLayout(metricsGroup);
    
    int row = 0, col = 0;
    auto addCheck = [this, metricsLayout, &row, &col](MetricType type, const QString& name) {
        QCheckBox* check = new QCheckBox(name);
        check->setChecked(true);
        metricsLayout->addWidget(check, row, col);
        m_metricChecks[type] = check;
        if (++col > 2) { col = 0; row++; }
    };
    
    addCheck(MetricType::CpuUsage, tr("CPU Usage"));
    addCheck(MetricType::CpuTemperature, tr("CPU Temp"));
    addCheck(MetricType::MemoryUsed, tr("Memory Used"));
    addCheck(MetricType::GpuUsage, tr("GPU Usage"));
    addCheck(MetricType::GpuTemperature, tr("GPU Temp"));
    addCheck(MetricType::DiskRead, tr("Disk Read"));
    addCheck(MetricType::DiskWrite, tr("Disk Write"));
    addCheck(MetricType::NetworkSend, tr("Network Send"));
    addCheck(MetricType::NetworkReceive, tr("Network Recv"));
    
    mainLayout->addWidget(metricsGroup);
    
    // Output settings
    QGroupBox* outputGroup = new QGroupBox(tr("Output"));
    QFormLayout* outputLayout = new QFormLayout(outputGroup);
    
    m_formatCombo = new QComboBox();
    m_formatCombo->addItem("CSV", static_cast<int>(ExportFormat::CSV));
    m_formatCombo->addItem("JSON", static_cast<int>(ExportFormat::JSON));
    outputLayout->addRow(tr("Format:"), m_formatCombo);
    
    QHBoxLayout* pathLayout = new QHBoxLayout();
    m_pathEdit = new QLineEdit();
    m_browseButton = new QPushButton(tr("Browse..."));
    connect(m_browseButton, &QPushButton::clicked, this, &ExportDialog::onBrowseClicked);
    pathLayout->addWidget(m_pathEdit);
    pathLayout->addWidget(m_browseButton);
    outputLayout->addRow(tr("File:"), pathLayout);
    
    mainLayout->addWidget(outputGroup);
    
    // Info
    m_infoLabel = new QLabel();
    mainLayout->addWidget(m_infoLabel);
    
    // Buttons
    QHBoxLayout* buttonLayout = new QHBoxLayout();
    buttonLayout->addStretch();
    
    QPushButton* exportButton = new QPushButton(tr("Export"));
    connect(exportButton, &QPushButton::clicked, this, &ExportDialog::onExportClicked);
    buttonLayout->addWidget(exportButton);
    
    QPushButton* cancelButton = new QPushButton(tr("Cancel"));
    connect(cancelButton, &QPushButton::clicked, this, &QDialog::reject);
    buttonLayout->addWidget(cancelButton);
    
    mainLayout->addLayout(buttonLayout);
    
    updatePreview();
}

void ExportDialog::setTimeRange(const QDateTime& start, const QDateTime& end)
{
    m_startDateEdit->setDateTime(start);
    m_endDateEdit->setDateTime(end);
    updatePreview();
}

void ExportDialog::setSelectedMetrics(const std::vector<MetricType>& metrics)
{
    for (auto& [type, check] : m_metricChecks) {
        bool selected = std::find(metrics.begin(), metrics.end(), type) != metrics.end();
        check->setChecked(selected);
    }
}

void ExportDialog::onBrowseClicked()
{
    QString filter = m_formatCombo->currentIndex() == 0 
        ? tr("CSV Files (*.csv)") 
        : tr("JSON Files (*.json)");
    
    QString path = QFileDialog::getSaveFileName(this, tr("Export To"), QString(), filter);
    if (!path.isEmpty()) {
        m_pathEdit->setText(path);
    }
}

void ExportDialog::onExportClicked()
{
    if (m_pathEdit->text().isEmpty()) {
        QMessageBox::warning(this, tr("Export"), tr("Please specify an output file."));
        return;
    }
    
    std::vector<MetricType> types;
    for (const auto& [type, check] : m_metricChecks) {
        if (check->isChecked()) {
            types.push_back(type);
        }
    }
    
    if (types.empty()) {
        QMessageBox::warning(this, tr("Export"), tr("Please select at least one metric."));
        return;
    }
    
    ExportFormat format = static_cast<ExportFormat>(m_formatCombo->currentData().toInt());
    
    bool success = m_history->exportData(
        m_pathEdit->text(),
        format,
        m_startDateEdit->dateTime(),
        m_endDateEdit->dateTime(),
        types
    );
    
    if (success) {
        QMessageBox::information(this, tr("Export"), tr("Data exported successfully!"));
        accept();
    } else {
        QMessageBox::critical(this, tr("Export"), tr("Failed to export data."));
    }
}

void ExportDialog::updatePreview()
{
    // Calculate estimated data size
    if (!m_history) return;
    
    qint64 totalRecords = m_history->totalRecordCount();
    qint64 dbSize = m_history->databaseSize();
    
    m_infoLabel->setText(QString(tr("Database: %1 records, %2 KB"))
        .arg(totalRecords)
        .arg(dbSize / 1024));
}
