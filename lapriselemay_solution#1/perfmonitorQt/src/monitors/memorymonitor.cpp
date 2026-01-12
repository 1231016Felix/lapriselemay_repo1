#include "memorymonitor.h"
#include "../utils/systeminfo.h"

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <Windows.h>
#include <Psapi.h>
#include <TlHelp32.h>
#pragma comment(lib, "psapi.lib")

// Define NTSTATUS if not defined
#ifndef NTSTATUS
typedef LONG NTSTATUS;
#endif

// Memory list command for NtSetSystemInformation
typedef enum _SYSTEM_MEMORY_LIST_COMMAND {
    MemoryCaptureAccessedBits,
    MemoryCaptureAndResetAccessedBits,
    MemoryEmptyWorkingSets,
    MemoryFlushModifiedList,
    MemoryPurgeStandbyList,
    MemoryPurgeLowPriorityStandbyList,
    MemoryCommandMax
} SYSTEM_MEMORY_LIST_COMMAND;

// NtSetSystemInformation function pointer
typedef NTSTATUS(WINAPI* PFN_NtSetSystemInformation)(
    ULONG SystemInformationClass,
    PVOID SystemInformation,
    ULONG SystemInformationLength
);

#define SystemMemoryListInformation 80

// Define privilege name if not defined
#ifndef SE_PROFILE_SINGLE_PROCESS_NAME
#define SE_PROFILE_SINGLE_PROCESS_NAME L"SeProfileSingleProcessPrivilege"
#endif

#ifndef SE_DEBUG_NAME
#define SE_DEBUG_NAME L"SeDebugPrivilege"
#endif

#ifndef SE_INC_WORKING_SET_NAME
#define SE_INC_WORKING_SET_NAME L"SeIncreaseWorkingSetPrivilege"
#endif

#ifndef SE_INCREASE_QUOTA_NAME
#define SE_INCREASE_QUOTA_NAME L"SeIncreaseQuotaPrivilege"
#endif

#endif

MemoryMonitor::MemoryMonitor(QObject *parent)
    : QObject(parent)
{
    update();
}

void MemoryMonitor::update()
{
#ifdef _WIN32
    MEMORYSTATUSEX memStatus;
    memStatus.dwLength = sizeof(memStatus);
    
    if (GlobalMemoryStatusEx(&memStatus)) {
        const double bytesToGB = 1.0 / (1024.0 * 1024.0 * 1024.0);
        
        m_info.totalGB = memStatus.ullTotalPhys * bytesToGB;
        m_info.availableGB = memStatus.ullAvailPhys * bytesToGB;
        m_info.usedGB = m_info.totalGB - m_info.availableGB;
        m_info.usagePercent = memStatus.dwMemoryLoad;
        m_info.commitLimitGB = memStatus.ullTotalPageFile * bytesToGB;
        m_info.committedGB = (memStatus.ullTotalPageFile - memStatus.ullAvailPageFile) * bytesToGB;
    }
    
    // Get additional memory info
    PERFORMANCE_INFORMATION perfInfo;
    perfInfo.cb = sizeof(perfInfo);
    
    if (GetPerformanceInfo(&perfInfo, sizeof(perfInfo))) {
        const double pageSize = perfInfo.PageSize;
        const double bytesToMB = 1.0 / (1024.0 * 1024.0);
        const double bytesToGB = bytesToMB / 1024.0;
        
        // System cache
        m_info.cachedGB = perfInfo.SystemCache * pageSize * bytesToGB;
        
        // Kernel pools
        m_info.pagedPoolMB = perfInfo.KernelPaged * pageSize * bytesToMB;
        m_info.nonPagedPoolMB = perfInfo.KernelNonpaged * pageSize * bytesToMB;
    }
#endif
}

bool MemoryMonitor::isAdministrator()
{
    return SystemInfo::isAdministrator();
}

// Helper function to enable a privilege
static bool EnablePrivilege(LPCWSTR privilegeName)
{
#ifdef _WIN32
    HANDLE hToken;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, &hToken)) {
        return false;
    }
    
    TOKEN_PRIVILEGES tp;
    tp.PrivilegeCount = 1;
    tp.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
    
    if (!LookupPrivilegeValueW(nullptr, privilegeName, &tp.Privileges[0].Luid)) {
        CloseHandle(hToken);
        return false;
    }
    
    BOOL result = AdjustTokenPrivileges(hToken, FALSE, &tp, sizeof(tp), nullptr, nullptr);
    DWORD error = GetLastError();
    CloseHandle(hToken);
    
    return result && (error == ERROR_SUCCESS);
#else
    return false;
#endif
}

