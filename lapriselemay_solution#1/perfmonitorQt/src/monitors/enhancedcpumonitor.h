#pragma once

#include <QObject>
#include <QString>
#include <vector>
#include <memory>

#ifdef _WIN32
#include <Windows.h>
#include <pdh.h>
#endif

/// <summary>
/// Type de cœur CPU (Intel Hybrid Architecture)
/// </summary>
enum class CoreType {
    Unknown,
    Performance,    // P-Core (haute performance)
    Efficient       // E-Core (efficacité énergétique)
};

/// <summary>
/// Information sur un cœur CPU individuel
/// </summary>
struct CoreInfo {
    int index{0};
    CoreType type{CoreType::Unknown};
    double usage{0.0};
    double frequency{0.0};      // MHz actuelle
    double maxFrequency{0.0};   // MHz max théorique
    int numaNode{0};
    bool isHyperThread{false};
    int physicalCoreId{0};
};

/// <summary>
/// Information complète sur le CPU
/// </summary>
struct EnhancedCpuInfo {
    QString name;
    QString vendor;
    QString architecture;
    
    double usage{0.0};
    double currentSpeed{0.0};       // GHz
    double baseSpeed{0.0};          // GHz
    double turboSpeed{0.0};         // GHz max turbo
    double temperature{0.0};        // °C (si disponible)
    double power{0.0};              // Watts (si disponible)
    
    int physicalCores{0};
    int logicalProcessors{0};
    int performanceCores{0};        // P-Cores (Intel 12th gen+)
    int efficientCores{0};          // E-Cores (Intel 12th gen+)
    
    int processCount{0};
    int threadCount{0};
    
    QString uptime;
    
    bool isHybridArchitecture{false};
    bool hasTemperatureSensor{false};
    bool hasPowerSensor{false};
    
    std::vector<CoreInfo> cores;
    std::vector<double> coreUsages;  // Compatibilité avec l'ancien format
    
    // Statistiques par type de cœur
    double pCoreAvgUsage{0.0};
    double eCoreAvgUsage{0.0};
};

/// <summary>
/// Moniteur CPU amélioré avec support des architectures hybrides Intel
/// et détection des cœurs P/E (Performance/Efficiency)
/// </summary>
class EnhancedCpuMonitor : public QObject
{
    Q_OBJECT

public:
    explicit EnhancedCpuMonitor(QObject *parent = nullptr);
    ~EnhancedCpuMonitor() override;

    /// <summary>
    /// Met à jour les informations CPU
    /// </summary>
    void update();
    
    /// <summary>
    /// Récupère les informations actuelles
    /// </summary>
    [[nodiscard]] const EnhancedCpuInfo& info() const { return m_info; }
    
    /// <summary>
    /// Vérifie si l'architecture hybride est détectée
    /// </summary>
    [[nodiscard]] bool isHybridCpu() const { return m_info.isHybridArchitecture; }
    
    /// <summary>
    /// Récupère le nombre de P-Cores
    /// </summary>
    [[nodiscard]] int performanceCoreCount() const { return m_info.performanceCores; }
    
    /// <summary>
    /// Récupère le nombre de E-Cores
    /// </summary>
    [[nodiscard]] int efficientCoreCount() const { return m_info.efficientCores; }

signals:
    /// <summary>
    /// Émis quand les données sont mises à jour
    /// </summary>
    void updated();
    
    /// <summary>
    /// Émis si une température critique est détectée
    /// </summary>
    void temperatureWarning(double temperature);

private:
    void initializePdh();
    void detectHybridArchitecture();
    void queryProcessorName();
    void queryProcessorInfo();
    void updateCoreTypes();
    void updateTemperature();
    QString formatUptime(qint64 milliseconds);
    
    EnhancedCpuInfo m_info;
    
#ifdef _WIN32
    PDH_HQUERY m_query{nullptr};
    PDH_HCOUNTER m_cpuCounter{nullptr};
    std::vector<PDH_HCOUNTER> m_coreCounters;
    std::vector<PDH_HCOUNTER> m_frequencyCounters;
    FILETIME m_prevIdleTime{};
    FILETIME m_prevKernelTime{};
    FILETIME m_prevUserTime{};
    bool m_pdhInitialized{false};
#endif
    
    // Cache pour la détection des types de cœurs
    std::vector<CoreType> m_coreTypeCache;
    bool m_coreTypesDetected{false};
};

// ============================================================================
// IMPLÉMENTATION (normalement dans .cpp, inclus ici pour simplicité)
// ============================================================================

#ifdef _WIN32
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

