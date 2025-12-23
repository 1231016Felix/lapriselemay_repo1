#include "advancedprocesswidget.h"
#include "advancedprocessdialog.h"
#include "../monitors/advancedprocessmonitor.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QHeaderView>
#include <QMenu>
#include <QMessageBox>
#include <QApplication>
#include <QClipboard>
#include <QDesktopServices>
#include <QUrl>
#include <QFileInfo>
#include <QTimer>

// Helper function - must be declared before use
static QString formatBytes(qint64 bytes)
{
    const char* units[] = {"B", "KB", "MB", "GB", "TB"};
    int unitIndex = 0;
    double size = bytes;
    
    while (size >= 1024.0 && unitIndex < 4) {
        size /= 1024.0;
        unitIndex++;
    }
    
    return QString("%1 %2").arg(size, 0, 'f', unitIndex > 0 ? 1 : 0).arg(units[unitIndex]);
}

AdvancedProcessWidget::AdvancedProcessWidget(QWidget *parent)
    : QWidget(parent)
    , m_monitor(std::make_unique<AdvancedProcessMonitor>())
{
    setupUi();
    
    // Start auto-refresh
    m_monitor->startAutoRefresh(1000);
    
    // Connect signals - SAVE selection BEFORE update
    connect(m_monitor.get(), &AdvancedProcessMonitor::aboutToRefresh, this, [this]() {
        // Save current selection BEFORE the model is updated
        quint32 selectedPid = getSelectedPid();
        if (selectedPid > 0) {
            m_pendingProcessSelection = selectedPid;
        }
    });
    
    // RESTORE selection AFTER update - use QTimer::singleShot to ensure all model signals are processed
    connect(m_monitor.get(), &AdvancedProcessMonitor::processesUpdated, this, [this]() {
        // Update summary label immediately
        m_summaryLabel->setText(tr("%1 processes, %2 threads | CPU: %3% | Memory: %4")
            .arg(m_monitor->totalProcessCount())
            .arg(m_monitor->totalThreadCount())
            .arg(m_monitor->totalCpuUsage(), 0, 'f', 1)
            .arg(formatBytes(m_monitor->totalMemoryUsage())));
        
        // Restore selection AFTER all model signals have been processed
        // This is critical to prevent selection loss during layoutChanged
        if (m_pendingProcessSelection > 0) {
            quint32 pidToRestore = m_pendingProcessSelection;
            QTimer::singleShot(0, this, [this, pidToRestore]() {
                // Check if selection is still needed
                quint32 currentPid = getSelectedPid();
                if (currentPid != pidToRestore) {
                    auto* proxyModel = qobject_cast<AdvancedProcessSortFilterProxy*>(m_treeView->model());
                    if (proxyModel) {
                        QModelIndex proxyIndex = proxyModel->findProxyIndexByPid(pidToRestore);
                        if (proxyIndex.isValid()) {
                            // Block signals to avoid triggering unnecessary selection changed events
                            m_treeView->selectionModel()->blockSignals(true);
                            m_treeView->setCurrentIndex(proxyIndex);
                            m_treeView->scrollTo(proxyIndex);
                            m_treeView->selectionModel()->blockSignals(false);
                            updateButtonStates();
                        }
                    }
                }
            });
        }
        
        updateButtonStates();
    });
    
    connect(m_monitor.get(), &AdvancedProcessMonitor::processStarted, 
            this, [this](quint32 pid, const QString& name) {
        Q_UNUSED(pid);
        emit statusMessage(tr("Process started: %1").arg(name));
    });
    
    connect(m_monitor.get(), &AdvancedProcessMonitor::processEnded,
            this, [this](quint32 pid, const QString& name) {
        Q_UNUSED(pid);
        emit statusMessage(tr("Process ended: %1").arg(name));
    });
}

AdvancedProcessWidget::~AdvancedProcessWidget()
{
    m_monitor->stopAutoRefresh();
}

