#include "historychartwidget.h"

#include <QGraphicsTextItem>
#include <QMouseEvent>
#include <QWheelEvent>
#include <QHBoxLayout>
#include <QVBoxLayout>
#include <QPushButton>
#include <QButtonGroup>
#include <QDateTimeEdit>
#include <QLabel>
#include <QProgressBar>
#include <QApplication>
#include <QScreen>
#include <QImageWriter>
#include <cmath>

// ==================== InteractiveChartWidget ====================

InteractiveChartWidget::InteractiveChartWidget(QWidget *parent)
    : QChartView(parent)
{
    setupChart();
    createContextMenu();
    
    // Enable mouse tracking for tooltips
    setMouseTracking(true);
    
    // Rubber band for selection
    m_rubberBand = new QRubberBand(QRubberBand::Rectangle, this);
    
    // Anti-aliasing for smoother lines
    setRenderHint(QPainter::Antialiasing);
}

InteractiveChartWidget::~InteractiveChartWidget() = default;

void InteractiveChartWidget::setupChart()
{
    m_chart = new QChart();
    m_chart->setAnimationOptions(QChart::SeriesAnimations);
    m_chart->legend()->setVisible(true);
    m_chart->legend()->setAlignment(Qt::AlignBottom);
    
    // Dark theme styling
    m_chart->setBackgroundBrush(QBrush(QColor(30, 30, 30)));
    m_chart->setTitleBrush(QBrush(Qt::white));
    m_chart->legend()->setLabelColor(Qt::white);
    
    setChart(m_chart);
    setupAxes();
}

void InteractiveChartWidget::setupAxes()
{
    // Time axis (X)
    m_axisX = new QDateTimeAxis();
    m_axisX->setFormat("dd/MM hh:mm");
    m_axisX->setTitleText("Time");
    m_axisX->setLabelsColor(Qt::white);
    m_axisX->setTitleBrush(QBrush(Qt::white));
    m_axisX->setGridLineColor(QColor(60, 60, 60));
    m_chart->addAxis(m_axisX, Qt::AlignBottom);
    
    // Value axis (Y)
    m_axisY = new QValueAxis();
    m_axisY->setTitleText("Value");
    m_axisY->setLabelsColor(Qt::white);
    m_axisY->setTitleBrush(QBrush(Qt::white));
    m_axisY->setGridLineColor(QColor(60, 60, 60));
    m_axisY->setRange(0, 100);
    m_chart->addAxis(m_axisY, Qt::AlignLeft);
}

void InteractiveChartWidget::createContextMenu()
{
    m_contextMenu = new QMenu(this);
    
    QAction* zoomInAction = m_contextMenu->addAction("Zoom In");
    connect(zoomInAction, &QAction::triggered, this, &InteractiveChartWidget::zoomIn);
    
    QAction* zoomOutAction = m_contextMenu->addAction("Zoom Out");
    connect(zoomOutAction, &QAction::triggered, this, &InteractiveChartWidget::zoomOut);
    
    QAction* resetZoomAction = m_contextMenu->addAction("Reset Zoom");
    connect(resetZoomAction, &QAction::triggered, this, &InteractiveChartWidget::resetZoom);
    
    m_contextMenu->addSeparator();
    
    QMenu* displayModeMenu = m_contextMenu->addMenu("Display Mode");
    
    QAction* lineAction = displayModeMenu->addAction("Line");
    connect(lineAction, &QAction::triggered, [this]() { setDisplayMode(ChartDisplayMode::Line); });
    
    QAction* areaAction = displayModeMenu->addAction("Area");
    connect(areaAction, &QAction::triggered, [this]() { setDisplayMode(ChartDisplayMode::Area); });
    
    QAction* minMaxAction = displayModeMenu->addAction("Min/Max/Avg");
    connect(minMaxAction, &QAction::triggered, [this]() { setDisplayMode(ChartDisplayMode::MinMaxAvg); });
    
    m_contextMenu->addSeparator();
    
    QAction* exportAction = m_contextMenu->addAction("Export as Image...");
    connect(exportAction, &QAction::triggered, [this]() {
        QString fileName = QFileDialog::getSaveFileName(this, "Export Chart",
            QString(), "PNG Image (*.png);;JPEG Image (*.jpg)");
        if (!fileName.isEmpty()) {
            exportToImage(fileName);
        }
    });
}

