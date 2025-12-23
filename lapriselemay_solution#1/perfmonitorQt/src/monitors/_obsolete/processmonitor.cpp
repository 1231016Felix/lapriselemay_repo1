#include "processmonitor.h"

#ifdef _WIN32
#include <TlHelp32.h>
#include <Psapi.h>
#include <ShlObj.h>
#pragma comment(lib, "psapi.lib")
#pragma comment(lib, "version.lib")
#endif

#include <QFileInfo>
#include <QFileIconProvider>
#include <QFont>
#include <QColor>
#include <algorithm>
#include <map>
#include <functional>

// ============================================================================
// ProcessTreeModel implementation
// ============================================================================

ProcessTreeModel::ProcessTreeModel(QObject *parent)
    : QAbstractItemModel(parent)
{
}

void ProcessTreeModel::setProcesses(const std::vector<ProcessInfo>& processes)
{
    beginResetModel();
    m_allProcesses = processes;
    buildGroups();
    endResetModel();
}

void ProcessTreeModel::updateProcesses(const std::vector<ProcessInfo>& processes)
{
    // Store old group count to detect structural changes
    int oldGroupCount = static_cast<int>(m_groups.size());
    
    // Store old group sizes for children comparison
    std::vector<int> oldGroupSizes;
    oldGroupSizes.reserve(m_groups.size());
    for (const auto& group : m_groups) {
        oldGroupSizes.push_back(static_cast<int>(group.processes.size()));
    }
    
    // Build a map of old PID -> row position for selection preservation
    std::map<quint32, std::pair<int, int>> oldPidPositions; // pid -> (groupIdx, procIdx)
    for (int gIdx = 0; gIdx < static_cast<int>(m_groups.size()); ++gIdx) {
        const auto& group = m_groups[gIdx];
        if (group.processes.size() == 1) {
            oldPidPositions[group.processes[0].pid] = {gIdx, -1}; // -1 means top-level
        } else {
            for (int pIdx = 0; pIdx < static_cast<int>(group.processes.size()); ++pIdx) {
                oldPidPositions[group.processes[pIdx].pid] = {gIdx, pIdx};
            }
        }
    }
    
    // Update data
    m_allProcesses = processes;
    buildGroups();
    
    int newGroupCount = static_cast<int>(m_groups.size());
    
    // If structure changed significantly, we need a layout change
    // This happens when processes are added/removed
    bool structureChanged = (oldGroupCount != newGroupCount);
    
    // Also check if any group's child count changed
    if (!structureChanged && m_grouped) {
        for (size_t i = 0; i < m_groups.size() && i < oldGroupSizes.size(); ++i) {
            if (static_cast<int>(m_groups[i].processes.size()) != oldGroupSizes[i]) {
                structureChanged = true;
                break;
            }
        }
    }
    
    if (structureChanged) {
        // Use layoutChanged instead of modelReset - it preserves selection better
        emit layoutAboutToBeChanged();
        emit layoutChanged();
    } else {
        // Just update data values without changing structure
        if (!m_groups.empty()) {
            QModelIndex topLeft = index(0, 0);
            QModelIndex bottomRight = index(newGroupCount - 1, ColCount - 1);
            emit dataChanged(topLeft, bottomRight, {Qt::DisplayRole, Qt::UserRole});
            
            // Also update children for expanded groups
            for (int groupIdx = 0; groupIdx < newGroupCount; ++groupIdx) {
                const auto& group = m_groups[groupIdx];
                if (group.processes.size() > 1 && m_grouped) {
                    QModelIndex parentIdx = index(groupIdx, 0);
                    QModelIndex childTopLeft = index(0, 0, parentIdx);
                    QModelIndex childBottomRight = index(static_cast<int>(group.processes.size()) - 1, ColCount - 1, parentIdx);
                    emit dataChanged(childTopLeft, childBottomRight, {Qt::DisplayRole, Qt::UserRole});
                }
            }
        }
    }
}

void ProcessTreeModel::setGrouped(bool grouped)
{
    if (m_grouped != grouped) {
        beginResetModel();
        m_grouped = grouped;
        buildGroups();
        endResetModel();
    }
}

