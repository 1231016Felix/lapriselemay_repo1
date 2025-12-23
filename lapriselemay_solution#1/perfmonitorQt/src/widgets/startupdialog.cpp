#include "startupdialog.h"
#include "../monitors/startupmonitor.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QHeaderView>
#include <QMessageBox>
#include <QFileDialog>
#include <QClipboard>
#include <QApplication>
#include <QMenu>
#include <QAction>
#include <QFile>
#include <QTextStream>
#include <QStandardPaths>

// ============================================================================
// StartupFilterProxy Implementation
// ============================================================================

StartupFilterProxy::StartupFilterProxy(QObject* parent)
    : QSortFilterProxyModel(parent)
{
    setFilterCaseSensitivity(Qt::CaseInsensitive);
}

void StartupFilterProxy::setSourceFilter(int source)
{
    m_sourceFilter = source;
    invalidateFilter();
}

void StartupFilterProxy::setStatusFilter(int status)
{
    m_statusFilter = status;
    invalidateFilter();
}

bool StartupFilterProxy::filterAcceptsRow(int source_row, const QModelIndex &source_parent) const
{
    Q_UNUSED(source_parent);
    
    auto model = qobject_cast<StartupTableModel*>(sourceModel());
    if (!model) return true;
    
    const StartupEntry* entry = model->getEntry(source_row);
    if (!entry) return true;
    
    // Text filter
    if (!filterRegularExpression().pattern().isEmpty()) {
        bool matches = entry->name.contains(filterRegularExpression()) ||
                      entry->publisher.contains(filterRegularExpression()) ||
                      entry->command.contains(filterRegularExpression());
        if (!matches) return false;
    }
    
    // Source filter
    if (m_sourceFilter >= 0) {
        if (static_cast<int>(entry->source) != m_sourceFilter) {
            return false;
        }
    }
    
    // Status filter
    if (m_statusFilter >= 0) {
        bool wantEnabled = (m_statusFilter == 1);
        if (entry->isEnabled != wantEnabled) {
            return false;
        }
    }
    
    return true;
}

bool StartupFilterProxy::lessThan(const QModelIndex &left, const QModelIndex &right) const
{
    QVariant leftData = sourceModel()->data(left, Qt::DisplayRole);
    QVariant rightData = sourceModel()->data(right, Qt::DisplayRole);
    
    return leftData.toString().toLower() < rightData.toString().toLower();
}


// ============================================================================
// StartupDialog Implementation
// ============================================================================

StartupDialog::StartupDialog(QWidget *parent)
    : QDialog(parent)
{
    setWindowTitle(tr("Startup Manager"));
    setMinimumSize(1000, 650);
    resize(1150, 750);
    setWindowFlags(windowFlags() | Qt::WindowMaximizeButtonHint);
    
    m_monitor = std::make_unique<StartupMonitor>();
    m_proxyModel = std::make_unique<StartupFilterProxy>();
    m_proxyModel->setSourceModel(m_monitor->model());
    m_proxyModel->setSortRole(Qt::DisplayRole);
    
    setupUi();
    updateStats();
    
    connect(m_monitor.get(), &StartupMonitor::refreshed, this, &StartupDialog::updateStats);
    connect(m_monitor.get(), &StartupMonitor::errorOccurred, this, &StartupDialog::onError);
}

StartupDialog::~StartupDialog() = default;

void StartupDialog::setupUi()
{
    auto mainLayout = new QVBoxLayout(this);
    mainLayout->setSpacing(10);
    
    createToolbar();
    createFilters();
    createTable();
    createDetailsPanel();
    createStatsBar();
}

