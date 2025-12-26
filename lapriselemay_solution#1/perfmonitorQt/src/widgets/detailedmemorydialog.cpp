#include "detailedmemorydialog.h"
#include "../monitors/detailedmemorymonitor.h"
#include "sparklinegraph.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QGroupBox>
#include <QHeaderView>
#include <QMessageBox>
#include <QFileDialog>
#include <QTextStream>
#include <QDateTime>
#include <QApplication>
#include <QClipboard>
#include <QMenu>

DetailedMemoryDialog::DetailedMemoryDialog(QWidget *parent)
    : QDialog(parent)
    , m_monitor(std::make_unique<DetailedMemoryMonitor>())
{
    setWindowTitle(tr("Detailed Memory Monitor"));
    setMinimumSize(900, 700);
    resize(1000, 750);
    setWindowFlags(windowFlags() | Qt::WindowMaximizeButtonHint);
    
    setupUi();
    
    // Connect signals
    connect(m_monitor.get(), &DetailedMemoryMonitor::refreshed,
            this, &DetailedMemoryDialog::onRefreshed);
    connect(m_monitor.get(), &DetailedMemoryMonitor::potentialLeakDetected,
            this, &DetailedMemoryDialog::onPotentialLeakDetected);
    connect(m_monitor.get(), &DetailedMemoryMonitor::systemMemoryLow,
            this, &DetailedMemoryDialog::onSystemMemoryLow);
    
    // Start monitoring
    m_monitor->startAutoRefresh(2000);
    
    // Initial update
    onRefreshed();
}

DetailedMemoryDialog::~DetailedMemoryDialog()
{
    m_monitor->stopAutoRefresh();
}

void DetailedMemoryDialog::setupUi()
{
    auto mainLayout = new QVBoxLayout(this);
    
    m_tabWidget = new QTabWidget();
    
    createOverviewTab();
    createProcessesTab();
    createLeakDetectionTab();
    createCompositionTab();
    
    mainLayout->addWidget(m_tabWidget);
    
    // Bottom buttons
    auto buttonLayout = new QHBoxLayout();
    
    auto exportBtn = new QPushButton(tr("📊 Export Report"));
    connect(exportBtn, &QPushButton::clicked, this, &DetailedMemoryDialog::exportReport);
    buttonLayout->addWidget(exportBtn);
    
    buttonLayout->addStretch();
    
    auto closeBtn = new QPushButton(tr("Close"));
    connect(closeBtn, &QPushButton::clicked, this, &QDialog::accept);
    buttonLayout->addWidget(closeBtn);
    
    mainLayout->addLayout(buttonLayout);
}