void ProcessTreeModel::buildGroups()
{
    m_groups.clear();
    
    if (!m_grouped) {
        // No grouping - each process is its own "group"
        for (const auto& proc : m_allProcesses) {
            AppGroup group;
            group.name = proc.name;
            group.displayName = proc.displayName.isEmpty() ? proc.name : proc.displayName;
            group.processes.push_back(proc);
            group.recalculate();
            m_groups.push_back(std::move(group));
        }
    } else {
        // Group by executable name (without extension)
        std::map<QString, AppGroup> groupMap;
        
        for (const auto& proc : m_allProcesses) {
            QString key = proc.name.toLower();
            // Remove .exe extension for grouping
            if (key.endsWith(".exe")) {
                key = key.left(key.length() - 4);
            }
            
            auto& group = groupMap[key];
            if (group.name.isEmpty()) {
                group.name = proc.name;
                group.displayName = proc.displayName.isEmpty() ? proc.name : proc.displayName;
            }
            group.processes.push_back(proc);
        }
        
        // Convert map to vector and calculate totals
        for (auto& [key, group] : groupMap) {
            group.recalculate();
            m_groups.push_back(std::move(group));
        }
        
        // Sort groups by memory usage (descending)
        std::sort(m_groups.begin(), m_groups.end(),
            [](const AppGroup& a, const AppGroup& b) {
                return a.totalMemoryBytes > b.totalMemoryBytes;
            });
    }
}

QModelIndex ProcessTreeModel::index(int row, int column, const QModelIndex &parent) const
{
    if (!hasIndex(row, column, parent))
        return QModelIndex();

    if (!parent.isValid()) {
        // Top level - app groups
        if (row < static_cast<int>(m_groups.size())) {
            return createIndex(row, column, quintptr(-1)); // -1 means top level
        }
    } else {
        // Child level - individual processes
        int groupIndex = parent.row();
        if (groupIndex < static_cast<int>(m_groups.size())) {
            const auto& group = m_groups[groupIndex];
            if (row < static_cast<int>(group.processes.size())) {
                return createIndex(row, column, quintptr(groupIndex));
            }
        }
    }
    return QModelIndex();
}

QModelIndex ProcessTreeModel::parent(const QModelIndex &index) const
{
    if (!index.isValid())
        return QModelIndex();

    quintptr id = index.internalId();
    if (id == quintptr(-1)) {
        // Top level item has no parent
        return QModelIndex();
    }
    
    // Child item - parent is the group
    return createIndex(static_cast<int>(id), 0, quintptr(-1));
}

int ProcessTreeModel::rowCount(const QModelIndex &parent) const
{
    if (!parent.isValid()) {
        // Root - return number of groups
        return static_cast<int>(m_groups.size());
    }
    
    if (parent.internalId() == quintptr(-1) && m_grouped) {
        // Group item - return number of processes (only if grouped and has multiple)
        int groupIndex = parent.row();
        if (groupIndex < static_cast<int>(m_groups.size())) {
            const auto& group = m_groups[groupIndex];
            // Only show children if there's more than 1 process
            return group.processes.size() > 1 ? static_cast<int>(group.processes.size()) : 0;
        }
    }
    
    return 0;
}

int ProcessTreeModel::columnCount(const QModelIndex&) const
{
    return ColCount;
}

