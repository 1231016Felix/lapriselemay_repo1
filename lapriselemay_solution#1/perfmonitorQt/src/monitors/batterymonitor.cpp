#include "batterymonitor.h"

#ifdef _WIN32
#include <Windows.h>
#include <SetupAPI.h>
#include <devguid.h>

// Include winioctl.h for CTL_CODE macro (may be excluded by WIN32_LEAN_AND_MEAN)
#ifndef CTL_CODE
#include <winioctl.h>
#endif

// If still not defined, define manually
#ifndef CTL_CODE
#define CTL_CODE(DeviceType, Function, Method, Access) \
    (((DeviceType) << 16) | ((Access) << 14) | ((Function) << 2) | (Method))
#endif

#ifndef FILE_DEVICE_BATTERY
#define FILE_DEVICE_BATTERY 0x00000029
#endif

#ifndef METHOD_BUFFERED
#define METHOD_BUFFERED 0
#endif

#ifndef FILE_READ_ACCESS
#define FILE_READ_ACCESS 0x0001
#endif

// Battery IOCTL codes
#ifndef IOCTL_BATTERY_QUERY_TAG
#define IOCTL_BATTERY_QUERY_TAG \
    CTL_CODE(FILE_DEVICE_BATTERY, 0x10, METHOD_BUFFERED, FILE_READ_ACCESS)
#endif

#ifndef IOCTL_BATTERY_QUERY_INFORMATION
#define IOCTL_BATTERY_QUERY_INFORMATION \
    CTL_CODE(FILE_DEVICE_BATTERY, 0x11, METHOD_BUFFERED, FILE_READ_ACCESS)
#endif

#ifndef IOCTL_BATTERY_QUERY_STATUS
#define IOCTL_BATTERY_QUERY_STATUS \
    CTL_CODE(FILE_DEVICE_BATTERY, 0x13, METHOD_BUFFERED, FILE_READ_ACCESS)
#endif

// Battery information structures (from poclass.h)
#ifndef BATTERY_INFORMATION

typedef enum _BATTERY_QUERY_INFORMATION_LEVEL {
    BatteryInformation = 0,
    BatteryGranularityInformation,
    BatteryTemperature,
    BatteryEstimatedTime,
    BatteryDeviceName,
    BatteryManufactureDate,
    BatteryManufactureName,
    BatterySerialNumber,
    BatteryUniqueID
} BATTERY_QUERY_INFORMATION_LEVEL;

typedef struct _BATTERY_QUERY_INFORMATION {
    ULONG BatteryTag;
    BATTERY_QUERY_INFORMATION_LEVEL InformationLevel;
    LONG AtRate;
} BATTERY_QUERY_INFORMATION, *PBATTERY_QUERY_INFORMATION;

typedef struct _BATTERY_INFORMATION {
    ULONG Capabilities;
    UCHAR Technology;
    UCHAR Reserved[3];
    UCHAR Chemistry[4];
    ULONG DesignedCapacity;
    ULONG FullChargedCapacity;
    ULONG DefaultAlert1;
    ULONG DefaultAlert2;
    ULONG CriticalBias;
    ULONG CycleCount;
} BATTERY_INFORMATION, *PBATTERY_INFORMATION;

typedef struct _BATTERY_WAIT_STATUS {
    ULONG BatteryTag;
    ULONG Timeout;
    ULONG PowerState;
    ULONG LowCapacity;
    ULONG HighCapacity;
} BATTERY_WAIT_STATUS, *PBATTERY_WAIT_STATUS;

typedef struct _BATTERY_STATUS {
    ULONG PowerState;
    ULONG Capacity;
    ULONG Voltage;
    LONG Rate;
} BATTERY_STATUS, *PBATTERY_STATUS;

#endif // BATTERY_INFORMATION

#pragma comment(lib, "setupapi.lib")
#endif // _WIN32

#include <QSysInfo>

BatteryMonitor::BatteryMonitor(QObject *parent)
    : QObject(parent)
{
    detectSurfaceDevice();
    update();
}

BatteryMonitor::~BatteryMonitor()
{
#ifdef _WIN32
    if (m_batteryHandle && m_batteryHandle != INVALID_HANDLE_VALUE) {
        CloseHandle(m_batteryHandle);
    }
#endif
}