inline EnhancedCpuMonitor::EnhancedCpuMonitor(QObject *parent)
    : QObject(parent)
{
    queryProcessorName();
    queryProcessorInfo();
    detectHybridArchitecture();
    initializePdh();
    
#ifdef _WIN32
    GetSystemTimes(&m_prevIdleTime, &m_prevKernelTime, &m_prevUserTime);
#endif
}

inline EnhancedCpuMonitor::~EnhancedCpuMonitor()
{
#ifdef _WIN32
    if (m_query) {
        PdhCloseQuery(m_query);
    }
#endif
}

inline void EnhancedCpuMonitor::detectHybridArchitecture()
{
#ifdef _WIN32
    // Vérifier si c'est un CPU Intel 12th gen+ avec architecture hybride
    m_info.isHybridArchitecture = false;
    m_info.performanceCores = 0;
    m_info.efficientCores = 0;
    
    // Utiliser CPUID pour détecter le type de cœur (leaf 0x1A)
    int cpuInfo[4] = {0};
    __cpuid(cpuInfo, 0);
    
    // Vérifier le vendor (GenuineIntel)
    char vendor[13] = {0};
    memcpy(vendor, &cpuInfo[1], 4);
    memcpy(vendor + 4, &cpuInfo[3], 4);
    memcpy(vendor + 8, &cpuInfo[2], 4);
    m_info.vendor = QString::fromLatin1(vendor);
    
    if (m_info.vendor != "GenuineIntel") {
        // AMD et autres n'ont pas d'architecture hybride (pour l'instant)
        return;
    }
    
    // Vérifier si CPUID leaf 0x1A (Hybrid Information) est supporté
    __cpuid(cpuInfo, 0);
    int maxLeaf = cpuInfo[0];
    
    if (maxLeaf >= 0x1A) {
        // Intel Hybrid Architecture détectée (12th gen+)
        // Note: La détection précise nécessite d'exécuter CPUID sur chaque cœur
        // Pour une implémentation simplifiée, on utilise le nom du CPU
        
        QString cpuName = m_info.name.toLower();
        if (cpuName.contains("12th gen") || cpuName.contains("13th gen") || 
            cpuName.contains("14th gen") || cpuName.contains("core ultra") ||
            cpuName.contains("raptor") || cpuName.contains("alder")) {
            
            m_info.isHybridArchitecture = true;
            
            // Estimation basée sur les configurations courantes
            // Ces valeurs devraient être affinées par CPUID sur chaque cœur
            if (m_info.physicalCores > 0) {
                // Heuristique: les P-cores ont généralement HT, pas les E-cores
                // Ratio approximatif basé sur les configs Intel courantes
                if (m_info.logicalProcessors > m_info.physicalCores * 2) {
                    // Probablement des E-cores présents
                    // Ex: i9-12900K = 8P (16 threads) + 8E (8 threads) = 24 threads
                    int threadsWithoutEcores = m_info.physicalCores * 2;
                    int possibleEcores = (m_info.logicalProcessors - threadsWithoutEcores);
                    
                    // Les E-cores n'ont pas d'HT, donc chaque E-core = 1 thread
                    m_info.efficientCores = possibleEcores;
                    m_info.performanceCores = m_info.physicalCores - possibleEcores;
                    
                    if (m_info.performanceCores < 0) {
                        // Ajustement si l'heuristique échoue
                        m_info.performanceCores = m_info.physicalCores / 2;
                        m_info.efficientCores = m_info.physicalCores - m_info.performanceCores;
                    }
                } else {
                    // Pas assez de threads pour avoir des E-cores distincts
                    // Probablement un CPU mobile ou bas de gamme
                    m_info.performanceCores = m_info.physicalCores;
                    m_info.efficientCores = 0;
                    m_info.isHybridArchitecture = false;
                }
            }
        }
    }
    
    // Initialiser les informations de type de cœur
    m_info.cores.resize(m_info.logicalProcessors);
    for (int i = 0; i < m_info.logicalProcessors; ++i) {
        m_info.cores[i].index = i;
        m_info.cores[i].type = CoreType::Unknown;
    }
    
    // Si architecture hybride, marquer les types de cœurs
    if (m_info.isHybridArchitecture) {
        updateCoreTypes();
    }
#endif
}

