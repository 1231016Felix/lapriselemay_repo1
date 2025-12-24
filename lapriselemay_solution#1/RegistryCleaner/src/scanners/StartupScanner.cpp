// StartupScanner.cpp - Invalid startup entries scanner
#include "pch.h"
#include "scanners/StartupScanner.h"
#include "registry/RegistryUtils.h"
#include "core/ProtectedKeys.h"

namespace RegistryCleaner::Scanners {

    using namespace Registry;
    using namespace Registry::Utils;

    // Startup registry paths
    const std::vector<std::pair<RootKey, StringView>> STARTUP_PATHS = {
        { RootKey::CurrentUser, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run" },
        { RootKey::CurrentUser, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\RunOnce" },
        { RootKey::LocalMachine, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run" },
        { RootKey::LocalMachine, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\RunOnce" },
        { RootKey::LocalMachine, L"SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Run" },
        { RootKey::LocalMachine, L"SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\RunOnce" },
    };

    StartupScanner::StartupScanner()
        : BaseScanner(IssueCategory::StartupEntry, L"Programmes au démarrage invalides") {}

    std::vector<RegistryIssue> StartupScanner::Scan(const ProgressCallback& progress) {
        std::vector<RegistryIssue> issues;

        for (const auto& [root, path] : STARTUP_PATHS) {
            ScanStartupPath(root, path, issues, progress);
        }

        return issues;
    }

    void StartupScanner::ScanStartupPath(
        RootKey root,
        StringView path,
        std::vector<RegistryIssue>& issues,
        const ProgressCallback& progress
    ) const {
        String fullPath = std::format(L"{}\\{}", ToString(root), path);
        ReportProgress(progress, fullPath, issues.size());

        auto keyResult = RegistryKey::Open(root, path, KEY_READ);
        if (!keyResult) return;

        auto& key = *keyResult;
        auto valuesResult = key.EnumerateValues();
        if (!valuesResult) return;

        for (const auto& value : *valuesResult) {
            if (!value.IsString()) continue;

            const String& commandLine = value.AsString();
            if (commandLine.empty()) continue;

            // Skip protected values
            if (ProtectedKeys::IsProtectedValue(value.Name())) continue;

            // Extract and check the file path
            auto filePath = ExtractFilePath(commandLine);
            if (!filePath) continue;

            // Check if contains critical keywords (likely system)
            if (ProtectedKeys::ContainsCriticalKeyword(*filePath)) continue;

            if (!FileExists(*filePath)) {
                issues.push_back(CreateIssue(
                    fullPath,
                    value.Name(),
                    std::format(L"Programme au démarrage introuvable: {}", value.Name()),
                    std::format(L"Chemin: {}", *filePath),
                    Severity::Medium,
                    true
                ));
            }
        }
    }

} // namespace RegistryCleaner::Scanners
