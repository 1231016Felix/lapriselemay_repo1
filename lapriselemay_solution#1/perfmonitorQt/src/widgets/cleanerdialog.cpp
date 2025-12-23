#include "cleanerdialog.h"

#include <QFileInfo>
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QGroupBox>
#include <QSplitter>
#include <QHeaderView>
#include <QMessageBox>
#include <QCloseEvent>
#include <QApplication>
#include <QStyle>
#include <QScrollArea>
#include <QFrame>

// ============================================================================
// CleanerWorker Implementation
// ============================================================================

CleanerWorker::CleanerWorker(SystemCleaner* cleaner, QObject* parent)
    : QObject(parent), m_cleaner(cleaner)
{
}

void CleanerWorker::scan()
{
    if (!m_cleaner) return;
    
    connect(m_cleaner, &SystemCleaner::scanStarted, this, &CleanerWorker::scanStarted);
    connect(m_cleaner, &SystemCleaner::scanProgress, this, &CleanerWorker::scanProgress);
    connect(m_cleaner, &SystemCleaner::scanItemCompleted, this, &CleanerWorker::scanItemCompleted);
    connect(m_cleaner, &SystemCleaner::scanCompleted, this, &CleanerWorker::scanCompleted);
    connect(m_cleaner, &SystemCleaner::scanCancelled, this, &CleanerWorker::scanCancelled);
    
    m_cleaner->startScan();
}

void CleanerWorker::clean()
{
    if (!m_cleaner) return;
    
    connect(m_cleaner, &SystemCleaner::cleaningStarted, this, &CleanerWorker::cleaningStarted);
    connect(m_cleaner, &SystemCleaner::cleaningProgress, this, &CleanerWorker::cleaningProgress);
    connect(m_cleaner, &SystemCleaner::cleaningItemCompleted, this, &CleanerWorker::cleaningItemCompleted);
    connect(m_cleaner, &SystemCleaner::cleaningCompleted, this, &CleanerWorker::cleaningCompleted);
    connect(m_cleaner, &SystemCleaner::cleaningCancelled, this, &CleanerWorker::cleaningCancelled);
    
    m_cleaner->startCleaning();
}

void CleanerWorker::cancel()
{
    if (m_cleaner) {
        m_cleaner->cancelScan();
        m_cleaner->cancelCleaning();
    }
}

// ============================================================================
// CleanerDialog Implementation
// ============================================================================

CleanerDialog::CleanerDialog(QWidget *parent)
    : QDialog(parent)
{
    setWindowTitle(tr("System Cleaner"));
    setWindowIcon(QIcon(":/icons/disk.png"));
    setMinimumSize(750, 500);
    resize(850, 550);
    
    m_cleaner = std::make_unique<SystemCleaner>();
    m_cleaner->initialize();
    
    setupUi();
    populateTree();
}

CleanerDialog::~CleanerDialog()
{
    if (m_workerThread && m_workerThread->isRunning()) {
        if (m_worker) {
            m_worker->cancel();
        }
        m_workerThread->quit();
        m_workerThread->wait(3000);
    }
}

void CleanerDialog::closeEvent(QCloseEvent* event)
{
    if (m_isWorking) {
        auto result = QMessageBox::question(this, tr("Cancel Operation"),
            tr("An operation is in progress. Do you want to cancel it?"),
            QMessageBox::Yes | QMessageBox::No);
        
        if (result == QMessageBox::No) {
            event->ignore();
            return;
        }
        
        onCancel();
    }
    
    event->accept();
}


