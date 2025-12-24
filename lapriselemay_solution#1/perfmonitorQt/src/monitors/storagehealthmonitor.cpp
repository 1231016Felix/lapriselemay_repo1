#include "storagehealthmonitor.h"

#include <QDebug>
#include <algorithm>
#include <set>

#ifdef _WIN32
#include <Windows.h>
#include <winioctl.h>
#include <ntddscsi.h>
#include <ntddstor.h>

#pragma comment(lib, "setupapi.lib")

// Bus type definitions (if not available in older SDK)
#ifndef BusTypeNvme
#define BusTypeNvme 17
#endif

// S.M.A.R.T. command constants
#define SMART_CYL_LOW               0x4F
#define SMART_CYL_HI                0xC2
#define IDE_EXECUTE_SMART_FUNCTION  0xB0
#define SMART_READ_DATA             0xD0
#define SMART_READ_THRESHOLDS       0xD1
#define SMART_RETURN_STATUS         0xDA

// NVMe constants
#define NVME_HEALTH_INFO_LOG        0x02

// SMART data structures
#pragma pack(push, 1)
struct SMART_ATTRIBUTE_ENTRY {
    BYTE  Id;
    WORD  Flags;
    BYTE  CurrentValue;
    BYTE  WorstValue;
    BYTE  RawValue[6];
    BYTE  Reserved;
};

struct SMART_THRESHOLD_ENTRY {
    BYTE  Id;
    BYTE  ThresholdValue;
    BYTE  Reserved[10];
};

struct SMART_DATA_BLOCK {
    WORD  Revision;
    SMART_ATTRIBUTE_ENTRY Attributes[30];
    BYTE  Reserved[149];
    BYTE  Checksum;
};

struct SMART_THRESHOLDS_BLOCK {
    WORD  Revision;
    SMART_THRESHOLD_ENTRY Thresholds[30];
    BYTE  Reserved[149];
    BYTE  Checksum;
};

struct SENDCMDINPARAMS_BLOCK {
    DWORD cBufferSize;
    IDEREGS irDriveRegs;
    BYTE bDriveNumber;
    BYTE bReserved[3];
    DWORD dwReserved[4];
    BYTE bBuffer[1];
};

struct SENDCMDOUTPARAMS_BLOCK {
    DWORD cBufferSize;
    DRIVERSTATUS DriverStatus;
    BYTE bBuffer[1];
};

struct NVME_HEALTH_INFO_BLOCK {
    BYTE CriticalWarning;
    WORD CompositeTemperature;
    BYTE AvailableSpare;
    BYTE AvailableSpareThreshold;
    BYTE PercentageUsed;
    BYTE Reserved1[26];
    BYTE DataUnitsRead[16];
    BYTE DataUnitsWritten[16];
    BYTE HostReadCommands[16];
    BYTE HostWriteCommands[16];
    BYTE ControllerBusyTime[16];
    BYTE PowerCycles[16];
    BYTE PowerOnHours[16];
    BYTE UnsafeShutdowns[16];
    BYTE MediaErrors[16];
    BYTE NumberOfErrorLogEntries[16];
    DWORD WarningCompositeTemperatureTime;
    DWORD CriticalCompositeTemperatureTime;
    WORD TemperatureSensor[8];
    BYTE Reserved2[296];
};

struct DEVICE_SEEK_PENALTY_DESC {
    DWORD Version;
    DWORD Size;
    BOOLEAN IncursSeekPenalty;
};
#pragma pack(pop)

#endif // _WIN32

// ============================================================================
// SmartAttributeModel Implementation
// ============================================================================

SmartAttributeModel::SmartAttributeModel(QObject *parent)
    : QAbstractTableModel(parent)
{
}

void SmartAttributeModel::setAttributes(const std::vector<SmartAttributeData>& attributes)
{
    beginResetModel();
    m_attributes = attributes;
    endResetModel();
}

void SmartAttributeModel::clear()
{
    beginResetModel();
    m_attributes.clear();
    endResetModel();
}

int SmartAttributeModel::rowCount(const QModelIndex &parent) const
{
    if (parent.isValid()) return 0;
    return static_cast<int>(m_attributes.size());
}

int SmartAttributeModel::columnCount(const QModelIndex &parent) const
{
    if (parent.isValid()) return 0;
    return 6;
}

