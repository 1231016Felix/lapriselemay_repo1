#pragma once

#include <Windows.h>
#include <string>
#include <vector>
#include <chrono>

namespace DriverManager {

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
        
        uint32_t problemCode = 0;
        
        // For UI selection
        bool selected = false;
    };

    struct DriverCategory {
        std::wstring name;
        DriverType type;
        std::vector<DriverInfo> drivers;
        bool expanded = true;
    };

    // Convert wide string to UTF-8 for ImGui
    inline std::string WideToUtf8(const std::wstring& wide) {
        if (wide.empty()) return "";
        int size = WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), (int)wide.size(), nullptr, 0, nullptr, nullptr);
        std::string result(size, 0);
        WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), (int)wide.size(), result.data(), size, nullptr, nullptr);
        return result;
    }

    // Convert UTF-8 to wide string
    inline std::wstring Utf8ToWide(const std::string& utf8) {
        if (utf8.empty()) return L"";
        int size = MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), (int)utf8.size(), nullptr, 0);
        std::wstring result(size, 0);
        MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), (int)utf8.size(), result.data(), size);
        return result;
    }

    // Get status color for ImGui
    inline uint32_t GetStatusColor(DriverStatus status) {
        switch (status) {
            case DriverStatus::OK:       return 0xFF00FF00; // Green
            case DriverStatus::Warning:  return 0xFF00FFFF; // Yellow
            case DriverStatus::Error:    return 0xFF0000FF; // Red
            case DriverStatus::Disabled: return 0xFF808080; // Gray
            default:                     return 0xFFFFFFFF; // White
        }
    }

    // Get status text
    inline const char* GetStatusText(DriverStatus status) {
        switch (status) {
            case DriverStatus::OK:       return "OK";
            case DriverStatus::Warning:  return "Avertissement";
            case DriverStatus::Error:    return "Erreur";
            case DriverStatus::Disabled: return "Désactivé";
            default:                     return "Inconnu";
        }
    }

    // Get type text
    inline const char* GetTypeText(DriverType type) {
        switch (type) {
            case DriverType::System:    return "Système";
            case DriverType::Display:   return "Affichage";
            case DriverType::Audio:     return "Audio";
            case DriverType::Network:   return "Réseau";
            case DriverType::Storage:   return "Stockage";
            case DriverType::USB:       return "USB";
            case DriverType::Bluetooth: return "Bluetooth";
            case DriverType::Printer:   return "Imprimante";
            case DriverType::HID:       return "Périphérique d'entrée";
            default:                    return "Autre";
        }
    }

} // namespace DriverManager
