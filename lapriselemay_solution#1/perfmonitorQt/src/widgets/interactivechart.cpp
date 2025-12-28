#include "interactivechart.h"

#include <QVBoxLayout>
#include <QGraphicsScene>
#include <QGraphicsTextItem>
#include <QMouseEvent>
#include <QWheelEvent>
#include <QApplication>
#include <QClipboard>
#include <QFileDialog>
#include <QPdfWriter>
#include <QPainter>
#include <QBuffer>
#include <QtMath>
#include <QDebug>

InteractiveChart::InteractiveChart(QWidget* parent)
    : QWidget(parent)
{
    setupChart();
    createContextMenu();
}

InteractiveChart::~InteractiveChart() = default;

void InteractiveChart::setupChart()
{
    m_chart = new QChart();
    m_chart->setAnimationOptions(QChart::NoAnimation);
    m_chart->legend()->setVisible(true);
    m_chart->legend()->setAlignment(Qt::AlignBottom);
    
    m_chartView = new QChartView(m_chart, this);
    m_chartView->setRenderHint(QPainter::Antialiasing);
    m_chartView->setRubberBand(QChartView::NoRubberBand);  // We handle this manually
    m_chartView->setMouseTracking(true);
    
    QVBoxLayout* layout = new QVBoxLayout(this);
    layout->setContentsMargins(0, 0, 0, 0);
    layout->addWidget(m_chartView);
    
    setMouseTracking(true);
    
    // Apply dark theme by default
    setDarkTheme(true);
    
    setupAxes();
    
    // Create crosshair items
    m_crosshairH = new QGraphicsLineItem();
    m_crosshairV = new QGraphicsLineItem();
    m_crosshairLabel = new QGraphicsTextItem();
    
    QPen crosshairPen(QColor(128, 128, 128, 180));
    crosshairPen.setStyle(Qt::DashLine);
    m_crosshairH->setPen(crosshairPen);
    m_crosshairV->setPen(crosshairPen);
    
    m_chart->scene()->addItem(m_crosshairH);
    m_chart->scene()->addItem(m_crosshairV);
    m_chart->scene()->addItem(m_crosshairLabel);
    
    hideCrosshair();
    
    m_rubberBand = std::make_unique<QRubberBand>(QRubberBand::Rectangle, m_chartView);
}

void InteractiveChart::setupAxes()
{
    // Remove existing axes
    for (auto axis : m_chart->axes()) {
        m_chart->removeAxis(axis);
    }
    
    // Create new axes
    m_axisX = new QDateTimeAxis();
    m_axisX->setFormat("HH:mm");
    m_axisX->setTitleText("Time");
    m_axisX->setTickCount(10);
    
    m_axisY = new QValueAxis();
    m_axisY->setTitleText("Value");
    m_axisY->setRange(0, 100);
    m_axisY->setTickCount(11);
    
    m_chart->addAxis(m_axisX, Qt::AlignBottom);
    m_chart->addAxis(m_axisY, Qt::AlignLeft);
}

void InteractiveChart::clear()
{
    m_chart->removeAllSeries();
    m_seriesData.clear();
    m_lineSeries.clear();
    m_areaSeries.clear();
    m_zoomHistory.clear();
    m_zoomHistoryIndex = -1;
    setupAxes();
}

void InteractiveChart::addSeries(const QString& name, const QColor& color,
                                  const std::vector<QPointF>& data, bool showArea)
{
    // Store data
    ChartSeries seriesInfo;
    seriesInfo.name = name;
    seriesInfo.color = color;
    seriesInfo.data = data;
    seriesInfo.showArea = showArea;
    m_seriesData[name] = seriesInfo;
    
    // Create Qt series
    QLineSeries* lineSeries = new QLineSeries();
    lineSeries->setName(name);
    lineSeries->setColor(color);
    
    QPen pen = lineSeries->pen();
    pen.setWidth(2);
    lineSeries->setPen(pen);
    
    // Add data points
    for (const auto& pt : data) {
        lineSeries->append(pt);
    }
    
    // Connect signals
    connect(lineSeries, &QLineSeries::hovered, this, &InteractiveChart::onSeriesHovered);
    connect(lineSeries, &QLineSeries::clicked, this, &InteractiveChart::onSeriesClicked);
    
    if (showArea) {
        QLineSeries* lowerSeries = new QLineSeries();
        for (const auto& pt : data) {
            lowerSeries->append(pt.x(), 0);
        }
        
        QAreaSeries* areaSeries = new QAreaSeries(lineSeries, lowerSeries);
        areaSeries->setName(name);
        QColor areaColor = color;
        areaColor.setAlpha(50);
        areaSeries->setBrush(areaColor);
        areaSeries->setPen(QPen(color, 2));
        
        m_chart->addSeries(areaSeries);
        areaSeries->attachAxis(m_axisX);
        areaSeries->attachAxis(m_axisY);
        
        m_areaSeries[name] = areaSeries;
    } else {
        m_chart->addSeries(lineSeries);
        lineSeries->attachAxis(m_axisX);
        lineSeries->attachAxis(m_axisY);
        
        m_lineSeries[name] = lineSeries;
    }
    
    updateChart();
}

