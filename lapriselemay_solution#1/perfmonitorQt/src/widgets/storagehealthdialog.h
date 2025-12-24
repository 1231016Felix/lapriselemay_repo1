#pragma once

#include <QDialog>
#include <QFrame>
#include <QTabWidget>
#include <QLabel>
#include <QProgressBar>
#include <QTableView>
#include <QComboBox>
#include <QPushButton>
#include <QTimer>
#include <QGroupBox>
#include <QCheckBox>
#include <QVBoxLayout>
#include <QSplitter>
#include <memory>

#include "monitors/storagehealthmonitor.h"

/**
 * @brief Widget displaying a single disk's health summary
 */
class DiskHealthCard : public QFrame
{
    Q_OBJECT

public:
    explicit DiskHealthCard(QWidget *parent = nullptr);
    
    void setDiskInfo(const DiskHealthInfo& info);
    QString devicePath() const { return m_devicePath; }

signals:
    void detailsRequested(const QString& devicePath);

private:
    void setupUi();
    void updateHealthIndicator(DriveHealthStatus status, int percent);
    
    QString m_devicePath;
    
    QLabel* m_iconLabel{nullptr};
    QLabel* m_modelLabel{nullptr};
    QLabel* m_typeLabel{nullptr};
    QLabel* m_capacityLabel{nullptr};
    QLabel* m_healthLabel{nullptr};
    QLabel* m_temperatureLabel{nullptr};
    QProgressBar* m_healthBar{nullptr};
    QPushButton* m_detailsButton{nullptr};
};

/**
 * @brief Detailed view of a single disk's SMART data
 */
class DiskDetailWidget : public QWidget
{
    Q_OBJECT

public:
    explicit DiskDetailWidget(QWidget *parent = nullptr);
    
    void setDiskInfo(const DiskHealthInfo& info);
    void clear();

private:
    void setupUi();
    void updateNvmeSection(const DiskHealthInfo& info);
    void updateSmartTable(const DiskHealthInfo& info);
    
    // Info section
    QLabel* m_modelLabel{nullptr};
    QLabel* m_serialLabel{nullptr};
    QLabel* m_firmwareLabel{nullptr};
    QLabel* m_interfaceLabel{nullptr};
    QLabel* m_capacityLabel{nullptr};
    
    // Health section
    QLabel* m_healthStatusLabel{nullptr};
    QLabel* m_healthPercentLabel{nullptr};
    QProgressBar* m_healthBar{nullptr};
    QLabel* m_healthDescLabel{nullptr};
    
    // Temperature section
    QLabel* m_tempLabel{nullptr};
    QLabel* m_tempStatusLabel{nullptr};
    
    // Power section
    QLabel* m_powerOnHoursLabel{nullptr};
    QLabel* m_powerCyclesLabel{nullptr};
    QLabel* m_lifeRemainingLabel{nullptr};
    
    // NVMe specific
    QGroupBox* m_nvmeGroup{nullptr};
    QLabel* m_nvmeSpareLabel{nullptr};
    QLabel* m_nvmeUsedLabel{nullptr};
    QLabel* m_nvmeWrittenLabel{nullptr};
    QLabel* m_nvmeReadLabel{nullptr};
    QLabel* m_nvmeErrorsLabel{nullptr};
    QLabel* m_nvmeShutdownsLabel{nullptr};
    
    // SMART table
    QTableView* m_smartTable{nullptr};
    std::unique_ptr<SmartAttributeModel> m_smartModel;
    
    // Alerts section
    QLabel* m_alertsLabel{nullptr};
};

/**
 * @brief Main dialog for storage health monitoring
 */
class StorageHealthDialog : public QDialog
{
    Q_OBJECT

public:
    explicit StorageHealthDialog(QWidget *parent = nullptr);
    ~StorageHealthDialog() override;

private slots:
    void refreshData();
    void onDiskSelected(int index);
    void exportReport();
    void showDiskDetails(const QString& devicePath);
    void onAutoRefreshToggled(bool enabled);

private:
    void setupUi();
    void populateDiskList();
    void updateDiskCards();
    
    std::unique_ptr<StorageHealthMonitor> m_monitor;
    QTimer* m_refreshTimer{nullptr};
    
    // UI
    QComboBox* m_diskCombo{nullptr};
    QWidget* m_cardsContainer{nullptr};
    QVBoxLayout* m_cardsLayout{nullptr};
    std::vector<DiskHealthCard*> m_diskCards;
    
    DiskDetailWidget* m_detailWidget{nullptr};
    
    QPushButton* m_refreshButton{nullptr};
    QPushButton* m_exportButton{nullptr};
    QCheckBox* m_autoRefreshCheck{nullptr};
    
    QLabel* m_lastUpdateLabel{nullptr};
    QLabel* m_adminWarningLabel{nullptr};
};
