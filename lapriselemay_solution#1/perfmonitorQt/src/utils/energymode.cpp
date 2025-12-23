#include "energymode.h"

#ifdef _WIN32
#include <Winsvc.h>
#include <powrprof.h>
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "powrprof.lib")
#endif

#include <QProcess>
#include <QRegularExpression>
#include <QDebug>

EnergyModeManager::EnergyModeManager(QObject *parent)
    : QObject(parent)
{
    initializeServiceList();
    loadSettings();
}

EnergyModeManager::~EnergyModeManager()
{
    saveSettings();
    // Auto-restore if still active when app closes
    if (m_isActive) {
        deactivate();
    }
}

void EnergyModeManager::initializeServiceList()
{
    // Services safe to stop for performance
    // Format: {internal_name, display_name, description, wasRunning, isEssential, isSelected}
    
    m_services = {
        // Telemetry & Diagnostics
        {"DiagTrack", "Connected User Experiences and Telemetry", 
         "Télémétrie Microsoft - collecte de données", false, false, true},
        {"dmwappushservice", "Device Management WAP Push", 
         "Service de push WAP", false, false, true},
        {"diagnosticshub.standardcollector.service", "Diagnostics Hub",
         "Collecteur de diagnostics", false, false, true},
        
        // Windows Search & Indexing
        {"WSearch", "Windows Search", 
         "Indexation des fichiers - utilise CPU/disque", false, false, true},
        
        // Windows Update (attention: désactive les mises à jour)
        {"wuauserv", "Windows Update", 
         "Service de mise à jour Windows", false, false, false},
        {"UsoSvc", "Update Orchestrator Service", 
         "Orchestrateur de mises à jour", false, false, false},
        {"BITS", "Background Intelligent Transfer Service", 
         "Transferts en arrière-plan (utilisé par Windows Update)", false, false, true},
        
        // SysMain (Superfetch)
        {"SysMain", "SysMain (Superfetch)", 
         "Préchargement d'applications - utilise RAM/disque", false, false, true},
        
        // Print & Fax
        {"Spooler", "Print Spooler", 
         "File d'impression - inutile sans imprimante", false, false, false},
        {"Fax", "Fax", 
         "Service de télécopie", false, false, true},
        
        // Xbox Services (désactiver si pas de jeux Xbox/Game Pass)
        {"XblAuthManager", "Xbox Live Auth Manager", 
         "Authentification Xbox Live", false, false, false},
        {"XblGameSave", "Xbox Live Game Save", 
         "Sauvegarde cloud Xbox", false, false, false},
        {"XboxGipSvc", "Xbox Accessory Management", 
         "Gestion accessoires Xbox", false, false, false},
        {"XboxNetApiSvc", "Xbox Live Networking", 
         "Réseau Xbox Live", false, false, false},
        
        // Remote & Network services
        {"RemoteRegistry", "Remote Registry", 
         "Registre à distance - risque sécurité", false, false, true},
        {"RemoteAccess", "Routing and Remote Access", 
         "Accès distant", false, false, true},
        {"lmhosts", "TCP/IP NetBIOS Helper", 
         "Support NetBIOS", false, false, true},
        
        // Maintenance & Diagnostics
        {"WerSvc", "Windows Error Reporting", 
         "Rapport d'erreurs Windows", false, false, true},
        {"DPS", "Diagnostic Policy Service", 
         "Politique de diagnostic", false, false, true},
        {"WdiServiceHost", "Diagnostic Service Host", 
         "Hôte de diagnostic", false, false, true},
        {"WdiSystemHost", "Diagnostic System Host", 
         "Hôte système diagnostic", false, false, true},
        {"defragsvc", "Optimize Drives", 
         "Défragmentation (pas nécessaire pour SSD)", false, false, true},
        
        // Other non-essential
        {"MapsBroker", "Downloaded Maps Manager", 
         "Gestionnaire de cartes téléchargées", false, false, true},
        {"lfsvc", "Geolocation Service", 
         "Service de géolocalisation", false, false, true},
        {"WbioSrvc", "Windows Biometric Service", 
         "Service biométrique (si non utilisé)", false, false, false},
        {"TabletInputService", "Touch Keyboard and Handwriting", 
         "Clavier tactile (si non utilisé)", false, false, false},
        {"PhoneSvc", "Phone Service", 
         "Service téléphone", false, false, true},
        {"icssvc", "Windows Mobile Hotspot", 
         "Point d'accès mobile", false, false, true},
        {"wisvc", "Windows Insider Service", 
         "Programme Insider", false, false, true},
    };
}

