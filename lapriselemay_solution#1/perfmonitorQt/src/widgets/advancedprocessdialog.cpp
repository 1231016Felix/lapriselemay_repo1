#include "advancedprocessdialog.h"
#include "../monitors/advancedprocessmonitor.h"
#include "sparklinegraph.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QGroupBox>
#include <QHeaderView>
#include <QMessageBox>
#include <QClipboard>
#include <QApplication>
#include <QDesktopServices>
#include <QUrl>
#include <QFileInfo>
#include <QScrollArea>
#include <QSplitter>

#ifdef _WIN32
#include <Windows.h>
#include <TlHelp32.h>
#include <Psapi.h>
#pragma comment(lib, "psapi.lib")
#endif

// ============================================================================
// AdvancedProcessDialog implementation
// ============================================================================

AdvancedProcessDialog::AdvancedProcessDialog(quint32 pid, AdvancedProcessMonitor* monitor, QWidget *parent)
    : QDialog(parent)
    , m_pid(pid)
    , m_monitor(monitor)
    , m_refreshTimer(std::make_unique<QTimer>())
{
    const auto* proc = m_monitor->getProcessByPid(pid);
    QString title = proc ? proc->name : QString::number(pid);
    setWindowTitle(tr("Process Details - %1 (PID: %2)").arg(title).arg(pid));
    setMinimumSize(700, 600);
    resize(850, 700);
    
    setupUi();
    loadProcessInfo();
    
    connect(m_refreshTimer.get(), &QTimer::timeout, this, &AdvancedProcessDialog::refreshData);
    m_refreshTimer->start(1000);
}

AdvancedProcessDialog::~AdvancedProcessDialog()
{
    m_refreshTimer->stop();
}

void AdvancedProcessDialog::setupUi()
{
    auto mainLayout = new QVBoxLayout(this);
    
    m_tabWidget = new QTabWidget();
    m_tabWidget->addTab(createOverviewTab(), tr("Overview"));
    m_tabWidget->addTab(createPerformanceTab(), tr("Performance"));
    m_tabWidget->addTab(createProcessTreeTab(), tr("Process Tree"));
    m_tabWidget->addTab(createModulesTab(), tr("Modules"));
    m_tabWidget->addTab(createThreadsTab(), tr("Threads"));
    
    mainLayout->addWidget(m_tabWidget);
    
    // Action buttons
    auto buttonLayout = new QHBoxLayout();
    
    m_suspendResumeBtn = new QPushButton(tr("Suspend"));
    connect(m_suspendResumeBtn, &QPushButton::clicked, this, &AdvancedProcessDialog::onSuspendResume);
    buttonLayout->addWidget(m_suspendResumeBtn);
    
    m_terminateBtn = new QPushButton(tr("Terminate"));
    m_terminateBtn->setStyleSheet("background-color: #d32f2f; color: white;");
    connect(m_terminateBtn, &QPushButton::clicked, this, &AdvancedProcessDialog::onTerminate);
    buttonLayout->addWidget(m_terminateBtn);
    
    m_terminateTreeBtn = new QPushButton(tr("Terminate Tree"));
    m_terminateTreeBtn->setStyleSheet("background-color: #b71c1c; color: white;");
    m_terminateTreeBtn->setToolTip(tr("Terminate this process and all its child processes"));
    connect(m_terminateTreeBtn, &QPushButton::clicked, this, &AdvancedProcessDialog::onTerminateTree);
    buttonLayout->addWidget(m_terminateTreeBtn);
    
    buttonLayout->addStretch();
    
    auto closeBtn = new QPushButton(tr("Close"));
    connect(closeBtn, &QPushButton::clicked, this, &QDialog::accept);
    buttonLayout->addWidget(closeBtn);
    
    mainLayout->addLayout(buttonLayout);
}

