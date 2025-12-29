#include "detailedmemorymonitor.h"

#ifdef _WIN32
#include <Psapi.h>
#include <TlHelp32.h>
#pragma comment(lib, "psapi.lib")
#endif

#include <algorithm>
#include <QDebug>
#include <QColor>
#include <QBrush>

// ============================================================================
// ProcessMemoryModel Implementation
// ============================================================================

ProcessMemoryModel::ProcessMemoryModel(QObject *parent)
    : QAbstractTableModel(parent)
{
}

void ProcessMemoryModel::setProcesses(const std::vector<ProcessMemoryInfo>& processes)
{
    beginResetModel();
    m_processes = processes;
    
    // Sort by current column
    sort(m_sortColumn, m_sortOrder);
    
    endResetModel();
}

const ProcessMemoryInfo* ProcessMemoryModel::getProcess(int row) const
{
    if (row >= 0 && row < static_cast<int>(m_processes.size())) {
        return &m_processes[row];
    }
    return nullptr;
}

int ProcessMemoryModel::rowCount(const QModelIndex &parent) const
{
    if (parent.isValid()) return 0;
    return static_cast<int>(m_processes.size());
}

int ProcessMemoryModel::columnCount(const QModelIndex &parent) const
{
    if (parent.isValid()) return 0;
    return ColCount;
}

QVariant ProcessMemoryModel::data(const QModelIndex &index, int role) const
{
    if (!index.isValid() || index.row() >= static_cast<int>(m_processes.size())) {
        return {};
    }
    
    const auto& proc = m_processes[index.row()];
    
    if (role == Qt::DisplayRole) {
        switch (index.column()) {
            case ColName: return proc.name;
            case ColPID: return proc.pid;
            case ColWorkingSet: return formatBytes(proc.workingSetSize);
            case ColPrivateWS: return formatBytes(proc.privateWorkingSet);
            case ColSharedWS: return formatBytes(proc.sharedWorkingSet);
            case ColPrivateBytes: return formatBytes(proc.privateBytes);
            case ColVirtualBytes: return formatBytes(proc.virtualBytes);
            case ColPageFaults: return QString::number(proc.pageFaultsDelta);
            case ColGrowthRate: return formatGrowthRate(proc.growthRateMBPerMin);
            case ColLeakStatus:
                if (proc.isPotentialLeak) return tr("⚠️ Potential Leak");
                if (proc.consecutiveGrowthCount >= 3) return tr("📈 Growing");
                return tr("✓ Normal");
        }
    }
    else if (role == Qt::ForegroundRole) {
        if (index.column() == ColLeakStatus) {
            if (proc.isPotentialLeak) return QColor(Qt::red);
            if (proc.consecutiveGrowthCount >= 3) return QColor(255, 165, 0); // Orange
            return QColor(Qt::darkGreen);
        }
        if (index.column() == ColGrowthRate && proc.growthRateMBPerMin > 5.0) {
            return QColor(255, 165, 0);
        }
    }
    else if (role == Qt::ToolTipRole) {
        QString tooltip = QString("<b>%1</b> (PID: %2)<br><br>").arg(proc.name).arg(proc.pid);
        tooltip += QString("<b>Working Set:</b> %1<br>").arg(formatBytes(proc.workingSetSize));
        tooltip += QString("  - Private: %1<br>").arg(formatBytes(proc.privateWorkingSet));
        tooltip += QString("  - Shared: %1<br>").arg(formatBytes(proc.sharedWorkingSet));
        tooltip += QString("  - Peak: %1<br><br>").arg(formatBytes(proc.peakWorkingSet));
        tooltip += QString("<b>Private Bytes:</b> %1<br>").arg(formatBytes(proc.privateBytes));
        tooltip += QString("<b>Virtual Bytes:</b> %1<br>").arg(formatBytes(proc.virtualBytes));
        tooltip += QString("<b>Page Faults/s:</b> %1<br><br>").arg(proc.pageFaultsDelta);
        
        if (proc.isPotentialLeak) {
            tooltip += QString("<span style='color:red'><b>⚠️ Potential Memory Leak!</b></span><br>");
            tooltip += QString("Growth rate: %1 MB/min<br>").arg(proc.growthRateMBPerMin, 0, 'f', 2);
            tooltip += QString("Consecutive growth: %1 samples").arg(proc.consecutiveGrowthCount);
        }
        return tooltip;
    }
    else if (role == Qt::TextAlignmentRole) {
        if (index.column() >= ColWorkingSet && index.column() <= ColPageFaults) {
            return static_cast<int>(Qt::AlignRight | Qt::AlignVCenter);
        }
    }
    else if (role == Qt::UserRole) {
        // Return raw values for sorting
        switch (index.column()) {
            case ColWorkingSet: return proc.workingSetSize;
            case ColPrivateWS: return proc.privateWorkingSet;
            case ColSharedWS: return proc.sharedWorkingSet;
            case ColPrivateBytes: return proc.privateBytes;
            case ColVirtualBytes: return proc.virtualBytes;
            case ColPageFaults: return proc.pageFaultsDelta;
            case ColGrowthRate: return proc.growthRateMBPerMin;
        }
    }
    
    return {};
}