QVariant ProcessTreeModel::data(const QModelIndex &index, int role) const
{
    if (!index.isValid())
        return QVariant();

    quintptr id = index.internalId();
    
    if (id == quintptr(-1)) {
        // Top level - app group
        int groupIndex = index.row();
        if (groupIndex >= static_cast<int>(m_groups.size()))
            return QVariant();
        
        const auto& group = m_groups[groupIndex];
        
        if (role == Qt::DisplayRole) {
            switch (index.column()) {
                case ColName: 
                    if (group.processCount > 1 && m_grouped) {
                        return QString("%1 (%2)").arg(group.displayName).arg(group.processCount);
                    }
                    return group.displayName;
                case ColPID:
                    if (group.processCount == 1) {
                        return group.processes[0].pid;
                    }
                    return QVariant();
                case ColCPU: 
                    return QString("%1%").arg(group.totalCpuUsage, 0, 'f', 1);
                case ColMemory: 
                    return formatBytes(group.totalMemoryBytes);
                case ColThreads: 
                    return group.totalThreads;
                case ColStatus:
                    if (group.processCount == 1) {
                        return group.processes[0].status;
                    }
                    return QVariant();
            }
        }
        else if (role == Qt::DecorationRole && index.column() == ColName) {
            // Return cached icon or default
            return group.icon;
        }
        else if (role == Qt::FontRole && group.processCount > 1) {
            QFont font;
            font.setBold(true);
            return font;
        }
        else if (role == Qt::TextAlignmentRole) {
            if (index.column() >= ColPID && index.column() <= ColThreads)
                return Qt::AlignRight;
        }
        else if (role == Qt::UserRole) {
            // For sorting
            switch (index.column()) {
                case ColName: return group.name.toLower();
                case ColPID: return group.processCount == 1 ? group.processes[0].pid : 0;
                case ColCPU: return group.totalCpuUsage;
                case ColMemory: return group.totalMemoryBytes;
                case ColThreads: return group.totalThreads;
            }
        }
    }
    else {
        // Child level - individual process
        int groupIndex = static_cast<int>(id);
        if (groupIndex >= static_cast<int>(m_groups.size()))
            return QVariant();
        
        const auto& group = m_groups[groupIndex];
        int procIndex = index.row();
        if (procIndex >= static_cast<int>(group.processes.size()))
            return QVariant();
        
        const auto& proc = group.processes[procIndex];
        
        if (role == Qt::DisplayRole) {
            switch (index.column()) {
                case ColName: return QString("  %1").arg(proc.name);
                case ColPID: return proc.pid;
                case ColCPU: return QString("%1%").arg(proc.cpuUsage, 0, 'f', 1);
                case ColMemory: return formatBytes(proc.memoryBytes);
                case ColThreads: return proc.threadCount;
                case ColStatus: return proc.status;
            }
        }
        else if (role == Qt::ForegroundRole) {
            return QColor(150, 150, 150); // Dimmed for child items
        }
        else if (role == Qt::TextAlignmentRole) {
            if (index.column() >= ColPID && index.column() <= ColThreads)
                return Qt::AlignRight;
        }
        else if (role == Qt::UserRole) {
            switch (index.column()) {
                case ColName: return proc.name.toLower();
                case ColPID: return static_cast<qulonglong>(proc.pid);
                case ColCPU: return proc.cpuUsage;
                case ColMemory: return proc.memoryBytes;
                case ColThreads: return proc.threadCount;
            }
        }
    }
    
    return QVariant();
}

QVariant ProcessTreeModel::headerData(int section, Qt::Orientation orientation, int role) const
{
    if (orientation != Qt::Horizontal || role != Qt::DisplayRole)
        return QVariant();

    switch (section) {
        case ColName: return tr("Name");
        case ColPID: return tr("PID");
        case ColCPU: return tr("CPU");
        case ColMemory: return tr("Memory");
        case ColThreads: return tr("Threads");
        case ColStatus: return tr("Status");
    }
    return QVariant();
}

Qt::ItemFlags ProcessTreeModel::flags(const QModelIndex &index) const
{
    if (!index.isValid())
        return Qt::NoItemFlags;
    return Qt::ItemIsEnabled | Qt::ItemIsSelectable;
}

ProcessInfo* ProcessTreeModel::getProcess(const QModelIndex& index)
{
    if (!index.isValid())
        return nullptr;
    
    quintptr id = index.internalId();
    
    if (id == quintptr(-1)) {
        // Top level group
        int groupIndex = index.row();
        if (groupIndex < static_cast<int>(m_groups.size())) {
            auto& group = m_groups[groupIndex];
            if (group.processes.size() == 1) {
                return &group.processes[0];
            }
        }
        return nullptr;
    }
    
    // Child process
    int groupIndex = static_cast<int>(id);
    if (groupIndex < static_cast<int>(m_groups.size())) {
        auto& group = m_groups[groupIndex];
        int procIndex = index.row();
        if (procIndex < static_cast<int>(group.processes.size())) {
            return &group.processes[procIndex];
        }
    }
    return nullptr;
}

