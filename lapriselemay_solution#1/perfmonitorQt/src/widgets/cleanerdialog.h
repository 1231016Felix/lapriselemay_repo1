#pragma once

#include <QDialog>
#include <QTreeWidget>
#include <QProgressBar>
#include <QLabel>
#include <QPushButton>
#include <QCheckBox>
#include <QTextEdit>
#include <QStackedWidget>
#include <QThread>
#include <memory>

#include "../cleaners/systemcleaner.h"

/**
 * @brief Worker thread for scanning and cleaning operations
 */
class CleanerWorker : public QObject
{
    Q_OBJECT

public:
    explicit CleanerWorker(SystemCleaner* cleaner, QObject* parent = nullptr);

public slots:
    void scan();
    void clean();
    void cancel();

signals:
    void scanStarted();
    void scanProgress(int current, int total, const QString& currentItem);
    void scanItemCompleted(CleanerCategory category, qint64 size, int files);
    void scanCompleted(qint64 totalSize, int totalFiles);
    void scanCancelled();
    
    void cleaningStarted();
    void cleaningProgress(int current, int total, const QString& currentFile);
    void cleaningItemCompleted(CleanerCategory category, qint64 freedSize, int deletedFiles);
    void cleaningCompleted(const CleaningResult& result);
    void cleaningCancelled();

private:
    SystemCleaner* m_cleaner;
};

/**
 * @brief System Cleaner Dialog - CCleaner/BleachBit style interface
 */
class CleanerDialog : public QDialog
{
    Q_OBJECT

public:
    explicit CleanerDialog(QWidget *parent = nullptr);
    ~CleanerDialog() override;

protected:
    void closeEvent(QCloseEvent* event) override;

private slots:
    void onAnalyze();
    void onClean();
    void onCancel();
    void onSelectAll();
    void onDeselectAll();
    void onItemChanged(QTreeWidgetItem* item, int column);
    
    // Worker signals
    void onScanStarted();
    void onScanProgress(int current, int total, const QString& currentItem);
    void onScanItemCompleted(CleanerCategory category, qint64 size, int files);
    void onScanCompleted(qint64 totalSize, int totalFiles);
    void onScanCancelled();
    
    void onCleaningStarted();
    void onCleaningProgress(int current, int total, const QString& currentFile);
    void onCleaningItemCompleted(CleanerCategory category, qint64 freedSize, int deletedFiles);
    void onCleaningCompleted(const CleaningResult& result);
    void onCleaningCancelled();

private:
    void setupUi();
    void setupCategories();
    void populateTree();
    void updateCategorySize(CleanerCategory category, qint64 size, int files);
    void collectSelectedCategories();
    void setButtonsEnabled(bool enabled);
    
    QTreeWidgetItem* findCategoryItem(CleanerCategory category);
    QString getCategoryGroupName(CleanerCategory category);
    QIcon getCategoryIcon(CleanerCategory category);
    
    // UI Components
    QTreeWidget* m_treeWidget{nullptr};
    QStackedWidget* m_stackedWidget{nullptr};
    
    // Analysis page
    QWidget* m_analysisPage{nullptr};
    
    // Progress page
    QWidget* m_progressPage{nullptr};
    QProgressBar* m_progressBar{nullptr};
    QLabel* m_progressLabel{nullptr};
    QLabel* m_statusLabel{nullptr};
    QTextEdit* m_logTextEdit{nullptr};
    
    // Results page
    QWidget* m_resultsPage{nullptr};
    QLabel* m_resultsLabel{nullptr};
    QLabel* m_resultsSizeLabel{nullptr};
    QLabel* m_resultsFilesLabel{nullptr};
    QLabel* m_resultsTimeLabel{nullptr};
    
    // Buttons
    QPushButton* m_analyzeButton{nullptr};
    QPushButton* m_cleanButton{nullptr};
    QPushButton* m_cancelButton{nullptr};
    QPushButton* m_closeButton{nullptr};
    
    // Total display
    QLabel* m_totalSizeLabel{nullptr};
    QLabel* m_totalFilesLabel{nullptr};
    
    // Cleaner
    std::unique_ptr<SystemCleaner> m_cleaner;
    QThread* m_workerThread{nullptr};
    CleanerWorker* m_worker{nullptr};
    
    // State
    bool m_hasScanned{false};
    bool m_isWorking{false};
    
    // Category to tree item mapping
    QMap<CleanerCategory, QTreeWidgetItem*> m_categoryItems;
};
