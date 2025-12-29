#include "processimpactdialog.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QHeaderView>
#include <QFileDialog>
#include <QMessageBox>
#include <QSettings>
#include <QShowEvent>
#include <QCloseEvent>
#include <QScrollArea>
#include <QGroupBox>
#include <QtCharts/QChartView>
#include <QtCharts/QBarSeries>
#include <QtCharts/QBarSet>
#include <QtCharts/QHorizontalBarSeries>
#include <QtCharts/QBarCategoryAxis>
#include <QtCharts/QValueAxis>

// ============================================================================
// ProcessImpactTableModel Implementation
// ============================================================================

ProcessImpactTableModel::ProcessImpactTableModel(QObject* parent)
    : QAbstractTableModel(parent)
{
}

void ProcessImpactTableModel::setImpacts(const std::vector<ProcessImpact>& impacts)
{
    beginResetModel();
    m_impacts = impacts;
    endResetModel();
}

void ProcessImpactTableModel::updateImpacts(const std::vector<ProcessImpact>& impacts)
{
    // Update existing and add new
    if (m_impacts.size() != impacts.size()) {
        setImpacts(impacts);
        return;
    }
    
    if (impacts.empty()) {
        return;
    }
    
    m_impacts = impacts;
    emit dataChanged(index(0, 0), index(rowCount() - 1, columnCount() - 1));
}

const ProcessImpact* ProcessImpactTableModel::getImpact(int row) const
{
    if (row < 0 || row >= static_cast<int>(m_impacts.size())) {
        return nullptr;
    }
    return &m_impacts[row];
}

const ProcessImpact* ProcessImpactTableModel::getImpactByPid(quint32 pid) const
{
    for (const auto& impact : m_impacts) {
        if (impact.pid == pid) return &impact;
    }
    return nullptr;
}

int ProcessImpactTableModel::rowCount(const QModelIndex& parent) const
{
    if (parent.isValid()) return 0;
    return static_cast<int>(m_impacts.size());
}

int ProcessImpactTableModel::columnCount(const QModelIndex& parent) const
{
    if (parent.isValid()) return 0;
    return ColCount;
}

QVariant ProcessImpactTableModel::data(const QModelIndex& index, int role) const
{
    if (!index.isValid() || index.row() >= static_cast<int>(m_impacts.size())) {
        return QVariant();
    }
    
    const auto& impact = m_impacts[index.row()];
    
    if (role == Qt::DisplayRole) {
        switch (index.column()) {
            case ColName:
                return impact.displayName.isEmpty() ? impact.name : impact.displayName;
            case ColPID:
                return static_cast<quint32>(impact.pid);
            case ColBatteryScore:
                return QString::number(impact.batteryImpactScore, 'f', 1);
            case ColCpuAvg:
                return QString::number(impact.avgCpuPercent, 'f', 1) + "%";
            case ColCpuPeak:
                return QString::number(impact.peakCpuPercent, 'f', 1) + "%";
            case ColMemoryAvg:
                return ProcessImpactMonitor::formatBytes(impact.avgMemoryBytes);
            case ColDiskTotal:
                return ProcessImpactMonitor::formatBytes(impact.totalReadBytes + impact.totalWriteBytes);
            case ColDiskRead:
                return ProcessImpactMonitor::formatBytes(impact.totalReadBytes);
            case ColDiskWrite:
                return ProcessImpactMonitor::formatBytes(impact.totalWriteBytes);
            case ColActivity:
                return QString::number(impact.activityPercent, 'f', 0) + "%";
            case ColWakeCount:
                return impact.wakeCount;
            case ColOverallScore:
                return QString::number(impact.overallImpactScore, 'f', 1);
            default:
                return QVariant();
        }
    }
    
    if (role == Qt::DecorationRole && index.column() == ColName) {
        if (!impact.icon.isNull()) {
            return impact.icon;
        }
        return QVariant();  // Return empty variant if no icon
    }
    
    if (role == Qt::TextAlignmentRole) {
        if (index.column() == ColName) {
            return static_cast<int>(Qt::AlignLeft | Qt::AlignVCenter);
        }
        return static_cast<int>(Qt::AlignRight | Qt::AlignVCenter);
    }
    
    if (role == Qt::ToolTipRole && index.column() == ColName) {
        return impact.executablePath;
    }
    
    // For sorting
    if (role == Qt::UserRole) {
        switch (index.column()) {
            case ColBatteryScore: return impact.batteryImpactScore;
            case ColCpuAvg: return impact.avgCpuPercent;
            case ColCpuPeak: return impact.peakCpuPercent;
            case ColMemoryAvg: return impact.avgMemoryBytes;
            case ColDiskTotal: return impact.totalReadBytes + impact.totalWriteBytes;
            case ColDiskRead: return impact.totalReadBytes;
            case ColDiskWrite: return impact.totalWriteBytes;
            case ColActivity: return impact.activityPercent;
            case ColWakeCount: return impact.wakeCount;
            case ColOverallScore: return impact.overallImpactScore;
            default: return QVariant();
        }
    }
    
    // Store isSystem flag
    if (role == Qt::UserRole + 1) {
        return impact.isSystemProcess;
    }
    
    return QVariant();
}

