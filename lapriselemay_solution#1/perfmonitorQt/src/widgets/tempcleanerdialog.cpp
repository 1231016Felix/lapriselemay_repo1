#include "tempcleanerdialog.h"

#include <QVBoxLayout>
#include <QFileDialog>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QHeaderView>
#include <QMessageBox>
#include <QApplication>
#include <QStyle>
#include <QScrollArea>
#include <QPushButton>
#include <QDialogButtonBox>
#include <QMenu>
#include <QClipboard>
#include <QFileInfo>
#include <QDesktopServices>
#include <QUrl>
#include <QDebug>

// ==================== TempCleanerDialog ====================

TempCleanerDialog::TempCleanerDialog(QWidget *parent)
    : QDialog(parent)
{
    setWindowTitle(tr("Advanced System Cleaner"));
    setMinimumSize(800, 600);
    resize(950, 700);
    
    m_cleaner = std::make_unique<TempCleaner>();
    
    setupUi();
    populateCategoryTree();
    updateButtonStates();
    
    // Connect cleaner signals
    connect(m_cleaner.get(), &TempCleaner::analysisProgress,
            this, &TempCleanerDialog::onAnalysisProgress);
    connect(m_cleaner.get(), &TempCleaner::analysisComplete,
            this, &TempCleanerDialog::onAnalysisComplete);
    connect(m_cleaner.get(), &TempCleaner::categoryAnalyzed,
            this, &TempCleanerDialog::onCategoryAnalyzed);
    connect(m_cleaner.get(), &TempCleaner::cleanProgress,
            this, &TempCleanerDialog::onCleanProgress);
    connect(m_cleaner.get(), &TempCleaner::categoryCleaned,
            this, &TempCleanerDialog::onCategoryCleaned);
    connect(m_cleaner.get(), &TempCleaner::cleanComplete,
            this, &TempCleanerDialog::onCleanComplete);
    connect(m_cleaner.get(), &TempCleaner::logMessage,
            this, &TempCleanerDialog::onLogMessage);
    connect(m_cleaner.get(), &TempCleaner::errorOccurred,
            this, &TempCleanerDialog::onError);
}

TempCleanerDialog::~TempCleanerDialog()
{
    if (m_cleaner) {
        m_cleaner->stop();
    }
}

void TempCleanerDialog::setupUi()
{
    QVBoxLayout* mainLayout = new QVBoxLayout(this);
    mainLayout->setSpacing(10);
    
    // Tab widget for main/options/log
    m_tabWidget = new QTabWidget();
    m_tabWidget->addTab(createMainPage(), tr("🧹 Cleaner"));
    m_tabWidget->addTab(createOptionsPage(), tr("⚙️ Options"));
    m_tabWidget->addTab(createLogPage(), tr("📋 Log"));
    
    mainLayout->addWidget(m_tabWidget);
    
    // Bottom buttons
    QHBoxLayout* buttonLayout = new QHBoxLayout();
    
    m_analyzeBtn = new QPushButton(tr("🔍 Analyze"));
    m_analyzeBtn->setMinimumHeight(36);
    connect(m_analyzeBtn, &QPushButton::clicked, this, &TempCleanerDialog::onAnalyze);
    
    m_cleanBtn = new QPushButton(tr("🧹 Clean Selected"));
    m_cleanBtn->setMinimumHeight(36);
    m_cleanBtn->setEnabled(false);
    connect(m_cleanBtn, &QPushButton::clicked, this, &TempCleanerDialog::onClean);
    
    m_stopBtn = new QPushButton(tr("⏹ Stop"));
    m_stopBtn->setMinimumHeight(36);
    m_stopBtn->setVisible(false);
    connect(m_stopBtn, &QPushButton::clicked, this, &TempCleanerDialog::onStop);
    
    m_closeBtn = new QPushButton(tr("Close"));
    m_closeBtn->setMinimumHeight(36);
    connect(m_closeBtn, &QPushButton::clicked, this, &QDialog::accept);
    
    buttonLayout->addWidget(m_analyzeBtn);
    buttonLayout->addWidget(m_cleanBtn);
    buttonLayout->addWidget(m_stopBtn);
    buttonLayout->addStretch();
    buttonLayout->addWidget(m_closeBtn);
    
    mainLayout->addLayout(buttonLayout);
}


