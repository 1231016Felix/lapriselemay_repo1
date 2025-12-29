#include "processimpactmonitor.h"

#include <QDebug>
#include <QFileInfo>
#include <QSettings>
#include <algorithm>
#include <numeric>

#ifdef _WIN32
#include <Windows.h>
#include <Psapi.h>
#include <TlHelp32.h>
#include <Pdh.h>
#include <PdhMsg.h>
#include <iphlpapi.h>
#include <winternl.h>
#pragma comment(lib, "pdh.lib")
#pragma comment(lib, "iphlpapi.lib")
#endif

ProcessImpactMonitor::ProcessImpactMonitor(QObject* parent)
    : QObject(parent)
    , m_sampleTimer(new QTimer(this))
{
    connect(m_sampleTimer, &QTimer::timeout, this, &ProcessImpactMonitor::onSampleTimer);
    initializeBatteryDetection();
}

ProcessImpactMonitor::~ProcessImpactMonitor()
{
    stop();
}

void ProcessImpactMonitor::start(int intervalMs)
{
    if (m_isRunning) return;
    
    if (intervalMs > 0) {
        m_config.sampleIntervalMs = intervalMs;
    }
    
    m_isRunning = true;
    m_startTime = QDateTime::currentDateTime();
    m_sampleTimer->start(m_config.sampleIntervalMs);
    
    // Don't do initial sample here - let the timer trigger it
    // This avoids race conditions with UI initialization
}

void ProcessImpactMonitor::stop()
{
    m_isRunning = false;
    m_sampleTimer->stop();
}

void ProcessImpactMonitor::initializeBatteryDetection()
{
#ifdef _WIN32
    SYSTEM_POWER_STATUS powerStatus;
    if (GetSystemPowerStatus(&powerStatus)) {
        m_hasBattery = (powerStatus.BatteryFlag != 128); // 128 = No battery
    }
#endif
}

std::vector<ProcessImpact> ProcessImpactMonitor::getAllProcesses() const
{
    QMutexLocker locker(&m_mutex);
    std::vector<ProcessImpact> result;
    result.reserve(m_processes.size());
    
    for (const auto& [pid, impact] : m_processes) {
        if (impact.isRunning) {
            // Create a copy without the samples history
            ProcessImpact copy = impact;
            copy.samples.clear();
            result.push_back(std::move(copy));
        }
    }
    
    return result;
}

std::vector<ProcessImpact> ProcessImpactMonitor::getTopProcesses(ImpactCategory category, int count, bool includeSystem) const
{
    auto processes = getAllImpacts(includeSystem);
    
    // Sort by category
    switch (category) {
        case ImpactCategory::CpuHog:
        case ImpactCategory::CpuUsage:
            std::sort(processes.begin(), processes.end(), compareByCpu);
            break;
        case ImpactCategory::MemoryHog:
        case ImpactCategory::MemoryUsage:
            std::sort(processes.begin(), processes.end(), compareByMemory);
            break;
        case ImpactCategory::DiskHog:
        case ImpactCategory::DiskIO:
        case ImpactCategory::DiskRead:
        case ImpactCategory::DiskWrite:
            std::sort(processes.begin(), processes.end(), compareByDisk);
            break;
        case ImpactCategory::NetworkHog:
        case ImpactCategory::NetworkUsage:
            std::sort(processes.begin(), processes.end(), compareByNetwork);
            break;
        case ImpactCategory::BatteryDrainer:
        case ImpactCategory::BatteryDrain:
            std::sort(processes.begin(), processes.end(), compareByBattery);
            break;
        case ImpactCategory::GpuUsage:
        case ImpactCategory::OverallImpact:
        default:
            std::sort(processes.begin(), processes.end(), compareByOverall);
            break;
    }
    
    // Take top N
    if (static_cast<int>(processes.size()) > count) {
        processes.resize(count);
    }
    
    return processes;
}

std::optional<ProcessImpact> ProcessImpactMonitor::getProcessImpact(DWORD pid) const
{
    QMutexLocker locker(&m_mutex);
    auto it = m_processes.find(pid);
    if (it != m_processes.end()) {
        return it->second;
    }
    return std::nullopt;
}

std::vector<ProcessSample> ProcessImpactMonitor::getProcessHistory(DWORD pid) const
{
    QMutexLocker locker(&m_mutex);
    auto it = m_processes.find(pid);
    if (it != m_processes.end()) {
        return std::vector<ProcessSample>(it->second.samples.begin(), it->second.samples.end());
    }
    return {};
}

