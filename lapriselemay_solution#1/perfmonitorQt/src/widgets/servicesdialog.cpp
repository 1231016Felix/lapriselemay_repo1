#include "servicesdialog.h"
#include "servicehistorydialog.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QFormLayout>
#include <QGridLayout>
#include <QHeaderView>
#include <QMessageBox>
#include <QMenu>
#include <QClipboard>
#include <QApplication>
#include <QScrollArea>
#include <QDebug>

// ==================== ServicesDialog ====================

ServicesDialog::ServicesDialog(QWidget* parent)
    : QDialog(parent)
{
    setWindowTitle(tr("Windows Services Manager"));
    setMinimumSize(1200, 700);
    resize(1400, 800);
    
    // Enable maximize/minimize buttons and allow double-click to maximize
    setWindowFlags(windowFlags() | Qt::WindowMinMaxButtonsHint);
    
    m_monitor = std::make_unique<ServiceMonitor>();
    m_monitor->initialize();
    
    setupUi();
    
    // Connect signals
    connect(m_monitor.get(), &ServiceMonitor::servicesRefreshed,
            this, &ServicesDialog::onServiceSelectionChanged);
    connect(m_monitor.get(), &ServiceMonitor::serviceCrashed,
            this, &ServicesDialog::onCrashHistoryUpdated);
    connect(m_monitor.get(), &ServiceMonitor::errorOccurred,
            this, [this](const QString& error) {
                m_statusLabel->setText(tr("Error: %1").arg(error));
            });
    
    // Selection preservation: save before refresh
    connect(m_monitor.get(), &ServiceMonitor::aboutToRefresh, this, [this]() {
        if (!m_selectedService.isEmpty()) {
            m_pendingServiceSelection = m_selectedService;
        }
    });
    
    // Selection preservation: restore after refresh
    connect(m_monitor.get(), &ServiceMonitor::servicesRefreshed, this, [this]() {
        restoreSelection();
    });
    
    // Start auto-refresh
    m_refreshTimer = std::make_unique<QTimer>(this);
    connect(m_refreshTimer.get(), &QTimer::timeout, this, &ServicesDialog::updateResourceStats);
    m_refreshTimer->start(5000);
    
    m_monitor->startAutoRefresh(5000);
    onRefresh();
}

ServicesDialog::~ServicesDialog()
{
    m_monitor->stopAutoRefresh();
}

void ServicesDialog::setupUi()
{
    QVBoxLayout* mainLayout = new QVBoxLayout(this);
    mainLayout->setSpacing(10);
    
    createToolbar();
    createMainView();
    
    // Status bar
    QHBoxLayout* statusLayout = new QHBoxLayout();
    m_statusLabel = new QLabel();
    m_adminLabel = new QLabel();
    
    if (ServiceMonitor::isAdmin()) {
        m_adminLabel->setText(tr("✓ Running as Administrator"));
        m_adminLabel->setStyleSheet("color: #4CAF50; font-weight: bold;");
    } else {
        m_adminLabel->setText(tr("⚠ Not running as Administrator - Some operations may fail"));
        m_adminLabel->setStyleSheet("color: #FF9800; font-weight: bold;");
    }
    
    statusLayout->addWidget(m_statusLabel);
    statusLayout->addStretch();
    statusLayout->addWidget(m_adminLabel);
    
    mainLayout->addLayout(statusLayout);
}