void StartupDialog::createToolbar()
{
    auto toolbar = new QHBoxLayout();
    
    m_refreshBtn = new QPushButton(tr("🔄 Refresh"));
    m_refreshBtn->setToolTip(tr("Refresh startup entries list"));
    connect(m_refreshBtn, &QPushButton::clicked, this, &StartupDialog::onRefresh);
    toolbar->addWidget(m_refreshBtn);
    
    toolbar->addSpacing(20);
    
    m_enableBtn = new QPushButton(tr("✓ Enable"));
    m_enableBtn->setEnabled(false);
    m_enableBtn->setStyleSheet("QPushButton { background-color: #2e7d32; }");
    connect(m_enableBtn, &QPushButton::clicked, this, &StartupDialog::onToggleEnabled);
    toolbar->addWidget(m_enableBtn);
    
    m_disableBtn = new QPushButton(tr("✗ Disable"));
    m_disableBtn->setEnabled(false);
    m_disableBtn->setStyleSheet("QPushButton { background-color: #f57c00; }");
    connect(m_disableBtn, &QPushButton::clicked, this, &StartupDialog::onToggleEnabled);
    toolbar->addWidget(m_disableBtn);
    
    m_deleteBtn = new QPushButton(tr("🗑 Delete"));
    m_deleteBtn->setEnabled(false);
    m_deleteBtn->setStyleSheet("QPushButton { background-color: #c62828; }");
    connect(m_deleteBtn, &QPushButton::clicked, this, &StartupDialog::onDelete);
    toolbar->addWidget(m_deleteBtn);
    
    toolbar->addSpacing(20);
    
    m_openLocationBtn = new QPushButton(tr("📁 Open Location"));
    m_openLocationBtn->setEnabled(false);
    connect(m_openLocationBtn, &QPushButton::clicked, this, &StartupDialog::onOpenLocation);
    toolbar->addWidget(m_openLocationBtn);
    
    m_openFileBtn = new QPushButton(tr("📂 Open File"));
    m_openFileBtn->setEnabled(false);
    connect(m_openFileBtn, &QPushButton::clicked, this, &StartupDialog::onOpenFileLocation);
    toolbar->addWidget(m_openFileBtn);
    
    toolbar->addStretch();
    
    m_addBtn = new QPushButton(tr("➕ Add"));
    m_addBtn->setToolTip(tr("Add new startup entry"));
    connect(m_addBtn, &QPushButton::clicked, this, &StartupDialog::onAddEntry);
    toolbar->addWidget(m_addBtn);
    
    m_exportBtn = new QPushButton(tr("📋 Export"));
    m_exportBtn->setToolTip(tr("Export startup list to file"));
    connect(m_exportBtn, &QPushButton::clicked, this, &StartupDialog::onExport);
    toolbar->addWidget(m_exportBtn);
    
    qobject_cast<QVBoxLayout*>(layout())->addLayout(toolbar);
}

void StartupDialog::createFilters()
{
    auto filterLayout = new QHBoxLayout();
    
    filterLayout->addWidget(new QLabel(tr("Search:")));
    m_searchEdit = new QLineEdit();
    m_searchEdit->setPlaceholderText(tr("Filter by name, publisher, or command..."));
    m_searchEdit->setClearButtonEnabled(true);
    m_searchEdit->setMinimumWidth(300);
    connect(m_searchEdit, &QLineEdit::textChanged, this, &StartupDialog::onFilterChanged);
    filterLayout->addWidget(m_searchEdit);
    
    filterLayout->addSpacing(20);
    
    filterLayout->addWidget(new QLabel(tr("Source:")));
    m_sourceCombo = new QComboBox();
    m_sourceCombo->addItem(tr("All Sources"), -1);
    m_sourceCombo->addItem(tr("Registry (User)"), static_cast<int>(StartupSource::RegistryCurrentUser));
    m_sourceCombo->addItem(tr("Registry (System)"), static_cast<int>(StartupSource::RegistryLocalMachine));
    m_sourceCombo->addItem(tr("Startup Folder (User)"), static_cast<int>(StartupSource::StartupFolderUser));
    m_sourceCombo->addItem(tr("Startup Folder (All Users)"), static_cast<int>(StartupSource::StartupFolderCommon));
    m_sourceCombo->addItem(tr("Task Scheduler"), static_cast<int>(StartupSource::TaskScheduler));
    m_sourceCombo->addItem(tr("Services"), static_cast<int>(StartupSource::Services));
    connect(m_sourceCombo, QOverload<int>::of(&QComboBox::currentIndexChanged), 
            this, &StartupDialog::onFilterChanged);
    filterLayout->addWidget(m_sourceCombo);
    
    filterLayout->addWidget(new QLabel(tr("Status:")));
    m_statusCombo = new QComboBox();
    m_statusCombo->addItem(tr("All"), -1);
    m_statusCombo->addItem(tr("Enabled"), 1);
    m_statusCombo->addItem(tr("Disabled"), 0);
    connect(m_statusCombo, QOverload<int>::of(&QComboBox::currentIndexChanged), 
            this, &StartupDialog::onFilterChanged);
    filterLayout->addWidget(m_statusCombo);
    
    filterLayout->addStretch();
    
    qobject_cast<QVBoxLayout*>(layout())->addLayout(filterLayout);
}