QVariant SmartAttributeModel::data(const QModelIndex &index, int role) const
{
    if (!index.isValid() || index.row() >= static_cast<int>(m_attributes.size()))
        return QVariant();
    
    const auto& attr = m_attributes[index.row()];
    
    if (role == Qt::DisplayRole) {
        switch (index.column()) {
            case 0: return QString("0x%1").arg(attr.id, 2, 16, QChar('0')).toUpper();
            case 1: return attr.name;
            case 2: return attr.currentValue;
            case 3: return attr.worstValue;
            case 4: return attr.threshold;
            case 5: return attr.rawValueString;
        }
    }
    else if (role == Qt::ForegroundRole) {
        if (!attr.isOk || attr.currentValue <= attr.threshold) {
            return QColor(Qt::red);
        }
        if (attr.isCritical && attr.rawValue > 0) {
            return QColor(255, 165, 0);
        }
    }
    else if (role == Qt::BackgroundRole) {
        if (!attr.isOk) {
            return QColor(255, 200, 200);
        }
        if (attr.isCritical && attr.rawValue > 0) {
            return QColor(255, 240, 200);
        }
    }
    
    return QVariant();
}

QVariant SmartAttributeModel::headerData(int section, Qt::Orientation orientation, int role) const
{
    if (orientation != Qt::Horizontal || role != Qt::DisplayRole)
        return QVariant();
    
    switch (section) {
        case 0: return tr("ID");
        case 1: return tr("Attribute");
        case 2: return tr("Current");
        case 3: return tr("Worst");
        case 4: return tr("Threshold");
        case 5: return tr("Raw Value");
    }
    return QVariant();
}

// ============================================================================
// StorageHealthMonitor Implementation
// ============================================================================

StorageHealthMonitor::StorageHealthMonitor(QObject *parent)
    : QObject(parent)
{
    m_isAdmin = isAdmin();
}

StorageHealthMonitor::~StorageHealthMonitor() = default;

bool StorageHealthMonitor::isAdmin()
{
#ifdef _WIN32
    BOOL isAdminResult = FALSE;
    PSID adminGroup = nullptr;
    SID_IDENTIFIER_AUTHORITY ntAuthority = SECURITY_NT_AUTHORITY;
    
    if (AllocateAndInitializeSid(&ntAuthority, 2,
            SECURITY_BUILTIN_DOMAIN_RID, DOMAIN_ALIAS_RID_ADMINS,
            0, 0, 0, 0, 0, 0, &adminGroup)) {
        CheckTokenMembership(nullptr, adminGroup, &isAdminResult);
        FreeSid(adminGroup);
    }
    return isAdminResult == TRUE;
#else
    return false;
#endif
}

QString StorageHealthMonitor::formatBytes(uint64_t bytes)
{
    const char* units[] = {"B", "KB", "MB", "GB", "TB", "PB"};
    int unitIndex = 0;
    double size = static_cast<double>(bytes);
    
    while (size >= 1024.0 && unitIndex < 5) {
        size /= 1024.0;
        unitIndex++;
    }
    
    return QString("%1 %2").arg(size, 0, 'f', unitIndex > 0 ? 1 : 0).arg(units[unitIndex]);
}

QString StorageHealthMonitor::getAttributeName(uint8_t id)
{
    static const std::map<uint8_t, QString> names = {
        {0x01, "Read Error Rate"},
        {0x03, "Spin-Up Time"},
        {0x04, "Start/Stop Count"},
        {0x05, "Reallocated Sectors Count"},
        {0x07, "Seek Error Rate"},
        {0x09, "Power-On Hours"},
        {0x0A, "Spin Retry Count"},
        {0x0C, "Power Cycle Count"},
        {0xAD, "SSD Wear Leveling Count"},
        {0xAE, "Unexpected Power Loss"},
        {0xBB, "Reported Uncorrectable Errors"},
        {0xBE, "Airflow Temperature"},
        {0xC2, "Temperature"},
        {0xC4, "Reallocation Event Count"},
        {0xC5, "Current Pending Sector Count"},
        {0xC6, "Uncorrectable Sector Count"},
        {0xC7, "UltraDMA CRC Error Count"},
        {0xE7, "SSD Life Left"},
        {0xE8, "Available Reserved Space"},
        {0xE9, "Media Wearout Indicator"},
        {0xF1, "Total LBAs Written"},
        {0xF2, "Total LBAs Read"},
    };
    
    auto it = names.find(id);
    if (it != names.end()) {
        return it->second;
    }
    return QString("Unknown (0x%1)").arg(id, 2, 16, QChar('0')).toUpper();
}

bool StorageHealthMonitor::isAttributeCritical(uint8_t id)
{
    static const std::set<uint8_t> critical = {
        0x05, 0x0A, 0xC4, 0xC5, 0xC6, 0xC8, 0xBB, 0x07, 0xAB, 0xAC,
    };
    return critical.count(id) > 0;
}

