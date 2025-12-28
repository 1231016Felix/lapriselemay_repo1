#pragma once

#include <QDialog>
#include <QComboBox>
#include <QDateTimeEdit>
#include <QPushButton>
#include <QCheckBox>
#include <QLabel>
#include <QTabWidget>
#include <QTableWidget>
#include <QGroupBox>
#include <QSplitter>
#include <memory>

#include "../database/metricshistory.h"

class InteractiveChart;
class MetricsHistory;

/**
 * @brief Dialog for viewing historical metrics data
 * 
 * Features:
 * - Time range selection (presets and custom)
 * - Multiple metric type selection
 * - Interactive chart with zoom/pan
 * - Period comparison (today vs yesterday, this week vs last week)
 * - Data export to CSV/JSON
 * - Statistics summary
 */
class HistoryDialog : public QDialog
{
    Q_OBJECT

public:
    explicit HistoryDialog(MetricsHistory* history, QWidget* parent = nullptr);
    ~HistoryDialog() override;

    /// Set default metric type to display
    void setDefaultMetric(MetricType type);
    
    /// Set default time range
    void setDefaultTimeRange(TimeRange range);

private slots:
    void onTimeRangeChanged();
    void onMetricSelectionChanged();
    void onRefreshClicked();
    void onExportClicked();
    void onCompareClicked();
    void onChartTimeRangeSelected(const QDateTime& start, const QDateTime& end);
    void updateStatistics();
    void updateComparisonTable();

private:
    void setupUi();
    void createToolbar();
    void createChartArea();
    void createStatisticsPanel();
    void createComparisonTab();
    void loadData();
    void updateChart();
    QString formatValue(MetricType type, double value) const;
    QString getMetricUnit(MetricType type) const;
    QColor getMetricColor(MetricType type) const;

    MetricsHistory* m_history{nullptr};
    
    // Toolbar widgets
    QComboBox* m_timeRangeCombo{nullptr};
    QDateTimeEdit* m_startDateEdit{nullptr};
    QDateTimeEdit* m_endDateEdit{nullptr};
    QPushButton* m_refreshButton{nullptr};
    QPushButton* m_exportButton{nullptr};
    QPushButton* m_compareButton{nullptr};
    
    // Metric selection
    QGroupBox* m_metricGroup{nullptr};
    std::map<MetricType, QCheckBox*> m_metricChecks;
    
    // Main content
    QTabWidget* m_tabWidget{nullptr};
    InteractiveChart* m_chart{nullptr};
    
    // Statistics panel
    QLabel* m_statsMinLabel{nullptr};
    QLabel* m_statsMaxLabel{nullptr};
    QLabel* m_statsAvgLabel{nullptr};
    QLabel* m_statsSamplesLabel{nullptr};
    QLabel* m_statsTimeRangeLabel{nullptr};
    
    // Comparison tab
    QTableWidget* m_comparisonTable{nullptr};
    QComboBox* m_comparisonTypeCombo{nullptr};
    
    // State
    TimeRange m_currentTimeRange{TimeRange::Last24Hours};
    QDateTime m_customStart;
    QDateTime m_customEnd;
    std::vector<MetricType> m_selectedMetrics;
};


/**
 * @brief Dialog for exporting metrics data
 */
class ExportDialog : public QDialog
{
    Q_OBJECT

public:
    explicit ExportDialog(MetricsHistory* history, QWidget* parent = nullptr);
    
    void setTimeRange(const QDateTime& start, const QDateTime& end);
    void setSelectedMetrics(const std::vector<MetricType>& metrics);

private slots:
    void onBrowseClicked();
    void onExportClicked();
    void updatePreview();

private:
    void setupUi();

    MetricsHistory* m_history{nullptr};
    
    QDateTimeEdit* m_startDateEdit{nullptr};
    QDateTimeEdit* m_endDateEdit{nullptr};
    QComboBox* m_formatCombo{nullptr};
    QLineEdit* m_pathEdit{nullptr};
    QPushButton* m_browseButton{nullptr};
    QTableWidget* m_previewTable{nullptr};
    QLabel* m_infoLabel{nullptr};
    std::map<MetricType, QCheckBox*> m_metricChecks;
};
