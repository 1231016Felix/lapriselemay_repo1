#pragma once

#include <QObject>
#include <QAbstractTableModel>
#include <QString>
#include <vector>
#include <memory>

struct NetworkAdapterInfo {
    QString name;
    QString description;
    QString macAddress;
    QString ipv4Address;
    QString ipv6Address;
    qint64 speed{0};  // bits per second
    bool isConnected{false};
    qint64 bytesSent{0};
    qint64 bytesReceived{0};
};

struct NetworkActivity {
    qint64 sentBytesPerSec{0};
    qint64 receivedBytesPerSec{0};
    qint64 totalSent{0};
    qint64 totalReceived{0};
};

class NetworkTableModel : public QAbstractTableModel
{
    Q_OBJECT

public:
    explicit NetworkTableModel(QObject *parent = nullptr);
    
    void setAdapters(const std::vector<NetworkAdapterInfo>& adapters);
    
    int rowCount(const QModelIndex &parent = QModelIndex()) const override;
    int columnCount(const QModelIndex &parent = QModelIndex()) const override;
    QVariant data(const QModelIndex &index, int role = Qt::DisplayRole) const override;
    QVariant headerData(int section, Qt::Orientation orientation, int role) const override;

private:
    std::vector<NetworkAdapterInfo> m_adapters;
};

class NetworkMonitor : public QObject
{
    Q_OBJECT

public:
    explicit NetworkMonitor(QObject *parent = nullptr);
    ~NetworkMonitor() override;

    void update();
    [[nodiscard]] QAbstractTableModel* model() { return m_model.get(); }
    [[nodiscard]] const NetworkActivity& activity() const { return m_activity; }

private:
    void queryAdapters();
    void queryActivity();
    QString formatSpeed(qint64 bitsPerSecond);

    std::vector<NetworkAdapterInfo> m_adapters;
    NetworkActivity m_activity;
    std::unique_ptr<NetworkTableModel> m_model;
    
    qint64 m_prevSentBytes{0};
    qint64 m_prevReceivedBytes{0};
    qint64 m_prevTime{0};
};
