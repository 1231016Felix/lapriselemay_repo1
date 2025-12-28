#include "servicemonitor.h"

#ifdef _WIN32
#include <Psapi.h>
#include <TlHelp32.h>
#include <winsvc.h>
#include <winevt.h>
#pragma comment(lib, "wevtapi.lib")
#endif

#include <QDebug>
#include <QThread>
#include <QColor>
#include <algorithm>

// ==================== ServiceInfo Methods ====================

QString ServiceInfo::stateString() const
{
    return ServiceMonitor::stateToString(state);
}

QString ServiceInfo::startTypeString() const
{
    return ServiceMonitor::startTypeToString(startType);
}

QString ServiceInfo::memoryString() const
{
    return ServiceMonitor::formatBytes(resources.memoryUsageBytes);
}

QColor ServiceInfo::stateColor() const
{
    switch (state) {
        case ServiceState::Running: return QColor(0, 200, 83);       // Green
        case ServiceState::Stopped: return QColor(158, 158, 158);    // Gray
        case ServiceState::Paused: return QColor(255, 193, 7);       // Amber
        case ServiceState::StartPending:
        case ServiceState::StopPending:
        case ServiceState::ContinuePending:
        case ServiceState::PausePending:
            return QColor(33, 150, 243);  // Blue
        default: return QColor(158, 158, 158);
    }
}

// ==================== ServiceTableModel ====================

ServiceTableModel::ServiceTableModel(QObject* parent)
    : QAbstractTableModel(parent)
{
}

void ServiceTableModel::setServices(const std::vector<ServiceInfo>& services)
{
    beginResetModel();
    m_allServices = services;
    applyFilter();
    endResetModel();
}

void ServiceTableModel::updateService(const ServiceInfo& service)
{
    for (size_t i = 0; i < m_filteredServices.size(); ++i) {
        if (m_filteredServices[i].serviceName == service.serviceName) {
            m_filteredServices[i] = service;
            emit dataChanged(index(i, 0), index(i, ColCount - 1));
            break;
        }
    }
}

void ServiceTableModel::setFilter(const ServiceFilter& filter)
{
    beginResetModel();
    m_filter = filter;
    applyFilter();
    endResetModel();
}

const ServiceInfo* ServiceTableModel::getService(int row) const
{
    if (row >= 0 && row < static_cast<int>(m_filteredServices.size())) {
        return &m_filteredServices[row];
    }
    return nullptr;
}

const ServiceInfo* ServiceTableModel::getServiceByName(const QString& name) const
{
    for (const auto& svc : m_filteredServices) {
        if (svc.serviceName == name) {
            return &svc;
        }
    }
    return nullptr;
}

int ServiceTableModel::rowCount(const QModelIndex& parent) const
{
    Q_UNUSED(parent)
    return static_cast<int>(m_filteredServices.size());
}

int ServiceTableModel::columnCount(const QModelIndex& parent) const
{
    Q_UNUSED(parent)
    return ColCount;
}

QVariant ServiceTableModel::data(const QModelIndex& index, int role) const
{
    if (!index.isValid() || index.row() >= static_cast<int>(m_filteredServices.size())) {
        return QVariant();
    }
    
    const ServiceInfo& svc = m_filteredServices[index.row()];
    
    if (role == Qt::DisplayRole) {
        switch (index.column()) {
            case ColName: return svc.serviceName;
            case ColDisplayName: return svc.displayName;
            case ColState: return svc.stateString();
            case ColStartType: return svc.startTypeString();
            case ColPID: return svc.processId > 0 ? QString::number(svc.processId) : "-";
            case ColCPU: 
                return svc.state == ServiceState::Running 
                    ? QString("%1%").arg(svc.resources.cpuUsagePercent, 0, 'f', 1) 
                    : "-";
            case ColMemory:
                return svc.state == ServiceState::Running 
                    ? formatBytes(svc.resources.memoryUsageBytes) 
                    : "-";
            case ColDescription: return svc.description;
        }
    }
    else if (role == Qt::ForegroundRole) {
        if (index.column() == ColState) {
            return svc.stateColor();
        }
        if (index.column() == ColCPU && svc.resources.cpuUsagePercent > 5.0) {
            return QColor(255, 152, 0);  // Orange for high CPU
        }
        if (index.column() == ColMemory && svc.resources.memoryUsageBytes > 500 * 1024 * 1024) {
            return QColor(255, 152, 0);  // Orange for high memory
        }
    }
    else if (role == Qt::ToolTipRole) {
        QString tooltip = QString("<b>%1</b><br>").arg(svc.displayName);
        tooltip += QString("Service Name: %1<br>").arg(svc.serviceName);
        tooltip += QString("Status: %1<br>").arg(svc.stateString());
        tooltip += QString("Start Type: %1<br>").arg(svc.startTypeString());
        if (svc.processId > 0) {
            tooltip += QString("PID: %1<br>").arg(svc.processId);
            tooltip += QString("CPU: %1%<br>").arg(svc.resources.cpuUsagePercent, 0, 'f', 1);
            tooltip += QString("Memory: %1<br>").arg(formatBytes(svc.resources.memoryUsageBytes));
            tooltip += QString("Threads: %1<br>").arg(svc.resources.threadCount);
        }
        if (!svc.imagePath.isEmpty()) {
            tooltip += QString("Path: %1").arg(svc.imagePath);
        }
        return tooltip;
    }
    else if (role == Qt::TextAlignmentRole) {
        if (index.column() == ColPID || index.column() == ColCPU || index.column() == ColMemory) {
            return Qt::AlignRight;
        }
    }
    else if (role == Qt::UserRole) {
        // Return raw values for sorting
        switch (index.column()) {
            case ColCPU: return svc.resources.cpuUsagePercent;
            case ColMemory: return svc.resources.memoryUsageBytes;
            case ColState: return static_cast<int>(svc.state);
            case ColStartType: return static_cast<int>(svc.startType);
            default: return QVariant();
        }
    }
    
    return QVariant();
}