QWidget* AdvancedProcessDialog::createOverviewTab()
{
    auto widget = new QWidget();
    auto layout = new QVBoxLayout(widget);
    
    // Basic info group
    auto basicGroup = new QGroupBox(tr("Basic Information"));
    auto basicLayout = new QGridLayout(basicGroup);
    
    int row = 0;
    
    basicLayout->addWidget(new QLabel(tr("Name:")), row, 0);
    m_nameLabel = new QLabel();
    m_nameLabel->setStyleSheet("font-weight: bold; font-size: 14px;");
    m_nameLabel->setTextInteractionFlags(Qt::TextSelectableByMouse);
    basicLayout->addWidget(m_nameLabel, row, 1, 1, 3);
    row++;
    
    basicLayout->addWidget(new QLabel(tr("Description:")), row, 0);
    m_descriptionLabel = new QLabel();
    m_descriptionLabel->setWordWrap(true);
    m_descriptionLabel->setTextInteractionFlags(Qt::TextSelectableByMouse);
    basicLayout->addWidget(m_descriptionLabel, row, 1, 1, 3);
    row++;
    
    basicLayout->addWidget(new QLabel(tr("PID:")), row, 0);
    m_pidLabel = new QLabel();
    m_pidLabel->setTextInteractionFlags(Qt::TextSelectableByMouse);
    basicLayout->addWidget(m_pidLabel, row, 1);
    
    basicLayout->addWidget(new QLabel(tr("Parent PID:")), row, 2);
    m_parentPidLabel = new QLabel();
    m_parentPidLabel->setTextInteractionFlags(Qt::TextSelectableByMouse);
    basicLayout->addWidget(m_parentPidLabel, row, 3);
    row++;
    
    basicLayout->addWidget(new QLabel(tr("Path:")), row, 0);
    m_pathLabel = new QLabel();
    m_pathLabel->setWordWrap(true);
    m_pathLabel->setTextInteractionFlags(Qt::TextSelectableByMouse);
    basicLayout->addWidget(m_pathLabel, row, 1, 1, 3);
    row++;
    
    auto pathActionsLayout = new QHBoxLayout();
    auto openLocationBtn = new QPushButton(tr("Open File Location"));
    connect(openLocationBtn, &QPushButton::clicked, this, &AdvancedProcessDialog::onOpenFileLocation);
    pathActionsLayout->addWidget(openLocationBtn);
    
    auto copyPathBtn = new QPushButton(tr("Copy Path"));
    connect(copyPathBtn, &QPushButton::clicked, this, &AdvancedProcessDialog::onCopyPath);
    pathActionsLayout->addWidget(copyPathBtn);
    pathActionsLayout->addStretch();
    basicLayout->addLayout(pathActionsLayout, row, 1, 1, 3);
    row++;
    
    basicLayout->addWidget(new QLabel(tr("User:")), row, 0);
    m_userLabel = new QLabel();
    m_userLabel->setTextInteractionFlags(Qt::TextSelectableByMouse);
    basicLayout->addWidget(m_userLabel, row, 1, 1, 3);
    
    layout->addWidget(basicGroup);
    
    // Status group
    auto statusGroup = new QGroupBox(tr("Status"));
    auto statusLayout = new QGridLayout(statusGroup);
    
    row = 0;
    statusLayout->addWidget(new QLabel(tr("Status:")), row, 0);
    m_statusLabel = new QLabel();
    statusLayout->addWidget(m_statusLabel, row, 1);
    
    statusLayout->addWidget(new QLabel(tr("Architecture:")), row, 2);
    m_architectureLabel = new QLabel();
    statusLayout->addWidget(m_architectureLabel, row, 3);
    row++;
    
    statusLayout->addWidget(new QLabel(tr("Start Time:")), row, 0);
    m_startTimeLabel = new QLabel();
    statusLayout->addWidget(m_startTimeLabel, row, 1);
    
    statusLayout->addWidget(new QLabel(tr("CPU Time:")), row, 2);
    m_cpuTimeLabel = new QLabel();
    statusLayout->addWidget(m_cpuTimeLabel, row, 3);
    row++;
    
    statusLayout->addWidget(new QLabel(tr("Elevated:")), row, 0);
    m_elevatedLabel = new QLabel();
    statusLayout->addWidget(m_elevatedLabel, row, 1);
    
    layout->addWidget(statusGroup);
    
    // Priority group
    auto priorityGroup = new QGroupBox(tr("Priority"));
    auto priorityLayout = new QHBoxLayout(priorityGroup);
    
    priorityLayout->addWidget(new QLabel(tr("Priority Class:")));
    m_priorityCombo = new QComboBox();
    m_priorityCombo->addItem(tr("Idle"), 0);
    m_priorityCombo->addItem(tr("Below Normal"), 1);
    m_priorityCombo->addItem(tr("Normal"), 2);
    m_priorityCombo->addItem(tr("Above Normal"), 3);
    m_priorityCombo->addItem(tr("High"), 4);
    m_priorityCombo->addItem(tr("Realtime"), 5);
    connect(m_priorityCombo, QOverload<int>::of(&QComboBox::currentIndexChanged),
            this, &AdvancedProcessDialog::onPriorityChanged);
    priorityLayout->addWidget(m_priorityCombo);
    priorityLayout->addStretch();
    
    layout->addWidget(priorityGroup);
    
    // Affinity group
    m_affinityGroup = new QGroupBox(tr("CPU Affinity"));
    auto affinityLayout = new QGridLayout(m_affinityGroup);
    
#ifdef _WIN32
    SYSTEM_INFO sysInfo;
    GetSystemInfo(&sysInfo);
    int cpuCount = sysInfo.dwNumberOfProcessors;
    
    for (int i = 0; i < cpuCount && i < 64; ++i) {
        auto check = new QCheckBox(QString("CPU %1").arg(i));
        check->setProperty("cpuIndex", i);
        connect(check, &QCheckBox::toggled, this, &AdvancedProcessDialog::onAffinityChanged);
        m_affinityChecks.push_back(check);
        affinityLayout->addWidget(check, i / 8, i % 8);
    }
#endif
    
    layout->addWidget(m_affinityGroup);
    layout->addStretch();
    
    return widget;
}

