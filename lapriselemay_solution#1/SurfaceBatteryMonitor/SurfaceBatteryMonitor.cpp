/*
    Application Console Batterie - Surface Laptop Go 2
    Version: 2.0 (Refactorisée avec RAII)
    Standard: C++20
*/

#include <iostream>
#include <vector>
#include <windows.h>
#include <poclass.h>
#include <setupapi.h>
#include <devguid.h>
#include <iomanip>
#include <string>
#include <format> 
#include <cmath>
#include <memory>
#include <optional>

#pragma comment(lib, "setupapi.lib")

namespace SurfaceMonitor {

// --- Couleurs ANSI ---
namespace Color {
    constexpr const char* Reset = "\033[0m";
    constexpr const char* Red = "\033[31m";
    constexpr const char* Green = "\033[32m";
    constexpr const char* Yellow = "\033[33m";
    constexpr const char* Cyan = "\033[36m";
    constexpr const char* Magenta = "\033[35m";
    constexpr const char* Bold = "\033[1m";
}

/// <summary>
/// Statistiques de la batterie
/// </summary>
struct BatteryStats {
    bool isConnected = false;
    bool isCharging = false;

    unsigned long designCapacity = 0;
    unsigned long fullChargeCapacity = 0;
    unsigned long cycleCount = 0;
    unsigned long currentCapacity = 0;
    int chargePercentage = 0;
    long rateInMilliwatts = 0;

    long calculatedTimeSeconds = -1;
    std::string timeStatus = "Inconnu";
    
    /// <summary>
    /// Calcule le pourcentage de santé de la batterie
    /// </summary>
    [[nodiscard]] double GetHealthPercentage() const noexcept {
        if (designCapacity == 0) return 0.0;
        return static_cast<double>(fullChargeCapacity) / designCapacity * 100.0;
    }
    
    /// <summary>
    /// Calcule la puissance en watts
    /// </summary>
    [[nodiscard]] double GetPowerWatts() const noexcept {
        return std::abs(rateInMilliwatts) / 1000.0;
    }
};

/// <summary>
/// RAII wrapper pour les handles Windows
/// </summary>
class UniqueHandle {
public:
    UniqueHandle() noexcept : handle_(INVALID_HANDLE_VALUE) {}
    
    explicit UniqueHandle(HANDLE h) noexcept : handle_(h) {}
    
    ~UniqueHandle() { Close(); }
    
    // Move only
    UniqueHandle(UniqueHandle&& other) noexcept : handle_(other.handle_) {
        other.handle_ = INVALID_HANDLE_VALUE;
    }
    
    UniqueHandle& operator=(UniqueHandle&& other) noexcept {
        if (this != &other) {
            Close();
            handle_ = other.handle_;
            other.handle_ = INVALID_HANDLE_VALUE;
        }
        return *this;
    }
    
    // No copy
    UniqueHandle(const UniqueHandle&) = delete;
    UniqueHandle& operator=(const UniqueHandle&) = delete;
    
    [[nodiscard]] HANDLE Get() const noexcept { return handle_; }
    [[nodiscard]] bool IsValid() const noexcept { 
        return handle_ != INVALID_HANDLE_VALUE && handle_ != nullptr; 
    }
    [[nodiscard]] explicit operator bool() const noexcept { return IsValid(); }
    
    void Close() noexcept {
        if (IsValid()) {
            CloseHandle(handle_);
            handle_ = INVALID_HANDLE_VALUE;
        }
    }
    
    HANDLE Release() noexcept {
        HANDLE temp = handle_;
        handle_ = INVALID_HANDLE_VALUE;
        return temp;
    }
    
private:
    HANDLE handle_;
};

/// <summary>
/// RAII wrapper pour HDEVINFO
/// </summary>
class DeviceInfoSet {
public:
    DeviceInfoSet() noexcept : hDevInfo_(INVALID_HANDLE_VALUE) {}
    
    explicit DeviceInfoSet(HDEVINFO h) noexcept : hDevInfo_(h) {}
    
    ~DeviceInfoSet() { Destroy(); }
    
    // Move only
    DeviceInfoSet(DeviceInfoSet&& other) noexcept : hDevInfo_(other.hDevInfo_) {
        other.hDevInfo_ = INVALID_HANDLE_VALUE;
    }
    
    DeviceInfoSet& operator=(DeviceInfoSet&& other) noexcept {
        if (this != &other) {
            Destroy();
            hDevInfo_ = other.hDevInfo_;
            other.hDevInfo_ = INVALID_HANDLE_VALUE;
        }
        return *this;
    }
    
    DeviceInfoSet(const DeviceInfoSet&) = delete;
    DeviceInfoSet& operator=(const DeviceInfoSet&) = delete;
    