void StartupDialog::createTable()
{
    m_tableView = new QTableView();
    m_tableView->setModel(m_proxyModel.get());
    m_tableView->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_tableView->setSelectionMode(QAbstractItemView::SingleSelection);
    m_tableView->setAlternatingRowColors(true);
    m_tableView->setSortingEnabled(true);
    m_tableView->setContextMenuPolicy(Qt::CustomContextMenu);
    m_tableView->verticalHeader()->setVisible(false);
    m_tableView->horizontalHeader()->setStretchLastSection(true);
    m_tableView->horizontalHeader()->setSectionResizeMode(QHeaderView::Interactive);
    
    // Set column widths
    m_tableView->setColumnWidth(0, 40);   // Enabled checkbox
    m_tableView->setColumnWidth(1, 200);  // Name
    m_tableView->setColumnWidth(2, 150);  // Publisher
    m_tableView->setColumnWidth(3, 80);   // Status
    m_tableView->setColumnWidth(4, 80);   // Impact
    m_tableView->setColumnWidth(5, 140);  // Source
    
    connect(m_tableView->selectionModel(), &QItemSelectionModel::selectionChanged,
            this, &StartupDialog::onSelectionChanged);
    connect(m_tableView, &QTableView::customContextMenuRequested,
            this, &StartupDialog::showContextMenu);
    connect(m_tableView, &QTableView::doubleClicked, 
            this, &StartupDialog::onOpenFileLocation);
    
    qobject_cast<QVBoxLayout*>(layout())->addWidget(m_tableView, 1);
}

