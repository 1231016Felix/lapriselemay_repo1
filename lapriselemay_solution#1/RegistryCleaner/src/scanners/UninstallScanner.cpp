// UninstallScanner.cpp - Orphaned uninstall entries scanner
#include "pch.h"
#include "scanners/UninstallScanner.h"
#include "registry/RegistryUtils.h"
#include "core/ProtectedKeys.h"

namespace RegistryCleaner::Scanners {

    using namespace Registry;
    using namespace Registry::Utils;

    // Uninstall registry paths to scan
    constexpr std::array UNINSTALL_PATHS = {
        std::pair{ RootKey::LocalMachine, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall" },
        std::pair{ RootKey::LocalMachine, L"SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall" },
        std::pair{ RootKey::CurrentUser, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall" },
    };

    UninstallScanner::UninstallScanner()
        : BaseScanner(IssueCategory::UninstallEntry, L"Entrées de désinstallation orphelines") {}

    std::vector<RegistryIssue> UninstallScanner::Scan(const ProgressCallback& progress) {
        std::vector<RegistryIssue> issues;

        for (const auto& [root, path] : UNINSTALL_PATHS) {
            auto keyResult = RegistryKey::Open(root, path, KEY_READ);
            if (!keyResult) continue;

            auto& parentKey = *keyResult;
            auto subKeysResult = parentKey.EnumerateSubKeys();
            if (!subKeysResult) continue;

            for (const auto& subKeyName : *subKeysResult) {
                String fullPath = std::format(L"{}\\{}\\{}", ToString(root), path, subKeyName);
                ReportProgress(progress, fullPath, issues.size());

                // Skip protected keys
                if (ProtectedKeys::IsProtectedKey(fullPath)) continue;

                auto subKeyResult = RegistryKey::Open(
                    parentKey.Handle(), subKeyName, parentKey.Path(), KEY_READ);
                
                if (!subKeyResult) continue;

                auto issue = CheckUninstallKey(*subKeyResult, fullPath);
                if (issue) {
                    issues.push_back(std::move(*issue));
                }
            }
        }

        return issues;
    }

    bool UninstallScanner::IsValidUninstallEntry(const RegistryKey& key) const {
        // Check for required values
        auto displayName = key.GetValue(L"DisplayName");
        if (!displayName) return false;

        // Check if uninstall command exists and points to valid file
        auto uninstallString = key.GetValue(L"UninstallString");
        if (uninstallString && uninstallString->IsString()) {
            auto filePath = ExtractFilePath(uninstallString->AsString());
            if (filePath && FileExists(*filePath)) {
                return true;
            }
        }

        // Check QuietUninstallString as alternative
        auto quietUninstall = key.GetValue(L"QuietUninstallString");
        if (quietUninstall && quietUninstall->IsString()) {
            auto filePath = ExtractFilePath(quietUninstall->AsString());
            if (filePath && FileExists(*filePath)) {
                return true;
            }
        }

        // Check InstallLocation
        auto installLocation = key.GetValue(L"InstallLocation");
        if (installLocation && installLocation->IsString()) {
            if (DirectoryExists(installLocation->AsString())) {
                return true;
            }
        }

        return false;
    }

    std::optional<RegistryIssue> UninstallScanner::CheckUninstallKey(
        const RegistryKey& key,
        StringView keyPath
    ) const {
        // Skip system components (usually safe)
        auto systemComponent = key.GetValue(L"SystemComponent");
        if (systemComponent && systemComponent->IsDWord() && systemComponent->AsDWord() == 1) {
            return std::nullopt;
        }

        // Skip Windows updates
        auto releaseType = key.GetValue(L"ReleaseType");
        if (releaseType && releaseType->IsString()) {
            const auto& rt = releaseType->AsString();
            if (rt.find(L"Update") != String::npos || rt.find(L"Hotfix") != String::npos) {
                return std::nullopt;
            }
        }

        if (!IsValidUninstallEntry(key)) {
            auto displayName = key.GetValue(L"DisplayName");
            String name = displayName ? displayName->ToString() : L"(sans nom)";
            
            return CreateIssue(
                keyPath,
                L"",
                std::format(L"Programme désinstallé: {}", name),
                L"L'entrée de désinstallation pointe vers des fichiers inexistants",
                Severity::Medium,
                false
            );
        }

        return std::nullopt;
    }

} // namespace RegistryCleaner::Scanners