QVariant ProcessImpactTableModel::headerData(int section, Qt::Orientation orientation, int role) const
{
    if (orientation != Qt::Horizontal || role != Qt::DisplayRole) {
        return QVariant();
    }
    
    switch (section) {
        case ColName: return tr("Process");
        case ColPID: return tr("PID");
        case ColBatteryScore: return tr("Battery");
        case ColCpuAvg: return tr("CPU Avg");
        case ColCpuPeak: return tr("CPU Peak");
        case ColMemoryAvg: return tr("Memory");
        case ColDiskTotal: return tr("Disk Total");
        case ColDiskRead: return tr("Disk Read");
        case ColDiskWrite: return tr("Disk Write");
        case ColActivity: return tr("Activity");
        case ColWakeCount: return tr("Wakes");
        case ColOverallScore: return tr("Overall");
        default: return QVariant();
    }
}

// ============================================================================
// ProcessImpactSortFilterProxy Implementation
// ============================================================================

ProcessImpactSortFilterProxy::ProcessImpactSortFilterProxy(QObject* parent)
    : QSortFilterProxyModel(parent)
{
    setFilterCaseSensitivity(Qt::CaseInsensitive);
}

void ProcessImpactSortFilterProxy::setShowSystemProcesses(bool show)
{
    m_showSystem = show;
    invalidateFilter();
}

void ProcessImpactSortFilterProxy::setMinimumImpact(double minScore)
{
    m_minImpact = minScore;
    invalidateFilter();
}

bool ProcessImpactSortFilterProxy::lessThan(const QModelIndex& left, const QModelIndex& right) const
{
    // Use UserRole for numeric sorting
    QVariant leftData = sourceModel()->data(left, Qt::UserRole);
    QVariant rightData = sourceModel()->data(right, Qt::UserRole);
    
    if (leftData.isValid() && rightData.isValid()) {
        if (leftData.typeId() == QMetaType::Double) {
            return leftData.toDouble() < rightData.toDouble();
        }
        if (leftData.typeId() == QMetaType::LongLong) {
            return leftData.toLongLong() < rightData.toLongLong();
        }
        if (leftData.typeId() == QMetaType::Int) {
            return leftData.toInt() < rightData.toInt();
        }
    }
    
    return QSortFilterProxyModel::lessThan(left, right);
}

bool ProcessImpactSortFilterProxy::filterAcceptsRow(int sourceRow, const QModelIndex& sourceParent) const
{
    // Check system process filter
    if (!m_showSystem) {
        QModelIndex idx = sourceModel()->index(sourceRow, 0, sourceParent);
        bool isSystem = sourceModel()->data(idx, Qt::UserRole + 1).toBool();
        if (isSystem) return false;
    }
    
    // Check text filter
    if (!filterRegularExpression().pattern().isEmpty()) {
        QModelIndex nameIdx = sourceModel()->index(sourceRow, ProcessImpactTableModel::ColName, sourceParent);
        QString name = sourceModel()->data(nameIdx).toString();
        if (!name.contains(filterRegularExpression())) {
            return false;
        }
    }
    
    return true;
}

// ============================================================================
// ProcessImpactDetailPanel Implementation
// ============================================================================

ProcessImpactDetailPanel::ProcessImpactDetailPanel(QWidget* parent)
    : QWidget(parent)
{
    setupUi();
}