void StartupDialog::createDetailsPanel()
{
    m_detailsGroup = new QGroupBox(tr("Details"));
    auto layout = new QGridLayout(m_detailsGroup);
    layout->setColumnStretch(1, 1);
    layout->setColumnStretch(3, 1);
    
    int row = 0;
    
    layout->addWidget(new QLabel(tr("<b>Name:</b>")), row, 0);
    m_detailNameLabel = new QLabel("-");
    m_detailNameLabel->setWordWrap(true);
    m_detailNameLabel->setTextInteractionFlags(Qt::TextSelectableByMouse);
    layout->addWidget(m_detailNameLabel, row, 1);
    
    layout->addWidget(new QLabel(tr("<b>Publisher:</b>")), row, 2);
    m_detailPublisherLabel = new QLabel("-");
    m_detailPublisherLabel->setTextInteractionFlags(Qt::TextSelectableByMouse);
    layout->addWidget(m_detailPublisherLabel, row++, 3);
    
    layout->addWidget(new QLabel(tr("<b>Status:</b>")), row, 0);
    m_detailStatusLabel = new QLabel("-");
    layout->addWidget(m_detailStatusLabel, row, 1);
    
    layout->addWidget(new QLabel(tr("<b>Impact:</b>")), row, 2);
    m_detailImpactLabel = new QLabel("-");
    layout->addWidget(m_detailImpactLabel, row++, 3);
    
    layout->addWidget(new QLabel(tr("<b>Source:</b>")), row, 0);
    m_detailSourceLabel = new QLabel("-");
    m_detailSourceLabel->setTextInteractionFlags(Qt::TextSelectableByMouse);
    layout->addWidget(m_detailSourceLabel, row, 1, 1, 3);
    row++;
    
    layout->addWidget(new QLabel(tr("<b>Command:</b>")), row, 0);
    m_detailCommandLabel = new QLabel("-");
    m_detailCommandLabel->setWordWrap(true);
    m_detailCommandLabel->setTextInteractionFlags(Qt::TextSelectableByMouse);
    layout->addWidget(m_detailCommandLabel, row, 1, 1, 3);
    row++;
    
    layout->addWidget(new QLabel(tr("<b>Path:</b>")), row, 0);
    m_detailPathLabel = new QLabel("-");
    m_detailPathLabel->setWordWrap(true);
    m_detailPathLabel->setTextInteractionFlags(Qt::TextSelectableByMouse);
    layout->addWidget(m_detailPathLabel, row, 1, 1, 3);
    row++;
    
    layout->addWidget(new QLabel(tr("<b>Version:</b>")), row, 0);
    m_detailVersionLabel = new QLabel("-");
    layout->addWidget(m_detailVersionLabel, row, 1);
    
    m_detailsGroup->setMaximumHeight(180);
    qobject_cast<QVBoxLayout*>(this->layout())->addWidget(m_detailsGroup);
}

void StartupDialog::createStatsBar()
{
    auto statsLayout = new QHBoxLayout();
    
    m_totalLabel = new QLabel();
    m_totalLabel->setStyleSheet("font-weight: bold;");
    statsLayout->addWidget(m_totalLabel);
    
    statsLayout->addSpacing(30);
    
    m_enabledLabel = new QLabel();
    m_enabledLabel->setStyleSheet("color: #4caf50;");
    statsLayout->addWidget(m_enabledLabel);
    
    m_disabledLabel = new QLabel();
    m_disabledLabel->setStyleSheet("color: #ff9800;");
    statsLayout->addWidget(m_disabledLabel);
    
    m_highImpactLabel = new QLabel();
    m_highImpactLabel->setStyleSheet("color: #f44336; font-weight: bold;");
    statsLayout->addWidget(m_highImpactLabel);
    
    statsLayout->addStretch();
    
    // Admin status
    QLabel* adminLabel = new QLabel();
    if (StartupMonitor::isAdmin()) {
        adminLabel->setText(tr("🛡️ Administrator"));
        adminLabel->setStyleSheet("color: #4caf50; font-weight: bold;");
    } else {
        adminLabel->setText(tr("⚠️ Limited (Run as Admin for full access)"));
        adminLabel->setStyleSheet("color: #ff9800;");
    }
    statsLayout->addWidget(adminLabel);
    
    // Close button
    auto closeBtn = new QPushButton(tr("Close"));
    connect(closeBtn, &QPushButton::clicked, this, &QDialog::accept);
    statsLayout->addWidget(closeBtn);
    
    qobject_cast<QVBoxLayout*>(layout())->addLayout(statsLayout);
}

void StartupDialog::updateStats()
{
    m_totalLabel->setText(tr("Total: %1").arg(m_monitor->totalCount()));
    m_enabledLabel->setText(tr("✓ Enabled: %1").arg(m_monitor->enabledCount()));
    m_disabledLabel->setText(tr("✗ Disabled: %1").arg(m_monitor->disabledCount()));
    m_highImpactLabel->setText(tr("⚠ High Impact: %1").arg(m_monitor->highImpactCount()));
}