QVariant ServiceTableModel::headerData(int section, Qt::Orientation orientation, int role) const
{
    if (orientation == Qt::Horizontal && role == Qt::DisplayRole) {
        switch (section) {
            case ColName: return tr("Name");
            case ColDisplayName: return tr("Display Name");
            case ColState: return tr("Status");
            case ColStartType: return tr("Startup Type");
            case ColPID: return tr("PID");
            case ColCPU: return tr("CPU");
            case ColMemory: return tr("Memory");
            case ColDescription: return tr("Description");
        }
    }
    return QVariant();
}

void ServiceTableModel::sort(int column, Qt::SortOrder order)
{
    m_sortColumn = column;
    m_sortOrder = order;
    
    beginResetModel();
    
    std::sort(m_filteredServices.begin(), m_filteredServices.end(),
        [column, order](const ServiceInfo& a, const ServiceInfo& b) {
            bool less = false;
            switch (column) {
                case ColName: less = a.serviceName.toLower() < b.serviceName.toLower(); break;
                case ColDisplayName: less = a.displayName.toLower() < b.displayName.toLower(); break;
                case ColState: less = static_cast<int>(a.state) < static_cast<int>(b.state); break;
                case ColStartType: less = static_cast<int>(a.startType) < static_cast<int>(b.startType); break;
                case ColPID: less = a.processId < b.processId; break;
                case ColCPU: less = a.resources.cpuUsagePercent < b.resources.cpuUsagePercent; break;
                case ColMemory: less = a.resources.memoryUsageBytes < b.resources.memoryUsageBytes; break;
                case ColDescription: less = a.description.toLower() < b.description.toLower(); break;
                default: less = false;
            }
            return order == Qt::AscendingOrder ? less : !less;
        });
    
    endResetModel();
}

void ServiceTableModel::applyFilter()
{
    m_filteredServices.clear();
    
    QString searchLower = m_filter.searchText.toLower();
    
    for (const auto& svc : m_allServices) {
        // State filter
        if (!m_filter.showRunning && svc.state == ServiceState::Running) continue;
        if (!m_filter.showStopped && svc.state == ServiceState::Stopped) continue;
        if (!m_filter.showDisabled && svc.startType == ServiceStartType::Disabled) continue;
        
        // Windows/third-party filter
        if (m_filter.showWindowsOnly && !svc.isWindowsService) continue;
        if (m_filter.showThirdPartyOnly && svc.isWindowsService) continue;
        
        // High resource filter
        if (m_filter.showHighResourceOnly) {
            if (svc.resources.cpuUsagePercent < m_filter.highCpuThreshold &&
                svc.resources.memoryUsageBytes < m_filter.highMemoryThreshold) {
                continue;
            }
        }
        
        // Text search
        if (!searchLower.isEmpty()) {
            if (!svc.serviceName.toLower().contains(searchLower) &&
                !svc.displayName.toLower().contains(searchLower) &&
                !svc.description.toLower().contains(searchLower)) {
                continue;
            }
        }
        
        m_filteredServices.push_back(svc);
    }
    
    // Apply current sort
    sort(m_sortColumn, m_sortOrder);
}

QString ServiceTableModel::formatBytes(qint64 bytes) const
{
    return ServiceMonitor::formatBytes(bytes);
}