void ProcessImpactDetailPanel::setupUi()
{
    auto* mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(16, 16, 16, 16);
    mainLayout->setSpacing(16);
    
    // Header with icon and name
    auto* headerWidget = new QWidget(this);
    auto* headerLayout = new QHBoxLayout(headerWidget);
    headerLayout->setContentsMargins(0, 0, 0, 0);
    headerLayout->setSpacing(12);
    
    m_iconLabel = new QLabel(this);
    m_iconLabel->setFixedSize(48, 48);
    m_iconLabel->setAlignment(Qt::AlignCenter);
    m_iconLabel->setStyleSheet("background: #2a2a2a; border-radius: 8px;");
    headerLayout->addWidget(m_iconLabel);
    
    auto* nameLayout = new QVBoxLayout();
    nameLayout->setSpacing(2);
    
    m_nameLabel = new QLabel(this);
    m_nameLabel->setStyleSheet("font-size: 16px; font-weight: 600; color: #fff;");
    nameLayout->addWidget(m_nameLabel);
    
    m_pidLabel = new QLabel(this);
    m_pidLabel->setStyleSheet("color: #888; font-size: 12px;");
    nameLayout->addWidget(m_pidLabel);
    
    m_pathLabel = new QLabel(this);
    m_pathLabel->setStyleSheet("color: #666; font-size: 11px;");
    m_pathLabel->setWordWrap(true);
    nameLayout->addWidget(m_pathLabel);
    
    headerLayout->addLayout(nameLayout, 1);
    mainLayout->addWidget(headerWidget);
    
    // Scores summary
    m_scoresWidget = new QWidget(this);
    auto* scoresLayout = new QHBoxLayout(m_scoresWidget);
    scoresLayout->setSpacing(16);
    
    auto createScoreBox = [this](const QString& title, QLabel*& label) {
        auto* box = new QFrame(this);
        box->setStyleSheet(R"(
            QFrame {
                background: #252525;
                border-radius: 8px;
                padding: 8px;
            }
        )");
        auto* layout = new QVBoxLayout(box);
        layout->setSpacing(4);
        
        auto* titleLabel = new QLabel(title, box);
        titleLabel->setStyleSheet("color: #888; font-size: 11px;");
        titleLabel->setAlignment(Qt::AlignCenter);
        layout->addWidget(titleLabel);
        
        label = new QLabel("--", box);
        label->setStyleSheet("font-size: 20px; font-weight: 600; color: #fff;");
        label->setAlignment(Qt::AlignCenter);
        layout->addWidget(label);
        
        return box;
    };
    
    scoresLayout->addWidget(createScoreBox("Battery Impact", m_batteryScoreLabel));
    scoresLayout->addWidget(createScoreBox("Disk Impact", m_diskScoreLabel));
    scoresLayout->addWidget(createScoreBox("Overall Impact", m_overallScoreLabel));
    
    mainLayout->addWidget(m_scoresWidget);
    
    // Metrics tabs
    m_metricsTab = new QTabWidget(this);
    m_metricsTab->setStyleSheet(R"(
        QTabWidget::pane {
            background: #1e1e1e;
            border: 1px solid #333;
            border-radius: 4px;
        }
        QTabBar::tab {
            background: #252525;
            color: #888;
            padding: 8px 16px;
            border-top-left-radius: 4px;
            border-top-right-radius: 4px;
        }
        QTabBar::tab:selected {
            background: #1e1e1e;
            color: #fff;
        }
    )");
    
    // CPU Tab
    auto* cpuWidget = new QWidget();
    auto* cpuLayout = new QGridLayout(cpuWidget);
    cpuLayout->setSpacing(12);
    
    auto addMetricRow = [](QGridLayout* layout, int row, const QString& label, QLabel*& valueLabel) {
        auto* lbl = new QLabel(label);
        lbl->setStyleSheet("color: #888;");
        layout->addWidget(lbl, row, 0);
        
        valueLabel = new QLabel("--");
        valueLabel->setStyleSheet("color: #fff; font-weight: 500;");
        valueLabel->setAlignment(Qt::AlignRight);
        layout->addWidget(valueLabel, row, 1);
    };
    
    addMetricRow(cpuLayout, 0, "Average Usage:", m_cpuAvgLabel);
    addMetricRow(cpuLayout, 1, "Peak Usage:", m_cpuPeakLabel);
    addMetricRow(cpuLayout, 2, "Total CPU Time:", m_cpuTimeLabel);
    addMetricRow(cpuLayout, 3, "High CPU Spikes:", m_cpuSpikesLabel);
    cpuLayout->setRowStretch(4, 1);
    
    m_metricsTab->addTab(cpuWidget, "💻 CPU");
    
    // Memory Tab
    auto* memWidget = new QWidget();
    auto* memLayout = new QGridLayout(memWidget);
    memLayout->setSpacing(12);
    
    addMetricRow(memLayout, 0, "Average Memory:", m_memAvgLabel);
    addMetricRow(memLayout, 1, "Peak Memory:", m_memPeakLabel);
    addMetricRow(memLayout, 2, "Memory Growth:", m_memGrowthLabel);
    memLayout->setRowStretch(3, 1);
    
    m_metricsTab->addTab(memWidget, "🧠 Memory");
    
    // Disk Tab
    auto* diskWidget = new QWidget();
    auto* diskLayout = new QGridLayout(diskWidget);
    diskLayout->setSpacing(12);
    
    addMetricRow(diskLayout, 0, "Total Read:", m_diskReadLabel);
    addMetricRow(diskLayout, 1, "Total Write:", m_diskWriteLabel);
    addMetricRow(diskLayout, 2, "Avg Read Rate:", m_diskReadRateLabel);
    addMetricRow(diskLayout, 3, "Avg Write Rate:", m_diskWriteRateLabel);
    addMetricRow(diskLayout, 4, "Peak Read Rate:", m_diskPeakReadLabel);
    addMetricRow(diskLayout, 5, "Peak Write Rate:", m_diskPeakWriteLabel);
    diskLayout->setRowStretch(6, 1);
    
    m_metricsTab->addTab(diskWidget, "💾 Disk");
    
    // Activity Tab
    auto* activityWidget = new QWidget();
    auto* activityLayout = new QGridLayout(activityWidget);
    activityLayout->setSpacing(12);
    
    addMetricRow(activityLayout, 0, "Activity %:", m_activityLabel);
    addMetricRow(activityLayout, 1, "Wake Count:", m_wakeCountLabel);
    addMetricRow(activityLayout, 2, "Active Seconds:", m_activeSecsLabel);
    activityLayout->setRowStretch(3, 1);
    
    m_metricsTab->addTab(activityWidget, "📊 Activity");
    
    mainLayout->addWidget(m_metricsTab, 1);
}