void ServicesDialog::createToolbar()
{
    QHBoxLayout* toolbar = new QHBoxLayout();
    
    // Search
    QLabel* searchLabel = new QLabel(tr("Search:"));
    m_searchEdit = new QLineEdit();
    m_searchEdit->setPlaceholderText(tr("Filter services..."));
    m_searchEdit->setClearButtonEnabled(true);
    m_searchEdit->setMinimumWidth(200);
    connect(m_searchEdit, &QLineEdit::textChanged, this, &ServicesDialog::onSearchChanged);
    
    // State filter
    QLabel* stateLabel = new QLabel(tr("State:"));
    m_stateFilter = new QComboBox();
    m_stateFilter->addItem(tr("All"), -1);
    m_stateFilter->addItem(tr("Running"), static_cast<int>(ServiceState::Running));
    m_stateFilter->addItem(tr("Stopped"), static_cast<int>(ServiceState::Stopped));
    m_stateFilter->addItem(tr("Paused"), static_cast<int>(ServiceState::Paused));
    connect(m_stateFilter, QOverload<int>::of(&QComboBox::currentIndexChanged),
            this, &ServicesDialog::onFilterChanged);
    
    // Start type filter
    QLabel* startTypeLabel = new QLabel(tr("Startup:"));
    m_startTypeFilter = new QComboBox();
    m_startTypeFilter->addItem(tr("All"), -1);
    m_startTypeFilter->addItem(tr("Automatic"), static_cast<int>(ServiceStartType::Automatic));
    m_startTypeFilter->addItem(tr("Manual"), static_cast<int>(ServiceStartType::Manual));
    m_startTypeFilter->addItem(tr("Disabled"), static_cast<int>(ServiceStartType::Disabled));
    connect(m_startTypeFilter, QOverload<int>::of(&QComboBox::currentIndexChanged),
            this, &ServicesDialog::onFilterChanged);
    
    // Checkboxes
    m_showWindowsOnly = new QCheckBox(tr("Windows services only"));
    connect(m_showWindowsOnly, &QCheckBox::toggled, this, &ServicesDialog::onFilterChanged);
    
    m_showHighResourceOnly = new QCheckBox(tr("High resource only"));
    connect(m_showHighResourceOnly, &QCheckBox::toggled, this, &ServicesDialog::onFilterChanged);
    
    // Refresh button
    m_refreshButton = new QPushButton(tr("Refresh"));
    m_refreshButton->setIcon(style()->standardIcon(QStyle::SP_BrowserReload));
    connect(m_refreshButton, &QPushButton::clicked, this, &ServicesDialog::onRefresh);
    
    toolbar->addWidget(searchLabel);
    toolbar->addWidget(m_searchEdit);
    toolbar->addSpacing(20);
    toolbar->addWidget(stateLabel);
    toolbar->addWidget(m_stateFilter);
    toolbar->addWidget(startTypeLabel);
    toolbar->addWidget(m_startTypeFilter);
    toolbar->addSpacing(20);
    toolbar->addWidget(m_showWindowsOnly);
    toolbar->addWidget(m_showHighResourceOnly);
    toolbar->addStretch();
    toolbar->addWidget(m_refreshButton);
    
    static_cast<QVBoxLayout*>(layout())->addLayout(toolbar);
}

void ServicesDialog::createMainView()
{
    m_splitter = new QSplitter(Qt::Horizontal, this);
    
    // Left: Table view
    m_tableView = new QTableView();
    m_tableView->setModel(m_monitor->model());
    m_tableView->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_tableView->setSelectionMode(QAbstractItemView::SingleSelection);
    m_tableView->setSortingEnabled(true);
    m_tableView->setAlternatingRowColors(true);
    m_tableView->setContextMenuPolicy(Qt::CustomContextMenu);
    m_tableView->horizontalHeader()->setStretchLastSection(true);
    m_tableView->verticalHeader()->setVisible(false);
    m_tableView->setColumnWidth(0, 150);  // Name
    m_tableView->setColumnWidth(1, 200);  // Display Name
    m_tableView->setColumnWidth(2, 80);   // State
    m_tableView->setColumnWidth(3, 120);  // Startup Type
    m_tableView->setColumnWidth(4, 60);   // PID
    m_tableView->setColumnWidth(5, 60);   // CPU
    m_tableView->setColumnWidth(6, 80);   // Memory
    
    connect(m_tableView->selectionModel(), &QItemSelectionModel::selectionChanged,
            this, &ServicesDialog::onServiceSelectionChanged);
    connect(m_tableView, &QTableView::doubleClicked,
            this, &ServicesDialog::onServiceDoubleClicked);
    connect(m_tableView, &QTableView::customContextMenuRequested,
            this, [this](const QPoint& pos) {
                QModelIndex index = m_tableView->indexAt(pos);
                if (!index.isValid()) return;
                
                const ServiceInfo* svc = m_monitor->model()->getService(index.row());
                if (!svc) return;
                
                QMenu menu;
                menu.addAction(tr("Start"), this, &ServicesDialog::onStartClicked);
                menu.addAction(tr("Stop"), this, &ServicesDialog::onStopClicked);
                menu.addAction(tr("Restart"), this, &ServicesDialog::onRestartClicked);
                menu.addSeparator();
                menu.addAction(tr("Properties..."), this, [this, svc]() {
                    showServiceProperties(svc->serviceName);
                });
                menu.addAction(tr("Copy name"), this, [svc]() {
                    QApplication::clipboard()->setText(svc->serviceName);
                });
                menu.exec(m_tableView->viewport()->mapToGlobal(pos));
            });
    
    // Right: Details panel
    createDetailsPanel();
    
    m_splitter->addWidget(m_tableView);
    m_splitter->addWidget(m_detailsTabs);
    m_splitter->setStretchFactor(0, 2);
    m_splitter->setStretchFactor(1, 1);
    m_splitter->setSizes({800, 400});
    
    static_cast<QVBoxLayout*>(layout())->addWidget(m_splitter, 1);
}

