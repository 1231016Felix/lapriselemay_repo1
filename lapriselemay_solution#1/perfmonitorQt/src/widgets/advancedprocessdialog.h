#pragma once

// Windows header must come first to avoid conflicts
#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#endif

#include <QDialog>
#include <QLabel>
#include <QTabWidget>
#include <QTreeWidget>
#include <QTableWidget>
#include <QTimer>
#include <QProgressBar>
#include <QPushButton>
#include <QComboBox>
#include <QCheckBox>
#include <QGroupBox>
#include <QtGlobal>
#include <memory>

class AdvancedProcessMonitor;
class SparklineGraph;
struct AdvancedProcessInfo;

/**
 * @brief Advanced Process Dialog with detailed information
 */
class AdvancedProcessDialog : public QDialog
{
    Q_OBJECT

public:
    explicit AdvancedProcessDialog(quint32 pid, AdvancedProcessMonitor* monitor, QWidget *parent = nullptr);
    ~AdvancedProcessDialog() override;

private slots:
    void refreshData();
    void onSuspendResume();
    void onTerminate();
    void onTerminateTree();
    void onOpenFileLocation();
    void onCopyPath();
    void onPriorityChanged(int index);
    void onAffinityChanged();
    void navigateToProcess(quint32 pid);

private:
    void setupUi();
    QWidget* createOverviewTab();
    QWidget* createPerformanceTab();
    QWidget* createProcessTreeTab();
    QWidget* createModulesTab();
    QWidget* createThreadsTab();
    QWidget* createHandlesTab();
    QWidget* createMemoryTab();
    QWidget* createSecurityTab();
    
    void loadProcessInfo();
    void loadModules();
    void loadThreads();
    void loadHandles();
    void loadMemoryInfo();
    void loadSecurityInfo();
    void buildProcessTree();
    void addChildProcesses(QTreeWidgetItem* parentItem, quint32 parentPid);
    
    static QString formatBytes(qint64 bytes);
    static QString formatDuration(qint64 msecs);
    int getPriorityFromHandle(void* hProcess);

    quint32 m_pid{0};
    AdvancedProcessMonitor* m_monitor{nullptr};
    std::unique_ptr<QTimer> m_refreshTimer;
    
    // Overview tab
    QLabel* m_nameLabel{nullptr};
    QLabel* m_pidLabel{nullptr};
    QLabel* m_parentPidLabel{nullptr};
    QLabel* m_pathLabel{nullptr};
    QLabel* m_commandLineLabel{nullptr};
    QLabel* m_statusLabel{nullptr};
    QLabel* m_startTimeLabel{nullptr};
    QLabel* m_cpuTimeLabel{nullptr};
    QLabel* m_userLabel{nullptr};
    QLabel* m_architectureLabel{nullptr};
    QLabel* m_elevatedLabel{nullptr};
    QLabel* m_descriptionLabel{nullptr};
    
    // Performance tab
    QLabel* m_cpuUsageLabel{nullptr};
    QLabel* m_cpuKernelLabel{nullptr};
    QLabel* m_cpuUserLabel{nullptr};
    QLabel* m_memoryUsageLabel{nullptr};
    QLabel* m_memoryPrivateLabel{nullptr};
    QLabel* m_memoryPeakLabel{nullptr};
    QLabel* m_ioReadLabel{nullptr};
    QLabel* m_ioWriteLabel{nullptr};
    QLabel* m_threadCountLabel{nullptr};
    QLabel* m_handleCountLabel{nullptr};
    
    QProgressBar* m_cpuProgressBar{nullptr};
    QProgressBar* m_memoryProgressBar{nullptr};
    
    SparklineGraph* m_cpuGraph{nullptr};
    SparklineGraph* m_memoryGraph{nullptr};
    SparklineGraph* m_ioGraph{nullptr};
    
    // Process tree tab
    QTreeWidget* m_processTreeWidget{nullptr};
    QLabel* m_ancestorsLabel{nullptr};
    
    // Modules tab
    QTableWidget* m_modulesTable{nullptr};
    QLabel* m_moduleCountLabel{nullptr};
    
    // Threads tab
    QTableWidget* m_threadsTable{nullptr};
    QLabel* m_threadSummaryLabel{nullptr};
    
    // Handles tab
    QTableWidget* m_handlesTable{nullptr};
    QLabel* m_handleSummaryLabel{nullptr};
    
    // Memory tab
    QLabel* m_workingSetLabel{nullptr};
    QLabel* m_privateWorkingSetLabel{nullptr};
    QLabel* m_sharedWorkingSetLabel{nullptr};
    QLabel* m_virtualSizeLabel{nullptr};
    QLabel* m_pagedPoolLabel{nullptr};
    QLabel* m_nonPagedPoolLabel{nullptr};
    QTableWidget* m_memoryRegionsTable{nullptr};
    
    // Security tab
    QLabel* m_integrityLevelLabel{nullptr};
    QLabel* m_sidLabel{nullptr};
    QTableWidget* m_privilegesTable{nullptr};
    
    // Actions
    QPushButton* m_suspendResumeBtn{nullptr};
    QPushButton* m_terminateBtn{nullptr};
    QPushButton* m_terminateTreeBtn{nullptr};
    QComboBox* m_priorityCombo{nullptr};
    QGroupBox* m_affinityGroup{nullptr};
    std::vector<QCheckBox*> m_affinityChecks;
    
    QTabWidget* m_tabWidget{nullptr};
    
    // State
    bool m_isSuspended{false};
    qint64 m_lastIoRead{0};
    qint64 m_lastIoWrite{0};
};


/**
 * @brief Process History Dialog - shows recently terminated processes
 */
class ProcessHistoryDialog : public QDialog
{
    Q_OBJECT

public:
    explicit ProcessHistoryDialog(AdvancedProcessMonitor* monitor, QWidget *parent = nullptr);

private slots:
    void refreshHistory();
    void clearHistory();
    void onItemDoubleClicked(int row, int column);

private:
    void setupUi();
    static QString formatDuration(const QDateTime& start, const QDateTime& end);

    AdvancedProcessMonitor* m_monitor{nullptr};
    QTableWidget* m_historyTable{nullptr};
    QLabel* m_summaryLabel{nullptr};
    QPushButton* m_clearBtn{nullptr};
    QPushButton* m_refreshBtn{nullptr};
};