quint32 ProcessTreeModel::getPid(const QModelIndex& index) const
{
    if (!index.isValid())
        return 0;
    
    quintptr id = index.internalId();
    
    if (id == quintptr(-1)) {
        int groupIndex = index.row();
        if (groupIndex < static_cast<int>(m_groups.size())) {
            const auto& group = m_groups[groupIndex];
            if (group.processes.size() == 1) {
                return group.processes[0].pid;
            }
        }
        return 0;
    }
    
    int groupIndex = static_cast<int>(id);
    if (groupIndex < static_cast<int>(m_groups.size())) {
        const auto& group = m_groups[groupIndex];
        int procIndex = index.row();
        if (procIndex < static_cast<int>(group.processes.size())) {
            return group.processes[procIndex].pid;
        }
    }
    return 0;
}

QModelIndex ProcessTreeModel::findIndexByPid(quint32 pid) const
{
    if (pid == 0)
        return QModelIndex();
    
    for (int groupIdx = 0; groupIdx < static_cast<int>(m_groups.size()); ++groupIdx) {
        const auto& group = m_groups[groupIdx];
        
        // Check if single process group
        if (group.processes.size() == 1 && group.processes[0].pid == pid) {
            return createIndex(groupIdx, 0, quintptr(-1));
        }
        
        // Check child processes
        for (int procIdx = 0; procIdx < static_cast<int>(group.processes.size()); ++procIdx) {
            if (group.processes[procIdx].pid == pid) {
                return createIndex(procIdx, 0, quintptr(groupIdx));
            }
        }
    }
    
    return QModelIndex();
}

QString ProcessTreeModel::formatBytes(qint64 bytes) const
{
    const char* units[] = {"B", "KB", "MB", "GB"};
    int unitIndex = 0;
    double size = bytes;
    
    while (size >= 1024.0 && unitIndex < 3) {
        size /= 1024.0;
        unitIndex++;
    }
    
    return QString("%1 %2").arg(size, 0, 'f', unitIndex > 0 ? 1 : 0).arg(units[unitIndex]);
}

QIcon ProcessTreeModel::getAppIcon(const QString& exePath) const
{
    if (exePath.isEmpty())
        return QIcon();
    
    auto it = m_iconCache.find(exePath);
    if (it != m_iconCache.end())
        return it->second;
    
    QFileIconProvider provider;
    QIcon icon = provider.icon(QFileInfo(exePath));
    m_iconCache[exePath] = icon;
    return icon;
}

// ============================================================================
// ProcessSortFilterProxy implementation
// ============================================================================

ProcessSortFilterProxy::ProcessSortFilterProxy(QObject* parent)
    : QSortFilterProxyModel(parent)
{
    setRecursiveFilteringEnabled(true);
}

bool ProcessSortFilterProxy::lessThan(const QModelIndex &left, const QModelIndex &right) const
{
    QVariant leftData = sourceModel()->data(left, Qt::UserRole);
    QVariant rightData = sourceModel()->data(right, Qt::UserRole);
    
    if (leftData.typeId() == QMetaType::Double) {
        return leftData.toDouble() < rightData.toDouble();
    }
    if (leftData.typeId() == QMetaType::LongLong || leftData.typeId() == QMetaType::ULongLong) {
        return leftData.toLongLong() < rightData.toLongLong();
    }
    if (leftData.typeId() == QMetaType::Int) {
        return leftData.toInt() < rightData.toInt();
    }
    
    return leftData.toString() < rightData.toString();
}

bool ProcessSortFilterProxy::filterAcceptsRow(int source_row, const QModelIndex &source_parent) const
{
    if (filterRegularExpression().pattern().isEmpty())
        return true;
    
    QModelIndex nameIndex = sourceModel()->index(source_row, 0, source_parent);
    QString name = sourceModel()->data(nameIndex, Qt::DisplayRole).toString();
    
    return name.contains(filterRegularExpression());
}