void InteractiveChart::updateSeries(const QString& name, const std::vector<QPointF>& data)
{
    auto it = m_seriesData.find(name);
    if (it == m_seriesData.end()) return;
    
    it->second.data = data;
    
    if (m_lineSeries.count(name)) {
        QLineSeries* series = m_lineSeries[name];
        series->clear();
        for (const auto& pt : data) {
            series->append(pt);
        }
    }
    
    updateChart();
}

void InteractiveChart::removeSeries(const QString& name)
{
    m_seriesData.erase(name);
    
    if (m_lineSeries.count(name)) {
        m_chart->removeSeries(m_lineSeries[name]);
        m_lineSeries.erase(name);
    }
    if (m_areaSeries.count(name)) {
        m_chart->removeSeries(m_areaSeries[name]);
        m_areaSeries.erase(name);
    }
}

void InteractiveChart::setSeriesVisible(const QString& name, bool visible)
{
    if (m_seriesData.count(name)) {
        m_seriesData[name].visible = visible;
    }
    if (m_lineSeries.count(name)) {
        m_lineSeries[name]->setVisible(visible);
    }
    if (m_areaSeries.count(name)) {
        m_areaSeries[name]->setVisible(visible);
    }
}

QStringList InteractiveChart::seriesNames() const
{
    QStringList names;
    for (const auto& [name, _] : m_seriesData) {
        names.append(name);
    }
    return names;
}

void InteractiveChart::setTitle(const QString& title)
{
    m_chart->setTitle(title);
}

void InteractiveChart::setAxisTitles(const QString& xTitle, const QString& yTitle)
{
    if (m_axisX) m_axisX->setTitleText(xTitle);
    if (m_axisY) m_axisY->setTitleText(yTitle);
}

void InteractiveChart::setYAxisRange(double min, double max)
{
    m_autoYRange = false;
    m_yMin = min;
    m_yMax = max;
    if (m_axisY) m_axisY->setRange(min, max);
}

void InteractiveChart::setAutoYAxisRange(bool autoRange)
{
    m_autoYRange = autoRange;
    if (autoRange) updateChart();
}

void InteractiveChart::setGridVisible(bool visible)
{
    if (m_axisX) m_axisX->setGridLineVisible(visible);
    if (m_axisY) m_axisY->setGridLineVisible(visible);
}

void InteractiveChart::setLegendVisible(bool visible)
{
    m_chart->legend()->setVisible(visible);
}

void InteractiveChart::setDarkTheme(bool dark)
{
    m_darkTheme = dark;
    
    if (dark) {
        m_chart->setBackgroundBrush(QColor(30, 30, 30));
        m_chart->setPlotAreaBackgroundBrush(QColor(25, 25, 25));
        m_chart->setPlotAreaBackgroundVisible(true);
        m_chart->setTitleBrush(Qt::white);
        
        if (m_axisX) {
            m_axisX->setLabelsColor(Qt::white);
            m_axisX->setTitleBrush(Qt::white);
            m_axisX->setGridLineColor(QColor(60, 60, 60));
        }
        if (m_axisY) {
            m_axisY->setLabelsColor(Qt::white);
            m_axisY->setTitleBrush(Qt::white);
            m_axisY->setGridLineColor(QColor(60, 60, 60));
        }
        
        m_chart->legend()->setLabelColor(Qt::white);
        
        if (m_crosshairLabel) {
            m_crosshairLabel->setDefaultTextColor(Qt::white);
        }
    } else {
        m_chart->setBackgroundBrush(Qt::white);
        m_chart->setPlotAreaBackgroundBrush(QColor(250, 250, 250));
        m_chart->setPlotAreaBackgroundVisible(true);
        m_chart->setTitleBrush(Qt::black);
        
        if (m_axisX) {
            m_axisX->setLabelsColor(Qt::black);
            m_axisX->setTitleBrush(Qt::black);
            m_axisX->setGridLineColor(QColor(200, 200, 200));
        }
        if (m_axisY) {
            m_axisY->setLabelsColor(Qt::black);
            m_axisY->setTitleBrush(Qt::black);
            m_axisY->setGridLineColor(QColor(200, 200, 200));
        }
        
        m_chart->legend()->setLabelColor(Qt::black);
        
        if (m_crosshairLabel) {
            m_crosshairLabel->setDefaultTextColor(Qt::black);
        }
    }
}