void StartupDialog::updateDetailsPanel(const StartupEntry* entry)
{
    if (!entry) {
        m_detailNameLabel->setText("-");
        m_detailPublisherLabel->setText("-");
        m_detailCommandLabel->setText("-");
        m_detailPathLabel->setText("-");
        m_detailSourceLabel->setText("-");
        m_detailImpactLabel->setText("-");
        m_detailStatusLabel->setText("-");
        m_detailVersionLabel->setText("-");
        return;
    }
    
    m_detailNameLabel->setText(entry->name);
    m_detailPublisherLabel->setText(entry->publisher.isEmpty() ? tr("Unknown") : entry->publisher);
    m_detailCommandLabel->setText(entry->command);
    m_detailPathLabel->setText(entry->executablePath);
    m_detailSourceLabel->setText(entry->sourceLocation);
    m_detailVersionLabel->setText(entry->version.isEmpty() ? "-" : entry->version);
    
    // Status with color
    if (entry->isEnabled) {
        m_detailStatusLabel->setText(tr("✓ Enabled"));
        m_detailStatusLabel->setStyleSheet("color: #4caf50; font-weight: bold;");
    } else {
        m_detailStatusLabel->setText(tr("✗ Disabled"));
        m_detailStatusLabel->setStyleSheet("color: #ff9800; font-weight: bold;");
    }
    
    // Impact with color
    QString impactText = StartupMonitor::impactToString(entry->impact);
    QString impactColor;
    switch (entry->impact) {
        case StartupImpact::High: impactColor = "#f44336"; break;
        case StartupImpact::Medium: impactColor = "#ff9800"; break;
        case StartupImpact::Low: impactColor = "#4caf50"; break;
        default: impactColor = "#888888"; break;
    }
    m_detailImpactLabel->setText(impactText);
    m_detailImpactLabel->setStyleSheet(QString("color: %1; font-weight: bold;").arg(impactColor));
    
    // Mark invalid entries
    if (!entry->isValid) {
        m_detailPathLabel->setStyleSheet("color: #f44336;");
        m_detailPathLabel->setText(entry->executablePath + tr(" (NOT FOUND)"));
    } else {
        m_detailPathLabel->setStyleSheet("");
    }
}


void StartupDialog::onRefresh()
{
    m_refreshBtn->setEnabled(false);
    m_refreshBtn->setText(tr("🔄 Refreshing..."));
    QApplication::processEvents();
    
    m_monitor->refresh();
    
    m_refreshBtn->setText(tr("🔄 Refresh"));
    m_refreshBtn->setEnabled(true);
}

void StartupDialog::onSelectionChanged()
{
    auto indexes = m_tableView->selectionModel()->selectedRows();
    bool hasSelection = !indexes.isEmpty();
    
    const StartupEntry* entry = nullptr;
    
    if (hasSelection) {
        int sourceRow = m_proxyModel->mapToSource(indexes.first()).row();
        entry = m_monitor->model()->getEntry(sourceRow);
    }
    
    // Update button states
    m_deleteBtn->setEnabled(hasSelection && entry && 
        (entry->source != StartupSource::Services && 
         entry->source != StartupSource::TaskScheduler));
    m_openLocationBtn->setEnabled(hasSelection);
    m_openFileBtn->setEnabled(hasSelection && entry && entry->isValid);
    
    if (entry) {
        m_enableBtn->setEnabled(!entry->isEnabled);
        m_disableBtn->setEnabled(entry->isEnabled);
    } else {
        m_enableBtn->setEnabled(false);
        m_disableBtn->setEnabled(false);
    }
    
    updateDetailsPanel(entry);
}

