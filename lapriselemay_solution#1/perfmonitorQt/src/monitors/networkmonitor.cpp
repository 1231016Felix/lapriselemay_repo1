#include "networkmonitor.h"

#include <QColor>
#include <QDateTime>

#ifdef _WIN32
#include <WinSock2.h>
#include <WS2tcpip.h>
#include <iphlpapi.h>
#include <netioapi.h>
#pragma comment(lib, "iphlpapi.lib")
#pragma comment(lib, "ws2_32.lib")
#endif

// NetworkTableModel implementation
NetworkTableModel::NetworkTableModel(QObject *parent)
    : QAbstractTableModel(parent)
{
}

void NetworkTableModel::setAdapters(const std::vector<NetworkAdapterInfo>& adapters)
{
    beginResetModel();
    m_adapters = adapters;
    endResetModel();
}

int NetworkTableModel::rowCount(const QModelIndex&) const
{
    return static_cast<int>(m_adapters.size());
}

int NetworkTableModel::columnCount(const QModelIndex&) const
{
    return 5;
}

QVariant NetworkTableModel::data(const QModelIndex &index, int role) const
{
    if (!index.isValid() || index.row() >= static_cast<int>(m_adapters.size()))
        return QVariant();

    const auto& adapter = m_adapters[index.row()];
    
    if (role == Qt::DisplayRole) {
        switch (index.column()) {
            case 0: return adapter.description;
            case 1: return adapter.ipv4Address.isEmpty() ? "N/A" : adapter.ipv4Address;
            case 2: return adapter.macAddress;
            case 3: {
                if (adapter.speed >= 1000000000)
                    return QString("%1 Gbps").arg(adapter.speed / 1000000000.0, 0, 'f', 1);
                else if (adapter.speed >= 1000000)
                    return QString("%1 Mbps").arg(adapter.speed / 1000000.0, 0, 'f', 0);
                else
                    return QString("%1 Kbps").arg(adapter.speed / 1000.0, 0, 'f', 0);
            }
            case 4: return adapter.isConnected ? tr("Connected") : tr("Disconnected");
        }
    }
    else if (role == Qt::ForegroundRole && index.column() == 4) {
        return adapter.isConnected ? QColor(0, 170, 0) : QColor(170, 0, 0);
    }
    
    return QVariant();
}

QVariant NetworkTableModel::headerData(int section, Qt::Orientation orientation, int role) const
{
    if (orientation != Qt::Horizontal || role != Qt::DisplayRole)
        return QVariant();

    switch (section) {
        case 0: return tr("Adapter");
        case 1: return tr("IPv4 Address");
        case 2: return tr("MAC Address");
        case 3: return tr("Speed");
        case 4: return tr("Status");
    }
    return QVariant();
}

// NetworkMonitor implementation
NetworkMonitor::NetworkMonitor(QObject *parent)
    : QObject(parent)
    , m_model(std::make_unique<NetworkTableModel>())
{
#ifdef _WIN32
    WSADATA wsaData;
    WSAStartup(MAKEWORD(2, 2), &wsaData);
#endif
    
    m_prevTime = QDateTime::currentMSecsSinceEpoch();
    update();
}

NetworkMonitor::~NetworkMonitor()
{
#ifdef _WIN32
    WSACleanup();
#endif
}

void NetworkMonitor::update()
{
    queryAdapters();
    queryActivity();
    m_model->setAdapters(m_adapters);
}