void CleanerDialog::setupUi()
{
    auto mainLayout = new QVBoxLayout(this);
    mainLayout->setSpacing(6);
    mainLayout->setContentsMargins(10, 10, 10, 10);
    
    // Header (compact)
    auto headerLayout = new QHBoxLayout();
    auto headerIcon = new QLabel();
    headerIcon->setPixmap(QApplication::style()->standardIcon(QStyle::SP_DriveHDIcon).pixmap(32, 32));
    auto headerLabel = new QLabel(tr("<b>System Cleaner</b> - Free up disk space by removing temporary files and cache."));
    headerLabel->setWordWrap(true);
    headerLayout->addWidget(headerIcon);
    headerLayout->addWidget(headerLabel, 1);
    mainLayout->addLayout(headerLayout);
    
    // Splitter for tree and details
    auto splitter = new QSplitter(Qt::Horizontal);
    
    // Left side - Tree with categories
    auto leftWidget = new QWidget();
    auto leftLayout = new QVBoxLayout(leftWidget);
    leftLayout->setContentsMargins(0, 0, 0, 0);
    
    // Selection buttons
    auto selectionLayout = new QHBoxLayout();
    auto selectAllBtn = new QPushButton(tr("Select All"));
    auto deselectAllBtn = new QPushButton(tr("Deselect All"));
    connect(selectAllBtn, &QPushButton::clicked, this, &CleanerDialog::onSelectAll);
    connect(deselectAllBtn, &QPushButton::clicked, this, &CleanerDialog::onDeselectAll);
    selectionLayout->addWidget(selectAllBtn);
    selectionLayout->addWidget(deselectAllBtn);
    selectionLayout->addStretch();
    leftLayout->addLayout(selectionLayout);
    
    // Tree widget
    m_treeWidget = new QTreeWidget();
    m_treeWidget->setHeaderLabels({tr("Category"), tr("Size"), tr("Files")});
    m_treeWidget->setColumnWidth(0, 250);
    m_treeWidget->setColumnWidth(1, 80);
    m_treeWidget->setColumnWidth(2, 60);
    m_treeWidget->setRootIsDecorated(true);
    m_treeWidget->setAlternatingRowColors(true);
    m_treeWidget->setSelectionMode(QAbstractItemView::NoSelection);
    connect(m_treeWidget, &QTreeWidget::itemChanged, this, &CleanerDialog::onItemChanged);
    leftLayout->addWidget(m_treeWidget);
    
    splitter->addWidget(leftWidget);
    
    // Right side - Stacked widget for different states
    m_stackedWidget = new QStackedWidget();
    
    // Analysis page (initial state)
    m_analysisPage = new QWidget();
    auto analysisLayout = new QVBoxLayout(m_analysisPage);
    analysisLayout->setAlignment(Qt::AlignCenter);
    
    auto analysisIcon = new QLabel();
    analysisIcon->setPixmap(QApplication::style()->standardIcon(QStyle::SP_FileDialogContentsView).pixmap(48, 48));
    analysisIcon->setAlignment(Qt::AlignCenter);
    analysisLayout->addWidget(analysisIcon);
    
    auto analysisText = new QLabel(tr("<b>Click 'Analyze' to scan for files</b><br>"
                                      "Select categories on the left, then click Analyze."));
    analysisText->setAlignment(Qt::AlignCenter);
    analysisLayout->addWidget(analysisText);
    
    m_stackedWidget->addWidget(m_analysisPage);
    
    // Progress page
    m_progressPage = new QWidget();
    auto progressLayout = new QVBoxLayout(m_progressPage);
    
    m_statusLabel = new QLabel(tr("Scanning..."));
    m_statusLabel->setStyleSheet("font-weight: bold; font-size: 14px;");
    progressLayout->addWidget(m_statusLabel);
    
    m_progressBar = new QProgressBar();
    m_progressBar->setRange(0, 100);
    m_progressBar->setMinimumHeight(25);
    progressLayout->addWidget(m_progressBar);
    
    m_progressLabel = new QLabel();
    m_progressLabel->setWordWrap(true);
    progressLayout->addWidget(m_progressLabel);
    
    m_logTextEdit = new QTextEdit();
    m_logTextEdit->setReadOnly(true);
    m_logTextEdit->setMaximumHeight(120);
    m_logTextEdit->setStyleSheet("font-family: Consolas, monospace; font-size: 11px;");
    progressLayout->addWidget(m_logTextEdit);
    
    progressLayout->addStretch();
    m_stackedWidget->addWidget(m_progressPage);
    
    // Results page
    m_resultsPage = new QWidget();
    auto resultsLayout = new QVBoxLayout(m_resultsPage);
    resultsLayout->setAlignment(Qt::AlignCenter);
    
    auto resultsIcon = new QLabel();
    resultsIcon->setPixmap(QApplication::style()->standardIcon(QStyle::SP_DialogApplyButton).pixmap(48, 48));
    resultsIcon->setAlignment(Qt::AlignCenter);
    resultsLayout->addWidget(resultsIcon);
    
    m_resultsLabel = new QLabel(tr("<b>Cleaning Complete!</b>"));
    m_resultsLabel->setAlignment(Qt::AlignCenter);
    resultsLayout->addWidget(m_resultsLabel);
    
    auto resultsFrame = new QFrame();
    resultsFrame->setFrameStyle(QFrame::StyledPanel);
    auto resultsFrameLayout = new QGridLayout(resultsFrame);
    
    resultsFrameLayout->addWidget(new QLabel(tr("Space Freed:")), 0, 0);
    m_resultsSizeLabel = new QLabel("0 B");
    m_resultsSizeLabel->setStyleSheet("font-weight: bold; font-size: 16px; color: #00aa00;");
    resultsFrameLayout->addWidget(m_resultsSizeLabel, 0, 1);
    
    resultsFrameLayout->addWidget(new QLabel(tr("Files Deleted:")), 1, 0);
    m_resultsFilesLabel = new QLabel("0");
    m_resultsFilesLabel->setStyleSheet("font-weight: bold;");
    resultsFrameLayout->addWidget(m_resultsFilesLabel, 1, 1);
    
    resultsFrameLayout->addWidget(new QLabel(tr("Time Taken:")), 2, 0);
    m_resultsTimeLabel = new QLabel("0.0s");
    resultsFrameLayout->addWidget(m_resultsTimeLabel, 2, 1);
    
    resultsLayout->addWidget(resultsFrame);
    resultsLayout->addStretch();
    
    m_stackedWidget->addWidget(m_resultsPage);
    
    splitter->addWidget(m_stackedWidget);
    splitter->setSizes({400, 400});
    splitter->setChildrenCollapsible(false);
    
    mainLayout->addWidget(splitter, 1);
    
    // Total summary bar
    auto summaryFrame = new QFrame();
    summaryFrame->setFrameStyle(QFrame::StyledPanel);
    summaryFrame->setStyleSheet("background-color: palette(alternate-base);");
    summaryFrame->setMinimumHeight(40);
    auto summaryLayout = new QHBoxLayout(summaryFrame);
    
    summaryLayout->addWidget(new QLabel(tr("Total to clean:")));
    m_totalSizeLabel = new QLabel("0 B");
    m_totalSizeLabel->setStyleSheet("font-weight: bold; font-size: 16px; color: #0078d7;");
    summaryLayout->addWidget(m_totalSizeLabel);
    
    summaryLayout->addSpacing(30);
    
    summaryLayout->addWidget(new QLabel(tr("Files:")));
    m_totalFilesLabel = new QLabel("0");
    m_totalFilesLabel->setStyleSheet("font-weight: bold;");
    summaryLayout->addWidget(m_totalFilesLabel);
    
    summaryLayout->addStretch();
    
    mainLayout->addWidget(summaryFrame);
    
    // Button bar
    auto buttonLayout = new QHBoxLayout();
    buttonLayout->setContentsMargins(0, 5, 0, 0);
    
    m_analyzeButton = new QPushButton(tr("🔍 Analyze"));
    m_analyzeButton->setMinimumSize(100, 35);
    m_analyzeButton->setStyleSheet("font-weight: bold;");
    connect(m_analyzeButton, &QPushButton::clicked, this, &CleanerDialog::onAnalyze);
    
    m_cleanButton = new QPushButton(tr("🧹 Clean"));
    m_cleanButton->setMinimumSize(100, 35);
    m_cleanButton->setEnabled(false);
    m_cleanButton->setStyleSheet("font-weight: bold;");
    connect(m_cleanButton, &QPushButton::clicked, this, &CleanerDialog::onClean);
    
    m_cancelButton = new QPushButton(tr("Cancel"));
    m_cancelButton->setEnabled(false);
    connect(m_cancelButton, &QPushButton::clicked, this, &CleanerDialog::onCancel);
    
    m_closeButton = new QPushButton(tr("Close"));
    connect(m_closeButton, &QPushButton::clicked, this, &QDialog::accept);
    
    buttonLayout->addWidget(m_analyzeButton);
    buttonLayout->addWidget(m_cleanButton);
    buttonLayout->addStretch();
    buttonLayout->addWidget(m_cancelButton);
    buttonLayout->addWidget(m_closeButton);
    
    mainLayout->addLayout(buttonLayout);
}


