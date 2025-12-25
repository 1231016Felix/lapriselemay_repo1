// ContextMenuScanner.h - Scanner for invalid context menu entries
#pragma once
#include "pch.h"
#include "scanners/BaseScanner.h"
#include "registry/RegistryKey.h"

namespace RegistryCleaner::Scanners {
    using namespace Registry;

    class ContextMenuScanner : public BaseScanner {
    public:
        ContextMenuScanner() : BaseScanner(Config::IssueCategory::ContextMenu, L"Menu contextuel") {}

        std::vector<RegistryIssue> Scan(const ProgressCallback& progress) override {
            std::vector<RegistryIssue> issues;
            ScanShellExtensions(issues, progress);
            ScanContextMenuHandlers(issues, progress);
            return issues;
        }

    private:
        void ScanShellExtensions(std::vector<RegistryIssue>& issues, const ProgressCallback& progress) {
            const String shellExPath = L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Shell Extensions\\Approved";
            auto keyResult = RegistryKey::Open(RootKey::LocalMachine, shellExPath, KEY_READ);
            if (!keyResult) return;

            auto valuesResult = keyResult->EnumerateValues();
            if (!valuesResult) return;

            for (const auto& value : *valuesResult) {
                String clsid = value.Name();
                if (progress) progress(shellExPath, issues.size());
                if (clsid.empty() || clsid[0] != L'{') continue;

                String clsidPath = L"CLSID\\" + clsid;
                auto clsidKey = RegistryKey::Open(RootKey::ClassesRoot, clsidPath, KEY_READ);
                if (!clsidKey) {
                    auto desc = value.TryAsString();
                    issues.push_back(CreateIssue(
                        L"HKLM\\" + shellExPath, clsid,
                        std::format(L"Extension shell orpheline: {}", desc.value_or(clsid)),
                        L"", Config::Severity::Low, true
                    ));
                }
            }
        }

        void ScanContextMenuHandlers(std::vector<RegistryIssue>& issues, const ProgressCallback& progress) {
            std::vector<String> locations = {
                L"*\\shellex\\ContextMenuHandlers",
                L"Directory\\shellex\\ContextMenuHandlers",
                L"Folder\\shellex\\ContextMenuHandlers"
            };

            for (const auto& location : locations) {
                auto keyResult = RegistryKey::Open(RootKey::ClassesRoot, location, KEY_READ);
                if (!keyResult) continue;

                auto subKeysResult = keyResult->EnumerateSubKeys();
                if (!subKeysResult) continue;

                for (const auto& handler : *subKeysResult) {
                    String handlerPath = location + L"\\" + handler;
                    if (progress) progress(handlerPath, issues.size());

                    auto handlerKey = RegistryKey::Open(RootKey::ClassesRoot, handlerPath, KEY_READ);
                    if (!handlerKey) continue;

                    auto valueResult = handlerKey->GetValue(L"");
                    String clsid = valueResult ? valueResult->TryAsString().value_or(handler) : handler;
                    if (clsid.empty() || clsid[0] != L'{') continue;

                    String clsidPath = L"CLSID\\" + clsid;
                    auto clsidKey = RegistryKey::Open(RootKey::ClassesRoot, clsidPath, KEY_READ);
                    if (!clsidKey) {
                        issues.push_back(CreateIssue(
                            L"HKCR\\" + handlerPath, L"",
                            std::format(L"Handler menu contextuel orphelin: {}", handler),
                            L"", Config::Severity::Medium, false
                        ));
                    }
                }
            }
        }
    };
} // namespace RegistryCleaner::Scanners