QWidget* TempCleanerDialog::createMainPage()
{
    QWidget* page = new QWidget();
    QVBoxLayout* layout = new QVBoxLayout(page);
    
    // Selection buttons
    QHBoxLayout* selectionLayout = new QHBoxLayout();
    
    m_selectAllBtn = new QPushButton(tr("Select All"));
    connect(m_selectAllBtn, &QPushButton::clicked, this, &TempCleanerDialog::onSelectAll);
    
    m_selectNoneBtn = new QPushButton(tr("Select None"));
    connect(m_selectNoneBtn, &QPushButton::clicked, this, &TempCleanerDialog::onSelectNone);
    
    m_selectSafeBtn = new QPushButton(tr("Select Safe Only"));
    connect(m_selectSafeBtn, &QPushButton::clicked, this, &TempCleanerDialog::onSelectSafe);
    
    m_previewBtn = new QPushButton(tr("👁 Preview Files..."));
    connect(m_previewBtn, &QPushButton::clicked, this, &TempCleanerDialog::onPreview);
    
    selectionLayout->addWidget(m_selectAllBtn);
    selectionLayout->addWidget(m_selectNoneBtn);
    selectionLayout->addWidget(m_selectSafeBtn);
    selectionLayout->addStretch();
    selectionLayout->addWidget(m_previewBtn);
    
    layout->addLayout(selectionLayout);
    
    // Category tree
    m_categoryTree = new QTreeWidget();
    m_categoryTree->setHeaderLabels({tr("Category"), tr("Size"), tr("Files"), tr("Risk")});
    m_categoryTree->setColumnWidth(0, 350);
    m_categoryTree->setColumnWidth(1, 100);
    m_categoryTree->setColumnWidth(2, 80);
    m_categoryTree->setColumnWidth(3, 80);
    m_categoryTree->setAlternatingRowColors(true);
    m_categoryTree->setRootIsDecorated(true);
    m_categoryTree->setAnimated(true);
    
    connect(m_categoryTree, &QTreeWidget::itemChanged, [this](QTreeWidgetItem* item, int column) {
        if (column == 0) {
            updateTotalSize();
            updateButtonStates();
        }
    });
    
    layout->addWidget(m_categoryTree, 1);
    
    // Status and progress
    QHBoxLayout* statusLayout = new QHBoxLayout();
    
    m_statusLabel = new QLabel(tr("Ready. Click 'Analyze' to scan for cleanable files."));
    statusLayout->addWidget(m_statusLabel);
    
    statusLayout->addStretch();
    
    m_selectedSizeLabel = new QLabel(tr("Selected: 0 B"));
    m_selectedSizeLabel->setStyleSheet("font-weight: bold;");
    statusLayout->addWidget(m_selectedSizeLabel);
    
    m_totalSizeLabel = new QLabel(tr("Total: 0 B"));
    m_totalSizeLabel->setStyleSheet("font-weight: bold; color: #0078d7;");
    statusLayout->addWidget(m_totalSizeLabel);
    
    layout->addLayout(statusLayout);
    
    m_progressBar = new QProgressBar();
    m_progressBar->setVisible(false);
    layout->addWidget(m_progressBar);
    
    return page;
}