void CleanerDialog::populateTree()
{
    m_treeWidget->clear();
    m_categoryItems.clear();
    
    // Group names
    QMap<QString, QTreeWidgetItem*> groupItems;
    
    for (auto& item : m_cleaner->items()) {
        QString groupName = getCategoryGroupName(item.category);
        
        // Create group if needed
        if (!groupItems.contains(groupName)) {
            auto groupItem = new QTreeWidgetItem(m_treeWidget);
            groupItem->setText(0, groupName);
            groupItem->setFlags(groupItem->flags() | Qt::ItemIsAutoTristate | Qt::ItemIsUserCheckable);
            groupItem->setCheckState(0, Qt::Checked);
            groupItem->setExpanded(true);
            
            // Set group icon
            if (groupName == tr("Windows")) {
                groupItem->setIcon(0, QApplication::style()->standardIcon(QStyle::SP_ComputerIcon));
            } else if (groupName == tr("Browsers")) {
                groupItem->setIcon(0, QApplication::style()->standardIcon(QStyle::SP_DriveNetIcon));
            } else if (groupName == tr("Applications")) {
                groupItem->setIcon(0, QApplication::style()->standardIcon(QStyle::SP_DesktopIcon));
            } else if (groupName == tr("Privacy")) {
                groupItem->setIcon(0, QApplication::style()->standardIcon(QStyle::SP_DialogResetButton));
            }
            
            groupItems[groupName] = groupItem;
        }
        
        // Create category item
        auto catItem = new QTreeWidgetItem(groupItems[groupName]);
        catItem->setText(0, item.name);
        catItem->setText(1, "-");
        catItem->setText(2, "-");
        catItem->setFlags(catItem->flags() | Qt::ItemIsUserCheckable);
        catItem->setCheckState(0, item.isEnabled ? Qt::Checked : Qt::Unchecked);
        catItem->setToolTip(0, item.description);
        catItem->setIcon(0, getCategoryIcon(item.category));
        catItem->setData(0, Qt::UserRole, static_cast<int>(item.category));
        
        // Mark special items
        if (item.requiresAdmin && !SystemCleaner::isAdmin()) {
            catItem->setText(0, item.name + tr(" (Admin required)"));
            catItem->setForeground(0, QBrush(Qt::gray));
            catItem->setCheckState(0, Qt::Unchecked);
            catItem->setDisabled(true);
        }
        
        if (item.isPrivacy) {
            catItem->setForeground(0, QBrush(QColor(200, 100, 0)));
        }
        
        if (!item.isSafe) {
            catItem->setForeground(0, QBrush(Qt::red));
            catItem->setText(0, item.name + tr(" ⚠️"));
        }
        
        m_categoryItems[item.category] = catItem;
    }
}