void ProcessImpactDetailPanel::setImpact(const ProcessImpact& impact)
{
    m_impact = impact;
    
    // Header
    if (!impact.icon.isNull()) {
        m_iconLabel->setPixmap(impact.icon.pixmap(40, 40));
    } else {
        m_iconLabel->setText("📦");
        m_iconLabel->setStyleSheet("background: #2a2a2a; border-radius: 8px; font-size: 24px;");
    }
    
    m_nameLabel->setText(impact.displayName.isEmpty() ? impact.name : impact.displayName);
    m_pidLabel->setText(QString("PID: %1").arg(impact.pid));
    m_pathLabel->setText(impact.executablePath);
    
    // Scores
    auto setScoreColor = [](QLabel* label, double score) {
        QString color;
        if (score < 20) color = "#4caf50";      // Green
        else if (score < 40) color = "#8bc34a"; // Light green
        else if (score < 60) color = "#ffc107"; // Amber
        else if (score < 80) color = "#ff9800"; // Orange
        else color = "#f44336";                  // Red
        
        label->setStyleSheet(QString("font-size: 20px; font-weight: 600; color: %1;").arg(color));
    };
    
    m_batteryScoreLabel->setText(QString::number(impact.batteryImpactScore, 'f', 0));
    setScoreColor(m_batteryScoreLabel, impact.batteryImpactScore);
    
    m_diskScoreLabel->setText(QString::number(impact.diskImpactScore, 'f', 0));
    setScoreColor(m_diskScoreLabel, impact.diskImpactScore);
    
    m_overallScoreLabel->setText(QString::number(impact.overallImpactScore, 'f', 0));
    setScoreColor(m_overallScoreLabel, impact.overallImpactScore);
    
    // CPU metrics
    m_cpuAvgLabel->setText(QString("%1%").arg(impact.avgCpuPercent, 0, 'f', 2));
    m_cpuPeakLabel->setText(QString("%1%").arg(impact.peakCpuPercent, 0, 'f', 1));
    m_cpuTimeLabel->setText(QString("%1 sec").arg(impact.totalCpuSeconds, 0, 'f', 1));
    m_cpuSpikesLabel->setText(QString::number(impact.cpuSpikeCount));
    
    // Memory metrics
    m_memAvgLabel->setText(ProcessImpactMonitor::formatBytes(impact.avgMemoryBytes));
    m_memPeakLabel->setText(ProcessImpactMonitor::formatBytes(impact.peakMemoryBytes));
    QString growthText = ProcessImpactMonitor::formatBytes(std::abs(impact.memoryGrowth));
    if (impact.memoryGrowth > 0) growthText = "+" + growthText;
    else if (impact.memoryGrowth < 0) growthText = "-" + growthText;
    m_memGrowthLabel->setText(growthText);
    
    // Disk metrics
    m_diskReadLabel->setText(ProcessImpactMonitor::formatBytes(impact.totalReadBytes));
    m_diskWriteLabel->setText(ProcessImpactMonitor::formatBytes(impact.totalWriteBytes));
    m_diskReadRateLabel->setText(ProcessImpactMonitor::formatBytesPerSec(impact.avgReadBytesPerSec));
    m_diskWriteRateLabel->setText(ProcessImpactMonitor::formatBytesPerSec(impact.avgWriteBytesPerSec));
    m_diskPeakReadLabel->setText(ProcessImpactMonitor::formatBytesPerSec(impact.peakReadBytesPerSec));
    m_diskPeakWriteLabel->setText(ProcessImpactMonitor::formatBytesPerSec(impact.peakWriteBytesPerSec));
    
    // Activity metrics
    m_activityLabel->setText(QString("%1%").arg(impact.activityPercent, 0, 'f', 1));
    m_wakeCountLabel->setText(QString::number(impact.wakeCount));
    m_activeSecsLabel->setText(QString::number(impact.activeSeconds));
}