void DetailedMemoryDialog::createOverviewTab()
{
    m_overviewTab = new QWidget();
    auto layout = new QVBoxLayout(m_overviewTab);
    
    // Physical Memory Group
    auto physicalGroup = new QGroupBox(tr("Physical Memory (RAM)"));
    auto physicalLayout = new QGridLayout(physicalGroup);
    
    physicalLayout->addWidget(new QLabel(tr("In Use:")), 0, 0);
    m_physicalUsedLabel = new QLabel("---");
    m_physicalUsedLabel->setStyleSheet("font-weight: bold; font-size: 14px;");
    physicalLayout->addWidget(m_physicalUsedLabel, 0, 1);
    
    physicalLayout->addWidget(new QLabel(tr("Total:")), 0, 2);
    m_physicalTotalLabel = new QLabel("---");
    m_physicalTotalLabel->setStyleSheet("font-weight: bold;");
    physicalLayout->addWidget(m_physicalTotalLabel, 0, 3);
    
    m_physicalProgress = new QProgressBar();
    m_physicalProgress->setMinimumHeight(25);
    m_physicalProgress->setTextVisible(true);
    physicalLayout->addWidget(m_physicalProgress, 1, 0, 1, 4);
    
    m_memoryGraph = new SparklineGraph(120, QColor(139, 0, 139));
    m_memoryGraph->setMinimumHeight(100);
    physicalLayout->addWidget(m_memoryGraph, 2, 0, 1, 4);
    
    layout->addWidget(physicalGroup);
    
    // Commit Charge Group
    auto commitGroup = new QGroupBox(tr("Commit Charge (Virtual Memory)"));
    auto commitLayout = new QGridLayout(commitGroup);
    
    commitLayout->addWidget(new QLabel(tr("Committed:")), 0, 0);
    m_commitUsedLabel = new QLabel("---");
    m_commitUsedLabel->setStyleSheet("font-weight: bold;");
    commitLayout->addWidget(m_commitUsedLabel, 0, 1);
    
    commitLayout->addWidget(new QLabel(tr("Limit:")), 0, 2);
    m_commitLimitLabel = new QLabel("---");
    commitLayout->addWidget(m_commitLimitLabel, 0, 3);
    
    m_commitProgress = new QProgressBar();
    m_commitProgress->setMinimumHeight(20);
    commitLayout->addWidget(m_commitProgress, 1, 0, 1, 4);
    
    m_commitGraph = new SparklineGraph(120, QColor(0, 120, 215));
    m_commitGraph->setMinimumHeight(80);
    commitLayout->addWidget(m_commitGraph, 2, 0, 1, 4);
    
    layout->addWidget(commitGroup);
    
    // System Details Group
    auto detailsGroup = new QGroupBox(tr("System Details"));
    auto detailsLayout = new QGridLayout(detailsGroup);
    
    detailsLayout->addWidget(new QLabel(tr("System Cache:")), 0, 0);
    m_cacheLabel = new QLabel("---");
    detailsLayout->addWidget(m_cacheLabel, 0, 1);
    
    detailsLayout->addWidget(new QLabel(tr("Kernel Paged:")), 0, 2);
    m_kernelPagedLabel = new QLabel("---");
    detailsLayout->addWidget(m_kernelPagedLabel, 0, 3);
    
    detailsLayout->addWidget(new QLabel(tr("Kernel Non-Paged:")), 1, 0);
    m_kernelNonPagedLabel = new QLabel("---");
    detailsLayout->addWidget(m_kernelNonPagedLabel, 1, 1);
    
    detailsLayout->addWidget(new QLabel(tr("Processes:")), 1, 2);
    m_processCountLabel = new QLabel("---");
    detailsLayout->addWidget(m_processCountLabel, 1, 3);
    
    detailsLayout->addWidget(new QLabel(tr("Handles:")), 2, 0);
    m_handleCountLabel = new QLabel("---");
    detailsLayout->addWidget(m_handleCountLabel, 2, 1);
    
    layout->addWidget(detailsGroup);
    layout->addStretch();
    
    m_tabWidget->addTab(m_overviewTab, tr("📊 Overview"));
}

