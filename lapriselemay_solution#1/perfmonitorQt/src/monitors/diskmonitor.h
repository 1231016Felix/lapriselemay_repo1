#pragma once

#include <QObject>
#include <QAbstractTableModel>
#include <QString>
#include <vector>
#include <memory>

struct DiskInfo {
    QString name;
    QString label;
    QString fileSystem;
    qint64 totalBytes{0};
    qint64 freeBytes{0};
    qint64 usedBytes{0};
    double usagePercent{0.0};
};

struct DiskActivity {
    qint64 readBytesPerSec{0};
    qint64 writeBytesPerSec{0};
    double readTime{0.0};
    double writeTime{0.0};
};

class DiskTableModel : public QAbstractTableModel
{
    Q_OBJECT

public:
    explicit DiskTableModel(QObject *parent = nullptr);
    
    void setDisks(const std::vector<DiskInfo>& disks);
    
    int rowCount(const QModelIndex &parent = QModelIndex()) const override;
    int columnCount(const QModelIndex &parent = QModelIndex()) const override;
    QVariant data(const QModelIndex &index, int role = Qt::DisplayRole) const override;
    QVariant headerData(int section, Qt::Orientation orientation, int role) const override;

private:
    std::vector<DiskInfo> m_disks;
};

class DiskMonitor : public QObject
{
    Q_OBJECT

public:
    explicit DiskMonitor(QObject *parent = nullptr);
    ~DiskMonitor() override;

    void update();
    [[nodiscard]] QAbstractTableModel* model() { return m_model.get(); }
    [[nodiscard]] const DiskActivity& activity() const { return m_activity; }

private:
    void queryDisks();
    void queryActivity();

    std::vector<DiskInfo> m_disks;
    DiskActivity m_activity;
    std::unique_ptr<DiskTableModel> m_model;
    
#ifdef _WIN32
    qint64 m_prevReadBytes{0};
    qint64 m_prevWriteBytes{0};
    qint64 m_prevTime{0};
#endif
};