void StartupDialog::onToggleEnabled()
{
    auto indexes = m_tableView->selectionModel()->selectedRows();
    if (indexes.isEmpty()) return;
    
    int sourceRow = m_proxyModel->mapToSource(indexes.first()).row();
    const StartupEntry* entry = m_monitor->model()->getEntry(sourceRow);
    if (!entry) return;
    
    bool newState = !entry->isEnabled;
    
    // Confirm for services
    if (entry->source == StartupSource::Services) {
        auto result = QMessageBox::warning(this, tr("Modify Service"),
            tr("Modifying service startup type may affect system stability.\n\n"
               "Service: %1\n\n"
               "Are you sure you want to %2 this service?")
                .arg(entry->name)
                .arg(newState ? tr("enable") : tr("disable")),
            QMessageBox::Yes | QMessageBox::No, QMessageBox::No);
        
        if (result != QMessageBox::Yes) return;
    }
    
    if (!m_monitor->setEnabled(sourceRow, newState)) {
        QMessageBox::warning(this, tr("Error"),
            tr("Failed to %1 startup entry.\n\n"
               "You may need administrator privileges.")
                .arg(newState ? tr("enable") : tr("disable")));
    }
    
    onSelectionChanged();
}

void StartupDialog::onDelete()
{
    auto indexes = m_tableView->selectionModel()->selectedRows();
    if (indexes.isEmpty()) return;
    
    int sourceRow = m_proxyModel->mapToSource(indexes.first()).row();
    const StartupEntry* entry = m_monitor->model()->getEntry(sourceRow);
    if (!entry) return;
    
    auto result = QMessageBox::warning(this, tr("Delete Startup Entry"),
        tr("Are you sure you want to permanently delete this startup entry?\n\n"
           "Name: %1\n"
           "Command: %2\n\n"
           "This action cannot be undone.")
            .arg(entry->name)
            .arg(entry->command),
        QMessageBox::Yes | QMessageBox::No, QMessageBox::No);
    
    if (result == QMessageBox::Yes) {
        if (!m_monitor->deleteEntry(sourceRow)) {
            QMessageBox::warning(this, tr("Error"),
                tr("Failed to delete startup entry.\n\n"
                   "You may need administrator privileges."));
        }
    }
}

void StartupDialog::onOpenLocation()
{
    auto indexes = m_tableView->selectionModel()->selectedRows();
    if (indexes.isEmpty()) return;
    
    int sourceRow = m_proxyModel->mapToSource(indexes.first()).row();
    m_monitor->openLocation(sourceRow);
}

void StartupDialog::onOpenFileLocation()
{
    auto indexes = m_tableView->selectionModel()->selectedRows();
    if (indexes.isEmpty()) return;
    
    int sourceRow = m_proxyModel->mapToSource(indexes.first()).row();
    m_monitor->openFileLocation(sourceRow);
}

void StartupDialog::onAddEntry()
{
    AddStartupDialog dialog(this);
    if (dialog.exec() == QDialog::Accepted) {
        StartupSource source = static_cast<StartupSource>(dialog.source());
        if (m_monitor->addEntry(dialog.name(), dialog.command(), source)) {
            QMessageBox::information(this, tr("Success"),
                tr("Startup entry added successfully."));
        }
    }
}

void StartupDialog::onExport()
{
    QString defaultPath = QStandardPaths::writableLocation(QStandardPaths::DocumentsLocation)
                         + "/startup_programs.csv";
    
    QString filePath = QFileDialog::getSaveFileName(this, tr("Export Startup List"),
        defaultPath, tr("CSV Files (*.csv);;Text Files (*.txt);;All Files (*)"));
    
    if (filePath.isEmpty()) return;
    
    QFile file(filePath);
    if (!file.open(QIODevice::WriteOnly | QIODevice::Text)) {
        QMessageBox::warning(this, tr("Error"),
            tr("Failed to create file: %1").arg(filePath));
        return;
    }
    
    QTextStream out(&file);
    
    // CSV Header
    out << "Name,Publisher,Status,Impact,Source,Command,Executable Path,Valid\n";
    
    // Helper function for escaping CSV
    auto escapeCSV = [](const QString& s) -> QString {
        QString escaped = s;
        escaped.replace(QStringLiteral("\""), QStringLiteral("\"\""));
        return escaped;
    };
    
    // Data rows
    for (const auto& entry : m_monitor->entries()) {
        out << "\"" << escapeCSV(entry.name) << "\","
            << "\"" << escapeCSV(entry.publisher) << "\","
            << (entry.isEnabled ? "Enabled" : "Disabled") << ","
            << StartupMonitor::impactToString(entry.impact) << ","
            << "\"" << StartupMonitor::sourceToString(entry.source) << "\","
            << "\"" << escapeCSV(entry.command) << "\","
            << "\"" << escapeCSV(entry.executablePath) << "\","
            << (entry.isValid ? "Yes" : "No") << "\n";
    }
    
    file.close();
    
    QMessageBox::information(this, tr("Export Complete"),
        tr("Startup list exported to:\n%1").arg(filePath));
}