void ProcessImpactMonitor::clearHistory()
{
    QMutexLocker locker(&m_mutex);
    m_processes.clear();
    m_prevData.clear();
}

void ProcessImpactMonitor::refresh()
{
    onSampleTimer();
}

void ProcessImpactMonitor::recalculateImpacts()
{
    QMutexLocker locker(&m_mutex);
    for (auto& [pid, impact] : m_processes) {
        calculateImpactScores(impact);
    }
    emit dataUpdated();
    emit impactsUpdated();
}

void ProcessImpactMonitor::setAnalysisWindow(int seconds)
{
    m_analysisWindowSecs = seconds;
    m_config.historyMinutes = seconds / 60;
    if (m_config.historyMinutes < 1) m_config.historyMinutes = 1;
}

double ProcessImpactMonitor::windowCoverage() const
{
    if (!m_startTime.isValid()) return 0.0;
    
    qint64 elapsedSecs = m_startTime.secsTo(QDateTime::currentDateTime());
    if (elapsedSecs <= 0) return 0.0;
    
    return std::min(1.0, static_cast<double>(elapsedSecs) / m_analysisWindowSecs);
}

std::vector<ProcessImpact> ProcessImpactMonitor::getAllImpacts(bool includeSystem) const
{
    QMutexLocker locker(&m_mutex);
    std::vector<ProcessImpact> result;
    result.reserve(m_processes.size());
    
    for (const auto& [pid, impact] : m_processes) {
        if (impact.isRunning) {
            if (includeSystem || !impact.isSystemProcess) {
                // Create a copy without the samples history to reduce memory/copy overhead
                ProcessImpact copy = impact;
                copy.samples.clear();  // Don't copy samples - they're only for internal use
                result.push_back(std::move(copy));
            }
        }
    }
    
    return result;
}

std::vector<ProcessImpact> ProcessImpactMonitor::getImpactsSorted(ImpactCategory category, bool ascending, bool includeSystem) const
{
    auto processes = getAllImpacts(includeSystem);
    
    // Sort by category
    switch (category) {
        case ImpactCategory::CpuHog:
        case ImpactCategory::CpuUsage:
            std::sort(processes.begin(), processes.end(), compareByCpu);
            break;
        case ImpactCategory::MemoryHog:
        case ImpactCategory::MemoryUsage:
            std::sort(processes.begin(), processes.end(), compareByMemory);
            break;
        case ImpactCategory::DiskHog:
        case ImpactCategory::DiskIO:
        case ImpactCategory::DiskRead:
        case ImpactCategory::DiskWrite:
            std::sort(processes.begin(), processes.end(), compareByDisk);
            break;
        case ImpactCategory::NetworkHog:
        case ImpactCategory::NetworkUsage:
            std::sort(processes.begin(), processes.end(), compareByNetwork);
            break;
        case ImpactCategory::BatteryDrainer:
        case ImpactCategory::BatteryDrain:
            std::sort(processes.begin(), processes.end(), compareByBattery);
            break;
        case ImpactCategory::OverallImpact:
        default:
            std::sort(processes.begin(), processes.end(), compareByOverall);
            break;
    }
    
    if (ascending) {
        std::reverse(processes.begin(), processes.end());
    }
    
    return processes;
}

void ProcessImpactMonitor::onSampleTimer()
{
    try {
        sampleAllProcesses();
        pruneOldSamples();
        pruneDeadProcesses();
        m_totalSamples++;
    } catch (...) {
        qWarning() << "Exception in ProcessImpactMonitor::onSampleTimer";
        return;
    }
    
    emit dataUpdated();
    emit impactsUpdated();
}

