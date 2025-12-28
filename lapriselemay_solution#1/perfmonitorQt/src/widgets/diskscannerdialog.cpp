#include "diskscannerdialog.h"
#include "../monitors/diskscannermonitor.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QGroupBox>
#include <QHeaderView>
#include <QFileDialog>
#include <QMessageBox>
#include <QMenu>
#include <QAction>
#include <QClipboard>
#include <QApplication>
#include <QStorageInfo>
#include <QDateTime>

DiskScannerDialog::DiskScannerDialog(QWidget* parent)
    : QDialog(parent)
    , m_scanner(std::make_unique<DiskScannerMonitor>())
{
    setWindowTitle(tr("Disk Space Analyzer"));
    setMinimumSize(1000, 700);
    resize(1100, 800);
    setWindowFlags(windowFlags() | Qt::WindowMaximizeButtonHint);
    
    setupUi();
    
    // Connect scanner signals
    connect(m_scanner.get(), &DiskScannerMonitor::scanStarted,
            this, &DiskScannerDialog::onScanStarted);
    connect(m_scanner.get(), &DiskScannerMonitor::scanProgress,
            this, &DiskScannerDialog::onScanProgress);
    connect(m_scanner.get(), &DiskScannerMonitor::scanFinished,
            this, [this](const ScanStatistics&) { onScanFinished(); });
    connect(m_scanner.get(), &DiskScannerMonitor::scanCancelled,
            this, &DiskScannerDialog::onScanCancelled);
    
    // Drive info refresh timer
    m_driveRefreshTimer = new QTimer(this);
    connect(m_driveRefreshTimer, &QTimer::timeout, this, &DiskScannerDialog::refreshDriveInfo);
    m_driveRefreshTimer->start(5000);
    
    refreshDriveInfo();
}

DiskScannerDialog::~DiskScannerDialog()
{
    m_scanner->cancelScan();
}

void DiskScannerDialog::setupUi()
{
    auto mainLayout = new QVBoxLayout(this);
    
    // Drive selection group
    createDriveSelector();
    
    // Tab widget
    m_tabWidget = new QTabWidget();
    createScanTab();
    createLargeFilesTab();
    createStatisticsTab();
    mainLayout->addWidget(m_tabWidget);
    
    // Close button
    auto buttonLayout = new QHBoxLayout();
    buttonLayout->addStretch();
    auto closeBtn = new QPushButton(tr("Close"));
    connect(closeBtn, &QPushButton::clicked, this, &QDialog::accept);
    buttonLayout->addWidget(closeBtn);
    mainLayout->addLayout(buttonLayout);
}