QVariant ProcessMemoryModel::headerData(int section, Qt::Orientation orientation, int role) const
{
    if (orientation != Qt::Horizontal || role != Qt::DisplayRole) {
        return {};
    }
    
    switch (section) {
        case ColName: return tr("Process");
        case ColPID: return tr("PID");
        case ColWorkingSet: return tr("Working Set");
        case ColPrivateWS: return tr("Private WS");
        case ColSharedWS: return tr("Shared WS");
        case ColPrivateBytes: return tr("Private Bytes");
        case ColVirtualBytes: return tr("Virtual");
        case ColPageFaults: return tr("Page Faults/s");
        case ColGrowthRate: return tr("Growth Rate");
        case ColLeakStatus: return tr("Status");
    }
    return {};
}

void ProcessMemoryModel::sort(int column, Qt::SortOrder order)
{
    m_sortColumn = column;
    m_sortOrder = order;
    
    beginResetModel();
    
    std::sort(m_processes.begin(), m_processes.end(),
        [column, order](const ProcessMemoryInfo& a, const ProcessMemoryInfo& b) {
            bool less = false;
            switch (column) {
                case ColName: less = a.name.toLower() < b.name.toLower(); break;
                case ColPID: less = a.pid < b.pid; break;
                case ColWorkingSet: less = a.workingSetSize < b.workingSetSize; break;
                case ColPrivateWS: less = a.privateWorkingSet < b.privateWorkingSet; break;
                case ColSharedWS: less = a.sharedWorkingSet < b.sharedWorkingSet; break;
                case ColPrivateBytes: less = a.privateBytes < b.privateBytes; break;
                case ColVirtualBytes: less = a.virtualBytes < b.virtualBytes; break;
                case ColPageFaults: less = a.pageFaultsDelta < b.pageFaultsDelta; break;
                case ColGrowthRate: less = a.growthRateMBPerMin < b.growthRateMBPerMin; break;
                case ColLeakStatus: less = a.consecutiveGrowthCount < b.consecutiveGrowthCount; break;
                default: less = a.privateBytes < b.privateBytes; break;
            }
            return order == Qt::AscendingOrder ? less : !less;
        });
    
    endResetModel();
}

QString ProcessMemoryModel::formatBytes(qint64 bytes) const
{
    return DetailedMemoryMonitor::formatBytes(bytes);
}

QString ProcessMemoryModel::formatGrowthRate(double mbPerMin) const
{
    if (std::abs(mbPerMin) < 0.01) return "-";
    if (mbPerMin > 0) return QString("+%1 MB/min").arg(mbPerMin, 0, 'f', 2);
    return QString("%1 MB/min").arg(mbPerMin, 0, 'f', 2);
}

// ============================================================================
// DetailedMemoryMonitor Implementation
// ============================================================================

DetailedMemoryMonitor::DetailedMemoryMonitor(QObject *parent)
    : QObject(parent)
    , m_model(std::make_unique<ProcessMemoryModel>())
    , m_refreshTimer(std::make_unique<QTimer>(this))
{
    connect(m_refreshTimer.get(), &QTimer::timeout, this, &DetailedMemoryMonitor::refresh);
    
    // Initial refresh
    refresh();
}

DetailedMemoryMonitor::~DetailedMemoryMonitor()
{
    stopAutoRefresh();
}

void DetailedMemoryMonitor::refresh()
{
    emit aboutToRefresh();
    
    querySystemMemory();
    queryProcessMemory();
    takeSnapshot();
    checkSystemMemoryThresholds();
    
    m_model->setProcesses(m_processes);
    
    emit refreshed();
}

