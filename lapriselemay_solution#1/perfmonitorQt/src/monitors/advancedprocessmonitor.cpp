#include "advancedprocessmonitor.h"

#include <QFileInfo>
#include <QFileIconProvider>
#include <QFont>
#include <QColor>
#include <QBrush>
#include <algorithm>
#include <set>

#ifdef _WIN32
// Windows.h is already included via advancedprocessmonitor.h
#include <TlHelp32.h>
#include <Psapi.h>
#include <ShlObj.h>
#include <Sddl.h>
#include <UserEnv.h>
#pragma comment(lib, "psapi.lib")
#pragma comment(lib, "version.lib")
#pragma comment(lib, "userenv.lib")
#pragma comment(lib, "advapi32.lib")

// Define NTSTATUS if not already defined
#ifndef NTSTATUS
typedef LONG NTSTATUS;
#endif
#ifndef STATUS_SUCCESS
#define STATUS_SUCCESS ((NTSTATUS)0x00000000L)
#endif
#endif

// ============================================================================
// Helper functions
// ============================================================================

#ifdef _WIN32
static ULONGLONG fileTimeToUInt64(const FILETIME& ft) {
    return (static_cast<ULONGLONG>(ft.dwHighDateTime) << 32) | ft.dwLowDateTime;
}

static QDateTime fileTimeToQDateTime(const FILETIME& ft) {
    ULONGLONG time = fileTimeToUInt64(ft);
    // Convert from 100-nanosecond intervals since Jan 1, 1601 to ms since epoch
    // Difference between 1601 and 1970 in 100ns intervals
    constexpr ULONGLONG EPOCH_DIFF = 116444736000000000ULL;
    if (time < EPOCH_DIFF) return QDateTime();
    qint64 msecs = (time - EPOCH_DIFF) / 10000;
    return QDateTime::fromMSecsSinceEpoch(msecs);
}
#endif

// ============================================================================
// ProcessHistoryManager implementation
// ============================================================================

ProcessHistoryManager::ProcessHistoryManager(QObject* parent)
    : QObject(parent)
{
}

void ProcessHistoryManager::recordProcessStart(const AdvancedProcessInfo& proc)
{
    m_runningProcesses[proc.pid] = proc;
}

void ProcessHistoryManager::recordProcessEnd(quint32 pid, const QString& reason, int exitCode)
{
    auto it = m_runningProcesses.find(pid);
    if (it == m_runningProcesses.end()) return;
    
    ProcessHistoryEntry entry;
    entry.pid = pid;
    entry.name = it->second.name;
    entry.executablePath = it->second.executablePath;
    entry.startTime = it->second.startTime;
    entry.endTime = QDateTime::currentDateTime();
    entry.peakMemoryBytes = it->second.peakMemoryBytes;
    entry.totalCpuTimeMs = it->second.cpuTimeMs;
    entry.terminationReason = reason;
    entry.exitCode = exitCode;
    
    m_history.push_front(entry);
    
    // Trim history if needed
    while (static_cast<int>(m_history.size()) > m_maxHistorySize) {
        m_history.pop_back();
    }
    
    m_runningProcesses.erase(it);
    
    emit processEnded(entry);
}

void ProcessHistoryManager::clearHistory()
{
    m_history.clear();
    emit historyCleared();
}

void ProcessHistoryManager::setMaxHistorySize(int size)
{
    m_maxHistorySize = size;
    while (static_cast<int>(m_history.size()) > m_maxHistorySize) {
        m_history.pop_back();
    }
}

// ============================================================================
// AdvancedProcessTreeModel implementation
// ============================================================================

AdvancedProcessTreeModel::AdvancedProcessTreeModel(QObject *parent)
    : QAbstractItemModel(parent)
{
}

void AdvancedProcessTreeModel::setProcesses(const std::vector<AdvancedProcessInfo>& processes)
{
    beginResetModel();
    m_processes = processes;
    buildTree();
    endResetModel();
}

void AdvancedProcessTreeModel::updateProcesses(const std::vector<AdvancedProcessInfo>& processes)
{
    // Store old structure info for comparison
    int oldRootCount = static_cast<int>(m_rootIndices.size());
    std::vector<int> oldChildCounts;
    oldChildCounts.reserve(m_nodes.size());
    for (const auto& node : m_nodes) {
        oldChildCounts.push_back(static_cast<int>(node.childIndices.size()));
    }
    
    // Update data
    m_processes = processes;
    buildTree();
    
    int newRootCount = static_cast<int>(m_rootIndices.size());
    
    // Check if structure changed significantly
    bool structureChanged = (oldRootCount != newRootCount);
    
    // Also check child counts for structural changes
    if (!structureChanged && m_nodes.size() == oldChildCounts.size()) {
        for (size_t i = 0; i < m_nodes.size() && i < oldChildCounts.size(); ++i) {
            if (static_cast<int>(m_nodes[i].childIndices.size()) != oldChildCounts[i]) {
                structureChanged = true;
                break;
            }
        }
    } else if (m_nodes.size() != oldChildCounts.size()) {
        structureChanged = true;
    }
    
    if (structureChanged) {
        // Use layoutChanged instead of modelReset - it preserves selection better
        emit layoutAboutToBeChanged();
        emit layoutChanged();
    } else {
        // Just update data values without changing structure
        if (!m_rootIndices.empty()) {
            QModelIndex topLeft = index(0, 0);
            QModelIndex bottomRight = index(newRootCount - 1, ColCount - 1);
            emit dataChanged(topLeft, bottomRight, {Qt::DisplayRole, Qt::UserRole, Qt::DecorationRole});
            
            // Also update children for expanded groups
            for (int rootIdx = 0; rootIdx < newRootCount; ++rootIdx) {
                int nodeIdx = m_rootIndices[rootIdx];
                if (nodeIdx < static_cast<int>(m_nodes.size())) {
                    const auto& node = m_nodes[nodeIdx];
                    if (!node.childIndices.empty()) {
                        QModelIndex parentIdx = index(rootIdx, 0);
                        QModelIndex childTopLeft = index(0, 0, parentIdx);
                        QModelIndex childBottomRight = index(static_cast<int>(node.childIndices.size()) - 1, ColCount - 1, parentIdx);
                        emit dataChanged(childTopLeft, childBottomRight, {Qt::DisplayRole, Qt::UserRole, Qt::DecorationRole});
                    }
                }
            }
        }
    }
}

