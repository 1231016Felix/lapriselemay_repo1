#include "startupmonitor.h"

#ifdef _WIN32
#include <Windows.h>
#include <ShlObj.h>
#include <Shlwapi.h>
#include <shellapi.h>
#include <taskschd.h>
#include <comdef.h>
#include <Wincrypt.h>
#include <Softpub.h>
#include <wintrust.h>
#include <Winsvc.h>

#pragma comment(lib, "shlwapi.lib")
#pragma comment(lib, "taskschd.lib")
#pragma comment(lib, "comsuppw.lib")
#pragma comment(lib, "wintrust.lib")
#pragma comment(lib, "crypt32.lib")
#pragma comment(lib, "version.lib")
#endif

#include <QFileInfo>
#include <QDir>
#include <QFileIconProvider>
#include <QStandardPaths>
#include <QProcess>
#include <QDesktopServices>
#include <QUrl>
#include <QSettings>
#include <QCoreApplication>
#include <algorithm>

// ============================================================================
// StartupTableModel Implementation
// ============================================================================

StartupTableModel::StartupTableModel(QObject *parent)
    : QAbstractTableModel(parent)
{
}

void StartupTableModel::setEntries(const std::vector<StartupEntry>& entries)
{
    beginResetModel();
    m_entries = entries;
    endResetModel();
}

StartupEntry* StartupTableModel::getEntry(int row)
{
    if (row >= 0 && row < static_cast<int>(m_entries.size())) {
        return &m_entries[row];
    }
    return nullptr;
}

const StartupEntry* StartupTableModel::getEntry(int row) const
{
    if (row >= 0 && row < static_cast<int>(m_entries.size())) {
        return &m_entries[row];
    }
    return nullptr;
}

int StartupTableModel::rowCount(const QModelIndex&) const
{
    return static_cast<int>(m_entries.size());
}

int StartupTableModel::columnCount(const QModelIndex&) const
{
    return ColCount;
}

QVariant StartupTableModel::data(const QModelIndex &index, int role) const
{
    if (!index.isValid() || index.row() >= static_cast<int>(m_entries.size()))
        return QVariant();

    const auto& entry = m_entries[index.row()];
    
    if (role == Qt::DisplayRole) {
        switch (index.column()) {
            case ColEnabled: return QVariant();
            case ColName: return entry.name;
            case ColPublisher: return entry.publisher.isEmpty() ? tr("Unknown") : entry.publisher;
            case ColStatus: return entry.isEnabled ? tr("Enabled") : tr("Disabled");
            case ColImpact: return StartupMonitor::impactToString(entry.impact);
            case ColSource: return StartupMonitor::sourceToString(entry.source);
            case ColCommand: return entry.command;
        }
    }
    else if (role == Qt::CheckStateRole && index.column() == ColEnabled) {
        return entry.isEnabled ? Qt::Checked : Qt::Unchecked;
    }
    else if (role == Qt::DecorationRole && index.column() == ColName) {
        return entry.icon;
    }
    else if (role == Qt::ForegroundRole) {
        if (!entry.isValid) {
            return QColor(255, 100, 100); // Red for invalid entries
        }
        if (!entry.isEnabled) {
            return QColor(128, 128, 128); // Gray for disabled
        }
    }
    else if (role == Qt::ToolTipRole) {
        QString tooltip = QString("<b>%1</b><br>").arg(entry.name);
        tooltip += QString("<b>Command:</b> %1<br>").arg(entry.command);
        tooltip += QString("<b>Publisher:</b> %1<br>").arg(entry.publisher);
        tooltip += QString("<b>Location:</b> %1<br>").arg(entry.sourceLocation);
        if (!entry.isValid) {
            tooltip += "<br><font color='red'>⚠️ Executable not found!</font>";
        }
        return tooltip;
    }
    else if (role == Qt::TextAlignmentRole) {
        if (index.column() == ColImpact || index.column() == ColStatus) {
            return Qt::AlignCenter;
        }
    }
    else if (role == Qt::BackgroundRole) {
        if (entry.impact == StartupImpact::High && entry.isEnabled) {
            return QColor(80, 40, 40); // Dark red background for high impact
        }
    }
    
    return QVariant();
}

QVariant StartupTableModel::headerData(int section, Qt::Orientation orientation, int role) const
{
    if (orientation != Qt::Horizontal || role != Qt::DisplayRole)
        return QVariant();

    switch (section) {
        case ColEnabled: return QString();
        case ColName: return tr("Name");
        case ColPublisher: return tr("Publisher");
        case ColStatus: return tr("Status");
        case ColImpact: return tr("Impact");
        case ColSource: return tr("Source");
        case ColCommand: return tr("Command");
    }
    return QVariant();
}

Qt::ItemFlags StartupTableModel::flags(const QModelIndex &index) const
{
    Qt::ItemFlags flags = QAbstractTableModel::flags(index);
    
    if (index.column() == ColEnabled) {
        flags |= Qt::ItemIsUserCheckable;
    }
    
    return flags;
}

bool StartupTableModel::setData(const QModelIndex &index, const QVariant &value, int role)
{
    if (!index.isValid() || index.column() != ColEnabled || role != Qt::CheckStateRole)
        return false;
    
    bool enabled = (value.toInt() == Qt::Checked);
    emit entryToggled(index.row(), enabled);
    return true;
}


// ============================================================================
// StartupMonitor Implementation
// ============================================================================

StartupMonitor::StartupMonitor(QObject *parent)
    : QObject(parent)
    , m_model(std::make_unique<StartupTableModel>())
{
    // Setup backup path for disabled entries
    m_disabledBackupPath = QStandardPaths::writableLocation(QStandardPaths::AppDataLocation) 
                           + "/disabled_startup";
    QDir().mkpath(m_disabledBackupPath);
    
    // Connect model signals
    connect(m_model.get(), &StartupTableModel::entryToggled,
            this, [this](int row, bool enabled) {
        setEnabled(row, enabled);
    });
    
    refresh();
}

StartupMonitor::~StartupMonitor() = default;