void DiskScannerDialog::createDriveSelector()
{
    auto group = new QGroupBox(tr("Scan Location"));
    auto layout = new QGridLayout(group);
    
    // Drive combo
    layout->addWidget(new QLabel(tr("Drive:")), 0, 0);
    m_driveCombo = new QComboBox();
    m_driveCombo->setMinimumWidth(100);
    populateDriveCombo();
    connect(m_driveCombo, &QComboBox::currentTextChanged, this, [this](const QString& drive) {
        m_pathEdit->setText(drive);
        refreshDriveInfo();
    });
    layout->addWidget(m_driveCombo, 0, 1);
    
    // Path edit
    layout->addWidget(new QLabel(tr("Path:")), 0, 2);
    m_pathEdit = new QLineEdit();
    m_pathEdit->setPlaceholderText(tr("Select a drive or enter a path..."));
    if (m_driveCombo->count() > 0) {
        m_pathEdit->setText(m_driveCombo->currentText());
    }
    layout->addWidget(m_pathEdit, 0, 3);
    
    // Browse button
    m_browseBtn = new QPushButton(tr("Browse..."));
    connect(m_browseBtn, &QPushButton::clicked, this, &DiskScannerDialog::onBrowseClicked);
    layout->addWidget(m_browseBtn, 0, 4);
    
    // Scan/Cancel buttons
    m_scanBtn = new QPushButton(tr("🔍 Scan"));
    m_scanBtn->setMinimumWidth(100);
    connect(m_scanBtn, &QPushButton::clicked, this, &DiskScannerDialog::onScanClicked);
    layout->addWidget(m_scanBtn, 0, 5);
    
    m_cancelBtn = new QPushButton(tr("Cancel"));
    m_cancelBtn->setEnabled(false);
    connect(m_cancelBtn, &QPushButton::clicked, this, &DiskScannerDialog::onCancelClicked);
    layout->addWidget(m_cancelBtn, 0, 6);
    
    // Drive info
    m_driveInfoLabel = new QLabel();
    layout->addWidget(m_driveInfoLabel, 1, 0, 1, 4);
    
    m_driveUsageBar = new QProgressBar();
    m_driveUsageBar->setMinimumHeight(20);
    m_driveUsageBar->setTextVisible(true);
    layout->addWidget(m_driveUsageBar, 1, 4, 1, 3);
    
    // Progress
    m_progressBar = new QProgressBar();
    m_progressBar->setMinimum(0);
    m_progressBar->setMaximum(0); // Indeterminate
    m_progressBar->setVisible(false);
    layout->addWidget(m_progressBar, 2, 0, 1, 5);
    
    m_statusLabel = new QLabel(tr("Ready"));
    m_statusLabel->setStyleSheet("color: gray;");
    layout->addWidget(m_statusLabel, 2, 5, 1, 2);
    
    layout->setColumnStretch(3, 1);
    
    static_cast<QVBoxLayout*>(this->layout())->insertWidget(0, group);
}

void DiskScannerDialog::createScanTab()
{
    m_scanTab = new QWidget();
    auto layout = new QVBoxLayout(m_scanTab);
    
    // Tree view with splitter
    auto splitter = new QSplitter(Qt::Vertical);
    
    // Tree view
    m_treeView = new QTreeView();
    m_treeView->setModel(m_scanner->model());
    m_treeView->setAlternatingRowColors(true);
    m_treeView->setSelectionMode(QAbstractItemView::SingleSelection);
    m_treeView->setContextMenuPolicy(Qt::CustomContextMenu);
    m_treeView->setSortingEnabled(true);
    m_treeView->header()->setSectionResizeMode(QHeaderView::Interactive);
    m_treeView->header()->setStretchLastSection(true);
    
    connect(m_treeView->selectionModel(), &QItemSelectionModel::currentChanged,
            this, &DiskScannerDialog::onTreeItemSelected);
    connect(m_treeView, &QTreeView::customContextMenuRequested,
            this, &DiskScannerDialog::onTreeContextMenu);
    
    splitter->addWidget(m_treeView);
    
    // Selected item info
    auto infoGroup = new QGroupBox(tr("Selected Item"));
    auto infoLayout = new QVBoxLayout(infoGroup);
    m_selectedInfoLabel = new QLabel(tr("Select an item to see details"));
    m_selectedInfoLabel->setWordWrap(true);
    infoLayout->addWidget(m_selectedInfoLabel);
    
    // Action buttons
    auto actionLayout = new QHBoxLayout();
    
    auto openBtn = new QPushButton(tr("Open"));
    connect(openBtn, &QPushButton::clicked, this, [this]() {
        auto index = m_treeView->currentIndex();
        if (index.isValid()) {
            auto item = m_scanner->model()->getItem(index);
            if (item) DiskScannerMonitor::openFile(item->path);
        }
    });
    actionLayout->addWidget(openBtn);
    
    auto explorerBtn = new QPushButton(tr("Show in Explorer"));
    connect(explorerBtn, &QPushButton::clicked, this, &DiskScannerDialog::onOpenInExplorer);
    actionLayout->addWidget(explorerBtn);
    
    auto recycleBtn = new QPushButton(tr("🗑️ Move to Recycle Bin"));
    connect(recycleBtn, &QPushButton::clicked, this, &DiskScannerDialog::onMoveToRecycleBin);
    actionLayout->addWidget(recycleBtn);
    
    actionLayout->addStretch();
    infoLayout->addLayout(actionLayout);
    
    splitter->addWidget(infoGroup);
    splitter->setSizes({500, 150});
    
    layout->addWidget(splitter);
    
    m_tabWidget->addTab(m_scanTab, tr("📁 Directory Tree"));
}