void ProcessImpactDetailPanel::clear()
{
    m_iconLabel->clear();
    m_iconLabel->setText("📦");
    m_nameLabel->setText("Select a process");
    m_pidLabel->clear();
    m_pathLabel->clear();
    
    m_batteryScoreLabel->setText("--");
    m_diskScoreLabel->setText("--");
    m_overallScoreLabel->setText("--");
    
    m_cpuAvgLabel->setText("--");
    m_cpuPeakLabel->setText("--");
    m_cpuTimeLabel->setText("--");
    m_cpuSpikesLabel->setText("--");
    
    m_memAvgLabel->setText("--");
    m_memPeakLabel->setText("--");
    m_memGrowthLabel->setText("--");
    
    m_diskReadLabel->setText("--");
    m_diskWriteLabel->setText("--");
    m_diskReadRateLabel->setText("--");
    m_diskWriteRateLabel->setText("--");
    m_diskPeakReadLabel->setText("--");
    m_diskPeakWriteLabel->setText("--");
    
    m_activityLabel->setText("--");
    m_wakeCountLabel->setText("--");
    m_activeSecsLabel->setText("--");
}

// ============================================================================
// ProcessComparisonChart Implementation
// ============================================================================

ProcessComparisonChart::ProcessComparisonChart(QWidget* parent)
    : QWidget(parent)
{
    setupUi();
}

void ProcessComparisonChart::setupUi()
{
    auto* layout = new QVBoxLayout(this);
    layout->setContentsMargins(0, 0, 0, 0);
    
    m_chartView = new QChartView(this);
    m_chartView->setRenderHint(QPainter::Antialiasing);
    m_chartView->setMinimumHeight(200);
    
    // Create an empty chart initially
    auto* chart = new QChart();
    chart->setBackgroundBrush(QColor(30, 30, 30));
    chart->legend()->setVisible(false);
    m_chartView->setChart(chart);
    
    layout->addWidget(m_chartView);
}

void ProcessComparisonChart::setImpacts(const std::vector<ProcessImpact>& impacts, ImpactCategory category)
{
    m_impacts = impacts;
    m_category = category;
    updateChart();
}

void ProcessComparisonChart::clear()
{
    m_impacts.clear();
    if (m_chartView && m_chartView->chart()) {
        m_chartView->chart()->removeAllSeries();
    }
}

void ProcessComparisonChart::updateChart()
{
    // Safety check - don't create chart if no data
    if (m_impacts.empty()) {
        if (m_chartView->chart()) {
            m_chartView->chart()->removeAllSeries();
        }
        return;
    }
    
    // Delete old chart before creating new one
    QChart* oldChart = m_chartView->chart();
    
    auto* chart = new QChart();
    chart->setBackgroundBrush(QColor(30, 30, 30));
    chart->setTitleBrush(QColor(255, 255, 255));
    chart->legend()->setVisible(false);
    
    auto* barSeries = new QHorizontalBarSeries();
    auto* barSet = new QBarSet("Impact");
    barSet->setColor(QColor(100, 181, 246));
    
    QStringList categories;
    
    int count = std::min(static_cast<int>(m_impacts.size()), 10);
    for (int i = count - 1; i >= 0; --i) {
        const auto& impact = m_impacts[i];
        double value = 0;
        
        switch (m_category) {
            case ImpactCategory::BatteryDrain:
                value = impact.batteryImpactScore;
                break;
            case ImpactCategory::CpuUsage:
                value = impact.avgCpuPercent;
                break;
            case ImpactCategory::DiskIO:
                value = impact.diskImpactScore;
                break;
            case ImpactCategory::MemoryUsage:
                value = std::min(100.0, static_cast<double>(impact.avgMemoryBytes) / (4.0 * 1024 * 1024 * 1024) * 100);
                break;
            default:
                value = impact.overallImpactScore;
                break;
        }
        
        *barSet << value;
        QString name = impact.displayName.isEmpty() ? impact.name : impact.displayName;
        if (name.length() > 20) name = name.left(18) + "...";
        categories << name;
    }
    
    barSeries->append(barSet);
    chart->addSeries(barSeries);
    
    auto* axisY = new QBarCategoryAxis();
    axisY->append(categories);
    axisY->setLabelsColor(Qt::white);
    chart->addAxis(axisY, Qt::AlignLeft);
    barSeries->attachAxis(axisY);
    
    auto* axisX = new QValueAxis();
    axisX->setRange(0, 100);
    axisX->setLabelsColor(Qt::white);
    axisX->setGridLineColor(QColor(60, 60, 60));
    chart->addAxis(axisX, Qt::AlignBottom);
    barSeries->attachAxis(axisX);
    
    m_chartView->setChart(chart);
    
    // Delete old chart after setting new one
    delete oldChart;
}

// ============================================================================
// ProcessImpactDialog Implementation
// ============================================================================