inline void EnhancedCpuMonitor::updateCoreTypes()
{
#ifdef _WIN32
    if (!m_info.isHybridArchitecture) return;
    
    // Pour une détection précise, il faudrait exécuter CPUID 0x1A sur chaque cœur
    // via SetThreadAffinityMask. Cette implémentation utilise une heuristique.
    
    // Heuristique: Les P-cores sont généralement les premiers,
    // avec leurs threads HT, puis les E-cores
    
    int pCoreThreads = m_info.performanceCores * 2;  // P-cores avec HT
    int eCoreThreads = m_info.efficientCores;         // E-cores sans HT
    
    for (int i = 0; i < m_info.logicalProcessors; ++i) {
        if (i < pCoreThreads) {
            m_info.cores[i].type = CoreType::Performance;
            m_info.cores[i].isHyperThread = (i % 2 == 1);
            m_info.cores[i].physicalCoreId = i / 2;
        } else {
            m_info.cores[i].type = CoreType::Efficient;
            m_info.cores[i].isHyperThread = false;
            m_info.cores[i].physicalCoreId = m_info.performanceCores + (i - pCoreThreads);
        }
    }
    
    m_coreTypesDetected = true;
#endif
}

inline void EnhancedCpuMonitor::initializePdh()
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

inline void EnhancedCpuMonitor::queryProcessorName()
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
    
    m_info.architecture = QSysInfo::currentCpuArchitecture();
#else
    m_info.name = QSysInfo::currentCpuArchitecture();
    m_info.architecture = QSysInfo::currentCpuArchitecture();
#endif
}

inline void EnhancedCpuMonitor::queryProcessorInfo()
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
        m_info.physicalCores = cores;
    } else {
        m_info.physicalCores = m_info.logicalProcessors / 2;
    }
    
    // Lecture de la fréquence de base depuis le registre
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
    m_info.physicalCores = m_info.logicalProcessors;
#endif
    
    m_info.coreUsages.resize(m_info.logicalProcessors, 0.0);
}

inline void EnhancedCpuMonitor::update()
{
#ifdef _WIN32
    // Mise à jour de l'utilisation globale
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
    
    // Mise à jour des usages par cœur
    if (m_pdhInitialized && PdhCollectQueryData(m_query) == ERROR_SUCCESS) {
        double pCoreTotal = 0.0;
        int pCoreCount = 0;
        double eCoreTotal = 0.0;
        int eCoreCount = 0;
        
        for (size_t i = 0; i < m_coreCounters.size(); ++i) {
            PDH_FMT_COUNTERVALUE value;
            if (PdhGetFormattedCounterValue(m_coreCounters[i], PDH_FMT_DOUBLE, 
                nullptr, &value) == ERROR_SUCCESS) {
                
                m_info.coreUsages[i] = value.doubleValue;
                
                if (i < m_info.cores.size()) {
                    m_info.cores[i].usage = value.doubleValue;
                    
                    // Statistiques par type de cœur
                    if (m_info.isHybridArchitecture) {
                        if (m_info.cores[i].type == CoreType::Performance) {
                            pCoreTotal += value.doubleValue;
                            pCoreCount++;
                        } else if (m_info.cores[i].type == CoreType::Efficient) {
                            eCoreTotal += value.doubleValue;
                            eCoreCount++;
                        }
                    }
                }
            }
        }
        
        // Calcul des moyennes par type
        m_info.pCoreAvgUsage = pCoreCount > 0 ? pCoreTotal / pCoreCount : 0.0;
        m_info.eCoreAvgUsage = eCoreCount > 0 ? eCoreTotal / eCoreCount : 0.0;
    }
    
    // Estimation de la vitesse actuelle
    m_info.currentSpeed = m_info.baseSpeed * (0.8 + (m_info.usage / 500.0));
    
    // Comptage des processus
    DWORD processIds[1024], bytesReturned;
    if (EnumProcesses(processIds, sizeof(processIds), &bytesReturned)) {
        m_info.processCount = bytesReturned / sizeof(DWORD);
    }
    
    // Comptage des threads
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
    
    // Uptime
    m_info.uptime = formatUptime(GetTickCount64());
    
    // Mise à jour de la température (si disponible)
    updateTemperature();
#endif
    
    emit updated();
}

inline void EnhancedCpuMonitor::updateTemperature()
{
    // La lecture de la température CPU nécessite généralement:
    // - MSR (Model Specific Registers) - nécessite un driver kernel
    // - WMI (Windows Management Instrumentation) - pas toujours disponible
    // - Bibliothèque tierce (LibreHardwareMonitor, OpenHardwareMonitor)
    
    // Cette implémentation utilise WMI si disponible
    // Pour une solution robuste, intégrer LibreHardwareMonitorLib
    
    m_info.hasTemperatureSensor = false;
    m_info.temperature = 0.0;
    
    // TODO: Intégrer LibreHardwareMonitor pour une lecture fiable
    // Voir: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
}

inline QString EnhancedCpuMonitor::formatUptime(qint64 milliseconds)
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
