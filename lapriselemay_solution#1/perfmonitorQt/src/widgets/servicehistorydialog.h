#pragma once

#include <QDialog>
#include <QComboBox>
#include <QTableWidget>
#include <QPushButton>
#include <QLabel>
#include <QTabWidget>
#include <QSplitter>
#include <memory>

#include "../database/servicehistory.h"

class InteractiveChart;
class ComparisonChart;
class TimeRangeSelector;
class ServiceHistoryManager;

/**
 * @brief Dialog for viewing service history and analytics
 * 
 * Features:
 * - Resource usage graphs (CPU, Memory) over time
 * - Service availability statistics
 * - Crash history timeline
 * - Period comparison (today vs yesterday, etc.)
 * - Top services by resource usage
 * - Export to CSV/JSON
 */
class ServiceHistoryDialog : public QDialog
{
    Q_OBJECT

public:
    explicit ServiceHistoryDialog(QWidget* parent = nullptr);
    ~ServiceHistoryDialog() override;

    /// Set the service to display initially
    void setService(const QString& serviceName);

private slots:
    void onServiceChanged(int index);
    void onTimeRangeChanged(const QDateTime& start, const QDateTime& end);
    void onRefreshRequested();
    void onExportClicked();
    void onCompareClicked();
    void updateCharts();
    void updateStatistics();
    void updateCrashTable();
    void updateTopServicesTable();
    void updateComparisonChart();

private:
    void setupUi();
    void createToolbar();
    void createChartsTab();
    void createStatisticsTab();
    void createCrashHistoryTab();
    void createComparisonTab();
    void createTopServicesTab();
    void loadServices();
    QString formatBytes(qint64 bytes) const;
    QString formatDuration(qint64 seconds) const;

    std::unique_ptr<ServiceHistoryManager> m_historyManager;
    
    // Toolbar
    QComboBox* m_serviceCombo{nullptr};
    TimeRangeSelector* m_timeRangeSelector{nullptr};
    QPushButton* m_exportButton{nullptr};
    
    // Tabs
    QTabWidget* m_tabWidget{nullptr};
    
    // Charts tab
    InteractiveChart* m_cpuChart{nullptr};
    InteractiveChart* m_memoryChart{nullptr};
    
    // Statistics tab
    QLabel* m_totalSamplesLabel{nullptr};
    QLabel* m_availabilityLabel{nullptr};
    QLabel* m_avgCpuLabel{nullptr};
    QLabel* m_maxCpuLabel{nullptr};
    QLabel* m_avgMemoryLabel{nullptr};
    QLabel* m_maxMemoryLabel{nullptr};
    QLabel* m_crashCountLabel{nullptr};
    QLabel* m_uptimeLabel{nullptr};
    
    // Crash history tab
    QTableWidget* m_crashTable{nullptr};
    
    // Comparison tab
    ComparisonChart* m_comparisonChart{nullptr};
    QComboBox* m_comparisonTypeCombo{nullptr};
    QComboBox* m_comparisonMetricCombo{nullptr};
    
    // Top services tab
    QTableWidget* m_topCpuTable{nullptr};
    QTableWidget* m_topMemoryTable{nullptr};
    QTableWidget* m_topCrashTable{nullptr};
    
    // Status
    QLabel* m_statusLabel{nullptr};
    
    // Current selection
    QString m_currentService;
    QDateTime m_startTime;
    QDateTime m_endTime;
};