QString StorageHealthMonitor::healthStatusToString(DriveHealthStatus status)
{
    switch (status) {
        case DriveHealthStatus::Excellent: return tr("Excellent");
        case DriveHealthStatus::Good: return tr("Good");
        case DriveHealthStatus::Fair: return tr("Fair");
        case DriveHealthStatus::Poor: return tr("Poor");
        case DriveHealthStatus::Critical: return tr("Critical");
        default: return tr("Unknown");
    }
}

QString StorageHealthMonitor::healthStatusColor(DriveHealthStatus status)
{
    switch (status) {
        case DriveHealthStatus::Excellent: return "#00aa00";
        case DriveHealthStatus::Good: return "#88cc00";
        case DriveHealthStatus::Fair: return "#ffaa00";
        case DriveHealthStatus::Poor: return "#ff6600";
        case DriveHealthStatus::Critical: return "#ff0000";
        default: return "#888888";
    }
}

void StorageHealthMonitor::update()
{
    m_previousState.clear();
    for (const auto& disk : m_disks) {
        m_previousState[disk.devicePath] = disk;
    }
    
    m_disks.clear();
    enumerateDisks();
    
    qDebug() << "StorageHealthMonitor: Found" << m_disks.size() << "disks";
    
    for (auto& disk : m_disks) {
        qDebug() << "Processing disk:" << disk.model << "NVMe:" << disk.isNvme;
        if (disk.isNvme) {
            readNvmeHealth(disk);
        } else {
            readSmartData(disk);
        }
        qDebug() << "SMART attributes count:" << disk.smartAttributes.size();
        calculateHealthStatus(disk);
        checkAlerts(disk);
        disk.lastUpdated = QDateTime::currentDateTime();
    }
    
    emit updated();
}

const DiskHealthInfo* StorageHealthMonitor::getDiskInfo(const QString& devicePath) const
{
    for (const auto& disk : m_disks) {
        if (disk.devicePath == devicePath) {
            return &disk;
        }
    }
    return nullptr;
}

