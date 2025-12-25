// ImageExecutionScanner.h - Scanner for IFEO entries
#pragma once
#include "pch.h"
#include "scanners/BaseScanner.h"
#include "registry/RegistryKey.h"

namespace RegistryCleaner::Scanners {
    using namespace Registry;

    class ImageExecutionScanner : public BaseScanner {
    public:
        ImageExecutionScanner() : BaseScanner(Config::IssueCategory::ImageExecution, L"Execution fichiers Image") {}

        std::vector<RegistryIssue> Scan(const ProgressCallback& progress) override {
            std::vector<RegistryIssue> issues;
            ScanIFEO(issues, progress);
            return issues;
        }

    private:
        void ScanIFEO(std::vector<RegistryIssue>& issues, const ProgressCallback& progress) {
            const String ifeoPath = L"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Image File Execution Options";
            auto keyResult = RegistryKey::Open(RootKey::LocalMachine, ifeoPath, KEY_READ);
            if (!keyResult) return;

            auto subKeysResult = keyResult->EnumerateSubKeys();
            if (!subKeysResult) return;

            for (const auto& exeName : *subKeysResult) {
                String fullPath = ifeoPath + L"\\" + exeName;
                if (progress) progress(fullPath, issues.size());

                auto exeKey = RegistryKey::Open(RootKey::LocalMachine, fullPath, KEY_READ);
                if (!exeKey) continue;

                auto debuggerValue = exeKey->GetValue(L"Debugger");
                if (debuggerValue) {
                    String debugger = debuggerValue->AsString();
                    if (!debugger.empty()) {
                        String debuggerPath = ExtractPath(debugger);
                        if (!debuggerPath.empty() && !fs::exists(debuggerPath)) {
                            issues.push_back(CreateIssue(
                                L"HKLM\\" + fullPath, L"Debugger",
                                std::format(L"IFEO Debugger introuvable: {}", exeName),
                                L"", Config::Severity::Medium, true
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
            } else {
                size_t space = path.find(L' ');
                if (space != String::npos) path = path.substr(0, space);
            }
            wchar_t expanded[MAX_PATH];
            if (ExpandEnvironmentStringsW(path.c_str(), expanded, MAX_PATH)) path = expanded;
            return path;
        }
    };
} // namespace RegistryCleaner::Scanners