QWidget* TempCleanerDialog::createOptionsPage()
{
    QWidget* page = new QWidget();
    QVBoxLayout* layout = new QVBoxLayout(page);
    
    // Cleaning options group
    QGroupBox* optionsGroup = new QGroupBox(tr("Cleaning Options"));
    QVBoxLayout* optionsLayout = new QVBoxLayout(optionsGroup);
    
    m_dryRunCheck = new QCheckBox(tr("Dry run (simulate cleaning without deleting files)"));
    m_dryRunCheck->setToolTip(tr("Preview what would be deleted without actually removing files"));
    connect(m_dryRunCheck, &QCheckBox::toggled, [this](bool checked) {
        m_cleaner->setDryRun(checked);
    });
    optionsLayout->addWidget(m_dryRunCheck);
    
    m_secureDeleteCheck = new QCheckBox(tr("Secure delete (overwrite files before deletion)"));
    m_secureDeleteCheck->setToolTip(tr("Overwrites files with random data 3 times before deletion"));
    connect(m_secureDeleteCheck, &QCheckBox::toggled, [this](bool checked) {
        m_cleaner->setSecureDelete(checked);
    });
    optionsLayout->addWidget(m_secureDeleteCheck);
    
    m_deleteReadOnlyCheck = new QCheckBox(tr("Delete read-only files"));
    m_deleteReadOnlyCheck->setToolTip(tr("Remove the read-only attribute before deleting"));
    connect(m_deleteReadOnlyCheck, &QCheckBox::toggled, [this](bool checked) {
        m_cleaner->setDeleteReadOnly(checked);
    });
    optionsLayout->addWidget(m_deleteReadOnlyCheck);
    
    // Minimum age
    QHBoxLayout* ageLayout = new QHBoxLayout();
    ageLayout->addWidget(new QLabel(tr("Only delete files older than:")));
    
    m_minAgeSpin = new QSpinBox();
    m_minAgeSpin->setRange(0, 365);
    m_minAgeSpin->setSuffix(tr(" days"));
    m_minAgeSpin->setToolTip(tr("0 = delete all files regardless of age"));
    connect(m_minAgeSpin, QOverload<int>::of(&QSpinBox::valueChanged), [this](int value) {
        m_cleaner->setMinFileAge(value);
    });
    ageLayout->addWidget(m_minAgeSpin);
    ageLayout->addStretch();
    optionsLayout->addLayout(ageLayout);
    
    layout->addWidget(optionsGroup);
    
    // Admin status
    QGroupBox* adminGroup = new QGroupBox(tr("Administrator Status"));
    QVBoxLayout* adminLayout = new QVBoxLayout(adminGroup);
    
    QLabel* adminLabel = new QLabel();
    if (TempCleaner::isAdmin()) {
        adminLabel->setText(tr("✓ Running as Administrator - All features available"));
        adminLabel->setStyleSheet("color: #4CAF50; font-weight: bold;");
    } else {
        adminLabel->setText(tr("⚠ Not running as Administrator - Some categories may be limited"));
        adminLabel->setStyleSheet("color: #FF9800; font-weight: bold;");
    }
    adminLayout->addWidget(adminLabel);
    
    layout->addWidget(adminGroup);
    layout->addStretch();
    
    return page;
}


QWidget* TempCleanerDialog::createLogPage()
{
    QWidget* page = new QWidget();
    QVBoxLayout* layout = new QVBoxLayout(page);
    
    m_logText = new QTextEdit();
    m_logText->setReadOnly(true);
    m_logText->setFont(QFont("Consolas", 9));
    m_logText->setStyleSheet("QTextEdit { background-color: #1e1e1e; color: #d4d4d4; }");
    
    layout->addWidget(m_logText);
    
    // Clear log button
    QPushButton* clearBtn = new QPushButton(tr("Clear Log"));
    connect(clearBtn, &QPushButton::clicked, m_logText, &QTextEdit::clear);
    
    QHBoxLayout* btnLayout = new QHBoxLayout();
    btnLayout->addStretch();
    btnLayout->addWidget(clearBtn);
    layout->addLayout(btnLayout);
    
    return page;
}