void DetailedMemoryMonitor::startAutoRefresh(int intervalMs)
{
    m_refreshTimer->start(intervalMs);
}

void DetailedMemoryMonitor::stopAutoRefresh()
{
    m_refreshTimer->stop();
}

bool DetailedMemoryMonitor::isAutoRefreshing() const
{
    return m_refreshTimer->isActive();
}

void DetailedMemoryMonitor::querySystemMemory()
{
#ifdef _WIN32
    // Basic memory info
    MEMORYSTATUSEX memStatus;
    memStatus.dwLength = sizeof(memStatus);
    
    if (GlobalMemoryStatusEx(&memStatus)) {
        m_systemMemory.totalPhysical = memStatus.ullTotalPhys;
        m_systemMemory.availablePhysical = memStatus.ullAvailPhys;
        m_systemMemory.usedPhysical = m_systemMemory.totalPhysical - m_systemMemory.availablePhysical;
        m_systemMemory.commitLimit = memStatus.ullTotalPageFile;
        m_systemMemory.commitTotal = memStatus.ullTotalPageFile - memStatus.ullAvailPageFile;
    }
    
    // Performance info
    PERFORMANCE_INFORMATION perfInfo;
    perfInfo.cb = sizeof(perfInfo);
    
    if (GetPerformanceInfo(&perfInfo, sizeof(perfInfo))) {
        m_systemMemory.pageSize = perfInfo.PageSize;
        m_systemMemory.commitPeak = static_cast<qint64>(perfInfo.CommitPeak) * m_systemMemory.pageSize;
        m_systemMemory.systemCache = static_cast<qint64>(perfInfo.SystemCache) * m_systemMemory.pageSize;
        m_systemMemory.kernelPaged = static_cast<qint64>(perfInfo.KernelPaged) * m_systemMemory.pageSize;
        m_systemMemory.kernelNonPaged = static_cast<qint64>(perfInfo.KernelNonpaged) * m_systemMemory.pageSize;
        m_systemMemory.kernelTotal = m_systemMemory.kernelPaged + m_systemMemory.kernelNonPaged;
        m_systemMemory.handleCount = perfInfo.HandleCount;
        m_systemMemory.processCount = perfInfo.ProcessCount;
        m_systemMemory.threadCount = perfInfo.ThreadCount;
    }
    
    // Extended memory info (requires admin for some)
    queryExtendedMemoryInfo();
#endif
}

#ifdef _WIN32
void DetailedMemoryMonitor::queryExtendedMemoryInfo()
{
    // Try to get more detailed memory composition
    // This uses undocumented NtQuerySystemInformation
    
    typedef struct _SYSTEM_FILECACHE_INFORMATION {
        SIZE_T CurrentSize;
        SIZE_T PeakSize;
        ULONG PageFaultCount;
        SIZE_T MinimumWorkingSet;
        SIZE_T MaximumWorkingSet;
        SIZE_T CurrentSizeIncludingTransitionInPages;
        SIZE_T PeakSizeIncludingTransitionInPages;
        ULONG TransitionRePurposeCount;
        ULONG Flags;
    } SYSTEM_FILECACHE_INFORMATION;
    
    // Define NTSTATUS if not defined
    typedef LONG NTSTATUS_LOCAL;
    typedef NTSTATUS_LOCAL (WINAPI *NtQuerySystemInformationFunc)(
        ULONG SystemInformationClass,
        PVOID SystemInformation,
        ULONG SystemInformationLength,
        PULONG ReturnLength
    );
    
    HMODULE hNtdll = GetModuleHandleW(L"ntdll.dll");
    if (!hNtdll) return;
    
    auto NtQuerySystemInformation = reinterpret_cast<NtQuerySystemInformationFunc>(
        GetProcAddress(hNtdll, "NtQuerySystemInformation"));
    
    if (!NtQuerySystemInformation) return;
    
    // SystemFileCacheInformation = 21
    SYSTEM_FILECACHE_INFORMATION cacheInfo{};
    ULONG returnLength = 0;
    
    NTSTATUS_LOCAL status = NtQuerySystemInformation(21, &cacheInfo, sizeof(cacheInfo), &returnLength);
    if (status == 0) {
        m_systemMemory.systemCacheTransition = 
            static_cast<qint64>(cacheInfo.CurrentSizeIncludingTransitionInPages) * m_systemMemory.pageSize;
    }
}
#endif