void ServicesDialog::createDetailsPanel()
{
    m_detailsTabs = new QTabWidget();
    
    // General tab
    QWidget* generalTab = new QWidget();
    QVBoxLayout* generalLayout = new QVBoxLayout(generalTab);
    
    QGroupBox* infoGroup = new QGroupBox(tr("Service Information"));
    QFormLayout* infoLayout = new QFormLayout(infoGroup);
    
    m_detailNameLabel = new QLabel("-");
    m_detailDisplayNameLabel = new QLabel("-");
    m_detailDescriptionLabel = new QLabel("-");
    m_detailDescriptionLabel->setWordWrap(true);
    m_detailStateLabel = new QLabel("-");
    m_detailStartTypeLabel = new QLabel("-");
    m_detailPathLabel = new QLabel("-");
    m_detailPathLabel->setWordWrap(true);
    m_detailAccountLabel = new QLabel("-");
    
    infoLayout->addRow(tr("Name:"), m_detailNameLabel);
    infoLayout->addRow(tr("Display Name:"), m_detailDisplayNameLabel);
    infoLayout->addRow(tr("Description:"), m_detailDescriptionLabel);
    infoLayout->addRow(tr("State:"), m_detailStateLabel);
    infoLayout->addRow(tr("Startup Type:"), m_detailStartTypeLabel);
    infoLayout->addRow(tr("Path:"), m_detailPathLabel);
    infoLayout->addRow(tr("Account:"), m_detailAccountLabel);
    
    generalLayout->addWidget(infoGroup);
    
    // Resource usage
    QGroupBox* resourceGroup = new QGroupBox(tr("Resource Usage"));
    QGridLayout* resourceLayout = new QGridLayout(resourceGroup);
    
    m_detailPidLabel = new QLabel("-");
    m_detailCpuLabel = new QLabel("-");
    m_detailMemoryLabel = new QLabel("-");
    m_detailThreadsLabel = new QLabel("-");
    m_detailHandlesLabel = new QLabel("-");
    
    resourceLayout->addWidget(new QLabel(tr("PID:")), 0, 0);
    resourceLayout->addWidget(m_detailPidLabel, 0, 1);
    resourceLayout->addWidget(new QLabel(tr("CPU:")), 0, 2);
    resourceLayout->addWidget(m_detailCpuLabel, 0, 3);
    resourceLayout->addWidget(new QLabel(tr("Memory:")), 1, 0);
    resourceLayout->addWidget(m_detailMemoryLabel, 1, 1);
    resourceLayout->addWidget(new QLabel(tr("Threads:")), 1, 2);
    resourceLayout->addWidget(m_detailThreadsLabel, 1, 3);
    resourceLayout->addWidget(new QLabel(tr("Handles:")), 2, 0);
    resourceLayout->addWidget(m_detailHandlesLabel, 2, 1);
    
    generalLayout->addWidget(resourceGroup);
    
    // Actions
    QGroupBox* actionGroup = new QGroupBox(tr("Actions"));
    QVBoxLayout* actionLayout = new QVBoxLayout(actionGroup);
    
    QHBoxLayout* buttonsLayout = new QHBoxLayout();
    m_startButton = new QPushButton(tr("Start"));
    m_stopButton = new QPushButton(tr("Stop"));
    m_restartButton = new QPushButton(tr("Restart"));
    
    connect(m_startButton, &QPushButton::clicked, this, &ServicesDialog::onStartClicked);
    connect(m_stopButton, &QPushButton::clicked, this, &ServicesDialog::onStopClicked);
    connect(m_restartButton, &QPushButton::clicked, this, &ServicesDialog::onRestartClicked);
    
    buttonsLayout->addWidget(m_startButton);
    buttonsLayout->addWidget(m_stopButton);
    buttonsLayout->addWidget(m_restartButton);
    buttonsLayout->addStretch();
    
    actionLayout->addLayout(buttonsLayout);
    
    QHBoxLayout* startupLayout = new QHBoxLayout();
    m_startupTypeCombo = new QComboBox();
    m_startupTypeCombo->addItem(tr("Automatic"), static_cast<int>(ServiceStartType::Automatic));
    m_startupTypeCombo->addItem(tr("Automatic (Delayed)"), static_cast<int>(ServiceStartType::AutomaticDelayed));
    m_startupTypeCombo->addItem(tr("Manual"), static_cast<int>(ServiceStartType::Manual));
    m_startupTypeCombo->addItem(tr("Disabled"), static_cast<int>(ServiceStartType::Disabled));
    
    m_applyStartupButton = new QPushButton(tr("Apply"));
    connect(m_applyStartupButton, &QPushButton::clicked, this, &ServicesDialog::onChangeStartupType);
    
    startupLayout->addWidget(new QLabel(tr("Startup Type:")));
    startupLayout->addWidget(m_startupTypeCombo);
    startupLayout->addWidget(m_applyStartupButton);
    startupLayout->addStretch();
    
    actionLayout->addLayout(startupLayout);
    
    generalLayout->addWidget(actionGroup);
    generalLayout->addStretch();
    
    m_detailsTabs->addTab(generalTab, tr("General"));
    
    // Dependencies tab
    QWidget* depsTab = new QWidget();
    QVBoxLayout* depsLayout = new QVBoxLayout(depsTab);
    
    m_dependenciesTree = new QTreeWidget();
    m_dependenciesTree->setHeaderLabels({tr("Service"), tr("State")});
    m_dependenciesTree->setAlternatingRowColors(true);
    depsLayout->addWidget(m_dependenciesTree);
    
    m_detailsTabs->addTab(depsTab, tr("Dependencies"));
    
    // Crash history tab
    createCrashHistoryTab();
    
    // High resource tab
    createHighResourceTab();
}

