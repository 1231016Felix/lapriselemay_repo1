#pragma once

#include <QDialog>
#include <QTableView>
#include <QSortFilterProxyModel>
#include <QAbstractTableModel>
#include <QComboBox>
#include <QLineEdit>
#include <QCheckBox>
#include <QLabel>
#include <QPushButton>
#include <QSplitter>
#include <QTabWidget>
#include <memory>

#include "../monitors/processimpactmonitor.h"

class QChartView;
class QChart;
class QLineSeries;
class QBarSeries;
class QBarSet;

/**
 * @brief Table model for displaying all process impacts
 */
class ProcessImpactTableModel : public QAbstractTableModel
{
    Q_OBJECT

public:
    enum Column {
        ColName = 0,
        ColPID,
        ColBatteryScore,
        ColCpuAvg,
        ColCpuPeak,
        ColMemoryAvg,
        ColDiskTotal,
        ColDiskRead,
        ColDiskWrite,
        ColActivity,
        ColWakeCount,
        ColOverallScore,
        ColCount
    };

    explicit ProcessImpactTableModel(QObject* parent = nullptr);
    
    void setImpacts(const std::vector<ProcessImpact>& impacts);
    void updateImpacts(const std::vector<ProcessImpact>& impacts);
    const ProcessImpact* getImpact(int row) const;
    const ProcessImpact* getImpactByPid(quint32 pid) const;
    
    int rowCount(const QModelIndex& parent = QModelIndex()) const override;
    int columnCount(const QModelIndex& parent = QModelIndex()) const override;
    QVariant data(const QModelIndex& index, int role = Qt::DisplayRole) const override;
    QVariant headerData(int section, Qt::Orientation orientation, int role) const override;

private:
    std::vector<ProcessImpact> m_impacts;
};

/**
 * @brief Sort/filter proxy for impact table
 */
class ProcessImpactSortFilterProxy : public QSortFilterProxyModel
{
    Q_OBJECT

public:
    explicit ProcessImpactSortFilterProxy(QObject* parent = nullptr);
    
    void setShowSystemProcesses(bool show);
    void setMinimumImpact(double minScore);

protected:
    bool lessThan(const QModelIndex& left, const QModelIndex& right) const override;
    bool filterAcceptsRow(int sourceRow, const QModelIndex& sourceParent) const override;

private:
    bool m_showSystem{false};
    double m_minImpact{0.0};
};

/**
 * @brief Detail panel showing in-depth info for selected process
 */
class ProcessImpactDetailPanel : public QWidget
{
    Q_OBJECT

public:
    explicit ProcessImpactDetailPanel(QWidget* parent = nullptr);
    
    void setImpact(const ProcessImpact& impact);
    void clear();

private:
    void setupUi();
    void updateCharts();
    
    ProcessImpact m_impact;
    
    // Info labels
    QLabel* m_nameLabel{nullptr};
    QLabel* m_pidLabel{nullptr};
    QLabel* m_pathLabel{nullptr};
    QLabel* m_iconLabel{nullptr};
    
    // Score displays
    QWidget* m_scoresWidget{nullptr};
    QLabel* m_batteryScoreLabel{nullptr};
    QLabel* m_diskScoreLabel{nullptr};
    QLabel* m_overallScoreLabel{nullptr};
    
    // Metrics tabs
    QTabWidget* m_metricsTab{nullptr};
    
    // CPU metrics
    QLabel* m_cpuAvgLabel{nullptr};
    QLabel* m_cpuPeakLabel{nullptr};
    QLabel* m_cpuTimeLabel{nullptr};
    QLabel* m_cpuSpikesLabel{nullptr};
    
    // Memory metrics
    QLabel* m_memAvgLabel{nullptr};
    QLabel* m_memPeakLabel{nullptr};
    QLabel* m_memGrowthLabel{nullptr};
    
    // Disk metrics
    QLabel* m_diskReadLabel{nullptr};
    QLabel* m_diskWriteLabel{nullptr};
    QLabel* m_diskReadRateLabel{nullptr};
    QLabel* m_diskWriteRateLabel{nullptr};
    QLabel* m_diskPeakReadLabel{nullptr};
    QLabel* m_diskPeakWriteLabel{nullptr};
    
    // Activity metrics
    QLabel* m_activityLabel{nullptr};
    QLabel* m_wakeCountLabel{nullptr};
    QLabel* m_activeSecsLabel{nullptr};
};

/**
 * @brief Comparison chart widget for processes
 */
class ProcessComparisonChart : public QWidget
{
    Q_OBJECT

public:
    explicit ProcessComparisonChart(QWidget* parent = nullptr);
    
    void setImpacts(const std::vector<ProcessImpact>& impacts, ImpactCategory category);
    void clear();

private:
    void setupUi();
    void updateChart();
    
    std::vector<ProcessImpact> m_impacts;
    ImpactCategory m_category{ImpactCategory::BatteryDrain};
    
    QChartView* m_chartView{nullptr};
};

/**
 * @brief Main dialog for detailed process impact analysis
 */
class ProcessImpactDialog : public QDialog
{
    Q_OBJECT

public:
    explicit ProcessImpactDialog(QWidget* parent = nullptr);
    ~ProcessImpactDialog() override;

    void refresh();
    void setCategory(ImpactCategory category);

protected:
    void showEvent(QShowEvent* event) override;
    void closeEvent(QCloseEvent* event) override;

private slots:
    void onMonitorUpdated();
    void onCategoryChanged(int index);
    void onFilterChanged(const QString& text);
    void onShowSystemChanged(int state);
    void onTableSelectionChanged();
    void onExportClicked();

private:
    void setupUi();
    void updateTable();
    void updateDetailPanel();
    void updateComparisonChart();
    void saveSettings();
    void loadSettings();

    std::unique_ptr<ProcessImpactMonitor> m_monitor;
    
    // Table
    std::unique_ptr<ProcessImpactTableModel> m_tableModel;
    std::unique_ptr<ProcessImpactSortFilterProxy> m_proxyModel;
    QTableView* m_tableView{nullptr};
    
    // Controls
    QComboBox* m_categoryCombo{nullptr};
    QLineEdit* m_filterEdit{nullptr};
    QCheckBox* m_showSystemCheck{nullptr};
    QPushButton* m_exportButton{nullptr};
    QPushButton* m_refreshButton{nullptr};
    
    // Detail view
    QSplitter* m_splitter{nullptr};
    ProcessImpactDetailPanel* m_detailPanel{nullptr};
    
    // Comparison chart
    ProcessComparisonChart* m_comparisonChart{nullptr};
    
    // Status
    QLabel* m_statusLabel{nullptr};
    
    // Settings
    ImpactCategory m_currentCategory{ImpactCategory::BatteryDrain};
};
