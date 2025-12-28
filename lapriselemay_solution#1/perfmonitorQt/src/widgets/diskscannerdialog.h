#pragma once

#include <QDialog>
#include <QTabWidget>
#include <QTreeView>
#include <QTableWidget>
#include <QComboBox>
#include <QLineEdit>
#include <QPushButton>
#include <QProgressBar>
#include <QLabel>
#include <QGroupBox>
#include <QSplitter>
#include <QTimer>
#include <memory>

class DiskScannerMonitor;
class TreeMapWidget;

/**
 * @brief Dialog for scanning and analyzing disk space usage
 */
class DiskScannerDialog : public QDialog
{
    Q_OBJECT

public:
    explicit DiskScannerDialog(QWidget* parent = nullptr);
    ~DiskScannerDialog() override;

private slots:
    void onScanClicked();
    void onCancelClicked();
    void onBrowseClicked();
    void onScanStarted(const QString& path);
    void onScanProgress(int files, int dirs, const QString& currentPath);
    void onScanFinished();
    void onScanCancelled();
    void onTreeItemSelected(const QModelIndex& index);
    void onTreeContextMenu(const QPoint& pos);
    void onLargeFileDoubleClicked(int row, int column);
    void onDeleteSelected();
    void onMoveToRecycleBin();
    void onOpenInExplorer();
    void refreshDriveInfo();

private:
    void setupUi();
    void createDriveSelector();
    void createScanTab();
    void createLargeFilesTab();
    void createStatisticsTab();
    void updateStatistics();
    void updateLargeFilesTable();
    void populateDriveCombo();
    QString formatSize(qint64 bytes) const;

    // Main components
    QTabWidget* m_tabWidget{nullptr};
    std::unique_ptr<DiskScannerMonitor> m_scanner;
    
    // Drive selection
    QComboBox* m_driveCombo{nullptr};
    QLineEdit* m_pathEdit{nullptr};
    QPushButton* m_browseBtn{nullptr};
    QPushButton* m_scanBtn{nullptr};
    QPushButton* m_cancelBtn{nullptr};
    QProgressBar* m_progressBar{nullptr};
    QLabel* m_statusLabel{nullptr};
    
    // Drive info
    QLabel* m_driveInfoLabel{nullptr};
    QProgressBar* m_driveUsageBar{nullptr};
    
    // Scan results tab
    QWidget* m_scanTab{nullptr};
    QTreeView* m_treeView{nullptr};
    QLabel* m_selectedInfoLabel{nullptr};
    
    // Large files tab
    QWidget* m_largeFilesTab{nullptr};
    QTableWidget* m_largeFilesTable{nullptr};
    QLabel* m_largeFilesCountLabel{nullptr};
    
    // Statistics tab
    QWidget* m_statsTab{nullptr};
    QLabel* m_totalSizeLabel{nullptr};
    QLabel* m_totalFilesLabel{nullptr};
    QLabel* m_totalDirsLabel{nullptr};
    QLabel* m_scanTimeLabel{nullptr};
    QLabel* m_inaccessibleLabel{nullptr};
    QLabel* m_symlinksLabel{nullptr};
    QTableWidget* m_extensionsTable{nullptr};
    QTableWidget* m_sizeDistTable{nullptr};
    
    // Context menu actions
    QAction* m_openAction{nullptr};
    QAction* m_openExplorerAction{nullptr};
    QAction* m_deleteAction{nullptr};
    QAction* m_recycleBinAction{nullptr};
    QAction* m_copyPathAction{nullptr};
    
    // Timer for drive info refresh
    QTimer* m_driveRefreshTimer{nullptr};
};