void DiskScannerDialog::createLargeFilesTab()
{
    m_largeFilesTab = new QWidget();
    auto layout = new QVBoxLayout(m_largeFilesTab);
    
    // Info label
    m_largeFilesCountLabel = new QLabel(tr("Scan a drive to find large files (>10 MB)"));
    layout->addWidget(m_largeFilesCountLabel);
    
    // Large files table
    m_largeFilesTable = new QTableWidget();
    m_largeFilesTable->setColumnCount(5);
    m_largeFilesTable->setHorizontalHeaderLabels({
        tr("Name"), tr("Path"), tr("Size"), tr("Type"), tr("Modified")
    });
    m_largeFilesTable->setAlternatingRowColors(true);
    m_largeFilesTable->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_largeFilesTable->setEditTriggers(QAbstractItemView::NoEditTriggers);
    m_largeFilesTable->horizontalHeader()->setStretchLastSection(true);
    m_largeFilesTable->horizontalHeader()->setSectionResizeMode(QHeaderView::Interactive);
    m_largeFilesTable->setSortingEnabled(true);
    m_largeFilesTable->setContextMenuPolicy(Qt::CustomContextMenu);
    
    connect(m_largeFilesTable, &QTableWidget::cellDoubleClicked,
            this, &DiskScannerDialog::onLargeFileDoubleClicked);
    connect(m_largeFilesTable, &QTableWidget::customContextMenuRequested,
            this, [this](const QPoint& pos) {
        int row = m_largeFilesTable->rowAt(pos.y());
        if (row < 0) return;
        
        QMenu menu;
        menu.addAction(tr("Open File"), [this, row]() {
            auto pathItem = m_largeFilesTable->item(row, 1);
            if (pathItem) DiskScannerMonitor::openFile(pathItem->text());
        });
        menu.addAction(tr("Show in Explorer"), [this, row]() {
            auto pathItem = m_largeFilesTable->item(row, 1);
            if (pathItem) DiskScannerMonitor::openInExplorer(pathItem->text());
        });
        menu.addSeparator();
        menu.addAction(tr("Move to Recycle Bin"), [this, row]() {
            auto pathItem = m_largeFilesTable->item(row, 1);
            if (pathItem) {
                if (QMessageBox::question(this, tr("Confirm"),
                    tr("Move this file to the Recycle Bin?\n%1").arg(pathItem->text())) == QMessageBox::Yes) {
                    DiskScannerMonitor::moveToRecycleBin(pathItem->text());
                    m_largeFilesTable->removeRow(row);
                }
            }
        });
        menu.addAction(tr("Copy Path"), [this, row]() {
            auto pathItem = m_largeFilesTable->item(row, 1);
            if (pathItem) QApplication::clipboard()->setText(pathItem->text());
        });
        menu.exec(m_largeFilesTable->viewport()->mapToGlobal(pos));
    });
    
    layout->addWidget(m_largeFilesTable);
    
    m_tabWidget->addTab(m_largeFilesTab, tr("📦 Large Files"));
}

