/*
    Application Console Batterie - Surface Laptop Go 2
    Version: Finale (Stable & Epuree)
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

// On garde uniquement setupapi pour la communication materielle
#pragma comment(lib, "setupapi.lib")

// --- Couleurs ---
const std::string RESET = "\033[0m";
const std::string RED = "\033[31m";
const std::string GREEN = "\033[32m";
const std::string YELLOW = "\033[33m";
const std::string CYAN = "\033[36m";
const std::string MAGENTA = "\033[35m";
const std::string BOLD = "\033[1m";

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
};

// --- Partie Materielle (Connexion au pilote batterie) ---
HANDLE GetBatteryHandle() {
    HDEVINFO hDevInfo = SetupDiGetClassDevs(&GUID_DEVCLASS_BATTERY, 0, 0, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
    if (hDevInfo == INVALID_HANDLE_VALUE) return INVALID_HANDLE_VALUE;

    SP_DEVICE_INTERFACE_DATA deviceInterfaceData = { 0 };
    deviceInterfaceData.cbSize = sizeof(SP_DEVICE_INTERFACE_DATA);

    if (SetupDiEnumDeviceInterfaces(hDevInfo, 0, &GUID_DEVCLASS_BATTERY, 0, &deviceInterfaceData)) {
        DWORD cbRequired = 0;
        SetupDiGetDeviceInterfaceDetail(hDevInfo, &deviceInterfaceData, 0, 0, &cbRequired, 0);
        std::vector<char> buffer(cbRequired);
        PSP_DEVICE_INTERFACE_DETAIL_DATA deviceDetail = (PSP_DEVICE_INTERFACE_DETAIL_DATA)buffer.data();
        deviceDetail->cbSize = sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA);

        if (SetupDiGetDeviceInterfaceDetail(hDevInfo, &deviceInterfaceData, deviceDetail, cbRequired, &cbRequired, 0)) {
            HANDLE hBattery = CreateFile(deviceDetail->DevicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
            SetupDiDestroyDeviceInfoList(hDevInfo);
            return hBattery;
        }
    }
    SetupDiDestroyDeviceInfoList(hDevInfo);
    return INVALID_HANDLE_VALUE;
}

// --- Analyse et Calculs ---
BatteryStats AnalyzeBattery() {
    BatteryStats stats;

    // 1. Infos basiques (Pour savoir si on est sur secteur)
    SYSTEM_POWER_STATUS sps;
    if (GetSystemPowerStatus(&sps)) {
        stats.isCharging = (sps.ACLineStatus == 1);
        stats.chargePercentage = sps.BatteryLifePercent;
    }

    // 2. Infos precises via le pilote
    HANDLE hBattery = GetBatteryHandle();
    if (hBattery != INVALID_HANDLE_VALUE) {
        stats.isConnected = true;
        BATTERY_QUERY_INFORMATION bqi = { 0 };
        DWORD dwWait = 0, dwOut;

        // Recuperation du TAG (Identifiant de session batterie)
        if (DeviceIoControl(hBattery, IOCTL_BATTERY_QUERY_TAG, &dwWait, sizeof(dwWait), &bqi.BatteryTag, sizeof(bqi.BatteryTag), &dwOut, NULL)) {

            // Infos Statiques (Capacite usine, Cycles)
            BATTERY_INFORMATION bi = { 0 };
            bqi.InformationLevel = BatteryInformation;
            if (DeviceIoControl(hBattery, IOCTL_BATTERY_QUERY_INFORMATION, &bqi, sizeof(bqi), &bi, sizeof(bi), &dwOut, NULL)) {
                stats.designCapacity = bi.DesignedCapacity;
                stats.fullChargeCapacity = bi.FullChargedCapacity;
                stats.cycleCount = bi.CycleCount;
            }

            // Infos Dynamiques (Consommation, Capacite actuelle)
            BATTERY_STATUS bs = { 0 };
            BATTERY_WAIT_STATUS bws = { 0 };
            bws.BatteryTag = bqi.BatteryTag;
            if (DeviceIoControl(hBattery, IOCTL_BATTERY_QUERY_STATUS, &bws, sizeof(bws), &bs, sizeof(bs), &dwOut, NULL)) {
                stats.currentCapacity = bs.Capacity;
                stats.rateInMilliwatts = bs.Rate;
            }
        }
        CloseHandle(hBattery);
    }

    // 3. Calcul Mathematique de l'autonomie
    double rate = std::abs(stats.rateInMilliwatts);
    if (rate > 0) {
        if (!stats.isCharging) {
            // Decharge : Temps = Capacité Restante / Vitesse de consommation
            double hoursLeft = (double)stats.currentCapacity / rate;
            stats.calculatedTimeSeconds = static_cast<long>(hoursLeft * 3600);
            stats.timeStatus = "Restant";
        }
        else {
            // Charge : Temps = (Capacité Max - Actuelle) / Vitesse de charge
            if (stats.fullChargeCapacity > stats.currentCapacity) {
                double capacityNeeded = (double)(stats.fullChargeCapacity - stats.currentCapacity);
                double hoursLeft = capacityNeeded / rate;
                stats.calculatedTimeSeconds = static_cast<long>(hoursLeft * 3600);
                stats.timeStatus = "Avant 100%";
            }
            else {
                stats.calculatedTimeSeconds = 0;
                stats.timeStatus = "Charge terminee";
            }
        }
    }
    else {
        stats.calculatedTimeSeconds = -1;
        stats.timeStatus = "Calcul en cours...";
    }

    return stats;
}

std::string FormatTime(long seconds) {
    if (seconds < 0) return "-- h -- min";
    if (seconds == 0) return "Termine";
    return std::format("{}h {:02}min", seconds / 3600, (seconds % 3600) / 60);
}

void DisplayDashboard(const BatteryStats& stats) {
    // Effacement propre de l'ecran
    std::cout << "\033[H\033[J";

    std::cout << "=================================================\n";
    std::cout << "   SURFACE MONITOR (Stable)\n";
    std::cout << "=================================================\n\n";

    if (!stats.isConnected) { std::cout << " [!] Erreur d'acces au pilote batterie.\n"; return; }

    std::string colorPct = (stats.chargePercentage < 20) ? RED : GREEN;
    double watts = std::abs(stats.rateInMilliwatts) / 1000.0;

    std::cout << BOLD << " 1. TEMPS REEL" << RESET << "\n";
    std::cout << " -------------\n";
    std::cout << " Source            : " << (stats.isCharging ? "Secteur (En charge)" : "Batterie") << "\n";
    std::cout << " Niveau Charge     : " << colorPct << stats.chargePercentage << " %" << RESET << " (" << stats.currentCapacity << " mWh)\n"; // Ajout petit détail ici aussi

    std::cout << " Puissance         : ";
    if (stats.isCharging) std::cout << GREEN << "+" << std::fixed << std::setprecision(2) << watts << " W" << RESET << "\n";
    else std::cout << RED << "-" << std::fixed << std::setprecision(2) << watts << " W" << RESET << "\n";

    std::cout << "\n";
    std::cout << " Autonomie (Calc)  : " << MAGENTA << FormatTime(stats.calculatedTimeSeconds) << RESET << "\n";
    std::cout << " (" << stats.timeStatus << ")\n\n";

    std::cout << BOLD << " 2. SANTE (HEALTH)" << RESET << "\n";
    std::cout << " -----------------\n";

    // Calcul pourcentage santé
    double health = 0.0;
    if (stats.designCapacity > 0) {
        health = (double)stats.fullChargeCapacity / stats.designCapacity * 100.0;
    }

    std::cout << " Sante Batterie    : " << (health > 80 ? GREEN : YELLOW) << std::fixed << std::setprecision(1) << health << " %" << RESET << "\n";
    std::cout << " Cycles de Charge  : " << stats.cycleCount << "\n";

    // --- MODIFICATION ICI ---
    // Affiche Capacité Actuelle Max vs Capacité Usine
    std::cout << " Usure Capacite    : " << stats.fullChargeCapacity << " mWh (Actuelle) / " << stats.designCapacity << " mWh (Neuve)\n";

    std::cout << "\n=================================================\n";
    std::cout << "Ctrl+C pour quitter.";
}

int main() {
    // Activation des couleurs ANSI
    HANDLE hOut = GetStdHandle(STD_OUTPUT_HANDLE);
    DWORD dwMode = 0;
    GetConsoleMode(hOut, &dwMode);
    dwMode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
    SetConsoleMode(hOut, dwMode);
    SetConsoleOutputCP(CP_UTF8);

    while (true) {
        BatteryStats currentStats = AnalyzeBattery();
        DisplayDashboard(currentStats);
        Sleep(1000); // Rafraichissement chaque seconde
    }
    return 0;
}