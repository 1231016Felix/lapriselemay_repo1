// StartMenuScanner.h - Scanner for invalid Start Menu entries
#pragma once
#include "pch.h"
#include "scanners/BaseScanner.h"
#include "registry/RegistryKey.h"

namespace RegistryCleaner::Scanners {
    using namespace Registry;

    class StartMenuScanner : public BaseScanner {
    public:
        StartMenuScanner() : BaseScanner(Config::IssueCategory::StartMenu, L"Menu Demarrer") {}

        std::vector<RegistryIssue> Scan(const ProgressCallback& progress) override {
            std::vector<RegistryIssue> issues;
            ScanRecentDocs(issues, progress);
            return issues;
        }

    private:
        void ScanRecentDocs(std::vector<RegistryIssue>& issues, const ProgressCallback& progress) {
            const String recentPath = L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\RecentDocs";
            auto keyResult = RegistryKey::Open(RootKey::CurrentUser, recentPath, KEY_READ);
            if (!keyResult) return;

            auto subKeysResult = keyResult->EnumerateSubKeys();
            if (!subKeysResult) return;

            for (const auto& ext : *subKeysResult) {
                String extPath = recentPath + L"\\" + ext;
                if (progress) progress(extPath, issues.size());

                auto extKey = RegistryKey::Open(RootKey::CurrentUser, extPath, KEY_READ);
                if (!extKey) continue;

                auto valueCount = extKey->GetValueCount();
                if (valueCount && *valueCount > 50) {
                    issues.push_back(CreateIssue(
                        L"HKCU\\" + extPath, L"",
                        std::format(L"Documents recents ({}): {} entrees", ext, *valueCount),
                        L"", Config::Severity::Low, false
                    ));
                }
            }
        }
    };
} // namespace RegistryCleaner::Scanners
