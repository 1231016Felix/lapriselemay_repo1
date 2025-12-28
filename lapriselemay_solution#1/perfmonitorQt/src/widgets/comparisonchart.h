#pragma once

#include <QWidget>
#include <QLabel>
#include <QColor>
#include <QPointF>
#include <QChart>
#include <QChartView>
#include <QLineSeries>
#include <QAreaSeries>
#include <QDateTimeAxis>
#include <QValueAxis>
#include <QLegend>
#include <QDateTime>
#include <vector>
#include <memory>

/**
 * @brief Chart widget for comparing two time periods
 * 
 * Features:
 * - Overlay mode: Shows both periods on the same time axis (offset adjusted)
 * - Side-by-side mode: Shows two charts vertically
 * - Difference highlighting (green = improvement, red = regression)
 * - Statistics summary (average, min, max, % change)
 * - Interactive tooltips showing values from both periods
 */
class ComparisonChart : public QWidget
{
    Q_OBJECT

public:
    enum class ComparisonMode {
        Overlay,        // Both periods superimposed
        SideBySide,     // Two charts stacked vertically
        Difference      // Show the difference between periods
    };
    Q_ENUM(ComparisonMode)

    struct PeriodData {
        QString name;                      // e.g., "Today", "Yesterday"
        QDateTime startTime;
        QDateTime endTime;
        std::vector<QPointF> data;         // x = offset from period start (ms), y = value
        QColor color;
        
        // Statistics (calculated)
        double minValue{0.0};
        double maxValue{0.0};
        double avgValue{0.0};
        int sampleCount{0};
    };

    struct ComparisonStats {
        double period1Avg{0.0};
        double period2Avg{0.0};
        double avgDifference{0.0};
        double avgDifferencePercent{0.0};
        QString verdict;        // "Better", "Worse", "Similar"
        QColor verdictColor;
    };

    explicit ComparisonChart(QWidget* parent = nullptr);
    ~ComparisonChart() override;

    // Set data for comparison
    void setPeriod1(const QString& name, const QDateTime& start, const QDateTime& end,
                    const std::vector<QPointF>& data, const QColor& color = QColor(0, 120, 215));
    void setPeriod2(const QString& name, const QDateTime& start, const QDateTime& end,
                    const std::vector<QPointF>& data, const QColor& color = QColor(255, 127, 14));
    
    // Clear data
    void clear();
    
    // Configuration
    void setComparisonMode(ComparisonMode mode);
    ComparisonMode comparisonMode() const { return m_mode; }
    
    void setTitle(const QString& title);
    void setYAxisTitle(const QString& title);
    void setValueSuffix(const QString& suffix) { m_valueSuffix = suffix; }
    
    void setYAxisRange(double min, double max);
    void setAutoYAxisRange(bool autoRange = true);
    
    void setHigherIsBetter(bool better) { m_higherIsBetter = better; }
    void setShowStatistics(bool show);
    void setShowDifferenceArea(bool show);
    void setDarkTheme(bool dark);
    
    // Get statistics
    ComparisonStats getStatistics() const;
    
    // Export
    bool exportToImage(const QString& path, int width = 1200, int height = 800);
    void copyToClipboard();

signals:
    void dataPointHovered(const QString& period, const QDateTime& time, double value);

protected:
    void resizeEvent(QResizeEvent* event) override;

private slots:
    void onSeries1Hovered(const QPointF& point, bool state);
    void onSeries2Hovered(const QPointF& point, bool state);

private:
    void setupChart();
    void updateChart();
    void updateOverlayChart();
    void updateSideBySideChart();
    void updateDifferenceChart();
    void calculateStatistics();
    void updateStatisticsDisplay();
    void applyTheme();
    QString formatValue(double value) const;
    
    // Chart components
    QChart* m_chart1{nullptr};           // Main chart (or period 1 in side-by-side)
    QChart* m_chart2{nullptr};           // Period 2 chart (side-by-side only)
    QChartView* m_chartView1{nullptr};
    QChartView* m_chartView2{nullptr};
    
    QLineSeries* m_series1{nullptr};
    QLineSeries* m_series2{nullptr};
    QAreaSeries* m_diffArea{nullptr};    // For difference highlighting
    
    QDateTimeAxis* m_axisX1{nullptr};
    QValueAxis* m_axisY1{nullptr};
    QDateTimeAxis* m_axisX2{nullptr};
    QValueAxis* m_axisY2{nullptr};
    
    // Data
    PeriodData m_period1;
    PeriodData m_period2;
    ComparisonStats m_stats;
    
    // Statistics display
    QWidget* m_statsWidget{nullptr};
    QLabel* m_period1StatsLabel{nullptr};
    QLabel* m_period2StatsLabel{nullptr};
    QLabel* m_verdictLabel{nullptr};
    
    // Configuration
    ComparisonMode m_mode{ComparisonMode::Overlay};
    QString m_title;
    QString m_yAxisTitle;
    QString m_valueSuffix;
    bool m_autoYRange{true};
    double m_yMin{0.0};
    double m_yMax{100.0};
    bool m_higherIsBetter{false};  // For metrics like battery %, higher is better
    bool m_showStatistics{true};
    bool m_showDifferenceArea{true};
    bool m_darkTheme{true};
};