void StorageHealthMonitor::enumerateDisks()
{
#ifdef _WIN32
    for (int driveNum = 0; driveNum < 16; driveNum++) {
        QString devicePath = QString("\\\\.\\PhysicalDrive%1").arg(driveNum);
        
        HANDLE hDevice = CreateFileW(
            reinterpret_cast<LPCWSTR>(devicePath.utf16()),
            GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr,
            OPEN_EXISTING,
            0,
            nullptr
        );
        
        if (hDevice == INVALID_HANDLE_VALUE) {
            // Try with read-only access (non-admin fallback)
            hDevice = CreateFileW(
                reinterpret_cast<LPCWSTR>(devicePath.utf16()),
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                nullptr,
                OPEN_EXISTING,
                0,
                nullptr
            );
            
            if (hDevice == INVALID_HANDLE_VALUE) {
                // Try with minimum access for enumeration only
                hDevice = CreateFileW(
                    reinterpret_cast<LPCWSTR>(devicePath.utf16()),
                    0,  // No access rights, just query
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    nullptr,
                    OPEN_EXISTING,
                    0,
                    nullptr
                );
                
                if (hDevice == INVALID_HANDLE_VALUE) {
                    continue;
                }
            }
        }
        
        DiskHealthInfo disk;
        disk.devicePath = devicePath;
        
        STORAGE_PROPERTY_QUERY query;
        memset(&query, 0, sizeof(query));
        query.PropertyId = StorageDeviceProperty;
        query.QueryType = PropertyStandardQuery;
        
        char buffer[1024];
        memset(buffer, 0, sizeof(buffer));
        DWORD bytesReturned = 0;
        
        if (DeviceIoControl(hDevice, IOCTL_STORAGE_QUERY_PROPERTY,
                &query, sizeof(query), buffer, sizeof(buffer), &bytesReturned, nullptr)) {
            
            STORAGE_DEVICE_DESCRIPTOR* descriptor = reinterpret_cast<STORAGE_DEVICE_DESCRIPTOR*>(buffer);
            
            if (descriptor->ProductIdOffset) {
                disk.model = QString::fromLatin1(buffer + descriptor->ProductIdOffset).trimmed();
            }
            if (descriptor->SerialNumberOffset) {
                disk.serialNumber = QString::fromLatin1(buffer + descriptor->SerialNumberOffset).trimmed();
            }
            if (descriptor->ProductRevisionOffset) {
                disk.firmwareVersion = QString::fromLatin1(buffer + descriptor->ProductRevisionOffset).trimmed();
            }
            
            disk.isRemovable = descriptor->RemovableMedia;
            
            STORAGE_BUS_TYPE busType = descriptor->BusType;
            if (busType == BusTypeNvme) {
                disk.interfaceType = "NVMe";
                disk.isNvme = true;
                disk.isSsd = true;
            } else if (busType == BusTypeSata) {
                disk.interfaceType = "SATA";
            } else if (busType == BusTypeUsb) {
                disk.interfaceType = "USB";
            } else if (busType == BusTypeScsi) {
                disk.interfaceType = "SCSI";
            } else if (busType == BusTypeAta) {
                disk.interfaceType = "ATA";
            } else {
                disk.interfaceType = QString("Other (%1)").arg(static_cast<int>(busType));
            }
        }
        
        DISK_GEOMETRY_EX geometry;
        memset(&geometry, 0, sizeof(geometry));
        if (DeviceIoControl(hDevice, IOCTL_DISK_GET_DRIVE_GEOMETRY_EX,
                nullptr, 0, &geometry, sizeof(geometry), &bytesReturned, nullptr)) {
            disk.totalBytes = geometry.DiskSize.QuadPart;
            disk.totalFormatted = formatBytes(disk.totalBytes);
        }
        
        if (!disk.isNvme) {
            STORAGE_PROPERTY_QUERY seekQuery;
            memset(&seekQuery, 0, sizeof(seekQuery));
            seekQuery.PropertyId = StorageDeviceSeekPenaltyProperty;
            seekQuery.QueryType = PropertyStandardQuery;
            
            DEVICE_SEEK_PENALTY_DESC seekDescriptor;
            memset(&seekDescriptor, 0, sizeof(seekDescriptor));
            if (DeviceIoControl(hDevice, IOCTL_STORAGE_QUERY_PROPERTY,
                    &seekQuery, sizeof(seekQuery), &seekDescriptor, sizeof(seekDescriptor), 
                    &bytesReturned, nullptr)) {
                disk.isSsd = !seekDescriptor.IncursSeekPenalty;
            }
        }
        
        if (!disk.isNvme && !disk.isRemovable) {
            GETVERSIONINPARAMS versionParams;
            memset(&versionParams, 0, sizeof(versionParams));
            if (DeviceIoControl(hDevice, SMART_GET_VERSION,
                    nullptr, 0, &versionParams, sizeof(versionParams), &bytesReturned, nullptr)) {
                disk.smartSupported = (versionParams.fCapabilities & CAP_SMART_CMD) != 0;
                disk.smartEnabled = true;
            }
        } else if (disk.isNvme) {
            disk.smartSupported = true;
            disk.smartEnabled = true;
        }
        
        CloseHandle(hDevice);
        
        qDebug() << "Found disk:" << disk.model << "Interface:" << disk.interfaceType 
                 << "Removable:" << disk.isRemovable << "NVMe:" << disk.isNvme;
        
        if (!disk.isRemovable && !disk.interfaceType.contains("USB")) {
            m_disks.push_back(disk);
            qDebug() << "  -> Added to list";
        } else {
            qDebug() << "  -> Filtered out";
        }
    }
    
    qDebug() << "Total disks enumerated:" << m_disks.size();
#endif
}