void TempCleanerDialog::populateCategoryTree()
{
    m_categoryTree->clear();
    m_categoryItems.clear();
    m_groupItems.clear();
    
    const auto& categories = m_cleaner->categories();
    
    for (const auto& cat : categories) {
        // Get or create group item
        QTreeWidgetItem* groupItem = nullptr;
        if (m_groupItems.count(cat.group)) {
            groupItem = m_groupItems.at(cat.group);
        } else {
            groupItem = new QTreeWidgetItem(m_categoryTree);
            groupItem->setText(0, cat.group);
            groupItem->setFlags(groupItem->flags() | Qt::ItemIsAutoTristate);
            groupItem->setCheckState(0, Qt::Unchecked);
            groupItem->setExpanded(true);
            
            // Set group icon based on name
            if (cat.group == "Windows") {
                groupItem->setIcon(0, QIcon(":/icons/cpu.png"));
            } else if (cat.group.contains("Chrome") || cat.group.contains("Firefox") || 
                       cat.group.contains("Edge") || cat.group.contains("Brave")) {
                groupItem->setIcon(0, QIcon(":/icons/network.png"));
            } else if (cat.group == "Development") {
                groupItem->setIcon(0, QIcon(":/icons/process.png"));
            } else {
                groupItem->setIcon(0, QIcon(":/icons/disk.png"));
            }
            
            m_groupItems[cat.group] = groupItem;
        }
        
        // Create category item
        QTreeWidgetItem* item = new QTreeWidgetItem(groupItem);
        item->setText(0, QString("%1 %2").arg(cat.icon, cat.name));
        item->setText(1, "-");
        item->setText(2, "-");
        item->setText(3, getRiskText(cat.riskLevel));
        item->setFlags(item->flags() | Qt::ItemIsUserCheckable);
        item->setCheckState(0, cat.isSelected ? Qt::Checked : Qt::Unchecked);
        item->setToolTip(0, cat.description);
        item->setData(0, Qt::UserRole, static_cast<int>(cat.category));
        
        // Risk level styling
        item->setForeground(3, QBrush(getRiskColor(cat.riskLevel)));
        
        // Admin badge
        if (cat.requiresAdmin && !TempCleaner::isAdmin()) {
            item->setForeground(0, QBrush(Qt::gray));
            item->setToolTip(0, cat.description + tr("\n\n⚠ Requires Administrator privileges"));
        }
        
        m_categoryItems[cat.category] = item;
    }
}

QColor TempCleanerDialog::getRiskColor(CleanRiskLevel level)
{
    switch (level) {
        case CleanRiskLevel::Safe: return QColor(76, 175, 80);      // Green
        case CleanRiskLevel::Low: return QColor(139, 195, 74);      // Light Green
        case CleanRiskLevel::Medium: return QColor(255, 152, 0);    // Orange
        case CleanRiskLevel::High: return QColor(244, 67, 54);      // Red
        case CleanRiskLevel::Critical: return QColor(183, 28, 28);  // Dark Red
        default: return Qt::white;
    }
}

QString TempCleanerDialog::getRiskStyleSheet(CleanRiskLevel level)
{
    QColor color = getRiskColor(level);
    return QString("color: %1;").arg(color.name());
}

QString TempCleanerDialog::getRiskText(CleanRiskLevel level)
{
    switch (level) {
        case CleanRiskLevel::Safe: return tr("Safe");
        case CleanRiskLevel::Low: return tr("Low");
        case CleanRiskLevel::Medium: return tr("Medium");
        case CleanRiskLevel::High: return tr("High");
        case CleanRiskLevel::Critical: return tr("Critical");
        default: return tr("Unknown");
    }
}

QIcon TempCleanerDialog::getCategoryIcon(const QString& iconStr)
{
    // Could map emoji to actual icons here
    return QIcon();
}


// ==================== Button Handlers ====================

