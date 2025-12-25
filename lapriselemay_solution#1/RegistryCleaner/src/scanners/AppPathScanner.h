// AppPathScanner.h - Scanner for invalid application paths
#pragma once

#include "pch.h"
#include "scanners/BaseScanner.h"
#include "registry/RegistryKey.h"

namespace RegistryCleaner::Scanners {

    using namespace Registry;

    class AppPathScanner : public BaseScanner {
    public:
        AppPathScanner() : BaseScanner(Config::IssueCategory::AppPaths, L"Chemins des applications") {}

        std::vector<RegistryIssue> Scan(const ProgressCallback& progress) override {
            std::vector<RegistryIssue> issues;
            ScanAppPaths(issues, progress);
            return issues;
        }

    private:
        void ScanAppPaths(std::vector<RegistryIssue>& issues, const ProgressCallback& progress) {
            const String appPathsKey = L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths";
            
            for (auto root : {RootKey::LocalMachine, RootKey::CurrentUser}) {
                auto keyResult = RegistryKey::Open(root, appPathsKey, KEY_READ);
                if (!keyResult) continue;

                auto subKeysResult = keyResult->EnumerateSubKeys();
                if (!subKeysResult) continue;

                for (const auto& appName : *subKeysResult) {
                    String fullPath = appPathsKey + L"\\" + appName;
                    if (progress) progress(fullPath, issues.size());

                    auto appKey = RegistryKey::Open(root, fullPath, KEY_READ);
                    if (!appKey) continue;

                    auto valueResult = appKey->GetValue(L"");
                    if (valueResult) {
                        String appPath = ExtractPath(valueResult->AsString());
                        if (!appPath.empty() && !fs::exists(appPath)) {
                            issues.push_back(CreateIssue(
                                ToString(root) + L"\\" + fullPath, L"",
                                std::format(L"Application introuvable: {}", appName),
                                L"", Config::Severity::Medium, false
                            ));
                        }
                    }
                }
            }
        }

        String ExtractPath(const String& value) {
            String path = value;
            if (!path.empty() && path.front() == L'"') {
                size_t endQuote = path.find(L'"', 1);
                if (endQuote != String::npos) path = path.substr(1, endQuote - 1);
            }
            wchar_t expanded[MAX_PATH];
            if (ExpandEnvironmentStringsW(path.c_str(), expanded, MAX_PATH)) path = expanded;
            return path;
        }
    };

} // namespace RegistryCleaner::Scanners
