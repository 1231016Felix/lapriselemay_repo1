#include "comparisonchart.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QLabel>
#include <QGroupBox>
#include <QSplitter>
#include <QGraphicsScene>
#include <QApplication>
#include <QClipboard>
#include <QPainter>
#include <QAreaSeries>
#include <QDebug>
#include <numeric>
#include <cmath>

ComparisonChart::ComparisonChart(QWidget* parent)
    : QWidget(parent)
{
    setupChart();
}

ComparisonChart::~ComparisonChart() = default;

void ComparisonChart::setupChart()
{
    QVBoxLayout* mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(0, 0, 0, 0);
    mainLayout->setSpacing(5);

    // Main chart
    m_chart1 = new QChart();
    m_chart1->setAnimationOptions(QChart::NoAnimation);
    m_chart1->legend()->setVisible(true);
    m_chart1->legend()->setAlignment(Qt::AlignBottom);
    
    m_chartView1 = new QChartView(m_chart1, this);
    m_chartView1->setRenderHint(QPainter::Antialiasing);
    mainLayout->addWidget(m_chartView1, 1);

    // Statistics widget
    m_statsWidget = new QWidget();
    QHBoxLayout* statsLayout = new QHBoxLayout(m_statsWidget);
    statsLayout->setContentsMargins(10, 5, 10, 5);
    
    m_period1StatsLabel = new QLabel();
    m_period2StatsLabel = new QLabel();
    m_verdictLabel = new QLabel();
    m_verdictLabel->setStyleSheet("font-weight: bold; font-size: 14px;");
    
    statsLayout->addWidget(m_period1StatsLabel);
    statsLayout->addWidget(m_period2StatsLabel);
    statsLayout->addStretch();
    statsLayout->addWidget(m_verdictLabel);
    
    mainLayout->addWidget(m_statsWidget);

    // Apply dark theme by default
    applyTheme();
}

void ComparisonChart::setPeriod1(const QString& name, const QDateTime& start, const QDateTime& end,
                                   const std::vector<QPointF>& data, const QColor& color)
{
    m_period1.name = name;
    m_period1.startTime = start;
    m_period1.endTime = end;
    m_period1.data = data;
    m_period1.color = color;
    
    calculateStatistics();
    updateChart();
}

void ComparisonChart::setPeriod2(const QString& name, const QDateTime& start, const QDateTime& end,
                                   const std::vector<QPointF>& data, const QColor& color)
{
    m_period2.name = name;
    m_period2.startTime = start;
    m_period2.endTime = end;
    m_period2.data = data;
    m_period2.color = color;
    
    calculateStatistics();
    updateChart();
}

void ComparisonChart::clear()
{
    m_period1 = PeriodData();
    m_period2 = PeriodData();
    m_stats = ComparisonStats();
    
    m_chart1->removeAllSeries();
    
    // Clear axes
    for (auto axis : m_chart1->axes()) {
        m_chart1->removeAxis(axis);
    }
    
    m_series1 = nullptr;
    m_series2 = nullptr;
    m_diffArea = nullptr;
    
    updateStatisticsDisplay();
}

void ComparisonChart::setComparisonMode(ComparisonMode mode)
{
    m_mode = mode;
    updateChart();
}

void ComparisonChart::setTitle(const QString& title)
{
    m_title = title;
    if (m_chart1) {
        m_chart1->setTitle(title);
    }
}

void ComparisonChart::setYAxisTitle(const QString& title)
{
    m_yAxisTitle = title;
    if (m_axisY1) {
        m_axisY1->setTitleText(title);
    }
}

void ComparisonChart::setYAxisRange(double min, double max)
{
    m_autoYRange = false;
    m_yMin = min;
    m_yMax = max;
    if (m_axisY1) {
        m_axisY1->setRange(min, max);
    }
}

void ComparisonChart::setAutoYAxisRange(bool autoRange)
{
    m_autoYRange = autoRange;
    if (autoRange) {
        updateChart();
    }
}