void AdvancedProcessWidget::setupUi()
{
    auto mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(0, 0, 0, 0);
    
    // Toolbar
    auto toolbarLayout = new QHBoxLayout();
    
    // Search
    m_searchEdit = new QLineEdit();
    m_searchEdit->setPlaceholderText(tr("Search processes..."));
    m_searchEdit->setClearButtonEnabled(true);
    m_searchEdit->setMaximumWidth(250);
    connect(m_searchEdit, &QLineEdit::textChanged, 
            this, &AdvancedProcessWidget::onSearchTextChanged);
    toolbarLayout->addWidget(m_searchEdit);
    
    // Grouping mode
    toolbarLayout->addWidget(new QLabel(tr("Group by:")));
    m_groupingCombo = new QComboBox();
    m_groupingCombo->addItem(tr("Category"), static_cast<int>(AdvancedProcessTreeModel::GroupingMode::ByCategory));
    m_groupingCombo->addItem(tr("Process Tree"), static_cast<int>(AdvancedProcessTreeModel::GroupingMode::ByParent));
    m_groupingCombo->addItem(tr("Name"), static_cast<int>(AdvancedProcessTreeModel::GroupingMode::ByName));
    m_groupingCombo->addItem(tr("None (Flat)"), static_cast<int>(AdvancedProcessTreeModel::GroupingMode::None));
    connect(m_groupingCombo, QOverload<int>::of(&QComboBox::currentIndexChanged),
            this, &AdvancedProcessWidget::onGroupingModeChanged);
    toolbarLayout->addWidget(m_groupingCombo);
    
    // Show system processes
    m_showSystemCheck = new QCheckBox(tr("Show Windows processes"));
    m_showSystemCheck->setChecked(true);
    connect(m_showSystemCheck, &QCheckBox::toggled,
            this, &AdvancedProcessWidget::onShowSystemProcessesChanged);
    toolbarLayout->addWidget(m_showSystemCheck);
    
    toolbarLayout->addStretch();
    
    // Buttons
    m_refreshBtn = new QPushButton(tr("Refresh"));
    connect(m_refreshBtn, &QPushButton::clicked, this, &AdvancedProcessWidget::refresh);
    toolbarLayout->addWidget(m_refreshBtn);
    
    m_historyBtn = new QPushButton(tr("📜 History"));
    m_historyBtn->setToolTip(tr("Show recently terminated processes"));
    connect(m_historyBtn, &QPushButton::clicked, this, &AdvancedProcessWidget::onShowHistory);
    toolbarLayout->addWidget(m_historyBtn);
    
    mainLayout->addLayout(toolbarLayout);
    
    // Tree view
    m_treeView = new QTreeView();
    m_treeView->setModel(m_monitor->model());
    m_treeView->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_treeView->setSelectionMode(QAbstractItemView::ExtendedSelection);
    m_treeView->setAlternatingRowColors(true);
    m_treeView->setSortingEnabled(true);
    m_treeView->setRootIsDecorated(true);
    m_treeView->setExpandsOnDoubleClick(false);
    m_treeView->setAnimated(true);
    m_treeView->setContextMenuPolicy(Qt::CustomContextMenu);
    m_treeView->header()->setStretchLastSection(true);
    
    // Set column widths
    m_treeView->setColumnWidth(0, 250);  // Name
    m_treeView->setColumnWidth(1, 60);   // PID
    m_treeView->setColumnWidth(2, 80);   // Status
    m_treeView->setColumnWidth(3, 60);   // CPU
    m_treeView->setColumnWidth(4, 80);   // Memory
    m_treeView->setColumnWidth(5, 70);   // Disk
    m_treeView->setColumnWidth(6, 70);   // Network
    m_treeView->setColumnWidth(7, 50);   // GPU
    m_treeView->setColumnWidth(8, 60);   // Threads
    m_treeView->setColumnWidth(9, 60);   // Handles
    
    connect(m_treeView, &QTreeView::customContextMenuRequested,
            this, &AdvancedProcessWidget::onContextMenu);
    connect(m_treeView, &QTreeView::doubleClicked,
            this, &AdvancedProcessWidget::onItemDoubleClicked);
    connect(m_treeView->selectionModel(), &QItemSelectionModel::selectionChanged,
            this, [this]() { updateButtonStates(); });
    
    mainLayout->addWidget(m_treeView);
    
    // Bottom toolbar
    auto bottomLayout = new QHBoxLayout();
    
    m_summaryLabel = new QLabel(tr("Loading..."));
    bottomLayout->addWidget(m_summaryLabel);
    
    bottomLayout->addStretch();
    
    m_suspendResumeBtn = new QPushButton(tr("⏸ Suspend"));
    m_suspendResumeBtn->setEnabled(false);
    connect(m_suspendResumeBtn, &QPushButton::clicked, this, &AdvancedProcessWidget::onSuspendResume);
    bottomLayout->addWidget(m_suspendResumeBtn);
    
    m_detailsBtn = new QPushButton(tr("📋 Details"));
    m_detailsBtn->setEnabled(false);
    connect(m_detailsBtn, &QPushButton::clicked, this, &AdvancedProcessWidget::onShowDetails);
    bottomLayout->addWidget(m_detailsBtn);
    
    m_endTaskBtn = new QPushButton(tr("End Task"));
    m_endTaskBtn->setStyleSheet("background-color: #d32f2f; color: white;");
    m_endTaskBtn->setEnabled(false);
    connect(m_endTaskBtn, &QPushButton::clicked, this, &AdvancedProcessWidget::onEndTask);
    bottomLayout->addWidget(m_endTaskBtn);
    
    mainLayout->addLayout(bottomLayout);
}