void DetailedMemoryDialog::createProcessesTab()
{
    m_processesTab = new QWidget();
    auto layout = new QVBoxLayout(m_processesTab);
    
    // Filter
    auto filterLayout = new QHBoxLayout();
    filterLayout->addWidget(new QLabel(tr("Filter:")));
    m_filterEdit = new QLineEdit();
    m_filterEdit->setPlaceholderText(tr("Type to filter processes..."));
    m_filterEdit->setClearButtonEnabled(true);
    connect(m_filterEdit, &QLineEdit::textChanged, this, &DetailedMemoryDialog::onFilterChanged);
    filterLayout->addWidget(m_filterEdit);
    layout->addLayout(filterLayout);
    
    // Process table
    m_processTable = new QTableView();
    m_processTable->setAlternatingRowColors(true);
    m_processTable->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_processTable->setSelectionMode(QAbstractItemView::SingleSelection);
    m_processTable->setSortingEnabled(true);
    m_processTable->horizontalHeader()->setStretchLastSection(true);
    m_processTable->horizontalHeader()->setSectionResizeMode(QHeaderView::Interactive);
    m_processTable->verticalHeader()->setVisible(false);
    m_processTable->setContextMenuPolicy(Qt::CustomContextMenu);
    
    // Setup proxy model for filtering
    m_proxyModel = new QSortFilterProxyModel(this);
    m_proxyModel->setSourceModel(m_monitor->model());
    m_proxyModel->setFilterCaseSensitivity(Qt::CaseInsensitive);
    m_proxyModel->setFilterKeyColumn(0); // Filter by name
    m_processTable->setModel(m_proxyModel);
    
    connect(m_processTable, &QTableView::doubleClicked,
            this, &DetailedMemoryDialog::onProcessDoubleClicked);
    
    // Context menu
    connect(m_processTable, &QTableView::customContextMenuRequested,
            this, [this](const QPoint& pos) {
        auto index = m_processTable->indexAt(pos);
        if (!index.isValid()) return;
        
        auto sourceIndex = m_proxyModel->mapToSource(index);
        auto proc = static_cast<ProcessMemoryModel*>(m_monitor->model())->getProcess(sourceIndex.row());
        if (!proc) return;
        
        QMenu menu;
        menu.addAction(tr("Copy Process Name"), [proc]() {
            QApplication::clipboard()->setText(proc->name);
        });
        menu.addAction(tr("Copy PID"), [proc]() {
            QApplication::clipboard()->setText(QString::number(proc->pid));
        });
        menu.addAction(tr("Copy Memory Details"), [this, proc]() {
            QString details = QString("%1 (PID: %2)\n"
                                     "Working Set: %3\n"
                                     "Private WS: %4\n"
                                     "Private Bytes: %5\n"
                                     "Virtual: %6")
                .arg(proc->name)
                .arg(proc->pid)
                .arg(formatBytes(proc->workingSetSize))
                .arg(formatBytes(proc->privateWorkingSet))
                .arg(formatBytes(proc->privateBytes))
                .arg(formatBytes(proc->virtualBytes));
            QApplication::clipboard()->setText(details);
        });
        menu.exec(m_processTable->viewport()->mapToGlobal(pos));
    });
    
    layout->addWidget(m_processTable);
    
    // Selected process info
    m_selectedProcessLabel = new QLabel();
    m_selectedProcessLabel->setWordWrap(true);
    layout->addWidget(m_selectedProcessLabel);
    
    m_tabWidget->addTab(m_processesTab, tr("📋 Processes"));
}

void DetailedMemoryDialog::createLeakDetectionTab()
{
    m_leakTab = new QWidget();
    auto layout = new QVBoxLayout(m_leakTab);
    
    // Settings
    auto settingsGroup = new QGroupBox(tr("Leak Detection Settings"));
    auto settingsLayout = new QHBoxLayout(settingsGroup);
    
    m_leakDetectionCheckbox = new QCheckBox(tr("Enable leak detection"));
    m_leakDetectionCheckbox->setChecked(m_monitor->isLeakDetectionEnabled());
    connect(m_leakDetectionCheckbox, &QCheckBox::toggled, 
            m_monitor.get(), &DetailedMemoryMonitor::setLeakDetectionEnabled);
    settingsLayout->addWidget(m_leakDetectionCheckbox);
    
    settingsLayout->addStretch();
    
    m_leakThresholdLabel = new QLabel(tr("Threshold: >10 MB/min growth for 5+ samples"));
    m_leakThresholdLabel->setStyleSheet("color: gray;");
    settingsLayout->addWidget(m_leakThresholdLabel);
    
    layout->addWidget(settingsGroup);
    
    // Status
    m_leakStatusLabel = new QLabel(tr("✓ No memory leaks detected"));
    m_leakStatusLabel->setStyleSheet("font-size: 14px; font-weight: bold; color: green; padding: 10px;");
    layout->addWidget(m_leakStatusLabel);
    
    // Leak list
    auto leakGroup = new QGroupBox(tr("Potential Memory Leaks"));
    auto leakLayout = new QVBoxLayout(leakGroup);
    
    m_leakTree = new QTreeWidget();
    m_leakTree->setHeaderLabels({tr("Process"), tr("PID"), tr("Growth Rate"), 
                                  tr("Private Bytes"), tr("Consecutive Growth")});
    m_leakTree->setAlternatingRowColors(true);
    m_leakTree->setRootIsDecorated(false);
    m_leakTree->header()->setSectionResizeMode(QHeaderView::ResizeToContents);
    leakLayout->addWidget(m_leakTree);
    
    // Info label
    auto infoLabel = new QLabel(
        tr("💡 A potential memory leak is flagged when a process shows sustained memory growth "
           "(>10 MB/min) for at least 5 consecutive samples. This may indicate the process "
           "is not properly releasing memory."));
    infoLabel->setWordWrap(true);
    infoLabel->setStyleSheet("color: gray; padding: 5px;");
    leakLayout->addWidget(infoLabel);
    
    layout->addWidget(leakGroup);
    
    m_tabWidget->addTab(m_leakTab, tr("🔍 Leak Detection"));
}

