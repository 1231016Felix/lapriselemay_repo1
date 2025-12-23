#pragma once

#include <QDialog>
#include <QTreeWidget>
#include <QProgressBar>
#include <QLabel>
#include <QPushButton>
#include <QTextEdit>
#include <QTabWidget>
#include <QCheckBox>
#include <QSpinBox>
#include <QGroupBox>
#include <QStackedWidget>
#include <QThread>
#include <memory>

#include "../utils/tempcleaner.h"

class CleanerWorker;

/**
 * @brief Powerful temp file cleaner dialog
 * 
 * Features:
 * - Category tree with checkboxes
 * - Size estimation and preview
 * - Progress tracking
 * - Detailed log
 * - Options (dry run, secure delete, etc.)
 */
class TempCleanerDialog : public QDialog
{
    Q_OBJECT

public:
    explicit TempCleanerDialog(QWidget *parent = nullptr);
    ~TempCleanerDialog() override;

signals:
    void cleaningComplete(qint64 bytesFreed);

private slots:
    void onAnalyze();
    void onClean();
    void onStop();
    void onPreview();
    void onSelectAll();
    void onSelectNone();
    void onSelectSafe();
    
    // From cleaner
    void onAnalysisProgress(int current, int total, const QString& category);
    void onAnalysisComplete();
    void onCategoryAnalyzed(CleanCategory category, qint64 size, int fileCount);
    void onCleanProgress(int current, int total, const QString& currentFile);
    void onCategoryCleaned(CleanCategory category, const CleanResult& result);
    void onCleanComplete(const CleanSummary& summary);
    void onLogMessage(const QString& message);
    void onError(const QString& error);

private:
    void setupUi();
    QWidget* createMainPage();
    QWidget* createOptionsPage();
    QWidget* createLogPage();
    
    void populateCategoryTree();
    void updateCategoryItem(QTreeWidgetItem* item, CleanCategoryInfo& info);
    void updateTotalSize();
    void updateButtonStates();
    void addLogEntry(const QString& message, bool isError = false);
    void showSummary(const CleanSummary& summary);
    
    QString getRiskStyleSheet(CleanRiskLevel level);
    QString getRiskText(CleanRiskLevel level);
    QIcon getCategoryIcon(const QString& iconStr);
    
    // UI Components
    QStackedWidget* m_stackedWidget{nullptr};
    QTabWidget* m_tabWidget{nullptr};
    
    // Main page
    QTreeWidget* m_categoryTree{nullptr};
    QLabel* m_totalSizeLabel{nullptr};
    QLabel* m_selectedSizeLabel{nullptr};
    QLabel* m_statusLabel{nullptr};
    QProgressBar* m_progressBar{nullptr};
    
    QPushButton* m_analyzeBtn{nullptr};
    QPushButton* m_cleanBtn{nullptr};
    QPushButton* m_stopBtn{nullptr};
    QPushButton* m_previewBtn{nullptr};
    QPushButton* m_selectAllBtn{nullptr};
    QPushButton* m_selectNoneBtn{nullptr};
    QPushButton* m_selectSafeBtn{nullptr};
    
    // Options page
    QCheckBox* m_dryRunCheck{nullptr};
    QCheckBox* m_secureDeleteCheck{nullptr};
    QCheckBox* m_deleteReadOnlyCheck{nullptr};
    QSpinBox* m_minAgeSpin{nullptr};
    
    // Log page
    QTextEdit* m_logText{nullptr};
    
    // Close button
    QPushButton* m_closeBtn{nullptr};
    
    // Data
    std::unique_ptr<TempCleaner> m_cleaner;
    QThread* m_workerThread{nullptr};
    
    // Category item mapping
    std::map<CleanCategory, QTreeWidgetItem*> m_categoryItems;
    std::map<QString, QTreeWidgetItem*> m_groupItems;
    
    // State
    bool m_isAnalyzing{false};
    bool m_isCleaning{false};
};

/**
 * @brief File preview dialog
 */
class FilePreviewDialog : public QDialog
{
    Q_OBJECT

public:
    explicit FilePreviewDialog(TempCleaner* cleaner, QWidget* parent = nullptr);

private:
    void setupUi();
    void populateFiles();
    
    TempCleaner* m_cleaner;
    QTreeWidget* m_fileTree{nullptr};
    QLabel* m_totalLabel{nullptr};
};