void AdvancedProcessWidget::refresh()
{
    m_monitor->refresh();
}

void AdvancedProcessWidget::onSearchTextChanged(const QString& text)
{
    m_monitor->setFilter(text);
}

void AdvancedProcessWidget::onGroupingModeChanged(int index)
{
    auto mode = static_cast<AdvancedProcessTreeModel::GroupingMode>(
        m_groupingCombo->itemData(index).toInt());
    m_monitor->setGroupingMode(mode);
}

void AdvancedProcessWidget::onShowSystemProcessesChanged(bool checked)
{
    m_monitor->setShowSystemProcesses(checked);
}

void AdvancedProcessWidget::onEndTask()
{
    auto pids = getSelectedPids();
    if (pids.isEmpty()) return;
    
    QString message;
    if (pids.size() == 1) {
        const auto* proc = m_monitor->getProcessByPid(pids.first());
        QString name = proc ? proc->name : QString::number(pids.first());
        message = tr("Are you sure you want to terminate '%1' (PID: %2)?")
                  .arg(name).arg(pids.first());
    } else {
        message = tr("Are you sure you want to terminate %1 selected processes?")
                  .arg(pids.size());
    }
    
    auto reply = QMessageBox::question(this, tr("End Task"), message,
        QMessageBox::Yes | QMessageBox::No, QMessageBox::No);
    
    if (reply == QMessageBox::Yes) {
        int success = 0, failed = 0;
        for (quint32 pid : pids) {
            if (m_monitor->terminateProcess(pid)) {
                success++;
            } else {
                failed++;
            }
        }
        
        if (failed > 0) {
            emit statusMessage(tr("%1 terminated, %2 failed").arg(success).arg(failed));
        } else {
            emit statusMessage(tr("%1 process(es) terminated").arg(success));
        }
    }
}