void DetailedMemoryMonitor::queryProcessMemory()
{
#ifdef _WIN32
    std::vector<ProcessMemoryInfo> newProcesses;
    
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) {
        return;
    }
    
    PROCESSENTRY32W pe;
    pe.dwSize = sizeof(pe);
    
    if (Process32FirstW(snapshot, &pe)) {
        do {
            // Skip system idle process
            if (pe.th32ProcessID == 0) continue;
            
            ProcessMemoryInfo info;
            info.pid = pe.th32ProcessID;
            info.name = QString::fromWCharArray(pe.szExeFile);
            info.lastUpdated = QDateTime::currentDateTime();
            
            // Open process for memory query
            HANDLE hProcess = OpenProcess(
                PROCESS_QUERY_INFORMATION | PROCESS_VM_READ,
                FALSE, pe.th32ProcessID
            );
            
            if (hProcess) {
                queryProcessMemoryInfo(hProcess, info);
                
                // Get executable path
                WCHAR exePath[MAX_PATH];
                DWORD pathSize = MAX_PATH;
                if (QueryFullProcessImageNameW(hProcess, 0, exePath, &pathSize)) {
                    info.executablePath = QString::fromWCharArray(exePath);
                }
                
                // Get process start time
                FILETIME createTime, exitTime, kernelTime, userTime;
                if (GetProcessTimes(hProcess, &createTime, &exitTime, &kernelTime, &userTime)) {
                    ULARGE_INTEGER uli;
                    uli.LowPart = createTime.dwLowDateTime;
                    uli.HighPart = createTime.dwHighDateTime;
                    // Convert FILETIME to QDateTime
                    qint64 msecs = (uli.QuadPart - 116444736000000000ULL) / 10000;
                    info.processStartTime = QDateTime::fromMSecsSinceEpoch(msecs);
                }
                
                CloseHandle(hProcess);
            }
            
            // Calculate deltas and update leak detection
            auto prevIt = m_previousProcesses.find(info.pid);
            if (prevIt != m_previousProcesses.end()) {
                const auto& prev = prevIt->second;
                info.privateBytesDelta = info.privateBytes - prev.privateBytes;
                info.workingSetDelta = info.workingSetSize - prev.workingSetSize;
                info.pageFaultsDelta = info.pageFaultCount - prev.pageFaultCount;
                info.consecutiveGrowthCount = prev.consecutiveGrowthCount;
                info.growthRateMBPerMin = prev.growthRateMBPerMin;
                
                if (m_leakDetectionEnabled) {
                    updateLeakDetection(info);
                }
            }
            
            newProcesses.push_back(std::move(info));
            
        } while (Process32NextW(snapshot, &pe));
    }
    
    CloseHandle(snapshot);
    
    // Update previous processes map
    m_previousProcesses.clear();
    for (const auto& proc : newProcesses) {
        m_previousProcesses[proc.pid] = proc;
    }
    
    m_processes = std::move(newProcesses);
#endif
}