void StorageHealthMonitor::readSmartData(DiskHealthInfo& disk)
{
#ifdef _WIN32
    if (!disk.smartSupported) {
        return;
    }
    
    HANDLE hDevice = CreateFileW(
        reinterpret_cast<LPCWSTR>(disk.devicePath.utf16()),
        GENERIC_READ | GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr,
        OPEN_EXISTING,
        0,
        nullptr
    );
    
    if (hDevice == INVALID_HANDLE_VALUE) {
        // Try with read-only access if read-write fails (non-admin)
        hDevice = CreateFileW(
            reinterpret_cast<LPCWSTR>(disk.devicePath.utf16()),
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr,
            OPEN_EXISTING,
            0,
            nullptr
        );
        if (hDevice == INVALID_HANDLE_VALUE) {
            qDebug() << "Failed to open device for SMART data:" << disk.devicePath 
                     << "Error:" << GetLastError();
            return;
        }
    }
    
    int driveNumber = 0;
    if (disk.devicePath.contains("PhysicalDrive")) {
        driveNumber = disk.devicePath.mid(disk.devicePath.lastIndexOf("PhysicalDrive") + 13).toInt();
    }
    
    const DWORD dataSize = sizeof(SENDCMDOUTPARAMS_BLOCK) + 512 - 1;
    std::vector<BYTE> outBuffer(dataSize, 0);
    
    SENDCMDINPARAMS_BLOCK inParams;
    memset(&inParams, 0, sizeof(inParams));
    inParams.cBufferSize = 512;
    inParams.irDriveRegs.bFeaturesReg = SMART_READ_DATA;
    inParams.irDriveRegs.bSectorCountReg = 1;
    inParams.irDriveRegs.bCylLowReg = SMART_CYL_LOW;
    inParams.irDriveRegs.bCylHighReg = SMART_CYL_HI;
    inParams.irDriveRegs.bDriveHeadReg = 0xA0 | ((driveNumber & 1) << 4);
    inParams.irDriveRegs.bCommandReg = IDE_EXECUTE_SMART_FUNCTION;
    inParams.bDriveNumber = static_cast<BYTE>(driveNumber);
    
    DWORD bytesReturned = 0;
    BOOL success = DeviceIoControl(hDevice, SMART_RCV_DRIVE_DATA,
        &inParams, sizeof(inParams), outBuffer.data(), dataSize, &bytesReturned, nullptr);
    
    if (success) {
        SENDCMDOUTPARAMS_BLOCK* outParams = reinterpret_cast<SENDCMDOUTPARAMS_BLOCK*>(outBuffer.data());
        parseSmartAttributes(outParams->bBuffer, 512, disk.smartAttributes);
        
        std::fill(outBuffer.begin(), outBuffer.end(), 0);
        inParams.irDriveRegs.bFeaturesReg = SMART_READ_THRESHOLDS;
        
        if (DeviceIoControl(hDevice, SMART_RCV_DRIVE_DATA,
                &inParams, sizeof(inParams), outBuffer.data(), dataSize, &bytesReturned, nullptr)) {
            outParams = reinterpret_cast<SENDCMDOUTPARAMS_BLOCK*>(outBuffer.data());
            parseSmartThresholds(outParams->bBuffer, 512, disk.smartAttributes);
        }
    }
    
    for (const auto& attr : disk.smartAttributes) {
        if (attr.id == 0xC2 || attr.id == 0xBE) {
            disk.temperatureCelsius = static_cast<int>(attr.rawValue & 0xFF);
        }
        if (attr.id == 0x09) {
            disk.powerOnHours = attr.rawValue;
        }
        if (attr.id == 0x0C) {
            disk.powerCycles = attr.rawValue;
        }
    }
    
    CloseHandle(hDevice);
#endif
}

void StorageHealthMonitor::parseSmartAttributes(const uint8_t* data, size_t length, 
                                                 std::vector<SmartAttributeData>& attributes)
{
#ifdef _WIN32
    if (length < sizeof(SMART_DATA_BLOCK)) return;
    
    const SMART_DATA_BLOCK* smartData = reinterpret_cast<const SMART_DATA_BLOCK*>(data);
    
    for (int i = 0; i < 30; i++) {
        const SMART_ATTRIBUTE_ENTRY& attr = smartData->Attributes[i];
        if (attr.Id == 0) continue;
        
        SmartAttributeData attrData;
        attrData.id = attr.Id;
        attrData.name = getAttributeName(attr.Id);
        attrData.currentValue = attr.CurrentValue;
        attrData.worstValue = attr.WorstValue;
        
        attrData.rawValue = 0;
        for (int j = 5; j >= 0; j--) {
            attrData.rawValue = (attrData.rawValue << 8) | attr.RawValue[j];
        }
        
        switch (attr.Id) {
            case 0x09:
                attrData.rawValueString = QString("%1 hours (%2 days)")
                    .arg(attrData.rawValue).arg(attrData.rawValue / 24);
                break;
            case 0xC2:
            case 0xBE:
                attrData.rawValueString = QString("%1 C").arg(attrData.rawValue & 0xFF);
                break;
            case 0xF1:
            case 0xF2:
                attrData.rawValueString = formatBytes(attrData.rawValue * 512);
                break;
            default:
                attrData.rawValueString = QString::number(attrData.rawValue);
                break;
        }
        
        attrData.isCritical = isAttributeCritical(attr.Id);
        attrData.isPrefail = (attr.Flags & 0x01) != 0;
        attrData.isOk = true;
        
        attributes.push_back(attrData);
    }
#endif
}

void StorageHealthMonitor::parseSmartThresholds(const uint8_t* data, size_t length,
                                                  std::vector<SmartAttributeData>& attributes)
{
#ifdef _WIN32
    if (length < sizeof(SMART_THRESHOLDS_BLOCK)) return;
    
    const SMART_THRESHOLDS_BLOCK* thresholdData = reinterpret_cast<const SMART_THRESHOLDS_BLOCK*>(data);
    
    for (auto& attr : attributes) {
        for (int i = 0; i < 30; i++) {
            if (thresholdData->Thresholds[i].Id == attr.id) {
                attr.threshold = thresholdData->Thresholds[i].ThresholdValue;
                attr.isOk = attr.currentValue > attr.threshold || attr.threshold == 0;
                break;
            }
        }
    }
#endif
}

