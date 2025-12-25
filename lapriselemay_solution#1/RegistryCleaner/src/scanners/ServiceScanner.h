// ServiceScanner.h - Scanner for invalid Windows service references
#pragma once
#include "pch.h"
#include "scanners/BaseScanner.h"
#include "registry/RegistryKey.h"

namespace RegistryCleaner::Scanners {
    using namespace Registry;

    class ServiceScanner : public BaseScanner {
    public:
        ServiceScanner() : BaseScanner(Config::IssueCategory::Services, L"Services Windows") {}

        std::vector<RegistryIssue> Scan(const ProgressCallback& progress) override {
            std::vector<RegistryIssue> issues;
            ScanServices(issues, progress);
            return issues;
        }

    private:
        void ScanServices(std::vector<RegistryIssue>& issues, const ProgressCallback& progress) {
            const String servicesPath = L"SYSTEM\\CurrentControlSet\\Services";
            auto keyResult = RegistryKey::Open(RootKey::LocalMachine, servicesPath, KEY_READ);
            if (!keyResult) return;

            auto subKeysResult = keyResult->EnumerateSubKeys();
            if (!subKeysResult) return;

            for (const auto& serviceName : *subKeysResult) {
                String fullPath = servicesPath + L"\\" + serviceName;
                if (progress) progress(fullPath, issues.size());

                auto serviceKey = RegistryKey::Open(RootKey::LocalMachine, fullPath, KEY_READ);
                if (!serviceKey) continue;

                auto typeValue = serviceKey->GetValue(L"Type");
                if (!typeValue) continue;

                auto typeDword = typeValue->TryAsDWord();
                if (!typeDword) continue;
                DWORD type = *typeDword;
                if (type == 1 || type == 2 || type == 8) continue; // Skip drivers

                auto imagePathValue = serviceKey->GetValue(L"ImagePath");
                if (!imagePathValue) continue;

                auto imagePathStr = imagePathValue->TryAsString();
                if (!imagePathStr || imagePathStr->empty()) continue;

                String filePath = ExtractServicePath(*imagePathStr);
                if (!filePath.empty() && !fs::exists(filePath)) {
                    auto startValue = serviceKey->GetValue(L"Start");
                    DWORD startType = 0;
                    if (startValue) {
                        auto startDword = startValue->TryAsDWord();
                        if (startDword) startType = *startDword;
                    }
                    if (startType != 4) { // Not disabled
                        issues.push_back(CreateIssue(
                            L"HKLM\\" + fullPath, L"ImagePath",
                            std::format(L"Service introuvable: {}", serviceName),
                            L"", Config::Severity::Medium, true
                        ));
                    }
                }
            }
        }

        String ExtractServicePath(const String& imagePath) {
            String path = imagePath;
            if (path.starts_with(L"\\SystemRoot\\")) {
                wchar_t winDir[MAX_PATH];
                GetWindowsDirectoryW(winDir, MAX_PATH);
                path = String(winDir) + path.substr(11);
            }
            if (!path.empty() && path.front() == L'"') {
                size_t endQuote = path.find(L'"', 1);
                if (endQuote != String::npos) path = path.substr(1, endQuote - 1);
            }
            size_t extPos = path.find(L".exe");
            if (extPos == String::npos) extPos = path.find(L".sys");
            if (extPos != String::npos) path = path.substr(0, extPos + 4);
            wchar_t expanded[MAX_PATH];
            if (ExpandEnvironmentStringsW(path.c_str(), expanded, MAX_PATH)) path = expanded;
            return path;
        }
    };
} // namespace RegistryCleaner::Scanners