void DiskScannerDialog::createStatisticsTab()
{
    m_statsTab = new QWidget();
    auto layout = new QVBoxLayout(m_statsTab);
    
    // Summary group
    auto summaryGroup = new QGroupBox(tr("Scan Summary"));
    auto summaryLayout = new QGridLayout(summaryGroup);
    
    summaryLayout->addWidget(new QLabel(tr("Total Size:")), 0, 0);
    m_totalSizeLabel = new QLabel("-");
    m_totalSizeLabel->setStyleSheet("font-weight: bold; font-size: 14px;");
    summaryLayout->addWidget(m_totalSizeLabel, 0, 1);
    
    summaryLayout->addWidget(new QLabel(tr("Total Files:")), 0, 2);
    m_totalFilesLabel = new QLabel("-");
    m_totalFilesLabel->setStyleSheet("font-weight: bold;");
    summaryLayout->addWidget(m_totalFilesLabel, 0, 3);
    
    summaryLayout->addWidget(new QLabel(tr("Total Folders:")), 1, 0);
    m_totalDirsLabel = new QLabel("-");
    summaryLayout->addWidget(m_totalDirsLabel, 1, 1);
    
    summaryLayout->addWidget(new QLabel(tr("Scan Time:")), 1, 2);
    m_scanTimeLabel = new QLabel("-");
    summaryLayout->addWidget(m_scanTimeLabel, 1, 3);
    
    summaryLayout->addWidget(new QLabel(tr("Inaccessible Folders:")), 2, 0);
    m_inaccessibleLabel = new QLabel("-");
    m_inaccessibleLabel->setStyleSheet("color: orange;");
    summaryLayout->addWidget(m_inaccessibleLabel, 2, 1);
    
    summaryLayout->addWidget(new QLabel(tr("Skipped Symlinks:")), 2, 2);
    m_symlinksLabel = new QLabel("-");
    summaryLayout->addWidget(m_symlinksLabel, 2, 3);
    
    layout->addWidget(summaryGroup);
    
    // Split for tables
    auto splitter = new QSplitter(Qt::Horizontal);
    
    // Size distribution
    auto distGroup = new QGroupBox(tr("Size Distribution"));
    auto distLayout = new QVBoxLayout(distGroup);
    m_sizeDistTable = new QTableWidget();
    m_sizeDistTable->setColumnCount(2);
    m_sizeDistTable->setHorizontalHeaderLabels({tr("Size Range"), tr("Count")});
    m_sizeDistTable->horizontalHeader()->setStretchLastSection(true);
    m_sizeDistTable->setEditTriggers(QAbstractItemView::NoEditTriggers);
    distLayout->addWidget(m_sizeDistTable);
    splitter->addWidget(distGroup);
    
    // Top extensions
    auto extGroup = new QGroupBox(tr("Top File Types by Size"));
    auto extLayout = new QVBoxLayout(extGroup);
    m_extensionsTable = new QTableWidget();
    m_extensionsTable->setColumnCount(2);
    m_extensionsTable->setHorizontalHeaderLabels({tr("Extension"), tr("Total Size")});
    m_extensionsTable->horizontalHeader()->setStretchLastSection(true);
    m_extensionsTable->setEditTriggers(QAbstractItemView::NoEditTriggers);
    extLayout->addWidget(m_extensionsTable);
    splitter->addWidget(extGroup);
    
    layout->addWidget(splitter);
    
    m_tabWidget->addTab(m_statsTab, tr("📊 Statistics"));
}

void DiskScannerDialog::populateDriveCombo()
{
    m_driveCombo->clear();
    QStringList drives = DiskScannerMonitor::getAvailableDrives();
    
    for (const QString& drive : drives) {
        QStorageInfo info(drive);
        QString label = QString("%1 (%2)")
            .arg(drive)
            .arg(info.name().isEmpty() ? tr("Local Disk") : info.name());
        m_driveCombo->addItem(label, drive);
    }
}