void InteractiveChart::setComparisonMode(bool enabled, const QString& period1Name,
                                          const QString& period2Name)
{
    // Implementation for comparison mode
    // This would show two overlaid datasets with different time origins
    Q_UNUSED(enabled)
    Q_UNUSED(period1Name)
    Q_UNUSED(period2Name)
}

void InteractiveChart::setCrosshairEnabled(bool enabled)
{
    m_crosshairEnabled = enabled;
    if (!enabled) {
        hideCrosshair();
    }
}

void InteractiveChart::resetZoom()
{
    if (m_seriesData.empty()) return;
    
    // Find data range
    qreal minX = std::numeric_limits<qreal>::max();
    qreal maxX = std::numeric_limits<qreal>::lowest();
    qreal minY = std::numeric_limits<qreal>::max();
    qreal maxY = std::numeric_limits<qreal>::lowest();
    
    for (const auto& [name, series] : m_seriesData) {
        for (const auto& pt : series.data) {
            minX = qMin(minX, pt.x());
            maxX = qMax(maxX, pt.x());
            minY = qMin(minY, pt.y());
            maxY = qMax(maxY, pt.y());
        }
    }
    
    if (minX < maxX && minY <= maxY) {
        m_axisX->setRange(QDateTime::fromMSecsSinceEpoch(static_cast<qint64>(minX)),
                          QDateTime::fromMSecsSinceEpoch(static_cast<qint64>(maxX)));
        
        if (m_autoYRange) {
            double padding = (maxY - minY) * 0.1;
            m_axisY->setRange(qMax(0.0, minY - padding), maxY + padding);
        } else {
            m_axisY->setRange(m_yMin, m_yMax);
        }
    }
    
    m_zoomHistory.clear();
    m_zoomHistoryIndex = -1;
    pushZoomState();
    
    emit visibleRangeChanged(m_axisX->min(), m_axisX->max());
}

void InteractiveChart::undoZoom()
{
    if (m_zoomHistoryIndex > 0) {
        m_zoomHistoryIndex--;
        applyZoomState(m_zoomHistory[m_zoomHistoryIndex]);
    }
}

void InteractiveChart::redoZoom()
{
    if (m_zoomHistoryIndex < static_cast<int>(m_zoomHistory.size()) - 1) {
        m_zoomHistoryIndex++;
        applyZoomState(m_zoomHistory[m_zoomHistoryIndex]);
    }
}

void InteractiveChart::zoomToTimeRange(const QDateTime& start, const QDateTime& end)
{
    pushZoomState();
    m_axisX->setRange(start, end);
    emit visibleRangeChanged(start, end);
}

std::pair<QDateTime, QDateTime> InteractiveChart::visibleTimeRange() const
{
    return {m_axisX->min(), m_axisX->max()};
}

void InteractiveChart::updateChart()
{
    if (m_seriesData.empty()) return;
    
    // Auto-adjust Y axis if needed
    if (m_autoYRange) {
        qreal minY = std::numeric_limits<qreal>::max();
        qreal maxY = std::numeric_limits<qreal>::lowest();
        
        for (const auto& [name, series] : m_seriesData) {
            if (!series.visible) continue;
            for (const auto& pt : series.data) {
                minY = qMin(minY, pt.y());
                maxY = qMax(maxY, pt.y());
            }
        }
        
        if (minY <= maxY) {
            double padding = (maxY - minY) * 0.1;
            if (padding < 1.0) padding = 1.0;
            m_axisY->setRange(qMax(0.0, minY - padding), maxY + padding);
        }
    }
    
    // Update time axis format based on range
    qint64 rangeMs = m_axisX->max().toMSecsSinceEpoch() - m_axisX->min().toMSecsSinceEpoch();
    if (rangeMs < 3600000) {  // < 1 hour
        m_axisX->setFormat("HH:mm:ss");
    } else if (rangeMs < 86400000) {  // < 1 day
        m_axisX->setFormat("HH:mm");
    } else if (rangeMs < 604800000) {  // < 1 week
        m_axisX->setFormat("ddd HH:mm");
    } else {
        m_axisX->setFormat("dd/MM HH:mm");
    }
}