bool EnergyModeManager::isRunningAsAdmin()
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

bool EnergyModeManager::activate()
{
    if (m_isActive) {
        setStatus("Mode Énergie déjà actif");
        return true;
    }
    
    if (!isRunningAsAdmin()) {
        setStatus("Erreur: Droits administrateur requis");
        return false;
    }
    
    setStatus("Activation du Mode Énergie...");
    emit progressChanged(0, servicesToStopCount() + 2);
    
    // 1. Save current power plan
    m_previousPowerPlan = "";
    QProcess process;
    process.start("powercfg", {"/getactivescheme"});
    process.waitForFinished();
    QString output = process.readAllStandardOutput();
    // Extract GUID from output like "Power Scheme GUID: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
    QRegularExpression rx("([0-9a-fA-F-]{36})");
    auto match = rx.match(output);
    if (match.hasMatch()) {
        m_previousPowerPlan = match.captured(1);
    }
    
    // 2. Set High Performance power plan
    setHighPerformancePowerPlan();
    emit progressChanged(1, servicesToStopCount() + 2);
    
    // 3. Stop selected services
    int stoppedCount = 0;
    int progress = 2;
    
    for (auto& service : m_services) {
        if (!service.isSelected) continue;
        
        // Check if service is currently running
        bool wasRunning = isServiceRunning(service.name);
        m_previousServiceStates[service.name] = wasRunning;
        service.wasRunning = wasRunning;
        
        if (wasRunning) {
            setStatus(QString("Arrêt de %1...").arg(service.displayName));
            if (stopService(service.name)) {
                stoppedCount++;
                emit serviceStateChanged(service.name, false);
            }
        }
        
        emit progressChanged(++progress, servicesToStopCount() + 2);
    }
    
    m_isActive = true;
    setStatus(QString("Mode Énergie activé - %1 services arrêtés").arg(stoppedCount));
    emit activationChanged(true);
    
    saveSettings();
    return true;
}

bool EnergyModeManager::deactivate()
{
    if (!m_isActive) {
        setStatus("Mode Énergie n'est pas actif");
        return true;
    }
    
    setStatus("Désactivation du Mode Énergie...");
    emit progressChanged(0, servicesToStopCount() + 2);
    
    // 1. Restore services
    int restoredCount = 0;
    int progress = 0;
    
    for (auto& service : m_services) {
        if (!service.isSelected) continue;
        
        auto it = m_previousServiceStates.find(service.name);
        if (it != m_previousServiceStates.end() && it->second) {
            // Service was running before, restart it
            setStatus(QString("Redémarrage de %1...").arg(service.displayName));
            if (startService(service.name)) {
                restoredCount++;
                emit serviceStateChanged(service.name, true);
            }
        }
        emit progressChanged(++progress, servicesToStopCount() + 2);
    }
    
    // 2. Restore power plan
    restorePowerPlan();
    emit progressChanged(++progress, servicesToStopCount() + 2);
    
    m_isActive = false;
    m_previousServiceStates.clear();
    setStatus(QString("Mode Énergie désactivé - %1 services restaurés").arg(restoredCount));
    emit activationChanged(false);
    
    saveSettings();
    return true;
}

bool EnergyModeManager::toggle()
{
    return m_isActive ? deactivate() : activate();
}

