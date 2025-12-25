// HelpFileScanner.h - Scanner for invalid help file references
#pragma once
#include "pch.h"
#include "scanners/BaseScanner.h"
#include "registry/RegistryKey.h"

namespace RegistryCleaner::Scanners {
    using namespace Registry;

    class HelpFileScanner : public BaseScanner {
    public:
        HelpFileScanner() : BaseScanner(Config::IssueCategory::HelpFiles, L"Fichiers d'aide") {}

        std::vector<RegistryIssue> Scan(const ProgressCallback& progress) override {
            std::vector<RegistryIssue> issues;
            ScanHelpFiles(issues, progress);
            return issues;
        }

    private:
        void ScanHelpFiles(std::vector<RegistryIssue>& issues, const ProgressCallback& progress) {
            const String helpPath = L"SOFTWARE\\Microsoft\\Windows\\Help";
            for (auto root : {RootKey::LocalMachine, RootKey::CurrentUser}) {
                auto keyResult = RegistryKey::Open(root, helpPath, KEY_READ);
                if (!keyResult) continue;

                auto valuesResult = keyResult->EnumerateValues();
                if (!valuesResult) continue;

                for (const auto& value : *valuesResult) {
                    if (progress) progress(helpPath, issues.size());
                    String path = value.AsString();
                    if (path.empty()) continue;

                    wchar_t expanded[MAX_PATH];
                    if (ExpandEnvironmentStringsW(path.c_str(), expanded, MAX_PATH)) path = expanded;

                    if (!fs::exists(path)) {
                        issues.push_back(CreateIssue(
                            ToString(root) + L"\\" + helpPath, value.Name(),
                            std::format(L"Fichier aide introuvable: {}", value.Name()),
                            L"", Config::Severity::Low, true
                        ));
                    }
                }
            }
        }
    };
} // namespace RegistryCleaner::Scanners