QString CleanerDialog::getCategoryGroupName(CleanerCategory category)
{
    switch (category) {
        case CleanerCategory::WindowsTemp:
        case CleanerCategory::UserTemp:
        case CleanerCategory::Thumbnails:
        case CleanerCategory::Prefetch:
        case CleanerCategory::RecycleBin:
        case CleanerCategory::WindowsLogs:
        case CleanerCategory::WindowsUpdate:
        case CleanerCategory::MemoryDumps:
        case CleanerCategory::IconCache:
        case CleanerCategory::FontCache:
        case CleanerCategory::ErrorReports:
        case CleanerCategory::DeliveryOptimization:
        case CleanerCategory::OldWindowsInstall:
            return tr("Windows");
            
        case CleanerCategory::ChromeCache:
        case CleanerCategory::ChromeHistory:
        case CleanerCategory::ChromeCookies:
        case CleanerCategory::EdgeCache:
        case CleanerCategory::EdgeHistory:
        case CleanerCategory::EdgeCookies:
        case CleanerCategory::FirefoxCache:
        case CleanerCategory::FirefoxHistory:
        case CleanerCategory::FirefoxCookies:
        case CleanerCategory::OperaCache:
        case CleanerCategory::BraveCache:
        case CleanerCategory::BrowserCache:
        case CleanerCategory::BrowserHistory:
        case CleanerCategory::BrowserCookies:
            return tr("Browsers");
            
        case CleanerCategory::VSCodeCache:
        case CleanerCategory::NPMCache:
        case CleanerCategory::NuGetCache:
        case CleanerCategory::PipCache:
        case CleanerCategory::SteamCache:
        case CleanerCategory::EpicGamesCache:
            return tr("Applications");
            
        case CleanerCategory::RecentDocs:
        case CleanerCategory::DNSCache:
        case CleanerCategory::Clipboard:
            return tr("Privacy");
            
        default:
            return tr("Other");
    }
}