#ifdef _WIN32
bool DetailedMemoryMonitor::queryProcessMemoryInfo(HANDLE hProcess, ProcessMemoryInfo& info)
{
    // Get process memory counters (extended version)
    PROCESS_MEMORY_COUNTERS_EX pmc;
    pmc.cb = sizeof(pmc);
    
    if (GetProcessMemoryInfo(hProcess, reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&pmc), sizeof(pmc))) {
        info.workingSetSize = pmc.WorkingSetSize;
        info.peakWorkingSet = pmc.PeakWorkingSetSize;
        info.pageFaultCount = pmc.PageFaultCount;
        info.privateBytes = pmc.PrivateUsage;
        info.pagedPoolBytes = pmc.QuotaPagedPoolUsage;
        info.nonPagedPoolBytes = pmc.QuotaNonPagedPoolUsage;
    }
    
    // Get virtual memory info
    MEMORY_BASIC_INFORMATION mbi;
    SIZE_T virtualSize = 0;
    SIZE_T privateWS = 0;
    SIZE_T sharedWS = 0;
    
    // Walk the virtual address space
    LPVOID address = nullptr;
    while (VirtualQueryEx(hProcess, address, &mbi, sizeof(mbi))) {
        if (mbi.State == MEM_COMMIT) {
            virtualSize += mbi.RegionSize;
        }
        
        // Move to next region
        address = static_cast<LPBYTE>(mbi.BaseAddress) + mbi.RegionSize;
        
        // Prevent infinite loop
        if (reinterpret_cast<SIZE_T>(address) < reinterpret_cast<SIZE_T>(mbi.BaseAddress)) {
            break;
        }
    }
    
    info.virtualBytes = virtualSize;
    
    // Use Working Set Information for private/shared breakdown
    // This requires PROCESS_QUERY_INFORMATION access
    ULONG wsInfoSize = sizeof(PSAPI_WORKING_SET_INFORMATION) + 
                       sizeof(PSAPI_WORKING_SET_BLOCK) * 1024 * 1024; // Start with 1M entries
    
    auto wsInfo = std::make_unique<BYTE[]>(wsInfoSize);
    auto pWsInfo = reinterpret_cast<PSAPI_WORKING_SET_INFORMATION*>(wsInfo.get());
    
    if (QueryWorkingSet(hProcess, pWsInfo, wsInfoSize)) {
        for (ULONG_PTR i = 0; i < pWsInfo->NumberOfEntries; i++) {
            PSAPI_WORKING_SET_BLOCK block = pWsInfo->WorkingSetInfo[i];
            SIZE_T pageSize = m_systemMemory.pageSize > 0 ? m_systemMemory.pageSize : 4096;
            
            if (block.Shared) {
                sharedWS += pageSize;
            } else {
                privateWS += pageSize;
            }
        }
        
        info.privateWorkingSet = privateWS;
        info.sharedWorkingSet = sharedWS;
    } else {
        // Fallback: estimate private WS as total WS - a typical shared ratio
        info.privateWorkingSet = info.workingSetSize;
        info.sharedWorkingSet = 0;
    }
    
    return true;
}
#endif

void DetailedMemoryMonitor::updateLeakDetection(ProcessMemoryInfo& proc)
{
    // Calculate growth rate in MB per minute
    // Assuming refresh interval of ~2 seconds
    const double intervalMinutes = 2.0 / 60.0; // 2 seconds in minutes
    const double deltaBytes = proc.privateBytesDelta;
    const double deltaMB = deltaBytes / (1024.0 * 1024.0);
    const double currentRate = deltaMB / intervalMinutes;
    
    // Exponential moving average for smoothing
    const double alpha = 0.3;
    if (proc.growthRateMBPerMin == 0.0) {
        proc.growthRateMBPerMin = currentRate;
    } else {
        proc.growthRateMBPerMin = alpha * currentRate + (1.0 - alpha) * proc.growthRateMBPerMin;
    }
    
    // Update consecutive growth counter
    if (proc.privateBytesDelta > 0) {
        proc.consecutiveGrowthCount++;
    } else if (proc.privateBytesDelta < 0) {
        // Reset on shrinkage
        proc.consecutiveGrowthCount = 0;
    }
    
    // Determine if this is a potential leak
    bool wasPotentialLeak = proc.isPotentialLeak;
    proc.isPotentialLeak = (proc.growthRateMBPerMin > m_leakThresholdMBPerMin) &&
                           (proc.consecutiveGrowthCount >= m_minConsecutiveGrowth);
    
    // Emit signal if newly detected
    if (proc.isPotentialLeak && !wasPotentialLeak) {
        emit potentialLeakDetected(proc.pid, proc.name, proc.growthRateMBPerMin);
    }
}

void DetailedMemoryMonitor::takeSnapshot()
{
    MemorySnapshot snapshot;
    snapshot.timestamp = QDateTime::currentDateTime();
    snapshot.usedPhysical = m_systemMemory.usedPhysical;
    snapshot.commitCharge = m_systemMemory.commitTotal;
    snapshot.systemCache = m_systemMemory.systemCache;
    
    for (const auto& proc : m_processes) {
        snapshot.processPrivateBytes[proc.pid] = proc.privateBytes;
    }
    
    m_history.push_back(std::move(snapshot));
    
    // Trim history if needed
    while (static_cast<int>(m_history.size()) > m_maxHistorySize) {
        m_history.pop_front();
    }
}

void DetailedMemoryMonitor::checkSystemMemoryThresholds()
{
    if (m_systemMemory.totalPhysical == 0) return;
    
    double usagePercent = (static_cast<double>(m_systemMemory.usedPhysical) / 
                           m_systemMemory.totalPhysical) * 100.0;
    
    if (usagePercent >= m_lowMemoryThreshold && !m_lowMemoryWarningIssued) {
        m_lowMemoryWarningIssued = true;
        emit systemMemoryLow(usagePercent);
    } else if (usagePercent < m_lowMemoryThreshold - 5.0) {
        // Reset warning with hysteresis
        m_lowMemoryWarningIssued = false;
    }
}