void StartupMonitor::refresh()
{
    m_entries.clear();
    
#ifdef _WIN32
    // Scan all sources
    scanRegistry(StartupSource::RegistryCurrentUser);
    scanRegistry(StartupSource::RegistryLocalMachine);
    scanRegistry(StartupSource::RegistryCurrentUserOnce);
    scanRegistry(StartupSource::RegistryLocalMachineOnce);
    scanStartupFolders();
    scanTaskScheduler();
    scanServices();
#endif
    
    // Sort by name
    std::sort(m_entries.begin(), m_entries.end(),
        [](const StartupEntry& a, const StartupEntry& b) {
            return a.name.toLower() < b.name.toLower();
        });
    
    m_model->setEntries(m_entries);
    emit refreshed();
}

bool StartupMonitor::isAdmin()
{
#ifdef _WIN32
    BOOL isAdmin = FALSE;
    PSID adminGroup = NULL;
    
    SID_IDENTIFIER_AUTHORITY ntAuthority = SECURITY_NT_AUTHORITY;
    if (AllocateAndInitializeSid(&ntAuthority, 2,
            SECURITY_BUILTIN_DOMAIN_RID, DOMAIN_ALIAS_RID_ADMINS,
            0, 0, 0, 0, 0, 0, &adminGroup)) {
        CheckTokenMembership(NULL, adminGroup, &isAdmin);
        FreeSid(adminGroup);
    }
    return isAdmin == TRUE;
#else
    return false;
#endif
}

QString StartupMonitor::sourceToString(StartupSource source)
{
    switch (source) {
        case StartupSource::RegistryCurrentUser: return "Registry (User)";
        case StartupSource::RegistryLocalMachine: return "Registry (System)";
        case StartupSource::RegistryCurrentUserOnce: return "Registry RunOnce (User)";
        case StartupSource::RegistryLocalMachineOnce: return "Registry RunOnce (System)";
        case StartupSource::StartupFolderUser: return "Startup Folder (User)";
        case StartupSource::StartupFolderCommon: return "Startup Folder (All Users)";
        case StartupSource::TaskScheduler: return "Task Scheduler";
        case StartupSource::Services: return "Windows Service";
        default: return "Unknown";
    }
}

QString StartupMonitor::impactToString(StartupImpact impact)
{
    switch (impact) {
        case StartupImpact::None: return "None";
        case StartupImpact::Low: return "Low";
        case StartupImpact::Medium: return "Medium";
        case StartupImpact::High: return "High";
        default: return "Not measured";
    }
}

int StartupMonitor::enabledCount() const
{
    return std::count_if(m_entries.begin(), m_entries.end(),
        [](const StartupEntry& e) { return e.isEnabled; });
}

int StartupMonitor::disabledCount() const
{
    return std::count_if(m_entries.begin(), m_entries.end(),
        [](const StartupEntry& e) { return !e.isEnabled; });
}

int StartupMonitor::highImpactCount() const
{
    return std::count_if(m_entries.begin(), m_entries.end(),
        [](const StartupEntry& e) { return e.isEnabled && e.impact == StartupImpact::High; });
}


QString StartupMonitor::getRegistryPath(StartupSource source) const
{
    switch (source) {
        case StartupSource::RegistryCurrentUser:
            return "HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        case StartupSource::RegistryLocalMachine:
            return "HKEY_LOCAL_MACHINE\\Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        case StartupSource::RegistryCurrentUserOnce:
            return "HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce";
        case StartupSource::RegistryLocalMachineOnce:
            return "HKEY_LOCAL_MACHINE\\Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce";
        default:
            return QString();
    }
}

QString StartupMonitor::getStartupFolderPath(bool allUsers) const
{
#ifdef _WIN32
    wchar_t path[MAX_PATH];
    if (SUCCEEDED(SHGetFolderPathW(NULL, 
            allUsers ? CSIDL_COMMON_STARTUP : CSIDL_STARTUP, 
            NULL, 0, path))) {
        return QString::fromWCharArray(path);
    }
#endif
    return QString();
}

void StartupMonitor::scanRegistry(StartupSource source)
{
#ifdef _WIN32
    HKEY hRootKey = (source == StartupSource::RegistryCurrentUser || 
                     source == StartupSource::RegistryCurrentUserOnce) 
                    ? HKEY_CURRENT_USER : HKEY_LOCAL_MACHINE;
    
    QString subKey;
    switch (source) {
        case StartupSource::RegistryCurrentUser:
        case StartupSource::RegistryLocalMachine:
            subKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
            break;
        case StartupSource::RegistryCurrentUserOnce:
        case StartupSource::RegistryLocalMachineOnce:
            subKey = "Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce";
            break;
        default:
            return;
    }
    
    HKEY hKey;
    if (RegOpenKeyExW(hRootKey, subKey.toStdWString().c_str(), 
                      0, KEY_READ, &hKey) != ERROR_SUCCESS) {
        return;
    }
    
    DWORD index = 0;
    wchar_t valueName[16383];
    DWORD valueNameLen;
    BYTE valueData[32767];
    DWORD valueDataLen;
    DWORD valueType;
    
    while (true) {
        valueNameLen = sizeof(valueName) / sizeof(wchar_t);
        valueDataLen = sizeof(valueData);
        
        LONG result = RegEnumValueW(hKey, index++, valueName, &valueNameLen,
                                    NULL, &valueType, valueData, &valueDataLen);
        
        if (result == ERROR_NO_MORE_ITEMS) break;
        if (result != ERROR_SUCCESS) continue;
        if (valueType != REG_SZ && valueType != REG_EXPAND_SZ) continue;
        
        StartupEntry entry;
        entry.name = QString::fromWCharArray(valueName);
        entry.command = QString::fromWCharArray(reinterpret_cast<wchar_t*>(valueData));
        entry.source = source;
        entry.sourceLocation = getRegistryPath(source) + "\\" + entry.name;
        entry.isEnabled = true;
        entry.isElevated = (source == StartupSource::RegistryLocalMachine || 
                           source == StartupSource::RegistryLocalMachineOnce);
        
        extractExecutableInfo(entry);
        entry.impact = estimateImpact(entry);
        
        m_entries.push_back(entry);
    }
    
    RegCloseKey(hKey);
    
    // Also scan disabled entries (ApprovedRun)
    QString approvedKey = (hRootKey == HKEY_CURRENT_USER) 
        ? "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run"
        : "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run";
    
    HKEY hApprovedKey;
    if (RegOpenKeyExW(hRootKey, approvedKey.toStdWString().c_str(),
                      0, KEY_READ, &hApprovedKey) == ERROR_SUCCESS) {
        
        // Check each entry we found to see if it's disabled
        for (auto& entry : m_entries) {
            if (entry.source != source) continue;
            
            BYTE data[12];
            DWORD dataSize = sizeof(data);
            if (RegQueryValueExW(hApprovedKey, entry.name.toStdWString().c_str(),
                                 NULL, NULL, data, &dataSize) == ERROR_SUCCESS) {
                // First 4 bytes indicate enabled/disabled state
                // 02 00 00 00 = enabled, 03 00 00 00 = disabled
                if (dataSize >= 4 && data[0] == 0x03) {
                    entry.isEnabled = false;
                    entry.impact = StartupImpact::None;
                }
            }
        }
        
        RegCloseKey(hApprovedKey);
    }
#endif
}


