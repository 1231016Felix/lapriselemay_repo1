// FontScanner.h - Scanner for invalid font references
#pragma once
#include "pch.h"
#include "scanners/BaseScanner.h"
#include "registry/RegistryKey.h"

namespace RegistryCleaner::Scanners {
    using namespace Registry;

    class FontScanner : public BaseScanner {
    public:
        FontScanner() : BaseScanner(Config::IssueCategory::Fonts, L"Polices de caracteres") {}

        std::vector<RegistryIssue> Scan(const ProgressCallback& progress) override {
            std::vector<RegistryIssue> issues;
            ScanFonts(issues, progress);
            return issues;
        }

    private:
        void ScanFonts(std::vector<RegistryIssue>& issues, const ProgressCallback& progress) {
            const String fontsPath = L"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Fonts";
            auto keyResult = RegistryKey::Open(RootKey::LocalMachine, fontsPath, KEY_READ);
            if (!keyResult) return;

            wchar_t winDir[MAX_PATH];
            GetWindowsDirectoryW(winDir, MAX_PATH);
            String fontsDir = String(winDir) + L"\\Fonts\\";

            auto valuesResult = keyResult->EnumerateValues();
            if (!valuesResult) return;

            for (const auto& value : *valuesResult) {
                if (progress) progress(fontsPath, issues.size());
                String fontFile = value.AsString();
                if (fontFile.empty()) continue;

                String fullPath;
                if (fontFile.find(L':') == String::npos && fontFile.find(L'\\') == String::npos) {
                    fullPath = fontsDir + fontFile;
                } else {
                    fullPath = fontFile;
                    wchar_t expanded[MAX_PATH];
                    if (ExpandEnvironmentStringsW(fullPath.c_str(), expanded, MAX_PATH)) fullPath = expanded;
                }

                if (!fs::exists(fullPath)) {
                    issues.push_back(CreateIssue(
                        L"HKLM\\" + fontsPath, value.Name(),
                        std::format(L"Police introuvable: {}", value.Name()),
                        L"", Config::Severity::Low, true
                    ));
                }
            }
        }
    };
} // namespace RegistryCleaner::Scanners