void StorageHealthMonitor::readNvmeHealth(DiskHealthInfo& disk)
{
#ifdef _WIN32
    HANDLE hDevice = CreateFileW(
        reinterpret_cast<LPCWSTR>(disk.devicePath.utf16()),
        GENERIC_READ | GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr,
        OPEN_EXISTING,
        0,
        nullptr
    );
    
    if (hDevice == INVALID_HANDLE_VALUE) {
        // Try with read-only access if read-write fails (non-admin)
        hDevice = CreateFileW(
            reinterpret_cast<LPCWSTR>(disk.devicePath.utf16()),
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr,
            OPEN_EXISTING,
            0,
            nullptr
        );
        if (hDevice == INVALID_HANDLE_VALUE) {
            qDebug() << "Failed to open device for NVMe health:" << disk.devicePath;
            return;
        }
    }
    
    // Proper structure for NVMe health query
    struct {
        STORAGE_PROPERTY_QUERY Query;
        STORAGE_PROTOCOL_SPECIFIC_DATA ProtocolSpecific;
        BYTE Buffer[sizeof(NVME_HEALTH_INFO_BLOCK)];
    } queryBuffer;
    
    memset(&queryBuffer, 0, sizeof(queryBuffer));
    
    queryBuffer.Query.PropertyId = StorageDeviceProtocolSpecificProperty;
    queryBuffer.Query.QueryType = PropertyStandardQuery;
    
    queryBuffer.ProtocolSpecific.ProtocolType = ProtocolTypeNvme;
    queryBuffer.ProtocolSpecific.DataType = NVMeDataTypeLogPage;
    queryBuffer.ProtocolSpecific.ProtocolDataRequestValue = NVME_HEALTH_INFO_LOG;
    queryBuffer.ProtocolSpecific.ProtocolDataRequestSubValue = 0;
    queryBuffer.ProtocolSpecific.ProtocolDataOffset = sizeof(STORAGE_PROTOCOL_SPECIFIC_DATA);
    queryBuffer.ProtocolSpecific.ProtocolDataLength = sizeof(NVME_HEALTH_INFO_BLOCK);
    
    DWORD bytesReturned = 0;
    BOOL success = DeviceIoControl(hDevice, IOCTL_STORAGE_QUERY_PROPERTY,
        &queryBuffer, sizeof(queryBuffer),
        &queryBuffer, sizeof(queryBuffer), &bytesReturned, nullptr);
    
    if (success && bytesReturned >= offsetof(decltype(queryBuffer), Buffer) + sizeof(NVME_HEALTH_INFO_BLOCK)) {
        const NVME_HEALTH_INFO_BLOCK* healthInfo = reinterpret_cast<const NVME_HEALTH_INFO_BLOCK*>(queryBuffer.Buffer);
        
        disk.nvmeHealth.isValid = true;
        disk.nvmeHealth.availableSpare = healthInfo->AvailableSpare;
        disk.nvmeHealth.availableSpareThreshold = healthInfo->AvailableSpareThreshold;
        disk.nvmeHealth.percentageUsed = healthInfo->PercentageUsed;
        
        disk.nvmeHealth.temperature = healthInfo->CompositeTemperature;
        disk.temperatureCelsius = healthInfo->CompositeTemperature - 273;
        
        auto extract64 = [](const BYTE* d) -> uint64_t {
            uint64_t value = 0;
            for (int i = 7; i >= 0; i--) {
                value = (value << 8) | d[i];
            }
            return value;
        };
        
        disk.nvmeHealth.dataUnitsRead = extract64(healthInfo->DataUnitsRead);
        disk.nvmeHealth.dataUnitsWritten = extract64(healthInfo->DataUnitsWritten);
        disk.nvmeHealth.powerCycles = extract64(healthInfo->PowerCycles);
        disk.nvmeHealth.powerOnHours = extract64(healthInfo->PowerOnHours);
        disk.nvmeHealth.unsafeShutdowns = extract64(healthInfo->UnsafeShutdowns);
        disk.nvmeHealth.mediaErrors = extract64(healthInfo->MediaErrors);
        
        disk.powerOnHours = disk.nvmeHealth.powerOnHours;
        disk.powerCycles = disk.nvmeHealth.powerCycles;
        
        // Create synthetic SMART attributes for NVMe
        SmartAttributeData attr;
        
        attr = SmartAttributeData();
        attr.id = 0xE8;
        attr.name = "Available Spare";
        attr.currentValue = disk.nvmeHealth.availableSpare;
        attr.threshold = disk.nvmeHealth.availableSpareThreshold;
        attr.rawValue = disk.nvmeHealth.availableSpare;
        attr.rawValueString = QString("%1%").arg(disk.nvmeHealth.availableSpare);
        attr.isCritical = true;
        attr.isOk = disk.nvmeHealth.availableSpare > disk.nvmeHealth.availableSpareThreshold;
        disk.smartAttributes.push_back(attr);
        
        attr = SmartAttributeData();
        attr.id = 0xE9;
        attr.name = "Percentage Used";
        attr.currentValue = 100 - std::min(disk.nvmeHealth.percentageUsed, static_cast<uint8_t>(100));
        attr.rawValue = disk.nvmeHealth.percentageUsed;
        attr.rawValueString = QString("%1%").arg(disk.nvmeHealth.percentageUsed);
        attr.isCritical = true;
        attr.isOk = disk.nvmeHealth.percentageUsed < 100;
        disk.smartAttributes.push_back(attr);
        
        attr = SmartAttributeData();
        attr.id = 0xC2;
        attr.name = "Temperature";
        attr.currentValue = 100;
        attr.rawValue = disk.temperatureCelsius;
        attr.rawValueString = QString("%1 C").arg(disk.temperatureCelsius);
        attr.isOk = disk.temperatureCelsius < 70;
        disk.smartAttributes.push_back(attr);
        
        attr = SmartAttributeData();
        attr.id = 0x09;
        attr.name = "Power-On Hours";
        attr.currentValue = 100;
        attr.rawValue = disk.powerOnHours;
        attr.rawValueString = QString("%1 hours (%2 days)")
            .arg(disk.powerOnHours).arg(disk.powerOnHours / 24);
        disk.smartAttributes.push_back(attr);
        
        attr = SmartAttributeData();
        attr.id = 0xF1;
        attr.name = "Data Written";
        attr.currentValue = 100;
        attr.rawValue = disk.nvmeHealth.dataUnitsWritten * 512000;
        attr.rawValueString = formatBytes(attr.rawValue);
        disk.smartAttributes.push_back(attr);
        
        attr = SmartAttributeData();
        attr.id = 0xBB;
        attr.name = "Media Errors";
        attr.currentValue = disk.nvmeHealth.mediaErrors > 0 ? 1 : 100;
        attr.rawValue = disk.nvmeHealth.mediaErrors;
        attr.rawValueString = QString::number(disk.nvmeHealth.mediaErrors);
        attr.isCritical = true;
        attr.isOk = disk.nvmeHealth.mediaErrors == 0;
        disk.smartAttributes.push_back(attr);
    }
    
    CloseHandle(hDevice);
#endif
}