void AdvancedProcessTreeModel::setGroupingMode(GroupingMode mode)
{
    if (m_groupingMode != mode) {
        beginResetModel();
        m_groupingMode = mode;
        buildTree();
        endResetModel();
    }
}

void AdvancedProcessTreeModel::buildTree()
{
    m_nodes.clear();
    m_rootIndices.clear();
    
    switch (m_groupingMode) {
        case GroupingMode::None:
            buildFlatTree();
            break;
        case GroupingMode::ByCategory:
            buildCategoryTree();
            break;
        case GroupingMode::ByParent:
            buildParentChildTree();
            break;
        case GroupingMode::ByName:
            buildNameGroupTree();
            break;
    }
}

void AdvancedProcessTreeModel::buildFlatTree()
{
    for (size_t i = 0; i < m_processes.size(); ++i) {
        TreeNode node;
        node.process = &m_processes[i];
        node.isGroup = false;
        m_nodes.push_back(node);
        m_rootIndices.push_back(static_cast<int>(i));
    }
}

void AdvancedProcessTreeModel::buildCategoryTree()
{
    // Create category groups
    std::map<ProcessCategory, std::vector<int>> categoryMap;
    
    for (size_t i = 0; i < m_processes.size(); ++i) {
        categoryMap[m_processes[i].category].push_back(static_cast<int>(i));
    }
    
    // Order: Apps, Background, Windows, Services
    std::vector<ProcessCategory> order = {
        ProcessCategory::Apps,
        ProcessCategory::Background,
        ProcessCategory::Windows,
        ProcessCategory::Services,
        ProcessCategory::Unknown
    };
    
    for (ProcessCategory cat : order) {
        auto it = categoryMap.find(cat);
        if (it == categoryMap.end() || it->second.empty()) continue;
        
        // Create group node
        TreeNode groupNode;
        groupNode.isGroup = true;
        groupNode.groupName = getCategoryName(cat);
        groupNode.category = cat;
        groupNode.processCount = static_cast<int>(it->second.size());
        
        int groupIdx = static_cast<int>(m_nodes.size());
        m_rootIndices.push_back(groupIdx);
        
        // Calculate aggregates and add children
        for (int procIdx : it->second) {
            groupNode.totalCpu += m_processes[procIdx].cpuUsage;
            groupNode.totalMemory += m_processes[procIdx].memoryBytes;
            groupNode.childIndices.push_back(static_cast<int>(m_nodes.size()) + 1 + 
                static_cast<int>(groupNode.childIndices.size()));
        }
        
        m_nodes.push_back(groupNode);
        
        // Add process nodes as children
        for (int procIdx : it->second) {
            TreeNode procNode;
            procNode.process = &m_processes[procIdx];
            procNode.parentIndex = groupIdx;
            procNode.isGroup = false;
            m_nodes.push_back(procNode);
        }
    }
}

void AdvancedProcessTreeModel::buildParentChildTree()
{
    // Build parent-child relationships
    std::unordered_map<quint32, int> pidToIndex;
    for (size_t i = 0; i < m_processes.size(); ++i) {
        pidToIndex[m_processes[i].pid] = static_cast<int>(i);
    }
    
    // First pass: create all nodes
    for (size_t i = 0; i < m_processes.size(); ++i) {
        TreeNode node;
        node.process = &m_processes[i];
        node.isGroup = false;
        m_nodes.push_back(node);
    }
    
    // Second pass: build hierarchy
    std::set<int> hasParent;
    for (size_t i = 0; i < m_processes.size(); ++i) {
        quint32 parentPid = m_processes[i].parentPid;
        auto it = pidToIndex.find(parentPid);
        if (it != pidToIndex.end() && it->second != static_cast<int>(i)) {
            m_nodes[i].parentIndex = it->second;
            m_nodes[it->second].childIndices.push_back(static_cast<int>(i));
            hasParent.insert(static_cast<int>(i));
        }
    }
    
    // Root nodes are those without parents
    for (size_t i = 0; i < m_nodes.size(); ++i) {
        if (hasParent.find(static_cast<int>(i)) == hasParent.end()) {
            m_rootIndices.push_back(static_cast<int>(i));
        }
    }
}

void AdvancedProcessTreeModel::buildNameGroupTree()
{
    std::map<QString, std::vector<int>> nameMap;
    
    for (size_t i = 0; i < m_processes.size(); ++i) {
        QString key = m_processes[i].name.toLower();
        nameMap[key].push_back(static_cast<int>(i));
    }
    
    for (auto& [name, indices] : nameMap) {
        if (indices.size() == 1) {
            // Single process - no grouping
            TreeNode node;
            node.process = &m_processes[indices[0]];
            node.isGroup = false;
            m_rootIndices.push_back(static_cast<int>(m_nodes.size()));
            m_nodes.push_back(node);
        } else {
            // Multiple processes - create group
            TreeNode groupNode;
            groupNode.isGroup = true;
            groupNode.groupName = m_processes[indices[0]].displayName.isEmpty() 
                ? m_processes[indices[0]].name 
                : m_processes[indices[0]].displayName;
            groupNode.processCount = static_cast<int>(indices.size());
            
            int groupIdx = static_cast<int>(m_nodes.size());
            m_rootIndices.push_back(groupIdx);
            
            for (int procIdx : indices) {
                groupNode.totalCpu += m_processes[procIdx].cpuUsage;
                groupNode.totalMemory += m_processes[procIdx].memoryBytes;
                groupNode.childIndices.push_back(static_cast<int>(m_nodes.size()) + 1 + 
                    static_cast<int>(groupNode.childIndices.size()));
            }
            
            m_nodes.push_back(groupNode);
            
            for (int procIdx : indices) {
                TreeNode procNode;
                procNode.process = &m_processes[procIdx];
                procNode.parentIndex = groupIdx;
                procNode.isGroup = false;
                m_nodes.push_back(procNode);
            }
        }
    }
}