void InteractiveChartWidget::loadMetricData(MetricType type, const QDateTime& from, 
                                             const QDateTime& to, const QString& label)
{
    if (!m_metricsHistory) return;
    
    m_currentMetricType = type;
    m_dataFrom = from;
    m_dataTo = to;
    m_originalFrom = from;
    m_originalTo = to;
    
    // Get data from history
    m_currentData = m_metricsHistory->getMetricData(type, from, to, label);
    
    // Clear and rebuild chart
    clearSeries();
    
    if (m_currentData.empty()) return;
    
    // Create series based on display mode
    SeriesStyle style;
    style.lineColor = QColor(0, 120, 215);
    style.fillColor = QColor(0, 120, 215, 80);
    
    addSeries(MetricsHistory::metricTypeToString(type), m_currentData, style);
    
    // Update title
    setChartTitle(QString("%1 - %2 to %3")
        .arg(MetricsHistory::metricTypeToString(type))
        .arg(from.toString("dd/MM/yyyy hh:mm"))
        .arg(to.toString("dd/MM/yyyy hh:mm")));
    
    emit timeRangeChanged(from, to);
}

void InteractiveChartWidget::loadMetricData(MetricType type, TimeRange range, const QString& label)
{
    auto [from, to] = MetricsHistory::timeRangeToDateTime(range);
    loadMetricData(type, from, to, label);
}

void InteractiveChartWidget::addSeries(const QString& name, 
                                        const std::vector<MetricDataPoint>& data,
                                        const SeriesStyle& style)
{
    if (data.empty()) return;
    
    QLineSeries* lineSeries = new QLineSeries();
    lineSeries->setName(name);
    
    for (const auto& point : data) {
        lineSeries->append(point.timestamp.toMSecsSinceEpoch(), point.value);
    }
    
    // Apply styling
    applyStyle(lineSeries, style);
    
    // Connect hover signal
    connect(lineSeries, &QLineSeries::hovered, this, &InteractiveChartWidget::onSeriesHovered);
    connect(lineSeries, &QLineSeries::clicked, this, &InteractiveChartWidget::onSeriesClicked);
    
    if (m_displayMode == ChartDisplayMode::Area) {
        // Create area series
        QLineSeries* lowerSeries = new QLineSeries();
        for (const auto& point : data) {
            lowerSeries->append(point.timestamp.toMSecsSinceEpoch(), 0);
        }
        
        QAreaSeries* areaSeries = new QAreaSeries(lineSeries, lowerSeries);
        areaSeries->setName(name);
        areaSeries->setBrush(style.fillColor);
        areaSeries->setPen(QPen(style.lineColor, style.lineWidth));
        
        m_chart->addSeries(areaSeries);
        areaSeries->attachAxis(m_axisX);
        areaSeries->attachAxis(m_axisY);
    } else {
        m_chart->addSeries(lineSeries);
        lineSeries->attachAxis(m_axisX);
        lineSeries->attachAxis(m_axisY);
    }
    
    updateAxes();
}

void InteractiveChartWidget::clearSeries()
{
    m_chart->removeAllSeries();
}

void InteractiveChartWidget::applyStyle(QLineSeries* series, const SeriesStyle& style)
{
    QPen pen(style.lineColor);
    pen.setWidth(style.lineWidth);
    series->setPen(pen);
    
    if (style.showPoints) {
        series->setPointsVisible(true);
        series->setMarkerSize(style.pointSize);
    }
}