void ComparisonChart::setShowStatistics(bool show)
{
    m_showStatistics = show;
    m_statsWidget->setVisible(show);
}

void ComparisonChart::setShowDifferenceArea(bool show)
{
    m_showDifferenceArea = show;
    updateChart();
}

void ComparisonChart::setDarkTheme(bool dark)
{
    m_darkTheme = dark;
    applyTheme();
}

ComparisonChart::ComparisonStats ComparisonChart::getStatistics() const
{
    return m_stats;
}

void ComparisonChart::calculateStatistics()
{
    // Period 1 stats
    if (!m_period1.data.empty()) {
        double sum = 0;
        m_period1.minValue = m_period1.data[0].y();
        m_period1.maxValue = m_period1.data[0].y();
        
        for (const auto& pt : m_period1.data) {
            sum += pt.y();
            m_period1.minValue = qMin(m_period1.minValue, pt.y());
            m_period1.maxValue = qMax(m_period1.maxValue, pt.y());
        }
        m_period1.avgValue = sum / m_period1.data.size();
        m_period1.sampleCount = static_cast<int>(m_period1.data.size());
    }
    
    // Period 2 stats
    if (!m_period2.data.empty()) {
        double sum = 0;
        m_period2.minValue = m_period2.data[0].y();
        m_period2.maxValue = m_period2.data[0].y();
        
        for (const auto& pt : m_period2.data) {
            sum += pt.y();
            m_period2.minValue = qMin(m_period2.minValue, pt.y());
            m_period2.maxValue = qMax(m_period2.maxValue, pt.y());
        }
        m_period2.avgValue = sum / m_period2.data.size();
        m_period2.sampleCount = static_cast<int>(m_period2.data.size());
    }
    
    // Comparison stats
    m_stats.period1Avg = m_period1.avgValue;
    m_stats.period2Avg = m_period2.avgValue;
    m_stats.avgDifference = m_period2.avgValue - m_period1.avgValue;
    
    if (std::abs(m_period1.avgValue) > 0.001) {
        m_stats.avgDifferencePercent = (m_stats.avgDifference / m_period1.avgValue) * 100.0;
    } else {
        m_stats.avgDifferencePercent = 0.0;
    }
    
    // Determine verdict
    double threshold = 5.0; // 5% change is significant
    if (std::abs(m_stats.avgDifferencePercent) < threshold) {
        m_stats.verdict = tr("Similar");
        m_stats.verdictColor = QColor(158, 158, 158);
    } else if (m_higherIsBetter) {
        // For metrics like battery, higher is better
        if (m_stats.avgDifference > 0) {
            m_stats.verdict = tr("Better ↑");
            m_stats.verdictColor = QColor(76, 175, 80);
        } else {
            m_stats.verdict = tr("Worse ↓");
            m_stats.verdictColor = QColor(244, 67, 54);
        }
    } else {
        // For metrics like CPU usage, lower is better
        if (m_stats.avgDifference < 0) {
            m_stats.verdict = tr("Better ↓");
            m_stats.verdictColor = QColor(76, 175, 80);
        } else {
            m_stats.verdict = tr("Worse ↑");
            m_stats.verdictColor = QColor(244, 67, 54);
        }
    }
    
    updateStatisticsDisplay();
}