void StartupMonitor::scanStartupFolders()
{
#ifdef _WIN32
    // Scan user startup folder
    QString userStartup = getStartupFolderPath(false);
    if (!userStartup.isEmpty()) {
        QDir dir(userStartup);
        for (const auto& fileInfo : dir.entryInfoList(QDir::Files | QDir::NoDotAndDotDot)) {
            StartupEntry entry;
            entry.name = fileInfo.baseName();
            entry.source = StartupSource::StartupFolderUser;
            entry.sourceLocation = fileInfo.absoluteFilePath();
            entry.isEnabled = true;
            
            // Handle .lnk shortcuts
            if (fileInfo.suffix().toLower() == "lnk") {
                // Resolve shortcut target
                IShellLinkW* psl = nullptr;
                IPersistFile* ppf = nullptr;
                
                CoInitialize(NULL);
                
                if (SUCCEEDED(CoCreateInstance(CLSID_ShellLink, NULL, CLSCTX_INPROC_SERVER,
                                               IID_IShellLinkW, (LPVOID*)&psl))) {
                    if (SUCCEEDED(psl->QueryInterface(IID_IPersistFile, (LPVOID*)&ppf))) {
                        if (SUCCEEDED(ppf->Load(fileInfo.absoluteFilePath().toStdWString().c_str(), 
                                               STGM_READ))) {
                            wchar_t targetPath[MAX_PATH];
                            wchar_t arguments[1024];
                            
                            psl->GetPath(targetPath, MAX_PATH, NULL, 0);
                            psl->GetArguments(arguments, 1024);
                            
                            entry.executablePath = QString::fromWCharArray(targetPath);
                            entry.arguments = QString::fromWCharArray(arguments);
                            entry.command = entry.executablePath;
                            if (!entry.arguments.isEmpty()) {
                                entry.command += " " + entry.arguments;
                            }
                        }
                        ppf->Release();
                    }
                    psl->Release();
                }
                
                CoUninitialize();
            } else {
                entry.command = fileInfo.absoluteFilePath();
                entry.executablePath = fileInfo.absoluteFilePath();
            }
            
            extractExecutableInfo(entry);
            entry.impact = estimateImpact(entry);
            
            m_entries.push_back(entry);
        }
    }
    
    // Scan common (all users) startup folder
    QString commonStartup = getStartupFolderPath(true);
    if (!commonStartup.isEmpty() && commonStartup != userStartup) {
        QDir dir(commonStartup);
        for (const auto& fileInfo : dir.entryInfoList(QDir::Files | QDir::NoDotAndDotDot)) {
            StartupEntry entry;
            entry.name = fileInfo.baseName();
            entry.source = StartupSource::StartupFolderCommon;
            entry.sourceLocation = fileInfo.absoluteFilePath();
            entry.isEnabled = true;
            entry.isElevated = true;
            
            if (fileInfo.suffix().toLower() == "lnk") {
                IShellLinkW* psl = nullptr;
                IPersistFile* ppf = nullptr;
                
                CoInitialize(NULL);
                
                if (SUCCEEDED(CoCreateInstance(CLSID_ShellLink, NULL, CLSCTX_INPROC_SERVER,
                                               IID_IShellLinkW, (LPVOID*)&psl))) {
                    if (SUCCEEDED(psl->QueryInterface(IID_IPersistFile, (LPVOID*)&ppf))) {
                        if (SUCCEEDED(ppf->Load(fileInfo.absoluteFilePath().toStdWString().c_str(), 
                                               STGM_READ))) {
                            wchar_t targetPath[MAX_PATH];
                            wchar_t arguments[1024];
                            
                            psl->GetPath(targetPath, MAX_PATH, NULL, 0);
                            psl->GetArguments(arguments, 1024);
                            
                            entry.executablePath = QString::fromWCharArray(targetPath);
                            entry.arguments = QString::fromWCharArray(arguments);
                            entry.command = entry.executablePath;
                            if (!entry.arguments.isEmpty()) {
                                entry.command += " " + entry.arguments;
                            }
                        }
                        ppf->Release();
                    }
                    psl->Release();
                }
                
                CoUninitialize();
            } else {
                entry.command = fileInfo.absoluteFilePath();
                entry.executablePath = fileInfo.absoluteFilePath();
            }
            
            extractExecutableInfo(entry);
            entry.impact = estimateImpact(entry);
            
            m_entries.push_back(entry);
        }
    }
#endif
}