QWidget* AdvancedProcessDialog::createPerformanceTab()
{
    auto widget = new QWidget();
    auto layout = new QVBoxLayout(widget);
    
    // CPU group
    auto cpuGroup = new QGroupBox(tr("CPU"));
    auto cpuLayout = new QGridLayout(cpuGroup);
    
    cpuLayout->addWidget(new QLabel(tr("Usage:")), 0, 0);
    m_cpuUsageLabel = new QLabel("0%");
    m_cpuUsageLabel->setStyleSheet("font-size: 18px; font-weight: bold; color: #0078d7;");
    cpuLayout->addWidget(m_cpuUsageLabel, 0, 1);
    
    cpuLayout->addWidget(new QLabel(tr("Kernel:")), 0, 2);
    m_cpuKernelLabel = new QLabel("0%");
    cpuLayout->addWidget(m_cpuKernelLabel, 0, 3);
    
    cpuLayout->addWidget(new QLabel(tr("User:")), 0, 4);
    m_cpuUserLabel = new QLabel("0%");
    cpuLayout->addWidget(m_cpuUserLabel, 0, 5);
    
    m_cpuProgressBar = new QProgressBar();
    m_cpuProgressBar->setRange(0, 100);
    cpuLayout->addWidget(m_cpuProgressBar, 1, 0, 1, 6);
    
    m_cpuGraph = new SparklineGraph(60, QColor(0, 120, 215));
    m_cpuGraph->setMinimumHeight(80);
    cpuLayout->addWidget(m_cpuGraph, 2, 0, 1, 6);
    
    layout->addWidget(cpuGroup);
    
    // Memory group
    auto memGroup = new QGroupBox(tr("Memory"));
    auto memLayout = new QGridLayout(memGroup);
    
    memLayout->addWidget(new QLabel(tr("Working Set:")), 0, 0);
    m_memoryUsageLabel = new QLabel("0 MB");
    m_memoryUsageLabel->setStyleSheet("font-size: 18px; font-weight: bold; color: #8b008b;");
    memLayout->addWidget(m_memoryUsageLabel, 0, 1);
    
    memLayout->addWidget(new QLabel(tr("Private:")), 0, 2);
    m_memoryPrivateLabel = new QLabel("0 MB");
    memLayout->addWidget(m_memoryPrivateLabel, 0, 3);
    
    memLayout->addWidget(new QLabel(tr("Peak:")), 0, 4);
    m_memoryPeakLabel = new QLabel("0 MB");
    memLayout->addWidget(m_memoryPeakLabel, 0, 5);
    
    m_memoryGraph = new SparklineGraph(60, QColor(139, 0, 139));
    m_memoryGraph->setMinimumHeight(80);
    memLayout->addWidget(m_memoryGraph, 1, 0, 1, 6);
    
    layout->addWidget(memGroup);
    
    // I/O group
    auto ioGroup = new QGroupBox(tr("Disk I/O"));
    auto ioLayout = new QGridLayout(ioGroup);
    
    ioLayout->addWidget(new QLabel(tr("Read:")), 0, 0);
    m_ioReadLabel = new QLabel("0 B/s");
    m_ioReadLabel->setStyleSheet("font-weight: bold; color: #00aa00;");
    ioLayout->addWidget(m_ioReadLabel, 0, 1);
    
    ioLayout->addWidget(new QLabel(tr("Write:")), 0, 2);
    m_ioWriteLabel = new QLabel("0 B/s");
    m_ioWriteLabel->setStyleSheet("font-weight: bold; color: #cc6600;");
    ioLayout->addWidget(m_ioWriteLabel, 0, 3);
    
    m_ioGraph = new SparklineGraph(60, QColor(0, 170, 0));
    m_ioGraph->setMinimumHeight(60);
    ioLayout->addWidget(m_ioGraph, 1, 0, 1, 4);
    
    layout->addWidget(ioGroup);
    
    // Counts
    auto countsLayout = new QHBoxLayout();
    countsLayout->addWidget(new QLabel(tr("Threads:")));
    m_threadCountLabel = new QLabel("0");
    m_threadCountLabel->setStyleSheet("font-weight: bold;");
    countsLayout->addWidget(m_threadCountLabel);
    countsLayout->addSpacing(20);
    
    countsLayout->addWidget(new QLabel(tr("Handles:")));
    m_handleCountLabel = new QLabel("0");
    m_handleCountLabel->setStyleSheet("font-weight: bold;");
    countsLayout->addWidget(m_handleCountLabel);
    countsLayout->addStretch();
    
    layout->addLayout(countsLayout);
    layout->addStretch();
    
    return widget;
}

QWidget* AdvancedProcessDialog::createProcessTreeTab()
{
    auto widget = new QWidget();
    auto layout = new QVBoxLayout(widget);
    
    // Ancestors info
    auto ancestorsGroup = new QGroupBox(tr("Process Ancestry (Parent Chain)"));
    auto ancestorsLayout = new QVBoxLayout(ancestorsGroup);
    m_ancestorsLabel = new QLabel();
    m_ancestorsLabel->setWordWrap(true);
    m_ancestorsLabel->setTextInteractionFlags(Qt::TextSelectableByMouse);
    ancestorsLayout->addWidget(m_ancestorsLabel);
    layout->addWidget(ancestorsGroup);
    
    // Process tree
    auto treeGroup = new QGroupBox(tr("Child Processes (Process Tree)"));
    auto treeLayout = new QVBoxLayout(treeGroup);
    
    m_processTreeWidget = new QTreeWidget();
    m_processTreeWidget->setHeaderLabels({tr("Name"), tr("PID"), tr("CPU"), tr("Memory"), tr("Status")});
    m_processTreeWidget->setColumnWidth(0, 250);
    m_processTreeWidget->setColumnWidth(1, 70);
    m_processTreeWidget->setColumnWidth(2, 70);
    m_processTreeWidget->setColumnWidth(3, 100);
    m_processTreeWidget->setAlternatingRowColors(true);
    
    connect(m_processTreeWidget, &QTreeWidget::itemDoubleClicked,
            [this](QTreeWidgetItem* item, int) {
        quint32 pid = item->data(1, Qt::UserRole).toUInt();
        if (pid > 0) {
            navigateToProcess(pid);
        }
    });
    
    treeLayout->addWidget(m_processTreeWidget);
    layout->addWidget(treeGroup);
    
    return widget;
}