QModelIndex AdvancedProcessTreeModel::index(int row, int column, const QModelIndex &parent) const
{
    if (!hasIndex(row, column, parent))
        return QModelIndex();

    if (!parent.isValid()) {
        // Top level
        if (row < static_cast<int>(m_rootIndices.size())) {
            return createIndex(row, column, quintptr(m_rootIndices[row]));
        }
    } else {
        // Child level
        int parentNodeIdx = static_cast<int>(parent.internalId());
        if (parentNodeIdx < static_cast<int>(m_nodes.size())) {
            const auto& parentNode = m_nodes[parentNodeIdx];
            if (row < static_cast<int>(parentNode.childIndices.size())) {
                return createIndex(row, column, quintptr(parentNode.childIndices[row]));
            }
        }
    }
    return QModelIndex();
}

QModelIndex AdvancedProcessTreeModel::parent(const QModelIndex &index) const
{
    if (!index.isValid())
        return QModelIndex();

    int nodeIdx = static_cast<int>(index.internalId());
    if (nodeIdx >= static_cast<int>(m_nodes.size()))
        return QModelIndex();
    
    int parentIdx = m_nodes[nodeIdx].parentIndex;
    if (parentIdx < 0)
        return QModelIndex();
    
    // Find row of parent in its parent's children (or in root)
    const auto& parentNode = m_nodes[parentIdx];
    if (parentNode.parentIndex < 0) {
        // Parent is a root node
        for (size_t i = 0; i < m_rootIndices.size(); ++i) {
            if (m_rootIndices[i] == parentIdx) {
                return createIndex(static_cast<int>(i), 0, quintptr(parentIdx));
            }
        }
    } else {
        // Parent has a grandparent
        const auto& grandparent = m_nodes[parentNode.parentIndex];
        for (size_t i = 0; i < grandparent.childIndices.size(); ++i) {
            if (grandparent.childIndices[i] == parentIdx) {
                return createIndex(static_cast<int>(i), 0, quintptr(parentIdx));
            }
        }
    }
    
    return QModelIndex();
}

int AdvancedProcessTreeModel::rowCount(const QModelIndex &parent) const
{
    if (!parent.isValid()) {
        return static_cast<int>(m_rootIndices.size());
    }
    
    int nodeIdx = static_cast<int>(parent.internalId());
    if (nodeIdx < static_cast<int>(m_nodes.size())) {
        return static_cast<int>(m_nodes[nodeIdx].childIndices.size());
    }
    
    return 0;
}

int AdvancedProcessTreeModel::columnCount(const QModelIndex&) const
{
    return ColCount;
}

QVariant AdvancedProcessTreeModel::data(const QModelIndex &index, int role) const
{
    if (!index.isValid())
        return QVariant();

    int nodeIdx = static_cast<int>(index.internalId());
    if (nodeIdx >= static_cast<int>(m_nodes.size()))
        return QVariant();
    
    const auto& node = m_nodes[nodeIdx];
    
    if (node.isGroup) {
        // Group node
        if (role == Qt::DisplayRole) {
            switch (index.column()) {
                case ColName: 
                    return QString("%1 (%2)").arg(node.groupName).arg(node.processCount);
                case ColCPU: 
                    return QString("%1%").arg(node.totalCpu, 0, 'f', 1);
                case ColMemory: 
                    return formatBytes(node.totalMemory);
                default:
                    return QVariant();
            }
        }
        else if (role == Qt::FontRole) {
            QFont font;
            font.setBold(true);
            return font;
        }
        else if (role == Qt::BackgroundRole) {
            return QBrush(QColor(60, 60, 60));
        }
    }
    else if (node.process) {
        // Process node
        const auto& proc = *node.process;
        
        if (role == Qt::DisplayRole) {
            switch (index.column()) {
                case ColName: 
                    return proc.displayName.isEmpty() ? proc.name : proc.displayName;
                case ColPID: 
                    return proc.pid;
                case ColStatus: {
                    switch (proc.state) {
                        case ProcessState::Running: return tr("Running");
                        case ProcessState::Suspended: return tr("Suspended");
                        case ProcessState::NotResponding: return tr("Not Responding");
                        case ProcessState::Terminated: return tr("Terminated");
                    }
                    return QString();
                }
                case ColCPU: 
                    return QString("%1%").arg(proc.cpuUsage, 0, 'f', 1);
                case ColMemory: 
                    return formatBytes(proc.memoryBytes);
                case ColDisk:
                    return formatBytesPerSec(proc.ioReadBytesPerSec + proc.ioWriteBytesPerSec);
                case ColNetwork:
                    return formatBytesPerSec(proc.networkSentBytes + proc.networkRecvBytes);
                case ColGPU:
                    if (proc.gpuUsage > 0)
                        return QString("%1%").arg(proc.gpuUsage, 0, 'f', 1);
                    return QString();
                case ColThreads: 
                    return proc.threadCount;
                case ColHandles:
                    return proc.handleCount;
                case ColUser:
                    return proc.userName;
            }
        }
        else if (role == Qt::DecorationRole && index.column() == ColName) {
            return proc.icon;
        }
        else if (role == Qt::ForegroundRole) {
            if (proc.state == ProcessState::Suspended) {
                return QBrush(QColor(128, 128, 128));
            }
            if (proc.state == ProcessState::NotResponding) {
                return QBrush(QColor(255, 100, 100));
            }
            if (node.parentIndex >= 0) {
                return QBrush(QColor(180, 180, 180));
            }
        }
        else if (role == Qt::ToolTipRole && index.column() == ColName) {
            return QString("%1\nPID: %2\nPath: %3\nCommand: %4")
                .arg(proc.name)
                .arg(proc.pid)
                .arg(proc.executablePath)
                .arg(proc.commandLine);
        }
        else if (role == Qt::TextAlignmentRole) {
            if (index.column() >= ColPID && index.column() != ColStatus && index.column() != ColUser)
                return Qt::AlignRight;
        }
        else if (role == Qt::UserRole) {
            // For sorting
            switch (index.column()) {
                case ColName: return proc.name.toLower();
                case ColPID: return static_cast<qulonglong>(proc.pid);
                case ColStatus: return static_cast<int>(proc.state);
                case ColCPU: return proc.cpuUsage;
                case ColMemory: return proc.memoryBytes;
                case ColDisk: return proc.ioReadBytesPerSec + proc.ioWriteBytesPerSec;
                case ColNetwork: return proc.networkSentBytes + proc.networkRecvBytes;
                case ColGPU: return proc.gpuUsage;
                case ColThreads: return proc.threadCount;
                case ColHandles: return proc.handleCount;
                case ColUser: return proc.userName;
            }
        }
    }
    
    return QVariant();
}