void StartupMonitor::scanTaskScheduler()
{
#ifdef _WIN32
    CoInitialize(NULL);
    
    ITaskService* pService = nullptr;
    if (FAILED(CoCreateInstance(CLSID_TaskScheduler, NULL, CLSCTX_INPROC_SERVER,
                                IID_ITaskService, (void**)&pService))) {
        CoUninitialize();
        return;
    }
    
    if (FAILED(pService->Connect(_variant_t(), _variant_t(), _variant_t(), _variant_t()))) {
        pService->Release();
        CoUninitialize();
        return;
    }
    
    ITaskFolder* pRootFolder = nullptr;
    if (FAILED(pService->GetFolder(_bstr_t(L"\\"), &pRootFolder))) {
        pService->Release();
        CoUninitialize();
        return;
    }
    
    IRegisteredTaskCollection* pTaskCollection = nullptr;
    if (SUCCEEDED(pRootFolder->GetTasks(TASK_ENUM_HIDDEN, &pTaskCollection))) {
        LONG numTasks = 0;
        pTaskCollection->get_Count(&numTasks);
        
        for (LONG i = 1; i <= numTasks; i++) {
            IRegisteredTask* pTask = nullptr;
            if (SUCCEEDED(pTaskCollection->get_Item(_variant_t(i), &pTask))) {
                BSTR bstrName = nullptr;
                TASK_STATE state;
                
                pTask->get_Name(&bstrName);
                pTask->get_State(&state);
                
                ITaskDefinition* pDef = nullptr;
                if (SUCCEEDED(pTask->get_Definition(&pDef))) {
                    ITriggerCollection* pTriggers = nullptr;
                    if (SUCCEEDED(pDef->get_Triggers(&pTriggers))) {
                        LONG numTriggers = 0;
                        pTriggers->get_Count(&numTriggers);
                        
                        bool isStartupTask = false;
                        for (LONG t = 1; t <= numTriggers; t++) {
                            ITrigger* pTrigger = nullptr;
                            if (SUCCEEDED(pTriggers->get_Item(t, &pTrigger))) {
                                TASK_TRIGGER_TYPE2 type;
                                pTrigger->get_Type(&type);
                                
                                // TASK_TRIGGER_LOGON = 9, TASK_TRIGGER_BOOT = 8
                                if (type == TASK_TRIGGER_LOGON || type == TASK_TRIGGER_BOOT) {
                                    isStartupTask = true;
                                }
                                pTrigger->Release();
                            }
                        }
                        
                        if (isStartupTask) {
                            StartupEntry entry;
                            entry.name = QString::fromWCharArray(bstrName);
                            entry.source = StartupSource::TaskScheduler;
                            entry.sourceLocation = "Task Scheduler: \\" + entry.name;
                            entry.isEnabled = (state == TASK_STATE_READY || state == TASK_STATE_RUNNING);
                            
                            // Get the action (command)
                            IActionCollection* pActions = nullptr;
                            if (SUCCEEDED(pDef->get_Actions(&pActions))) {
                                LONG numActions = 0;
                                pActions->get_Count(&numActions);
                                
                                if (numActions > 0) {
                                    IAction* pAction = nullptr;
                                    if (SUCCEEDED(pActions->get_Item(1, &pAction))) {
                                        IExecAction* pExecAction = nullptr;
                                        if (SUCCEEDED(pAction->QueryInterface(IID_IExecAction, 
                                                      (void**)&pExecAction))) {
                                            BSTR bstrPath = nullptr;
                                            BSTR bstrArgs = nullptr;
                                            
                                            pExecAction->get_Path(&bstrPath);
                                            pExecAction->get_Arguments(&bstrArgs);
                                            
                                            if (bstrPath) {
                                                entry.executablePath = QString::fromWCharArray(bstrPath);
                                                entry.command = entry.executablePath;
                                                SysFreeString(bstrPath);
                                            }
                                            if (bstrArgs) {
                                                entry.arguments = QString::fromWCharArray(bstrArgs);
                                                if (!entry.arguments.isEmpty()) {
                                                    entry.command += " " + entry.arguments;
                                                }
                                                SysFreeString(bstrArgs);
                                            }
                                            
                                            pExecAction->Release();
                                        }
                                        pAction->Release();
                                    }
                                }
                                pActions->Release();
                            }
                            
                            extractExecutableInfo(entry);
                            entry.impact = estimateImpact(entry);
                            
                            m_entries.push_back(entry);
                        }
                        
                        pTriggers->Release();
                    }
                    pDef->Release();
                }
                
                if (bstrName) SysFreeString(bstrName);
                pTask->Release();
            }
        }
        pTaskCollection->Release();
    }
    
    pRootFolder->Release();
    pService->Release();
    CoUninitialize();
#endif
}


void StartupMonitor::scanServices()
{
#ifdef _WIN32
    SC_HANDLE hSCManager = OpenSCManager(NULL, NULL, SC_MANAGER_ENUMERATE_SERVICE);
    if (!hSCManager) return;
    
    DWORD bytesNeeded = 0;
    DWORD servicesReturned = 0;
    DWORD resumeHandle = 0;
    
    // First call to get required buffer size
    EnumServicesStatusExW(hSCManager, SC_ENUM_PROCESS_INFO, SERVICE_WIN32,
                          SERVICE_STATE_ALL, NULL, 0, &bytesNeeded,
                          &servicesReturned, &resumeHandle, NULL);
    
    if (bytesNeeded == 0) {
        CloseServiceHandle(hSCManager);
        return;
    }
    
    std::vector<BYTE> buffer(bytesNeeded);
    auto pServices = reinterpret_cast<ENUM_SERVICE_STATUS_PROCESSW*>(buffer.data());
    
    resumeHandle = 0;
    if (!EnumServicesStatusExW(hSCManager, SC_ENUM_PROCESS_INFO, SERVICE_WIN32,
                               SERVICE_STATE_ALL, buffer.data(), bytesNeeded,
                               &bytesNeeded, &servicesReturned, &resumeHandle, NULL)) {
        CloseServiceHandle(hSCManager);
        return;
    }
    
    for (DWORD i = 0; i < servicesReturned; i++) {
        SC_HANDLE hService = OpenServiceW(hSCManager, pServices[i].lpServiceName,
                                          SERVICE_QUERY_CONFIG);
        if (!hService) continue;
        
        // Get service config
        DWORD configSize = 0;
        QueryServiceConfigW(hService, NULL, 0, &configSize);
        
        if (configSize > 0) {
            std::vector<BYTE> configBuffer(configSize);
            auto pConfig = reinterpret_cast<QUERY_SERVICE_CONFIGW*>(configBuffer.data());
            
            if (QueryServiceConfigW(hService, pConfig, configSize, &configSize)) {
                // Only include auto-start services
                if (pConfig->dwStartType == SERVICE_AUTO_START ||
                    pConfig->dwStartType == SERVICE_BOOT_START ||
                    pConfig->dwStartType == SERVICE_SYSTEM_START) {
                    
                    StartupEntry entry;
                    entry.name = QString::fromWCharArray(pServices[i].lpDisplayName);
                    entry.serviceName = QString::fromWCharArray(pServices[i].lpServiceName);
                    entry.source = StartupSource::Services;
                    entry.sourceLocation = "Services: " + entry.serviceName;
                    entry.isEnabled = (pConfig->dwStartType != SERVICE_DISABLED);
                    entry.isElevated = true;
                    
                    // Get binary path
                    QString binaryPath = QString::fromWCharArray(pConfig->lpBinaryPathName);
                    // Remove quotes and get just the executable
                    binaryPath.remove('"');
                    int spacePos = binaryPath.indexOf(' ');
                    if (spacePos > 0 && !binaryPath.startsWith("\\??\\")) {
                        entry.executablePath = binaryPath.left(spacePos);
                        entry.arguments = binaryPath.mid(spacePos + 1);
                    } else {
                        entry.executablePath = binaryPath;
                    }
                    entry.command = binaryPath;
                    
                    // Service start type string
                    switch (pConfig->dwStartType) {
                        case SERVICE_AUTO_START: entry.serviceStartType = "Automatic"; break;
                        case SERVICE_BOOT_START: entry.serviceStartType = "Boot"; break;
                        case SERVICE_SYSTEM_START: entry.serviceStartType = "System"; break;
                        case SERVICE_DEMAND_START: entry.serviceStartType = "Manual"; break;
                        case SERVICE_DISABLED: entry.serviceStartType = "Disabled"; break;
                    }
                    
                    extractExecutableInfo(entry);
                    entry.impact = estimateImpact(entry);
                    
                    // Filter out system-critical services
                    if (!entry.serviceName.startsWith("wudf", Qt::CaseInsensitive) &&
                        !entry.serviceName.startsWith("wd", Qt::CaseInsensitive)) {
                        m_entries.push_back(entry);
                    }
                }
            }
        }
        
        CloseServiceHandle(hService);
    }
    
    CloseServiceHandle(hSCManager);
#endif
}