QWidget* AdvancedProcessDialog::createModulesTab()
{
    auto widget = new QWidget();
    auto layout = new QVBoxLayout(widget);
    
    m_moduleCountLabel = new QLabel(tr("Loading modules..."));
    layout->addWidget(m_moduleCountLabel);
    
    m_modulesTable = new QTableWidget();
    m_modulesTable->setColumnCount(4);
    m_modulesTable->setHorizontalHeaderLabels({tr("Name"), tr("Path"), tr("Base Address"), tr("Size")});
    m_modulesTable->horizontalHeader()->setStretchLastSection(true);
    m_modulesTable->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_modulesTable->setAlternatingRowColors(true);
    m_modulesTable->verticalHeader()->setVisible(false);
    m_modulesTable->setColumnWidth(0, 150);
    m_modulesTable->setColumnWidth(1, 350);
    m_modulesTable->setColumnWidth(2, 120);
    
    layout->addWidget(m_modulesTable);
    
    loadModules();
    
    return widget;
}

QWidget* AdvancedProcessDialog::createThreadsTab()
{
    auto widget = new QWidget();
    auto layout = new QVBoxLayout(widget);
    
    m_threadSummaryLabel = new QLabel(tr("Loading threads..."));
    layout->addWidget(m_threadSummaryLabel);
    
    m_threadsTable = new QTableWidget();
    m_threadsTable->setColumnCount(5);
    m_threadsTable->setHorizontalHeaderLabels({tr("Thread ID"), tr("Priority"), tr("State"), tr("Start Address"), tr("CPU Time")});
    m_threadsTable->horizontalHeader()->setStretchLastSection(true);
    m_threadsTable->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_threadsTable->setAlternatingRowColors(true);
    m_threadsTable->verticalHeader()->setVisible(false);
    
    layout->addWidget(m_threadsTable);
    
    loadThreads();
    
    return widget;
}

QWidget* AdvancedProcessDialog::createHandlesTab()
{
    auto widget = new QWidget();
    auto layout = new QVBoxLayout(widget);
    
    layout->addWidget(new QLabel(tr("Handle enumeration requires administrator privileges.")));
    
    return widget;
}

QWidget* AdvancedProcessDialog::createMemoryTab()
{
    auto widget = new QWidget();
    auto layout = new QVBoxLayout(widget);
    
    layout->addWidget(new QLabel(tr("Memory details will appear here...")));
    
    return widget;
}

QWidget* AdvancedProcessDialog::createSecurityTab()
{
    auto widget = new QWidget();
    auto layout = new QVBoxLayout(widget);
    
    layout->addWidget(new QLabel(tr("Security information will appear here...")));
    
    return widget;
}

void AdvancedProcessDialog::loadProcessInfo()
{
    const auto* proc = m_monitor->getProcessByPid(m_pid);
    if (!proc) {
        QMessageBox::warning(this, tr("Error"), tr("Process not found. It may have terminated."));
        return;
    }
    
    // Basic info
    m_nameLabel->setText(proc->name);
    m_descriptionLabel->setText(proc->displayName.isEmpty() ? proc->description : proc->displayName);
    m_pidLabel->setText(QString::number(proc->pid));
    
    // Parent PID with link
    const auto* parent = m_monitor->getProcessByPid(proc->parentPid);
    QString parentText = QString::number(proc->parentPid);
    if (parent) {
        parentText += QString(" (%1)").arg(parent->name);
    }
    m_parentPidLabel->setText(parentText);
    
    m_pathLabel->setText(proc->executablePath);
    m_userLabel->setText(proc->userName);
    
    // Status
    QString statusText;
    QString statusColor;
    switch (proc->state) {
        case ProcessState::Running:
            statusText = tr("Running");
            statusColor = "color: #00aa00;";
            m_isSuspended = false;
            break;
        case ProcessState::Suspended:
            statusText = tr("Suspended");
            statusColor = "color: #808080;";
            m_isSuspended = true;
            break;
        case ProcessState::NotResponding:
            statusText = tr("Not Responding");
            statusColor = "color: #ff0000;";
            m_isSuspended = false;
            break;
        default:
            statusText = tr("Unknown");
            break;
    }
    m_statusLabel->setText(statusText);
    m_statusLabel->setStyleSheet(QString("font-weight: bold; %1").arg(statusColor));
    
    m_architectureLabel->setText(proc->is64Bit ? "64-bit" : "32-bit");
    m_startTimeLabel->setText(proc->startTime.toString("yyyy-MM-dd hh:mm:ss"));
    m_cpuTimeLabel->setText(formatDuration(proc->cpuTimeMs));
    m_elevatedLabel->setText(proc->isElevated ? tr("Yes (Administrator)") : tr("No"));
    m_elevatedLabel->setStyleSheet(proc->isElevated ? "color: #ff8c00; font-weight: bold;" : "");
    
    // Update suspend/resume button
    m_suspendResumeBtn->setText(m_isSuspended ? tr("Resume") : tr("Suspend"));
    
    // Load priority
#ifdef _WIN32
    HANDLE hProcess = OpenProcess(PROCESS_QUERY_INFORMATION, FALSE, m_pid);
    if (hProcess) {
        int priority = getPriorityFromHandle(static_cast<void*>(hProcess));
        m_priorityCombo->blockSignals(true);
        m_priorityCombo->setCurrentIndex(priority);
        m_priorityCombo->blockSignals(false);
        
        // Load affinity
        DWORD_PTR processAffinity, systemAffinity;
        if (GetProcessAffinityMask(hProcess, &processAffinity, &systemAffinity)) {
            for (size_t i = 0; i < m_affinityChecks.size(); ++i) {
                m_affinityChecks[i]->blockSignals(true);
                m_affinityChecks[i]->setChecked((processAffinity >> i) & 1);
                m_affinityChecks[i]->blockSignals(false);
            }
        }
        
        CloseHandle(hProcess);
    }
#endif
    
    // Build process tree
    buildProcessTree();
    
    // Initial performance update
    refreshData();
}

