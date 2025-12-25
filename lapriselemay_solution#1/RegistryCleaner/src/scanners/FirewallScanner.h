// FirewallScanner.h - Scanner for invalid firewall application references
#pragma once
#include "pch.h"
#include "scanners/BaseScanner.h"
#include "registry/RegistryKey.h"

namespace RegistryCleaner::Scanners {
    using namespace Registry;

    class FirewallScanner : public BaseScanner {
    public:
        FirewallScanner() : BaseScanner(Config::IssueCategory::Firewall, L"Parametres du pare-feu") {}

        std::vector<RegistryIssue> Scan(const ProgressCallback& progress) override {
            std::vector<RegistryIssue> issues;
            ScanFirewallRules(issues, progress);
            return issues;
        }

    private:
        void ScanFirewallRules(std::vector<RegistryIssue>& issues, const ProgressCallback& progress) {
            const String rulesPath = L"SYSTEM\\CurrentControlSet\\Services\\SharedAccess\\Parameters\\FirewallPolicy\\FirewallRules";
            auto keyResult = RegistryKey::Open(RootKey::LocalMachine, rulesPath, KEY_READ);
            if (!keyResult) return;

            auto valuesResult = keyResult->EnumerateValues();
            if (!valuesResult) return;

            for (const auto& value : *valuesResult) {
                if (progress) progress(rulesPath, issues.size());
                String ruleData = value.AsString();
                String appPath = ExtractAppPath(ruleData);
                if (appPath.empty()) continue;

                wchar_t expanded[MAX_PATH];
                if (ExpandEnvironmentStringsW(appPath.c_str(), expanded, MAX_PATH)) appPath = expanded;

                if (!fs::exists(appPath)) {
                    issues.push_back(CreateIssue(
                        L"HKLM\\" + rulesPath, value.Name(),
                        std::format(L"Regle pare-feu app introuvable: {}", fs::path(appPath).filename().wstring()),
                        L"", Config::Severity::Low, true
                    ));
                }
            }
        }

        String ExtractAppPath(const String& ruleData) {
            size_t appPos = ruleData.find(L"App=");
            if (appPos == String::npos) return L"";
            size_t start = appPos + 4;
            size_t end = ruleData.find(L'|', start);
            return (end == String::npos) ? ruleData.substr(start) : ruleData.substr(start, end - start);
        }
    };
} // namespace RegistryCleaner::Scanners