QIcon CleanerDialog::getCategoryIcon(CleanerCategory category)
{
    // Use system icons for now - you can replace with custom icons
    switch (category) {
        case CleanerCategory::RecycleBin:
            return QApplication::style()->standardIcon(QStyle::SP_TrashIcon);
        case CleanerCategory::ChromeCache:
        case CleanerCategory::EdgeCache:
        case CleanerCategory::FirefoxCache:
        case CleanerCategory::OperaCache:
        case CleanerCategory::BraveCache:
            return QApplication::style()->standardIcon(QStyle::SP_DriveNetIcon);
        default:
            return QApplication::style()->standardIcon(QStyle::SP_FileIcon);
    }
}

void CleanerDialog::onItemChanged(QTreeWidgetItem* item, int column)
{
    if (column != 0) return;
    
    // Update cleaner item state
    QVariant data = item->data(0, Qt::UserRole);
    if (data.isValid()) {
        CleanerCategory category = static_cast<CleanerCategory>(data.toInt());
        bool enabled = item->checkState(0) == Qt::Checked;
        m_cleaner->setItemEnabled(category, enabled);
    }
}

void CleanerDialog::onSelectAll()
{
    m_treeWidget->blockSignals(true);
    for (int i = 0; i < m_treeWidget->topLevelItemCount(); ++i) {
        auto groupItem = m_treeWidget->topLevelItem(i);
        groupItem->setCheckState(0, Qt::Checked);
        for (int j = 0; j < groupItem->childCount(); ++j) {
            auto item = groupItem->child(j);
            if (!item->isDisabled()) {
                item->setCheckState(0, Qt::Checked);
            }
        }
    }
    m_treeWidget->blockSignals(false);
    m_cleaner->setAllEnabled(true);
}

void CleanerDialog::onDeselectAll()
{
    m_treeWidget->blockSignals(true);
    for (int i = 0; i < m_treeWidget->topLevelItemCount(); ++i) {
        auto groupItem = m_treeWidget->topLevelItem(i);
        groupItem->setCheckState(0, Qt::Unchecked);
        for (int j = 0; j < groupItem->childCount(); ++j) {
            groupItem->child(j)->setCheckState(0, Qt::Unchecked);
        }
    }
    m_treeWidget->blockSignals(false);
    m_cleaner->setAllEnabled(false);
}

void CleanerDialog::collectSelectedCategories()
{
    for (auto& item : m_cleaner->items()) {
        if (m_categoryItems.contains(item.category)) {
            item.isEnabled = m_categoryItems[item.category]->checkState(0) == Qt::Checked;
        }
    }
}

void CleanerDialog::setButtonsEnabled(bool enabled)
{
    m_analyzeButton->setEnabled(enabled);
    m_cleanButton->setEnabled(enabled && m_hasScanned);
    m_cancelButton->setEnabled(!enabled);
    m_closeButton->setEnabled(enabled);
    m_treeWidget->setEnabled(enabled);
}


void CleanerDialog::onAnalyze()
{
    collectSelectedCategories();
    
    m_stackedWidget->setCurrentWidget(m_progressPage);
    m_statusLabel->setText(tr("Analyzing..."));
    m_progressBar->setValue(0);
    m_progressLabel->clear();
    m_logTextEdit->clear();
    
    setButtonsEnabled(false);
    m_isWorking = true;
    
    // Reset sizes in tree
    for (auto it = m_categoryItems.begin(); it != m_categoryItems.end(); ++it) {
        it.value()->setText(1, "-");
        it.value()->setText(2, "-");
    }
    
    // Create worker thread
    m_workerThread = new QThread(this);
    m_worker = new CleanerWorker(m_cleaner.get());
    m_worker->moveToThread(m_workerThread);
    
    connect(m_workerThread, &QThread::started, m_worker, &CleanerWorker::scan);
    connect(m_worker, &CleanerWorker::scanStarted, this, &CleanerDialog::onScanStarted);
    connect(m_worker, &CleanerWorker::scanProgress, this, &CleanerDialog::onScanProgress);
    connect(m_worker, &CleanerWorker::scanItemCompleted, this, &CleanerDialog::onScanItemCompleted);
    connect(m_worker, &CleanerWorker::scanCompleted, this, &CleanerDialog::onScanCompleted);
    connect(m_worker, &CleanerWorker::scanCancelled, this, &CleanerDialog::onScanCancelled);
    
    connect(m_worker, &CleanerWorker::scanCompleted, m_workerThread, &QThread::quit);
    connect(m_worker, &CleanerWorker::scanCancelled, m_workerThread, &QThread::quit);
    connect(m_workerThread, &QThread::finished, m_worker, &QObject::deleteLater);
    
    m_workerThread->start();
}