// ==================== ServiceMonitor ====================

ServiceMonitor::ServiceMonitor(QObject* parent)
    : QObject(parent)
    , m_model(std::make_unique<ServiceTableModel>())
{
    qRegisterMetaType<ServiceState>("ServiceState");
    qRegisterMetaType<ServiceInfo>("ServiceInfo");
    qRegisterMetaType<ServiceCrashEvent>("ServiceCrashEvent");
    
    m_isAdmin = isAdmin();
}

ServiceMonitor::~ServiceMonitor()
{
    stopAutoRefresh();
}

bool ServiceMonitor::initialize()
{
    refresh();
    
    // Load crash history from Windows Event Log
    loadCrashHistoryFromEventLog(7);
    
    return true;
}

void ServiceMonitor::startAutoRefresh(int intervalMs)
{
    if (!m_refreshTimer) {
        m_refreshTimer = std::make_unique<QTimer>(this);
        connect(m_refreshTimer.get(), &QTimer::timeout, this, &ServiceMonitor::onRefreshTimer);
    }
    m_refreshTimer->start(intervalMs);
    
    // Also check for crashes periodically
    if (!m_crashCheckTimer) {
        m_crashCheckTimer = std::make_unique<QTimer>(this);
        connect(m_crashCheckTimer.get(), &QTimer::timeout, this, &ServiceMonitor::checkForCrashes);
    }
    m_crashCheckTimer->start(10000);  // Every 10 seconds
}

void ServiceMonitor::stopAutoRefresh()
{
    if (m_refreshTimer) {
        m_refreshTimer->stop();
    }
    if (m_crashCheckTimer) {
        m_crashCheckTimer->stop();
    }
}

bool ServiceMonitor::isAutoRefreshing() const
{
    return m_refreshTimer && m_refreshTimer->isActive();
}

void ServiceMonitor::refresh()
{
    enumerateServices();
    updateResourceUsage();
    m_model->setServices(m_services);
    emit servicesRefreshed();
}

void ServiceMonitor::onRefreshTimer()
{
    // Store previous states for crash detection
    for (const auto& svc : m_services) {
        m_previousStates[svc.serviceName] = svc.state;
    }
    
    refresh();
    detectCrashes();
}

const ServiceInfo* ServiceMonitor::getService(const QString& serviceName) const
{
    for (const auto& svc : m_services) {
        if (svc.serviceName.compare(serviceName, Qt::CaseInsensitive) == 0) {
            return &svc;
        }
    }
    return nullptr;
}

#ifdef _WIN32

void ServiceMonitor::enumerateServices()
{
    m_services.clear();
    
    SC_HANDLE hSCM = OpenSCManager(nullptr, nullptr, SC_MANAGER_ENUMERATE_SERVICE);
    if (!hSCM) {
        setError("Failed to open Service Control Manager");
        return;
    }
    
    DWORD bytesNeeded = 0;
    DWORD servicesReturned = 0;
    DWORD resumeHandle = 0;
    
    // First call to get required buffer size
    EnumServicesStatusExW(hSCM, SC_ENUM_PROCESS_INFO, SERVICE_WIN32,
        SERVICE_STATE_ALL, nullptr, 0, &bytesNeeded, &servicesReturned, &resumeHandle, nullptr);
    
    std::vector<BYTE> buffer(bytesNeeded);
    auto* services = reinterpret_cast<ENUM_SERVICE_STATUS_PROCESSW*>(buffer.data());
    
    if (!EnumServicesStatusExW(hSCM, SC_ENUM_PROCESS_INFO, SERVICE_WIN32,
        SERVICE_STATE_ALL, buffer.data(), bytesNeeded, &bytesNeeded, &servicesReturned, &resumeHandle, nullptr)) {
        CloseServiceHandle(hSCM);
        setError("Failed to enumerate services");
        return;
    }
    
    m_services.reserve(servicesReturned);
    
    for (DWORD i = 0; i < servicesReturned; ++i) {
        ServiceInfo info;
        info.serviceName = QString::fromWCharArray(services[i].lpServiceName);
        info.displayName = QString::fromWCharArray(services[i].lpDisplayName);
        info.processId = services[i].ServiceStatusProcess.dwProcessId;
        
        // Map state
        switch (services[i].ServiceStatusProcess.dwCurrentState) {
            case SERVICE_STOPPED: info.state = ServiceState::Stopped; break;
            case SERVICE_START_PENDING: info.state = ServiceState::StartPending; break;
            case SERVICE_STOP_PENDING: info.state = ServiceState::StopPending; break;
            case SERVICE_RUNNING: info.state = ServiceState::Running; break;
            case SERVICE_CONTINUE_PENDING: info.state = ServiceState::ContinuePending; break;
            case SERVICE_PAUSE_PENDING: info.state = ServiceState::PausePending; break;
            case SERVICE_PAUSED: info.state = ServiceState::Paused; break;
            default: info.state = ServiceState::Unknown;
        }
        
        // Check accepted controls
        DWORD controls = services[i].ServiceStatusProcess.dwControlsAccepted;
        info.canStop = (controls & SERVICE_ACCEPT_STOP) != 0;
        info.canPause = (controls & SERVICE_ACCEPT_PAUSE_CONTINUE) != 0;
        
        // Query additional details
        queryServiceDetails(info);
        
        // Check if it's a Windows service
        info.isWindowsService = isWindowsService(info.serviceName);
        info.isSystemCritical = isSystemCritical(info.serviceName);
        
        m_services.push_back(std::move(info));
    }
    
    CloseServiceHandle(hSCM);
}