ProcessImpactDialog::ProcessImpactDialog(QWidget* parent)
    : QDialog(parent)
    , m_tableModel(std::make_unique<ProcessImpactTableModel>(this))
    , m_proxyModel(std::make_unique<ProcessImpactSortFilterProxy>(this))
{
    setWindowTitle(tr("Process Impact Analysis"));
    setMinimumSize(900, 600);
    resize(1200, 800);
    
    // Allow maximize and resize
    setWindowFlags(windowFlags() 
        | Qt::WindowMaximizeButtonHint 
        | Qt::WindowMinimizeButtonHint);
    setSizeGripEnabled(true);
    
    m_proxyModel->setSourceModel(m_tableModel.get());
    m_proxyModel->setSortRole(Qt::UserRole);
    
    setupUi();
    loadSettings();
    
    // Create monitor AFTER UI is set up
    m_monitor = std::make_unique<ProcessImpactMonitor>(this);
    
    // Use QueuedConnection to avoid issues with signal during data collection
    connect(m_monitor.get(), &ProcessImpactMonitor::impactsUpdated,
            this, &ProcessImpactDialog::onMonitorUpdated, Qt::QueuedConnection);
}

ProcessImpactDialog::~ProcessImpactDialog()
{
    if (m_monitor) {
        m_monitor->stop();
    }
    saveSettings();
}

void ProcessImpactDialog::setupUi()
{
    auto* mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(16, 16, 16, 16);
    mainLayout->setSpacing(12);
    
    // Toolbar
    auto* toolbarLayout = new QHBoxLayout();
    toolbarLayout->setSpacing(12);
    
    auto* categoryLabel = new QLabel(tr("Sort by:"), this);
    categoryLabel->setStyleSheet("color: #aaa;");
    toolbarLayout->addWidget(categoryLabel);
    
    m_categoryCombo = new QComboBox(this);
    m_categoryCombo->addItem("🔋 Battery Impact", static_cast<int>(ImpactCategory::BatteryDrain));
    m_categoryCombo->addItem("💻 CPU Usage", static_cast<int>(ImpactCategory::CpuUsage));
    m_categoryCombo->addItem("💾 Disk I/O", static_cast<int>(ImpactCategory::DiskIO));
    m_categoryCombo->addItem("🧠 Memory Usage", static_cast<int>(ImpactCategory::MemoryUsage));
    m_categoryCombo->addItem("📊 Overall Impact", static_cast<int>(ImpactCategory::OverallImpact));
    connect(m_categoryCombo, QOverload<int>::of(&QComboBox::currentIndexChanged),
            this, &ProcessImpactDialog::onCategoryChanged);
    toolbarLayout->addWidget(m_categoryCombo);
    
    toolbarLayout->addSpacing(20);
    
    m_filterEdit = new QLineEdit(this);
    m_filterEdit->setPlaceholderText(tr("Filter processes..."));
    m_filterEdit->setClearButtonEnabled(true);
    m_filterEdit->setMinimumWidth(200);
    connect(m_filterEdit, &QLineEdit::textChanged, this, &ProcessImpactDialog::onFilterChanged);
    toolbarLayout->addWidget(m_filterEdit);
    
    m_showSystemCheck = new QCheckBox(tr("Show system processes"), this);
    connect(m_showSystemCheck, &QCheckBox::stateChanged, this, &ProcessImpactDialog::onShowSystemChanged);
    toolbarLayout->addWidget(m_showSystemCheck);
    
    toolbarLayout->addStretch();
    
    m_refreshButton = new QPushButton(tr("🔄 Refresh"), this);
    connect(m_refreshButton, &QPushButton::clicked, this, &ProcessImpactDialog::refresh);
    toolbarLayout->addWidget(m_refreshButton);
    
    m_exportButton = new QPushButton(tr("📥 Export"), this);
    connect(m_exportButton, &QPushButton::clicked, this, &ProcessImpactDialog::onExportClicked);
    toolbarLayout->addWidget(m_exportButton);
    
    mainLayout->addLayout(toolbarLayout);
    
    // Splitter for table and detail
    m_splitter = new QSplitter(Qt::Horizontal, this);
    
    // Left: Table
    auto* tableWidget = new QWidget();
    auto* tableLayout = new QVBoxLayout(tableWidget);
    tableLayout->setContentsMargins(0, 0, 0, 0);
    
    m_tableView = new QTableView(this);
    m_tableView->setModel(m_proxyModel.get());
    m_tableView->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_tableView->setSelectionMode(QAbstractItemView::SingleSelection);
    m_tableView->setSortingEnabled(true);
    m_tableView->setAlternatingRowColors(true);
    m_tableView->horizontalHeader()->setStretchLastSection(true);
    m_tableView->verticalHeader()->setVisible(false);
    m_tableView->setStyleSheet(R"(
        QTableView {
            background-color: #1e1e1e;
            alternate-background-color: #252525;
            gridline-color: #333;
            color: #e0e0e0;
            selection-background-color: #3a3a3a;
        }
        QHeaderView::section {
            background-color: #2a2a2a;
            color: #aaa;
            padding: 6px;
            border: none;
            border-bottom: 1px solid #333;
        }
    )");
    
    // Set column widths
    m_tableView->setColumnWidth(ProcessImpactTableModel::ColName, 200);
    m_tableView->setColumnWidth(ProcessImpactTableModel::ColPID, 60);
    m_tableView->setColumnWidth(ProcessImpactTableModel::ColBatteryScore, 70);
    m_tableView->setColumnWidth(ProcessImpactTableModel::ColCpuAvg, 70);
    m_tableView->setColumnWidth(ProcessImpactTableModel::ColMemoryAvg, 80);
    
    // Allow columns to stretch
    m_tableView->horizontalHeader()->setSectionResizeMode(ProcessImpactTableModel::ColName, QHeaderView::Stretch);
    
    connect(m_tableView->selectionModel(), &QItemSelectionModel::selectionChanged,
            this, &ProcessImpactDialog::onTableSelectionChanged);
    
    tableLayout->addWidget(m_tableView, 1);  // Give table more stretch
    
    // Comparison chart below table
    m_comparisonChart = new ProcessComparisonChart(this);
    m_comparisonChart->setMinimumHeight(150);
    m_comparisonChart->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Expanding);
    tableLayout->addWidget(m_comparisonChart);
    
    m_splitter->addWidget(tableWidget);
    
    // Right: Detail panel in scroll area
    auto* scrollArea = new QScrollArea(this);
    scrollArea->setWidgetResizable(true);
    scrollArea->setFrameShape(QFrame::NoFrame);
    scrollArea->setHorizontalScrollBarPolicy(Qt::ScrollBarAsNeeded);
    scrollArea->setVerticalScrollBarPolicy(Qt::ScrollBarAsNeeded);
    scrollArea->setStyleSheet("QScrollArea { background: transparent; }");
    
    m_detailPanel = new ProcessImpactDetailPanel(this);
    m_detailPanel->setMinimumWidth(280);
    scrollArea->setWidget(m_detailPanel);
    
    m_splitter->addWidget(scrollArea);
    
    m_splitter->setStretchFactor(0, 3);
    m_splitter->setStretchFactor(1, 1);
    m_splitter->setChildrenCollapsible(false);
    
    mainLayout->addWidget(m_splitter, 1);
    
    // Status bar
    m_statusLabel = new QLabel(this);
    m_statusLabel->setStyleSheet("color: #888;");
    mainLayout->addWidget(m_statusLabel);
    
    // Initial sort
    m_tableView->sortByColumn(ProcessImpactTableModel::ColBatteryScore, Qt::DescendingOrder);
}