void DetailedMemoryDialog::createCompositionTab()
{
    m_compositionTab = new QWidget();
    auto layout = new QVBoxLayout(m_compositionTab);
    
    // Memory composition tree
    auto compositionGroup = new QGroupBox(tr("Memory Composition"));
    auto compLayout = new QVBoxLayout(compositionGroup);
    
    m_compositionTree = new QTreeWidget();
    m_compositionTree->setHeaderLabels({tr("Category"), tr("Size"), tr("Percentage")});
    m_compositionTree->setAlternatingRowColors(true);
    m_compositionTree->header()->setSectionResizeMode(QHeaderView::ResizeToContents);
    compLayout->addWidget(m_compositionTree);
    
    layout->addWidget(compositionGroup);
    
    // Top consumers
    auto topGroup = new QGroupBox(tr("Top Memory Consumers"));
    auto topLayout = new QVBoxLayout(topGroup);
    
    m_topConsumersLabel = new QLabel();
    m_topConsumersLabel->setWordWrap(true);
    m_topConsumersLabel->setStyleSheet("font-family: monospace;");
    topLayout->addWidget(m_topConsumersLabel);
    
    layout->addWidget(topGroup);
    
    m_tabWidget->addTab(m_compositionTab, tr("🧩 Composition"));
}

void DetailedMemoryDialog::onRefreshed()
{
    updateOverview();
    updateLeakList();
    updateComposition();
    
    // Resize columns on first update
    static bool firstUpdate = true;
    if (firstUpdate) {
        m_processTable->resizeColumnsToContents();
        firstUpdate = false;
    }
}

void DetailedMemoryDialog::updateOverview()
{
    const auto& sys = m_monitor->systemMemory();
    
    // Physical memory
    m_physicalUsedLabel->setText(formatBytes(sys.usedPhysical));
    m_physicalTotalLabel->setText(formatBytes(sys.totalPhysical));
    
    double physicalPercent = sys.totalPhysical > 0 
        ? (static_cast<double>(sys.usedPhysical) / sys.totalPhysical * 100.0) 
        : 0;
    m_physicalProgress->setValue(static_cast<int>(physicalPercent));
    m_physicalProgress->setFormat(QString("%1% (%2 / %3)")
        .arg(physicalPercent, 0, 'f', 1)
        .arg(formatBytes(sys.usedPhysical))
        .arg(formatBytes(sys.totalPhysical)));
    
    // Color based on usage
    QString progressStyle;
    if (physicalPercent >= 90) {
        progressStyle = "QProgressBar::chunk { background-color: #ff4444; }";
    } else if (physicalPercent >= 75) {
        progressStyle = "QProgressBar::chunk { background-color: #ffaa00; }";
    } else {
        progressStyle = "QProgressBar::chunk { background-color: #8b008b; }";
    }
    m_physicalProgress->setStyleSheet(progressStyle);
    
    m_memoryGraph->addValue(physicalPercent);
    
    // Commit charge
    m_commitUsedLabel->setText(formatBytes(sys.commitTotal));
    m_commitLimitLabel->setText(formatBytes(sys.commitLimit));
    
    double commitPercent = sys.commitLimit > 0 
        ? (static_cast<double>(sys.commitTotal) / sys.commitLimit * 100.0) 
        : 0;
    m_commitProgress->setValue(static_cast<int>(commitPercent));
    m_commitProgress->setFormat(QString("%1%").arg(commitPercent, 0, 'f', 1));
    m_commitGraph->addValue(commitPercent);
    
    // System details
    m_cacheLabel->setText(formatBytes(sys.systemCache));
    m_kernelPagedLabel->setText(formatBytes(sys.kernelPaged));
    m_kernelNonPagedLabel->setText(formatBytes(sys.kernelNonPaged));
    m_processCountLabel->setText(QString::number(sys.processCount));
    m_handleCountLabel->setText(QString::number(sys.handleCount));
}