void ServiceMonitor::queryServiceDetails(ServiceInfo& service)
{
    SC_HANDLE hSCM = OpenSCManager(nullptr, nullptr, SC_MANAGER_CONNECT);
    if (!hSCM) return;
    
    SC_HANDLE hService = OpenServiceW(hSCM, service.serviceName.toStdWString().c_str(), 
        SERVICE_QUERY_CONFIG | SERVICE_QUERY_STATUS);
    
    if (hService) {
        queryServiceConfig(hService, service);
        CloseServiceHandle(hService);
    }
    
    CloseServiceHandle(hSCM);
}

void ServiceMonitor::queryServiceConfig(SC_HANDLE hService, ServiceInfo& service)
{
    DWORD bytesNeeded = 0;
    
    // Query config
    QueryServiceConfigW(hService, nullptr, 0, &bytesNeeded);
    if (bytesNeeded > 0) {
        std::vector<BYTE> buffer(bytesNeeded);
        auto* config = reinterpret_cast<QUERY_SERVICE_CONFIGW*>(buffer.data());
        
        if (QueryServiceConfigW(hService, config, bytesNeeded, &bytesNeeded)) {
            service.imagePath = QString::fromWCharArray(config->lpBinaryPathName);
            service.account = QString::fromWCharArray(config->lpServiceStartName);
            
            switch (config->dwStartType) {
                case SERVICE_BOOT_START: service.startType = ServiceStartType::Boot; break;
                case SERVICE_SYSTEM_START: service.startType = ServiceStartType::System; break;
                case SERVICE_AUTO_START: service.startType = ServiceStartType::Automatic; break;
                case SERVICE_DEMAND_START: service.startType = ServiceStartType::Manual; break;
                case SERVICE_DISABLED: service.startType = ServiceStartType::Disabled; break;
                default: service.startType = ServiceStartType::Manual;
            }
            
            // Check for delayed start
            SERVICE_DELAYED_AUTO_START_INFO delayInfo;
            DWORD delayBytes = 0;
            if (QueryServiceConfig2W(hService, SERVICE_CONFIG_DELAYED_AUTO_START_INFO,
                reinterpret_cast<LPBYTE>(&delayInfo), sizeof(delayInfo), &delayBytes)) {
                if (delayInfo.fDelayedAutostart && service.startType == ServiceStartType::Automatic) {
                    service.startType = ServiceStartType::AutomaticDelayed;
                    service.isDelayedStart = true;
                }
            }
        }
    }
    
    // Query description
    QueryServiceConfig2W(hService, SERVICE_CONFIG_DESCRIPTION, nullptr, 0, &bytesNeeded);
    if (bytesNeeded > 0) {
        std::vector<BYTE> buffer(bytesNeeded);
        auto* desc = reinterpret_cast<SERVICE_DESCRIPTIONW*>(buffer.data());
        
        if (QueryServiceConfig2W(hService, SERVICE_CONFIG_DESCRIPTION, 
            buffer.data(), bytesNeeded, &bytesNeeded)) {
            if (desc->lpDescription) {
                service.description = QString::fromWCharArray(desc->lpDescription);
            }
        }
    }
}

void ServiceMonitor::updateResourceUsage()
{
    for (auto& svc : m_services) {
        if (svc.state == ServiceState::Running && svc.processId > 0) {
            updateResourceUsageForService(svc);
        }
    }
}