void DiskScannerDialog::refreshDriveInfo()
{
    QString path = m_pathEdit->text();
    if (path.isEmpty()) return;
    
    QString drive = path.left(3);
    QStorageInfo info(drive);
    
    if (!info.isValid()) {
        m_driveInfoLabel->setText(tr("Invalid path"));
        m_driveUsageBar->setValue(0);
        return;
    }
    
    qint64 total = info.bytesTotal();
    qint64 free = info.bytesAvailable();
    qint64 used = total - free;
    
    m_driveInfoLabel->setText(QString("%1: %2 free of %3")
        .arg(info.name().isEmpty() ? drive : info.name())
        .arg(formatSize(free))
        .arg(formatSize(total)));
    
    int percent = total > 0 ? static_cast<int>((used * 100) / total) : 0;
    m_driveUsageBar->setValue(percent);
    m_driveUsageBar->setFormat(QString("%1% used (%2 / %3)")
        .arg(percent)
        .arg(formatSize(used))
        .arg(formatSize(total)));
    
    // Color based on usage
    QString style;
    if (percent >= 90) {
        style = "QProgressBar::chunk { background-color: #ff4444; }";
    } else if (percent >= 75) {
        style = "QProgressBar::chunk { background-color: #ffaa00; }";
    } else {
        style = "QProgressBar::chunk { background-color: #44aa44; }";
    }
    m_driveUsageBar->setStyleSheet(style);
}

void DiskScannerDialog::onScanClicked()
{
    QString path = m_pathEdit->text();
    if (path.isEmpty()) {
        QMessageBox::warning(this, tr("Warning"), tr("Please select a path to scan."));
        return;
    }
    
    QFileInfo info(path);
    if (!info.exists()) {
        QMessageBox::warning(this, tr("Warning"), tr("The specified path does not exist."));
        return;
    }
    
    m_scanner->startScan(path);
}

void DiskScannerDialog::onCancelClicked()
{
    m_scanner->cancelScan();
}

void DiskScannerDialog::onBrowseClicked()
{
    QString dir = QFileDialog::getExistingDirectory(this, tr("Select Folder"),
        m_pathEdit->text().isEmpty() ? QDir::rootPath() : m_pathEdit->text());
    
    if (!dir.isEmpty()) {
        m_pathEdit->setText(dir);
        refreshDriveInfo();
    }
}

void DiskScannerDialog::onScanStarted(const QString& path)
{
    m_scanBtn->setEnabled(false);
    m_cancelBtn->setEnabled(true);
    m_browseBtn->setEnabled(false);
    m_driveCombo->setEnabled(false);
    m_pathEdit->setEnabled(false);
    m_progressBar->setVisible(true);
    m_statusLabel->setText(tr("Scanning %1...").arg(path));
    m_statusLabel->setStyleSheet("color: blue;");
    
    // Clear previous results
    m_largeFilesTable->setRowCount(0);
}

void DiskScannerDialog::onScanProgress(int files, int dirs, const QString& currentPath)
{
    m_statusLabel->setText(tr("Scanned %1 files, %2 folders - %3")
        .arg(files).arg(dirs).arg(currentPath));
}

void DiskScannerDialog::onScanFinished()
{
    m_scanBtn->setEnabled(true);
    m_cancelBtn->setEnabled(false);
    m_browseBtn->setEnabled(true);
    m_driveCombo->setEnabled(true);
    m_pathEdit->setEnabled(true);
    m_progressBar->setVisible(false);
    
    const auto& stats = m_scanner->statistics();
    m_statusLabel->setText(tr("Scan complete: %1 files, %2 folders in %3s")
        .arg(stats.totalFiles)
        .arg(stats.totalDirectories)
        .arg(stats.scanDurationSeconds, 0, 'f', 1));
    m_statusLabel->setStyleSheet("color: green;");
    
    // Expand first level
    m_treeView->expandToDepth(0);
    m_treeView->resizeColumnToContents(0);
    m_treeView->resizeColumnToContents(1);
    
    // Update other tabs
    updateStatistics();
    updateLargeFilesTable();
}

void DiskScannerDialog::onScanCancelled()
{
    m_scanBtn->setEnabled(true);
    m_cancelBtn->setEnabled(false);
    m_browseBtn->setEnabled(true);
    m_driveCombo->setEnabled(true);
    m_pathEdit->setEnabled(true);
    m_progressBar->setVisible(false);
    m_statusLabel->setText(tr("Scan cancelled"));
    m_statusLabel->setStyleSheet("color: orange;");
}