void TempCleanerDialog::onAnalyze()
{
    if (m_isAnalyzing || m_isCleaning) return;
    
    m_isAnalyzing = true;
    updateButtonStates();
    
    m_progressBar->setVisible(true);
    m_progressBar->setRange(0, m_cleaner->categories().size());
    m_progressBar->setValue(0);
    
    m_statusLabel->setText(tr("Analyzing..."));
    addLogEntry(tr("Starting analysis..."));
    
    // Reset sizes
    for (auto& [cat, item] : m_categoryItems) {
        item->setText(1, tr("Scanning..."));
        item->setText(2, "-");
    }
    
    // Run analysis in background
    QThread* thread = QThread::create([this]() {
        m_cleaner->analyzeAll();
    });
    
    connect(thread, &QThread::finished, thread, &QThread::deleteLater);
    thread->start();
}

void TempCleanerDialog::onClean()
{
    if (m_isAnalyzing || m_isCleaning) return;
    
    // Collect selected categories
    for (auto& [cat, item] : m_categoryItems) {
        m_cleaner->setSelected(cat, item->checkState(0) == Qt::Checked);
    }
    
    int selectedCount = m_cleaner->selectedCount();
    if (selectedCount == 0) {
        QMessageBox::information(this, tr("Nothing Selected"),
            tr("Please select at least one category to clean."));
        return;
    }
    
    // Confirm cleaning
    QString message;
    if (m_cleaner->isDryRun()) {
        message = tr("This is a DRY RUN. No files will actually be deleted.\n\n"
                     "Proceed with simulation?");
    } else {
        message = tr("You are about to delete files from %1 categories.\n\n"
                     "Estimated size to free: %2\n\n"
                     "This action cannot be undone. Continue?")
                     .arg(selectedCount)
                     .arg(TempCleaner::formatBytes(m_cleaner->selectedSize()));
    }
    
    if (QMessageBox::question(this, tr("Confirm Cleaning"), message,
                              QMessageBox::Yes | QMessageBox::No) != QMessageBox::Yes) {
        return;
    }
    
    m_isCleaning = true;
    updateButtonStates();
    
    m_progressBar->setVisible(true);
    m_progressBar->setRange(0, 0); // Indeterminate
    
    m_statusLabel->setText(tr("Cleaning..."));
    addLogEntry(tr("Starting cleaning operation..."));
    
    // Switch to log tab
    m_tabWidget->setCurrentIndex(2);
    
    // Run cleaning in background
    QThread* thread = QThread::create([this]() {
        m_cleaner->cleanSelected();
    });
    
    connect(thread, &QThread::finished, thread, &QThread::deleteLater);
    thread->start();
}

void TempCleanerDialog::onStop()
{
    m_cleaner->stop();
    m_statusLabel->setText(tr("Stopping..."));
    addLogEntry(tr("Stop requested..."));
}

void TempCleanerDialog::onPreview()
{
    // Collect selected categories
    std::vector<CleanCategory> selected;
    for (auto& [cat, item] : m_categoryItems) {
        if (item->checkState(0) == Qt::Checked) {
            selected.push_back(cat);
        }
    }
    
    if (selected.empty()) {
        QMessageBox::information(this, tr("Nothing Selected"),
            tr("Please select at least one category to preview."));
        return;
    }
    
    FilePreviewDialog dialog(m_cleaner.get(), this);
    dialog.exec();
}

void TempCleanerDialog::onSelectAll()
{
    for (auto& [cat, item] : m_categoryItems) {
        item->setCheckState(0, Qt::Checked);
    }
    m_cleaner->selectAll(true);
    updateTotalSize();
    updateButtonStates();
}

void TempCleanerDialog::onSelectNone()
{
    for (auto& [cat, item] : m_categoryItems) {
        item->setCheckState(0, Qt::Unchecked);
    }
    m_cleaner->selectAll(false);
    updateTotalSize();
    updateButtonStates();
}