// ==================== Mouse Events ====================

void InteractiveChart::resizeEvent(QResizeEvent* event)
{
    QWidget::resizeEvent(event);
}

void InteractiveChart::mousePressEvent(QMouseEvent* event)
{
    if (event->button() == Qt::LeftButton) {
        m_lastMousePos = event->pos();
        
        if (event->modifiers() & Qt::ControlModifier && m_selectionEnabled) {
            // Start rubber band selection
            m_isSelecting = true;
            m_selectionStart = event->pos();
            m_rubberBand->setGeometry(QRect(m_selectionStart, QSize()));
            m_rubberBand->show();
        } else if (m_panEnabled) {
            // Start panning
            m_isPanning = true;
            setCursor(Qt::ClosedHandCursor);
        }
    }
    
    QWidget::mousePressEvent(event);
}

void InteractiveChart::mouseMoveEvent(QMouseEvent* event)
{
    if (m_isPanning && m_panEnabled) {
        QPoint delta = event->pos() - m_lastMousePos;
        
        // Calculate pan amount in data coordinates
        QRectF plotArea = m_chart->plotArea();
        qreal xRatio = delta.x() / plotArea.width();
        qreal yRatio = delta.y() / plotArea.height();
        
        qint64 xRange = m_axisX->max().toMSecsSinceEpoch() - m_axisX->min().toMSecsSinceEpoch();
        qreal yRange = m_axisY->max() - m_axisY->min();
        
        qint64 xDelta = static_cast<qint64>(-xRatio * xRange);
        qreal yDelta = yRatio * yRange;
        
        m_axisX->setRange(
            QDateTime::fromMSecsSinceEpoch(m_axisX->min().toMSecsSinceEpoch() + xDelta),
            QDateTime::fromMSecsSinceEpoch(m_axisX->max().toMSecsSinceEpoch() + xDelta)
        );
        m_axisY->setRange(m_axisY->min() + yDelta, m_axisY->max() + yDelta);
        
        m_lastMousePos = event->pos();
    }
    else if (m_isSelecting) {
        m_rubberBand->setGeometry(QRect(m_selectionStart, event->pos()).normalized());
    }
    else if (m_crosshairEnabled) {
        updateCrosshair(event->pos());
    }
    
    QWidget::mouseMoveEvent(event);
}

void InteractiveChart::mouseReleaseEvent(QMouseEvent* event)
{
    if (m_isPanning) {
        m_isPanning = false;
        setCursor(Qt::ArrowCursor);
        pushZoomState();
        emit visibleRangeChanged(m_axisX->min(), m_axisX->max());
    }
    else if (m_isSelecting) {
        m_isSelecting = false;
        m_rubberBand->hide();
        
        QRect selection = m_rubberBand->geometry();
        if (selection.width() > 10 && selection.height() > 10) {
            // Zoom to selection
            QPointF topLeft = chartToValue(selection.topLeft());
            QPointF bottomRight = chartToValue(selection.bottomRight());
            
            pushZoomState();
            
            m_axisX->setRange(
                QDateTime::fromMSecsSinceEpoch(static_cast<qint64>(qMin(topLeft.x(), bottomRight.x()))),
                QDateTime::fromMSecsSinceEpoch(static_cast<qint64>(qMax(topLeft.x(), bottomRight.x())))
            );
            m_axisY->setRange(qMin(topLeft.y(), bottomRight.y()), qMax(topLeft.y(), bottomRight.y()));
            
            emit visibleRangeChanged(m_axisX->min(), m_axisX->max());
        }
    }
    
    QWidget::mouseReleaseEvent(event);
}

