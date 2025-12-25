// ActiveXScanner.h - Scanner for orphaned ActiveX/COM components
#pragma once

#include "pch.h"
#include "scanners/BaseScanner.h"
#include "registry/RegistryKey.h"

namespace RegistryCleaner::Scanners {

    using namespace Registry;

    class ActiveXScanner : public BaseScanner {
    public:
        ActiveXScanner() : BaseScanner(Config::IssueCategory::ActiveX, L"Composants ActiveX/COM") {}

        std::vector<RegistryIssue> Scan(const ProgressCallback& progress) override {
            std::vector<RegistryIssue> issues;
            ScanCLSID(issues, progress);
            ScanTypeLib(issues, progress);
            return issues;
        }

    private:
        void ScanCLSID(std::vector<RegistryIssue>& issues, const ProgressCallback& progress) {
            const String clsidPath = L"CLSID";
            auto keyResult = RegistryKey::Open(RootKey::ClassesRoot, clsidPath, KEY_READ);
            if (!keyResult) return;

            auto subKeysResult = keyResult->EnumerateSubKeys();
            if (!subKeysResult) return;

            for (const auto& clsid : *subKeysResult) {
                if (progress) progress(clsidPath + L"\\" + clsid, issues.size());
                String subKeyPath = clsidPath + L"\\" + clsid;
                CheckServerPath(subKeyPath + L"\\InprocServer32", issues);
                CheckServerPath(subKeyPath + L"\\LocalServer32", issues);
            }
        }

        void CheckServerPath(const String& keyPath, std::vector<RegistryIssue>& issues) {
            auto keyResult = RegistryKey::Open(RootKey::ClassesRoot, keyPath, KEY_READ);
            if (!keyResult) return;

            auto valueResult = keyResult->GetValue(L"");
            if (!valueResult) return;

            String path = valueResult->AsString();
            if (path.empty()) return;

            String filePath = ExtractFilePath(path);
            if (!filePath.empty() && !fs::exists(filePath)) {
                issues.push_back(CreateIssue(
                    L"HKCR\\" + keyPath, L"",
                    std::format(L"Serveur COM introuvable: {}", filePath),
                    L"", Config::Severity::Medium, false
                ));
            }
        }

        void ScanTypeLib(std::vector<RegistryIssue>& issues, const ProgressCallback& progress) {
            const String typelibPath = L"TypeLib";
            auto keyResult = RegistryKey::Open(RootKey::ClassesRoot, typelibPath, KEY_READ);
            if (!keyResult) return;

            auto subKeysResult = keyResult->EnumerateSubKeys();
            if (!subKeysResult) return;

            for (const auto& typelib : *subKeysResult) {
                if (progress) progress(typelibPath + L"\\" + typelib, issues.size());
                String libPath = typelibPath + L"\\" + typelib;
                auto libKey = RegistryKey::Open(RootKey::ClassesRoot, libPath, KEY_READ);
                if (!libKey) continue;

                auto versions = libKey->EnumerateSubKeys();
                if (!versions) continue;

                for (const auto& version : *versions) {
                    CheckTypeLibPath(libPath + L"\\" + version, issues);
                }
            }
        }

        void CheckTypeLibPath(const String& versionPath, std::vector<RegistryIssue>& issues) {
            for (const auto& platform : {L"0\\win32", L"0\\win64"}) {
                String fullPath = versionPath + L"\\" + platform;
                auto keyResult = RegistryKey::Open(RootKey::ClassesRoot, fullPath, KEY_READ);
                if (!keyResult) continue;

                auto valueResult = keyResult->GetValue(L"");
                if (!valueResult) continue;

                String path = ExtractFilePath(valueResult->AsString());
                if (!path.empty() && !fs::exists(path)) {
                    issues.push_back(CreateIssue(
                        L"HKCR\\" + fullPath, L"",
                        std::format(L"TypeLib introuvable: {}", path),
                        L"", Config::Severity::Low, false
                    ));
                }
            }
        }

        String ExtractFilePath(const String& value) {
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
