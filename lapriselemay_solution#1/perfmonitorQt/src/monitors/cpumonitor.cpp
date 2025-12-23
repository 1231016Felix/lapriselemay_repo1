#include "cpumonitor.h"

#ifdef _WIN32
#include <Windows.h>
#include <intrin.h>
#include <powerbase.h>
#include <TlHelp32.h>
#include <Psapi.h>
#pragma comment(lib, "pdh.lib")
#pragma comment(lib, "powrprof.lib")
#pragma comment(lib, "psapi.lib")
#endif

#include <QSysInfo>
#include <QThread>

CpuMonitor::CpuMonitor(QObject *parent)
    : QObject(parent)
{
    queryProcessorName();
    queryProcessorInfo();
    initializePdh();
    
#ifdef _WIN32
    GetSystemTimes(&m_prevIdleTime, &m_prevKernelTime, &m_prevUserTime);
#endif
}

CpuMonitor::~CpuMonitor()
{
#ifdef _WIN32
    if (m_query) {
        PdhCloseQuery(m_query);
    }
#endif
}

void CpuMonitor::initializePdh()
{
#ifdef _WIN32
    PDH_STATUS status = PdhOpenQuery(nullptr, 0, &m_query);
    if (status != ERROR_SUCCESS) {
        return;
    }
    
    status = PdhAddEnglishCounterW(m_query, 
        L"\\Processor(_Total)\\% Processor Time", 0, &m_cpuCounter);
    
    if (status == ERROR_SUCCESS) {
        for (int i = 0; i < m_info.logicalProcessors; ++i) {
            PDH_HCOUNTER coreCounter;
            QString counterPath = QString("\\Processor(%1)\\% Processor Time").arg(i);
            status = PdhAddEnglishCounterW(m_query, 
                reinterpret_cast<LPCWSTR>(counterPath.utf16()), 0, &coreCounter);
            if (status == ERROR_SUCCESS) {
                m_coreCounters.push_back(coreCounter);
            }
        }
        
        PdhCollectQueryData(m_query);
        m_pdhInitialized = true;
    }
#endif
}

void CpuMonitor::queryProcessorName()
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
        
        m_info.name = QString::fromLatin1(brand).trimmed();
    } else {
        m_info.name = QSysInfo::currentCpuArchitecture();
    }
#else
    m_info.name = QSysInfo::currentCpuArchitecture();
#endif
}

void CpuMonitor::queryProcessorInfo()
{
#ifdef _WIN32
    SYSTEM_INFO sysInfo;
    GetSystemInfo(&sysInfo);
    m_info.logicalProcessors = sysInfo.dwNumberOfProcessors;
    
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
        m_info.cores = cores;
    } else {
        m_info.cores = m_info.logicalProcessors / 2;
    }
    
    HKEY hKey;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, 
        L"HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0",
        0, KEY_READ, &hKey) == ERROR_SUCCESS) {
        
        DWORD mhz = 0;
        DWORD size = sizeof(mhz);
        if (RegQueryValueExW(hKey, L"~MHz", nullptr, nullptr, 
            reinterpret_cast<LPBYTE>(&mhz), &size) == ERROR_SUCCESS) {
            m_info.baseSpeed = mhz / 1000.0;
        }
        RegCloseKey(hKey);
    }
#else
    m_info.logicalProcessors = QThread::idealThreadCount();
    m_info.cores = m_info.logicalProcessors;
#endif
    
    m_info.coreUsages.resize(m_info.logicalProcessors, 0.0);
}

void CpuMonitor::update()
{
#ifdef _WIN32
    FILETIME idleTime, kernelTime, userTime;
    if (GetSystemTimes(&idleTime, &kernelTime, &userTime)) {
        auto fileTimeToUInt64 = [](const FILETIME& ft) -> ULONGLONG {
            return (static_cast<ULONGLONG>(ft.dwHighDateTime) << 32) | ft.dwLowDateTime;
        };
        
        ULONGLONG idle = fileTimeToUInt64(idleTime) - fileTimeToUInt64(m_prevIdleTime);
        ULONGLONG kernel = fileTimeToUInt64(kernelTime) - fileTimeToUInt64(m_prevKernelTime);
        ULONGLONG user = fileTimeToUInt64(userTime) - fileTimeToUInt64(m_prevUserTime);
        
        ULONGLONG total = kernel + user;
        if (total > 0) {
            m_info.usage = (1.0 - static_cast<double>(idle) / total) * 100.0;
        }
        
        m_prevIdleTime = idleTime;
        m_prevKernelTime = kernelTime;
        m_prevUserTime = userTime;
    }
    
    if (m_pdhInitialized && PdhCollectQueryData(m_query) == ERROR_SUCCESS) {
        for (size_t i = 0; i < m_coreCounters.size(); ++i) {
            PDH_FMT_COUNTERVALUE value;
            if (PdhGetFormattedCounterValue(m_coreCounters[i], PDH_FMT_DOUBLE, 
                nullptr, &value) == ERROR_SUCCESS) {
                m_info.coreUsages[i] = value.doubleValue;
            }
        }
    }
    
    m_info.currentSpeed = m_info.baseSpeed * (0.8 + (m_info.usage / 500.0));
    
    DWORD processIds[1024], bytesReturned;
    if (EnumProcesses(processIds, sizeof(processIds), &bytesReturned)) {
        m_info.processCount = bytesReturned / sizeof(DWORD);
    }
    
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
    if (snapshot != INVALID_HANDLE_VALUE) {
        THREADENTRY32 te;
        te.dwSize = sizeof(te);
        
        int threadCount = 0;
        if (Thread32First(snapshot, &te)) {
            do {
                threadCount++;
            } while (Thread32Next(snapshot, &te));
        }
        m_info.threadCount = threadCount;
        CloseHandle(snapshot);
    }
    
    m_info.uptime = formatUptime(GetTickCount64());
#endif
}

QString CpuMonitor::formatUptime(qint64 milliseconds)
{
    qint64 seconds = milliseconds / 1000;
    qint64 minutes = seconds / 60;
    qint64 hours = minutes / 60;
    qint64 days = hours / 24;
    
    seconds %= 60;
    minutes %= 60;
    hours %= 24;
    
    if (days > 0) {
        return QString("%1d %2h %3m %4s")
            .arg(days).arg(hours).arg(minutes).arg(seconds);
    } else if (hours > 0) {
        return QString("%1h %2m %3s").arg(hours).arg(minutes).arg(seconds);
    } else {
        return QString("%1m %2s").arg(minutes).arg(seconds);
    }
}