void StartupMonitor::extractExecutableInfo(StartupEntry& entry)
{
    // Parse command to get executable path
    QString cmd = entry.command.trimmed();
    
    if (entry.executablePath.isEmpty()) {
        // Handle quoted paths
        if (cmd.startsWith('"')) {
            int endQuote = cmd.indexOf('"', 1);
            if (endQuote > 0) {
                entry.executablePath = cmd.mid(1, endQuote - 1);
                entry.arguments = cmd.mid(endQuote + 1).trimmed();
            }
        } else {
            // Find first space (might be path or arguments)
            int spacePos = cmd.indexOf(' ');
            if (spacePos > 0) {
                QString potentialPath = cmd.left(spacePos);
                if (QFileInfo::exists(potentialPath)) {
                    entry.executablePath = potentialPath;
                    entry.arguments = cmd.mid(spacePos + 1).trimmed();
                } else {
                    // Try with .exe
                    if (QFileInfo::exists(potentialPath + ".exe")) {
                        entry.executablePath = potentialPath + ".exe";
                        entry.arguments = cmd.mid(spacePos + 1).trimmed();
                    } else {
                        entry.executablePath = cmd;
                    }
                }
            } else {
                entry.executablePath = cmd;
            }
        }
    }
    
    // Expand environment variables
#ifdef _WIN32
    if (entry.executablePath.contains('%')) {
        wchar_t expanded[MAX_PATH];
        if (ExpandEnvironmentStringsW(entry.executablePath.toStdWString().c_str(),
                                      expanded, MAX_PATH)) {
            entry.executablePath = QString::fromWCharArray(expanded);
        }
    }
#endif
    
    // Check if executable exists
    entry.isValid = QFileInfo::exists(entry.executablePath);
    
    // Get file info
    if (entry.isValid) {
        entry.icon = getFileIcon(entry.executablePath);
        entry.description = getFileDescription(entry.executablePath);
        entry.version = getFileVersion(entry.executablePath);
        entry.publisher = getFilePublisher(entry.executablePath);
        entry.isMicrosoft = isMicrosoftSigned(entry.executablePath);
        
        // Use description as name if name is just filename
        if (!entry.description.isEmpty() && 
            entry.name.contains('.') && 
            entry.source != StartupSource::Services) {
            entry.name = entry.description;
        }
    }
}

QIcon StartupMonitor::getFileIcon(const QString& path)
{
    QFileIconProvider provider;
    return provider.icon(QFileInfo(path));
}

QString StartupMonitor::getFileDescription(const QString& path)
{
#ifdef _WIN32
    DWORD handle = 0;
    DWORD size = GetFileVersionInfoSizeW(path.toStdWString().c_str(), &handle);
    if (size == 0) return QString();
    
    std::vector<BYTE> buffer(size);
    if (!GetFileVersionInfoW(path.toStdWString().c_str(), handle, size, buffer.data()))
        return QString();
    
    struct LANGANDCODEPAGE {
        WORD wLanguage;
        WORD wCodePage;
    } *pTranslate;
    
    UINT cbTranslate = 0;
    if (!VerQueryValueW(buffer.data(), L"\\VarFileInfo\\Translation",
                        (LPVOID*)&pTranslate, &cbTranslate))
        return QString();
    
    if (cbTranslate < sizeof(LANGANDCODEPAGE))
        return QString();
    
    wchar_t subBlock[256];
    swprintf_s(subBlock, L"\\StringFileInfo\\%04x%04x\\FileDescription",
               pTranslate[0].wLanguage, pTranslate[0].wCodePage);
    
    LPVOID lpBuffer = nullptr;
    UINT dwBytes = 0;
    if (VerQueryValueW(buffer.data(), subBlock, &lpBuffer, &dwBytes) && dwBytes > 0) {
        return QString::fromWCharArray(static_cast<wchar_t*>(lpBuffer));
    }
#else
    Q_UNUSED(path);
#endif
    return QString();
}

