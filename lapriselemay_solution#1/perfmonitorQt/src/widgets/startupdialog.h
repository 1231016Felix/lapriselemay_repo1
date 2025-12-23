#pragma once

#include <QDialog>
#include <QTableView>
#include <QTreeView>
#include <QLabel>
#include <QPushButton>
#include <QLineEdit>
#include <QComboBox>
#include <QGroupBox>
#include <QSortFilterProxyModel>
#include <QTabWidget>
#include <memory>

class StartupMonitor;
struct StartupEntry;

/**
 * @brief Filter proxy for startup entries
 */
class StartupFilterProxy : public QSortFilterProxyModel
{
    Q_OBJECT

public:
    explicit StartupFilterProxy(QObject* parent = nullptr);
    
    void setSourceFilter(int source); // -1 = all
    void setStatusFilter(int status); // -1 = all, 0 = disabled, 1 = enabled
    
protected:
    bool filterAcceptsRow(int source_row, const QModelIndex &source_parent) const override;
    bool lessThan(const QModelIndex &left, const QModelIndex &right) const override;

private:
    int m_sourceFilter{-1};
    int m_statusFilter{-1};
};

/**
 * @brief Dialog for managing startup programs
 */
class StartupDialog : public QDialog
{
    Q_OBJECT

public:
    explicit StartupDialog(QWidget *parent = nullptr);
    ~StartupDialog() override;

private slots:
    void onRefresh();
    void onSelectionChanged();
    void onToggleEnabled();
    void onDelete();
    void onOpenLocation();
    void onOpenFileLocation();
    void onAddEntry();
    void onExport();
    void onFilterChanged();
    void onError(const QString& error);
    void showContextMenu(const QPoint& pos);

private:
    void setupUi();
    void createToolbar();
    void createFilters();
    void createTable();
    void createDetailsPanel();
    void createStatsBar();
    void updateStats();
    void updateDetailsPanel(const StartupEntry* entry);
    void applyFilters();

    std::unique_ptr<StartupMonitor> m_monitor;
    std::unique_ptr<StartupFilterProxy> m_proxyModel;
    
    // UI Components
    QTabWidget* m_tabWidget{nullptr};
    QTableView* m_tableView{nullptr};
    
    // Filters
    QLineEdit* m_searchEdit{nullptr};
    QComboBox* m_sourceCombo{nullptr};
    QComboBox* m_statusCombo{nullptr};
    
    // Toolbar buttons
    QPushButton* m_refreshBtn{nullptr};
    QPushButton* m_enableBtn{nullptr};
    QPushButton* m_disableBtn{nullptr};
    QPushButton* m_deleteBtn{nullptr};
    QPushButton* m_openLocationBtn{nullptr};
    QPushButton* m_openFileBtn{nullptr};
    QPushButton* m_addBtn{nullptr};
    QPushButton* m_exportBtn{nullptr};
    
    // Details panel
    QGroupBox* m_detailsGroup{nullptr};
    QLabel* m_detailNameLabel{nullptr};
    QLabel* m_detailPublisherLabel{nullptr};
    QLabel* m_detailCommandLabel{nullptr};
    QLabel* m_detailPathLabel{nullptr};
    QLabel* m_detailSourceLabel{nullptr};
    QLabel* m_detailImpactLabel{nullptr};
    QLabel* m_detailStatusLabel{nullptr};
    QLabel* m_detailVersionLabel{nullptr};
    
    // Stats bar
    QLabel* m_totalLabel{nullptr};
    QLabel* m_enabledLabel{nullptr};
    QLabel* m_disabledLabel{nullptr};
    QLabel* m_highImpactLabel{nullptr};
};

/**
 * @brief Dialog for adding a new startup entry
 */
class AddStartupDialog : public QDialog
{
    Q_OBJECT

public:
    explicit AddStartupDialog(QWidget *parent = nullptr);
    
    QString name() const;
    QString command() const;
    int source() const;

private slots:
    void onBrowse();
    void validate();

private:
    QLineEdit* m_nameEdit{nullptr};
    QLineEdit* m_commandEdit{nullptr};
    QComboBox* m_sourceCombo{nullptr};
    QPushButton* m_okBtn{nullptr};
};