void AdvancedProcessWidget::onSuspendResume()
{
    quint32 pid = getSelectedPid();
    if (pid == 0) return;
    
    const auto* proc = m_monitor->getProcessByPid(pid);
    if (!proc) return;
    
    if (proc->state == ProcessState::Suspended) {
        if (m_monitor->resumeProcess(pid)) {
            emit statusMessage(tr("Process resumed: %1").arg(proc->name));
        } else {
            QMessageBox::warning(this, tr("Error"), tr("Failed to resume process."));
        }
    } else {
        auto reply = QMessageBox::question(this, tr("Suspend Process"),
            tr("Are you sure you want to suspend '%1'?\n\n"
               "Warning: Suspending system processes may cause instability.")
               .arg(proc->name),
            QMessageBox::Yes | QMessageBox::No, QMessageBox::No);
        
        if (reply == QMessageBox::Yes) {
            if (m_monitor->suspendProcess(pid)) {
                emit statusMessage(tr("Process suspended: %1").arg(proc->name));
            } else {
                QMessageBox::warning(this, tr("Error"), tr("Failed to suspend process."));
            }
        }
    }
    
    updateButtonStates();
}

void AdvancedProcessWidget::onShowDetails()
{
    quint32 pid = getSelectedPid();
    if (pid == 0) return;
    
    auto* dialog = new AdvancedProcessDialog(pid, m_monitor.get(), this);
    dialog->setAttribute(Qt::WA_DeleteOnClose);
    dialog->show();
}

void AdvancedProcessWidget::onShowHistory()
{
    auto* dialog = new ProcessHistoryDialog(m_monitor.get(), this);
    dialog->setAttribute(Qt::WA_DeleteOnClose);
    dialog->show();
}

void AdvancedProcessWidget::onTerminateTree()
{
    quint32 pid = getSelectedPid();
    if (pid == 0) return;
    
    const auto* proc = m_monitor->getProcessByPid(pid);
    if (!proc) return;
    
    auto children = m_monitor->getChildProcesses(pid);
    
    auto reply = QMessageBox::question(this, tr("Terminate Process Tree"),
        tr("Are you sure you want to terminate '%1' and its %2 child processes?")
           .arg(proc->name).arg(children.size()),
        QMessageBox::Yes | QMessageBox::No, QMessageBox::No);
    
    if (reply == QMessageBox::Yes) {
        if (m_monitor->terminateProcessTree(pid)) {
            emit statusMessage(tr("Process tree terminated"));
        } else {
            QMessageBox::warning(this, tr("Error"), 
                tr("Failed to terminate some processes in the tree."));
        }
    }
}