void InteractiveChartWidget::updateAxes()
{
    if (m_currentData.empty()) return;
    
    // Update X axis range
    m_axisX->setRange(m_dataFrom, m_dataTo);
    
    // Update Y axis range
    if (m_autoYRange) {
        double minVal = std::numeric_limits<double>::max();
        double maxVal = std::numeric_limits<double>::lowest();
        
        for (const auto& point : m_currentData) {
            minVal = std::min(minVal, point.value);
            maxVal = std::max(maxVal, point.value);
        }
        
        // Add some padding
        double padding = (maxVal - minVal) * 0.1;
        m_axisY->setRange(std::max(0.0, minVal - padding), maxVal + padding);
    } else {
        m_axisY->setRange(m_yMin, m_yMax);
    }
}

void InteractiveChartWidget::setChartTitle(const QString& title)
{
    m_chart->setTitle(title);
}

void InteractiveChartWidget::setYAxisRange(double min, double max)
{
    m_autoYRange = false;
    m_yMin = min;
    m_yMax = max;
    m_axisY->setRange(min, max);
}

void InteractiveChartWidget::setAutoYAxisRange(bool autoRange)
{
    m_autoYRange = autoRange;
    if (autoRange) {
        updateAxes();
    }
}

void InteractiveChartWidget::setDisplayMode(ChartDisplayMode mode)
{
    if (m_displayMode == mode) return;
    m_displayMode = mode;
    
    // Reload data with new display mode
    if (!m_currentData.empty()) {
        clearSeries();
        SeriesStyle style;
        addSeries(MetricsHistory::metricTypeToString(m_currentMetricType), m_currentData, style);
    }
}

void InteractiveChartWidget::setComparisonMode(bool enabled)
{
    m_comparisonMode = enabled;
}

void InteractiveChartWidget::setComparisonPeriods(const QDateTime& period1Start, 
                                                   const QDateTime& period1End,
                                                   const QDateTime& period2Start, 
                                                   const QDateTime& period2End)
{
    m_period1Start = period1Start;
    m_period1End = period1End;
    m_period2Start = period2Start;
    m_period2End = period2End;
    
    if (!m_metricsHistory || !m_comparisonMode) return;
    
    clearSeries();
    
    // Load both periods
    auto data1 = m_metricsHistory->getMetricData(m_currentMetricType, period1Start, period1End);
    auto data2 = m_metricsHistory->getMetricData(m_currentMetricType, period2Start, period2End);
    
    // Normalize period 2 timestamps to overlay on period 1
    qint64 offset = period1Start.toMSecsSinceEpoch() - period2Start.toMSecsSinceEpoch();
    for (auto& point : data2) {
        point.timestamp = point.timestamp.addMSecs(offset);
    }
    
    // Add both series
    SeriesStyle style1;
    style1.lineColor = QColor(0, 120, 215);
    addSeries("Period 1", data1, style1);
    
    SeriesStyle style2;
    style2.lineColor = QColor(255, 152, 0);
    addSeries("Period 2", data2, style2);
}

// ==================== Mouse Events ====================

void InteractiveChartWidget::mousePressEvent(QMouseEvent *event)
{
    if (event->button() == Qt::LeftButton) {
        if (event->modifiers() & Qt::ControlModifier) {
            // Start rubber band selection for zoom
            m_isSelecting = true;
            m_rubberBandOrigin = event->pos();
            m_rubberBand->setGeometry(QRect(m_rubberBandOrigin, QSize()));
            m_rubberBand->show();
        } else {
            // Start panning
            m_isPanning = true;
            m_lastMousePos = event->pos();
            setCursor(Qt::ClosedHandCursor);
        }
    }
    
    QChartView::mousePressEvent(event);
}