void ServicesDialog::createCrashHistoryTab()
{
    QWidget* crashTab = new QWidget();
    QVBoxLayout* crashLayout = new QVBoxLayout(crashTab);
    
    QLabel* crashInfo = new QLabel(tr("Services that have crashed or stopped unexpectedly:"));
    crashLayout->addWidget(crashInfo);
    
    m_crashTable = new QTableWidget();
    m_crashTable->setColumnCount(5);
    m_crashTable->setHorizontalHeaderLabels({
        tr("Time"), tr("Service"), tr("Display Name"), tr("Reason"), tr("Crash Count (24h)")
    });
    m_crashTable->horizontalHeader()->setStretchLastSection(true);
    m_crashTable->setAlternatingRowColors(true);
    m_crashTable->setEditTriggers(QAbstractItemView::NoEditTriggers);
    crashLayout->addWidget(m_crashTable);
    
    QPushButton* clearCrashButton = new QPushButton(tr("Clear History"));
    connect(clearCrashButton, &QPushButton::clicked, this, [this]() {
        m_monitor->clearCrashHistory();
        m_crashTable->setRowCount(0);
    });
    
    QHBoxLayout* crashButtons = new QHBoxLayout();
    crashButtons->addStretch();
    crashButtons->addWidget(clearCrashButton);
    crashLayout->addLayout(crashButtons);
    
    m_detailsTabs->addTab(crashTab, tr("Crash History"));
}

void ServicesDialog::createHighResourceTab()
{
    QWidget* resourceTab = new QWidget();
    QVBoxLayout* resourceLayout = new QVBoxLayout(resourceTab);
    
    // High CPU
    QGroupBox* cpuGroup = new QGroupBox(tr("High CPU Usage (> 5%)"));
    QVBoxLayout* cpuLayout = new QVBoxLayout(cpuGroup);
    
    m_highCpuTable = new QTableWidget();
    m_highCpuTable->setColumnCount(4);
    m_highCpuTable->setHorizontalHeaderLabels({tr("Service"), tr("Display Name"), tr("CPU %"), tr("PID")});
    m_highCpuTable->horizontalHeader()->setStretchLastSection(true);
    m_highCpuTable->setAlternatingRowColors(true);
    m_highCpuTable->setMaximumHeight(200);
    cpuLayout->addWidget(m_highCpuTable);
    
    resourceLayout->addWidget(cpuGroup);
    
    // High Memory
    QGroupBox* memGroup = new QGroupBox(tr("High Memory Usage (> 100 MB)"));
    QVBoxLayout* memLayout = new QVBoxLayout(memGroup);
    
    m_highMemoryTable = new QTableWidget();
    m_highMemoryTable->setColumnCount(4);
    m_highMemoryTable->setHorizontalHeaderLabels({tr("Service"), tr("Display Name"), tr("Memory"), tr("PID")});
    m_highMemoryTable->horizontalHeader()->setStretchLastSection(true);
    m_highMemoryTable->setAlternatingRowColors(true);
    m_highMemoryTable->setMaximumHeight(200);
    memLayout->addWidget(m_highMemoryTable);
    
    resourceLayout->addWidget(memGroup);
    resourceLayout->addStretch();
    
    m_detailsTabs->addTab(resourceTab, tr("High Resource"));
}

