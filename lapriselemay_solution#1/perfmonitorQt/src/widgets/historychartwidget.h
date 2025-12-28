#pragma once

#include <QWidget>
#include <QChartView>
#include <QChart>
#include <QLineSeries>
#include <QAreaSeries>
#include <QDateTimeAxis>
#include <QValueAxis>
#include <QDateTime>
#include <QRubberBand>
#include <QMenu>
#include <memory>
#include <vector>

#include "database/metricshistory.h"

/**
 * @brief Display mode for the chart
 */
enum class ChartDisplayMode {
    Line,           // Simple line chart
    Area,           // Filled area chart
    MinMaxAvg,      // Shows min, max, and average
    Comparison      // Two periods overlaid
};

/**
 * @brief Series styling options
 */
struct SeriesStyle {
    QColor lineColor{0, 120, 215};
    QColor fillColor{0, 120, 215, 80};
    int lineWidth{2};
    bool showPoints{false};
    int pointSize{4};
};

/**
 * @brief Interactive chart widget with zoom, pan, and selection
 * 
 * Features:
 * - Mouse wheel zoom (centered on cursor)
 * - Click and drag to pan
 * - Rubber band selection for zoom to region
 * - Right-click context menu
 * - Tooltip on hover
 * - Multiple series support
 * - Comparison mode (overlay two time periods)
 */
class InteractiveChartWidget : public QChartView
{
    Q_OBJECT

public:
    explicit InteractiveChartWidget(QWidget *parent = nullptr);
    ~InteractiveChartWidget() override;

    /// Set the data source
    void setMetricsHistory(MetricsHistory* history) { m_metricsHistory = history; }
    
    /// Load and display data for a metric
    void loadMetricData(MetricType type, const QDateTime& from, const QDateTime& to,
                        const QString& label = QString());
    
    /// Load data using predefined time range
    void loadMetricData(MetricType type, TimeRange range, const QString& label = QString());
    
    /// Add a series to the chart (for multi-series display)
    void addSeries(const QString& name, const std::vector<MetricDataPoint>& data,
                   const SeriesStyle& style = SeriesStyle());
    
    /// Clear all series
    void clearSeries();
    
    /// Set chart title
    void setChartTitle(const QString& title);
    
    /// Set Y-axis range (auto if not set)
    void setYAxisRange(double min, double max);
    void setAutoYAxisRange(bool autoRange = true);
    
    /// Set display mode
    void setDisplayMode(ChartDisplayMode mode);
    ChartDisplayMode displayMode() const { return m_displayMode; }
    
    /// Comparison mode: overlay two time periods
    void setComparisonMode(bool enabled);
    void setComparisonPeriods(const QDateTime& period1Start, const QDateTime& period1End,
                              const QDateTime& period2Start, const QDateTime& period2End);
    
    /// Zoom controls
    void zoomIn();
    void zoomOut();
    void resetZoom();
    void zoomToRange(const QDateTime& from, const QDateTime& to);
    
    /// Get current visible range
    std::pair<QDateTime, QDateTime> visibleTimeRange() const;
    
    /// Export chart as image
    bool exportToImage(const QString& filePath, int width = 1920, int height = 1080);

signals:
    void timeRangeChanged(const QDateTime& from, const QDateTime& to);
    void pointHovered(const QDateTime& time, double value);
    void pointClicked(const QDateTime& time, double value);
    void zoomChanged(double zoomLevel);

protected:
    void mousePressEvent(QMouseEvent *event) override;
    void mouseMoveEvent(QMouseEvent *event) override;
    void mouseReleaseEvent(QMouseEvent *event) override;
    void wheelEvent(QWheelEvent *event) override;
    void contextMenuEvent(QContextMenuEvent *event) override;
    void resizeEvent(QResizeEvent *event) override;

private slots:
    void onSeriesHovered(const QPointF &point, bool state);
    void onSeriesClicked(const QPointF &point);

private:
    void setupChart();
    void setupAxes();
    void createContextMenu();
    void updateAxes();
    void applyStyle(QLineSeries* series, const SeriesStyle& style);
    QString formatTooltip(const QDateTime& time, double value);
    
    // Chart components
    QChart* m_chart{nullptr};
    QDateTimeAxis* m_axisX{nullptr};
    QValueAxis* m_axisY{nullptr};
    
    // Data source
    MetricsHistory* m_metricsHistory{nullptr};
    
    // Current data
    MetricType m_currentMetricType{MetricType::CpuUsage};
    std::vector<MetricDataPoint> m_currentData;
    QDateTime m_dataFrom;
    QDateTime m_dataTo;
    
    // Display settings
    ChartDisplayMode m_displayMode{ChartDisplayMode::Area};
    bool m_autoYRange{true};
    double m_yMin{0.0};
    double m_yMax{100.0};
    
    // Comparison mode
    bool m_comparisonMode{false};
    QDateTime m_period1Start, m_period1End;
    QDateTime m_period2Start, m_period2End;
    
    // Interaction state
    bool m_isPanning{false};
    bool m_isSelecting{false};
    QPoint m_lastMousePos;
    QRubberBand* m_rubberBand{nullptr};
    QPoint m_rubberBandOrigin;
    
    // Zoom state
    double m_zoomLevel{1.0};
    QDateTime m_originalFrom;
    QDateTime m_originalTo;
    
    // Context menu
    QMenu* m_contextMenu{nullptr};
    
    // Tooltip
    QGraphicsTextItem* m_tooltip{nullptr};
};

/**
 * @brief Widget for selecting time range with presets and custom date pickers
 */
class TimeRangeSelector : public QWidget
{
    Q_OBJECT

public:
    explicit TimeRangeSelector(QWidget *parent = nullptr);
    
    void setTimeRange(TimeRange range);
    void setCustomRange(const QDateTime& from, const QDateTime& to);
    
    TimeRange currentRange() const { return m_currentRange; }
    std::pair<QDateTime, QDateTime> customRange() const;

signals:
    void rangeChanged(TimeRange range);
    void customRangeChanged(const QDateTime& from, const QDateTime& to);

private slots:
    void onPresetClicked();
    void onCustomRangeApplied();

private:
    void setupUi();
    
    TimeRange m_currentRange{TimeRange::Last24Hours};
    class QButtonGroup* m_presetGroup{nullptr};
    class QDateTimeEdit* m_fromEdit{nullptr};
    class QDateTimeEdit* m_toEdit{nullptr};
};

/**
 * @brief Comparison widget showing two periods side by side
 */
class PeriodComparisonWidget : public QWidget
{
    Q_OBJECT

public:
    explicit PeriodComparisonWidget(QWidget *parent = nullptr);
    
    void setComparison(const PeriodComparison& comparison);
    void clear();

private:
    void setupUi();
    QString formatValue(double value, MetricType type);
    QString formatDifference(double diff, double percent);
    
    QLabel* m_metricLabel{nullptr};
    QLabel* m_period1Label{nullptr};
    QLabel* m_period2Label{nullptr};
    QLabel* m_period1Value{nullptr};
    QLabel* m_period2Value{nullptr};
    QLabel* m_differenceLabel{nullptr};
    QProgressBar* m_period1Bar{nullptr};
    QProgressBar* m_period2Bar{nullptr};
};