void TempCleanerDialog::onSelectSafe()
{
    m_cleaner->selectSafeOnly();
    
    for (auto& cat : m_cleaner->categories()) {
        if (m_categoryItems.count(cat.category)) {
            m_categoryItems[cat.category]->setCheckState(0, 
                cat.isSelected ? Qt::Checked : Qt::Unchecked);
        }
    }
    
    updateTotalSize();
    updateButtonStates();
}


// ==================== Cleaner Signal Handlers ====================

void TempCleanerDialog::onAnalysisProgress(int current, int total, const QString& category)
{
    m_progressBar->setMaximum(total);
    m_progressBar->setValue(current);
    m_statusLabel->setText(tr("Analyzing: %1 (%2/%3)").arg(category).arg(current).arg(total));
}

void TempCleanerDialog::onAnalysisComplete()
{
    m_isAnalyzing = false;
    m_progressBar->setVisible(false);
    
    updateTotalSize();
    updateButtonStates();
    
    m_statusLabel->setText(tr("Analysis complete. Total: %1 in %2 files.")
        .arg(TempCleaner::formatBytes(m_cleaner->totalEstimatedSize()))
        .arg(countTotalFiles()));
    
    addLogEntry(tr("Analysis complete. Found %1 to clean.")
        .arg(TempCleaner::formatBytes(m_cleaner->totalEstimatedSize())));
}

void TempCleanerDialog::onCategoryAnalyzed(CleanCategory category, qint64 size, int fileCount)
{
    if (m_categoryItems.count(category)) {
        QTreeWidgetItem* item = m_categoryItems[category];
        item->setText(1, TempCleaner::formatBytes(size));
        item->setText(2, QString::number(fileCount));
        
        // Update category info
        auto& catInfo = m_cleaner->categoryInfo(category);
        catInfo.estimatedSize = size;
        catInfo.fileCount = fileCount;
    }
}

void TempCleanerDialog::onCleanProgress(int current, int total, const QString& currentFile)
{
    if (total > 0) {
        m_progressBar->setRange(0, total);
        m_progressBar->setValue(current);
    }
    
    QString shortPath = currentFile;
    if (shortPath.length() > 60) {
        shortPath = "..." + shortPath.right(57);
    }
    m_statusLabel->setText(tr("Cleaning: %1").arg(shortPath));
}

void TempCleanerDialog::onCategoryCleaned(CleanCategory category, const CleanResult& result)
{
    if (m_categoryItems.count(category)) {
        QTreeWidgetItem* item = m_categoryItems[category];
        
        if (result.success) {
            item->setText(1, tr("✓ %1 freed").arg(TempCleaner::formatBytes(result.bytesFreed)));
            item->setForeground(1, QBrush(QColor(76, 175, 80)));
        } else {
            item->setText(1, tr("⚠ Partial"));
            item->setForeground(1, QBrush(QColor(255, 152, 0)));
        }
        
        item->setText(2, QString("%1/%2")
            .arg(result.filesDeleted)
            .arg(result.filesDeleted + result.filesFailed));
    }
    
    auto& catInfo = m_cleaner->categoryInfo(category);
    addLogEntry(tr("%1: Deleted %2 files, freed %3")
        .arg(catInfo.name)
        .arg(result.filesDeleted)
        .arg(TempCleaner::formatBytes(result.bytesFreed)));
    
    for (const QString& error : result.errors) {
        addLogEntry(tr("  Error: %1").arg(error), true);
    }
}

void TempCleanerDialog::onCleanComplete(const CleanSummary& summary)
{
    m_isCleaning = false;
    m_progressBar->setVisible(false);
    updateButtonStates();
    
    showSummary(summary);
    
    addLogEntry(tr("Cleaning complete!"));
    addLogEntry(tr("  Total freed: %1").arg(TempCleaner::formatBytes(summary.totalBytesFreed)));
    addLogEntry(tr("  Files deleted: %1").arg(summary.totalFilesDeleted));
    addLogEntry(tr("  Files failed: %1").arg(summary.totalFilesFailed));
    addLogEntry(tr("  Duration: %1 seconds")
        .arg(summary.startTime.secsTo(summary.endTime)));
    
    emit cleaningComplete(summary.totalBytesFreed);
}