void NetworkMonitor::queryAdapters()
{
    m_adapters.clear();
    
#ifdef _WIN32
    ULONG outBufLen = 0;
    ULONG flags = GAA_FLAG_INCLUDE_PREFIX | GAA_FLAG_SKIP_ANYCAST | GAA_FLAG_SKIP_MULTICAST;
    
    if (GetAdaptersAddresses(AF_UNSPEC, flags, nullptr, nullptr, &outBufLen) != ERROR_BUFFER_OVERFLOW) {
        return;
    }
    
    std::vector<BYTE> buffer(outBufLen);
    auto pAddresses = reinterpret_cast<PIP_ADAPTER_ADDRESSES>(buffer.data());
    
    if (GetAdaptersAddresses(AF_UNSPEC, flags, nullptr, pAddresses, &outBufLen) != NO_ERROR) {
        return;
    }
    
    for (auto pCurr = pAddresses; pCurr; pCurr = pCurr->Next) {
        if (pCurr->IfType == IF_TYPE_SOFTWARE_LOOPBACK ||
            pCurr->IfType == IF_TYPE_TUNNEL) {
            continue;
        }
        
        NetworkAdapterInfo adapter;
        adapter.name = QString::fromWCharArray(pCurr->FriendlyName);
        adapter.description = QString::fromWCharArray(pCurr->Description);
        adapter.speed = pCurr->TransmitLinkSpeed;
        adapter.isConnected = (pCurr->OperStatus == IfOperStatusUp);
        
        if (pCurr->PhysicalAddressLength > 0) {
            QStringList macParts;
            for (UINT i = 0; i < pCurr->PhysicalAddressLength; ++i) {
                macParts << QString("%1").arg(pCurr->PhysicalAddress[i], 2, 16, QChar('0')).toUpper();
            }
            adapter.macAddress = macParts.join(":");
        }
        
        for (auto pUnicast = pCurr->FirstUnicastAddress; pUnicast; pUnicast = pUnicast->Next) {
            if (pUnicast->Address.lpSockaddr->sa_family == AF_INET) {
                auto addr = reinterpret_cast<sockaddr_in*>(pUnicast->Address.lpSockaddr);
                char ipStr[INET_ADDRSTRLEN] = {0};
                inet_ntop(AF_INET, &addr->sin_addr, ipStr, INET_ADDRSTRLEN);
                adapter.ipv4Address = QString::fromLatin1(ipStr);
            }
            else if (pUnicast->Address.lpSockaddr->sa_family == AF_INET6) {
                if (adapter.ipv6Address.isEmpty()) {
                    auto addr = reinterpret_cast<sockaddr_in6*>(pUnicast->Address.lpSockaddr);
                    char ipStr[INET6_ADDRSTRLEN] = {0};
                    inet_ntop(AF_INET6, &addr->sin6_addr, ipStr, INET6_ADDRSTRLEN);
                    adapter.ipv6Address = QString::fromLatin1(ipStr);
                }
            }
        }
        
        m_adapters.push_back(adapter);
    }
#endif
}

void NetworkMonitor::queryActivity()
{
#ifdef _WIN32
    qint64 currentTime = QDateTime::currentMSecsSinceEpoch();
    qint64 totalSent = 0;
    qint64 totalReceived = 0;
    
    MIB_IF_TABLE2* pTable = nullptr;
    if (GetIfTable2(&pTable) == NO_ERROR) {
        for (ULONG i = 0; i < pTable->NumEntries; ++i) {
            const auto& row = pTable->Table[i];
            
            if (row.Type == IF_TYPE_SOFTWARE_LOOPBACK)
                continue;
                
            totalSent += row.OutOctets;
            totalReceived += row.InOctets;
        }
        FreeMibTable(pTable);
    }
    
    qint64 timeDiff = currentTime - m_prevTime;
    if (timeDiff > 0 && m_prevTime > 0) {
        qint64 sentDiff = totalSent - m_prevSentBytes;
        qint64 receivedDiff = totalReceived - m_prevReceivedBytes;
        
        if (sentDiff >= 0 && receivedDiff >= 0) {
            m_activity.sentBytesPerSec = (sentDiff * 1000) / timeDiff;
            m_activity.receivedBytesPerSec = (receivedDiff * 1000) / timeDiff;
        }
    }
    
    m_activity.totalSent = totalSent;
    m_activity.totalReceived = totalReceived;
    m_prevSentBytes = totalSent;
    m_prevReceivedBytes = totalReceived;
    m_prevTime = currentTime;
#endif
}

QString NetworkMonitor::formatSpeed(qint64 bitsPerSecond)
{
    if (bitsPerSecond >= 1000000000) {
        return QString("%1 Gbps").arg(bitsPerSecond / 1000000000.0, 0, 'f', 1);
    } else if (bitsPerSecond >= 1000000) {
        return QString("%1 Mbps").arg(bitsPerSecond / 1000000.0, 0, 'f', 0);
    } else if (bitsPerSecond >= 1000) {
        return QString("%1 Kbps").arg(bitsPerSecond / 1000.0, 0, 'f', 0);
    }
    return QString("%1 bps").arg(bitsPerSecond);
}