void DetailedMemoryDialog::updateLeakList()
{
    auto leaks = m_monitor->getPotentialLeaks();
    
    m_leakTree->clear();
    
    if (leaks.empty()) {
        m_leakStatusLabel->setText(tr("✓ No memory leaks detected"));
        m_leakStatusLabel->setStyleSheet("font-size: 14px; font-weight: bold; color: green; padding: 10px;");
    } else {
        m_leakStatusLabel->setText(tr("⚠️ %1 potential memory leak(s) detected!").arg(leaks.size()));
        m_leakStatusLabel->setStyleSheet("font-size: 14px; font-weight: bold; color: red; padding: 10px;");
        
        for (const auto& leak : leaks) {
            auto item = new QTreeWidgetItem(m_leakTree);
            item->setText(0, leak.name);
            item->setText(1, QString::number(leak.pid));
            item->setText(2, QString("+%1 MB/min").arg(leak.growthRateMBPerMin, 0, 'f', 2));
            item->setText(3, formatBytes(leak.privateBytes));
            item->setText(4, QString::number(leak.consecutiveGrowthCount));
            
            // Color the row
            for (int i = 0; i < 5; i++) {
                item->setForeground(i, QBrush(Qt::red));
            }
        }
    }
    
    // Also show growing processes (not yet flagged as leaks)
    for (const auto& proc : m_monitor->processes()) {
        if (!proc.isPotentialLeak && proc.consecutiveGrowthCount >= 3) {
            auto item = new QTreeWidgetItem(m_leakTree);
            item->setText(0, proc.name + " (growing)");
            item->setText(1, QString::number(proc.pid));
            item->setText(2, QString("+%1 MB/min").arg(proc.growthRateMBPerMin, 0, 'f', 2));
            item->setText(3, formatBytes(proc.privateBytes));
            item->setText(4, QString::number(proc.consecutiveGrowthCount));
            
            for (int i = 0; i < 5; i++) {
                item->setForeground(i, QBrush(QColor(255, 165, 0))); // Orange
            }
        }
    }
}