void TempCleanerDialog::onLogMessage(const QString& message)
{
    addLogEntry(message);
}

void TempCleanerDialog::onError(const QString& error)
{
    addLogEntry(tr("ERROR: %1").arg(error), true);
}


// ==================== Utility Methods ====================

void TempCleanerDialog::updateCategoryItem(QTreeWidgetItem* item, CleanCategoryInfo& info)
{
    item->setText(1, TempCleaner::formatBytes(info.estimatedSize));
    item->setText(2, QString::number(info.fileCount));
}

void TempCleanerDialog::updateTotalSize()
{
    qint64 totalSize = m_cleaner->totalEstimatedSize();
    qint64 selectedSize = 0;
    
    for (auto& [cat, item] : m_categoryItems) {
        if (item->checkState(0) == Qt::Checked) {
            selectedSize += m_cleaner->categoryInfo(cat).estimatedSize;
        }
    }
    
    m_totalSizeLabel->setText(tr("Total: %1").arg(TempCleaner::formatBytes(totalSize)));
    m_selectedSizeLabel->setText(tr("Selected: %1").arg(TempCleaner::formatBytes(selectedSize)));
}

int TempCleanerDialog::countTotalFiles()
{
    int total = 0;
    for (const auto& cat : m_cleaner->categories()) {
        total += cat.fileCount;
    }
    return total;
}

void TempCleanerDialog::updateButtonStates()
{
    bool hasSelection = false;
    for (auto& [cat, item] : m_categoryItems) {
        if (item->checkState(0) == Qt::Checked) {
            hasSelection = true;
            break;
        }
    }
    
    m_analyzeBtn->setEnabled(!m_isAnalyzing && !m_isCleaning);
    m_cleanBtn->setEnabled(hasSelection && !m_isAnalyzing && !m_isCleaning);
    m_stopBtn->setVisible(m_isAnalyzing || m_isCleaning);
    m_stopBtn->setEnabled(m_isAnalyzing || m_isCleaning);
    
    m_selectAllBtn->setEnabled(!m_isAnalyzing && !m_isCleaning);
    m_selectNoneBtn->setEnabled(!m_isAnalyzing && !m_isCleaning);
    m_selectSafeBtn->setEnabled(!m_isAnalyzing && !m_isCleaning);
    m_previewBtn->setEnabled(hasSelection && !m_isAnalyzing && !m_isCleaning);
}

void TempCleanerDialog::addLogEntry(const QString& message, bool isError)
{
    QString timestamp = QDateTime::currentDateTime().toString("hh:mm:ss");
    QString color = isError ? "#ff5252" : "#d4d4d4";
    
    m_logText->append(QString("<span style='color: #888;'>[%1]</span> "
                              "<span style='color: %2;'>%3</span>")
        .arg(timestamp, color, message.toHtmlEscaped()));
}

void TempCleanerDialog::showSummary(const CleanSummary& summary)
{
    QString title = m_cleaner->isDryRun() ? tr("Dry Run Complete") : tr("Cleaning Complete");
    
    QString message = m_cleaner->isDryRun() 
        ? tr("This was a simulation. No files were actually deleted.\n\n")
        : QString();
    
    message += tr("Space freed: %1\n"
                  "Files deleted: %2\n"
                  "Files failed: %3\n"
                  "Categories cleaned: %4\n"
                  "Duration: %5 seconds")
        .arg(TempCleaner::formatBytes(summary.totalBytesFreed))
        .arg(summary.totalFilesDeleted)
        .arg(summary.totalFilesFailed)
        .arg(summary.categoriesCleaned)
        .arg(summary.startTime.secsTo(summary.endTime));
    
    m_statusLabel->setText(tr("Done! Freed %1").arg(TempCleaner::formatBytes(summary.totalBytesFreed)));
    
    QMessageBox::information(this, title, message);
}