QVariant AdvancedProcessTreeModel::headerData(int section, Qt::Orientation orientation, int role) const
{
    if (orientation != Qt::Horizontal || role != Qt::DisplayRole)
        return QVariant();

    switch (section) {
        case ColName: return tr("Name");
        case ColPID: return tr("PID");
        case ColStatus: return tr("Status");
        case ColCPU: return tr("CPU");
        case ColMemory: return tr("Memory");
        case ColDisk: return tr("Disk");
        case ColNetwork: return tr("Network");
        case ColGPU: return tr("GPU");
        case ColThreads: return tr("Threads");
        case ColHandles: return tr("Handles");
        case ColUser: return tr("User");
    }
    return QVariant();
}

Qt::ItemFlags AdvancedProcessTreeModel::flags(const QModelIndex &index) const
{
    if (!index.isValid())
        return Qt::NoItemFlags;
    return Qt::ItemIsEnabled | Qt::ItemIsSelectable;
}

AdvancedProcessInfo* AdvancedProcessTreeModel::getProcess(const QModelIndex& index)
{
    if (!index.isValid())
        return nullptr;
    
    int nodeIdx = static_cast<int>(index.internalId());
    if (nodeIdx < static_cast<int>(m_nodes.size())) {
        return m_nodes[nodeIdx].process;
    }
    return nullptr;
}

const AdvancedProcessInfo* AdvancedProcessTreeModel::getProcess(const QModelIndex& index) const
{
    if (!index.isValid())
        return nullptr;
    
    int nodeIdx = static_cast<int>(index.internalId());
    if (nodeIdx < static_cast<int>(m_nodes.size())) {
        return m_nodes[nodeIdx].process;
    }
    return nullptr;
}

quint32 AdvancedProcessTreeModel::getPid(const QModelIndex& index) const
{
    const auto* proc = getProcess(index);
    return proc ? proc->pid : 0;
}

QModelIndex AdvancedProcessTreeModel::findIndexByPid(quint32 pid) const
{
    for (size_t i = 0; i < m_nodes.size(); ++i) {
        if (m_nodes[i].process && m_nodes[i].process->pid == pid) {
            // Find the row
            if (m_nodes[i].parentIndex < 0) {
                for (size_t r = 0; r < m_rootIndices.size(); ++r) {
                    if (m_rootIndices[r] == static_cast<int>(i)) {
                        return createIndex(static_cast<int>(r), 0, quintptr(i));
                    }
                }
            } else {
                const auto& parent = m_nodes[m_nodes[i].parentIndex];
                for (size_t r = 0; r < parent.childIndices.size(); ++r) {
                    if (parent.childIndices[r] == static_cast<int>(i)) {
                        return createIndex(static_cast<int>(r), 0, quintptr(i));
                    }
                }
            }
        }
    }
    return QModelIndex();
}

std::vector<quint32> AdvancedProcessTreeModel::getChildPids(quint32 parentPid) const
{
    std::vector<quint32> children;
    for (const auto& proc : m_processes) {
        if (proc.parentPid == parentPid) {
            children.push_back(proc.pid);
        }
    }
    return children;
}

QString AdvancedProcessTreeModel::formatBytes(qint64 bytes) const
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

QString AdvancedProcessTreeModel::formatBytesPerSec(qint64 bytesPerSec) const
{
    if (bytesPerSec == 0) return QString("0 B/s");
    return formatBytes(bytesPerSec) + "/s";
}

QString AdvancedProcessTreeModel::getCategoryName(ProcessCategory cat) const
{
    switch (cat) {
        case ProcessCategory::Apps: return tr("Apps");
        case ProcessCategory::Background: return tr("Background processes");
        case ProcessCategory::Windows: return tr("Windows processes");
        case ProcessCategory::Services: return tr("Services");
        default: return tr("Other");
    }
}

