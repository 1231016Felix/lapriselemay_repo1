// IEHistoryScanner.h - Scanner for IE history/TypedURLs
#pragma once
#include "pch.h"
#include "scanners/BaseScanner.h"
#include "registry/RegistryKey.h"

namespace RegistryCleaner::Scanners {
    using namespace Registry;

    class IEHistoryScanner : public BaseScanner {
    public:
        IEHistoryScanner() : BaseScanner(Config::IssueCategory::BrowserHistory, L"Historique liens IE") {}

        std::vector<RegistryIssue> Scan(const ProgressCallback& progress) override {
            std::vector<RegistryIssue> issues;
            ScanTypedURLs(issues, progress);
            ScanTypedPaths(issues, progress);
            return issues;
        }

    private:
        void ScanTypedURLs(std::vector<RegistryIssue>& issues, const ProgressCallback& progress) {
            const String typedUrlsPath = L"SOFTWARE\\Microsoft\\Internet Explorer\\TypedURLs";
            auto keyResult = RegistryKey::Open(RootKey::CurrentUser, typedUrlsPath, KEY_READ);
            if (!keyResult) return;

            auto valuesResult = keyResult->EnumerateValues();
            if (!valuesResult) return;

            if (progress) progress(typedUrlsPath, issues.size());
            if (valuesResult->size() > 10) {
                issues.push_back(CreateIssue(
                    L"HKCU\\" + typedUrlsPath, L"",
                    std::format(L"URLs IE saisies: {} entrees", valuesResult->size()),
                    L"", Config::Severity::Low, false
                ));
            }
        }

        void ScanTypedPaths(std::vector<RegistryIssue>& issues, const ProgressCallback& progress) {
            const String typedPathsPath = L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\TypedPaths";
            auto keyResult = RegistryKey::Open(RootKey::CurrentUser, typedPathsPath, KEY_READ);
            if (!keyResult) return;

            auto valuesResult = keyResult->EnumerateValues();
            if (!valuesResult) return;

            if (progress) progress(typedPathsPath, issues.size());
            size_t invalidCount = 0;
            for (const auto& value : *valuesResult) {
                String path = value.AsString();
                if (path.empty() || path.find(L"://") != String::npos) continue;
                wchar_t expanded[MAX_PATH];
                if (ExpandEnvironmentStringsW(path.c_str(), expanded, MAX_PATH)) path = expanded;
                if (!fs::exists(path)) ++invalidCount;
            }

            if (invalidCount > 0) {
                issues.push_back(CreateIssue(
                    L"HKCU\\" + typedPathsPath, L"",
                    std::format(L"Chemins saisis invalides: {} entrees", invalidCount),
                    L"", Config::Severity::Low, false
                ));
            }
        }
    };
} // namespace RegistryCleaner::Scanners
