#include "diskmonitor.h"

#ifdef _WIN32
#include <Windows.h>
#include <winioctl.h>
#endif

#include <QDateTime>

// DiskTableModel implementation
DiskTableModel::DiskTableModel(QObject *parent)
    : QAbstractTableModel(parent)
{
}

void DiskTableModel::setDisks(const std::vector<DiskInfo>& disks)
{
    beginResetModel();
    m_disks = disks;
    endResetModel();
}

int DiskTableModel::rowCount(const QModelIndex&) const
{
    return static_cast<int>(m_disks.size());
}

int DiskTableModel::columnCount(const QModelIndex&) const
{
    return 5;
}

QVariant DiskTableModel::data(const QModelIndex &index, int role) const
{
    if (!index.isValid() || index.row() >= static_cast<int>(m_disks.size()))
        return QVariant();

    const auto& disk = m_disks[index.row()];
    
    if (role == Qt::DisplayRole) {
        switch (index.column()) {
            case 0: return QString("%1 (%2)").arg(disk.name, disk.label);
            case 1: return disk.fileSystem;
            case 2: {
                double gb = disk.totalBytes / (1024.0 * 1024.0 * 1024.0);
                return QString("%1 GB").arg(gb, 0, 'f', 1);
            }
            case 3: {
                double gb = disk.freeBytes / (1024.0 * 1024.0 * 1024.0);
                return QString("%1 GB").arg(gb, 0, 'f', 1);
            }
            case 4: return QString("%1%").arg(disk.usagePercent, 0, 'f', 1);
        }
    }
    else if (role == Qt::TextAlignmentRole) {
        if (index.column() >= 2)
            return Qt::AlignRight;
    }
    
    return QVariant();
}

QVariant DiskTableModel::headerData(int section, Qt::Orientation orientation, int role) const
{
    if (orientation != Qt::Horizontal || role != Qt::DisplayRole)
        return QVariant();

    switch (section) {
        case 0: return tr("Drive");
        case 1: return tr("File System");
        case 2: return tr("Total Size");
        case 3: return tr("Free Space");
        case 4: return tr("Usage");
    }
    return QVariant();
}

// DiskMonitor implementation
DiskMonitor::DiskMonitor(QObject *parent)
    : QObject(parent)
    , m_model(std::make_unique<DiskTableModel>())
{
#ifdef _WIN32
    m_prevTime = QDateTime::currentMSecsSinceEpoch();
#endif
    update();
}

DiskMonitor::~DiskMonitor() = default;

void DiskMonitor::update()
{
    queryDisks();
    queryActivity();
    m_model->setDisks(m_disks);
}

void DiskMonitor::queryDisks()
{
    m_disks.clear();
    
#ifdef _WIN32
    DWORD drives = GetLogicalDrives();
    
    for (int i = 0; i < 26; ++i) {
        if (!(drives & (1 << i)))
            continue;
            
        wchar_t rootPath[4] = { static_cast<wchar_t>('A' + i), ':', '\\', 0 };
        
        UINT driveType = GetDriveType(rootPath);
        if (driveType != DRIVE_FIXED && driveType != DRIVE_REMOVABLE)
            continue;
        
        DiskInfo disk;
        disk.name = QString(QChar('A' + i)) + ":";
        disk.driveLetter = disk.name;  // Set drive letter (e.g., "C:")
        
        // Get volume information
        wchar_t volumeName[MAX_PATH + 1] = {0};
        wchar_t fsName[MAX_PATH + 1] = {0};
        
        if (GetVolumeInformation(rootPath, volumeName, MAX_PATH,
            nullptr, nullptr, nullptr, fsName, MAX_PATH)) {
            disk.label = QString::fromWCharArray(volumeName);
            disk.fileSystem = QString::fromWCharArray(fsName);
        }
        
        if (disk.label.isEmpty()) {
            disk.label = (driveType == DRIVE_REMOVABLE) ? "Removable" : "Local Disk";
        }
        
        // Get disk space
        ULARGE_INTEGER totalBytes, freeBytes;
        if (GetDiskFreeSpaceEx(rootPath, nullptr, &totalBytes, &freeBytes)) {
            disk.totalBytes = totalBytes.QuadPart;
            disk.freeBytes = freeBytes.QuadPart;
            disk.usedBytes = disk.totalBytes - disk.freeBytes;
            
            if (disk.totalBytes > 0) {
                disk.usagePercent = (disk.usedBytes * 100.0) / disk.totalBytes;
            }
        }
        
        m_disks.push_back(disk);
    }
#endif
}

void DiskMonitor::queryActivity()
{
#ifdef _WIN32
    qint64 currentTime = QDateTime::currentMSecsSinceEpoch();
    qint64 totalReadBytes = 0;
    qint64 totalWriteBytes = 0;
    
    // Query disk performance for each physical disk
    for (int diskNum = 0; diskNum < 16; ++diskNum) {
        QString path = QString("\\\\.\\PhysicalDrive%1").arg(diskNum);
        
        HANDLE hDisk = CreateFile(
            reinterpret_cast<LPCWSTR>(path.utf16()),
            0,  // No access needed for query
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr,
            OPEN_EXISTING,
            0,
            nullptr
        );
        
        if (hDisk == INVALID_HANDLE_VALUE)
            continue;
        
        DISK_PERFORMANCE diskPerf;
        DWORD bytesReturned;
        
        if (DeviceIoControl(
            hDisk,
            IOCTL_DISK_PERFORMANCE,
            nullptr, 0,
            &diskPerf, sizeof(diskPerf),
            &bytesReturned,
            nullptr
        )) {
            totalReadBytes += diskPerf.BytesRead.QuadPart;
            totalWriteBytes += diskPerf.BytesWritten.QuadPart;
        }
        
        CloseHandle(hDisk);
    }
    
    // Calculate rates
    qint64 timeDiff = currentTime - m_prevTime;
    if (timeDiff > 0 && m_prevTime > 0) {
        qint64 readDiff = totalReadBytes - m_prevReadBytes;
        qint64 writeDiff = totalWriteBytes - m_prevWriteBytes;
        
        if (readDiff >= 0 && writeDiff >= 0) {
            m_activity.readBytesPerSec = (readDiff * 1000) / timeDiff;
            m_activity.writeBytesPerSec = (writeDiff * 1000) / timeDiff;
        }
    }
    
    m_prevReadBytes = totalReadBytes;
    m_prevWriteBytes = totalWriteBytes;
    m_prevTime = currentTime;
#endif
}