bool EnergyModeManager::stopService(const QString& serviceName)
{
#ifdef _WIN32
    SC_HANDLE scManager = OpenSCManager(NULL, NULL, SC_MANAGER_CONNECT);
    if (!scManager) return false;
    
    SC_HANDLE service = OpenServiceW(scManager, 
        serviceName.toStdWString().c_str(),
        SERVICE_STOP | SERVICE_QUERY_STATUS);
    
    if (!service) {
        CloseServiceHandle(scManager);
        return false;
    }
    
    SERVICE_STATUS status;
    BOOL result = ControlService(service, SERVICE_CONTROL_STOP, &status);
    
    // Wait for service to stop
    if (result) {
        int timeout = 30; // 30 seconds max
        while (timeout > 0) {
            if (QueryServiceStatus(service, &status)) {
                if (status.dwCurrentState == SERVICE_STOPPED) {
                    break;
                }
            }
            Sleep(1000);
            timeout--;
        }
    }
    
    CloseServiceHandle(service);
    CloseServiceHandle(scManager);
    return result != FALSE;
#else
    Q_UNUSED(serviceName);
    return false;
#endif
}

bool EnergyModeManager::startService(const QString& serviceName)
{
#ifdef _WIN32
    SC_HANDLE scManager = OpenSCManager(NULL, NULL, SC_MANAGER_CONNECT);
    if (!scManager) return false;
    
    SC_HANDLE service = OpenServiceW(scManager,
        serviceName.toStdWString().c_str(),
        SERVICE_START | SERVICE_QUERY_STATUS);
    
    if (!service) {
        CloseServiceHandle(scManager);
        return false;
    }
    
    BOOL result = StartService(service, 0, NULL);
    
    // Wait for service to start
    if (result) {
        SERVICE_STATUS status;
        int timeout = 30;
        while (timeout > 0) {
            if (QueryServiceStatus(service, &status)) {
                if (status.dwCurrentState == SERVICE_RUNNING) {
                    break;
                }
            }
            Sleep(1000);
            timeout--;
        }
    }
    
    CloseServiceHandle(service);
    CloseServiceHandle(scManager);
    return result != FALSE;
#else
    Q_UNUSED(serviceName);
    return false;
#endif
}

bool EnergyModeManager::isServiceRunning(const QString& serviceName)
{
#ifdef _WIN32
    SC_HANDLE scManager = OpenSCManager(NULL, NULL, SC_MANAGER_CONNECT);
    if (!scManager) return false;
    
    SC_HANDLE service = OpenServiceW(scManager,
        serviceName.toStdWString().c_str(),
        SERVICE_QUERY_STATUS);
    
    if (!service) {
        CloseServiceHandle(scManager);
        return false;
    }
    
    SERVICE_STATUS status;
    BOOL result = QueryServiceStatus(service, &status);
    
    CloseServiceHandle(service);
    CloseServiceHandle(scManager);
    
    return result && status.dwCurrentState == SERVICE_RUNNING;
#else
    Q_UNUSED(serviceName);
    return false;
#endif
}

DWORD EnergyModeManager::getServiceStartType(const QString& serviceName)
{
#ifdef _WIN32
    SC_HANDLE scManager = OpenSCManager(NULL, NULL, SC_MANAGER_CONNECT);
    if (!scManager) return SERVICE_DISABLED;
    
    SC_HANDLE service = OpenServiceW(scManager,
        serviceName.toStdWString().c_str(),
        SERVICE_QUERY_CONFIG);
    
    if (!service) {
        CloseServiceHandle(scManager);
        return SERVICE_DISABLED;
    }
    
    DWORD bytesNeeded = 0;
    QueryServiceConfig(service, NULL, 0, &bytesNeeded);
    
    std::vector<BYTE> buffer(bytesNeeded);
    auto config = reinterpret_cast<LPQUERY_SERVICE_CONFIG>(buffer.data());
    
    DWORD startType = SERVICE_DISABLED;
    if (QueryServiceConfig(service, config, bytesNeeded, &bytesNeeded)) {
        startType = config->dwStartType;
    }
    
    CloseServiceHandle(service);
    CloseServiceHandle(scManager);
    return startType;
#else
    Q_UNUSED(serviceName);
    return 0;
#endif
}