void InteractiveChartWidget::mouseMoveEvent(QMouseEvent *event)
{
    if (m_isPanning) {
        QPoint delta = event->pos() - m_lastMousePos;
        m_chart->scroll(-delta.x(), delta.y());
        m_lastMousePos = event->pos();
    } else if (m_isSelecting) {
        m_rubberBand->setGeometry(QRect(m_rubberBandOrigin, event->pos()).normalized());
    } else {
        // Show tooltip on hover
        QPointF chartPos = m_chart->mapToValue(event->pos());
        // Tooltip handling would go here
    }
    
    QChartView::mouseMoveEvent(event);
}

void InteractiveChartWidget::mouseReleaseEvent(QMouseEvent *event)
{
    if (m_isPanning) {
        m_isPanning = false;
        setCursor(Qt::ArrowCursor);
        
        // Emit new time range
        auto range = visibleTimeRange();
        emit timeRangeChanged(range.first, range.second);
    } else if (m_isSelecting) {
        m_isSelecting = false;
        m_rubberBand->hide();
        
        // Zoom to selected region
        QRectF rect = m_rubberBand->geometry();
        if (rect.width() > 10 && rect.height() > 10) {
            m_chart->zoomIn(rect);
            m_zoomLevel *= 1.5;
            emit zoomChanged(m_zoomLevel);
        }
    }
    
    QChartView::mouseReleaseEvent(event);
}

void InteractiveChartWidget::wheelEvent(QWheelEvent *event)
{
    // Zoom centered on mouse position
    qreal factor = event->angleDelta().y() > 0 ? 1.25 : 0.8;
    
    // Get the position under the mouse
    QPointF mousePos = event->position();
    QPointF chartPos = m_chart->mapToValue(mousePos.toPoint());
    
    // Zoom
    if (event->angleDelta().y() > 0) {
        m_chart->zoomIn();
        m_zoomLevel *= 1.25;
    } else {
        m_chart->zoomOut();
        m_zoomLevel *= 0.8;
    }
    
    emit zoomChanged(m_zoomLevel);
    
    event->accept();
}

void InteractiveChartWidget::contextMenuEvent(QContextMenuEvent *event)
{
    m_contextMenu->exec(event->globalPos());
}

void InteractiveChartWidget::resizeEvent(QResizeEvent *event)
{
    QChartView::resizeEvent(event);
}

void InteractiveChartWidget::onSeriesHovered(const QPointF &point, bool state)
{
    if (state) {
        QDateTime time = QDateTime::fromMSecsSinceEpoch(static_cast<qint64>(point.x()));
        emit pointHovered(time, point.y());
        
        // Update tooltip
        setToolTip(formatTooltip(time, point.y()));
    }
}

void InteractiveChartWidget::onSeriesClicked(const QPointF &point)
{
    QDateTime time = QDateTime::fromMSecsSinceEpoch(static_cast<qint64>(point.x()));
    emit pointClicked(time, point.y());
}

QString InteractiveChartWidget::formatTooltip(const QDateTime& time, double value)
{
    return QString("%1\n%2: %3")
        .arg(time.toString("dd/MM/yyyy hh:mm:ss"))
        .arg(MetricsHistory::metricTypeToString(m_currentMetricType))
        .arg(value, 0, 'f', 2);
}

// ==================== Zoom Controls ====================

void InteractiveChartWidget::zoomIn()
{
    m_chart->zoomIn();
    m_zoomLevel *= 1.5;
    emit zoomChanged(m_zoomLevel);
}

void InteractiveChartWidget::zoomOut()
{
    m_chart->zoomOut();
    m_zoomLevel /= 1.5;
    emit zoomChanged(m_zoomLevel);
}

void InteractiveChartWidget::resetZoom()
{
    m_chart->zoomReset();
    m_zoomLevel = 1.0;
    
    // Reset to original range
    m_dataFrom = m_originalFrom;
    m_dataTo = m_originalTo;
    updateAxes();
    
    emit zoomChanged(m_zoomLevel);
    emit timeRangeChanged(m_dataFrom, m_dataTo);
}