void ProcessImpactDialog::refresh()
{
    if (!m_monitor) return;
    
    // Only recalculate if we have data
    if (m_monitor->totalSamples() > 0) {
        m_monitor->recalculateImpacts();
    }
    updateTable();
}

void ProcessImpactDialog::setCategory(ImpactCategory category)
{
    m_currentCategory = category;
    
    // Find and select in combo
    for (int i = 0; i < m_categoryCombo->count(); ++i) {
        if (m_categoryCombo->itemData(i).toInt() == static_cast<int>(category)) {
            m_categoryCombo->setCurrentIndex(i);
            break;
        }
    }
}

void ProcessImpactDialog::showEvent(QShowEvent* event)
{
    QDialog::showEvent(event);
    
    // Start monitoring when dialog is shown
    if (m_monitor && !m_monitor->isRunning()) {
        m_monitor->start(2000);  // 2 second interval to reduce CPU load
    }
    
    refresh();
}

void ProcessImpactDialog::closeEvent(QCloseEvent* event)
{
    saveSettings();
    QDialog::closeEvent(event);
}

void ProcessImpactDialog::onMonitorUpdated()
{
    // Safety check - only update if dialog is visible
    if (!isVisible()) {
        return;
    }
    
    updateTable();
}

void ProcessImpactDialog::onCategoryChanged(int index)
{
    m_currentCategory = static_cast<ImpactCategory>(m_categoryCombo->itemData(index).toInt());
    
    // Update sort column
    int sortColumn = ProcessImpactTableModel::ColBatteryScore;
    switch (m_currentCategory) {
        case ImpactCategory::CpuUsage:
            sortColumn = ProcessImpactTableModel::ColCpuAvg;
            break;
        case ImpactCategory::DiskIO:
            sortColumn = ProcessImpactTableModel::ColDiskTotal;
            break;
        case ImpactCategory::MemoryUsage:
            sortColumn = ProcessImpactTableModel::ColMemoryAvg;
            break;
        default:
            sortColumn = ProcessImpactTableModel::ColBatteryScore;
            break;
    }
    
    m_tableView->sortByColumn(sortColumn, Qt::DescendingOrder);
    updateComparisonChart();
}

