// RegistryUtils.h - Registry Utility Functions
#pragma once

#include "pch.h"
#include "RegistryKey.h"

namespace RegistryCleaner::Registry::Utils {

    // Check if a path exists in the file system
    [[nodiscard]] inline bool PathExists(StringView path) {
        if (path.empty()) return false;
        
        // Expand environment variables
        wchar_t expandedPath[MAX_PATH];
        DWORD result = ExpandEnvironmentStringsW(
            String(path).c_str(),
            expandedPath,
            MAX_PATH
        );
        
        if (result == 0 || result > MAX_PATH) {
            return false;
        }
        
        // Check if file or directory exists
        DWORD attrs = GetFileAttributesW(expandedPath);
        return attrs != INVALID_FILE_ATTRIBUTES;
    }

    // Check if a file exists (not directory)
    [[nodiscard]] inline bool FileExists(StringView path) {
        if (path.empty()) return false;
        
        wchar_t expandedPath[MAX_PATH];
        DWORD result = ExpandEnvironmentStringsW(
            String(path).c_str(),
            expandedPath,
            MAX_PATH
        );
        
        if (result == 0 || result > MAX_PATH) {
            return false;
        }
        
        DWORD attrs = GetFileAttributesW(expandedPath);
        return (attrs != INVALID_FILE_ATTRIBUTES) && 
               !(attrs & FILE_ATTRIBUTE_DIRECTORY);
    }

    // Check if a directory exists
    [[nodiscard]] inline bool DirectoryExists(StringView path) {
        if (path.empty()) return false;
        
        wchar_t expandedPath[MAX_PATH];
        DWORD result = ExpandEnvironmentStringsW(
            String(path).c_str(),
            expandedPath,
            MAX_PATH
        );
        
        if (result == 0 || result > MAX_PATH) {
            return false;
        }
        
        DWORD attrs = GetFileAttributesW(expandedPath);
        return (attrs != INVALID_FILE_ATTRIBUTES) && 
               (attrs & FILE_ATTRIBUTE_DIRECTORY);
    }

    // Extract file path from a registry value (handles quoted paths, arguments, etc.)
    [[nodiscard]] inline std::optional<String> ExtractFilePath(StringView value) {
        if (value.empty()) return std::nullopt;
        
        String path(value);
        
        // Remove leading/trailing whitespace
        size_t start = path.find_first_not_of(L" \t");
        size_t end = path.find_last_not_of(L" \t");
        if (start == String::npos) return std::nullopt;
        path = path.substr(start, end - start + 1);
        
        // Handle quoted paths
        if (path.front() == L'"') {
            size_t endQuote = path.find(L'"', 1);
            if (endQuote != String::npos) {
                return path.substr(1, endQuote - 1);
            }
        }
        
        // Handle paths with arguments (look for .exe, .dll, etc.)
        const std::vector<StringView> extensions = {
            L".exe", L".EXE", L".dll", L".DLL", 
            L".ocx", L".OCX", L".sys", L".SYS",
            L".cpl", L".CPL", L".scr", L".SCR"
        };
        
        for (const auto& ext : extensions) {
            size_t pos = path.find(ext);
            if (pos != String::npos) {
                return path.substr(0, pos + ext.size());
            }
        }
        
        // Handle paths with space separator (common in Run entries)
        size_t spacePos = path.find(L' ');
        if (spacePos != String::npos) {
            String potentialPath = path.substr(0, spacePos);
            if (PathExists(potentialPath)) {
                return potentialPath;
            }
        }
        
        return path;
    }

    // Check if a CLSID exists and is valid
    [[nodiscard]] inline bool IsValidCLSID(StringView clsid) {
        if (clsid.empty()) return false;
        
        // Try to open the CLSID key
        auto keyResult = RegistryKey::Open(
            RootKey::ClassesRoot,
            std::format(L"CLSID\\{}", clsid),
            KEY_READ
        );
        
        return keyResult.has_value();
    }

    // Check if a ProgID exists
    [[nodiscard]] inline bool IsValidProgID(StringView progId) {
        if (progId.empty()) return false;
        
        auto keyResult = RegistryKey::Open(
            RootKey::ClassesRoot,
            progId,
            KEY_READ
        );
        
        return keyResult.has_value();
    }

    // Parse a root key string to RootKey enum
    [[nodiscard]] inline std::optional<RootKey> ParseRootKey(StringView keyPath) {
        String upper(keyPath);
        ranges::transform(upper, upper.begin(), ::towupper);
        
        if (upper.starts_with(L"HKEY_CLASSES_ROOT") || upper.starts_with(L"HKCR")) {
            return RootKey::ClassesRoot;
        }
        if (upper.starts_with(L"HKEY_CURRENT_USER") || upper.starts_with(L"HKCU")) {
            return RootKey::CurrentUser;
        }
        if (upper.starts_with(L"HKEY_LOCAL_MACHINE") || upper.starts_with(L"HKLM")) {
            return RootKey::LocalMachine;
        }
        if (upper.starts_with(L"HKEY_USERS") || upper.starts_with(L"HKU")) {
            return RootKey::Users;
        }
        if (upper.starts_with(L"HKEY_CURRENT_CONFIG") || upper.starts_with(L"HKCC")) {
            return RootKey::CurrentConfig;
        }
        
        return std::nullopt;
    }

    // Split a full key path into root and subkey
    [[nodiscard]] inline std::pair<std::optional<RootKey>, String> SplitKeyPath(StringView fullPath) {
        size_t backslash = fullPath.find(L'\\');
        
        if (backslash == StringView::npos) {
            return { ParseRootKey(fullPath), String{} };
        }
        
        auto root = ParseRootKey(fullPath.substr(0, backslash));
        String subKey(fullPath.substr(backslash + 1));
        
        return { root, std::move(subKey) };
    }

    // Format a file size in human-readable format
    [[nodiscard]] inline String FormatFileSize(uint64_t bytes) {
        constexpr uint64_t KB = 1024;
        constexpr uint64_t MB = KB * 1024;
        constexpr uint64_t GB = MB * 1024;
        
        if (bytes >= GB) {
            return std::format(L"{:.2f} Go", static_cast<double>(bytes) / GB);
        }
        if (bytes >= MB) {
            return std::format(L"{:.2f} Mo", static_cast<double>(bytes) / MB);
        }
        if (bytes >= KB) {
            return std::format(L"{:.2f} Ko", static_cast<double>(bytes) / KB);
        }
        return std::format(L"{} octets", bytes);
    }

    // Get current timestamp as string
    [[nodiscard]] inline String GetTimestamp() {
        auto now = chrono::system_clock::now();
        auto time = chrono::system_clock::to_time_t(now);
        
        std::tm tm;
        localtime_s(&tm, &time);
        
        return std::format(L"{:04d}-{:02d}-{:02d}_{:02d}-{:02d}-{:02d}",
            tm.tm_year + 1900, tm.tm_mon + 1, tm.tm_mday,
            tm.tm_hour, tm.tm_min, tm.tm_sec);
    }

} // namespace RegistryCleaner::Registry::Utils
