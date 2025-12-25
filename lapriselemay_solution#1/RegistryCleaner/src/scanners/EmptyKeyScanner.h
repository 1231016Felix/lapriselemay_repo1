// EmptyKeyScanner.h - Scanner for empty registry keys
#pragma once
#include "pch.h"
#include "scanners/BaseScanner.h"
#include "registry/RegistryKey.h"

namespace RegistryCleaner::Scanners {
    using namespace Registry;

    class EmptyKeyScanner : public BaseScanner {
    public:
        EmptyKeyScanner() : BaseScanner(Config::IssueCategory::EmptyKeys, L"Cles vides") {}

        std::vector<RegistryIssue> Scan(const ProgressCallback& progress) override {
            std::vector<RegistryIssue> issues;
            ScanForEmptyKeys(RootKey::CurrentUser, L"SOFTWARE", issues, progress, 0);
            ScanForEmptyKeys(RootKey::LocalMachine, L"SOFTWARE", issues, progress, 0);
            return issues;
        }

    private:
        static constexpr int MAX_DEPTH = 4;

        void ScanForEmptyKeys(RootKey root, const String& basePath, std::vector<RegistryIssue>& issues, 
                             const ProgressCallback& progress, int depth) {
            if (depth > MAX_DEPTH) return;
            auto keyResult = RegistryKey::Open(root, basePath, KEY_READ);
            if (!keyResult) return;

            auto subKeysResult = keyResult->EnumerateSubKeys();
            if (!subKeysResult) return;

            for (const auto& subKey : *subKeysResult) {
                if (subKey == L"Microsoft" || subKey == L"Windows" || subKey == L"Classes" || 
                    subKey == L"Policies" || subKey == L"Wow6432Node") continue;

                String fullPath = basePath + L"\\" + subKey;
                if (progress) progress(fullPath, issues.size());

                auto subKeyResult = RegistryKey::Open(root, fullPath, KEY_READ);
                if (!subKeyResult) continue;

                auto valueCount = subKeyResult->GetValueCount();
                auto subKeyCount = subKeyResult->GetSubKeyCount();

                if (valueCount && subKeyCount && *valueCount == 0 && *subKeyCount == 0) {
                    issues.push_back(CreateIssue(
                        ToString(root) + L"\\" + fullPath, L"",
                        std::format(L"Cle vide: {}", subKey),
                        L"", Config::Severity::Low, false
                    ));
                } else if (subKeyCount && *subKeyCount > 0) {
                    ScanForEmptyKeys(root, fullPath, issues, progress, depth + 1);
                }
            }
        }
    };
} // namespace RegistryCleaner::Scanners