QColor AdvancedProcessTreeModel::getStateColor(ProcessState state) const
{
    switch (state) {
        case ProcessState::Running: return QColor(0, 200, 0);
        case ProcessState::Suspended: return QColor(128, 128, 128);
        case ProcessState::NotResponding: return QColor(255, 100, 100);
        case ProcessState::Terminated: return QColor(100, 100, 100);
    }
    return QColor();
}

// ============================================================================
// AdvancedProcessSortFilterProxy implementation
// ============================================================================

AdvancedProcessSortFilterProxy::AdvancedProcessSortFilterProxy(QObject* parent)
    : QSortFilterProxyModel(parent)
{
    setRecursiveFilteringEnabled(true);
}

void AdvancedProcessSortFilterProxy::setShowSystemProcesses(bool show)
{
    m_showSystemProcesses = show;
    invalidateFilter();
}

void AdvancedProcessSortFilterProxy::setCategoryFilter(ProcessCategory cat)
{
    m_hasCategoryFilter = true;
    m_categoryFilter = cat;
    invalidateFilter();
}

void AdvancedProcessSortFilterProxy::clearCategoryFilter()
{
    m_hasCategoryFilter = false;
    invalidateFilter();
}

bool AdvancedProcessSortFilterProxy::lessThan(const QModelIndex &left, const QModelIndex &right) const
{
    QVariant leftData = sourceModel()->data(left, Qt::UserRole);
    QVariant rightData = sourceModel()->data(right, Qt::UserRole);
    
    if (!leftData.isValid() || !rightData.isValid())
        return QSortFilterProxyModel::lessThan(left, right);
    
    if (leftData.typeId() == QMetaType::Double) {
        return leftData.toDouble() < rightData.toDouble();
    }
    if (leftData.typeId() == QMetaType::LongLong || leftData.typeId() == QMetaType::ULongLong) {
        return leftData.toLongLong() < rightData.toLongLong();
    }
    if (leftData.typeId() == QMetaType::Int) {
        return leftData.toInt() < rightData.toInt();
    }
    
    return leftData.toString().compare(rightData.toString(), Qt::CaseInsensitive) < 0;
}

bool AdvancedProcessSortFilterProxy::filterAcceptsRow(int source_row, const QModelIndex &source_parent) const
{
    auto* model = qobject_cast<AdvancedProcessTreeModel*>(sourceModel());
    if (!model) return true;
    
    QModelIndex idx = model->index(source_row, 0, source_parent);
    const auto* proc = model->getProcess(idx);
    
    if (proc) {
        // Category filter
        if (m_hasCategoryFilter && proc->category != m_categoryFilter) {
            return false;
        }
        
        // System process filter
        if (!m_showSystemProcesses && proc->category == ProcessCategory::Windows) {
            return false;
        }
        
        // Text filter
        if (!filterRegularExpression().pattern().isEmpty()) {
            QString name = model->data(idx, Qt::DisplayRole).toString();
            if (!name.contains(filterRegularExpression())) {
                return false;
            }
        }
    }
    
    return true;
}

QModelIndex AdvancedProcessSortFilterProxy::findProxyIndexByPid(quint32 pid) const
{
    auto* model = qobject_cast<AdvancedProcessTreeModel*>(sourceModel());
    if (!model) return QModelIndex();
    
    QModelIndex sourceIdx = model->findIndexByPid(pid);
    return mapFromSource(sourceIdx);
}

// ============================================================================
// AdvancedProcessMonitor implementation  
// ============================================================================

AdvancedProcessMonitor::AdvancedProcessMonitor(QObject *parent)
    : QObject(parent)
    , m_model(std::make_unique<AdvancedProcessTreeModel>())
    , m_proxyModel(std::make_unique<AdvancedProcessSortFilterProxy>())
    , m_historyManager(std::make_unique<ProcessHistoryManager>())
    , m_refreshTimer(std::make_unique<QTimer>())
{
    m_proxyModel->setSourceModel(m_model.get());
    m_proxyModel->setFilterCaseSensitivity(Qt::CaseInsensitive);
    
    connect(m_refreshTimer.get(), &QTimer::timeout, this, &AdvancedProcessMonitor::refresh);
    
#ifdef _WIN32
    FILETIME idle, kernel, user;
    GetSystemTimes(&idle, &kernel, &user);
    m_lastSystemKernelTime = kernel;
    m_lastSystemUserTime = user;
#endif
    
    refresh();
}

AdvancedProcessMonitor::~AdvancedProcessMonitor()
{
    stopAutoRefresh();
}

void AdvancedProcessMonitor::startAutoRefresh(int intervalMs)
{
    m_refreshTimer->start(intervalMs);
}

void AdvancedProcessMonitor::stopAutoRefresh()
{
    m_refreshTimer->stop();
}

void AdvancedProcessMonitor::refresh()
{
    // Emit signal BEFORE updating - allows widgets to save selection state
    emit aboutToRefresh();
    
    queryProcesses();
    detectNewAndEndedProcesses();
    m_model->updateProcesses(m_processes);
    emit processesUpdated();
}

void AdvancedProcessMonitor::setFilter(const QString& filter)
{
    m_proxyModel->setFilterFixedString(filter);
}

void AdvancedProcessMonitor::setGroupingMode(AdvancedProcessTreeModel::GroupingMode mode)
{
    m_model->setGroupingMode(mode);
}

void AdvancedProcessMonitor::setShowSystemProcesses(bool show)
{
    m_proxyModel->setShowSystemProcesses(show);
}