void AdvancedProcessDialog::refreshData()
{
    const auto* proc = m_monitor->getProcessByPid(m_pid);
    if (!proc) return;
    
    // Update performance metrics
    m_cpuUsageLabel->setText(QString("%1%").arg(proc->cpuUsage, 0, 'f', 1));
    m_cpuKernelLabel->setText(QString("%1%").arg(proc->cpuUsageKernel, 0, 'f', 1));
    m_cpuUserLabel->setText(QString("%1%").arg(proc->cpuUsageUser, 0, 'f', 1));
    m_cpuProgressBar->setValue(static_cast<int>(proc->cpuUsage));
    m_cpuGraph->addValue(proc->cpuUsage);
    
    m_memoryUsageLabel->setText(formatBytes(proc->memoryBytes));
    m_memoryPrivateLabel->setText(formatBytes(proc->privateBytes));
    m_memoryPeakLabel->setText(formatBytes(proc->peakMemoryBytes));
    m_memoryGraph->addValue(proc->memoryBytes / (1024.0 * 1024.0)); // MB
    
    m_ioReadLabel->setText(formatBytes(proc->ioReadBytesPerSec) + "/s");
    m_ioWriteLabel->setText(formatBytes(proc->ioWriteBytesPerSec) + "/s");
    m_ioGraph->addValue((proc->ioReadBytesPerSec + proc->ioWriteBytesPerSec) / (1024.0 * 1024.0));
    
    m_threadCountLabel->setText(QString::number(proc->threadCount));
    m_handleCountLabel->setText(QString::number(proc->handleCount));
    m_cpuTimeLabel->setText(formatDuration(proc->cpuTimeMs));
    
    // Update status
    QString statusText;
    switch (proc->state) {
        case ProcessState::Running: statusText = tr("Running"); break;
        case ProcessState::Suspended: statusText = tr("Suspended"); break;
        case ProcessState::NotResponding: statusText = tr("Not Responding"); break;
        default: statusText = tr("Unknown"); break;
    }
    m_statusLabel->setText(statusText);
}

void AdvancedProcessDialog::buildProcessTree()
{
    m_processTreeWidget->clear();
    
    // Build ancestors string
    auto ancestors = m_monitor->getProcessAncestors(m_pid);
    QString ancestorText;
    
    for (auto it = ancestors.rbegin(); it != ancestors.rend(); ++it) {
        const auto* ancestor = m_monitor->getProcessByPid(*it);
        if (ancestor) {
            if (!ancestorText.isEmpty()) ancestorText += " → ";
            ancestorText += QString("%1 (PID: %2)").arg(ancestor->name).arg(ancestor->pid);
        }
    }
    
    const auto* currentProc = m_monitor->getProcessByPid(m_pid);
    if (currentProc) {
        if (!ancestorText.isEmpty()) ancestorText += " → ";
        ancestorText += QString("<b>%1 (PID: %2)</b>").arg(currentProc->name).arg(currentProc->pid);
    }
    
    if (ancestorText.isEmpty()) {
        ancestorText = tr("No parent process found (root process)");
    }
    
    m_ancestorsLabel->setText(ancestorText);
    
    // Build child tree
    auto children = m_monitor->getChildProcesses(m_pid);
    
    if (children.empty()) {
        auto noChildItem = new QTreeWidgetItem(m_processTreeWidget);
        noChildItem->setText(0, tr("No child processes"));
        noChildItem->setFlags(Qt::NoItemFlags);
    } else {
        for (quint32 childPid : children) {
            const auto* child = m_monitor->getProcessByPid(childPid);
            if (!child) continue;
            
            auto item = new QTreeWidgetItem(m_processTreeWidget);
            item->setText(0, child->name);
            item->setText(1, QString::number(child->pid));
            item->setText(2, QString("%1%").arg(child->cpuUsage, 0, 'f', 1));
            item->setText(3, formatBytes(child->memoryBytes));
            
            QString status;
            switch (child->state) {
                case ProcessState::Running: status = tr("Running"); break;
                case ProcessState::Suspended: status = tr("Suspended"); break;
                case ProcessState::NotResponding: status = tr("Not Responding"); break;
                default: status = tr("Unknown"); break;
            }
            item->setText(4, status);
            
            item->setData(1, Qt::UserRole, child->pid);
            item->setIcon(0, child->icon);
            
            // Add grandchildren recursively
            addChildProcesses(item, child->pid);
        }
    }
    
    m_processTreeWidget->expandAll();
}