QModelIndex ProcessSortFilterProxy::findProxyIndexByPid(quint32 pid) const
{
    if (pid == 0)
        return QModelIndex();
    
    auto* treeModel = qobject_cast<ProcessTreeModel*>(sourceModel());
    if (!treeModel)
        return QModelIndex();
    
    // First, try to find in source model and map to proxy
    QModelIndex sourceIndex = treeModel->findIndexByPid(pid);
    if (sourceIndex.isValid()) {
        QModelIndex proxyIndex = mapFromSource(sourceIndex);
        if (proxyIndex.isValid()) {
            return proxyIndex;
        }
    }
    
    // Fallback: Search through all visible rows in the proxy recursively
    std::function<QModelIndex(const QModelIndex&)> searchRecursive = [&](const QModelIndex& parent) -> QModelIndex {
        int rows = rowCount(parent);
        for (int row = 0; row < rows; ++row) {
            QModelIndex proxyIdx = index(row, 0, parent);
            QModelIndex srcIdx = mapToSource(proxyIdx);
            
            if (srcIdx.isValid()) {
                quint32 currentPid = treeModel->getPid(srcIdx);
                if (currentPid == pid) {
                    return proxyIdx;
                }
            }
            
            // Search children
            if (hasChildren(proxyIdx)) {
                QModelIndex found = searchRecursive(proxyIdx);
                if (found.isValid()) {
                    return found;
                }
            }
        }
        return QModelIndex();
    };
    
    return searchRecursive(QModelIndex());
}

// ============================================================================
// ProcessMonitor implementation
// ============================================================================

ProcessMonitor::ProcessMonitor(QObject *parent)
    : QObject(parent)
    , m_model(std::make_unique<ProcessTreeModel>())
    , m_proxyModel(std::make_unique<ProcessSortFilterProxy>())
{
    m_proxyModel->setSourceModel(m_model.get());
    m_proxyModel->setFilterCaseSensitivity(Qt::CaseInsensitive);
    
#ifdef _WIN32
    FILETIME idle, kernel, user;
    GetSystemTimes(&idle, &kernel, &user);
    m_lastSystemKernelTime = kernel;
    m_lastSystemUserTime = user;
#endif
    
    refresh();
}

ProcessMonitor::~ProcessMonitor() = default;

void ProcessMonitor::setFilter(const QString& filter)
{
    m_proxyModel->setFilterFixedString(filter);
}

void ProcessMonitor::setGrouped(bool grouped)
{
    m_model->setGrouped(grouped);
}

bool ProcessMonitor::isGrouped() const
{
    return m_model->isGrouped();
}

void ProcessMonitor::refresh()
{
    queryProcesses();
    m_model->updateProcesses(m_processes);
}

QString ProcessMonitor::getProcessDescription(const QString& exePath)
{
#ifdef _WIN32
    if (exePath.isEmpty())
        return QString();
    
    DWORD handle = 0;
    DWORD size = GetFileVersionInfoSizeW(exePath.toStdWString().c_str(), &handle);
    if (size == 0)
        return QString();
    
    std::vector<BYTE> buffer(size);
    if (!GetFileVersionInfoW(exePath.toStdWString().c_str(), handle, size, buffer.data()))
        return QString();
    
    // Try to get FileDescription
    struct LANGANDCODEPAGE {
        WORD wLanguage;
        WORD wCodePage;
    } *lpTranslate;
    
    UINT cbTranslate = 0;
    if (!VerQueryValueW(buffer.data(), L"\\VarFileInfo\\Translation",
                        reinterpret_cast<LPVOID*>(&lpTranslate), &cbTranslate))
        return QString();
    
    if (cbTranslate < sizeof(LANGANDCODEPAGE))
        return QString();
    
    wchar_t subBlock[256];
    swprintf_s(subBlock, L"\\StringFileInfo\\%04x%04x\\FileDescription",
               lpTranslate[0].wLanguage, lpTranslate[0].wCodePage);
    
    LPVOID lpBuffer = nullptr;
    UINT dwBytes = 0;
    if (VerQueryValueW(buffer.data(), subBlock, &lpBuffer, &dwBytes) && dwBytes > 0) {
        return QString::fromWCharArray(static_cast<wchar_t*>(lpBuffer));
    }
#else
    Q_UNUSED(exePath);
#endif
    return QString();
}

