// MUICacheScanner.h - Scanner for MUI Cache entries
#pragma once
#include "pch.h"
#include "scanners/BaseScanner.h"
#include "registry/RegistryKey.h"

namespace RegistryCleaner::Scanners {
    using namespace Registry;

    class MUICacheScanner : public BaseScanner {
    public:
        MUICacheScanner() : BaseScanner(Config::IssueCategory::MUICache, L"Cache MUI") {}

        std::vector<RegistryIssue> Scan(const ProgressCallback& progress) override {
            std::vector<RegistryIssue> issues;
            ScanMUICache(issues, progress);
            return issues;
        }

    private:
        void ScanMUICache(std::vector<RegistryIssue>& issues, const ProgressCallback& progress) {
            const String muiCachePath = L"SOFTWARE\\Classes\\Local Settings\\Software\\Microsoft\\Windows\\Shell\\MuiCache";
            auto keyResult = RegistryKey::Open(RootKey::CurrentUser, muiCachePath, KEY_READ);
            if (!keyResult) return;

            auto valuesResult = keyResult->EnumerateValues();
            if (!valuesResult) return;

            size_t invalidCount = 0;
            for (const auto& value : *valuesResult) {
                if (progress) progress(muiCachePath, issues.size());
                String valueName = value.Name();
                size_t lastDot = valueName.rfind(L'.');
                if (lastDot == String::npos) continue;

                String possiblePath = valueName.substr(0, lastDot);
                if (possiblePath.length() < 3 || possiblePath.find(L'\\') == String::npos) continue;

                wchar_t expanded[MAX_PATH];
                if (ExpandEnvironmentStringsW(possiblePath.c_str(), expanded, MAX_PATH)) possiblePath = expanded;
                if (!fs::exists(possiblePath)) ++invalidCount;
            }

            if (invalidCount > 5) {
                issues.push_back(CreateIssue(
                    L"HKCU\\" + muiCachePath, L"",
                    std::format(L"Cache MUI orphelin: {} entrees", invalidCount),
                    L"", Config::Severity::Low, false
                ));
            }
        }
    };
} // namespace RegistryCleaner::Scanners