void AdvancedProcessDialog::addChildProcesses(QTreeWidgetItem* parentItem, quint32 parentPid)
{
    auto children = m_monitor->getChildProcesses(parentPid);
    
    for (quint32 childPid : children) {
        const auto* child = m_monitor->getProcessByPid(childPid);
        if (!child) continue;
        
        auto item = new QTreeWidgetItem(parentItem);
        item->setText(0, child->name);
        item->setText(1, QString::number(child->pid));
        item->setText(2, QString("%1%").arg(child->cpuUsage, 0, 'f', 1));
        item->setText(3, formatBytes(child->memoryBytes));
        
        QString status;
        switch (child->state) {
            case ProcessState::Running: status = tr("Running"); break;
            case ProcessState::Suspended: status = tr("Suspended"); break;
            case ProcessState::NotResponding: status = tr("Not Responding"); break;
            default: status = tr("Unknown"); break;
        }
        item->setText(4, status);
        
        item->setData(1, Qt::UserRole, child->pid);
        item->setIcon(0, child->icon);
        
        // Recursively add children (limit depth to prevent infinite loops)
        if (parentItem->parent() == nullptr || parentItem->parent()->parent() == nullptr) {
            addChildProcesses(item, child->pid);
        }
    }
}

void AdvancedProcessDialog::loadModules()
{
#ifdef _WIN32
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, m_pid);
    if (snapshot == INVALID_HANDLE_VALUE) {
        m_moduleCountLabel->setText(tr("Cannot access modules (access denied)"));
        return;
    }
    
    MODULEENTRY32W me;
    me.dwSize = sizeof(me);
    
    std::vector<MODULEENTRY32W> modules;
    if (Module32FirstW(snapshot, &me)) {
        do {
            modules.push_back(me);
        } while (Module32NextW(snapshot, &me));
    }
    
    CloseHandle(snapshot);
    
    m_moduleCountLabel->setText(tr("%1 modules loaded").arg(modules.size()));
    
    m_modulesTable->setRowCount(static_cast<int>(modules.size()));
    
    for (int i = 0; i < static_cast<int>(modules.size()); ++i) {
        const auto& mod = modules[i];
        
        m_modulesTable->setItem(i, 0, new QTableWidgetItem(QString::fromWCharArray(mod.szModule)));
        m_modulesTable->setItem(i, 1, new QTableWidgetItem(QString::fromWCharArray(mod.szExePath)));
        m_modulesTable->setItem(i, 2, new QTableWidgetItem(
            QString("0x%1").arg(reinterpret_cast<quintptr>(mod.modBaseAddr), 16, 16, QChar('0'))));
        m_modulesTable->setItem(i, 3, new QTableWidgetItem(formatBytes(mod.modBaseSize)));
    }
#endif
}

void AdvancedProcessDialog::loadThreads()
{
#ifdef _WIN32
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
    if (snapshot == INVALID_HANDLE_VALUE) {
        m_threadSummaryLabel->setText(tr("Cannot enumerate threads"));
        return;
    }
    
    THREADENTRY32 te;
    te.dwSize = sizeof(te);
    
    std::vector<THREADENTRY32> threads;
    if (Thread32First(snapshot, &te)) {
        do {
            if (te.th32OwnerProcessID == m_pid) {
                threads.push_back(te);
            }
        } while (Thread32Next(snapshot, &te));
    }
    
    CloseHandle(snapshot);
    
    m_threadSummaryLabel->setText(tr("%1 threads").arg(threads.size()));
    
    m_threadsTable->setRowCount(static_cast<int>(threads.size()));
    
    for (int i = 0; i < static_cast<int>(threads.size()); ++i) {
        const auto& thread = threads[i];
        
        m_threadsTable->setItem(i, 0, new QTableWidgetItem(QString::number(thread.th32ThreadID)));
        m_threadsTable->setItem(i, 1, new QTableWidgetItem(QString::number(thread.tpBasePri)));
        
        // Try to get more thread info
        HANDLE hThread = OpenThread(THREAD_QUERY_INFORMATION, FALSE, thread.th32ThreadID);
        if (hThread) {
            // Get thread times
            FILETIME createTime, exitTime, kernelTime, userTime;
            if (GetThreadTimes(hThread, &createTime, &exitTime, &kernelTime, &userTime)) {
                ULONGLONG totalTime = 
                    ((ULONGLONG)kernelTime.dwHighDateTime << 32 | kernelTime.dwLowDateTime) +
                    ((ULONGLONG)userTime.dwHighDateTime << 32 | userTime.dwLowDateTime);
                totalTime /= 10000; // Convert to ms
                m_threadsTable->setItem(i, 4, new QTableWidgetItem(formatDuration(totalTime)));
            }
            CloseHandle(hThread);
        }
        
        m_threadsTable->setItem(i, 2, new QTableWidgetItem(tr("Running")));
        m_threadsTable->setItem(i, 3, new QTableWidgetItem(tr("N/A")));
    }
#endif
}

void AdvancedProcessDialog::loadHandles()
{
    // Handle enumeration requires NtQuerySystemInformation which is complex
}

void AdvancedProcessDialog::loadMemoryInfo()
{
    // Memory region enumeration requires VirtualQueryEx
}

void AdvancedProcessDialog::loadSecurityInfo()
{
    // Security info requires token queries
}

void AdvancedProcessDialog::onSuspendResume()
{
    if (m_isSuspended) {
        if (m_monitor->resumeProcess(m_pid)) {
            m_isSuspended = false;
            m_suspendResumeBtn->setText(tr("Suspend"));
            m_statusLabel->setText(tr("Running"));
            m_statusLabel->setStyleSheet("font-weight: bold; color: #00aa00;");
        } else {
            QMessageBox::warning(this, tr("Error"), tr("Failed to resume process."));
        }
    } else {
        auto reply = QMessageBox::question(this, tr("Suspend Process"),
            tr("Are you sure you want to suspend this process?\n\n"
               "Warning: Suspending system processes may cause system instability."),
            QMessageBox::Yes | QMessageBox::No, QMessageBox::No);
        
        if (reply == QMessageBox::Yes) {
            if (m_monitor->suspendProcess(m_pid)) {
                m_isSuspended = true;
                m_suspendResumeBtn->setText(tr("Resume"));
                m_statusLabel->setText(tr("Suspended"));
                m_statusLabel->setStyleSheet("font-weight: bold; color: #808080;");
            } else {
                QMessageBox::warning(this, tr("Error"), tr("Failed to suspend process."));
            }
        }
    }
}