void AdvancedProcessWidget::onContextMenu(const QPoint& pos)
{
    QModelIndex index = m_treeView->indexAt(pos);
    if (!index.isValid()) return;
    
    quint32 pid = getSelectedPid();
    if (pid == 0) return;
    
    const auto* proc = m_monitor->getProcessByPid(pid);
    if (!proc) return;
    
    QMenu menu(this);
    
    // Details
    auto detailsAction = menu.addAction(tr("📋 Process Details..."));
    connect(detailsAction, &QAction::triggered, this, &AdvancedProcessWidget::onShowDetails);
    
    menu.addSeparator();
    
    // Suspend/Resume
    if (proc->state == ProcessState::Suspended) {
        auto resumeAction = menu.addAction(tr("▶ Resume"));
        connect(resumeAction, &QAction::triggered, this, &AdvancedProcessWidget::onSuspendResume);
    } else {
        auto suspendAction = menu.addAction(tr("⏸ Suspend"));
        connect(suspendAction, &QAction::triggered, this, &AdvancedProcessWidget::onSuspendResume);
    }
    
    menu.addSeparator();
    
    // Terminate
    auto endTaskAction = menu.addAction(tr("End Task"));
    connect(endTaskAction, &QAction::triggered, this, &AdvancedProcessWidget::onEndTask);
    
    auto terminateTreeAction = menu.addAction(tr("End Process Tree"));
    connect(terminateTreeAction, &QAction::triggered, this, &AdvancedProcessWidget::onTerminateTree);
    
    menu.addSeparator();
    
    // Priority submenu
    auto priorityMenu = menu.addMenu(tr("Set Priority"));
    
    auto priorities = QList<QPair<QString, int>>{
        {tr("Realtime"), 5}, {tr("High"), 4}, {tr("Above Normal"), 3},
        {tr("Normal"), 2}, {tr("Below Normal"), 1}, {tr("Idle"), 0}
    };
    
    for (const auto& [name, priority] : priorities) {
        auto action = priorityMenu->addAction(name);
        connect(action, &QAction::triggered, [this, pid, priority]() {
            if (m_monitor->setProcessPriority(pid, priority)) {
                emit statusMessage(tr("Priority changed"));
            } else {
                QMessageBox::warning(this, tr("Error"), tr("Failed to change priority."));
            }
        });
    }
    
    menu.addSeparator();
    
    // File location
    if (!proc->executablePath.isEmpty()) {
        auto openLocationAction = menu.addAction(tr("Open File Location"));
        connect(openLocationAction, &QAction::triggered, [proc]() {
            QFileInfo info(proc->executablePath);
            QDesktopServices::openUrl(QUrl::fromLocalFile(info.absolutePath()));
        });
    }
    
    menu.addSeparator();
    
    // Copy actions
    auto copyPidAction = menu.addAction(tr("Copy PID"));
    connect(copyPidAction, &QAction::triggered, [pid]() {
        QApplication::clipboard()->setText(QString::number(pid));
    });
    
    auto copyNameAction = menu.addAction(tr("Copy Name"));
    connect(copyNameAction, &QAction::triggered, [proc]() {
        QApplication::clipboard()->setText(proc->name);
    });
    
    if (!proc->executablePath.isEmpty()) {
        auto copyPathAction = menu.addAction(tr("Copy Path"));
        connect(copyPathAction, &QAction::triggered, [proc]() {
            QApplication::clipboard()->setText(proc->executablePath);
        });
    }
    
    menu.exec(m_treeView->viewport()->mapToGlobal(pos));
}

void AdvancedProcessWidget::onItemDoubleClicked(const QModelIndex& index)
{
    Q_UNUSED(index);
    onShowDetails();
}

void AdvancedProcessWidget::updateButtonStates()
{
    quint32 pid = getSelectedPid();
    bool hasSelection = (pid != 0);
    
    m_endTaskBtn->setEnabled(hasSelection);
    m_detailsBtn->setEnabled(hasSelection);
    m_suspendResumeBtn->setEnabled(hasSelection);
    
    if (hasSelection) {
        const auto* proc = m_monitor->getProcessByPid(pid);
        if (proc && proc->state == ProcessState::Suspended) {
            m_suspendResumeBtn->setText(tr("▶ Resume"));
        } else {
            m_suspendResumeBtn->setText(tr("⏸ Suspend"));
        }
    }
}

quint32 AdvancedProcessWidget::getSelectedPid() const
{
    auto selection = m_treeView->selectionModel()->selectedRows();
    if (selection.isEmpty()) return 0;
    
    auto proxyIndex = selection.first();
    auto* proxyModel = qobject_cast<AdvancedProcessSortFilterProxy*>(m_treeView->model());
    if (!proxyModel) return 0;
    
    auto sourceIndex = proxyModel->mapToSource(proxyIndex);
    return m_monitor->treeModel()->getPid(sourceIndex);
}

QList<quint32> AdvancedProcessWidget::getSelectedPids() const
{
    QList<quint32> pids;
    
    auto selection = m_treeView->selectionModel()->selectedRows();
    auto* proxyModel = qobject_cast<AdvancedProcessSortFilterProxy*>(m_treeView->model());
    if (!proxyModel) return pids;
    
    for (const auto& proxyIndex : selection) {
        auto sourceIndex = proxyModel->mapToSource(proxyIndex);
        quint32 pid = m_monitor->treeModel()->getPid(sourceIndex);
        if (pid != 0) {
            pids.append(pid);
        }
    }
    
    return pids;
}
