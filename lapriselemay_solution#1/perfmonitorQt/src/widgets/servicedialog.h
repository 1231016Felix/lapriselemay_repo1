#pragma once

#include <QDialog>
#include <QTableView>
#include <QLineEdit>
#include <QPushButton>
#include <QLabel>
#include <QComboBox>
#include <QGroupBox>
#include <QSortFilterProxyModel>
#include <QSplitter>
#include <QTabWidget>
#include <memory>

#include "monitors/servicemonitor.h"

/**
 * @brief Filter proxy for service table
 */
class ServiceFilterProxy : public QSortFilterProxyModel
{
    Q_OBJECT

public:
    explicit ServiceFilterProxy(QObject *parent = nullptr);
    
    void setFilterState(const QString& state);
    void setFilterStartType(const QString& startType);
    void setSearchText(const QString& text);

protected:
    bool filterAcceptsRow(int sourceRow, const QModelIndex &sourceParent) const override;

private:
    QString m_filterState;
    QString m_filterStartType;
    QString m_searchText;
};

/**
 * @brief Detailed view for a single service
 */
class ServiceDetailWidget : public QWidget
{
    Q_OBJECT

public:
    explicit ServiceDetailWidget(QWidget *parent = nullptr);
    
    void setService(const ServiceInfo* service);
    void clear();

private:
    void setupUi();
    
    QLabel* m_nameLabel{nullptr};
    QLabel* m_displayNameLabel{nullptr};
    QLabel* m_descriptionLabel{nullptr};
    QLabel* m_stateLabel{nullptr};
    QLabel* m_startTypeLabel{nullptr};
    QLabel* m_pathLabel{nullptr};
    QLabel* m_accountLabel{nullptr};
    QLabel* m_pidLabel{nullptr};
    QLabel* m_memoryLabel{nullptr};
    QLabel* m_dependenciesLabel{nullptr};
};

/**
 * @brief Widget showing service crash history
 */
class CrashHistoryWidget : public QWidget
{
    Q_OBJECT

public:
    explicit CrashHistoryWidget(QWidget *parent = nullptr);
    
    void setCrashEvents(const std::vector<ServiceCrashEvent>& events);
    void clear();

private:
    void setupUi();
    
    QTableView* m_tableView{nullptr};
};

/**
 * @brief Main dialog for Windows Service Management
 */
class ServiceDialog : public QDialog
{
    Q_OBJECT

public:
    explicit ServiceDialog(QWidget *parent = nullptr);
    ~ServiceDialog() override;

private slots:
    void onRefresh();
    void onServiceSelected(const QModelIndex& index);
    void onStartService();
    void onStopService();
    void onRestartService();
    void onChangeStartType();
    void onFilterChanged();
    void onSearchTextChanged(const QString& text);
    void onServiceStateChanged(const QString& serviceName, ServiceState oldState, ServiceState newState);
    void onOperationFailed(const QString& serviceName, const QString& operation, const QString& error);
    void onOperationSucceeded(const QString& serviceName, const QString& operation);

private:
    void setupUi();
    void setupToolBar();
    void setupServiceTable();
    void setupDetailPanel();
    void setupCrashHistoryTab();
    void updateButtonStates();
    void showStatusMessage(const QString& message, bool isError = false);
    const ServiceInfo* selectedService() const;

    // Monitors
    std::unique_ptr<ServiceMonitor> m_serviceMonitor;
    
    // UI Components
    QSplitter* m_splitter{nullptr};
    QTabWidget* m_tabWidget{nullptr};
    
    // Service table
    QTableView* m_tableView{nullptr};
    ServiceFilterProxy* m_proxyModel{nullptr};
    
    // Filters
    QLineEdit* m_searchEdit{nullptr};
    QComboBox* m_stateFilter{nullptr};
    QComboBox* m_startTypeFilter{nullptr};
    
    // Control buttons
    QPushButton* m_startBtn{nullptr};
    QPushButton* m_stopBtn{nullptr};
    QPushButton* m_restartBtn{nullptr};
    QComboBox* m_startTypeCombo{nullptr};
    QPushButton* m_applyStartTypeBtn{nullptr};
    QPushButton* m_refreshBtn{nullptr};
    
    // Detail panel
    ServiceDetailWidget* m_detailWidget{nullptr};
    
    // Crash history
    CrashHistoryWidget* m_crashHistoryWidget{nullptr};
    
    // Status
    QLabel* m_statusLabel{nullptr};
    QLabel* m_adminLabel{nullptr};
    QLabel* m_countLabel{nullptr};
};