void ServicesDialog::onRefresh()
{
    m_statusLabel->setText(tr("Refreshing..."));
    m_monitor->refresh();
    m_statusLabel->setText(tr("Loaded %1 services").arg(m_monitor->services().size()));
    
    updateResourceStats();
    onCrashHistoryUpdated();
}

void ServicesDialog::onSearchChanged(const QString& text)
{
    ServiceFilter filter;
    filter.searchText = text;
    filter.showRunning = true;
    filter.showStopped = true;
    filter.showDisabled = true;
    
    m_monitor->model()->setFilter(filter);
}

void ServicesDialog::onFilterChanged()
{
    ServiceFilter filter;
    filter.searchText = m_searchEdit->text();
    
    int stateIdx = m_stateFilter->currentData().toInt();
    filter.showRunning = (stateIdx == -1 || stateIdx == static_cast<int>(ServiceState::Running));
    filter.showStopped = (stateIdx == -1 || stateIdx == static_cast<int>(ServiceState::Stopped));
    filter.showDisabled = true;
    
    filter.showWindowsOnly = m_showWindowsOnly->isChecked();
    filter.showHighResourceOnly = m_showHighResourceOnly->isChecked();
    
    m_monitor->model()->setFilter(filter);
}

void ServicesDialog::onServiceSelectionChanged()
{
    QModelIndexList selection = m_tableView->selectionModel()->selectedRows();
    if (selection.isEmpty()) {
        m_selectedService.clear();
        updateServiceDetails(nullptr);
        return;
    }
    
    const ServiceInfo* svc = m_monitor->model()->getService(selection.first().row());
    if (svc) {
        m_selectedService = svc->serviceName;
        updateServiceDetails(svc);
    }
}

void ServicesDialog::updateServiceDetails(const ServiceInfo* service)
{
    if (!service) {
        m_detailNameLabel->setText("-");
        m_detailDisplayNameLabel->setText("-");
        m_detailDescriptionLabel->setText("-");
        m_detailStateLabel->setText("-");
        m_detailStartTypeLabel->setText("-");
        m_detailPathLabel->setText("-");
        m_detailAccountLabel->setText("-");
        m_detailPidLabel->setText("-");
        m_detailCpuLabel->setText("-");
        m_detailMemoryLabel->setText("-");
        m_detailThreadsLabel->setText("-");
        m_detailHandlesLabel->setText("-");
        m_dependenciesTree->clear();
        
        updateActionButtons(nullptr);
        return;
    }
    
    m_detailNameLabel->setText(service->serviceName);
    m_detailDisplayNameLabel->setText(service->displayName);
    m_detailDescriptionLabel->setText(service->description.isEmpty() ? "-" : service->description);
    m_detailStateLabel->setText(service->stateString());
    m_detailStateLabel->setStyleSheet(QString("color: %1; font-weight: bold;")
        .arg(service->stateColor().name()));
    m_detailStartTypeLabel->setText(service->startTypeString());
    m_detailPathLabel->setText(service->imagePath.isEmpty() ? "-" : service->imagePath);
    m_detailAccountLabel->setText(service->account.isEmpty() ? "-" : service->account);
    
    if (service->state == ServiceState::Running && service->processId > 0) {
        m_detailPidLabel->setText(QString::number(service->processId));
        m_detailCpuLabel->setText(QString("%1%").arg(service->resources.cpuUsagePercent, 0, 'f', 1));
        m_detailMemoryLabel->setText(ServiceMonitor::formatBytes(service->resources.memoryUsageBytes));
        m_detailThreadsLabel->setText(QString::number(service->resources.threadCount));
        m_detailHandlesLabel->setText(QString::number(service->resources.handleCount));
    } else {
        m_detailPidLabel->setText("-");
        m_detailCpuLabel->setText("-");
        m_detailMemoryLabel->setText("-");
        m_detailThreadsLabel->setText("-");
        m_detailHandlesLabel->setText("-");
    }
    
    // Set startup type combo
    int typeIndex = m_startupTypeCombo->findData(static_cast<int>(service->startType));
    if (typeIndex >= 0) {
        m_startupTypeCombo->setCurrentIndex(typeIndex);
    }
    
    // Update dependencies tree
    m_dependenciesTree->clear();
    
    QTreeWidgetItem* depsItem = new QTreeWidgetItem(m_dependenciesTree);
    depsItem->setText(0, tr("This service depends on:"));
    for (const QString& dep : service->dependencies) {
        const ServiceInfo* depSvc = m_monitor->getService(dep);
        QTreeWidgetItem* item = new QTreeWidgetItem(depsItem);
        item->setText(0, dep);
        item->setText(1, depSvc ? depSvc->stateString() : tr("Unknown"));
    }
    depsItem->setExpanded(true);
    
    QTreeWidgetItem* deptsItem = new QTreeWidgetItem(m_dependenciesTree);
    deptsItem->setText(0, tr("Services that depend on this:"));
    for (const QString& dept : service->dependents) {
        const ServiceInfo* deptSvc = m_monitor->getService(dept);
        QTreeWidgetItem* item = new QTreeWidgetItem(deptsItem);
        item->setText(0, dept);
        item->setText(1, deptSvc ? deptSvc->stateString() : tr("Unknown"));
    }
    deptsItem->setExpanded(true);
    
    updateActionButtons(service);
}