QString StartupMonitor::getFileVersion(const QString& path)
{
#ifdef _WIN32
    DWORD handle = 0;
    DWORD size = GetFileVersionInfoSizeW(path.toStdWString().c_str(), &handle);
    if (size == 0) return QString();
    
    std::vector<BYTE> buffer(size);
    if (!GetFileVersionInfoW(path.toStdWString().c_str(), handle, size, buffer.data()))
        return QString();
    
    VS_FIXEDFILEINFO* pFileInfo = nullptr;
    UINT len = 0;
    if (VerQueryValueW(buffer.data(), L"\\", (LPVOID*)&pFileInfo, &len)) {
        return QString("%1.%2.%3.%4")
            .arg(HIWORD(pFileInfo->dwFileVersionMS))
            .arg(LOWORD(pFileInfo->dwFileVersionMS))
            .arg(HIWORD(pFileInfo->dwFileVersionLS))
            .arg(LOWORD(pFileInfo->dwFileVersionLS));
    }
#else
    Q_UNUSED(path);
#endif
    return QString();
}


QString StartupMonitor::getFilePublisher(const QString& path)
{
#ifdef _WIN32
    DWORD handle = 0;
    DWORD size = GetFileVersionInfoSizeW(path.toStdWString().c_str(), &handle);
    if (size == 0) return QString();
    
    std::vector<BYTE> buffer(size);
    if (!GetFileVersionInfoW(path.toStdWString().c_str(), handle, size, buffer.data()))
        return QString();
    
    struct LANGANDCODEPAGE {
        WORD wLanguage;
        WORD wCodePage;
    } *pTranslate;
    
    UINT cbTranslate = 0;
    if (!VerQueryValueW(buffer.data(), L"\\VarFileInfo\\Translation",
                        (LPVOID*)&pTranslate, &cbTranslate))
        return QString();
    
    if (cbTranslate < sizeof(LANGANDCODEPAGE))
        return QString();
    
    wchar_t subBlock[256];
    swprintf_s(subBlock, L"\\StringFileInfo\\%04x%04x\\CompanyName",
               pTranslate[0].wLanguage, pTranslate[0].wCodePage);
    
    LPVOID lpBuffer = nullptr;
    UINT dwBytes = 0;
    if (VerQueryValueW(buffer.data(), subBlock, &lpBuffer, &dwBytes) && dwBytes > 0) {
        return QString::fromWCharArray(static_cast<wchar_t*>(lpBuffer));
    }
#else
    Q_UNUSED(path);
#endif
    return QString();
}

bool StartupMonitor::isMicrosoftSigned(const QString& path)
{
#ifdef _WIN32
    WINTRUST_FILE_INFO fileInfo;
    ZeroMemory(&fileInfo, sizeof(fileInfo));
    fileInfo.cbStruct = sizeof(fileInfo);
    fileInfo.pcwszFilePath = path.toStdWString().c_str();
    
    GUID policyGUID = WINTRUST_ACTION_GENERIC_VERIFY_V2;
    
    WINTRUST_DATA trustData;
    ZeroMemory(&trustData, sizeof(trustData));
    trustData.cbStruct = sizeof(trustData);
    trustData.dwUIChoice = WTD_UI_NONE;
    trustData.fdwRevocationChecks = WTD_REVOKE_NONE;
    trustData.dwUnionChoice = WTD_CHOICE_FILE;
    trustData.pFile = &fileInfo;
    trustData.dwStateAction = WTD_STATEACTION_VERIFY;
    
    LONG status = WinVerifyTrust(NULL, &policyGUID, &trustData);
    
    trustData.dwStateAction = WTD_STATEACTION_CLOSE;
    WinVerifyTrust(NULL, &policyGUID, &trustData);
    
    if (status == ERROR_SUCCESS) {
        // Check if signed by Microsoft
        QString publisher = getFilePublisher(path);
        return publisher.contains("Microsoft", Qt::CaseInsensitive);
    }
#else
    Q_UNUSED(path);
#endif
    return false;
}

StartupImpact StartupMonitor::estimateImpact(const StartupEntry& entry)
{
    if (!entry.isEnabled) {
        return StartupImpact::None;
    }
    
    // Estimate based on file size and type
    QFileInfo fileInfo(entry.executablePath);
    if (!fileInfo.exists()) {
        return StartupImpact::NotMeasured;
    }
    
    qint64 fileSize = fileInfo.size();
    
    // Known high-impact applications
    QString lowerName = entry.name.toLower();
    QString lowerPath = entry.executablePath.toLower();
    
    if (lowerPath.contains("onedrive") ||
        lowerPath.contains("dropbox") ||
        lowerPath.contains("googledrive") ||
        lowerPath.contains("spotify") ||
        lowerPath.contains("discord") ||
        lowerPath.contains("steam") ||
        lowerPath.contains("epic games") ||
        lowerPath.contains("adobe") ||
        lowerPath.contains("teams")) {
        return StartupImpact::High;
    }
    
    // Known low-impact (system utilities)
    if (entry.isMicrosoft && 
        (lowerPath.contains("\\windows\\") || 
         lowerPath.contains("securityhealth") ||
         lowerPath.contains("ctfmon"))) {
        return StartupImpact::Low;
    }
    
    // Estimate by file size
    if (fileSize > 50 * 1024 * 1024) { // > 50 MB
        return StartupImpact::High;
    } else if (fileSize > 10 * 1024 * 1024) { // > 10 MB
        return StartupImpact::Medium;
    } else {
        return StartupImpact::Low;
    }
}


bool StartupMonitor::setEnabled(int index, bool enabled)
{
    if (index < 0 || index >= static_cast<int>(m_entries.size())) {
        return false;
    }
    
    StartupEntry& entry = m_entries[index];
    
    if (entry.isEnabled == enabled) {
        return true; // No change needed
    }
    
    bool success = false;
    
    switch (entry.source) {
        case StartupSource::RegistryCurrentUser:
        case StartupSource::RegistryLocalMachine:
        case StartupSource::RegistryCurrentUserOnce:
        case StartupSource::RegistryLocalMachineOnce:
            success = enableRegistryEntry(entry, enabled);
            break;
            
        case StartupSource::StartupFolderUser:
        case StartupSource::StartupFolderCommon:
            success = enableStartupFolderEntry(entry, enabled);
            break;
            
        case StartupSource::TaskScheduler:
            success = enableTaskSchedulerEntry(entry, enabled);
            break;
            
        case StartupSource::Services:
            success = enableServiceEntry(entry, enabled);
            break;
            
        default:
            emit errorOccurred(tr("Unknown startup source"));
            return false;
    }
    
    if (success) {
        entry.isEnabled = enabled;
        entry.impact = enabled ? estimateImpact(entry) : StartupImpact::None;
        if (!enabled) {
            entry.lastDisabled = QDateTime::currentDateTime();
        }
        m_model->setEntries(m_entries);
        emit entryChanged(index);
    }
    
    return success;
}