    [[nodiscard]] HDEVINFO Get() const noexcept { return hDevInfo_; }
    [[nodiscard]] bool IsValid() const noexcept { return hDevInfo_ != INVALID_HANDLE_VALUE; }
    [[nodiscard]] explicit operator bool() const noexcept { return IsValid(); }
    
    void Destroy() noexcept {
        if (IsValid()) {
            SetupDiDestroyDeviceInfoList(hDevInfo_);
            hDevInfo_ = INVALID_HANDLE_VALUE;
        }
    }
    
private:
    HDEVINFO hDevInfo_;
};

/// <summary>
/// Moniteur de batterie avec gestion RAII des ressources
/// </summary>
class BatteryMonitor {
public:
    BatteryMonitor() = default;
    
    /// <summary>
    /// Analyse l'état actuel de la batterie
    /// </summary>
    [[nodiscard]] BatteryStats Analyze() const {
        BatteryStats stats;
        
        // 1. Infos système (secteur/batterie)
        QuerySystemPowerStatus(stats);
        
        // 2. Infos détaillées via le pilote
        QueryBatteryDriver(stats);
        
        // 3. Calcul de l'autonomie
        CalculateTimeRemaining(stats);
        
        return stats;
    }
    
private:
    static void QuerySystemPowerStatus(BatteryStats& stats) {
        SYSTEM_POWER_STATUS sps{};
        if (GetSystemPowerStatus(&sps)) {
            stats.isCharging = (sps.ACLineStatus == 1);
            stats.chargePercentage = sps.BatteryLifePercent;
        }
    }
    
    static void QueryBatteryDriver(BatteryStats& stats) {
        auto handle = GetBatteryHandle();
        if (!handle) return;
        
        stats.isConnected = true;
        
        BATTERY_QUERY_INFORMATION bqi{};
        DWORD dwWait = 0, dwOut = 0;
        
        // Récupération du TAG
        if (!DeviceIoControl(handle.Get(), IOCTL_BATTERY_QUERY_TAG, 
            &dwWait, sizeof(dwWait), &bqi.BatteryTag, sizeof(bqi.BatteryTag), &dwOut, nullptr)) {
            return;
        }
        
        // Infos statiques
        BATTERY_INFORMATION bi{};
        bqi.InformationLevel = BatteryInformation;
        if (DeviceIoControl(handle.Get(), IOCTL_BATTERY_QUERY_INFORMATION, 
            &bqi, sizeof(bqi), &bi, sizeof(bi), &dwOut, nullptr)) {
            stats.designCapacity = bi.DesignedCapacity;
            stats.fullChargeCapacity = bi.FullChargedCapacity;
            stats.cycleCount = bi.CycleCount;
        }
        
        // Infos dynamiques
        BATTERY_STATUS bs{};
        BATTERY_WAIT_STATUS bws{};
        bws.BatteryTag = bqi.BatteryTag;
        if (DeviceIoControl(handle.Get(), IOCTL_BATTERY_QUERY_STATUS, 
            &bws, sizeof(bws), &bs, sizeof(bs), &dwOut, nullptr)) {
            stats.currentCapacity = bs.Capacity;
            stats.rateInMilliwatts = bs.Rate;
        }
    }
    
    [[nodiscard]] static UniqueHandle GetBatteryHandle() {
        DeviceInfoSet devInfo{SetupDiGetClassDevs(
            &GUID_DEVCLASS_BATTERY, nullptr, nullptr, 
            DIGCF_PRESENT | DIGCF_DEVICEINTERFACE)};
        
        if (!devInfo) return UniqueHandle{};
        
        SP_DEVICE_INTERFACE_DATA deviceInterfaceData{};
        deviceInterfaceData.cbSize = sizeof(SP_DEVICE_INTERFACE_DATA);
        
        if (!SetupDiEnumDeviceInterfaces(devInfo.Get(), nullptr, 
            &GUID_DEVCLASS_BATTERY, 0, &deviceInterfaceData)) {
            return UniqueHandle{};
        }
        
        DWORD cbRequired = 0;
        SetupDiGetDeviceInterfaceDetail(devInfo.Get(), &deviceInterfaceData, 
            nullptr, 0, &cbRequired, nullptr);
        
        std::vector<char> buffer(cbRequired);
        auto* deviceDetail = reinterpret_cast<PSP_DEVICE_INTERFACE_DETAIL_DATA>(buffer.data());
        deviceDetail->cbSize = sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA);
        
        if (!SetupDiGetDeviceInterfaceDetail(devInfo.Get(), &deviceInterfaceData, 
            deviceDetail, cbRequired, &cbRequired, nullptr)) {
            return UniqueHandle{};
        }
        