void ProcessImpactMonitor::sampleAllProcesses()
{
#ifdef _WIN32
    QMutexLocker locker(&m_mutex);
    
    // Get current system time for CPU calculation
    FILETIME idleTime, kernelTime, userTime;
    if (!GetSystemTimes(&idleTime, &kernelTime, &userTime)) {
        return;
    }
    
    // Correctly combine kernel and user time
    ULARGE_INTEGER kernel, user;
    kernel.LowPart = kernelTime.dwLowDateTime;
    kernel.HighPart = kernelTime.dwHighDateTime;
    user.LowPart = userTime.dwLowDateTime;
    user.HighPart = userTime.dwHighDateTime;
    
    ULARGE_INTEGER currentSystemTime;
    currentSystemTime.QuadPart = kernel.QuadPart + user.QuadPart;
    
    // Mark all processes as potentially dead
    for (auto& [pid, impact] : m_processes) {
        impact.isRunning = false;
    }
    
    // Enumerate all processes
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) return;
    
    PROCESSENTRY32W pe32;
    pe32.dwSize = sizeof(pe32);
    
    int processCount = 0;
    const int maxProcessesPerCycle = 50;  // Limit to avoid blocking UI
    
    if (Process32FirstW(snapshot, &pe32)) {
        do {
            // Limit number of processes per cycle
            if (processCount >= maxProcessesPerCycle) {
                break;
            }
            
            DWORD pid = pe32.th32ProcessID;
            if (pid == 0) continue; // Skip System Idle Process
            
            QString processName = QString::fromWCharArray(pe32.szExeFile);
            
            // Skip system processes if configured
            if (!m_config.trackSystemProcesses && isSystemProcess(processName, QString())) {
                continue;
            }
            
            // Open process for querying
            HANDLE hProcess = OpenProcess(
                PROCESS_QUERY_INFORMATION | PROCESS_VM_READ,
                FALSE, pid
            );
            
            if (!hProcess) continue;
            
            // Get or create impact record
            auto& impact = m_processes[pid];
            impact.pid = pid;
            impact.name = processName;
            impact.isRunning = true;
            impact.lastSeen = QDateTime::currentDateTime();
            
            if (impact.firstSeen.isNull()) {
                impact.firstSeen = impact.lastSeen;
                
                // Get executable path - simplified to avoid potential crashes
                wchar_t pathBuffer[MAX_PATH] = {0};
                DWORD pathLen = GetModuleFileNameExW(hProcess, NULL, pathBuffer, MAX_PATH);
                if (pathLen > 0 && pathLen < MAX_PATH) {
                    impact.executablePath = QString::fromWCharArray(pathBuffer);
                    impact.isSystem = isSystemProcess(processName, impact.executablePath);
                    // Skip getProcessDescription for now - can cause crashes
                    // impact.description = getProcessDescription(impact.executablePath);
                }
                
                // Skip isBackgroundProcess for now - EnumWindows can be slow/problematic
                impact.isBackground = false;
            }
            
            // Create new sample
            ProcessSample sample;
            sample.timestamp = QDateTime::currentDateTime();
            
            // Memory usage
            PROCESS_MEMORY_COUNTERS_EX pmc;
            if (GetProcessMemoryInfo(hProcess, (PROCESS_MEMORY_COUNTERS*)&pmc, sizeof(pmc))) {
                sample.memoryBytes = pmc.WorkingSetSize;
                impact.currentMemoryBytes = sample.memoryBytes;
                if (sample.memoryBytes > impact.peakMemoryBytes) {
                    impact.peakMemoryBytes = sample.memoryBytes;
                }
            }
            
            // CPU usage
            FILETIME createTime, exitTime, kernelTimeFt, userTimeFt;
            if (GetProcessTimes(hProcess, &createTime, &exitTime, &kernelTimeFt, &userTimeFt)) {
                ULARGE_INTEGER procTime;
                procTime.LowPart = kernelTimeFt.dwLowDateTime + userTimeFt.dwLowDateTime;
                procTime.HighPart = kernelTimeFt.dwHighDateTime + userTimeFt.dwHighDateTime;
                
                auto prevIt = m_prevData.find(pid);
                if (prevIt != m_prevData.end() && m_prevSystemTime.QuadPart > 0) {
                    ULONGLONG procDelta = procTime.QuadPart - prevIt->second.cpuTime.QuadPart;
                    ULONGLONG sysDelta = currentSystemTime.QuadPart - m_prevSystemTime.QuadPart;
                    
                    if (sysDelta > 0) {
                        // Get number of processors
                        SYSTEM_INFO sysInfo;
                        GetSystemInfo(&sysInfo);
                        int numProcessors = sysInfo.dwNumberOfProcessors;
                        
                        sample.cpuPercent = (100.0 * procDelta) / (sysDelta * numProcessors);
                        sample.cpuPercent = std::min(sample.cpuPercent, 100.0);
                        
                        if (sample.cpuPercent > impact.peakCpuPercent) {
                            impact.peakCpuPercent = sample.cpuPercent;
                        }
                        
                        if (sample.cpuPercent > m_config.cpuSpikeThreshold) {
                            impact.cpuSpikeCount++;
                        }
                    }
                }
                
                // Store for next calculation
                m_prevData[pid].cpuTime = procTime;
                m_prevData[pid].timestamp = sample.timestamp;
            }
            
            // I/O counters (disk)
            IO_COUNTERS ioCounters;
            if (GetProcessIoCounters(hProcess, &ioCounters)) {
                auto prevIt = m_prevData.find(pid);
                if (prevIt != m_prevData.end()) {
                    qint64 readDelta = ioCounters.ReadTransferCount - prevIt->second.diskRead;
                    qint64 writeDelta = ioCounters.WriteTransferCount - prevIt->second.diskWrite;
                    
                    double elapsedSec = prevIt->second.timestamp.msecsTo(sample.timestamp) / 1000.0;
                    if (elapsedSec > 0) {
                        sample.diskReadBytes = static_cast<qint64>(readDelta / elapsedSec);
                        sample.diskWriteBytes = static_cast<qint64>(writeDelta / elapsedSec);
                        
                        impact.totalDiskReadBytes += readDelta;
                        impact.totalDiskWriteBytes += writeDelta;
                        
                        if (sample.diskReadBytes > impact.peakDiskReadBytesPerSec) {
                            impact.peakDiskReadBytesPerSec = sample.diskReadBytes;
                        }
                        if (sample.diskWriteBytes > impact.peakDiskWriteBytesPerSec) {
                            impact.peakDiskWriteBytesPerSec = sample.diskWriteBytes;
                        }
                    }
                }
                
                m_prevData[pid].diskRead = ioCounters.ReadTransferCount;
                m_prevData[pid].diskWrite = ioCounters.WriteTransferCount;
            }
            
            // Add sample to history
            impact.samples.push_back(sample);
            
            // Calculate running averages
            if (!impact.samples.empty()) {
                double cpuSum = 0;
                qint64 memSum = 0;
                qint64 diskReadSum = 0;
                qint64 diskWriteSum = 0;
                
                for (const auto& s : impact.samples) {
                    cpuSum += s.cpuPercent;
                    memSum += s.memoryBytes;
                    diskReadSum += s.diskReadBytes;
                    diskWriteSum += s.diskWriteBytes;
                }
                
                size_t count = impact.samples.size();
                impact.avgCpuPercent = cpuSum / count;
                impact.avgMemoryBytes = memSum / static_cast<qint64>(count);
                impact.avgDiskReadBytesPerSec = diskReadSum / static_cast<qint64>(count);
                impact.avgDiskWriteBytesPerSec = diskWriteSum / static_cast<qint64>(count);
            }
            
            // Calculate impact scores
            calculateImpactScores(impact);
            
            CloseHandle(hProcess);
            processCount++;
            
        } while (Process32NextW(snapshot, &pe32));
    }
    
    CloseHandle(snapshot);
    
    m_prevSystemTime = currentSystemTime;
