#pragma once

#include <QWidget>
#include <QTreeView>
#include <QLineEdit>
#include <QComboBox>
#include <QPushButton>
#include <QLabel>
#include <QCheckBox>
#include <memory>

class AdvancedProcessMonitor;
class AdvancedProcessTreeModel;

/**
 * @brief Advanced Process Manager Widget
 * 
 * A complete process management widget with:
 * - Multiple grouping modes (by category, parent-child tree, by name, flat)
 * - Search/filter
 * - Suspend/Resume functionality
 * - Process history
 * - Detailed process information
 */
class AdvancedProcessWidget : public QWidget
{
    Q_OBJECT

public:
    explicit AdvancedProcessWidget(QWidget *parent = nullptr);
    ~AdvancedProcessWidget() override;
    
    /// Get the monitor for external updates
    AdvancedProcessMonitor* monitor() { return m_monitor.get(); }
    
    /// Refresh process list
    void refresh();

signals:
    void statusMessage(const QString& message, int timeout = 3000);

private slots:
    void onSearchTextChanged(const QString& text);
    void onGroupingModeChanged(int index);
    void onShowSystemProcessesChanged(bool checked);
    void onEndTask();
    void onSuspendResume();
    void onShowDetails();
    void onShowHistory();
    void onTerminateTree();
    void onContextMenu(const QPoint& pos);
    void onItemDoubleClicked(const QModelIndex& index);

private:
    void setupUi();
    void updateButtonStates();
    quint32 getSelectedPid() const;
    QList<quint32> getSelectedPids() const;

    std::unique_ptr<AdvancedProcessMonitor> m_monitor;
    
    // Selection preservation
    quint32 m_pendingProcessSelection{0};
    
    // UI Components
    QTreeView* m_treeView{nullptr};
    QLineEdit* m_searchEdit{nullptr};
    QComboBox* m_groupingCombo{nullptr};
    QCheckBox* m_showSystemCheck{nullptr};
    
    QPushButton* m_refreshBtn{nullptr};
    QPushButton* m_endTaskBtn{nullptr};
    QPushButton* m_suspendResumeBtn{nullptr};
    QPushButton* m_detailsBtn{nullptr};
    QPushButton* m_historyBtn{nullptr};
    
    QLabel* m_summaryLabel{nullptr};
};