void StorageHealthMonitor::calculateHealthStatus(DiskHealthInfo& disk)
{
    int healthScore = 100;
    QStringList issues;
    
    if (disk.isNvme && disk.nvmeHealth.isValid) {
        int percentUsed = disk.nvmeHealth.percentageUsed;
        if (percentUsed > 100) {
            healthScore -= 50;
            issues << tr("Drive has exceeded its rated lifespan");
        } else if (percentUsed > 90) {
            healthScore -= 30;
            issues << tr("Drive is approaching end of life (%1% used)").arg(percentUsed);
        } else if (percentUsed > 70) {
            healthScore -= 15;
        } else if (percentUsed > 50) {
            healthScore -= 5;
        }
        
        if (disk.nvmeHealth.availableSpare < disk.nvmeHealth.availableSpareThreshold) {
            healthScore -= 30;
            issues << tr("Available spare space below threshold");
        }
        
        if (disk.nvmeHealth.mediaErrors > 0) {
            healthScore -= 20;
            issues << tr("Media errors detected: %1").arg(disk.nvmeHealth.mediaErrors);
        }
        
        disk.estimatedLifeRemainingPercent = 100.0 - disk.nvmeHealth.percentageUsed;
        if (disk.estimatedLifeRemainingPercent < 0) disk.estimatedLifeRemainingPercent = 0;
        
    } else {
        for (const auto& attr : disk.smartAttributes) {
            if (!attr.isOk) {
                healthScore -= 20;
                issues << tr("Attribute %1 below threshold").arg(attr.name);
            }
            
            if (attr.isCritical && attr.rawValue > 0) {
                switch (attr.id) {
                    case 0x05:
                        if (attr.rawValue > 100) {
                            healthScore -= 25;
                            issues << tr("High reallocated sector count: %1").arg(attr.rawValue);
                        } else if (attr.rawValue > 10) {
                            healthScore -= 10;
                        }
                        break;
                    case 0xC5:
                        healthScore -= 15;
                        issues << tr("Pending sectors: %1").arg(attr.rawValue);
                        break;
                    case 0xC6:
                        healthScore -= 20;
                        issues << tr("Uncorrectable sectors: %1").arg(attr.rawValue);
                        break;
                }
            }
        }
        
        if (!disk.smartPassed) {
            healthScore = std::min(healthScore, 20);
            issues << tr("SMART overall health test FAILED");
        }
    }
    
    if (disk.temperatureCelsius > 60) {
        healthScore -= 5;
        issues << tr("Elevated temperature: %1 C").arg(disk.temperatureCelsius);
    }
    
    healthScore = std::clamp(healthScore, 0, 100);
    disk.healthPercent = healthScore;
    
    if (healthScore >= 90) {
        disk.healthStatus = DriveHealthStatus::Excellent;
        disk.healthDescription = tr("Drive is in excellent condition");
    } else if (healthScore >= 70) {
        disk.healthStatus = DriveHealthStatus::Good;
        disk.healthDescription = tr("Drive is in good condition with minor wear");
    } else if (healthScore >= 50) {
        disk.healthStatus = DriveHealthStatus::Fair;
        disk.healthDescription = tr("Drive shows moderate wear, consider backup");
    } else if (healthScore >= 20) {
        disk.healthStatus = DriveHealthStatus::Poor;
        disk.healthDescription = tr("Drive health is poor, replace soon");
    } else {
        disk.healthStatus = DriveHealthStatus::Critical;
        disk.healthDescription = tr("Drive failure imminent, backup immediately!");
    }
    
    if (!issues.isEmpty()) {
        disk.healthDescription += "\n\n" + tr("Issues found:") + "\n- " + issues.join("\n- ");
    }
    
    if (disk.estimatedLifeRemainingPercent >= 0) {
        disk.estimatedLifeDescription = QString("%1%").arg(disk.estimatedLifeRemainingPercent, 0, 'f', 1);
    } else if (disk.isSsd) {
        disk.estimatedLifeDescription = tr("Unable to estimate (no wear data)");
    } else {
        disk.estimatedLifeDescription = tr("N/A (HDD)");
    }
}