#endif
}

void ProcessImpactMonitor::calculateImpactScores(ProcessImpact& impact)
{
    impact.batteryImpactScore = calculateBatteryImpact(impact);
    impact.overallImpactScore = calculateOverallImpact(impact);
    
    // Calculate disk impact score (0-100)
    double diskRate = (impact.avgDiskReadBytesPerSec + impact.avgDiskWriteBytesPerSec) / 1024.0 / 1024.0;
    impact.diskImpactScore = std::min(diskRate * 2.0, 100.0);
    
    // Synchronize alias fields for backward compatibility
    impact.totalReadBytes = impact.totalDiskReadBytes;
    impact.totalWriteBytes = impact.totalDiskWriteBytes;
    impact.avgReadBytesPerSec = impact.avgDiskReadBytesPerSec;
    impact.avgWriteBytesPerSec = impact.avgDiskWriteBytesPerSec;
    impact.peakReadBytesPerSec = impact.peakDiskReadBytesPerSec;
    impact.peakWriteBytesPerSec = impact.peakDiskWriteBytesPerSec;
    impact.totalCpuSeconds = impact.totalCpuTimeSeconds;
    impact.isSystemProcess = impact.isSystem;
}

double ProcessImpactMonitor::calculateBatteryImpact(const ProcessImpact& impact)
{
    // Battery impact is a weighted score based on:
    // - CPU usage (highest weight - CPU is biggest battery drain)
    // - Disk I/O (medium weight)
    // - Memory (lower weight)
    // - Network (medium weight - uses WiFi radio)
    
    double cpuScore = std::min(impact.avgCpuPercent * 2.0, 100.0);  // Weight: 40%
    
    // Disk: normalize to ~10MB/s as "heavy"
    double diskRate = (impact.avgDiskReadBytesPerSec + impact.avgDiskWriteBytesPerSec) / 1024.0 / 1024.0;
    double diskScore = std::min(diskRate * 10.0, 100.0);  // Weight: 25%
    
    // Memory: normalize to ~1GB as "heavy"
    double memGB = impact.currentMemoryBytes / 1024.0 / 1024.0 / 1024.0;
    double memScore = std::min(memGB * 20.0, 100.0);  // Weight: 15%
    
    // Network: normalize to ~1MB/s as "heavy"
    double netRate = impact.avgNetworkBytesPerSec / 1024.0 / 1024.0;
    double netScore = std::min(netRate * 100.0, 100.0);  // Weight: 20%
    
    double batteryScore = (cpuScore * 0.40) + (diskScore * 0.25) + (memScore * 0.15) + (netScore * 0.20);
    
    // Bonus penalty for frequent CPU spikes (wakes CPU from low power states)
    if (impact.cpuSpikeCount > 10) {
        batteryScore = std::min(batteryScore + 10.0, 100.0);
    }
    
    return batteryScore;
}