void ServiceMonitor::updateResourceUsageForService(ServiceInfo& service)
{
    HANDLE hProcess = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, 
        FALSE, service.processId);
    
    if (!hProcess) return;
    
    // Memory info
    PROCESS_MEMORY_COUNTERS_EX pmc;
    if (GetProcessMemoryInfo(hProcess, reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&pmc), sizeof(pmc))) {
        service.resources.memoryUsageBytes = pmc.PrivateUsage;
        service.resources.workingSetBytes = pmc.WorkingSetSize;
        if (pmc.WorkingSetSize > service.resources.peakMemoryUsage) {
            service.resources.peakMemoryUsage = pmc.WorkingSetSize;
        }
    }
    
    // Thread and handle count
    DWORD handleCount = 0;
    GetProcessHandleCount(hProcess, &handleCount);
    service.resources.handleCount = handleCount;
    
    // Thread count via snapshot
    HANDLE hSnapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
    if (hSnapshot != INVALID_HANDLE_VALUE) {
        THREADENTRY32 te;
        te.dwSize = sizeof(te);
        int threadCount = 0;
        
        if (Thread32First(hSnapshot, &te)) {
            do {
                if (te.th32OwnerProcessID == service.processId) {
                    ++threadCount;
                }
            } while (Thread32Next(hSnapshot, &te));
        }
        service.resources.threadCount = threadCount;
        CloseHandle(hSnapshot);
    }
    
    // CPU usage (requires two samples, simplified here)
    FILETIME createTime, exitTime, kernelTime, userTime;
    if (GetProcessTimes(hProcess, &createTime, &exitTime, &kernelTime, &userTime)) {
        ULARGE_INTEGER kernel, user;
        kernel.LowPart = kernelTime.dwLowDateTime;
        kernel.HighPart = kernelTime.dwHighDateTime;
        user.LowPart = userTime.dwLowDateTime;
        user.HighPart = userTime.dwHighDateTime;
        
        // Store in history for averaging
        // This is simplified - proper CPU usage requires delta calculations
        auto& history = m_cpuHistory[service.serviceName];
        history.push_back(service.resources.cpuUsagePercent);
        while (history.size() > MAX_CPU_HISTORY) {
            history.pop_front();
        }
        
        // Calculate averages
        if (!history.empty()) {
            double sum = 0;
            for (double v : history) sum += v;
            service.resources.avgCpuUsage5Min = sum / history.size();
        }
    }
    
    CloseHandle(hProcess);
}

bool ServiceMonitor::startService(const QString& serviceName)
{
    if (!m_isAdmin) {
        setError("Administrator privileges required to start services");
        return false;
    }
    
    SC_HANDLE hSCM = OpenSCManager(nullptr, nullptr, SC_MANAGER_CONNECT);
    if (!hSCM) {
        setError("Failed to open Service Control Manager");
        return false;
    }
    
    SC_HANDLE hService = OpenServiceW(hSCM, serviceName.toStdWString().c_str(), 
        SERVICE_START | SERVICE_QUERY_STATUS);
    
    if (!hService) {
        CloseServiceHandle(hSCM);
        setError(QString("Failed to open service: %1").arg(serviceName));
        return false;
    }
    
    bool success = false;
    
    if (StartServiceW(hService, 0, nullptr)) {
        success = waitForServiceState(hService, SERVICE_RUNNING);
        if (success) {
            emit serviceStarted(serviceName);
        }
    } else {
        DWORD err = GetLastError();
        if (err == ERROR_SERVICE_ALREADY_RUNNING) {
            success = true;
        } else {
            setError(QString("Failed to start service: error %1").arg(err));
        }
    }
    
    CloseServiceHandle(hService);
    CloseServiceHandle(hSCM);
    
    if (success) refresh();
    return success;
}

bool ServiceMonitor::stopService(const QString& serviceName)
{
    if (!m_isAdmin) {
        setError("Administrator privileges required to stop services");
        return false;
    }
    
    if (isSystemCritical(serviceName)) {
        setError(QString("Cannot stop system-critical service: %1").arg(serviceName));
        return false;
    }
    
    bool success = controlService(serviceName, SERVICE_CONTROL_STOP);
    if (success) {
        emit serviceStopped(serviceName);
        refresh();
    }
    return success;
}

bool ServiceMonitor::restartService(const QString& serviceName)
{
    if (stopService(serviceName)) {
        QThread::msleep(500);  // Brief pause
        if (startService(serviceName)) {
            emit serviceRestarted(serviceName);
            return true;
        }
    }
    return false;
}

bool ServiceMonitor::pauseService(const QString& serviceName)
{
    return controlService(serviceName, SERVICE_CONTROL_PAUSE);
}

bool ServiceMonitor::resumeService(const QString& serviceName)
{
    return controlService(serviceName, SERVICE_CONTROL_CONTINUE);
}

