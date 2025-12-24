// ProtectedKeys.h - Protected Registry Keys (Never Delete)
#pragma once

#include "pch.h"

namespace RegistryCleaner::ProtectedKeys {

    // Critical system keys that should NEVER be modified or deleted
    inline const std::vector<String> CRITICAL_KEYS = {
        // System core
        L"HKEY_LOCAL_MACHINE\\SYSTEM",
        L"HKEY_LOCAL_MACHINE\\SECURITY",
        L"HKEY_LOCAL_MACHINE\\SAM",
        L"HKEY_LOCAL_MACHINE\\HARDWARE",
        L"HKEY_LOCAL_MACHINE\\BCD00000000",
        
        // Windows core
        L"HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion",
        L"HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run",
        L"HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\RunOnce",
        L"HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies",
        L"HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Shell Folders",
        L"HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\User Shell Folders",
        
        // Security
        L"HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Cryptography",
        L"HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows Defender",
        L"HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies",
        
        // User core
        L"HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run",
        L"HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\RunOnce",
        L"HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Shell Folders",
        L"HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\User Shell Folders",
        
        // Classes root essentials
        L"HKEY_CLASSES_ROOT\\.exe",
        L"HKEY_CLASSES_ROOT\\.dll",
        L"HKEY_CLASSES_ROOT\\.bat",
        L"HKEY_CLASSES_ROOT\\.cmd",
        L"HKEY_CLASSES_ROOT\\.com",
        L"HKEY_CLASSES_ROOT\\.lnk",
        L"HKEY_CLASSES_ROOT\\.msi",
        L"HKEY_CLASSES_ROOT\\exefile",
        L"HKEY_CLASSES_ROOT\\dllfile",
        L"HKEY_CLASSES_ROOT\\batfile",
        L"HKEY_CLASSES_ROOT\\cmdfile",
    };

    // Protected value names that should not be deleted
    inline const std::vector<String> PROTECTED_VALUES = {
        L"(Default)",
        L"@",
        L"Path",
        L"InstallPath",
        L"ProgramFilesDir",
        L"CommonFilesDir",
        L"SystemRoot",
        L"windir",
    };

    // Keywords indicating critical entries (case-insensitive check)
    inline const std::vector<String> CRITICAL_KEYWORDS = {
        L"Microsoft",
        L"Windows",
        L"System32",
        L"SysWOW64",
        L"WinSxS",
        L"Trusted",
        L"Security",
        L"Policy",
        L"Crypto",
        L"Driver",
        L"Service",
    };

    // Check if a key path is protected
    [[nodiscard]] inline bool IsProtectedKey(StringView keyPath) {
        String keyPathUpper{ keyPath };
        ranges::transform(keyPathUpper, keyPathUpper.begin(), ::towupper);
        
        for (const auto& protectedKey : CRITICAL_KEYS) {
            String protectedUpper = protectedKey;
            ranges::transform(protectedUpper, protectedUpper.begin(), ::towupper);
            
            if (keyPathUpper.starts_with(protectedUpper)) {
                return true;
            }
        }
        return false;
    }

    // Check if a value name is protected
    [[nodiscard]] inline bool IsProtectedValue(StringView valueName) {
        String valueUpper{ valueName };
        ranges::transform(valueUpper, valueUpper.begin(), ::towupper);
        
        for (const auto& protectedValue : PROTECTED_VALUES) {
            String protectedUpper = protectedValue;
            ranges::transform(protectedUpper, protectedUpper.begin(), ::towupper);
            
            if (valueUpper == protectedUpper) {
                return true;
            }
        }
        return false;
    }

    // Check if path contains critical keywords
    [[nodiscard]] inline bool ContainsCriticalKeyword(StringView path) {
        String pathUpper{ path };
        ranges::transform(pathUpper, pathUpper.begin(), ::towupper);
        
        for (const auto& keyword : CRITICAL_KEYWORDS) {
            String keywordUpper = keyword;
            ranges::transform(keywordUpper, keywordUpper.begin(), ::towupper);
            
            if (pathUpper.find(keywordUpper) != String::npos) {
                return true;
            }
        }
        return false;
    }

} // namespace RegistryCleaner::ProtectedKeys