void AdvancedProcessMonitor::detectNewAndEndedProcesses()
{
    std::set<quint32> currentPids;
    for (const auto& proc : m_processes) {
        currentPids.insert(proc.pid);
        
        // Check for new processes
        if (m_previousProcesses.find(proc.pid) == m_previousProcesses.end()) {
            m_historyManager->recordProcessStart(proc);
            emit processStarted(proc.pid, proc.name);
        }
    }
    
    // Check for ended processes
    for (const auto& [pid, proc] : m_previousProcesses) {
        if (currentPids.find(pid) == currentPids.end()) {
            m_historyManager->recordProcessEnd(pid, "Unknown", 0);
            emit processEnded(pid, proc.name);
        }
    }
    
    // Update previous
    m_previousProcesses.clear();
    for (const auto& proc : m_processes) {
        m_previousProcesses[proc.pid] = proc;
    }
}

const AdvancedProcessInfo* AdvancedProcessMonitor::getProcessByPid(quint32 pid) const
{
    for (const auto& proc : m_processes) {
        if (proc.pid == pid) return &proc;
    }
    return nullptr;
}

std::vector<quint32> AdvancedProcessMonitor::getChildProcesses(quint32 parentPid) const
{
    std::vector<quint32> children;
    for (const auto& proc : m_processes) {
        if (proc.parentPid == parentPid) {
            children.push_back(proc.pid);
        }
    }
    return children;
}

std::vector<quint32> AdvancedProcessMonitor::getProcessAncestors(quint32 pid) const
{
    std::vector<quint32> ancestors;
    std::set<quint32> visited;
    
    const AdvancedProcessInfo* current = getProcessByPid(pid);
    while (current && current->parentPid != 0) {
        if (visited.count(current->parentPid)) break; // Prevent cycles
        visited.insert(current->parentPid);
        
        ancestors.push_back(current->parentPid);
        current = getProcessByPid(current->parentPid);
    }
    
    return ancestors;
}

int AdvancedProcessMonitor::totalThreadCount() const
{
    int total = 0;
    for (const auto& proc : m_processes) {
        total += proc.threadCount;
    }
    return total;
}

double AdvancedProcessMonitor::totalCpuUsage() const
{
    double total = 0.0;
    for (const auto& proc : m_processes) {
        total += proc.cpuUsage;
    }
    return total;
}

qint64 AdvancedProcessMonitor::totalMemoryUsage() const
{
    qint64 total = 0;
    for (const auto& proc : m_processes) {
        total += proc.memoryBytes;
    }
    return total;
}

bool AdvancedProcessMonitor::terminateProcess(quint32 pid)
{
#ifdef _WIN32
    HANDLE hProcess = OpenProcess(PROCESS_TERMINATE, FALSE, pid);
    if (!hProcess) return false;
    
    BOOL result = TerminateProcess(hProcess, 1);
    CloseHandle(hProcess);
    
    if (result) {
        m_historyManager->recordProcessEnd(pid, "User", 1);
        refresh();
    }
    
    return result != FALSE;
#else
    Q_UNUSED(pid);
    return false;
#endif
}

bool AdvancedProcessMonitor::terminateProcessTree(quint32 pid)
{
    // First, terminate all children recursively
    auto children = getChildProcesses(pid);
    for (quint32 childPid : children) {
        terminateProcessTree(childPid);
    }
    
    // Then terminate the parent
    return terminateProcess(pid);
}

bool AdvancedProcessMonitor::suspendProcess(quint32 pid)
{
#ifdef _WIN32
    return suspendResumeProcess(pid, true);
#else
    Q_UNUSED(pid);
    return false;
#endif
}

bool AdvancedProcessMonitor::resumeProcess(quint32 pid)
{
#ifdef _WIN32
    return suspendResumeProcess(pid, false);
#else
    Q_UNUSED(pid);
    return false;
#endif
}

#ifdef _WIN32
bool AdvancedProcessMonitor::suspendResumeProcess(quint32 pid, bool suspend)
{
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
    if (snapshot == INVALID_HANDLE_VALUE) return false;
    
    THREADENTRY32 te;
    te.dwSize = sizeof(te);
    
    bool success = false;
    if (Thread32First(snapshot, &te)) {
        do {
            if (te.th32OwnerProcessID == pid) {
                HANDLE hThread = OpenThread(THREAD_SUSPEND_RESUME, FALSE, te.th32ThreadID);
                if (hThread) {
                    if (suspend) {
                        SuspendThread(hThread);
                    } else {
                        ResumeThread(hThread);
                    }
                    CloseHandle(hThread);
                    success = true;
                }
            }
        } while (Thread32Next(snapshot, &te));
    }
    
    CloseHandle(snapshot);
    return success;
}
#endif

bool AdvancedProcessMonitor::setProcessPriority(quint32 pid, int priority)
{
#ifdef _WIN32
    HANDLE hProcess = OpenProcess(PROCESS_SET_INFORMATION, FALSE, pid);
    if (!hProcess) return false;
    
    DWORD priorityClass;
    switch (priority) {
        case 0: priorityClass = IDLE_PRIORITY_CLASS; break;
        case 1: priorityClass = BELOW_NORMAL_PRIORITY_CLASS; break;
        case 2: priorityClass = NORMAL_PRIORITY_CLASS; break;
        case 3: priorityClass = ABOVE_NORMAL_PRIORITY_CLASS; break;
        case 4: priorityClass = HIGH_PRIORITY_CLASS; break;
        case 5: priorityClass = REALTIME_PRIORITY_CLASS; break;
        default: priorityClass = NORMAL_PRIORITY_CLASS;
    }
    
    BOOL result = SetPriorityClass(hProcess, priorityClass);
    CloseHandle(hProcess);
    return result != FALSE;
#else
    Q_UNUSED(pid);
    Q_UNUSED(priority);
    return false;
#endif
}