bool ServiceMonitor::controlService(const QString& serviceName, DWORD control)
{
    if (!m_isAdmin) {
        setError("Administrator privileges required");
        return false;
    }
    
    SC_HANDLE hSCM = OpenSCManager(nullptr, nullptr, SC_MANAGER_CONNECT);
    if (!hSCM) return false;
    
    SC_HANDLE hService = OpenServiceW(hSCM, serviceName.toStdWString().c_str(),
        SERVICE_STOP | SERVICE_PAUSE_CONTINUE | SERVICE_QUERY_STATUS);
    
    if (!hService) {
        CloseServiceHandle(hSCM);
        return false;
    }
    
    SERVICE_STATUS status;
    bool success = ControlService(hService, control, &status) != 0;
    
    if (success) {
        DWORD targetState = SERVICE_STOPPED;
        if (control == SERVICE_CONTROL_PAUSE) targetState = SERVICE_PAUSED;
        else if (control == SERVICE_CONTROL_CONTINUE) targetState = SERVICE_RUNNING;
        
        success = waitForServiceState(hService, targetState);
    }
    
    CloseServiceHandle(hService);
    CloseServiceHandle(hSCM);
    
    return success;
}

bool ServiceMonitor::waitForServiceState(SC_HANDLE hService, DWORD desiredState, int timeoutMs)
{
    SERVICE_STATUS_PROCESS ssp;
    DWORD bytesNeeded;
    DWORD startTime = GetTickCount();
    
    while (true) {
        if (!QueryServiceStatusEx(hService, SC_STATUS_PROCESS_INFO,
            reinterpret_cast<LPBYTE>(&ssp), sizeof(ssp), &bytesNeeded)) {
            return false;
        }
        
        if (ssp.dwCurrentState == desiredState) {
            return true;
        }
        
        if (GetTickCount() - startTime > static_cast<DWORD>(timeoutMs)) {
            setError("Service operation timed out");
            return false;
        }
        
        QThread::msleep(250);
    }
}

bool ServiceMonitor::setStartType(const QString& serviceName, ServiceStartType startType)
{
    if (!m_isAdmin) {
        setError("Administrator privileges required");
        return false;
    }
    
    SC_HANDLE hSCM = OpenSCManager(nullptr, nullptr, SC_MANAGER_CONNECT);
    if (!hSCM) return false;
    
    SC_HANDLE hService = OpenServiceW(hSCM, serviceName.toStdWString().c_str(), SERVICE_CHANGE_CONFIG);
    if (!hService) {
        CloseServiceHandle(hSCM);
        return false;
    }
    
    DWORD dwStartType;
    switch (startType) {
        case ServiceStartType::Automatic:
        case ServiceStartType::AutomaticDelayed:
            dwStartType = SERVICE_AUTO_START; break;
        case ServiceStartType::Manual: dwStartType = SERVICE_DEMAND_START; break;
        case ServiceStartType::Disabled: dwStartType = SERVICE_DISABLED; break;
        default: dwStartType = SERVICE_NO_CHANGE;
    }
    
    bool success = ChangeServiceConfigW(hService, SERVICE_NO_CHANGE, dwStartType,
        SERVICE_NO_CHANGE, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr) != 0;
    
    // Handle delayed start
    if (success && startType == ServiceStartType::AutomaticDelayed) {
        SERVICE_DELAYED_AUTO_START_INFO delayInfo = { TRUE };
        ChangeServiceConfig2W(hService, SERVICE_CONFIG_DELAYED_AUTO_START_INFO, &delayInfo);
    }
    
    CloseServiceHandle(hService);
    CloseServiceHandle(hSCM);
    
    if (success) refresh();
    return success;
}

void ServiceMonitor::loadCrashHistoryFromEventLog(int days)
{
    // Query Windows Event Log for service crash events
    // Event ID 7034 = Service terminated unexpectedly
    // Event ID 7031 = Service terminated unexpectedly and will be restarted
    
    const wchar_t* query = L"<QueryList>"
        L"<Query Id='0'>"
        L"<Select Path='System'>*[System[(EventID=7031 or EventID=7034)]]</Select>"
        L"</Query></QueryList>";
    
    EVT_HANDLE hResults = EvtQuery(nullptr, nullptr, query, 
        EvtQueryChannelPath | EvtQueryReverseDirection);
    
    if (!hResults) return;
    
    EVT_HANDLE events[100];
    DWORD returned = 0;
    
    QDateTime cutoff = QDateTime::currentDateTime().addDays(-days);
    
    while (EvtNext(hResults, 100, events, INFINITE, 0, &returned)) {
        for (DWORD i = 0; i < returned; ++i) {
            // Parse event and add to crash history
            // (Simplified - actual implementation would parse XML)
            ServiceCrashEvent crash;
            crash.timestamp = QDateTime::currentDateTime();  // Would be parsed from event
            crash.eventId = 7034;
            
            if (m_crashEvents.size() >= MAX_CRASH_EVENTS) {
                m_crashEvents.pop_back();
            }
            m_crashEvents.push_front(crash);
            
            EvtClose(events[i]);
        }
    }
    
    EvtClose(hResults);
}