void BatteryMonitor::detectSurfaceDevice()
{
#ifdef _WIN32
    HKEY hKey;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE,
        L"HARDWARE\\DESCRIPTION\\System\\BIOS",
        0, KEY_READ, &hKey) == ERROR_SUCCESS) {
        
        wchar_t systemFamily[256] = {0};
        DWORD size = sizeof(systemFamily);
        
        if (RegQueryValueExW(hKey, L"SystemFamily", nullptr, nullptr,
            reinterpret_cast<LPBYTE>(systemFamily), &size) == ERROR_SUCCESS) {
            QString family = QString::fromWCharArray(systemFamily);
            m_isSurface = family.contains("Surface", Qt::CaseInsensitive);
        }
        
        if (!m_isSurface) {
            wchar_t productName[256] = {0};
            size = sizeof(productName);
            if (RegQueryValueExW(hKey, L"SystemProductName", nullptr, nullptr,
                reinterpret_cast<LPBYTE>(productName), &size) == ERROR_SUCCESS) {
                QString product = QString::fromWCharArray(productName);
                m_isSurface = product.contains("Surface", Qt::CaseInsensitive);
            }
        }
        
        RegCloseKey(hKey);
    }
#endif
}

void BatteryMonitor::update()
{
    queryBasicInfo();
    if (m_info.hasBattery) {
        queryExtendedInfo();
        
        // Calculate time remaining from charge rate if available
        if (m_info.chargeRate != 0 && m_info.currentCapacity > 0) {
            int seconds = 0;
            
            if (m_info.chargeRate < 0) {
                // Discharging: time = current capacity / discharge rate
                // chargeRate is negative when discharging, so we use abs value
                // capacity is in mWh, rate is in mW, result is in hours
                double hours = static_cast<double>(m_info.currentCapacity) / 
                               static_cast<double>(-m_info.chargeRate);
                seconds = static_cast<int>(hours * 3600);
            } else if (m_info.isCharging && m_info.fullChargeCapacity > m_info.currentCapacity) {
                // Charging: time = (full - current) / charge rate
                double hours = static_cast<double>(m_info.fullChargeCapacity - m_info.currentCapacity) / 
                               static_cast<double>(m_info.chargeRate);
                seconds = static_cast<int>(hours * 3600);
            }
            
            if (seconds > 0 && seconds < 360000) { // Sanity check: less than 100 hours
                m_info.timeRemaining = formatTime(seconds);
            }
        }
    }
}

void BatteryMonitor::queryBasicInfo()
{
#ifdef _WIN32
    SYSTEM_POWER_STATUS powerStatus;
    if (!GetSystemPowerStatus(&powerStatus)) {
        m_info.hasBattery = false;
        return;
    }
    
    m_info.hasBattery = (powerStatus.BatteryFlag != 128);
    
    if (!m_info.hasBattery) {
        return;
    }
    
    if (powerStatus.BatteryLifePercent != 255) {
        m_info.percentage = powerStatus.BatteryLifePercent;
    }
    
    m_info.isPluggedIn = (powerStatus.ACLineStatus == 1);
    m_info.isCharging = (powerStatus.BatteryFlag & 8) != 0;
    
    if (m_info.isCharging) {
        m_info.status = "Charging";
    } else if (m_info.isPluggedIn) {
        m_info.status = "Plugged in, not charging";
    } else {
        m_info.status = "Discharging";
    }
    
    if (powerStatus.BatteryLifeTime != 0xFFFFFFFF) {
        m_info.timeRemaining = formatTime(powerStatus.BatteryLifeTime);
    } else if (m_info.isCharging) {
        m_info.timeRemaining = "Calculating...";
    } else if (m_info.isPluggedIn) {
        m_info.timeRemaining = "Fully charged";
    } else {
        m_info.timeRemaining = "Calculating...";
    }
#else
    m_info.hasBattery = false;
#endif
}