void CleanerDialog::onClean()
{
    // Confirm
    auto result = QMessageBox::question(this, tr("Confirm Cleaning"),
        tr("Are you sure you want to delete the selected files?\n\n"
           "This will free up %1 by deleting %2 files.\n\n"
           "This action cannot be undone!")
        .arg(SystemCleaner::formatSize(m_cleaner->totalCleanableSize()))
        .arg(m_cleaner->totalCleanableFiles()),
        QMessageBox::Yes | QMessageBox::No);
    
    if (result != QMessageBox::Yes) {
        return;
    }
    
    collectSelectedCategories();
    
    m_stackedWidget->setCurrentWidget(m_progressPage);
    m_statusLabel->setText(tr("Cleaning..."));
    m_progressBar->setValue(0);
    m_progressLabel->clear();
    m_logTextEdit->clear();
    
    setButtonsEnabled(false);
    m_isWorking = true;
    
    // Create worker thread
    m_workerThread = new QThread(this);
    m_worker = new CleanerWorker(m_cleaner.get());
    m_worker->moveToThread(m_workerThread);
    
    connect(m_workerThread, &QThread::started, m_worker, &CleanerWorker::clean);
    connect(m_worker, &CleanerWorker::cleaningStarted, this, &CleanerDialog::onCleaningStarted);
    connect(m_worker, &CleanerWorker::cleaningProgress, this, &CleanerDialog::onCleaningProgress);
    connect(m_worker, &CleanerWorker::cleaningItemCompleted, this, &CleanerDialog::onCleaningItemCompleted);
    connect(m_worker, &CleanerWorker::cleaningCompleted, this, &CleanerDialog::onCleaningCompleted);
    connect(m_worker, &CleanerWorker::cleaningCancelled, this, &CleanerDialog::onCleaningCancelled);
    
    connect(m_worker, &CleanerWorker::cleaningCompleted, m_workerThread, &QThread::quit);
    connect(m_worker, &CleanerWorker::cleaningCancelled, m_workerThread, &QThread::quit);
    connect(m_workerThread, &QThread::finished, m_worker, &QObject::deleteLater);
    
    m_workerThread->start();
}

void CleanerDialog::onCancel()
{
    if (m_worker) {
        m_worker->cancel();
    }
}

void CleanerDialog::onScanStarted()
{
    m_logTextEdit->append(tr("Starting analysis..."));
}

void CleanerDialog::onScanProgress(int current, int total, const QString& currentItem)
{
    m_progressBar->setRange(0, total);
    m_progressBar->setValue(current);
    m_progressLabel->setText(tr("Scanning: %1").arg(currentItem));
}

void CleanerDialog::onScanItemCompleted(CleanerCategory category, qint64 size, int files)
{
    if (m_categoryItems.contains(category)) {
        auto item = m_categoryItems[category];
        item->setText(1, SystemCleaner::formatSize(size));
        item->setText(2, QString::number(files));
        
        // Color based on size
        if (size > 100 * 1024 * 1024) { // > 100 MB
            item->setForeground(1, QBrush(QColor(200, 0, 0)));
        } else if (size > 10 * 1024 * 1024) { // > 10 MB
            item->setForeground(1, QBrush(QColor(200, 100, 0)));
        }
    }
    
    // Get category name for log
    for (const auto& cleanerItem : m_cleaner->items()) {
        if (cleanerItem.category == category) {
            m_logTextEdit->append(tr("  %1: %2 (%3 files)")
                .arg(cleanerItem.name)
                .arg(SystemCleaner::formatSize(size))
                .arg(files));
            break;
        }
    }
}