void InteractiveChartWidget::zoomToRange(const QDateTime& from, const QDateTime& to)
{
    m_dataFrom = from;
    m_dataTo = to;
    m_axisX->setRange(from, to);
    
    emit timeRangeChanged(from, to);
}

std::pair<QDateTime, QDateTime> InteractiveChartWidget::visibleTimeRange() const
{
    return {m_axisX->min(), m_axisX->max()};
}

bool InteractiveChartWidget::exportToImage(const QString& filePath, int width, int height)
{
    QPixmap pixmap(width, height);
    pixmap.fill(Qt::transparent);
    
    QPainter painter(&pixmap);
    painter.setRenderHint(QPainter::Antialiasing);
    render(&painter);
    
    return pixmap.save(filePath);
}

// ==================== TimeRangeSelector ====================

TimeRangeSelector::TimeRangeSelector(QWidget *parent)
    : QWidget(parent)
{
    setupUi();
}

void TimeRangeSelector::setupUi()
{
    QHBoxLayout* mainLayout = new QHBoxLayout(this);
    mainLayout->setContentsMargins(0, 0, 0, 0);
    
    QWidget* presetWidget = new QWidget();
    QHBoxLayout* presetLayout = new QHBoxLayout(presetWidget);
    presetLayout->setContentsMargins(0, 0, 0, 0);
    presetLayout->setSpacing(4);
    
    m_presetGroup = new QButtonGroup(this);
    
    auto createPresetButton = [this, presetLayout](const QString& text, TimeRange range) {
        QPushButton* btn = new QPushButton(text);
        btn->setCheckable(true);
        btn->setProperty("timeRange", static_cast<int>(range));
        btn->setMinimumWidth(60);
        m_presetGroup->addButton(btn);
        presetLayout->addWidget(btn);
        return btn;
    };
    
    createPresetButton("1H", TimeRange::Last1Hour);
    createPresetButton("6H", TimeRange::Last6Hours);
    auto btn24h = createPresetButton("24H", TimeRange::Last24Hours);
    btn24h->setChecked(true);
    createPresetButton("7D", TimeRange::Last7Days);
    createPresetButton("30D", TimeRange::Last30Days);
    
    connect(m_presetGroup, &QButtonGroup::buttonClicked, this, &TimeRangeSelector::onPresetClicked);
    
    mainLayout->addWidget(presetWidget);
    
    QFrame* separator = new QFrame();
    separator->setFrameShape(QFrame::VLine);
    separator->setFrameShadow(QFrame::Sunken);
    mainLayout->addWidget(separator);
    
    QLabel* fromLabel = new QLabel("From:");
    mainLayout->addWidget(fromLabel);
    
    m_fromEdit = new QDateTimeEdit();
    m_fromEdit->setCalendarPopup(true);
    m_fromEdit->setDateTime(QDateTime::currentDateTime().addDays(-1));
    m_fromEdit->setDisplayFormat("dd/MM/yyyy hh:mm");
    mainLayout->addWidget(m_fromEdit);
    
    QLabel* toLabel = new QLabel("To:");
    mainLayout->addWidget(toLabel);
    
    m_toEdit = new QDateTimeEdit();
    m_toEdit->setCalendarPopup(true);
    m_toEdit->setDateTime(QDateTime::currentDateTime());
    m_toEdit->setDisplayFormat("dd/MM/yyyy hh:mm");
    mainLayout->addWidget(m_toEdit);
    
    QPushButton* applyBtn = new QPushButton("Apply");
    connect(applyBtn, &QPushButton::clicked, this, &TimeRangeSelector::onCustomRangeApplied);
    mainLayout->addWidget(applyBtn);
    
    mainLayout->addStretch();
}