const ProcessMemoryInfo* DetailedMemoryMonitor::getProcessByPid(quint32 pid) const
{
    for (const auto& proc : m_processes) {
        if (proc.pid == pid) {
            return &proc;
        }
    }
    return nullptr;
}

std::vector<ProcessMemoryInfo> DetailedMemoryMonitor::getTopByWorkingSet(int count) const
{
    std::vector<ProcessMemoryInfo> sorted = m_processes;
    std::sort(sorted.begin(), sorted.end(),
              [](const ProcessMemoryInfo& a, const ProcessMemoryInfo& b) {
                  return a.workingSetSize > b.workingSetSize;
              });
    
    if (static_cast<int>(sorted.size()) > count) {
        sorted.resize(count);
    }
    return sorted;
}

std::vector<ProcessMemoryInfo> DetailedMemoryMonitor::getTopByPrivateBytes(int count) const
{
    std::vector<ProcessMemoryInfo> sorted = m_processes;
    std::sort(sorted.begin(), sorted.end(),
              [](const ProcessMemoryInfo& a, const ProcessMemoryInfo& b) {
                  return a.privateBytes > b.privateBytes;
              });
    
    if (static_cast<int>(sorted.size()) > count) {
        sorted.resize(count);
    }
    return sorted;
}

std::vector<ProcessMemoryInfo> DetailedMemoryMonitor::getPotentialLeaks() const
{
    std::vector<ProcessMemoryInfo> leaks;
    for (const auto& proc : m_processes) {
        if (proc.isPotentialLeak) {
            leaks.push_back(proc);
        }
    }
    
    // Sort by growth rate (highest first)
    std::sort(leaks.begin(), leaks.end(),
              [](const ProcessMemoryInfo& a, const ProcessMemoryInfo& b) {
                  return a.growthRateMBPerMin > b.growthRateMBPerMin;
              });
    
    return leaks;
}

void DetailedMemoryMonitor::setMaxHistorySize(int size)
{
    m_maxHistorySize = size;
    while (static_cast<int>(m_history.size()) > m_maxHistorySize) {
        m_history.pop_front();
    }
}

void DetailedMemoryMonitor::clearHistory()
{
    m_history.clear();
}

void DetailedMemoryMonitor::setLeakDetectionEnabled(bool enabled)
{
    m_leakDetectionEnabled = enabled;
}

void DetailedMemoryMonitor::setLeakThresholdMBPerMin(double threshold)
{
    m_leakThresholdMBPerMin = threshold;
}

void DetailedMemoryMonitor::setMinConsecutiveGrowth(int count)
{
    m_minConsecutiveGrowth = count;
}

QString DetailedMemoryMonitor::formatBytes(qint64 bytes)
{
    if (bytes < 0) return "-";
    
    const double KB = 1024.0;
    const double MB = KB * 1024.0;
    const double GB = MB * 1024.0;
    const double TB = GB * 1024.0;
    
    if (bytes >= TB) {
        return QString("%1 TB").arg(bytes / TB, 0, 'f', 2);
    } else if (bytes >= GB) {
        return QString("%1 GB").arg(bytes / GB, 0, 'f', 2);
    } else if (bytes >= MB) {
        return QString("%1 MB").arg(bytes / MB, 0, 'f', 1);
    } else if (bytes >= KB) {
        return QString("%1 KB").arg(bytes / KB, 0, 'f', 0);
    }
    return QString("%1 B").arg(bytes);
}

QString DetailedMemoryMonitor::formatBytesShort(qint64 bytes)
{
    if (bytes < 0) return "-";
    
    const double KB = 1024.0;
    const double MB = KB * 1024.0;
    const double GB = MB * 1024.0;
    
    if (bytes >= GB) {
        return QString("%1G").arg(bytes / GB, 0, 'f', 1);
    } else if (bytes >= MB) {
        return QString("%1M").arg(bytes / MB, 0, 'f', 0);
    } else if (bytes >= KB) {
        return QString("%1K").arg(bytes / KB, 0, 'f', 0);
    }
    return QString("%1B").arg(bytes);
}