void InteractiveChart::wheelEvent(QWheelEvent* event)
{
    if (!m_zoomEnabled) return;
    
    qreal factor = event->angleDelta().y() > 0 ? 0.8 : 1.25;
    
    // Get cursor position in chart coordinates
    QPointF cursorPos = chartToValue(event->position().toPoint());
    
    // Calculate new ranges
    qint64 xRange = m_axisX->max().toMSecsSinceEpoch() - m_axisX->min().toMSecsSinceEpoch();
    qreal yRange = m_axisY->max() - m_axisY->min();
    
    qint64 newXRange = static_cast<qint64>(xRange * factor);
    qreal newYRange = yRange * factor;
    
    // Zoom centered on cursor
    qreal xRatio = (cursorPos.x() - m_axisX->min().toMSecsSinceEpoch()) / xRange;
    qreal yRatio = (cursorPos.y() - m_axisY->min()) / yRange;
    
    qint64 newXMin = static_cast<qint64>(cursorPos.x() - xRatio * newXRange);
    qint64 newXMax = static_cast<qint64>(cursorPos.x() + (1 - xRatio) * newXRange);
    qreal newYMin = cursorPos.y() - yRatio * newYRange;
    qreal newYMax = cursorPos.y() + (1 - yRatio) * newYRange;
    
    m_axisX->setRange(
        QDateTime::fromMSecsSinceEpoch(newXMin),
        QDateTime::fromMSecsSinceEpoch(newXMax)
    );
    m_axisY->setRange(newYMin, newYMax);
    
    pushZoomState();
    emit visibleRangeChanged(m_axisX->min(), m_axisX->max());
    
    event->accept();
}

void InteractiveChart::leaveEvent(QEvent* event)
{
    hideCrosshair();
    QWidget::leaveEvent(event);
}

void InteractiveChart::contextMenuEvent(QContextMenuEvent* event)
{
    m_contextMenu->exec(event->globalPos());
}

// ==================== Private Methods ====================

void InteractiveChart::updateCrosshair(const QPoint& pos)
{
    QRectF plotArea = m_chart->plotArea();
    
    if (!plotArea.contains(pos)) {
        hideCrosshair();
        return;
    }
    
    // Get value at cursor
    QPointF value = chartToValue(pos);
    QDateTime time = QDateTime::fromMSecsSinceEpoch(static_cast<qint64>(value.x()));
    
    // Draw crosshair lines
    m_crosshairH->setLine(plotArea.left(), pos.y(), plotArea.right(), pos.y());
    m_crosshairV->setLine(pos.x(), plotArea.top(), pos.x(), plotArea.bottom());
    m_crosshairH->setVisible(true);
    m_crosshairV->setVisible(true);
    
    // Update label
    QString text = QString("%1\n%2%3")
        .arg(formatDateTime(time))
        .arg(value.y(), 0, 'f', 1)
        .arg(m_valueSuffix);
    
    m_crosshairLabel->setPlainText(text);
    
    // Position label
    qreal labelX = pos.x() + 10;
    qreal labelY = pos.y() - 30;
    
    // Keep label in view
    if (labelX + m_crosshairLabel->boundingRect().width() > plotArea.right()) {
        labelX = pos.x() - m_crosshairLabel->boundingRect().width() - 10;
    }
    if (labelY < plotArea.top()) {
        labelY = pos.y() + 10;
    }
    
    m_crosshairLabel->setPos(labelX, labelY);
    m_crosshairLabel->setVisible(true);
}

void InteractiveChart::hideCrosshair()
{
    if (m_crosshairH) m_crosshairH->setVisible(false);
    if (m_crosshairV) m_crosshairV->setVisible(false);
    if (m_crosshairLabel) m_crosshairLabel->setVisible(false);
}

void InteractiveChart::pushZoomState()
{
    ZoomState state = currentZoomState();
    
    // Remove states after current position (for redo)
    if (m_zoomHistoryIndex < static_cast<int>(m_zoomHistory.size()) - 1) {
        m_zoomHistory.erase(m_zoomHistory.begin() + m_zoomHistoryIndex + 1, m_zoomHistory.end());
    }
    
    m_zoomHistory.push_back(state);
    
    if (m_zoomHistory.size() > MAX_ZOOM_HISTORY) {
        m_zoomHistory.erase(m_zoomHistory.begin());
    }
    
    m_zoomHistoryIndex = static_cast<int>(m_zoomHistory.size()) - 1;
}

void InteractiveChart::applyZoomState(const ZoomState& state)
{
    m_axisX->setRange(state.minTime, state.maxTime);
    m_axisY->setRange(state.minValue, state.maxValue);
    emit visibleRangeChanged(state.minTime, state.maxTime);
}

