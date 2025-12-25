// Config.h - Application Configuration
#pragma once

#include "pch.h"

namespace RegistryCleaner::Config {

    // Application info
    inline constexpr StringView APP_NAME = L"Windows Registry Cleaner";
    inline constexpr StringView APP_VERSION = L"2.0.0";

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
        ActiveX,             // ActiveX/COM components
        AppPaths,            // Application paths
        Software,            // Software paths
        HelpFiles,           // Help file references
        Firewall,            // Firewall rules
        Fonts,               // Font entries
        StartMenu,           // Start menu entries
        Sounds,              // Sound events
        BrowserHistory,      // IE History/TypedURLs
        ImageExecution,      // IFEO entries
        EmptyKeys,           // Empty registry keys
        Services,            // Windows services
        MUICache,            // MUI Cache
        ContextMenu,         // Context menu handlers
        Other                // Miscellaneous
    };

    // Get category name
    [[nodiscard]] inline String GetCategoryName(IssueCategory category) {
        switch (category) {
            case IssueCategory::UninstallEntry:  return L"Desinstallation";
            case IssueCategory::FileExtension:   return L"Extensions fichiers";
            case IssueCategory::MRUEntry:        return L"Fichiers recents";
            case IssueCategory::StartupEntry:    return L"Demarrage";
            case IssueCategory::SharedDll:       return L"DLLs partagees";
            case IssueCategory::ActiveX:         return L"ActiveX/COM";
            case IssueCategory::AppPaths:        return L"Chemins applications";
            case IssueCategory::Software:        return L"Chemins logiciels";
            case IssueCategory::HelpFiles:       return L"Fichiers aide";
            case IssueCategory::Firewall:        return L"Pare-feu";
            case IssueCategory::Fonts:           return L"Polices";
            case IssueCategory::StartMenu:       return L"Menu Demarrer";
            case IssueCategory::Sounds:          return L"Sons";
            case IssueCategory::BrowserHistory:  return L"Historique IE";
            case IssueCategory::ImageExecution:  return L"Execution Image";
            case IssueCategory::EmptyKeys:       return L"Cles vides";
            case IssueCategory::Services:        return L"Services";
            case IssueCategory::MUICache:        return L"Cache MUI";
            case IssueCategory::ContextMenu:     return L"Menu contextuel";
            case IssueCategory::Other:           return L"Autres";
            default:                             return L"Inconnu";
        }
    }

    // Get severity name
    [[nodiscard]] inline String GetSeverityName(Severity severity) {
        switch (severity) {
            case Severity::Low:      return L"Faible";
            case Severity::Medium:   return L"Moyen";
            case Severity::High:     return L"Eleve";
            case Severity::Critical: return L"Critique";
            default:                 return L"Inconnu";
        }
    }

} // namespace RegistryCleaner::Config