void BatteryMonitor::queryExtendedInfo()
{
#ifdef _WIN32
    // Battery device GUID
    static const GUID GUID_DEVICE_BATTERY = 
        { 0x72631e54, 0x78a4, 0x11d0, { 0xbc, 0xf7, 0x00, 0xaa, 0x00, 0xb7, 0xb3, 0x2a } };
    
    HDEVINFO hDevInfo = SetupDiGetClassDevsW(
        &GUID_DEVICE_BATTERY,
        nullptr,
        nullptr,
        DIGCF_PRESENT | DIGCF_DEVICEINTERFACE
    );
    
    if (hDevInfo == INVALID_HANDLE_VALUE) {
        return;
    }
    
    SP_DEVICE_INTERFACE_DATA deviceInterface;
    deviceInterface.cbSize = sizeof(deviceInterface);
    
    if (!SetupDiEnumDeviceInterfaces(hDevInfo, nullptr, 
        &GUID_DEVICE_BATTERY, 0, &deviceInterface)) {
        SetupDiDestroyDeviceInfoList(hDevInfo);
        return;
    }
    
    DWORD requiredSize = 0;
    SetupDiGetDeviceInterfaceDetailW(hDevInfo, &deviceInterface, 
        nullptr, 0, &requiredSize, nullptr);
    
    if (requiredSize == 0) {
        SetupDiDestroyDeviceInfoList(hDevInfo);
        return;
    }
    
    std::vector<BYTE> buffer(requiredSize);
    auto pDetail = reinterpret_cast<PSP_DEVICE_INTERFACE_DETAIL_DATA_W>(buffer.data());
    pDetail->cbSize = sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W);
    
    if (!SetupDiGetDeviceInterfaceDetailW(hDevInfo, &deviceInterface,
        pDetail, requiredSize, nullptr, nullptr)) {
        SetupDiDestroyDeviceInfoList(hDevInfo);
        return;
    }
    
    HANDLE hBattery = CreateFileW(
        pDetail->DevicePath,
        GENERIC_READ | GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        nullptr
    );
    
    SetupDiDestroyDeviceInfoList(hDevInfo);
    
    if (hBattery == INVALID_HANDLE_VALUE) {
        return;
    }
    
    // Get battery tag
    DWORD batteryTag = 0;
    DWORD dwOut = 0;
    DWORD dwWait = 0;
    
    if (!DeviceIoControl(hBattery, IOCTL_BATTERY_QUERY_TAG,
        &dwWait, sizeof(dwWait), &batteryTag, sizeof(batteryTag),
        &dwOut, nullptr) || batteryTag == 0) {
        CloseHandle(hBattery);
        return;
    }
    
    // Query battery information
    BATTERY_QUERY_INFORMATION bqi = {0};
    bqi.BatteryTag = batteryTag;
    
    // Get battery information
    bqi.InformationLevel = BatteryInformation;
    BATTERY_INFORMATION bi = {0};
    
    if (DeviceIoControl(hBattery, IOCTL_BATTERY_QUERY_INFORMATION,
        &bqi, sizeof(bqi), &bi, sizeof(bi), &dwOut, nullptr)) {
        
        m_info.designCapacity = bi.DesignedCapacity;
        m_info.fullChargeCapacity = bi.FullChargedCapacity;
        m_info.cycleCount = bi.CycleCount;
        m_info.chemistry = QString::fromLatin1(reinterpret_cast<char*>(bi.Chemistry), 4).trimmed();
        
        if (bi.DesignedCapacity > 0) {
            m_info.healthPercent = (bi.FullChargedCapacity * 100.0) / bi.DesignedCapacity;
        }
    }
    
    // Get manufacturer
    bqi.InformationLevel = BatteryManufactureName;
    wchar_t manufacturer[128] = {0};
    if (DeviceIoControl(hBattery, IOCTL_BATTERY_QUERY_INFORMATION,
        &bqi, sizeof(bqi), manufacturer, sizeof(manufacturer), &dwOut, nullptr)) {
        m_info.manufacturer = QString::fromWCharArray(manufacturer);
    }
    
    // Get serial number
    bqi.InformationLevel = BatterySerialNumber;
    wchar_t serial[128] = {0};
    if (DeviceIoControl(hBattery, IOCTL_BATTERY_QUERY_INFORMATION,
        &bqi, sizeof(bqi), serial, sizeof(serial), &dwOut, nullptr)) {
        m_info.serialNumber = QString::fromWCharArray(serial);
    }
    
    // Get battery status
    BATTERY_WAIT_STATUS bws = {0};
    bws.BatteryTag = batteryTag;
    
    BATTERY_STATUS bs = {0};
    if (DeviceIoControl(hBattery, IOCTL_BATTERY_QUERY_STATUS,
        &bws, sizeof(bws), &bs, sizeof(bs), &dwOut, nullptr)) {
        
        m_info.currentCapacity = bs.Capacity;
        m_info.voltage = bs.Voltage;
        m_info.chargeRate = bs.Rate;
    }
    
    // Get temperature
    bqi.InformationLevel = BatteryTemperature;
    ULONG temperature = 0;
    if (DeviceIoControl(hBattery, IOCTL_BATTERY_QUERY_INFORMATION,
        &bqi, sizeof(bqi), &temperature, sizeof(temperature), &dwOut, nullptr) && temperature > 0) {
        // Temperature is returned in tenths of Kelvin
        // Convert to Celsius: (temp_decikelvin / 10) - 273.15
        m_info.temperature = (temperature / 10.0) - 273.15;
    }
    else {
        // Temperature not available - set to -999 as indicator
        m_info.temperature = -999.0;
    
    }
    
    CloseHandle(hBattery);
#endif
}

QString BatteryMonitor::formatTime(int seconds)
{
    if (seconds < 0) return "Unknown";
    
    int hours = seconds / 3600;
    int minutes = (seconds % 3600) / 60;
    
    if (hours > 0) {
        return QString("%1h %2m").arg(hours).arg(minutes);
    }
    return QString("%1m").arg(minutes);
}
