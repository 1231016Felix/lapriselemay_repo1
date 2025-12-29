#pragma once

#include <QDialog>
#include <QTabWidget>
#include <QTableView>
#include <QTreeWidget>
#include <QLabel>
#include <QProgressBar>
#include <QGroupBox>
#include <QPushButton>
#include <QLineEdit>
#include <QCheckBox>
#include <QTimer>
#include <QSortFilterProxyModel>
#include <memory>

class DetailedMemoryMonitor;
class SparklineGraph;

/**
 * @brief Custom sort/filter proxy for process memory table
 */
class ProcessMemorySortFilterProxy : public QSortFilterProxyModel
{
    Q_OBJECT
public:
    explicit ProcessMemorySortFilterProxy(QObject* parent = nullptr);
    
    QModelIndex findProxyIndexByPid(quint32 pid) const;

protected:
    bool lessThan(const QModelIndex& left, const QModelIndex& right) const override;
};

/**
 * @brief Dialog showing detailed RAM usage and memory leak detection
 */
class DetailedMemoryDialog : public QDialog
{
    Q_OBJECT

public:
    explicit DetailedMemoryDialog(QWidget *parent = nullptr);
    ~DetailedMemoryDialog() override;

private slots:
    void onRefreshed();
    void onFilterChanged(const QString& text);
    void onProcessDoubleClicked(const QModelIndex& index);
    void onPotentialLeakDetected(quint32 pid, const QString& name, double growthRate);
    void onSystemMemoryLow(double usagePercent);
    void exportReport();

private:
    void setupUi();
    void createOverviewTab();
    void createProcessesTab();
    void createLeakDetectionTab();
    void createCompositionTab();
    void updateOverview();
    void updateLeakList();
    void updateComposition();
    
    quint32 getSelectedPid() const;
    void restoreSelection();
    
    QString formatBytes(qint64 bytes) const;
    QString formatPercent(double percent) const;

    // Tabs
    QTabWidget* m_tabWidget{nullptr};
    
    // Overview tab
    QWidget* m_overviewTab{nullptr};
    QLabel* m_physicalUsedLabel{nullptr};
    QLabel* m_physicalTotalLabel{nullptr};
    QProgressBar* m_physicalProgress{nullptr};
    QLabel* m_commitUsedLabel{nullptr};
    QLabel* m_commitLimitLabel{nullptr};
    QProgressBar* m_commitProgress{nullptr};
    QLabel* m_cacheLabel{nullptr};
    QLabel* m_kernelPagedLabel{nullptr};
    QLabel* m_kernelNonPagedLabel{nullptr};
    QLabel* m_processCountLabel{nullptr};
    QLabel* m_handleCountLabel{nullptr};
    SparklineGraph* m_memoryGraph{nullptr};
    SparklineGraph* m_commitGraph{nullptr};
    
    // Processes tab
    QWidget* m_processesTab{nullptr};
    QLineEdit* m_filterEdit{nullptr};
    QTableView* m_processTable{nullptr};
    ProcessMemorySortFilterProxy* m_proxyModel{nullptr};
    QLabel* m_selectedProcessLabel{nullptr};
    quint32 m_pendingProcessSelection{0};
    
    // Leak detection tab
    QWidget* m_leakTab{nullptr};
    QTreeWidget* m_leakTree{nullptr};
    QLabel* m_leakStatusLabel{nullptr};
    QCheckBox* m_leakDetectionCheckbox{nullptr};
    QLabel* m_leakThresholdLabel{nullptr};
    
    // Memory composition tab
    QWidget* m_compositionTab{nullptr};
    QTreeWidget* m_compositionTree{nullptr};
    QLabel* m_topConsumersLabel{nullptr};
    
    // Monitor
    std::unique_ptr<DetailedMemoryMonitor> m_monitor;
};