bool EnergyModeManager::setHighPerformancePowerPlan()
{
    // High Performance GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c
    QProcess process;
    process.start("powercfg", {"/setactive", "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"});
    return process.waitForFinished() && process.exitCode() == 0;
}

bool EnergyModeManager::restorePowerPlan()
{
    if (m_previousPowerPlan.isEmpty()) {
        // Default to Balanced: 381b4222-f694-41f0-9685-ff5bb260df2e
        m_previousPowerPlan = "381b4222-f694-41f0-9685-ff5bb260df2e";
    }
    
    QProcess process;
    process.start("powercfg", {"/setactive", m_previousPowerPlan});
    return process.waitForFinished() && process.exitCode() == 0;
}

void EnergyModeManager::setServiceEnabled(const QString& serviceName, bool enabled)
{
    for (auto& service : m_services) {
        if (service.name == serviceName) {
            service.isSelected = enabled;
            break;
        }
    }
    saveSettings();
}

int EnergyModeManager::servicesToStopCount() const
{
    int count = 0;
    for (const auto& service : m_services) {
        if (service.isSelected) count++;
    }
    return count;
}

qint64 EnergyModeManager::estimatedMemorySavings() const
{
    // Rough estimates in bytes
    qint64 total = 0;
    for (const auto& service : m_services) {
        if (service.isSelected) {
            if (service.name == "WSearch") total += 100 * 1024 * 1024; // ~100 MB
            else if (service.name == "SysMain") total += 200 * 1024 * 1024; // ~200 MB
            else total += 20 * 1024 * 1024; // ~20 MB average
        }
    }
    return total;
}

void EnergyModeManager::setStatus(const QString& message)
{
    m_statusMessage = message;
    emit statusMessageChanged(message);
}

void EnergyModeManager::loadSettings()
{
    QSettings settings("Félix-Antoine", "PerfMonitorQt");
    settings.beginGroup("EnergyMode");
    
    m_isActive = settings.value("isActive", false).toBool();
    
    // Load service selection states
    int size = settings.beginReadArray("services");
    for (int i = 0; i < size; ++i) {
        settings.setArrayIndex(i);
        QString name = settings.value("name").toString();
        bool selected = settings.value("selected", true).toBool();
        
        for (auto& service : m_services) {
            if (service.name == name) {
                service.isSelected = selected;
                break;
            }
        }
    }
    settings.endArray();
    
    // Load previous states if mode is active
    if (m_isActive) {
        m_previousPowerPlan = settings.value("previousPowerPlan").toString();
        
        int stateSize = settings.beginReadArray("previousStates");
        for (int i = 0; i < stateSize; ++i) {
            settings.setArrayIndex(i);
            QString name = settings.value("name").toString();
            bool wasRunning = settings.value("wasRunning", false).toBool();
            m_previousServiceStates[name] = wasRunning;
        }
        settings.endArray();
    }
    
    settings.endGroup();
}

void EnergyModeManager::saveSettings()
{
    QSettings settings("Félix-Antoine", "PerfMonitorQt");
    settings.beginGroup("EnergyMode");
    
    settings.setValue("isActive", m_isActive);
    settings.setValue("previousPowerPlan", m_previousPowerPlan);
    
    // Save service selections
    settings.beginWriteArray("services");
    int i = 0;
    for (const auto& service : m_services) {
        settings.setArrayIndex(i++);
        settings.setValue("name", service.name);
        settings.setValue("selected", service.isSelected);
    }
    settings.endArray();
    
    // Save previous states
    settings.beginWriteArray("previousStates");
    i = 0;
    for (const auto& [name, wasRunning] : m_previousServiceStates) {
        settings.setArrayIndex(i++);
        settings.setValue("name", name);
        settings.setValue("wasRunning", wasRunning);
    }
    settings.endArray();
    
    settings.endGroup();
}