void DiskScannerDialog::onTreeItemSelected(const QModelIndex& index)
{
    auto item = m_scanner->model()->getItem(index);
    if (!item) {
        m_selectedInfoLabel->setText(tr("Select an item to see details"));
        return;
    }
    
    QString info = QString("<b>%1</b><br><br>").arg(item->path);
    info += QString("<b>Size:</b> %1<br>").arg(formatSize(item->size));
    info += QString("<b>Allocated:</b> %1<br>").arg(formatSize(item->allocatedSize));
    
    if (item->isDirectory) {
        info += QString("<b>Files:</b> %1<br>").arg(item->fileCount);
        info += QString("<b>Folders:</b> %1<br>").arg(item->dirCount);
    } else {
        info += QString("<b>Type:</b> %1<br>").arg(item->extension.isEmpty() ? tr("File") : item->extension.toUpper());
    }
    
    info += QString("<b>Modified:</b> %1").arg(item->lastModified.toString("yyyy-MM-dd hh:mm:ss"));
    
    m_selectedInfoLabel->setText(info);
}

void DiskScannerDialog::onTreeContextMenu(const QPoint& pos)
{
    auto index = m_treeView->indexAt(pos);
    if (!index.isValid()) return;
    
    auto item = m_scanner->model()->getItem(index);
    if (!item) return;
    
    QMenu menu;
    
    if (!item->isDirectory) {
        menu.addAction(tr("Open File"), [item]() {
            DiskScannerMonitor::openFile(item->path);
        });
    }
    
    menu.addAction(tr("Show in Explorer"), [item]() {
        DiskScannerMonitor::openInExplorer(item->path);
    });
    
    menu.addSeparator();
    
    menu.addAction(tr("Move to Recycle Bin"), [this, item]() {
        QString msg = item->isDirectory 
            ? tr("Move this folder and all its contents to the Recycle Bin?\n%1\n\nSize: %2")
                .arg(item->path).arg(formatSize(item->size))
            : tr("Move this file to the Recycle Bin?\n%1\n\nSize: %2")
                .arg(item->path).arg(formatSize(item->size));
        
        if (QMessageBox::question(this, tr("Confirm"), msg) == QMessageBox::Yes) {
            if (DiskScannerMonitor::moveToRecycleBin(item->path)) {
                // Rescan to update
                QMessageBox::information(this, tr("Success"), 
                    tr("Item moved to Recycle Bin. Click Scan to refresh."));
            } else {
                QMessageBox::warning(this, tr("Error"), 
                    tr("Could not move item to Recycle Bin."));
            }
        }
    });
    
    menu.addSeparator();
    
    menu.addAction(tr("Copy Path"), [item]() {
        QApplication::clipboard()->setText(item->path);
    });
    
    menu.exec(m_treeView->viewport()->mapToGlobal(pos));
}

void DiskScannerDialog::onLargeFileDoubleClicked(int row, int)
{
    auto pathItem = m_largeFilesTable->item(row, 1);
    if (pathItem) {
        DiskScannerMonitor::openInExplorer(pathItem->text());
    }
}

void DiskScannerDialog::onDeleteSelected()
{
    // Implemented via context menu
}

void DiskScannerDialog::onMoveToRecycleBin()
{
    auto index = m_treeView->currentIndex();
    if (!index.isValid()) return;
    
    auto item = m_scanner->model()->getItem(index);
    if (!item) return;
    
    QString msg = item->isDirectory 
        ? tr("Move this folder to the Recycle Bin?\n%1").arg(item->path)
        : tr("Move this file to the Recycle Bin?\n%1").arg(item->path);
    
    if (QMessageBox::question(this, tr("Confirm"), msg) == QMessageBox::Yes) {
        DiskScannerMonitor::moveToRecycleBin(item->path);
    }
}

void DiskScannerDialog::onOpenInExplorer()
{
    auto index = m_treeView->currentIndex();
    if (!index.isValid()) return;
    
    auto item = m_scanner->model()->getItem(index);
    if (item) {
        DiskScannerMonitor::openInExplorer(item->path);
    }
}