void ComparisonChart::updateStatisticsDisplay()
{
    QString p1Text = QString("<b>%1:</b> Avg: %2%3 | Min: %4%3 | Max: %5%3")
        .arg(m_period1.name)
        .arg(m_period1.avgValue, 0, 'f', 1)
        .arg(m_valueSuffix)
        .arg(m_period1.minValue, 0, 'f', 1)
        .arg(m_period1.maxValue, 0, 'f', 1);
    m_period1StatsLabel->setText(p1Text);
    
    QString p2Text = QString("<b>%1:</b> Avg: %2%3 | Min: %4%3 | Max: %5%3")
        .arg(m_period2.name)
        .arg(m_period2.avgValue, 0, 'f', 1)
        .arg(m_valueSuffix)
        .arg(m_period2.minValue, 0, 'f', 1)
        .arg(m_period2.maxValue, 0, 'f', 1);
    m_period2StatsLabel->setText(p2Text);
    
    QString changeSign = m_stats.avgDifference >= 0 ? "+" : "";
    QString verdictText = QString("%1 (%2%3%4, %5%6%)")
        .arg(m_stats.verdict)
        .arg(changeSign)
        .arg(m_stats.avgDifference, 0, 'f', 1)
        .arg(m_valueSuffix)
        .arg(changeSign)
        .arg(m_stats.avgDifferencePercent, 0, 'f', 1);
    m_verdictLabel->setText(verdictText);
    m_verdictLabel->setStyleSheet(QString("font-weight: bold; font-size: 14px; color: %1;")
        .arg(m_stats.verdictColor.name()));
}

void ComparisonChart::updateChart()
{
    switch (m_mode) {
        case ComparisonMode::Overlay:
            updateOverlayChart();
            break;
        case ComparisonMode::SideBySide:
            updateSideBySideChart();
            break;
        case ComparisonMode::Difference:
            updateDifferenceChart();
            break;
    }
}

void ComparisonChart::updateOverlayChart()
{
    m_chart1->removeAllSeries();
    for (auto axis : m_chart1->axes()) {
        m_chart1->removeAxis(axis);
    }
    
    if (m_period1.data.empty() && m_period2.data.empty()) {
        return;
    }
    
    // Create axes
    m_axisX1 = new QDateTimeAxis();
    m_axisX1->setFormat("HH:mm");
    m_axisX1->setTitleText(tr("Time (relative)"));
    m_axisX1->setTickCount(10);
    
    m_axisY1 = new QValueAxis();
    m_axisY1->setTitleText(m_yAxisTitle);
    m_axisY1->setTickCount(11);
    
    m_chart1->addAxis(m_axisX1, Qt::AlignBottom);
    m_chart1->addAxis(m_axisY1, Qt::AlignLeft);
    
    // Calculate common time base (use period duration as offset)
    qint64 period1Duration = m_period1.endTime.toMSecsSinceEpoch() - m_period1.startTime.toMSecsSinceEpoch();
    qint64 period2Duration = m_period2.endTime.toMSecsSinceEpoch() - m_period2.startTime.toMSecsSinceEpoch();
    qint64 maxDuration = qMax(period1Duration, period2Duration);
    
    // Base time for X axis (use an arbitrary reference)
    QDateTime baseTime = QDateTime::currentDateTime();
    baseTime.setTime(QTime(0, 0));
    
    // Period 1 series
    if (!m_period1.data.empty()) {
        m_series1 = new QLineSeries();
        m_series1->setName(m_period1.name);
        m_series1->setColor(m_period1.color);
        
        QPen pen = m_series1->pen();
        pen.setWidth(2);
        m_series1->setPen(pen);
        
        qint64 p1Start = m_period1.startTime.toMSecsSinceEpoch();
        for (const auto& pt : m_period1.data) {
            // Normalize time to 0-based offset
            qint64 offset = static_cast<qint64>(pt.x()) - p1Start;
            qint64 normalizedTime = baseTime.toMSecsSinceEpoch() + offset;
            m_series1->append(normalizedTime, pt.y());
        }
        
        connect(m_series1, &QLineSeries::hovered, this, &ComparisonChart::onSeries1Hovered);
        m_chart1->addSeries(m_series1);
        m_series1->attachAxis(m_axisX1);
        m_series1->attachAxis(m_axisY1);
    }
    
    // Period 2 series
    if (!m_period2.data.empty()) {
        m_series2 = new QLineSeries();
        m_series2->setName(m_period2.name);
        m_series2->setColor(m_period2.color);
        
        QPen pen = m_series2->pen();
        pen.setWidth(2);
        pen.setStyle(Qt::DashLine);
        m_series2->setPen(pen);
        
        qint64 p2Start = m_period2.startTime.toMSecsSinceEpoch();
        for (const auto& pt : m_period2.data) {
            qint64 offset = static_cast<qint64>(pt.x()) - p2Start;
            qint64 normalizedTime = baseTime.toMSecsSinceEpoch() + offset;
            m_series2->append(normalizedTime, pt.y());
        }
        
        connect(m_series2, &QLineSeries::hovered, this, &ComparisonChart::onSeries2Hovered);
        m_chart1->addSeries(m_series2);
        m_series2->attachAxis(m_axisX1);
        m_series2->attachAxis(m_axisY1);
    }
    
    // Set axis ranges
    m_axisX1->setRange(baseTime, QDateTime::fromMSecsSinceEpoch(baseTime.toMSecsSinceEpoch() + maxDuration));
    
    if (m_autoYRange) {
        double minY = qMin(m_period1.minValue, m_period2.minValue);
        double maxY = qMax(m_period1.maxValue, m_period2.maxValue);
        double padding = (maxY - minY) * 0.1;
        m_axisY1->setRange(qMax(0.0, minY - padding), maxY + padding);
    } else {
        m_axisY1->setRange(m_yMin, m_yMax);
    }
    
    // Update time axis format
    if (maxDuration < 3600000) {
        m_axisX1->setFormat("mm:ss");
    } else if (maxDuration < 86400000) {
        m_axisX1->setFormat("HH:mm");
    } else {
        m_axisX1->setFormat("dd HH:mm");
    }
}

