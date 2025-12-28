#pragma once

#include <QWidget>
#include <QChart>
#include <QChartView>
#include <QLineSeries>
#include <QAreaSeries>
#include <QDateTimeAxis>
#include <QValueAxis>
#include <QRubberBand>
#include <QMenu>
#include <QDateTime>
#include <vector>
#include <memory>

/**
 * @brief Data series for the chart
 */
struct ChartSeries {
    QString name;
    QColor color;
    std::vector<QPointF> data;  // x = timestamp (msecs since epoch), y = value
    bool visible{true};
    bool showArea{false};       // Fill area under line
    double lineWidth{2.0};
};

/**
 * @brief Zoom state for undo/redo
 */
struct ZoomState {
    QDateTime minTime;
    QDateTime maxTime;
    double minValue;
    double maxValue;
};

/**
 * @brief Interactive chart widget with zoom, pan, and selection
 * 
 * Features:
 * - Mouse wheel zoom
 * - Click and drag to pan
 * - Rubber band selection for zoom area
 * - Right-click context menu
 * - Zoom history with undo/redo
 * - Crosshair cursor with value display
 * - Multiple data series support
 * - Time range selection
 */
class InteractiveChart : public QWidget
{
    Q_OBJECT

public:
    explicit InteractiveChart(QWidget* parent = nullptr);
    ~InteractiveChart() override;

    // ==================== Data Management ====================
    
    /// Clear all data
    void clear();
    
    /// Add a data series
    void addSeries(const QString& name, const QColor& color, 
                   const std::vector<QPointF>& data, bool showArea = false);
    
    /// Update data for a series
    void updateSeries(const QString& name, const std::vector<QPointF>& data);
    
    /// Remove a series
    void removeSeries(const QString& name);
    
    /// Set series visibility
    void setSeriesVisible(const QString& name, bool visible);
    
    /// Get all series names
    QStringList seriesNames() const;
    
    // ==================== Appearance ====================
    
    /// Set chart title
    void setTitle(const QString& title);
    
    /// Set axis titles
    void setAxisTitles(const QString& xTitle, const QString& yTitle);
    
    /// Set Y-axis range (auto if not set)
    void setYAxisRange(double min, double max);
    void setAutoYAxisRange(bool autoRange = true);
    
    /// Set value suffix (e.g., "%", " MB")
    void setValueSuffix(const QString& suffix) { m_valueSuffix = suffix; }
    
    /// Enable/disable grid
    void setGridVisible(bool visible);
    
    /// Enable/disable legend
    void setLegendVisible(bool visible);
    
    /// Set theme (dark/light)
    void setDarkTheme(bool dark);
    
    /// Set comparison mode (show difference between two periods)
    void setComparisonMode(bool enabled, const QString& period1Name = QString(), 
                           const QString& period2Name = QString());
    
    // ==================== Interaction ====================
    
    /// Enable/disable zoom
    void setZoomEnabled(bool enabled) { m_zoomEnabled = enabled; }
    
    /// Enable/disable pan
    void setPanEnabled(bool enabled) { m_panEnabled = enabled; }
    
    /// Enable/disable rubber band selection
    void setSelectionEnabled(bool enabled) { m_selectionEnabled = enabled; }
    
    /// Enable/disable crosshair
    void setCrosshairEnabled(bool enabled);
    
    /// Reset zoom to show all data
    void resetZoom();
    
    /// Undo last zoom
    void undoZoom();
    
    /// Redo zoom
    void redoZoom();
    
    /// Zoom to time range
    void zoomToTimeRange(const QDateTime& start, const QDateTime& end);
    
    /// Get current visible time range
    std::pair<QDateTime, QDateTime> visibleTimeRange() const;
    
    // ==================== Export ====================
    
    /// Export chart as image
    bool exportToImage(const QString& filePath, int width = 1920, int height = 1080);
    
    /// Export chart as PDF
    bool exportToPdf(const QString& filePath);
    
    /// Copy chart to clipboard
    void copyToClipboard();

signals:
    /// Emitted when user selects a time range
    void timeRangeSelected(const QDateTime& start, const QDateTime& end);
    
    /// Emitted when user clicks on a data point
    void dataPointClicked(const QString& seriesName, const QDateTime& time, double value);
    
    /// Emitted when visible range changes
    void visibleRangeChanged(const QDateTime& start, const QDateTime& end);
    
    /// Emitted when user hovers over data
    void dataPointHovered(const QString& seriesName, const QDateTime& time, double value);

protected:
    void resizeEvent(QResizeEvent* event) override;
    void mousePressEvent(QMouseEvent* event) override;
    void mouseMoveEvent(QMouseEvent* event) override;
    void mouseReleaseEvent(QMouseEvent* event) override;
    void wheelEvent(QWheelEvent* event) override;
    void leaveEvent(QEvent* event) override;
    void contextMenuEvent(QContextMenuEvent* event) override;

private slots:
    void onSeriesHovered(const QPointF& point, bool state);
    void onSeriesClicked(const QPointF& point);

private:
    void setupChart();
    void setupAxes();
    void updateChart();
    void updateCrosshair(const QPoint& pos);
    void hideCrosshair();
    void pushZoomState();
    void applyZoomState(const ZoomState& state);
    ZoomState currentZoomState() const;
    QPointF chartToValue(const QPoint& pos) const;
    QString formatDateTime(const QDateTime& dt) const;
    QString formatValue(double value) const;
    void createContextMenu();
    
    // Chart components
    QChart* m_chart{nullptr};
    QChartView* m_chartView{nullptr};
    QDateTimeAxis* m_axisX{nullptr};
    QValueAxis* m_axisY{nullptr};
    
    // Data series
    std::map<QString, ChartSeries> m_seriesData;
    std::map<QString, QLineSeries*> m_lineSeries;
    std::map<QString, QAreaSeries*> m_areaSeries;
    
    // Crosshair
    QGraphicsLineItem* m_crosshairH{nullptr};
    QGraphicsLineItem* m_crosshairV{nullptr};
    QGraphicsTextItem* m_crosshairLabel{nullptr};
    bool m_crosshairEnabled{true};
    
    // Interaction state
    bool m_zoomEnabled{true};
    bool m_panEnabled{true};
    bool m_selectionEnabled{true};
    bool m_isPanning{false};
    bool m_isSelecting{false};
    QPoint m_lastMousePos;
    QPoint m_selectionStart;
    std::unique_ptr<QRubberBand> m_rubberBand;
    
    // Zoom history
    std::vector<ZoomState> m_zoomHistory;
    int m_zoomHistoryIndex{-1};
    static constexpr int MAX_ZOOM_HISTORY = 20;
    
    // Appearance
    QString m_valueSuffix;
    bool m_darkTheme{true};
    bool m_autoYRange{true};
    double m_yMin{0.0};
    double m_yMax{100.0};
    
    // Context menu
    std::unique_ptr<QMenu> m_contextMenu;
};