void StartupDialog::onFilterChanged()
{
    m_proxyModel->setFilterRegularExpression(m_searchEdit->text());
    m_proxyModel->setSourceFilter(m_sourceCombo->currentData().toInt());
    m_proxyModel->setStatusFilter(m_statusCombo->currentData().toInt());
}

void StartupDialog::onError(const QString& error)
{
    QMessageBox::warning(this, tr("Error"), error);
}

void StartupDialog::showContextMenu(const QPoint& pos)
{
    auto index = m_tableView->indexAt(pos);
    if (!index.isValid()) return;
    
    int sourceRow = m_proxyModel->mapToSource(index).row();
    const StartupEntry* entry = m_monitor->model()->getEntry(sourceRow);
    if (!entry) return;
    
    QMenu menu(this);
    
    if (entry->isEnabled) {
        auto disableAction = menu.addAction(tr("✗ Disable"));
        connect(disableAction, &QAction::triggered, this, &StartupDialog::onToggleEnabled);
    } else {
        auto enableAction = menu.addAction(tr("✓ Enable"));
        connect(enableAction, &QAction::triggered, this, &StartupDialog::onToggleEnabled);
    }
    
    menu.addSeparator();
    
    auto openFileAction = menu.addAction(tr("📂 Open File Location"));
    openFileAction->setEnabled(entry->isValid);
    connect(openFileAction, &QAction::triggered, this, &StartupDialog::onOpenFileLocation);
    
    auto openLocAction = menu.addAction(tr("📁 Open Source Location"));
    connect(openLocAction, &QAction::triggered, this, &StartupDialog::onOpenLocation);
    
    menu.addSeparator();
    
    auto copyNameAction = menu.addAction(tr("Copy Name"));
    connect(copyNameAction, &QAction::triggered, [entry]() {
        QApplication::clipboard()->setText(entry->name);
    });
    
    auto copyCommandAction = menu.addAction(tr("Copy Command"));
    connect(copyCommandAction, &QAction::triggered, [entry]() {
        QApplication::clipboard()->setText(entry->command);
    });
    
    auto copyPathAction = menu.addAction(tr("Copy Path"));
    connect(copyPathAction, &QAction::triggered, [entry]() {
        QApplication::clipboard()->setText(entry->executablePath);
    });
    
    menu.addSeparator();
    
    auto deleteAction = menu.addAction(tr("🗑 Delete"));
    deleteAction->setEnabled(entry->source != StartupSource::Services && 
                            entry->source != StartupSource::TaskScheduler);
    connect(deleteAction, &QAction::triggered, this, &StartupDialog::onDelete);
    
    menu.exec(m_tableView->viewport()->mapToGlobal(pos));
}


// ============================================================================
// AddStartupDialog Implementation
// ============================================================================