void ServicesDialog::updateActionButtons(const ServiceInfo* service)
{
    bool isAdmin = ServiceMonitor::isAdmin();
    
    if (!service) {
        m_startButton->setEnabled(false);
        m_stopButton->setEnabled(false);
        m_restartButton->setEnabled(false);
        m_applyStartupButton->setEnabled(false);
        return;
    }
    
    bool isRunning = (service->state == ServiceState::Running);
    bool isStopped = (service->state == ServiceState::Stopped);
    bool canStop = service->canStop && !service->isSystemCritical;
    
    m_startButton->setEnabled(isAdmin && isStopped);
    m_stopButton->setEnabled(isAdmin && isRunning && canStop);
    m_restartButton->setEnabled(isAdmin && isRunning && canStop);
    m_applyStartupButton->setEnabled(isAdmin);
}

void ServicesDialog::onStartClicked()
{
    if (m_selectedService.isEmpty()) return;
    
    m_statusLabel->setText(tr("Starting %1...").arg(m_selectedService));
    
    if (m_monitor->startService(m_selectedService)) {
        m_statusLabel->setText(tr("Service started successfully"));
    } else {
        m_statusLabel->setText(tr("Failed to start service: %1").arg(m_monitor->lastError()));
    }
}

void ServicesDialog::onStopClicked()
{
    if (m_selectedService.isEmpty()) return;
    
    const ServiceInfo* svc = m_monitor->getService(m_selectedService);
    if (svc && svc->isSystemCritical) {
        QMessageBox::warning(this, tr("Warning"),
            tr("This is a system-critical service. Stopping it may cause system instability."));
        return;
    }
    
    m_statusLabel->setText(tr("Stopping %1...").arg(m_selectedService));
    
    if (m_monitor->stopService(m_selectedService)) {
        m_statusLabel->setText(tr("Service stopped successfully"));
    } else {
        m_statusLabel->setText(tr("Failed to stop service: %1").arg(m_monitor->lastError()));
    }
}

void ServicesDialog::onRestartClicked()
{
    if (m_selectedService.isEmpty()) return;
    
    m_statusLabel->setText(tr("Restarting %1...").arg(m_selectedService));
    
    if (m_monitor->restartService(m_selectedService)) {
        m_statusLabel->setText(tr("Service restarted successfully"));
    } else {
        m_statusLabel->setText(tr("Failed to restart service: %1").arg(m_monitor->lastError()));
    }
}

void ServicesDialog::onChangeStartupType()
{
    if (m_selectedService.isEmpty()) return;
    
    ServiceStartType newType = static_cast<ServiceStartType>(
        m_startupTypeCombo->currentData().toInt());
    
    if (m_monitor->setStartType(m_selectedService, newType)) {
        m_statusLabel->setText(tr("Startup type changed successfully"));
    } else {
        m_statusLabel->setText(tr("Failed to change startup type: %1").arg(m_monitor->lastError()));
    }
}

void ServicesDialog::onServiceDoubleClicked(const QModelIndex& index)
{
    const ServiceInfo* svc = m_monitor->model()->getService(index.row());
    if (svc) {
        showServiceProperties(svc->serviceName);
    }
}

void ServicesDialog::onCrashHistoryUpdated()
{
    m_crashTable->setRowCount(0);
    
    const auto& events = m_monitor->crashEvents();
    for (const auto& evt : events) {
        int row = m_crashTable->rowCount();
        m_crashTable->insertRow(row);
        
        m_crashTable->setItem(row, 0, new QTableWidgetItem(evt.timestamp.toString("dd/MM/yyyy HH:mm:ss")));
        m_crashTable->setItem(row, 1, new QTableWidgetItem(evt.serviceName));
        m_crashTable->setItem(row, 2, new QTableWidgetItem(evt.displayName));
        m_crashTable->setItem(row, 3, new QTableWidgetItem(evt.failureReason));
        m_crashTable->setItem(row, 4, new QTableWidgetItem(QString::number(evt.crashCount)));
    }
}

