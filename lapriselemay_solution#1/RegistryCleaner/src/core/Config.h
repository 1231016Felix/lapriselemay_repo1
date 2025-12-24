// Config.h - Application Configuration
#pragma once

#include "pch.h"

namespace RegistryCleaner::Config {

    // Application info
    inline constexpr StringView APP_NAME = L"Windows Registry Cleaner";
    inline constexpr StringView APP_VERSION = L"1.0.0";

    // Backup settings
    inline constexpr StringView BACKUP_FOLDER = L"RegistryBackups";
    inline constexpr int MAX_BACKUP_FILES = 10;

    // Scan settings
    inline constexpr int MAX_SCAN_DEPTH = 10;
    inline constexpr int PROGRESS_UPDATE_INTERVAL_MS = 100;

    // Console colors (for Windows console)
    enum class ConsoleColor : WORD {
        Default = FOREGROUND_RED | FOREGROUND_GREEN | FOREGROUND_BLUE,
        Red = FOREGROUND_RED | FOREGROUND_INTENSITY,
        Green = FOREGROUND_GREEN | FOREGROUND_INTENSITY,
        Yellow = FOREGROUND_RED | FOREGROUND_GREEN | FOREGROUND_INTENSITY,
        Blue = FOREGROUND_BLUE | FOREGROUND_INTENSITY,
        Cyan = FOREGROUND_GREEN | FOREGROUND_BLUE | FOREGROUND_INTENSITY,
        Magenta = FOREGROUND_RED | FOREGROUND_BLUE | FOREGROUND_INTENSITY,
        White = FOREGROUND_RED | FOREGROUND_GREEN | FOREGROUND_BLUE | FOREGROUND_INTENSITY
    };

    // Issue severity levels
    enum class Severity {
        Low,        // Safe to remove (MRU entries, etc.)
        Medium,     // Likely orphaned (missing files/programs)
        High,       // Potentially problematic (broken COM references)
        Critical    // System critical - do not remove automatically
    };

    // Issue categories
    enum class IssueCategory {
        UninstallEntry,      // Orphaned uninstall entries
        FileExtension,       // Invalid file extension handlers
        MRUEntry,            // Most Recently Used lists
        StartupEntry,        // Invalid startup programs
        SharedDll,           // Orphaned SharedDLLs
        COMEntry,            // Invalid COM/ActiveX entries
        AppPath,             // Invalid application paths
        Installer,           // Windows Installer orphans
        Help,                // Orphaned help file references
        Font,                // Invalid font entries
        Sound,               // Invalid sound scheme entries
        Other                // Miscellaneous
    };

    // Get category name
    [[nodiscard]] inline String GetCategoryName(IssueCategory category) {
        switch (category) {
            case IssueCategory::UninstallEntry:  return L"Entrées de désinstallation";
            case IssueCategory::FileExtension:   return L"Extensions de fichiers";
            case IssueCategory::MRUEntry:        return L"Fichiers récents (MRU)";
            case IssueCategory::StartupEntry:    return L"Programmes au démarrage";
            case IssueCategory::SharedDll:       return L"DLLs partagées";
            case IssueCategory::COMEntry:        return L"Entrées COM/ActiveX";
            case IssueCategory::AppPath:         return L"Chemins d'applications";
            case IssueCategory::Installer:       return L"Windows Installer";
            case IssueCategory::Help:            return L"Fichiers d'aide";
            case IssueCategory::Font:            return L"Polices";
            case IssueCategory::Sound:           return L"Schémas sonores";
            case IssueCategory::Other:           return L"Autres";
            default:                             return L"Inconnu";
        }
    }

    // Get severity name
    [[nodiscard]] inline String GetSeverityName(Severity severity) {
        switch (severity) {
            case Severity::Low:      return L"Faible";
            case Severity::Medium:   return L"Moyen";
            case Severity::High:     return L"Élevé";
            case Severity::Critical: return L"Critique";
            default:                 return L"Inconnu";
        }
    }

} // namespace RegistryCleaner::Config