void DetailedMemoryDialog::updateComposition()
{
    const auto& sys = m_monitor->systemMemory();
    
    m_compositionTree->clear();
    
    // Calculate percentages based on total physical memory
    auto addItem = [this, &sys](const QString& name, qint64 size) -> QTreeWidgetItem* {
        auto item = new QTreeWidgetItem(m_compositionTree);
        item->setText(0, name);
        item->setText(1, formatBytes(size));
        
        double percent = sys.totalPhysical > 0 
            ? (static_cast<double>(size) / sys.totalPhysical * 100.0) 
            : 0;
        item->setText(2, formatPercent(percent));
        return item;
    };
    
    // Physical memory breakdown
    auto physicalItem = new QTreeWidgetItem(m_compositionTree);
    physicalItem->setText(0, tr("Physical Memory"));
    physicalItem->setText(1, formatBytes(sys.totalPhysical));
    physicalItem->setText(2, "100%");
    physicalItem->setExpanded(true);
    
    auto usedChild = new QTreeWidgetItem(physicalItem);
    usedChild->setText(0, tr("  In Use"));
    usedChild->setText(1, formatBytes(sys.usedPhysical));
    usedChild->setText(2, formatPercent(static_cast<double>(sys.usedPhysical) / sys.totalPhysical * 100.0));
    
    auto availChild = new QTreeWidgetItem(physicalItem);
    availChild->setText(0, tr("  Available"));
    availChild->setText(1, formatBytes(sys.availablePhysical));
    availChild->setText(2, formatPercent(static_cast<double>(sys.availablePhysical) / sys.totalPhysical * 100.0));
    
    // Kernel memory
    auto kernelItem = new QTreeWidgetItem(m_compositionTree);
    kernelItem->setText(0, tr("Kernel Memory"));
    kernelItem->setText(1, formatBytes(sys.kernelTotal));
    kernelItem->setText(2, formatPercent(static_cast<double>(sys.kernelTotal) / sys.totalPhysical * 100.0));
    kernelItem->setExpanded(true);
    
    auto pagedChild = new QTreeWidgetItem(kernelItem);
    pagedChild->setText(0, tr("  Paged Pool"));
    pagedChild->setText(1, formatBytes(sys.kernelPaged));
    
    auto nonPagedChild = new QTreeWidgetItem(kernelItem);
    nonPagedChild->setText(0, tr("  Non-Paged Pool"));
    nonPagedChild->setText(1, formatBytes(sys.kernelNonPaged));
    
    // System cache
    addItem(tr("System Cache"), sys.systemCache);
    
    // Commit charge
    auto commitItem = new QTreeWidgetItem(m_compositionTree);
    commitItem->setText(0, tr("Commit Charge"));
    commitItem->setText(1, formatBytes(sys.commitTotal));
    commitItem->setText(2, QString("%1 of %2")
        .arg(formatBytes(sys.commitTotal))
        .arg(formatBytes(sys.commitLimit)));
    
    // Top consumers
    auto topProcesses = m_monitor->getTopByPrivateBytes(10);
    QString topText;
    int rank = 1;
    for (const auto& proc : topProcesses) {
        topText += QString("%1. %2 - %3 (PID: %4)\n")
            .arg(rank++, 2)
            .arg(proc.name, -25)
            .arg(formatBytes(proc.privateBytes), 12)
            .arg(proc.pid);
    }
    m_topConsumersLabel->setText(topText);
}

void DetailedMemoryDialog::onFilterChanged(const QString& text)
{
    m_proxyModel->setFilterFixedString(text);
}

void DetailedMemoryDialog::onProcessDoubleClicked(const QModelIndex& index)
{
    auto sourceIndex = m_proxyModel->mapToSource(index);
    auto proc = static_cast<ProcessMemoryModel*>(m_monitor->model())->getProcess(sourceIndex.row());
    if (!proc) return;
    
    QString details = QString(
        "<b>%1</b> (PID: %2)<br><br>"
        "<table>"
        "<tr><td><b>Working Set:</b></td><td>%3</td></tr>"
        "<tr><td>  Private:</td><td>%4</td></tr>"
        "<tr><td>  Shared:</td><td>%5</td></tr>"
        "<tr><td>  Peak:</td><td>%6</td></tr>"
        "<tr><td><b>Private Bytes:</b></td><td>%7</td></tr>"
        "<tr><td><b>Virtual Bytes:</b></td><td>%8</td></tr>"
        "<tr><td><b>Page Faults/s:</b></td><td>%9</td></tr>"
        "</table>"
        "<br><b>Path:</b> %10")
        .arg(proc->name)
        .arg(proc->pid)
        .arg(formatBytes(proc->workingSetSize))
        .arg(formatBytes(proc->privateWorkingSet))
        .arg(formatBytes(proc->sharedWorkingSet))
        .arg(formatBytes(proc->peakWorkingSet))
        .arg(formatBytes(proc->privateBytes))
        .arg(formatBytes(proc->virtualBytes))
        .arg(proc->pageFaultsDelta)
        .arg(proc->executablePath.isEmpty() ? tr("N/A") : proc->executablePath);
    
    m_selectedProcessLabel->setText(details);
}