void ServicesDialog::updateResourceStats()
{
    // Update high CPU table
    m_highCpuTable->setRowCount(0);
    auto highCpu = m_monitor->getTopByCpu(10);
    for (const auto& svc : highCpu) {
        if (svc.resources.cpuUsagePercent < 0.1) continue;
        
        int row = m_highCpuTable->rowCount();
        m_highCpuTable->insertRow(row);
        
        m_highCpuTable->setItem(row, 0, new QTableWidgetItem(svc.serviceName));
        m_highCpuTable->setItem(row, 1, new QTableWidgetItem(svc.displayName));
        m_highCpuTable->setItem(row, 2, new QTableWidgetItem(
            QString("%1%").arg(svc.resources.cpuUsagePercent, 0, 'f', 1)));
        m_highCpuTable->setItem(row, 3, new QTableWidgetItem(QString::number(svc.processId)));
    }
    
    // Update high memory table
    m_highMemoryTable->setRowCount(0);
    auto highMem = m_monitor->getTopByMemory(10);
    for (const auto& svc : highMem) {
        if (svc.resources.memoryUsageBytes < 1024 * 1024) continue;
        
        int row = m_highMemoryTable->rowCount();
        m_highMemoryTable->insertRow(row);
        
        m_highMemoryTable->setItem(row, 0, new QTableWidgetItem(svc.serviceName));
        m_highMemoryTable->setItem(row, 1, new QTableWidgetItem(svc.displayName));
        m_highMemoryTable->setItem(row, 2, new QTableWidgetItem(
            ServiceMonitor::formatBytes(svc.resources.memoryUsageBytes)));
        m_highMemoryTable->setItem(row, 3, new QTableWidgetItem(QString::number(svc.processId)));
    }
}

void ServicesDialog::showServiceProperties(const QString& serviceName)
{
    const ServiceInfo* svc = m_monitor->getService(serviceName);
    if (!svc) return;
    
    ServicePropertiesDialog dlg(*svc, m_monitor.get(), this);
    dlg.exec();
    onRefresh();
}

int ServicesDialog::findServiceRow(const QString& serviceName) const
{
    if (serviceName.isEmpty()) return -1;
    
    for (int row = 0; row < m_monitor->model()->rowCount(); ++row) {
        const ServiceInfo* svc = m_monitor->model()->getService(row);
        if (svc && svc->serviceName == serviceName) {
            return row;
        }
    }
    return -1;
}

void ServicesDialog::restoreSelection()
{
    if (m_pendingServiceSelection.isEmpty()) return;
    
    QString serviceToRestore = m_pendingServiceSelection;
    
    QTimer::singleShot(0, this, [this, serviceToRestore]() {
        int row = findServiceRow(serviceToRestore);
        if (row >= 0) {
            QModelIndex index = m_monitor->model()->index(row, 0);
            if (index.isValid()) {
                m_tableView->selectionModel()->blockSignals(true);
                m_tableView->setCurrentIndex(index);
                m_tableView->scrollTo(index);
                m_tableView->selectionModel()->blockSignals(false);
            }
        }
    });
}

// ==================== ServicePropertiesDialog ====================

ServicePropertiesDialog::ServicePropertiesDialog(const ServiceInfo& service,
                                                   ServiceMonitor* monitor,
                                                   QWidget* parent)
    : QDialog(parent)
    , m_service(service)
    , m_monitor(monitor)
{
    setWindowTitle(tr("Service Properties - %1").arg(service.displayName));
    setMinimumSize(450, 400);
    
    setupUi();
    updateState();
}