// Helper function to call NtSetSystemInformation
static bool CallNtSetSystemInformation(SYSTEM_MEMORY_LIST_COMMAND command)
{
#ifdef _WIN32
    HMODULE hNtdll = GetModuleHandleW(L"ntdll.dll");
    if (!hNtdll) {
        return false;
    }
    
    auto NtSetSystemInformation = reinterpret_cast<PFN_NtSetSystemInformation>(
        GetProcAddress(hNtdll, "NtSetSystemInformation"));
    
    if (!NtSetSystemInformation) {
        return false;
    }
    
    NTSTATUS status = NtSetSystemInformation(
        SystemMemoryListInformation,
        &command,
        sizeof(command)
    );
    
    return status == 0;
#else
    return false;
#endif
}

bool MemoryMonitor::purgeStandbyList()
{
#ifdef _WIN32
    if (!isAdministrator()) {
        return false;
    }
    
    // Enable required privilege
    EnablePrivilege(SE_PROFILE_SINGLE_PROCESS_NAME);
    
    bool success = true;
    
    // 1. Purge low priority standby list first
    success &= CallNtSetSystemInformation(MemoryPurgeLowPriorityStandbyList);
    
    // 2. Purge normal standby list
    success |= CallNtSetSystemInformation(MemoryPurgeStandbyList);
    
    // 3. Flush modified page list (write dirty pages to disk)
    success |= CallNtSetSystemInformation(MemoryFlushModifiedList);
    
    return success;
#else
    return false;
#endif
}

bool MemoryMonitor::purgeWorkingSets()
{
#ifdef _WIN32
    if (!isAdministrator()) {
        return false;
    }
    
    // Enable required privileges
    EnablePrivilege(SE_DEBUG_NAME);
    EnablePrivilege(SE_INC_WORKING_SET_NAME);
    
    // First, use the system-wide command to empty all working sets
    EnablePrivilege(SE_PROFILE_SINGLE_PROCESS_NAME);
    CallNtSetSystemInformation(MemoryEmptyWorkingSets);
    
    // Then, manually empty each process for more thorough cleanup
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) {
        return false;
    }
    
    PROCESSENTRY32W pe;
    pe.dwSize = sizeof(pe);
    
    int successCount = 0;
    DWORD currentPid = GetCurrentProcessId();
    
    if (Process32FirstW(snapshot, &pe)) {
        do {
            // Skip system processes and our own process
            if (pe.th32ProcessID == 0 || pe.th32ProcessID == 4 || pe.th32ProcessID == currentPid) {
                continue;
            }
            
            HANDLE hProcess = OpenProcess(
                PROCESS_SET_QUOTA | PROCESS_QUERY_INFORMATION,
                FALSE, pe.th32ProcessID
            );
            
            if (hProcess) {
                // Method 1: EmptyWorkingSet
                if (EmptyWorkingSet(hProcess)) {
                    successCount++;
                }
                
                // Method 2: SetProcessWorkingSetSizeEx - more aggressive
                // Setting both to -1 forces the system to trim the working set
                SetProcessWorkingSetSizeEx(hProcess, (SIZE_T)-1, (SIZE_T)-1, 0);
                
                CloseHandle(hProcess);
            }
        } while (Process32NextW(snapshot, &pe));
    }
    
    CloseHandle(snapshot);
    return successCount > 0;
#else
    return false;
#endif
}

bool MemoryMonitor::purgeAllMemory()
{
#ifdef _WIN32
    if (!isAdministrator()) {
        return false;
    }
    
    bool overallSuccess = false;
    
    // Enable all required privileges upfront
    EnablePrivilege(SE_DEBUG_NAME);
    EnablePrivilege(SE_PROFILE_SINGLE_PROCESS_NAME);
    EnablePrivilege(SE_INC_WORKING_SET_NAME);
    EnablePrivilege(SE_INCREASE_QUOTA_NAME);
    
    // Step 1: Flush file system buffers (writes cached data to disk)
    // This frees up file cache memory
    HANDLE hVolume = CreateFileW(
        L"\\\\.\\C:",
        GENERIC_READ | GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr,
        OPEN_EXISTING,
        0,
        nullptr
    );
    if (hVolume != INVALID_HANDLE_VALUE) {
        FlushFileBuffers(hVolume);
        CloseHandle(hVolume);
    }
    
    // Step 2: Empty all working sets (system-wide command)
    if (CallNtSetSystemInformation(MemoryEmptyWorkingSets)) {
        overallSuccess = true;
    }
    
    // Step 3: Empty working sets process by process (more thorough)
    if (purgeWorkingSets()) {
        overallSuccess = true;
    }
    
    // Step 4: Flush modified pages to disk
    if (CallNtSetSystemInformation(MemoryFlushModifiedList)) {
        overallSuccess = true;
    }
    
    // Step 5: Purge low priority standby list
    if (CallNtSetSystemInformation(MemoryPurgeLowPriorityStandbyList)) {
        overallSuccess = true;
    }
    
    // Step 6: Purge standby list (main cache)
    if (CallNtSetSystemInformation(MemoryPurgeStandbyList)) {
        overallSuccess = true;
    }
    
    // Step 7: Second pass - empty working sets again after cache purge
    CallNtSetSystemInformation(MemoryEmptyWorkingSets);
    
    return overallSuccess;
#else
    return false;
#endif
}