void ProcessImpactDialog::onFilterChanged(const QString& text)
{
    m_proxyModel->setFilterRegularExpression(text);
}

void ProcessImpactDialog::onShowSystemChanged(int state)
{
    m_proxyModel->setShowSystemProcesses(state == Qt::Checked);
    updateComparisonChart();
}

void ProcessImpactDialog::onTableSelectionChanged()
{
    updateDetailPanel();
}

void ProcessImpactDialog::onExportClicked()
{
    QString fileName = QFileDialog::getSaveFileName(
        this,
        tr("Export Process Impact Data"),
        QString(),
        tr("CSV Files (*.csv);;All Files (*)")
    );
    
    if (fileName.isEmpty()) return;
    
    QFile file(fileName);
    if (!file.open(QIODevice::WriteOnly | QIODevice::Text)) {
        QMessageBox::warning(this, tr("Export Error"),
                            tr("Could not open file for writing."));
        return;
    }
    
    QTextStream out(&file);
    
    // Header
    out << "Process,PID,Battery Score,CPU Avg %,CPU Peak %,Memory,Disk Read,Disk Write,"
        << "Activity %,Wake Count,Overall Score\n";
    
    // Data
    auto impacts = m_monitor->getAllImpacts(m_showSystemCheck->isChecked());
    for (const auto& imp : impacts) {
        out << "\"" << imp.name << "\","
            << imp.pid << ","
            << imp.batteryImpactScore << ","
            << imp.avgCpuPercent << ","
            << imp.peakCpuPercent << ","
            << imp.avgMemoryBytes << ","
            << imp.totalReadBytes << ","
            << imp.totalWriteBytes << ","
            << imp.activityPercent << ","
            << imp.wakeCount << ","
            << imp.overallImpactScore << "\n";
    }
    
    file.close();
    
    QMessageBox::information(this, tr("Export Complete"),
                            tr("Data exported successfully to %1").arg(fileName));
}

void ProcessImpactDialog::updateTable()
{
    if (!m_monitor || !m_tableModel || !m_showSystemCheck || !m_statusLabel) return;
    
    auto impacts = m_monitor->getImpactsSorted(m_currentCategory, false,
                                               m_showSystemCheck->isChecked());
    
    if (impacts.empty()) {
        m_statusLabel->setText(tr("Collecting data..."));
        return;
    }
    
    m_tableModel->updateImpacts(impacts);
    
    m_statusLabel->setText(tr("%1 processes analyzed").arg(impacts.size()));
    
    updateComparisonChart();
}

void ProcessImpactDialog::updateDetailPanel()
{
    if (!m_tableView || !m_tableView->selectionModel()) {
        return;
    }
    
    auto selection = m_tableView->selectionModel()->selectedRows();
    if (selection.isEmpty()) {
        m_detailPanel->clear();
        return;
    }
    
    QModelIndex proxyIndex = selection.first();
    QModelIndex sourceIndex = m_proxyModel->mapToSource(proxyIndex);
    
    const ProcessImpact* impact = m_tableModel->getImpact(sourceIndex.row());
    if (impact) {
        m_detailPanel->setImpact(*impact);
    }
}

void ProcessImpactDialog::updateComparisonChart()
{
    if (!m_monitor || !m_comparisonChart || !m_showSystemCheck) return;
    
    auto impacts = m_monitor->getTopProcesses(m_currentCategory, 10,
                                              m_showSystemCheck->isChecked());
    m_comparisonChart->setImpacts(impacts, m_currentCategory);
}

void ProcessImpactDialog::saveSettings()
{
    QSettings settings;
    settings.beginGroup("ProcessImpactDialog");
    settings.setValue("geometry", saveGeometry());
    settings.setValue("splitterState", m_splitter->saveState());
    settings.setValue("category", static_cast<int>(m_currentCategory));
    settings.setValue("showSystem", m_showSystemCheck->isChecked());
    settings.endGroup();
}

void ProcessImpactDialog::loadSettings()
{
    QSettings settings;
    settings.beginGroup("ProcessImpactDialog");
    
    if (settings.contains("geometry")) {
        restoreGeometry(settings.value("geometry").toByteArray());
    }
    if (settings.contains("splitterState")) {
        m_splitter->restoreState(settings.value("splitterState").toByteArray());
    }
    if (settings.contains("category")) {
        setCategory(static_cast<ImpactCategory>(settings.value("category").toInt()));
    }
    if (settings.contains("showSystem")) {
        m_showSystemCheck->setChecked(settings.value("showSystem").toBool());
    }
    
    settings.endGroup();
}