void ServicePropertiesDialog::setupUi()
{
    QVBoxLayout* mainLayout = new QVBoxLayout(this);
    
    // Info group
    QGroupBox* infoGroup = new QGroupBox(tr("Service Information"));
    QFormLayout* infoLayout = new QFormLayout(infoGroup);
    
    infoLayout->addRow(tr("Service name:"), new QLabel(m_service.serviceName));
    infoLayout->addRow(tr("Display name:"), new QLabel(m_service.displayName));
    
    QLabel* descLabel = new QLabel(m_service.description);
    descLabel->setWordWrap(true);
    infoLayout->addRow(tr("Description:"), descLabel);
    
    QLabel* pathLabel = new QLabel(m_service.imagePath);
    pathLabel->setWordWrap(true);
    infoLayout->addRow(tr("Path:"), pathLabel);
    
    m_stateLabel = new QLabel();
    infoLayout->addRow(tr("Status:"), m_stateLabel);
    
    mainLayout->addWidget(infoGroup);
    
    // Startup type
    QGroupBox* startupGroup = new QGroupBox(tr("Startup Type"));
    QHBoxLayout* startupLayout = new QHBoxLayout(startupGroup);
    
    m_startupTypeCombo = new QComboBox();
    m_startupTypeCombo->addItem(tr("Automatic"), static_cast<int>(ServiceStartType::Automatic));
    m_startupTypeCombo->addItem(tr("Automatic (Delayed)"), static_cast<int>(ServiceStartType::AutomaticDelayed));
    m_startupTypeCombo->addItem(tr("Manual"), static_cast<int>(ServiceStartType::Manual));
    m_startupTypeCombo->addItem(tr("Disabled"), static_cast<int>(ServiceStartType::Disabled));
    
    int idx = m_startupTypeCombo->findData(static_cast<int>(m_service.startType));
    if (idx >= 0) m_startupTypeCombo->setCurrentIndex(idx);
    
    startupLayout->addWidget(m_startupTypeCombo);
    startupLayout->addStretch();
    
    mainLayout->addWidget(startupGroup);
    
    // Actions
    QGroupBox* actionGroup = new QGroupBox(tr("Service Status"));
    QHBoxLayout* actionLayout = new QHBoxLayout(actionGroup);
    
    m_startButton = new QPushButton(tr("Start"));
    m_stopButton = new QPushButton(tr("Stop"));
    
    connect(m_startButton, &QPushButton::clicked, this, &ServicePropertiesDialog::onStartClicked);
    connect(m_stopButton, &QPushButton::clicked, this, &ServicePropertiesDialog::onStopClicked);
    
    actionLayout->addWidget(m_startButton);
    actionLayout->addWidget(m_stopButton);
    actionLayout->addStretch();
    
    mainLayout->addWidget(actionGroup);
    mainLayout->addStretch();
    
    // Buttons
    QHBoxLayout* buttonLayout = new QHBoxLayout();
    buttonLayout->addStretch();
    
    QPushButton* applyButton = new QPushButton(tr("Apply"));
    connect(applyButton, &QPushButton::clicked, this, &ServicePropertiesDialog::onApplyClicked);
    buttonLayout->addWidget(applyButton);
    
    QPushButton* closeButton = new QPushButton(tr("Close"));
    connect(closeButton, &QPushButton::clicked, this, &QDialog::accept);
    buttonLayout->addWidget(closeButton);
    
    mainLayout->addLayout(buttonLayout);
}

void ServicePropertiesDialog::updateState()
{
    m_stateLabel->setText(m_service.stateString());
    m_stateLabel->setStyleSheet(QString("color: %1; font-weight: bold;")
        .arg(m_service.stateColor().name()));
    
    bool isAdmin = ServiceMonitor::isAdmin();
    bool isRunning = (m_service.state == ServiceState::Running);
    bool isStopped = (m_service.state == ServiceState::Stopped);
    
    m_startButton->setEnabled(isAdmin && isStopped);
    m_stopButton->setEnabled(isAdmin && isRunning && m_service.canStop);
}

void ServicePropertiesDialog::onApplyClicked()
{
    ServiceStartType newType = static_cast<ServiceStartType>(
        m_startupTypeCombo->currentData().toInt());
    
    if (m_monitor->setStartType(m_service.serviceName, newType)) {
        m_service.startType = newType;
        QMessageBox::information(this, tr("Success"), tr("Startup type changed successfully."));
    } else {
        QMessageBox::warning(this, tr("Error"), 
            tr("Failed to change startup type: %1").arg(m_monitor->lastError()));
    }
}

void ServicePropertiesDialog::onStartClicked()
{
    if (m_monitor->startService(m_service.serviceName)) {
        m_service.state = ServiceState::Running;
        updateState();
    } else {
        QMessageBox::warning(this, tr("Error"),
            tr("Failed to start service: %1").arg(m_monitor->lastError()));
    }
}

void ServicePropertiesDialog::onStopClicked()
{
    if (m_monitor->stopService(m_service.serviceName)) {
        m_service.state = ServiceState::Stopped;
        updateState();
    } else {
        QMessageBox::warning(this, tr("Error"),
            tr("Failed to stop service: %1").arg(m_monitor->lastError()));
    }
}