double ProcessImpactMonitor::calculateOverallImpact(const ProcessImpact& impact)
{
    // Overall impact considers all resources equally
    double cpuScore = impact.avgCpuPercent;
    
    // Memory: normalize to 4GB as heavy
    double memGB = impact.currentMemoryBytes / 1024.0 / 1024.0 / 1024.0;
    double memScore = std::min(memGB * 25.0, 100.0);
    
    // Disk: normalize to 50MB/s as heavy
    double diskRate = (impact.avgDiskReadBytesPerSec + impact.avgDiskWriteBytesPerSec) / 1024.0 / 1024.0;
    double diskScore = std::min(diskRate * 2.0, 100.0);
    
    // Network: normalize to 10MB/s as heavy
    double netRate = impact.avgNetworkBytesPerSec / 1024.0 / 1024.0;
    double netScore = std::min(netRate * 10.0, 100.0);
    
    return (cpuScore + memScore + diskScore + netScore) / 4.0;
}

void ProcessImpactMonitor::pruneOldSamples()
{
    QMutexLocker locker(&m_mutex);
    QDateTime cutoff = QDateTime::currentDateTime().addSecs(-m_config.historyMinutes * 60);
    
    for (auto& [pid, impact] : m_processes) {
        while (!impact.samples.empty() && impact.samples.front().timestamp < cutoff) {
            impact.samples.pop_front();
        }
    }
}

void ProcessImpactMonitor::pruneDeadProcesses()
{
    QMutexLocker locker(&m_mutex);
    QDateTime cutoff = QDateTime::currentDateTime().addSecs(-60);  // Keep dead processes for 1 minute
    
    auto it = m_processes.begin();
    while (it != m_processes.end()) {
        if (!it->second.isRunning && it->second.lastSeen < cutoff) {
            m_prevData.erase(it->first);
            it = m_processes.erase(it);
        } else {
            ++it;
        }
    }
}

bool ProcessImpactMonitor::isSystemProcess(const QString& name, const QString& path)
{
    static const QStringList systemProcesses = {
        "System", "Registry", "smss.exe", "csrss.exe", "wininit.exe",
        "services.exe", "lsass.exe", "svchost.exe", "dwm.exe",
        "fontdrvhost.exe", "winlogon.exe", "LogonUI.exe", "sihost.exe",
        "taskhostw.exe", "explorer.exe", "ShellExperienceHost.exe",
        "SearchHost.exe", "StartMenuExperienceHost.exe", "RuntimeBroker.exe",
        "dllhost.exe", "conhost.exe", "SecurityHealthService.exe",
        "MsMpEng.exe", "NisSrv.exe", "SearchIndexer.exe",
        "spoolsv.exe", "WmiPrvSE.exe", "audiodg.exe"
    };
    
    if (systemProcesses.contains(name, Qt::CaseInsensitive)) {
        return true;
    }
    
    if (!path.isEmpty()) {
        QString lowerPath = path.toLower();
        if (lowerPath.contains("\\windows\\") || lowerPath.contains("\\system32\\")) {
            return true;
        }
    }
    
    return false;
}