void CleanerDialog::onScanCompleted(qint64 totalSize, int totalFiles)
{
    m_hasScanned = true;
    m_isWorking = false;
    setButtonsEnabled(true);
    
    m_totalSizeLabel->setText(SystemCleaner::formatSize(totalSize));
    m_totalFilesLabel->setText(QString::number(totalFiles));
    
    m_progressBar->setValue(m_progressBar->maximum());
    m_statusLabel->setText(tr("Analysis complete!"));
    m_progressLabel->setText(tr("Found %1 in %2 files that can be cleaned.")
        .arg(SystemCleaner::formatSize(totalSize))
        .arg(totalFiles));
    
    m_logTextEdit->append("");
    m_logTextEdit->append(tr("=== Analysis Complete ==="));
    m_logTextEdit->append(tr("Total: %1 (%2 files)")
        .arg(SystemCleaner::formatSize(totalSize))
        .arg(totalFiles));
    
    if (totalFiles > 0) {
        m_cleanButton->setEnabled(true);
    }
}

void CleanerDialog::onScanCancelled()
{
    m_isWorking = false;
    setButtonsEnabled(true);
    m_stackedWidget->setCurrentWidget(m_analysisPage);
    m_logTextEdit->append(tr("Analysis cancelled."));
}

void CleanerDialog::onCleaningStarted()
{
    m_logTextEdit->append(tr("Starting cleaning..."));
}

void CleanerDialog::onCleaningProgress(int current, int total, const QString& currentFile)
{
    m_progressBar->setRange(0, total);
    m_progressBar->setValue(current);
    
    // Show just the filename, not full path
    QFileInfo info(currentFile);
    m_progressLabel->setText(tr("Deleting: %1").arg(info.fileName()));
}

void CleanerDialog::onCleaningItemCompleted(CleanerCategory category, qint64 freedSize, int deletedFiles)
{
    for (const auto& cleanerItem : m_cleaner->items()) {
        if (cleanerItem.category == category) {
            m_logTextEdit->append(tr("  ✓ %1: freed %2 (%3 files)")
                .arg(cleanerItem.name)
                .arg(SystemCleaner::formatSize(freedSize))
                .arg(deletedFiles));
            break;
        }
    }
}

void CleanerDialog::onCleaningCompleted(const CleaningResult& result)
{
    m_isWorking = false;
    m_hasScanned = false;
    setButtonsEnabled(true);
    m_cleanButton->setEnabled(false);
    
    // Show results page
    m_stackedWidget->setCurrentWidget(m_resultsPage);
    
    m_resultsSizeLabel->setText(SystemCleaner::formatSize(result.bytesFreed));
    m_resultsFilesLabel->setText(QString::number(result.filesDeleted));
    m_resultsTimeLabel->setText(QString("%1 seconds").arg(result.durationSeconds, 0, 'f', 1));
    
    if (result.errors > 0) {
        m_resultsLabel->setText(tr("<h3>Cleaning Complete (with %1 errors)</h3>").arg(result.errors));
    } else {
        m_resultsLabel->setText(tr("<h3>Cleaning Complete!</h3>"));
    }
    
    m_logTextEdit->append("");
    m_logTextEdit->append(tr("=== Cleaning Complete ==="));
    m_logTextEdit->append(tr("Freed: %1").arg(SystemCleaner::formatSize(result.bytesFreed)));
    m_logTextEdit->append(tr("Deleted: %1 files, %2 directories")
        .arg(result.filesDeleted)
        .arg(result.directoriesDeleted));
    m_logTextEdit->append(tr("Time: %1 seconds").arg(result.durationSeconds, 0, 'f', 1));
    
    if (result.errors > 0) {
        m_logTextEdit->append(tr("Errors: %1").arg(result.errors));
    }
    
    // Reset totals
    m_totalSizeLabel->setText("0 B");
    m_totalFilesLabel->setText("0");
    
    // Reset tree values
    for (auto it = m_categoryItems.begin(); it != m_categoryItems.end(); ++it) {
        it.value()->setText(1, "-");
        it.value()->setText(2, "-");
        it.value()->setForeground(1, QBrush());
    }
}

void CleanerDialog::onCleaningCancelled()
{
    m_isWorking = false;
    setButtonsEnabled(true);
    m_stackedWidget->setCurrentWidget(m_analysisPage);
    m_logTextEdit->append(tr("Cleaning cancelled."));
}

QTreeWidgetItem* CleanerDialog::findCategoryItem(CleanerCategory category)
{
    return m_categoryItems.value(category, nullptr);
}

void CleanerDialog::updateCategorySize(CleanerCategory category, qint64 size, int files)
{
    if (auto item = findCategoryItem(category)) {
        item->setText(1, SystemCleaner::formatSize(size));
        item->setText(2, QString::number(files));
    }
}