void DiskScannerDialog::updateStatistics()
{
    const auto& stats = m_scanner->statistics();
    
    m_totalSizeLabel->setText(formatSize(stats.totalSize));
    m_totalFilesLabel->setText(QString::number(stats.totalFiles));
    m_totalDirsLabel->setText(QString::number(stats.totalDirectories));
    m_scanTimeLabel->setText(QString("%1 seconds").arg(stats.scanDurationSeconds, 0, 'f', 2));
    
    // Show inaccessible and skipped counts
    m_inaccessibleLabel->setText(QString::number(stats.inaccessibleDirectories));
    if (stats.inaccessibleDirectories > 0) {
        m_inaccessibleLabel->setToolTip(tr("Some folders could not be scanned due to permission restrictions."));
    }
    m_symlinksLabel->setText(QString::number(stats.skippedSymlinks));
    
    // Size distribution
    m_sizeDistTable->setRowCount(5);
    QStringList ranges = {"< 1 MB", "1-10 MB", "10-100 MB", "100 MB - 1 GB", "> 1 GB"};
    QList<int> counts = {stats.filesUnder1MB, stats.files1to10MB, stats.files10to100MB,
                         stats.files100MBto1GB, stats.filesOver1GB};
    
    for (int i = 0; i < 5; i++) {
        m_sizeDistTable->setItem(i, 0, new QTableWidgetItem(ranges[i]));
        auto countItem = new QTableWidgetItem(QString::number(counts[i]));
        countItem->setTextAlignment(Qt::AlignRight | Qt::AlignVCenter);
        m_sizeDistTable->setItem(i, 1, countItem);
    }
    
    // Top extensions
    m_extensionsTable->setRowCount(static_cast<int>(stats.topExtensions.size()));
    int row = 0;
    for (const auto& [ext, size] : stats.topExtensions) {
        QString extName = ext.isEmpty() ? tr("(no extension)") : QString(".%1").arg(ext);
        m_extensionsTable->setItem(row, 0, new QTableWidgetItem(extName));
        auto sizeItem = new QTableWidgetItem(formatSize(size));
        sizeItem->setData(Qt::UserRole, size);
        sizeItem->setTextAlignment(Qt::AlignRight | Qt::AlignVCenter);
        m_extensionsTable->setItem(row, 1, sizeItem);
        row++;
    }
}

void DiskScannerDialog::updateLargeFilesTable()
{
    const auto& largeFiles = m_scanner->largeFiles();
    
    m_largeFilesCountLabel->setText(tr("Found %1 large files (>10 MB)").arg(largeFiles.size()));
    m_largeFilesTable->setRowCount(static_cast<int>(largeFiles.size()));
    
    int row = 0;
    for (const auto& file : largeFiles) {
        m_largeFilesTable->setItem(row, 0, new QTableWidgetItem(file.name));
        m_largeFilesTable->setItem(row, 1, new QTableWidgetItem(file.path));
        
        auto sizeItem = new QTableWidgetItem(formatSize(file.size));
        sizeItem->setData(Qt::UserRole, file.size);
        sizeItem->setTextAlignment(Qt::AlignRight | Qt::AlignVCenter);
        m_largeFilesTable->setItem(row, 2, sizeItem);
        
        m_largeFilesTable->setItem(row, 3, new QTableWidgetItem(
            file.extension.isEmpty() ? tr("File") : file.extension.toUpper()));
        m_largeFilesTable->setItem(row, 4, new QTableWidgetItem(
            file.lastModified.toString("yyyy-MM-dd hh:mm")));
        
        row++;
    }
    
    m_largeFilesTable->resizeColumnsToContents();
    m_largeFilesTable->sortByColumn(2, Qt::DescendingOrder);
}

QString DiskScannerDialog::formatSize(qint64 bytes) const
{
    return DiskScannerMonitor::formatSize(bytes);
}