bool ProcessImpactMonitor::isBackgroundProcess(DWORD pid)
{
#ifdef _WIN32
    // Check if process has visible windows
    struct EnumData {
        DWORD pid;
        bool hasWindow;
    } data = { pid, false };
    
    EnumWindows([](HWND hwnd, LPARAM lParam) -> BOOL {
        auto* data = reinterpret_cast<EnumData*>(lParam);
        DWORD windowPid;
        GetWindowThreadProcessId(hwnd, &windowPid);
        if (windowPid == data->pid && IsWindowVisible(hwnd)) {
            data->hasWindow = true;
            return FALSE;
        }
        return TRUE;
    }, reinterpret_cast<LPARAM>(&data));
    
    return !data.hasWindow;
#else
    return false;
#endif
}

QString ProcessImpactMonitor::getProcessDescription(const QString& path)
{
#ifdef _WIN32
    if (path.isEmpty()) return QString();
    
    DWORD handle = 0;
    DWORD size = GetFileVersionInfoSizeW(path.toStdWString().c_str(), &handle);
    if (size == 0) return QString();
    
    std::vector<BYTE> buffer(size);
    if (!GetFileVersionInfoW(path.toStdWString().c_str(), handle, size, buffer.data())) {
        return QString();
    }
    
    struct LANGANDCODEPAGE {
        WORD wLanguage;
        WORD wCodePage;
    } *lpTranslate;
    
    UINT cbTranslate;
    if (!VerQueryValueW(buffer.data(), L"\\VarFileInfo\\Translation",
                        (LPVOID*)&lpTranslate, &cbTranslate)) {
        return QString();
    }
    
    if (cbTranslate < sizeof(LANGANDCODEPAGE)) return QString();
    
    wchar_t subBlock[256];
    swprintf_s(subBlock, L"\\StringFileInfo\\%04x%04x\\FileDescription",
               lpTranslate[0].wLanguage, lpTranslate[0].wCodePage);
    
    wchar_t* description = nullptr;
    UINT descLen = 0;
    if (VerQueryValueW(buffer.data(), subBlock, (LPVOID*)&description, &descLen)) {
        return QString::fromWCharArray(description, descLen - 1);
    }
#endif
    return QString();
}

QString ProcessImpactMonitor::formatBytes(qint64 bytes)
{
    if (bytes < 0) bytes = 0;
    
    const qint64 KB = 1024;
    const qint64 MB = KB * 1024;
    const qint64 GB = MB * 1024;
    const qint64 TB = GB * 1024;
    
    if (bytes >= TB) {
        return QString("%1 TB").arg(static_cast<double>(bytes) / TB, 0, 'f', 2);
    } else if (bytes >= GB) {
        return QString("%1 GB").arg(static_cast<double>(bytes) / GB, 0, 'f', 2);
    } else if (bytes >= MB) {
        return QString("%1 MB").arg(static_cast<double>(bytes) / MB, 0, 'f', 1);
    } else if (bytes >= KB) {
        return QString("%1 KB").arg(static_cast<double>(bytes) / KB, 0, 'f', 1);
    } else {
        return QString("%1 B").arg(bytes);
    }
}

QString ProcessImpactMonitor::formatBytesPerSec(qint64 bytesPerSec)
{
    return formatBytes(bytesPerSec) + "/s";
}

// Comparators for sorting
bool ProcessImpactMonitor::compareByCpu(const ProcessImpact& a, const ProcessImpact& b)
{
    return a.avgCpuPercent > b.avgCpuPercent;
}

bool ProcessImpactMonitor::compareByMemory(const ProcessImpact& a, const ProcessImpact& b)
{
    return a.currentMemoryBytes > b.currentMemoryBytes;
}

bool ProcessImpactMonitor::compareByDisk(const ProcessImpact& a, const ProcessImpact& b)
{
    qint64 aDisk = a.avgDiskReadBytesPerSec + a.avgDiskWriteBytesPerSec;
    qint64 bDisk = b.avgDiskReadBytesPerSec + b.avgDiskWriteBytesPerSec;
    return aDisk > bDisk;
}

bool ProcessImpactMonitor::compareByNetwork(const ProcessImpact& a, const ProcessImpact& b)
{
    return a.avgNetworkBytesPerSec > b.avgNetworkBytesPerSec;
}

bool ProcessImpactMonitor::compareByBattery(const ProcessImpact& a, const ProcessImpact& b)
{
    return a.batteryImpactScore > b.batteryImpactScore;
}

bool ProcessImpactMonitor::compareByOverall(const ProcessImpact& a, const ProcessImpact& b)
{
    return a.overallImpactScore > b.overallImpactScore;
}