void ProcessMonitor::queryProcesses()
{
    m_processes.clear();
    
#ifdef _WIN32
    FILETIME idleTime, kernelTime, userTime;
    GetSystemTimes(&idleTime, &kernelTime, &userTime);
    
    auto fileTimeToUInt64 = [](const FILETIME& ft) -> ULONGLONG {
        return (static_cast<ULONGLONG>(ft.dwHighDateTime) << 32) | ft.dwLowDateTime;
    };
    
    ULONGLONG sysKernelDiff = fileTimeToUInt64(kernelTime) - fileTimeToUInt64(m_lastSystemKernelTime);
    ULONGLONG sysUserDiff = fileTimeToUInt64(userTime) - fileTimeToUInt64(m_lastSystemUserTime);
    ULONGLONG sysTotalTime = sysKernelDiff + sysUserDiff;
    
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) {
        return;
    }
    
    PROCESSENTRY32W pe;
    pe.dwSize = sizeof(pe);
    
    if (Process32FirstW(snapshot, &pe)) {
        do {
            ProcessInfo proc;
            proc.pid = pe.th32ProcessID;
            proc.name = QString::fromWCharArray(pe.szExeFile);
            proc.threadCount = pe.cntThreads;
            
            HANDLE hProcess = OpenProcess(
                PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ,
                FALSE, proc.pid
            );
            
            if (hProcess) {
                // Get executable path
                wchar_t exePath[MAX_PATH] = {0};
                DWORD pathSize = MAX_PATH;
                if (QueryFullProcessImageNameW(hProcess, 0, exePath, &pathSize)) {
                    proc.executablePath = QString::fromWCharArray(exePath);
                    // Get friendly name from file description
                    proc.displayName = getProcessDescription(proc.executablePath);
                }
                
                PROCESS_MEMORY_COUNTERS_EX pmc;
                if (GetProcessMemoryInfo(hProcess, 
                    reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&pmc), sizeof(pmc))) {
                    proc.memoryBytes = pmc.WorkingSetSize;
                    proc.privateBytes = pmc.PrivateUsage;
                }
                
                FILETIME createTime, exitTime, procKernelTime, procUserTime;
                if (GetProcessTimes(hProcess, &createTime, &exitTime, 
                    &procKernelTime, &procUserTime)) {
                    
                    auto it = m_processTimes.find(proc.pid);
                    if (it != m_processTimes.end() && sysTotalTime > 0) {
                        ULONGLONG procKernelDiff = fileTimeToUInt64(procKernelTime) - 
                            fileTimeToUInt64(it->second.kernelTime);
                        ULONGLONG procUserDiff = fileTimeToUInt64(procUserTime) - 
                            fileTimeToUInt64(it->second.userTime);
                        
                        proc.cpuUsage = ((procKernelDiff + procUserDiff) * 100.0) / sysTotalTime;
                    }
                    
                    m_processTimes[proc.pid] = {procKernelTime, procUserTime, {}};
                }
                
                DWORD exitCode;
                if (GetExitCodeProcess(hProcess, &exitCode)) {
                    proc.status = (exitCode == STILL_ACTIVE) ? "Running" : "Terminated";
                }
                
                CloseHandle(hProcess);
            } else {
                proc.status = "Access Denied";
            }
            
            m_processes.push_back(proc);
            
        } while (Process32NextW(snapshot, &pe));
    }
    
    CloseHandle(snapshot);
    
    m_lastSystemKernelTime = kernelTime;
    m_lastSystemUserTime = userTime;
    
    // Clean up old process times
    std::unordered_map<quint32, ProcessTimes> newTimes;
    for (const auto& proc : m_processes) {
        auto it = m_processTimes.find(proc.pid);
        if (it != m_processTimes.end()) {
            newTimes[proc.pid] = it->second;
        }
    }
    m_processTimes = std::move(newTimes);
#endif
}

bool ProcessMonitor::terminateProcess(quint32 pid)
{
#ifdef _WIN32
    HANDLE hProcess = OpenProcess(PROCESS_TERMINATE, FALSE, pid);
    if (!hProcess) {
        return false;
    }
    
    BOOL result = TerminateProcess(hProcess, 1);
    CloseHandle(hProcess);
    
    if (result) {
        refresh();
    }
    
    return result != FALSE;
#else
    Q_UNUSED(pid);
    return false;
#endif
}
