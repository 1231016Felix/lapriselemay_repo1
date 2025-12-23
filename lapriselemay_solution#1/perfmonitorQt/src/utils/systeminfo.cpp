#include "systeminfo.h"

#ifdef _WIN32
#include <Windows.h>
#include <VersionHelpers.h>
#include <intrin.h>
#endif

#include <QSysInfo>
#include <QThread>

namespace SystemInfo {

QString formatBytes(qint64 bytes)
{
    const char* units[] = {"B", "KB", "MB", "GB", "TB"};
    int unitIndex = 0;
    double size = bytes;
    
    while (size >= 1024.0 && unitIndex < 4) {
        size /= 1024.0;
        unitIndex++;
    }
    
    return QString("%1 %2").arg(size, 0, 'f', unitIndex > 0 ? 1 : 0).arg(units[unitIndex]);
}

QString formatBytesPerSecond(qint64 bytesPerSec)
{
    return formatBytes(bytesPerSec) + "/s";
}

QString formatDuration(qint64 milliseconds)
{
    qint64 seconds = milliseconds / 1000;
    qint64 minutes = seconds / 60;
    qint64 hours = minutes / 60;
    qint64 days = hours / 24;
    
    seconds %= 60;
    minutes %= 60;
    hours %= 24;
    
    if (days > 0) {
        return QString("%1d %2h %3m").arg(days).arg(hours).arg(minutes);
    } else if (hours > 0) {
        return QString("%1h %2m %3s").arg(hours).arg(minutes).arg(seconds);
    } else if (minutes > 0) {
        return QString("%1m %2s").arg(minutes).arg(seconds);
    }
    return QString("%1s").arg(seconds);
}

QString formatPercentage(double value, int decimals)
{
    return QString("%1%").arg(value, 0, 'f', decimals);
}

QString getOSVersion()
{
    return QSysInfo::prettyProductName();
}

QString getComputerName()
{
#ifdef _WIN32
    wchar_t name[MAX_COMPUTERNAME_LENGTH + 1];
    DWORD size = sizeof(name) / sizeof(name[0]);
    if (GetComputerName(name, &size)) {
        return QString::fromWCharArray(name);
    }
#endif
    return QSysInfo::machineHostName();
}

QString getUserName()
{
#ifdef _WIN32
    wchar_t name[256];
    DWORD size = sizeof(name) / sizeof(name[0]);
    if (GetUserName(name, &size)) {
        return QString::fromWCharArray(name);
    }
#endif
    return QString();
}

bool isAdministrator()
{
#ifdef _WIN32
    BOOL isAdmin = FALSE;
    PSID adminGroup = nullptr;
    
    SID_IDENTIFIER_AUTHORITY ntAuthority = SECURITY_NT_AUTHORITY;
    if (AllocateAndInitializeSid(&ntAuthority, 2,
        SECURITY_BUILTIN_DOMAIN_RID, DOMAIN_ALIAS_RID_ADMINS,
        0, 0, 0, 0, 0, 0, &adminGroup)) {
        CheckTokenMembership(nullptr, adminGroup, &isAdmin);
        FreeSid(adminGroup);
    }
    return isAdmin != FALSE;
#else
    return false;
#endif
}

bool is64BitOS()
{
#ifdef _WIN32
    #if defined(_WIN64)
        return true;
    #else
        BOOL isWow64 = FALSE;
        IsWow64Process(GetCurrentProcess(), &isWow64);
        return isWow64 != FALSE;
    #endif
#else
    return QSysInfo::currentCpuArchitecture().contains("64");
#endif
}

bool is64BitProcess()
{
#ifdef _WIN64
    return true;
#else
    return false;
#endif
}

QString getCpuName()
{
#ifdef _WIN32
    int cpuInfo[4] = {0};
    char brand[49] = {0};
    
    __cpuid(cpuInfo, 0x80000000);
    unsigned int extIds = cpuInfo[0];
    
    if (extIds >= 0x80000004) {
        __cpuid(cpuInfo, 0x80000002);
        memcpy(brand, cpuInfo, sizeof(cpuInfo));
        __cpuid(cpuInfo, 0x80000003);
        memcpy(brand + 16, cpuInfo, sizeof(cpuInfo));
        __cpuid(cpuInfo, 0x80000004);
        memcpy(brand + 32, cpuInfo, sizeof(cpuInfo));
        
        return QString::fromLatin1(brand).trimmed();
    }
#endif
    return QSysInfo::currentCpuArchitecture();
}

int getCpuCoreCount()
{
#ifdef _WIN32
    DWORD length = 0;
    GetLogicalProcessorInformationEx(RelationProcessorCore, nullptr, &length);
    
    std::vector<BYTE> buffer(length);
    if (GetLogicalProcessorInformationEx(RelationProcessorCore,
        reinterpret_cast<PSYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>(buffer.data()), &length)) {
        
        int cores = 0;
        BYTE* ptr = buffer.data();
        while (ptr < buffer.data() + length) {
            auto info = reinterpret_cast<PSYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>(ptr);
            if (info->Relationship == RelationProcessorCore) {
                cores++;
            }
            ptr += info->Size;
        }
        return cores;
    }
#endif
    return QThread::idealThreadCount();
}

int getCpuThreadCount()
{
#ifdef _WIN32
    SYSTEM_INFO sysInfo;
    GetSystemInfo(&sysInfo);
    return sysInfo.dwNumberOfProcessors;
#else
    return QThread::idealThreadCount();
#endif
}

qint64 getTotalPhysicalMemory()
{
#ifdef _WIN32
    MEMORYSTATUSEX memStatus;
    memStatus.dwLength = sizeof(memStatus);
    if (GlobalMemoryStatusEx(&memStatus)) {
        return memStatus.ullTotalPhys;
    }
#endif
    return 0;
}

qint64 getAvailablePhysicalMemory()
{
#ifdef _WIN32
    MEMORYSTATUSEX memStatus;
    memStatus.dwLength = sizeof(memStatus);
    if (GlobalMemoryStatusEx(&memStatus)) {
        return memStatus.ullAvailPhys;
    }
#endif
    return 0;
}

bool hasBattery()
{
#ifdef _WIN32
    SYSTEM_POWER_STATUS powerStatus;
    if (GetSystemPowerStatus(&powerStatus)) {
        return powerStatus.BatteryFlag != 128;  // 128 = No battery
    }
#endif
    return false;
}

int getBatteryPercentage()
{
#ifdef _WIN32
    SYSTEM_POWER_STATUS powerStatus;
    if (GetSystemPowerStatus(&powerStatus)) {
        if (powerStatus.BatteryLifePercent != 255) {
            return powerStatus.BatteryLifePercent;
        }
    }
#endif
    return -1;
}

bool isOnACPower()
{
#ifdef _WIN32
    SYSTEM_POWER_STATUS powerStatus;
    if (GetSystemPowerStatus(&powerStatus)) {
        return powerStatus.ACLineStatus == 1;
    }
#endif
    return true;
}

} // namespace SystemInfo