void TimeRangeSelector::setTimeRange(TimeRange range)
{
    m_currentRange = range;
    for (QAbstractButton* btn : m_presetGroup->buttons()) {
        if (btn->property("timeRange").toInt() == static_cast<int>(range)) {
            btn->setChecked(true);
            break;
        }
    }
    emit rangeChanged(range);
}

void TimeRangeSelector::setCustomRange(const QDateTime& from, const QDateTime& to)
{
    m_currentRange = TimeRange::Custom;
    m_fromEdit->setDateTime(from);
    m_toEdit->setDateTime(to);
    if (m_presetGroup->checkedButton()) {
        m_presetGroup->setExclusive(false);
        m_presetGroup->checkedButton()->setChecked(false);
        m_presetGroup->setExclusive(true);
    }
    emit customRangeChanged(from, to);
}

std::pair<QDateTime, QDateTime> TimeRangeSelector::customRange() const
{
    return {m_fromEdit->dateTime(), m_toEdit->dateTime()};
}

void TimeRangeSelector::onPresetClicked()
{
    QAbstractButton* btn = m_presetGroup->checkedButton();
    if (!btn) return;
    m_currentRange = static_cast<TimeRange>(btn->property("timeRange").toInt());
    auto [from, to] = MetricsHistory::timeRangeToDateTime(m_currentRange);
    m_fromEdit->setDateTime(from);
    m_toEdit->setDateTime(to);
    emit rangeChanged(m_currentRange);
}

void TimeRangeSelector::onCustomRangeApplied()
{
    m_currentRange = TimeRange::Custom;
    if (m_presetGroup->checkedButton()) {
        m_presetGroup->setExclusive(false);
        m_presetGroup->checkedButton()->setChecked(false);
        m_presetGroup->setExclusive(true);
    }
    emit customRangeChanged(m_fromEdit->dateTime(), m_toEdit->dateTime());
}

// ==================== PeriodComparisonWidget ====================

PeriodComparisonWidget::PeriodComparisonWidget(QWidget *parent)
    : QWidget(parent)
{
    setupUi();
}

void PeriodComparisonWidget::setupUi()
{
    QVBoxLayout* mainLayout = new QVBoxLayout(this);
    
    m_metricLabel = new QLabel("Metric Comparison");
    m_metricLabel->setStyleSheet("font-size: 14px; font-weight: bold;");
    mainLayout->addWidget(m_metricLabel);
    
    QHBoxLayout* period1Layout = new QHBoxLayout();
    m_period1Label = new QLabel("Period 1:");
    m_period1Label->setMinimumWidth(150);
    period1Layout->addWidget(m_period1Label);
    
    m_period1Bar = new QProgressBar();
    m_period1Bar->setTextVisible(false);
    m_period1Bar->setStyleSheet("QProgressBar::chunk { background-color: #0078d7; }");
    period1Layout->addWidget(m_period1Bar);
    
    m_period1Value = new QLabel("--");
    m_period1Value->setMinimumWidth(80);
    m_period1Value->setAlignment(Qt::AlignRight);
    period1Layout->addWidget(m_period1Value);
    mainLayout->addLayout(period1Layout);
    
    QHBoxLayout* period2Layout = new QHBoxLayout();
    m_period2Label = new QLabel("Period 2:");
    m_period2Label->setMinimumWidth(150);
    period2Layout->addWidget(m_period2Label);
    
    m_period2Bar = new QProgressBar();
    m_period2Bar->setTextVisible(false);
    m_period2Bar->setStyleSheet("QProgressBar::chunk { background-color: #ff9800; }");
    period2Layout->addWidget(m_period2Bar);
    
    m_period2Value = new QLabel("--");
    m_period2Value->setMinimumWidth(80);
    m_period2Value->setAlignment(Qt::AlignRight);
    period2Layout->addWidget(m_period2Value);
    mainLayout->addLayout(period2Layout);
    
    m_differenceLabel = new QLabel("Difference: --");
    m_differenceLabel->setStyleSheet("font-size: 12px; margin-top: 8px;");
    mainLayout->addWidget(m_differenceLabel);
}

