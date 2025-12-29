#pragma once

#include <QDialog>
#include <QTableView>
#include <QTableWidget>
#include <QLineEdit>
#include <QComboBox>
#include <QPushButton>
#include <QCheckBox>
#include <QLabel>
#include <QGroupBox>
#include <QTabWidget>
#include <QTreeWidget>
#include <QSplitter>
#include <QTimer>
#include <memory>

#include "../monitors/servicemonitor.h"

class ServiceMonitor;

/**
 * @brief Dialog for viewing and managing Windows services
 * 
 * Features:
 * - List all services with state, start type, resource usage
 * - Start, stop, restart services
 * - Change startup type
 * - Filter and search
 * - View service dependencies
 * - View crash history
 * - Identify high-resource services
 */
class ServicesDialog : public QDialog
{
    Q_OBJECT

public:
    explicit ServicesDialog(QWidget* parent = nullptr);
    ~ServicesDialog() override;

private slots:
    void onRefresh();
    void onSearchChanged(const QString& text);
    void onFilterChanged();
    void onServiceSelectionChanged();
    void onStartClicked();
    void onStopClicked();
    void onRestartClicked();
    void onChangeStartupType();
    void onServiceDoubleClicked(const QModelIndex& index);
    void onCrashHistoryUpdated();
    void updateResourceStats();

private:
    void setupUi();
    void createToolbar();
    void createMainView();
    void createDetailsPanel();
    void createCrashHistoryTab();
    void createHighResourceTab();
    void updateServiceDetails(const ServiceInfo* service);
    void updateActionButtons(const ServiceInfo* service);
    void showServiceProperties(const QString& serviceName);
    void restoreSelection();
    int findServiceRow(const QString& serviceName) const;

    std::unique_ptr<ServiceMonitor> m_monitor;
    
    // Toolbar
    QLineEdit* m_searchEdit{nullptr};
    QComboBox* m_stateFilter{nullptr};
    QComboBox* m_startTypeFilter{nullptr};
    QCheckBox* m_showWindowsOnly{nullptr};
    QCheckBox* m_showHighResourceOnly{nullptr};
    QPushButton* m_refreshButton{nullptr};
    
    // Main view
    QTableView* m_tableView{nullptr};
    QSplitter* m_splitter{nullptr};
    
    // Details panel
    QTabWidget* m_detailsTabs{nullptr};
    QLabel* m_detailNameLabel{nullptr};
    QLabel* m_detailDisplayNameLabel{nullptr};
    QLabel* m_detailDescriptionLabel{nullptr};
    QLabel* m_detailStateLabel{nullptr};
    QLabel* m_detailStartTypeLabel{nullptr};
    QLabel* m_detailPathLabel{nullptr};
    QLabel* m_detailAccountLabel{nullptr};
    QLabel* m_detailPidLabel{nullptr};
    QLabel* m_detailCpuLabel{nullptr};
    QLabel* m_detailMemoryLabel{nullptr};
    QLabel* m_detailThreadsLabel{nullptr};
    QLabel* m_detailHandlesLabel{nullptr};
    QTreeWidget* m_dependenciesTree{nullptr};
    
    // Action buttons
    QPushButton* m_startButton{nullptr};
    QPushButton* m_stopButton{nullptr};
    QPushButton* m_restartButton{nullptr};
    QComboBox* m_startupTypeCombo{nullptr};
    QPushButton* m_applyStartupButton{nullptr};
    
    // Crash history tab
    QTableWidget* m_crashTable{nullptr};
    
    // High resource tab
    QTableWidget* m_highCpuTable{nullptr};
    QTableWidget* m_highMemoryTable{nullptr};
    
    // Status
    QLabel* m_statusLabel{nullptr};
    QLabel* m_adminLabel{nullptr};
    
    // Refresh timer
    std::unique_ptr<QTimer> m_refreshTimer;
    
    // Currently selected service
    QString m_selectedService;
    QString m_pendingServiceSelection;
};


/**
 * @brief Dialog showing detailed properties of a service
 */
class ServicePropertiesDialog : public QDialog
{
    Q_OBJECT

public:
    explicit ServicePropertiesDialog(const ServiceInfo& service, 
                                      ServiceMonitor* monitor,
                                      QWidget* parent = nullptr);

private slots:
    void onApplyClicked();
    void onStartClicked();
    void onStopClicked();

private:
    void setupUi();
    void updateState();

    ServiceInfo m_service;
    ServiceMonitor* m_monitor{nullptr};
    
    QLabel* m_stateLabel{nullptr};
    QComboBox* m_startupTypeCombo{nullptr};
    QPushButton* m_startButton{nullptr};
    QPushButton* m_stopButton{nullptr};
};