void DetailedMemoryDialog::onPotentialLeakDetected(quint32 pid, const QString& name, double growthRate)
{
    // Switch to leak detection tab
    m_tabWidget->setCurrentWidget(m_leakTab);
    
    // Show notification
    QMessageBox::warning(this, tr("Potential Memory Leak"),
        tr("Process <b>%1</b> (PID: %2) shows signs of a memory leak.<br><br>"
           "Growth rate: <b>%3 MB/min</b><br><br>"
           "Consider monitoring this process or restarting it if memory usage becomes excessive.")
        .arg(name).arg(pid).arg(growthRate, 0, 'f', 2));
}

void DetailedMemoryDialog::onSystemMemoryLow(double usagePercent)
{
    QMessageBox::warning(this, tr("Low Memory Warning"),
        tr("System memory usage is critically high: <b>%1%</b><br><br>"
           "Consider closing some applications to free up memory.")
        .arg(usagePercent, 0, 'f', 1));
}

void DetailedMemoryDialog::exportReport()
{
    QString fileName = QFileDialog::getSaveFileName(this,
        tr("Export Memory Report"),
        QString("memory_report_%1.txt").arg(QDateTime::currentDateTime().toString("yyyyMMdd_HHmmss")),
        tr("Text Files (*.txt);;CSV Files (*.csv);;All Files (*)"));
    
    if (fileName.isEmpty()) return;
    
    QFile file(fileName);
    if (!file.open(QIODevice::WriteOnly | QIODevice::Text)) {
        QMessageBox::critical(this, tr("Error"), 
            tr("Could not open file for writing: %1").arg(file.errorString()));
        return;
    }
    
    QTextStream out(&file);
    const auto& sys = m_monitor->systemMemory();
    
    out << "=== Memory Report ===" << "\n";
    out << "Generated: " << QDateTime::currentDateTime().toString(Qt::ISODate) << "\n\n";
    
    out << "--- System Memory ---" << "\n";
    out << "Physical Memory: " << formatBytes(sys.usedPhysical) << " / " << formatBytes(sys.totalPhysical) << "\n";
    out << "Commit Charge: " << formatBytes(sys.commitTotal) << " / " << formatBytes(sys.commitLimit) << "\n";
    out << "System Cache: " << formatBytes(sys.systemCache) << "\n";
    out << "Kernel Paged: " << formatBytes(sys.kernelPaged) << "\n";
    out << "Kernel Non-Paged: " << formatBytes(sys.kernelNonPaged) << "\n";
    out << "Processes: " << sys.processCount << "\n";
    out << "Handles: " << sys.handleCount << "\n\n";
    
    out << "--- Top Memory Consumers ---" << "\n";
    auto topProcesses = m_monitor->getTopByPrivateBytes(20);
    for (const auto& proc : topProcesses) {
        out << QString("%1\t%2\tWS: %3\tPrivate: %4\n")
            .arg(proc.pid, 6)
            .arg(proc.name, -30)
            .arg(formatBytes(proc.workingSetSize), 12)
            .arg(formatBytes(proc.privateBytes), 12);
    }
    out << "\n";
    
    auto leaks = m_monitor->getPotentialLeaks();
    if (!leaks.empty()) {
        out << "--- Potential Memory Leaks ---" << "\n";
        for (const auto& leak : leaks) {
            out << QString("%1 (PID: %2) - Growth: %3 MB/min, Private: %4\n")
                .arg(leak.name)
                .arg(leak.pid)
                .arg(leak.growthRateMBPerMin, 0, 'f', 2)
                .arg(formatBytes(leak.privateBytes));
        }
    }
    
    file.close();
    
    QMessageBox::information(this, tr("Export Complete"),
        tr("Memory report exported to:\n%1").arg(fileName));
}

QString DetailedMemoryDialog::formatBytes(qint64 bytes) const
{
    return DetailedMemoryMonitor::formatBytes(bytes);
}

QString DetailedMemoryDialog::formatPercent(double percent) const
{
    return QString("%1%").arg(percent, 0, 'f', 1);
}