void ComparisonChart::updateSideBySideChart()
{
    // For side-by-side, we would need a second chart
    // For now, fall back to overlay
    updateOverlayChart();
}

void ComparisonChart::updateDifferenceChart()
{
    m_chart1->removeAllSeries();
    for (auto axis : m_chart1->axes()) {
        m_chart1->removeAxis(axis);
    }
    
    if (m_period1.data.empty() || m_period2.data.empty()) {
        return;
    }
    
    // Create difference series
    QLineSeries* diffSeries = new QLineSeries();
    diffSeries->setName(tr("Difference (%1 - %2)").arg(m_period2.name, m_period1.name));
    
    // Resample both series to common time points
    qint64 p1Start = m_period1.startTime.toMSecsSinceEpoch();
    qint64 p2Start = m_period2.startTime.toMSecsSinceEpoch();
    
    QDateTime baseTime = QDateTime::currentDateTime();
    baseTime.setTime(QTime(0, 0));
    
    // Create interpolated difference
    size_t maxSamples = qMax(m_period1.data.size(), m_period2.data.size());
    
    for (size_t i = 0; i < maxSamples; ++i) {
        double p1Val = 0, p2Val = 0;
        qint64 offset = 0;
        
        if (i < m_period1.data.size()) {
            offset = static_cast<qint64>(m_period1.data[i].x()) - p1Start;
            p1Val = m_period1.data[i].y();
        }
        if (i < m_period2.data.size()) {
            p2Val = m_period2.data[i].y();
        }
        
        double diff = p2Val - p1Val;
        qint64 time = baseTime.toMSecsSinceEpoch() + offset;
        diffSeries->append(time, diff);
    }
    
    // Color based on difference direction
    if (m_stats.avgDifference >= 0) {
        diffSeries->setColor(m_higherIsBetter ? QColor(76, 175, 80) : QColor(244, 67, 54));
    } else {
        diffSeries->setColor(m_higherIsBetter ? QColor(244, 67, 54) : QColor(76, 175, 80));
    }
    
    QPen pen = diffSeries->pen();
    pen.setWidth(2);
    diffSeries->setPen(pen);
    
    // Create axes
    m_axisX1 = new QDateTimeAxis();
    m_axisX1->setFormat("HH:mm");
    m_axisX1->setTitleText(tr("Time"));
    m_axisX1->setTickCount(10);
    
    m_axisY1 = new QValueAxis();
    m_axisY1->setTitleText(tr("Difference") + " " + m_valueSuffix);
    m_axisY1->setTickCount(11);
    
    m_chart1->addAxis(m_axisX1, Qt::AlignBottom);
    m_chart1->addAxis(m_axisY1, Qt::AlignLeft);
    
    m_chart1->addSeries(diffSeries);
    diffSeries->attachAxis(m_axisX1);
    diffSeries->attachAxis(m_axisY1);
    
    // Add zero line
    QLineSeries* zeroLine = new QLineSeries();
    zeroLine->setName("");
    zeroLine->setPen(QPen(QColor(128, 128, 128), 1, Qt::DashLine));
    
    auto points = diffSeries->points();
    if (!points.isEmpty()) {
        zeroLine->append(points.first().x(), 0);
        zeroLine->append(points.last().x(), 0);
        
        m_chart1->addSeries(zeroLine);
        zeroLine->attachAxis(m_axisX1);
        zeroLine->attachAxis(m_axisY1);
        
        m_axisX1->setRange(QDateTime::fromMSecsSinceEpoch(static_cast<qint64>(points.first().x())),
                          QDateTime::fromMSecsSinceEpoch(static_cast<qint64>(points.last().x())));
    }
    
    // Auto Y range for difference
    double maxAbs = qMax(qAbs(m_stats.avgDifference * 2), 10.0);
    m_axisY1->setRange(-maxAbs, maxAbs);
}