void AdvancedProcessDialog::onTerminate()
{
    const auto* proc = m_monitor->getProcessByPid(m_pid);
    QString name = proc ? proc->name : QString::number(m_pid);
    
    auto reply = QMessageBox::question(this, tr("Terminate Process"),
        tr("Are you sure you want to terminate '%1' (PID: %2)?").arg(name).arg(m_pid),
        QMessageBox::Yes | QMessageBox::No, QMessageBox::No);
    
    if (reply == QMessageBox::Yes) {
        if (m_monitor->terminateProcess(m_pid)) {
            accept();
        } else {
            QMessageBox::warning(this, tr("Error"), tr("Failed to terminate process."));
        }
    }
}

void AdvancedProcessDialog::onTerminateTree()
{
    const auto* proc = m_monitor->getProcessByPid(m_pid);
    QString name = proc ? proc->name : QString::number(m_pid);
    
    auto children = m_monitor->getChildProcesses(m_pid);
    
    QString message = tr("Are you sure you want to terminate '%1' (PID: %2) "
                         "and all its %3 child processes?")
                      .arg(name).arg(m_pid).arg(children.size());
    
    auto reply = QMessageBox::question(this, tr("Terminate Process Tree"),
        message, QMessageBox::Yes | QMessageBox::No, QMessageBox::No);
    
    if (reply == QMessageBox::Yes) {
        if (m_monitor->terminateProcessTree(m_pid)) {
            accept();
        } else {
            QMessageBox::warning(this, tr("Error"), 
                tr("Failed to terminate some processes in the tree."));
        }
    }
}

void AdvancedProcessDialog::onOpenFileLocation()
{
    const auto* proc = m_monitor->getProcessByPid(m_pid);
    if (proc && !proc->executablePath.isEmpty()) {
        QFileInfo fileInfo(proc->executablePath);
        QDesktopServices::openUrl(QUrl::fromLocalFile(fileInfo.absolutePath()));
    }
}

void AdvancedProcessDialog::onCopyPath()
{
    const auto* proc = m_monitor->getProcessByPid(m_pid);
    if (proc && !proc->executablePath.isEmpty()) {
        QApplication::clipboard()->setText(proc->executablePath);
    }
}

void AdvancedProcessDialog::onPriorityChanged(int index)
{
    if (m_monitor->setProcessPriority(m_pid, index)) {
        // Success - no message needed
    } else {
        QMessageBox::warning(this, tr("Error"), tr("Failed to change process priority."));
        // Reload actual priority
        loadProcessInfo();
    }
}

void AdvancedProcessDialog::onAffinityChanged()
{
    quint64 mask = 0;
    for (size_t i = 0; i < m_affinityChecks.size(); ++i) {
        if (m_affinityChecks[i]->isChecked()) {
            mask |= (1ULL << i);
        }
    }
    
    if (mask == 0) {
        QMessageBox::warning(this, tr("Error"), 
            tr("At least one CPU must be selected."));
        loadProcessInfo(); // Reload to reset checkboxes
        return;
    }
    
    if (!m_monitor->setProcessAffinity(m_pid, mask)) {
        QMessageBox::warning(this, tr("Error"), tr("Failed to change process affinity."));
        loadProcessInfo();
    }
}

void AdvancedProcessDialog::navigateToProcess(quint32 pid)
{
    // Create a new dialog for the target process
    auto* dialog = new AdvancedProcessDialog(pid, m_monitor, parentWidget());
    dialog->setAttribute(Qt::WA_DeleteOnClose);
    dialog->show();
}

QString AdvancedProcessDialog::formatBytes(qint64 bytes)
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

QString AdvancedProcessDialog::formatDuration(qint64 msecs)
{
    qint64 secs = msecs / 1000;
    qint64 mins = secs / 60;
    qint64 hours = mins / 60;
    
    secs %= 60;
    mins %= 60;
    
    if (hours > 0) {
        return QString("%1:%2:%3")
            .arg(hours)
            .arg(mins, 2, 10, QChar('0'))
            .arg(secs, 2, 10, QChar('0'));
    }
    return QString("%1:%2")
        .arg(mins, 2, 10, QChar('0'))
        .arg(secs, 2, 10, QChar('0'));
}

int AdvancedProcessDialog::getPriorityFromHandle(void* hProcess)
{
#ifdef _WIN32
    DWORD priority = GetPriorityClass(static_cast<HANDLE>(hProcess));
    switch (priority) {
        case IDLE_PRIORITY_CLASS: return 0;
        case BELOW_NORMAL_PRIORITY_CLASS: return 1;
        case NORMAL_PRIORITY_CLASS: return 2;
        case ABOVE_NORMAL_PRIORITY_CLASS: return 3;
        case HIGH_PRIORITY_CLASS: return 4;
        case REALTIME_PRIORITY_CLASS: return 5;
        default: return 2;
    }
#else
    Q_UNUSED(hProcess);
    return 2;  // Normal priority
#endif
}

