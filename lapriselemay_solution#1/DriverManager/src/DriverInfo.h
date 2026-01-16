#pragma once

#include <Windows.h>
#include <string>
#include <vector>
#include <chrono>
#include <ctime>
#include <algorithm>

#include "StringUtils.h"  // Utiliser les fonctions centralisées

namespace DriverManager {

    // Driver age thresholds (days)
    constexpr int DRIVER_AGE_OLD_THRESHOLD = 365;      // 1 year
    constexpr int DRIVER_AGE_VERY_OLD_THRESHOLD = 730; // 2 years

    enum class DriverStatus {
        OK,
        Warning,
        Error,
        Disabled,
        Unknown
    };

    enum class DriverType {
        System,
        Display,
        Audio,
        Network,
        Storage,
        USB,
        Bluetooth,
        Printer,
        HID,
        Other
    };
    
    enum class DriverAge {
        Current,      // < 1 an
        Old,          // 1-2 ans
        VeryOld,      // > 2 ans
        Unknown
    };
    
    struct UpdateInfo {
        std::wstring newVersion;
        std::wstring downloadUrl;
        std::wstring releaseDate;
        std::wstring description;
        uint64_t downloadSize = 0;
        bool isImportant = false;
    };

    struct DriverInfo {
        std::wstring deviceName;
        std::wstring deviceDescription;
        std::wstring manufacturer;
        std::wstring driverVersion;
        std::wstring driverDate;
        std::wstring driverProvider;
        std::wstring infPath;
        std::wstring hardwareId;
        std::wstring deviceClass;
        std::wstring deviceClassGuid;
        std::wstring deviceInstanceId;
        
        DriverStatus status = DriverStatus::Unknown;
        DriverType type = DriverType::Other;
        
        bool isEnabled = true;
        bool hasUpdate = false;
        bool isSystemCritical = false;
        bool updateCheckPending = false;
        
        uint32_t problemCode = 0;
        
        // Age tracking
        int driverAgeDays = -1;  // -1 = unknown
        DriverAge ageCategory = DriverAge::Unknown;
        
        // Update information
        UpdateInfo availableUpdate;
        
        // For UI selection
        bool selected = false;
        
        // Pré-calculé pour recherche rapide (lowercase)
        std::string searchNameLower;
        std::string searchManufacturerLower;
        
        // Helper to parse driver date and calculate age
        void CalculateAge() {
            if (driverDate.empty()) {
                ageCategory = DriverAge::Unknown;
                driverAgeDays = -1;
                return;
            }
            
            // Parse date format: YYYY-MM-DD
            int year = 0, month = 0, day = 0;
            if (swscanf_s(driverDate.c_str(), L"%d-%d-%d", &year, &month, &day) == 3) {
                // Get current time
                std::time_t now = std::time(nullptr);
                std::tm localNow;
                localtime_s(&localNow, &now);
                
                // Create driver date
                std::tm driverTm = {};
                driverTm.tm_year = year - 1900;
                driverTm.tm_mon = month - 1;
                driverTm.tm_mday = day;
                
                std::time_t driverTime = std::mktime(&driverTm);
                
                if (driverTime != -1) {
                    double diffSeconds = std::difftime(now, driverTime);
                    driverAgeDays = static_cast<int>(diffSeconds / (60 * 60 * 24));
                    
                    if (driverAgeDays < DRIVER_AGE_OLD_THRESHOLD) {
                        ageCategory = DriverAge::Current;
                    } else if (driverAgeDays < DRIVER_AGE_VERY_OLD_THRESHOLD) {
                        ageCategory = DriverAge::Old;
                    } else {
                        ageCategory = DriverAge::VeryOld;
                    }
                }
            }
        }
        
        // Pré-calcule les champs de recherche (appeler après le scan)
        void PrepareSearchFields() {
            searchNameLower = WideToUtf8(deviceName);
            searchManufacturerLower = WideToUtf8(manufacturer);
            std::transform(searchNameLower.begin(), searchNameLower.end(), 
                          searchNameLower.begin(), ::tolower);
            std::transform(searchManufacturerLower.begin(), searchManufacturerLower.end(), 
                          searchManufacturerLower.begin(), ::tolower);
        }
        
        // Vérifie si le driver correspond au filtre de recherche
        bool MatchesFilter(const std::string& filterLower) const {
            if (filterLower.empty()) return true;
            return searchNameLower.find(filterLower) != std::string::npos ||
                   searchManufacturerLower.find(filterLower) != std::string::npos;
        }
    };

    struct DriverCategory {
        std::wstring name;
        DriverType type;
        std::vector<DriverInfo> drivers;
        bool expanded = true;
    };

    // Get status text
    inline const char* GetStatusText(DriverStatus status) {
        switch (status) {
            case DriverStatus::OK:       return "OK";
            case DriverStatus::Warning:  return "Avertissement";
            case DriverStatus::Error:    return "Erreur";
            case DriverStatus::Disabled: return "D\xc3\xa9sactiv\xc3\xa9";
            default:                     return "Inconnu";
        }
    }

    // Get type text
    inline const char* GetTypeText(DriverType type) {
        switch (type) {
            case DriverType::System:    return "Syst\xc3\xa8me";
            case DriverType::Display:   return "Affichage";
            case DriverType::Audio:     return "Audio";
            case DriverType::Network:   return "R\xc3\xa9seau";
            case DriverType::Storage:   return "Stockage";
            case DriverType::USB:       return "USB";
            case DriverType::Bluetooth: return "Bluetooth";
            case DriverType::Printer:   return "Imprimante";
            case DriverType::HID:       return "P\xc3\xa9riph\xc3\xa9rique d'entr\xc3\xa9" "e";
            default:                    return "Autre";
        }
    }
    
    // Get age text
    inline const char* GetAgeText(DriverAge age) {
        switch (age) {
            case DriverAge::Current:  return "R\xc3\xa9" "cent";
            case DriverAge::Old:      return "1-2 ans";
            case DriverAge::VeryOld:  return "> 2 ans";
            default:                  return "Inconnu";
        }
    }
    
    // Format age in human readable form
    inline std::string FormatAgeDays(int days) {
        if (days < 0) return "Inconnu";
        if (days == 0) return "Aujourd'hui";
        if (days == 1) return "Hier";
        if (days < 30) return std::to_string(days) + " jours";
        if (days < 365) return std::to_string(days / 30) + " mois";
        int years = days / 365;
        int months = (days % 365) / 30;
        if (months > 0) {
            return std::to_string(years) + " an" + (years > 1 ? "s" : "") + " " + std::to_string(months) + " mois";
        }
        return std::to_string(years) + " an" + (years > 1 ? "s" : "");
    }

} // namespace DriverManager
