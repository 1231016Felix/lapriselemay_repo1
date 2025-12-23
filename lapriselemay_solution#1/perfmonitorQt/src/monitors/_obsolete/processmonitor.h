#pragma once

#include <QObject>
#include <QAbstractItemModel>
#include <QSortFilterProxyModel>
#include <QString>
#include <QIcon>
#include <vector>
#include <memory>
#include <unordered_map>

#ifdef _WIN32
#include <Windows.h>
#endif

struct ProcessInfo {
    quint32 pid{0};
    QString name;
    QString displayName;      // Friendly name from exe description
    QString executablePath;
    double cpuUsage{0.0};
    qint64 memoryBytes{0};
    qint64 privateBytes{0};
    QString status;
    QString userName;
    int threadCount{0};
    int handleCount{0};
};

struct AppGroup {
    QString name;              // Application name (key for grouping)
    QString displayName;       // Friendly display name
    QIcon icon;
    std::vector<ProcessInfo> processes;
    
    // Aggregated values
    double totalCpuUsage{0.0};
    qint64 totalMemoryBytes{0};
    int totalThreads{0};
    int processCount{0};
    
    void recalculate() {
        totalCpuUsage = 0.0;
        totalMemoryBytes = 0;
        totalThreads = 0;
        processCount = static_cast<int>(processes.size());
        for (const auto& p : processes) {
            totalCpuUsage += p.cpuUsage;
            totalMemoryBytes += p.memoryBytes;
            totalThreads += p.threadCount;
        }
    }
};

/**
 * @brief Tree model for grouped process display
 * 
 * Level 0: Application groups (aggregated stats)
 * Level 1: Individual processes within each group
 */
class ProcessTreeModel : public QAbstractItemModel
{
    Q_OBJECT

public:
    enum Column {
        ColName = 0,
        ColPID,
        ColCPU,
        ColMemory,
        ColThreads,
        ColStatus,
        ColCount
    };

    explicit ProcessTreeModel(QObject *parent = nullptr);
    
    void setProcesses(const std::vector<ProcessInfo>& processes);
    void updateProcesses(const std::vector<ProcessInfo>& processes);
    void setGrouped(bool grouped);
    bool isGrouped() const { return m_grouped; }
    
    // QAbstractItemModel interface
    QModelIndex index(int row, int column, const QModelIndex &parent = QModelIndex()) const override;
    QModelIndex parent(const QModelIndex &index) const override;
    int rowCount(const QModelIndex &parent = QModelIndex()) const override;
    int columnCount(const QModelIndex &parent = QModelIndex()) const override;
    QVariant data(const QModelIndex &index, int role = Qt::DisplayRole) const override;
    QVariant headerData(int section, Qt::Orientation orientation, int role) const override;
    Qt::ItemFlags flags(const QModelIndex &index) const override;
    
    // For retrieving process info
    ProcessInfo* getProcess(const QModelIndex& index);
    quint32 getPid(const QModelIndex& index) const;
    QModelIndex findIndexByPid(quint32 pid) const;

private:
    void buildGroups();
    QString formatBytes(qint64 bytes) const;
    QIcon getAppIcon(const QString& exePath) const;

    std::vector<ProcessInfo> m_allProcesses;
    std::vector<AppGroup> m_groups;
    bool m_grouped{true};
    
    mutable std::unordered_map<QString, QIcon> m_iconCache;
};

class ProcessSortFilterProxy : public QSortFilterProxyModel
{
    Q_OBJECT
public:
    explicit ProcessSortFilterProxy(QObject* parent = nullptr);
    
    // Find index by PID in the proxy model (handles sorting/filtering)
    QModelIndex findProxyIndexByPid(quint32 pid) const;
    
protected:
    bool lessThan(const QModelIndex &left, const QModelIndex &right) const override;
    bool filterAcceptsRow(int source_row, const QModelIndex &source_parent) const override;
};

class ProcessMonitor : public QObject
{
    Q_OBJECT

public:
    explicit ProcessMonitor(QObject *parent = nullptr);
    ~ProcessMonitor() override;

    void refresh();
    bool terminateProcess(quint32 pid);
    
    [[nodiscard]] QAbstractItemModel* model() { return m_proxyModel.get(); }
    [[nodiscard]] ProcessTreeModel* treeModel() { return m_model.get(); }
    
    void setGrouped(bool grouped);
    bool isGrouped() const;

public slots:
    void setFilter(const QString& filter);

private:
    void queryProcesses();
    QString getProcessDescription(const QString& exePath);

    std::vector<ProcessInfo> m_processes;
    std::unique_ptr<ProcessTreeModel> m_model;
    std::unique_ptr<ProcessSortFilterProxy> m_proxyModel;
    
#ifdef _WIN32
    struct ProcessTimes {
        FILETIME kernelTime;
        FILETIME userTime;
        FILETIME lastCheck;
    };
    std::unordered_map<quint32, ProcessTimes> m_processTimes;
    FILETIME m_lastSystemKernelTime{};
    FILETIME m_lastSystemUserTime{};
#endif
};