void StorageHealthMonitor::checkAlerts(DiskHealthInfo& disk)
{
    disk.warnings.clear();
    disk.criticalAlerts.clear();
    
    if (disk.healthStatus == DriveHealthStatus::Critical) {
        disk.criticalAlerts << tr("Drive health is critical - backup data immediately!");
        emit diskHealthCritical(disk.model, disk.criticalAlerts.first());
    }
    
    if (!disk.smartPassed) {
        disk.criticalAlerts << tr("SMART health check failed");
        emit diskHealthCritical(disk.model, tr("SMART health check failed"));
    }
    
    if (disk.temperatureCelsius > 70) {
        disk.criticalAlerts << tr("Temperature critical: %1 C").arg(disk.temperatureCelsius);
    } else if (disk.temperatureCelsius > 55) {
        disk.warnings << tr("Temperature elevated: %1 C").arg(disk.temperatureCelsius);
    }
    
    if (disk.isNvme && disk.nvmeHealth.isValid) {
        if (disk.nvmeHealth.percentageUsed > 90) {
            disk.warnings << tr("SSD lifespan almost exhausted: %1% used")
                .arg(disk.nvmeHealth.percentageUsed);
        }
        
        if (disk.nvmeHealth.mediaErrors > 0) {
            disk.warnings << tr("Media errors detected: %1")
                .arg(disk.nvmeHealth.mediaErrors);
        }
    }
    
    for (const auto& attr : disk.smartAttributes) {
        if (!attr.isOk) {
            disk.warnings << tr("Attribute '%1' below threshold").arg(attr.name);
        }
        
        if (attr.id == 0x05 && attr.rawValue > 50) {
            disk.warnings << tr("High reallocated sector count: %1").arg(attr.rawValue);
        }
        
        if (attr.id == 0xC5 && attr.rawValue > 0) {
            disk.warnings << tr("Pending sectors detected: %1").arg(attr.rawValue);
        }
        
        if (attr.id == 0xC6 && attr.rawValue > 0) {
            disk.criticalAlerts << tr("Uncorrectable sectors: %1").arg(attr.rawValue);
        }
    }
}