// ============================================================================
// ProcessHistoryDialog implementation
// ============================================================================

ProcessHistoryDialog::ProcessHistoryDialog(AdvancedProcessMonitor* monitor, QWidget *parent)
    : QDialog(parent)
    , m_monitor(monitor)
{
    setWindowTitle(tr("Process History"));
    setMinimumSize(700, 400);
    resize(800, 500);
    
    setupUi();
    refreshHistory();
    
    connect(m_monitor->historyManager(), &ProcessHistoryManager::processEnded,
            this, &ProcessHistoryDialog::refreshHistory);
}

void ProcessHistoryDialog::setupUi()
{
    auto layout = new QVBoxLayout(this);
    
    m_summaryLabel = new QLabel();
    layout->addWidget(m_summaryLabel);
    
    m_historyTable = new QTableWidget();
    m_historyTable->setColumnCount(7);
    m_historyTable->setHorizontalHeaderLabels({
        tr("Name"), tr("PID"), tr("Start Time"), tr("End Time"), 
        tr("Duration"), tr("Peak Memory"), tr("Reason")
    });
    m_historyTable->horizontalHeader()->setStretchLastSection(true);
    m_historyTable->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_historyTable->setAlternatingRowColors(true);
    m_historyTable->verticalHeader()->setVisible(false);
    m_historyTable->setColumnWidth(0, 150);
    m_historyTable->setColumnWidth(1, 70);
    m_historyTable->setColumnWidth(2, 140);
    m_historyTable->setColumnWidth(3, 140);
    m_historyTable->setColumnWidth(4, 80);
    m_historyTable->setColumnWidth(5, 100);
    
    connect(m_historyTable, &QTableWidget::cellDoubleClicked,
            this, &ProcessHistoryDialog::onItemDoubleClicked);
    
    layout->addWidget(m_historyTable);
    
    auto buttonLayout = new QHBoxLayout();
    
    m_refreshBtn = new QPushButton(tr("Refresh"));
    connect(m_refreshBtn, &QPushButton::clicked, this, &ProcessHistoryDialog::refreshHistory);
    buttonLayout->addWidget(m_refreshBtn);
    
    m_clearBtn = new QPushButton(tr("Clear History"));
    connect(m_clearBtn, &QPushButton::clicked, this, &ProcessHistoryDialog::clearHistory);
    buttonLayout->addWidget(m_clearBtn);
    
    buttonLayout->addStretch();
    
    auto closeBtn = new QPushButton(tr("Close"));
    connect(closeBtn, &QPushButton::clicked, this, &QDialog::accept);
    buttonLayout->addWidget(closeBtn);
    
    layout->addLayout(buttonLayout);
}

void ProcessHistoryDialog::refreshHistory()
{
    const auto& history = m_monitor->historyManager()->history();
    
    m_summaryLabel->setText(tr("%1 processes in history").arg(history.size()));
    
    m_historyTable->setRowCount(static_cast<int>(history.size()));
    
    int row = 0;
    for (const auto& entry : history) {
        m_historyTable->setItem(row, 0, new QTableWidgetItem(entry.name));
        m_historyTable->setItem(row, 1, new QTableWidgetItem(QString::number(entry.pid)));
        m_historyTable->setItem(row, 2, new QTableWidgetItem(
            entry.startTime.toString("yyyy-MM-dd hh:mm:ss")));
        m_historyTable->setItem(row, 3, new QTableWidgetItem(
            entry.endTime.toString("yyyy-MM-dd hh:mm:ss")));
        m_historyTable->setItem(row, 4, new QTableWidgetItem(
            formatDuration(entry.startTime, entry.endTime)));
        
        // Format peak memory
        const char* units[] = {"B", "KB", "MB", "GB"};
        int unitIndex = 0;
        double size = entry.peakMemoryBytes;
        while (size >= 1024.0 && unitIndex < 3) {
            size /= 1024.0;
            unitIndex++;
        }
        m_historyTable->setItem(row, 5, new QTableWidgetItem(
            QString("%1 %2").arg(size, 0, 'f', 1).arg(units[unitIndex])));
        
        m_historyTable->setItem(row, 6, new QTableWidgetItem(entry.terminationReason));
        
        row++;
    }
}

void ProcessHistoryDialog::clearHistory()
{
    auto reply = QMessageBox::question(this, tr("Clear History"),
        tr("Are you sure you want to clear the process history?"),
        QMessageBox::Yes | QMessageBox::No, QMessageBox::No);
    
    if (reply == QMessageBox::Yes) {
        m_monitor->historyManager()->clearHistory();
        refreshHistory();
    }
}

void ProcessHistoryDialog::onItemDoubleClicked(int row, int)
{
    auto* pathItem = m_historyTable->item(row, 0);
    if (pathItem) {
        QApplication::clipboard()->setText(pathItem->text());
    }
}

QString ProcessHistoryDialog::formatDuration(const QDateTime& start, const QDateTime& end)
{
    qint64 secs = start.secsTo(end);
    
    if (secs < 60) {
        return tr("%1 sec").arg(secs);
    }
    
    qint64 mins = secs / 60;
    secs %= 60;
    
    if (mins < 60) {
        return tr("%1m %2s").arg(mins).arg(secs);
    }
    
    qint64 hours = mins / 60;
    mins %= 60;
    
    if (hours < 24) {
        return tr("%1h %2m").arg(hours).arg(mins);
    }
    
    qint64 days = hours / 24;
    hours %= 24;
    
    return tr("%1d %2h").arg(days).arg(hours);
}