bool AdvancedProcessMonitor::setProcessAffinity(quint32 pid, quint64 affinityMask)
{
#ifdef _WIN32
    HANDLE hProcess = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_INFORMATION, FALSE, pid);
    if (!hProcess) return false;
    
    BOOL result = SetProcessAffinityMask(hProcess, static_cast<DWORD_PTR>(affinityMask));
    CloseHandle(hProcess);
    return result != FALSE;
#else
    Q_UNUSED(pid);
    Q_UNUSED(affinityMask);
    return false;
#endif
}

ProcessCategory AdvancedProcessMonitor::categorizeProcess(const AdvancedProcessInfo& proc)
{
    QString lowerName = proc.name.toLower();
    QString lowerPath = proc.executablePath.toLower();
    
    // Check for Windows system processes
    if (lowerPath.contains("\\windows\\system32\\") ||
        lowerPath.contains("\\windows\\syswow64\\")) {
        
        // Check if it's a service host
        if (lowerName == "svchost.exe" || lowerName.contains("service")) {
            return ProcessCategory::Services;
        }
        return ProcessCategory::Windows;
    }
    
    // Check for services
    if (lowerName.endsWith("svc.exe") || lowerName.endsWith("service.exe")) {
        return ProcessCategory::Services;
    }
    
    // Check if it has a window (approximation)
    if (proc.hasWindow) {
        return ProcessCategory::Apps;
    }
    
    // Default to background
    return ProcessCategory::Background;
}

bool AdvancedProcessMonitor::isProcessResponding(quint32 pid)
{
#ifdef _WIN32
    // Find a window belonging to this process
    struct EnumData {
        DWORD pid;
        bool responding;
    } data = {static_cast<DWORD>(pid), true};
    
    EnumWindows([](HWND hwnd, LPARAM lParam) -> BOOL {
        auto* data = reinterpret_cast<EnumData*>(lParam);
        DWORD windowPid;
        GetWindowThreadProcessId(hwnd, &windowPid);
        if (windowPid == data->pid && IsWindowVisible(hwnd)) {
            DWORD_PTR result;
            // SendMessageTimeout returns 0 if not responding
            if (SendMessageTimeoutW(hwnd, WM_NULL, 0, 0, 
                SMTO_ABORTIFHUNG, 1000, &result) == 0) {
                data->responding = false;
                return FALSE; // Stop enumeration
            }
        }
        return TRUE;
    }, reinterpret_cast<LPARAM>(&data));
    
    return data.responding;
#else
    Q_UNUSED(pid);
    return true;
#endif
}

QIcon AdvancedProcessMonitor::getProcessIcon(const QString& exePath)
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

QString AdvancedProcessMonitor::getProcessDescription(const QString& exePath)
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

#ifdef _WIN32
QString AdvancedProcessMonitor::getProcessCommandLine(HANDLE hProcess)
{
    // Note: This requires PROCESS_QUERY_INFORMATION | PROCESS_VM_READ
    typedef NTSTATUS(NTAPI* NtQueryInformationProcessPtr)(
        HANDLE, ULONG, PVOID, ULONG, PULONG);
    
    static NtQueryInformationProcessPtr NtQueryInformationProcess = nullptr;
    if (!NtQueryInformationProcess) {
        HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
        if (ntdll) {
            NtQueryInformationProcess = reinterpret_cast<NtQueryInformationProcessPtr>(
                GetProcAddress(ntdll, "NtQueryInformationProcess"));
        }
    }
    
    if (!NtQueryInformationProcess) return QString();
    
    // This is a simplified version - full implementation would read PEB
    Q_UNUSED(hProcess);
    return QString();
}

QString AdvancedProcessMonitor::getProcessUserName(HANDLE hProcess)
{
    HANDLE hToken;
    if (!OpenProcessToken(hProcess, TOKEN_QUERY, &hToken))
        return QString();
    
    DWORD size = 0;
    GetTokenInformation(hToken, TokenUser, nullptr, 0, &size);
    if (size == 0) {
        CloseHandle(hToken);
        return QString();
    }
    
    std::vector<BYTE> buffer(size);
    if (!GetTokenInformation(hToken, TokenUser, buffer.data(), size, &size)) {
        CloseHandle(hToken);
        return QString();
    }
    
    TOKEN_USER* pUser = reinterpret_cast<TOKEN_USER*>(buffer.data());
    
    wchar_t name[256] = {0};
    wchar_t domain[256] = {0};
    DWORD nameSize = 256, domainSize = 256;
    SID_NAME_USE sidType;
    
    if (LookupAccountSidW(nullptr, pUser->User.Sid, name, &nameSize, 
                          domain, &domainSize, &sidType)) {
        CloseHandle(hToken);
        return QString("%1\\%2").arg(QString::fromWCharArray(domain), 
                                     QString::fromWCharArray(name));
    }
    
    CloseHandle(hToken);
    return QString();
}
#endif // _WIN32