ZoomState InteractiveChart::currentZoomState() const
{
    return {m_axisX->min(), m_axisX->max(), m_axisY->min(), m_axisY->max()};
}

QPointF InteractiveChart::chartToValue(const QPoint& pos) const
{
    QRectF plotArea = m_chart->plotArea();
    
    qreal xRatio = (pos.x() - plotArea.left()) / plotArea.width();
    qreal yRatio = 1.0 - (pos.y() - plotArea.top()) / plotArea.height();
    
    qint64 xRange = m_axisX->max().toMSecsSinceEpoch() - m_axisX->min().toMSecsSinceEpoch();
    qreal yRange = m_axisY->max() - m_axisY->min();
    
    qreal x = m_axisX->min().toMSecsSinceEpoch() + xRatio * xRange;
    qreal y = m_axisY->min() + yRatio * yRange;
    
    return QPointF(x, y);
}

QString InteractiveChart::formatDateTime(const QDateTime& dt) const
{
    return dt.toString("dd/MM/yyyy HH:mm:ss");
}

QString InteractiveChart::formatValue(double value) const
{
    return QString::number(value, 'f', 1) + m_valueSuffix;
}

void InteractiveChart::createContextMenu()
{
    m_contextMenu = std::make_unique<QMenu>(this);
    
    m_contextMenu->addAction(tr("Reset Zoom"), this, &InteractiveChart::resetZoom);
    m_contextMenu->addAction(tr("Undo Zoom"), this, &InteractiveChart::undoZoom);
    m_contextMenu->addAction(tr("Redo Zoom"), this, &InteractiveChart::redoZoom);
    m_contextMenu->addSeparator();
    m_contextMenu->addAction(tr("Copy to Clipboard"), this, &InteractiveChart::copyToClipboard);
    m_contextMenu->addAction(tr("Export as Image..."), this, [this]() {
        QString path = QFileDialog::getSaveFileName(this, tr("Export Chart"),
            QString(), tr("PNG Image (*.png);;JPEG Image (*.jpg)"));
        if (!path.isEmpty()) {
            exportToImage(path);
        }
    });
    m_contextMenu->addAction(tr("Export as PDF..."), this, [this]() {
        QString path = QFileDialog::getSaveFileName(this, tr("Export Chart"),
            QString(), tr("PDF Document (*.pdf)"));
        if (!path.isEmpty()) {
            exportToPdf(path);
        }
    });
}

void InteractiveChart::onSeriesHovered(const QPointF& point, bool state)
{
    if (state) {
        QLineSeries* series = qobject_cast<QLineSeries*>(sender());
        if (series) {
            emit dataPointHovered(series->name(),
                QDateTime::fromMSecsSinceEpoch(static_cast<qint64>(point.x())),
                point.y());
        }
    }
}

void InteractiveChart::onSeriesClicked(const QPointF& point)
{
    QLineSeries* series = qobject_cast<QLineSeries*>(sender());
    if (series) {
        emit dataPointClicked(series->name(),
            QDateTime::fromMSecsSinceEpoch(static_cast<qint64>(point.x())),
            point.y());
    }
}

// ==================== Export Methods ====================

bool InteractiveChart::exportToImage(const QString& filePath, int width, int height)
{
    QPixmap pixmap(width, height);
    pixmap.fill(m_darkTheme ? QColor(30, 30, 30) : Qt::white);
    
    QPainter painter(&pixmap);
    painter.setRenderHint(QPainter::Antialiasing);
    m_chartView->render(&painter);
    
    return pixmap.save(filePath);
}

bool InteractiveChart::exportToPdf(const QString& filePath)
{
    QPdfWriter writer(filePath);
    writer.setPageSize(QPageSize(QPageSize::A4));
    writer.setPageOrientation(QPageLayout::Landscape);
    
    QPainter painter(&writer);
    m_chartView->render(&painter);
    
    return true;
}

void InteractiveChart::copyToClipboard()
{
    QPixmap pixmap(m_chartView->size());
    pixmap.fill(m_darkTheme ? QColor(30, 30, 30) : Qt::white);
    
    QPainter painter(&pixmap);
    painter.setRenderHint(QPainter::Antialiasing);
    m_chartView->render(&painter);
    
    QApplication::clipboard()->setPixmap(pixmap);
}