void ComparisonChart::applyTheme()
{
    if (!m_chart1) return;
    
    if (m_darkTheme) {
        m_chart1->setBackgroundBrush(QColor(30, 30, 30));
        m_chart1->setPlotAreaBackgroundBrush(QColor(25, 25, 25));
        m_chart1->setPlotAreaBackgroundVisible(true);
        m_chart1->setTitleBrush(Qt::white);
        m_chart1->legend()->setLabelColor(Qt::white);
        
        m_statsWidget->setStyleSheet("background-color: #1e1e1e; color: white;");
    } else {
        m_chart1->setBackgroundBrush(Qt::white);
        m_chart1->setPlotAreaBackgroundBrush(QColor(250, 250, 250));
        m_chart1->setPlotAreaBackgroundVisible(true);
        m_chart1->setTitleBrush(Qt::black);
        m_chart1->legend()->setLabelColor(Qt::black);
        
        m_statsWidget->setStyleSheet("background-color: #f5f5f5; color: black;");
    }
}

QString ComparisonChart::formatValue(double value) const
{
    return QString::number(value, 'f', 1) + m_valueSuffix;
}

void ComparisonChart::onSeries1Hovered(const QPointF& point, bool state)
{
    if (state) {
        emit dataPointHovered(m_period1.name,
            QDateTime::fromMSecsSinceEpoch(static_cast<qint64>(point.x())),
            point.y());
    }
}

void ComparisonChart::onSeries2Hovered(const QPointF& point, bool state)
{
    if (state) {
        emit dataPointHovered(m_period2.name,
            QDateTime::fromMSecsSinceEpoch(static_cast<qint64>(point.x())),
            point.y());
    }
}

bool ComparisonChart::exportToImage(const QString& path, int width, int height)
{
    QPixmap pixmap(width, height);
    pixmap.fill(m_darkTheme ? QColor(30, 30, 30) : Qt::white);
    
    QPainter painter(&pixmap);
    painter.setRenderHint(QPainter::Antialiasing);
    m_chartView1->render(&painter);
    
    return pixmap.save(path);
}

void ComparisonChart::copyToClipboard()
{
    QPixmap pixmap(m_chartView1->size());
    pixmap.fill(m_darkTheme ? QColor(30, 30, 30) : Qt::white);
    
    QPainter painter(&pixmap);
    painter.setRenderHint(QPainter::Antialiasing);
    m_chartView1->render(&painter);
    
    QApplication::clipboard()->setPixmap(pixmap);
}

void ComparisonChart::resizeEvent(QResizeEvent* event)
{
    QWidget::resizeEvent(event);
}