bool StartupMonitor::setEnabled(const QString& name, StartupSource source, bool enabled)
{
    for (int i = 0; i < static_cast<int>(m_entries.size()); i++) {
        if (m_entries[i].name == name && m_entries[i].source == source) {
            return setEnabled(i, enabled);
        }
    }
    return false;
}

bool StartupMonitor::enableRegistryEntry(const StartupEntry& entry, bool enable)
{
#ifdef _WIN32
    // Windows uses StartupApproved registry key to enable/disable
    HKEY hRootKey = (entry.source == StartupSource::RegistryCurrentUser ||
                     entry.source == StartupSource::RegistryCurrentUserOnce)
                    ? HKEY_CURRENT_USER : HKEY_LOCAL_MACHINE;
    
    QString approvedKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run";
    
    HKEY hKey;
    if (RegOpenKeyExW(hRootKey, approvedKey.toStdWString().c_str(),
                      0, KEY_SET_VALUE, &hKey) != ERROR_SUCCESS) {
        // Try to create the key
        if (RegCreateKeyExW(hRootKey, approvedKey.toStdWString().c_str(),
                           0, NULL, 0, KEY_SET_VALUE, NULL, &hKey, NULL) != ERROR_SUCCESS) {
            emit errorOccurred(tr("Failed to open StartupApproved registry key"));
            return false;
        }
    }
    
    // Create the enable/disable data
    // 02 00 00 00 00 00 00 00 00 00 00 00 = enabled
    // 03 00 00 00 XX XX XX XX XX XX XX XX = disabled (XX = FILETIME when disabled)
    BYTE data[12] = {0};
    
    if (enable) {
        data[0] = 0x02;
    } else {
        data[0] = 0x03;
        // Store current time as FILETIME
        FILETIME ft;
        GetSystemTimeAsFileTime(&ft);
        memcpy(&data[4], &ft, sizeof(FILETIME));
    }
    
    LONG result = RegSetValueExW(hKey, entry.name.toStdWString().c_str(),
                                  0, REG_BINARY, data, sizeof(data));
    
    RegCloseKey(hKey);
    
    if (result != ERROR_SUCCESS) {
        emit errorOccurred(tr("Failed to modify startup entry"));
        return false;
    }
    
    return true;
#else
    Q_UNUSED(entry);
    Q_UNUSED(enable);
    return false;
#endif
}

bool StartupMonitor::enableStartupFolderEntry(const StartupEntry& entry, bool enable)
{
    QString sourcePath = entry.sourceLocation;
    QString backupPath = m_disabledBackupPath + "/" + QFileInfo(sourcePath).fileName();
    
    if (enable) {
        // Move from backup to startup folder
        if (QFile::exists(backupPath)) {
            // Remove existing if any
            QFile::remove(sourcePath);
            if (QFile::rename(backupPath, sourcePath)) {
                return true;
            }
        }
        emit errorOccurred(tr("Backup file not found"));
        return false;
    } else {
        // Move to backup folder
        if (QFile::exists(sourcePath)) {
            // Remove old backup if exists
            QFile::remove(backupPath);
            if (QFile::rename(sourcePath, backupPath)) {
                return true;
            }
        }
        emit errorOccurred(tr("Failed to move startup file"));
        return false;
    }
}


bool StartupMonitor::enableTaskSchedulerEntry(const StartupEntry& entry, bool enable)
{
#ifdef _WIN32
    CoInitialize(NULL);
    
    ITaskService* pService = nullptr;
    if (FAILED(CoCreateInstance(CLSID_TaskScheduler, NULL, CLSCTX_INPROC_SERVER,
                                IID_ITaskService, (void**)&pService))) {
        CoUninitialize();
        emit errorOccurred(tr("Failed to access Task Scheduler"));
        return false;
    }
    
    if (FAILED(pService->Connect(_variant_t(), _variant_t(), _variant_t(), _variant_t()))) {
        pService->Release();
        CoUninitialize();
        emit errorOccurred(tr("Failed to connect to Task Scheduler"));
        return false;
    }
    
    ITaskFolder* pRootFolder = nullptr;
    if (FAILED(pService->GetFolder(_bstr_t(L"\\"), &pRootFolder))) {
        pService->Release();
        CoUninitialize();
        emit errorOccurred(tr("Failed to get Task Scheduler root folder"));
        return false;
    }
    
    IRegisteredTask* pTask = nullptr;
    if (FAILED(pRootFolder->GetTask(_bstr_t(entry.name.toStdWString().c_str()), &pTask))) {
        pRootFolder->Release();
        pService->Release();
        CoUninitialize();
        emit errorOccurred(tr("Task not found"));
        return false;
    }
    
    HRESULT hr = pTask->put_Enabled(enable ? VARIANT_TRUE : VARIANT_FALSE);
    
    pTask->Release();
    pRootFolder->Release();
    pService->Release();
    CoUninitialize();
    
    if (FAILED(hr)) {
        emit errorOccurred(tr("Failed to modify task"));
        return false;
    }
    
    return true;
#else
    Q_UNUSED(entry);
    Q_UNUSED(enable);
    return false;
#endif
}

