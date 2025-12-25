// SoftwarePathScanner.h - Scanner for invalid software paths
#pragma once

#include "pch.h"
#include "scanners/BaseScanner.h"
#include "registry/RegistryKey.h"

namespace RegistryCleaner::Scanners {

    using namespace Registry;

    class SoftwarePathScanner : public BaseScanner {
    public:
        SoftwarePathScanner() : BaseScanner(Config::IssueCategory::Software, L"Chemins des logiciels") {}

        std::vector<RegistryIssue> Scan(const ProgressCallback& progress) override {
            std::vector<RegistryIssue> issues;
            ScanSoftwareKey(L"SOFTWARE", issues, progress);
            return issues;
        }

    private:
        void ScanSoftwareKey(const String& basePath, std::vector<RegistryIssue>& issues, const ProgressCallback& progress) {
            for (auto root : {RootKey::LocalMachine, RootKey::CurrentUser}) {
                auto keyResult = RegistryKey::Open(root, basePath, KEY_READ);
                if (!keyResult) continue;

                auto subKeysResult = keyResult->EnumerateSubKeys();
                if (!subKeysResult) continue;

                for (const auto& company : *subKeysResult) {
                    if (company == L"Microsoft" || company == L"Windows" || 
                        company == L"Classes" || company == L"Policies" || company == L"Wow6432Node") continue;

                    String companyPath = basePath + L"\\" + company;
                    if (progress) progress(companyPath, issues.size());
                    ScanCompanyKey(root, companyPath, issues);
                }
            }
        }

        void ScanCompanyKey(RootKey root, const String& companyPath, std::vector<RegistryIssue>& issues) {
            auto keyResult = RegistryKey::Open(root, companyPath, KEY_READ);
            if (!keyResult) return;

            auto subKeysResult = keyResult->EnumerateSubKeys();
            if (!subKeysResult) return;

            for (const auto& product : *subKeysResult) {
                String productPath = companyPath + L"\\" + product;
                auto productKey = RegistryKey::Open(root, productPath, KEY_READ);
                if (!productKey) continue;

                CheckPathValue(root, productPath, L"InstallPath", *productKey, issues);
                CheckPathValue(root, productPath, L"InstallLocation", *productKey, issues);
            }
        }

        void CheckPathValue(RootKey root, const String& keyPath, const String& valueName,
                           RegistryKey& key, std::vector<RegistryIssue>& issues) {
            auto valueResult = key.GetValue(valueName);
            if (!valueResult) return;

            String path = valueResult->AsString();
            if (path.empty()) return;

            wchar_t expanded[MAX_PATH];
            if (ExpandEnvironmentStringsW(path.c_str(), expanded, MAX_PATH)) path = expanded;

            if (!fs::exists(path)) {
                size_t lastSlash = keyPath.rfind(L'\\');
                String name = (lastSlash != String::npos) ? keyPath.substr(lastSlash + 1) : keyPath;
                issues.push_back(CreateIssue(
                    ToString(root) + L"\\" + keyPath, valueName,
                    std::format(L"Chemin logiciel invalide: {}", name),
                    L"", Config::Severity::Low, true
                ));
            }
        }
    };

} // namespace RegistryCleaner::Scanners