// ==================== FilePreviewDialog ====================

FilePreviewDialog::FilePreviewDialog(TempCleaner* cleaner, QWidget* parent)
    : QDialog(parent), m_cleaner(cleaner)
{
    setWindowTitle(tr("File Preview"));
    setMinimumSize(700, 500);
    resize(800, 600);
    
    setupUi();
    populateFiles();
}

void FilePreviewDialog::setupUi()
{
    QVBoxLayout* layout = new QVBoxLayout(this);
    
    m_fileTree = new QTreeWidget();
    m_fileTree->setHeaderLabels({tr("File"), tr("Size"), tr("Modified")});
    m_fileTree->setColumnWidth(0, 450);
    m_fileTree->setColumnWidth(1, 100);
    m_fileTree->setAlternatingRowColors(true);
    m_fileTree->setRootIsDecorated(true);
    
    // Context menu for file tree
    m_fileTree->setContextMenuPolicy(Qt::CustomContextMenu);
    connect(m_fileTree, &QTreeWidget::customContextMenuRequested, [this](const QPoint& pos) {
        QTreeWidgetItem* item = m_fileTree->itemAt(pos);
        if (!item || item->childCount() > 0) return;  // Skip group items
        
        QString path = item->data(0, Qt::UserRole).toString();
        if (path.isEmpty()) return;
        
        QMenu menu;
        menu.addAction(tr("Open Location"), [path]() {
            QFileInfo fi(path);
            QDesktopServices::openUrl(QUrl::fromLocalFile(fi.absolutePath()));
        });
        menu.addAction(tr("Copy Path"), [path]() {
            QApplication::clipboard()->setText(path);
        });
        menu.exec(m_fileTree->viewport()->mapToGlobal(pos));
    });
    
    layout->addWidget(m_fileTree);
    
    // Total label
    m_totalLabel = new QLabel();
    layout->addWidget(m_totalLabel);
    
    // Close button
    QPushButton* closeBtn = new QPushButton(tr("Close"));
    connect(closeBtn, &QPushButton::clicked, this, &QDialog::accept);
    
    QHBoxLayout* btnLayout = new QHBoxLayout();
    btnLayout->addStretch();
    btnLayout->addWidget(closeBtn);
    layout->addLayout(btnLayout);
}

void FilePreviewDialog::populateFiles()
{
    m_fileTree->clear();
    
    qint64 totalSize = 0;
    int totalFiles = 0;
    
    for (const auto& cat : m_cleaner->categories()) {
        if (!cat.isSelected) continue;
        
        auto files = m_cleaner->getFilesForCategory(cat.category, 100);
        if (files.empty()) continue;
        
        // Create category group
        QTreeWidgetItem* groupItem = new QTreeWidgetItem(m_fileTree);
        groupItem->setText(0, QString("%1 %2 (%3 files)")
            .arg(cat.icon, cat.name)
            .arg(files.size()));
        groupItem->setExpanded(false);
        
        qint64 categorySize = 0;
        
        for (const auto& file : files) {
            QTreeWidgetItem* item = new QTreeWidgetItem(groupItem);
            
            QString displayPath = file.path;
            if (displayPath.length() > 80) {
                displayPath = "..." + displayPath.right(77);
            }
            
            item->setText(0, displayPath);
            item->setText(1, TempCleaner::formatBytes(file.size));
            item->setText(2, file.lastModified.toString("yyyy-MM-dd hh:mm"));
            item->setData(0, Qt::UserRole, file.path);
            item->setToolTip(0, file.path);
            
            categorySize += file.size;
            totalFiles++;
        }
        
        totalSize += categorySize;
        groupItem->setText(1, TempCleaner::formatBytes(categorySize));
    }
    
    m_totalLabel->setText(tr("Showing %1 files, total size: %2 (limited to 100 files per category)")
        .arg(totalFiles)
        .arg(TempCleaner::formatBytes(totalSize)));
}