AddStartupDialog::AddStartupDialog(QWidget *parent)
    : QDialog(parent)
{
    setWindowTitle(tr("Add Startup Entry"));
    setMinimumWidth(500);
    
    auto layout = new QVBoxLayout(this);
    
    auto formLayout = new QGridLayout();
    
    formLayout->addWidget(new QLabel(tr("Name:")), 0, 0);
    m_nameEdit = new QLineEdit();
    m_nameEdit->setPlaceholderText(tr("Display name for the startup entry"));
    connect(m_nameEdit, &QLineEdit::textChanged, this, &AddStartupDialog::validate);
    formLayout->addWidget(m_nameEdit, 0, 1);
    
    formLayout->addWidget(new QLabel(tr("Command:")), 1, 0);
    auto cmdLayout = new QHBoxLayout();
    m_commandEdit = new QLineEdit();
    m_commandEdit->setPlaceholderText(tr("Full path to executable or command"));
    connect(m_commandEdit, &QLineEdit::textChanged, this, &AddStartupDialog::validate);
    cmdLayout->addWidget(m_commandEdit);
    
    auto browseBtn = new QPushButton(tr("Browse..."));
    connect(browseBtn, &QPushButton::clicked, this, &AddStartupDialog::onBrowse);
    cmdLayout->addWidget(browseBtn);
    formLayout->addLayout(cmdLayout, 1, 1);
    
    formLayout->addWidget(new QLabel(tr("Location:")), 2, 0);
    m_sourceCombo = new QComboBox();
    m_sourceCombo->addItem(tr("Current User (HKCU\\...\\Run)"), 
                          static_cast<int>(StartupSource::RegistryCurrentUser));
    m_sourceCombo->addItem(tr("All Users (HKLM\\...\\Run) - Requires Admin"), 
                          static_cast<int>(StartupSource::RegistryLocalMachine));
    formLayout->addWidget(m_sourceCombo, 2, 1);
    
    layout->addLayout(formLayout);
    
    // Info label
    auto infoLabel = new QLabel(tr(
        "<i>Note: The command will be executed when Windows starts.<br>"
        "Use full paths for executables. You can add arguments after the path.</i>"));
    infoLabel->setWordWrap(true);
    layout->addWidget(infoLabel);
    
    layout->addStretch();
    
    // Buttons
    auto buttonLayout = new QHBoxLayout();
    buttonLayout->addStretch();
    
    auto cancelBtn = new QPushButton(tr("Cancel"));
    connect(cancelBtn, &QPushButton::clicked, this, &QDialog::reject);
    buttonLayout->addWidget(cancelBtn);
    
    m_okBtn = new QPushButton(tr("Add"));
    m_okBtn->setEnabled(false);
    m_okBtn->setDefault(true);
    connect(m_okBtn, &QPushButton::clicked, this, &QDialog::accept);
    buttonLayout->addWidget(m_okBtn);
    
    layout->addLayout(buttonLayout);
}

QString AddStartupDialog::name() const
{
    return m_nameEdit->text().trimmed();
}

QString AddStartupDialog::command() const
{
    return m_commandEdit->text().trimmed();
}

int AddStartupDialog::source() const
{
    return m_sourceCombo->currentData().toInt();
}

void AddStartupDialog::onBrowse()
{
    QString filePath = QFileDialog::getOpenFileName(this, tr("Select Executable"),
        QString(), tr("Executables (*.exe *.bat *.cmd *.ps1);;All Files (*)"));
    
    if (!filePath.isEmpty()) {
        // Add quotes if path contains spaces
        if (filePath.contains(' ')) {
            filePath = "\"" + filePath + "\"";
        }
        m_commandEdit->setText(filePath);
        
        // Auto-fill name if empty
        if (m_nameEdit->text().isEmpty()) {
            QFileInfo fileInfo(filePath.remove('"'));
            m_nameEdit->setText(fileInfo.baseName());
        }
    }
}

void AddStartupDialog::validate()
{
    bool valid = !m_nameEdit->text().trimmed().isEmpty() &&
                 !m_commandEdit->text().trimmed().isEmpty();
    m_okBtn->setEnabled(valid);
}