        return UniqueHandle{CreateFile(
            deviceDetail->DevicePath,
            GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr)};
    }
    
    static void CalculateTimeRemaining(BatteryStats& stats) {
        const double rate = std::abs(stats.rateInMilliwatts);
        
        if (rate <= 0) {
            stats.calculatedTimeSeconds = -1;
            stats.timeStatus = "Calcul en cours...";
            return;
        }
        
        if (!stats.isCharging) {
            // Décharge
            const double hoursLeft = static_cast<double>(stats.currentCapacity) / rate;
            stats.calculatedTimeSeconds = static_cast<long>(hoursLeft * 3600);
            stats.timeStatus = "Restant";
        } else {
            // Charge
            if (stats.fullChargeCapacity > stats.currentCapacity) {
                const double capacityNeeded = static_cast<double>(
                    stats.fullChargeCapacity - stats.currentCapacity);
                const double hoursLeft = capacityNeeded / rate;
                stats.calculatedTimeSeconds = static_cast<long>(hoursLeft * 3600);
                stats.timeStatus = "Avant 100%";
            } else {
                stats.calculatedTimeSeconds = 0;
                stats.timeStatus = "Charge terminee";
            }
        }
    }
};

/// <summary>
/// Formatte une durée en heures:minutes
/// </summary>
[[nodiscard]] std::string FormatTime(long seconds) {
    if (seconds < 0) return "-- h -- min";
    if (seconds == 0) return "Termine";
    return std::format("{}h {:02}min", seconds / 3600, (seconds % 3600) / 60);
}

/// <summary>
/// Affiche le dashboard dans la console
/// </summary>
void DisplayDashboard(const BatteryStats& stats) {
    // Effacement propre de l'écran
    std::cout << "\033[H\033[J";

    std::cout << "=================================================\n";
    std::cout << "   SURFACE MONITOR v2.0 (RAII)\n";
    std::cout << "=================================================\n\n";

    if (!stats.isConnected) { 
        std::cout << " [!] Erreur d'acces au pilote batterie.\n"; 
        return; 
    }

    const char* colorPct = (stats.chargePercentage < 20) ? Color::Red : Color::Green;
    const double watts = stats.GetPowerWatts();

    std::cout << Color::Bold << " 1. TEMPS REEL" << Color::Reset << "\n";
    std::cout << " -------------\n";
    std::cout << " Source            : " << (stats.isCharging ? "Secteur (En charge)" : "Batterie") << "\n";
    std::cout << " Niveau Charge     : " << colorPct << stats.chargePercentage << " %" << Color::Reset 
              << " (" << stats.currentCapacity << " mWh)\n";

    std::cout << " Puissance         : ";
    if (stats.isCharging) {
        std::cout << Color::Green << "+" << std::fixed << std::setprecision(2) << watts << " W" << Color::Reset << "\n";
    } else {
        std::cout << Color::Red << "-" << std::fixed << std::setprecision(2) << watts << " W" << Color::Reset << "\n";
    }

    std::cout << "\n";
    std::cout << " Autonomie (Calc)  : " << Color::Magenta << FormatTime(stats.calculatedTimeSeconds) 
              << Color::Reset << "\n";
    std::cout << " (" << stats.timeStatus << ")\n\n";

    std::cout << Color::Bold << " 2. SANTE (HEALTH)" << Color::Reset << "\n";
    std::cout << " -----------------\n";

    const double health = stats.GetHealthPercentage();
    std::cout << " Sante Batterie    : " << (health > 80 ? Color::Green : Color::Yellow) 
              << std::fixed << std::setprecision(1) << health << " %" << Color::Reset << "\n";
    std::cout << " Cycles de Charge  : " << stats.cycleCount << "\n";
    std::cout << " Usure Capacite    : " << stats.fullChargeCapacity << " mWh (Actuelle) / " 
              << stats.designCapacity << " mWh (Neuve)\n";

    std::cout << "\n=================================================\n";
    std::cout << "Ctrl+C pour quitter.";
}

/// <summary>
/// Configure la console pour les couleurs ANSI
/// </summary>
void InitializeConsole() {
    HANDLE hOut = GetStdHandle(STD_OUTPUT_HANDLE);
    DWORD dwMode = 0;
    GetConsoleMode(hOut, &dwMode);
    dwMode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
    SetConsoleMode(hOut, dwMode);
    SetConsoleOutputCP(CP_UTF8);
}

} // namespace SurfaceMonitor

int main() {
    SurfaceMonitor::InitializeConsole();
    SurfaceMonitor::BatteryMonitor monitor;

    while (true) {
        const auto stats = monitor.Analyze();
        SurfaceMonitor::DisplayDashboard(stats);
        Sleep(1000);
    }
    
    return 0;
}