#else
// Non-Windows stubs
void ServiceMonitor::enumerateServices() {}
void ServiceMonitor::queryServiceDetails(ServiceInfo&) {}
void ServiceMonitor::updateResourceUsage() {}
void ServiceMonitor::updateResourceUsageForService(ServiceInfo&) {}
bool ServiceMonitor::startService(const QString&) { return false; }
bool ServiceMonitor::stopService(const QString&) { return false; }
bool ServiceMonitor::restartService(const QString&) { return false; }
bool ServiceMonitor::pauseService(const QString&) { return false; }
bool ServiceMonitor::resumeService(const QString&) { return false; }
bool ServiceMonitor::controlService(const QString&, DWORD) { return false; }
bool ServiceMonitor::waitForServiceState(SC_HANDLE, DWORD, int) { return false; }
bool ServiceMonitor::setStartType(const QString&, ServiceStartType) { return false; }
void ServiceMonitor::loadCrashHistoryFromEventLog(int) {}
#endif

void ServiceMonitor::checkForCrashes()
{
    detectCrashes();
}

void ServiceMonitor::detectCrashes()
{
    QDateTime now = QDateTime::currentDateTime();
    
    for (const auto& svc : m_services) {
        auto it = m_previousStates.find(svc.serviceName);
        if (it != m_previousStates.end()) {
            ServiceState prevState = it->second;
            
            // Detect unexpected stop (was running, now stopped)
            if (prevState == ServiceState::Running && svc.state == ServiceState::Stopped) {
                ServiceCrashEvent crash;
                crash.serviceName = svc.serviceName;
                crash.displayName = svc.displayName;
                crash.timestamp = now;
                crash.previousState = prevState;
                crash.failureReason = "Service stopped unexpectedly";
                
                // Count crashes in last 24h
                int count = 0;
                for (const auto& evt : m_crashEvents) {
                    if (evt.serviceName == svc.serviceName &&
                        evt.timestamp.secsTo(now) < 86400) {
                        ++count;
                    }
                }
                crash.crashCount = count + 1;
                
                m_crashEvents.push_front(crash);
                while (m_crashEvents.size() > MAX_CRASH_EVENTS) {
                    m_crashEvents.pop_back();
                }
                
                emit serviceCrashed(crash);
            }
        }
    }
}

std::vector<ServiceInfo> ServiceMonitor::getHighCpuServices(double threshold) const
{
    std::vector<ServiceInfo> result;
    for (const auto& svc : m_services) {
        if (svc.resources.cpuUsagePercent >= threshold) {
            result.push_back(svc);
        }
    }
    return result;
}

std::vector<ServiceInfo> ServiceMonitor::getHighMemoryServices(qint64 threshold) const
{
    std::vector<ServiceInfo> result;
    for (const auto& svc : m_services) {
        if (svc.resources.memoryUsageBytes >= threshold) {
            result.push_back(svc);
        }
    }
    return result;
}

std::vector<ServiceInfo> ServiceMonitor::getTopByCpu(int count) const
{
    auto sorted = m_services;
    std::partial_sort(sorted.begin(), sorted.begin() + std::min(count, static_cast<int>(sorted.size())),
        sorted.end(), [](const ServiceInfo& a, const ServiceInfo& b) {
            return a.resources.cpuUsagePercent > b.resources.cpuUsagePercent;
        });
    sorted.resize(std::min(count, static_cast<int>(sorted.size())));
    return sorted;
}

std::vector<ServiceInfo> ServiceMonitor::getTopByMemory(int count) const
{
    auto sorted = m_services;
    std::partial_sort(sorted.begin(), sorted.begin() + std::min(count, static_cast<int>(sorted.size())),
        sorted.end(), [](const ServiceInfo& a, const ServiceInfo& b) {
            return a.resources.memoryUsageBytes > b.resources.memoryUsageBytes;
        });
    sorted.resize(std::min(count, static_cast<int>(sorted.size())));
    return sorted;
}

std::vector<ServiceCrashEvent> ServiceMonitor::getCrashEvents(const QString& serviceName) const
{
    std::vector<ServiceCrashEvent> result;
    for (const auto& evt : m_crashEvents) {
        if (evt.serviceName.compare(serviceName, Qt::CaseInsensitive) == 0) {
            result.push_back(evt);
        }
    }
    return result;
}