void AdvancedProcessMonitor::queryProcesses()
{
    m_processes.clear();
    
#ifdef _WIN32
    FILETIME idleTime, kernelTime, userTime;
    GetSystemTimes(&idleTime, &kernelTime, &userTime);
    
    ULONGLONG sysKernelDiff = fileTimeToUInt64(kernelTime) - fileTimeToUInt64(m_lastSystemKernelTime);
    ULONGLONG sysUserDiff = fileTimeToUInt64(userTime) - fileTimeToUInt64(m_lastSystemUserTime);
    ULONGLONG sysTotalTime = sysKernelDiff + sysUserDiff;
    
    // Enumerate all windows first to determine which processes have windows
    std::set<DWORD> processesWithWindows;
    EnumWindows([](HWND hwnd, LPARAM lParam) -> BOOL {
        auto* pids = reinterpret_cast<std::set<DWORD>*>(lParam);
        if (IsWindowVisible(hwnd)) {
            DWORD pid;
            GetWindowThreadProcessId(hwnd, &pid);
            pids->insert(pid);
        }
        return TRUE;
    }, reinterpret_cast<LPARAM>(&processesWithWindows));
    
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) {
        return;
    }
    
    PROCESSENTRY32W pe;
    pe.dwSize = sizeof(pe);
    
    if (Process32FirstW(snapshot, &pe)) {
        do {
            AdvancedProcessInfo proc;
            proc.pid = pe.th32ProcessID;
            proc.parentPid = pe.th32ParentProcessID;
            proc.name = QString::fromWCharArray(pe.szExeFile);
            proc.threadCount = pe.cntThreads;
            proc.hasWindow = processesWithWindows.count(proc.pid) > 0;
            
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
                    proc.displayName = getProcessDescription(proc.executablePath);
                    proc.icon = getProcessIcon(proc.executablePath);
                }
                
                // Check if 64-bit
                BOOL isWow64 = FALSE;
                IsWow64Process(hProcess, &isWow64);
                proc.is64Bit = !isWow64;
                
                // Memory info
                PROCESS_MEMORY_COUNTERS_EX pmc;
                if (GetProcessMemoryInfo(hProcess, 
                    reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&pmc), sizeof(pmc))) {
                    proc.memoryBytes = pmc.WorkingSetSize;
                    proc.privateBytes = pmc.PrivateUsage;
                    proc.peakMemoryBytes = pmc.PeakWorkingSetSize;
                }
                
                // Handle count
                DWORD handleCount = 0;
                GetProcessHandleCount(hProcess, &handleCount);
                proc.handleCount = handleCount;
                
                // CPU times
                FILETIME createTime, exitTime, procKernelTime, procUserTime;
                if (GetProcessTimes(hProcess, &createTime, &exitTime, 
                    &procKernelTime, &procUserTime)) {
                    
                    proc.startTime = fileTimeToQDateTime(createTime);
                    proc.cpuTimeMs = (fileTimeToUInt64(procKernelTime) + 
                                      fileTimeToUInt64(procUserTime)) / 10000;
                    
                    auto it = m_previousTimes.find(proc.pid);
                    if (it != m_previousTimes.end() && sysTotalTime > 0) {
                        ULONGLONG procKernelDiff = fileTimeToUInt64(procKernelTime) - 
                            fileTimeToUInt64(it->second.kernelTime);
                        ULONGLONG procUserDiff = fileTimeToUInt64(procUserTime) - 
                            fileTimeToUInt64(it->second.userTime);
                        
                        proc.cpuUsage = ((procKernelDiff + procUserDiff) * 100.0) / sysTotalTime;
                        proc.cpuUsageKernel = (procKernelDiff * 100.0) / sysTotalTime;
                        proc.cpuUsageUser = (procUserDiff * 100.0) / sysTotalTime;
                    }
                    
                    m_previousTimes[proc.pid] = {procKernelTime, procUserTime, 0, 0};
                }
                
                // I/O counters
                IO_COUNTERS ioCounters;
                if (GetProcessIoCounters(hProcess, &ioCounters)) {
                    auto it = m_previousTimes.find(proc.pid);
                    if (it != m_previousTimes.end()) {
                        proc.ioReadBytesPerSec = ioCounters.ReadTransferCount - it->second.ioReadBytes;
                        proc.ioWriteBytesPerSec = ioCounters.WriteTransferCount - it->second.ioWriteBytes;
                        it->second.ioReadBytes = ioCounters.ReadTransferCount;
                        it->second.ioWriteBytes = ioCounters.WriteTransferCount;
                    }
                    proc.ioReadBytes = ioCounters.ReadTransferCount;
                    proc.ioWriteBytes = ioCounters.WriteTransferCount;
                }
                
                // User name
                proc.userName = getProcessUserName(hProcess);
                
                // Check if elevated
                HANDLE hToken;
                if (OpenProcessToken(hProcess, TOKEN_QUERY, &hToken)) {
                    TOKEN_ELEVATION elevation;
                    DWORD size;
                    if (GetTokenInformation(hToken, TokenElevation, &elevation, 
                        sizeof(elevation), &size)) {
                        proc.isElevated = elevation.TokenIsElevated != 0;
                    }
                    CloseHandle(hToken);
                }
                
                // Status
                DWORD exitCode;
                if (GetExitCodeProcess(hProcess, &exitCode)) {
                    if (exitCode == STILL_ACTIVE) {
                        // Check if responding (only for processes with windows)
                        if (proc.hasWindow && !isProcessResponding(proc.pid)) {
                            proc.state = ProcessState::NotResponding;
                        } else {
                            proc.state = ProcessState::Running;
                        }
                    } else {
                        proc.state = ProcessState::Terminated;
                    }
                }
                
                CloseHandle(hProcess);
            } else {
                proc.state = ProcessState::Running; // Assume running if we can't access
            }
            
            // Categorize
            proc.category = categorizeProcess(proc);
            
            m_processes.push_back(proc);
            
        } while (Process32NextW(snapshot, &pe));
    }
    
    CloseHandle(snapshot);
    
    m_lastSystemKernelTime = kernelTime;
    m_lastSystemUserTime = userTime;
    
    // Clean up old process times
    std::unordered_map<quint32, ProcessTimes> newTimes;
    for (const auto& proc : m_processes) {
        auto it = m_previousTimes.find(proc.pid);
        if (it != m_previousTimes.end()) {
            newTimes[proc.pid] = it->second;
        }
    }
    m_previousTimes = std::move(newTimes);
    
    // Sort by memory usage by default
    std::sort(m_processes.begin(), m_processes.end(),
        [](const AdvancedProcessInfo& a, const AdvancedProcessInfo& b) {
            return a.memoryBytes > b.memoryBytes;
        });
#endif
}