bool StartupMonitor::enableServiceEntry(const StartupEntry& entry, bool enable)
{
#ifdef _WIN32
    if (!isAdmin()) {
        emit errorOccurred(tr("Administrator privileges required to modify services"));
        return false;
    }
    
    SC_HANDLE hSCManager = OpenSCManager(NULL, NULL, SC_MANAGER_ALL_ACCESS);
    if (!hSCManager) {
        emit errorOccurred(tr("Failed to open Service Control Manager"));
        return false;
    }
    
    SC_HANDLE hService = OpenServiceW(hSCManager, entry.serviceName.toStdWString().c_str(),
                                      SERVICE_CHANGE_CONFIG);
    if (!hService) {
        CloseServiceHandle(hSCManager);
        emit errorOccurred(tr("Failed to open service: %1").arg(entry.serviceName));
        return false;
    }
    
    DWORD startType = enable ? SERVICE_AUTO_START : SERVICE_DEMAND_START;
    BOOL result = ChangeServiceConfigW(hService,
                                       SERVICE_NO_CHANGE,
                                       startType,
                                       SERVICE_NO_CHANGE,
                                       NULL, NULL, NULL, NULL, NULL, NULL, NULL);
    
    CloseServiceHandle(hService);
    CloseServiceHandle(hSCManager);
    
    if (!result) {
        emit errorOccurred(tr("Failed to change service configuration"));
        return false;
    }
    
    return true;
#else
    Q_UNUSED(entry);
    Q_UNUSED(enable);
    return false;
#endif
}

bool StartupMonitor::deleteEntry(int index)
{
    if (index < 0 || index >= static_cast<int>(m_entries.size())) {
        return false;
    }
    
    const StartupEntry& entry = m_entries[index];
    
#ifdef _WIN32
    switch (entry.source) {
        case StartupSource::RegistryCurrentUser:
        case StartupSource::RegistryLocalMachine:
        case StartupSource::RegistryCurrentUserOnce:
        case StartupSource::RegistryLocalMachineOnce: {
            HKEY hRootKey = (entry.source == StartupSource::RegistryCurrentUser ||
                            entry.source == StartupSource::RegistryCurrentUserOnce)
                           ? HKEY_CURRENT_USER : HKEY_LOCAL_MACHINE;
            
            QString subKey;
            if (entry.source == StartupSource::RegistryCurrentUser ||
                entry.source == StartupSource::RegistryLocalMachine) {
                subKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
            } else {
                subKey = "Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce";
            }
            
            HKEY hKey;
            if (RegOpenKeyExW(hRootKey, subKey.toStdWString().c_str(),
                             0, KEY_SET_VALUE, &hKey) == ERROR_SUCCESS) {
                RegDeleteValueW(hKey, entry.name.toStdWString().c_str());
                RegCloseKey(hKey);
            }
            break;
        }
        
        case StartupSource::StartupFolderUser:
        case StartupSource::StartupFolderCommon:
            QFile::remove(entry.sourceLocation);
            break;
            
        default:
            emit errorOccurred(tr("Cannot delete this type of startup entry"));
            return false;
    }
#endif
    
    refresh();
    return true;
}

bool StartupMonitor::addEntry(const QString& name, const QString& command, StartupSource source)
{
#ifdef _WIN32
    if (source == StartupSource::RegistryCurrentUser ||
        source == StartupSource::RegistryLocalMachine) {
        
        HKEY hRootKey = (source == StartupSource::RegistryCurrentUser)
                       ? HKEY_CURRENT_USER : HKEY_LOCAL_MACHINE;
        
        HKEY hKey;
        if (RegOpenKeyExW(hRootKey, L"Software\\Microsoft\\Windows\\CurrentVersion\\Run",
                         0, KEY_SET_VALUE, &hKey) == ERROR_SUCCESS) {
            
            RegSetValueExW(hKey, name.toStdWString().c_str(), 0, REG_SZ,
                          reinterpret_cast<const BYTE*>(command.toStdWString().c_str()),
                          (command.length() + 1) * sizeof(wchar_t));
            RegCloseKey(hKey);
            
            refresh();
            return true;
        }
    }
#else
    Q_UNUSED(name);
    Q_UNUSED(command);
    Q_UNUSED(source);
#endif
    
    emit errorOccurred(tr("Failed to add startup entry"));
    return false;
}

bool StartupMonitor::openLocation(int index)
{
    if (index < 0 || index >= static_cast<int>(m_entries.size())) {
        return false;
    }
    
    const StartupEntry& entry = m_entries[index];
    
#ifdef _WIN32
    switch (entry.source) {
        case StartupSource::RegistryCurrentUser:
        case StartupSource::RegistryLocalMachine:
        case StartupSource::RegistryCurrentUserOnce:
        case StartupSource::RegistryLocalMachineOnce: {
            // Open regedit at the location
            QString regPath = getRegistryPath(entry.source);
            QString cmd = QString("regedit /m /e NUL \"%1\"").arg(regPath);
            
            // Unfortunately, there's no direct way to open regedit at a specific key
            // We can at least open regedit
            ShellExecuteW(NULL, L"open", L"regedit", NULL, NULL, SW_SHOW);
            return true;
        }
        
        case StartupSource::StartupFolderUser:
        case StartupSource::StartupFolderCommon: {
            QString folder = QFileInfo(entry.sourceLocation).absolutePath();
            QDesktopServices::openUrl(QUrl::fromLocalFile(folder));
            return true;
        }
        
        case StartupSource::TaskScheduler: {
            ShellExecuteW(NULL, L"open", L"taskschd.msc", NULL, NULL, SW_SHOW);
            return true;
        }
        
        case StartupSource::Services: {
            ShellExecuteW(NULL, L"open", L"services.msc", NULL, NULL, SW_SHOW);
            return true;
        }
        
        default:
            return false;
    }
#else
    Q_UNUSED(index);
    return false;
#endif
}

bool StartupMonitor::openFileLocation(int index)
{
    if (index < 0 || index >= static_cast<int>(m_entries.size())) {
        return false;
    }
    
    const StartupEntry& entry = m_entries[index];
    
    if (entry.executablePath.isEmpty() || !QFileInfo::exists(entry.executablePath)) {
        emit errorOccurred(tr("Executable not found"));
        return false;
    }
    
#ifdef _WIN32
    QString cmd = QString("/select,\"%1\"").arg(QDir::toNativeSeparators(entry.executablePath));
    ShellExecuteW(NULL, L"open", L"explorer.exe", cmd.toStdWString().c_str(), NULL, SW_SHOW);
    return true;
#else
    QDesktopServices::openUrl(QUrl::fromLocalFile(QFileInfo(entry.executablePath).absolutePath()));
    return true;
#endif
}