std::vector<ServiceInfo> ServiceMonitor::getRecentlyCrashedServices(int hours) const
{
    std::vector<ServiceInfo> result;
    QDateTime cutoff = QDateTime::currentDateTime().addSecs(-hours * 3600);
    
    std::set<QString> crashedNames;
    for (const auto& evt : m_crashEvents) {
        if (evt.timestamp >= cutoff) {
            crashedNames.insert(evt.serviceName);
        }
    }
    
    for (const auto& svc : m_services) {
        if (crashedNames.count(svc.serviceName)) {
            result.push_back(svc);
        }
    }
    
    return result;
}

void ServiceMonitor::clearCrashHistory()
{
    m_crashEvents.clear();
}

// ==================== Static Utility Methods ====================

bool ServiceMonitor::isAdmin()
{
#ifdef _WIN32
    BOOL isAdmin = FALSE;
    SID_IDENTIFIER_AUTHORITY ntAuth = SECURITY_NT_AUTHORITY;
    PSID adminGroup = nullptr;
    
    if (AllocateAndInitializeSid(&ntAuth, 2, SECURITY_BUILTIN_DOMAIN_RID,
        DOMAIN_ALIAS_RID_ADMINS, 0, 0, 0, 0, 0, 0, &adminGroup)) {
        CheckTokenMembership(nullptr, adminGroup, &isAdmin);
        FreeSid(adminGroup);
    }
    return isAdmin != FALSE;
#else
    return false;
#endif
}

QString ServiceMonitor::stateToString(ServiceState state)
{
    switch (state) {
        case ServiceState::Stopped: return QObject::tr("Stopped");
        case ServiceState::StartPending: return QObject::tr("Starting...");
        case ServiceState::StopPending: return QObject::tr("Stopping...");
        case ServiceState::Running: return QObject::tr("Running");
        case ServiceState::ContinuePending: return QObject::tr("Resuming...");
        case ServiceState::PausePending: return QObject::tr("Pausing...");
        case ServiceState::Paused: return QObject::tr("Paused");
        default: return QObject::tr("Unknown");
    }
}

QString ServiceMonitor::startTypeToString(ServiceStartType type)
{
    switch (type) {
        case ServiceStartType::Boot: return QObject::tr("Boot");
        case ServiceStartType::System: return QObject::tr("System");
        case ServiceStartType::Automatic: return QObject::tr("Automatic");
        case ServiceStartType::AutomaticDelayed: return QObject::tr("Automatic (Delayed)");
        case ServiceStartType::Manual: return QObject::tr("Manual");
        case ServiceStartType::Disabled: return QObject::tr("Disabled");
        default: return QObject::tr("Unknown");
    }
}

QString ServiceMonitor::formatBytes(qint64 bytes)
{
    if (bytes < 1024) return QString("%1 B").arg(bytes);
    if (bytes < 1024 * 1024) return QString("%1 KB").arg(bytes / 1024.0, 0, 'f', 1);
    if (bytes < 1024 * 1024 * 1024) return QString("%1 MB").arg(bytes / (1024.0 * 1024.0), 0, 'f', 1);
    return QString("%1 GB").arg(bytes / (1024.0 * 1024.0 * 1024.0), 0, 'f', 2);
}

bool ServiceMonitor::isWindowsService(const QString& serviceName)
{
    // List of known Windows services (partial)
    static const QSet<QString> windowsServices = {
        "wuauserv", "bits", "cryptsvc", "msiserver", "trustedinstaller",
        "wsearch", "sysmain", "themes", "audiosrv", "audioendpointbuilder",
        "spooler", "lanmanserver", "lanmanworkstation", "netlogon",
        "dnscache", "dhcp", "eventlog", "plugplay", "power",
        "profiling", "schedule", "sens", "sharedaccess", "sppsvc",
        "wdiservicehost", "wdisystemhost", "wecsvc", "windefend",
        "winmgmt", "wlansvc", "wuauserv", "mpssvc", "bfe"
    };
    
    return windowsServices.contains(serviceName.toLower());
}

bool ServiceMonitor::isSystemCritical(const QString& serviceName)
{
    static const QSet<QString> criticalServices = {
        "rpcss", "dcomlaunch", "lsass", "samss", "plugplay",
        "eventlog", "power", "profiling", "winmgmt", "cryptsvc"
    };
    
    return criticalServices.contains(serviceName.toLower());
}

void ServiceMonitor::setError(const QString& error)
{
    m_lastError = error;
    emit errorOccurred(error);
    qWarning() << "ServiceMonitor error:" << error;
}