void PeriodComparisonWidget::setComparison(const PeriodComparison& comparison)
{
    m_metricLabel->setText(QString("Comparison: %1").arg(
        MetricsHistory::metricTypeToString(comparison.metricType)));
    
    m_period1Label->setText(QString("%1 - %2")
        .arg(comparison.period1Start.toString("dd/MM hh:mm"))
        .arg(comparison.period1End.toString("dd/MM hh:mm")));
    
    m_period2Label->setText(QString("%1 - %2")
        .arg(comparison.period2Start.toString("dd/MM hh:mm"))
        .arg(comparison.period2End.toString("dd/MM hh:mm")));
    
    double maxVal = std::max(comparison.period1Avg, comparison.period2Avg);
    if (maxVal > 0) {
        m_period1Bar->setValue(static_cast<int>(comparison.period1Avg / maxVal * 100));
        m_period2Bar->setValue(static_cast<int>(comparison.period2Avg / maxVal * 100));
    }
    
    m_period1Value->setText(formatValue(comparison.period1Avg, comparison.metricType));
    m_period2Value->setText(formatValue(comparison.period2Avg, comparison.metricType));
    m_differenceLabel->setText(formatDifference(comparison.avgDifference, comparison.avgDifferencePercent));
    
    if (comparison.avgDifferencePercent > 5) {
        m_differenceLabel->setStyleSheet("font-size: 12px; margin-top: 8px; color: #ff5252;");
    } else if (comparison.avgDifferencePercent < -5) {
        m_differenceLabel->setStyleSheet("font-size: 12px; margin-top: 8px; color: #4caf50;");
    } else {
        m_differenceLabel->setStyleSheet("font-size: 12px; margin-top: 8px; color: white;");
    }
}

void PeriodComparisonWidget::clear()
{
    m_metricLabel->setText("Metric Comparison");
    m_period1Label->setText("Period 1:");
    m_period2Label->setText("Period 2:");
    m_period1Value->setText("--");
    m_period2Value->setText("--");
    m_period1Bar->setValue(0);
    m_period2Bar->setValue(0);
    m_differenceLabel->setText("Difference: --");
}

QString PeriodComparisonWidget::formatValue(double value, MetricType type)
{
    switch (type) {
        case MetricType::CpuUsage:
        case MetricType::GpuUsage:
        case MetricType::BatteryPercent:
        case MetricType::BatteryHealth:
            return QString("%1%").arg(value, 0, 'f', 1);
        case MetricType::CpuTemperature:
        case MetricType::GpuTemperature:
            return QString("%1°C").arg(value, 0, 'f', 1);
        case MetricType::MemoryUsed:
        case MetricType::MemoryAvailable:
        case MetricType::MemoryCommit:
        case MetricType::GpuMemory:
            return QString("%1 GB").arg(value / (1024.0 * 1024.0 * 1024.0), 0, 'f', 2);
        case MetricType::DiskRead:
        case MetricType::DiskWrite:
        case MetricType::NetworkSend:
        case MetricType::NetworkReceive:
            if (value > 1024 * 1024) return QString("%1 MB/s").arg(value / (1024.0 * 1024.0), 0, 'f', 2);
            else if (value > 1024) return QString("%1 KB/s").arg(value / 1024.0, 0, 'f', 2);
            return QString("%1 B/s").arg(value, 0, 'f', 0);
        default:
            return QString::number(value, 'f', 2);
    }
}

QString PeriodComparisonWidget::formatDifference(double diff, double percent)
{
    QString sign = diff >= 0 ? "+" : "";
    QString arrow = diff > 0 ? "↑" : (diff < 0 ? "↓" : "→");
    return QString("Difference: %1%2 (%3%4%) %5")
        .arg(sign).arg(diff, 0, 'f', 2).arg(sign).arg(percent, 0, 'f', 1).arg(arrow);
}
