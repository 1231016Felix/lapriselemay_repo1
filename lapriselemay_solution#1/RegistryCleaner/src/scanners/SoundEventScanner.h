// SoundEventScanner.h - Scanner for invalid sound event references
#pragma once
#include "pch.h"
#include "scanners/BaseScanner.h"
#include "registry/RegistryKey.h"

namespace RegistryCleaner::Scanners {
    using namespace Registry;

    class SoundEventScanner : public BaseScanner {
    public:
        SoundEventScanner() : BaseScanner(Config::IssueCategory::Sounds, L"Sons et evenements") {}

        std::vector<RegistryIssue> Scan(const ProgressCallback& progress) override {
            std::vector<RegistryIssue> issues;
            ScanAppEvents(issues, progress);
            return issues;
        }

    private:
        void ScanAppEvents(std::vector<RegistryIssue>& issues, const ProgressCallback& progress) {
            const String eventsPath = L"AppEvents\\Schemes\\Apps";
            auto keyResult = RegistryKey::Open(RootKey::CurrentUser, eventsPath, KEY_READ);
            if (!keyResult) return;

            auto appsResult = keyResult->EnumerateSubKeys();
            if (!appsResult) return;

            for (const auto& app : *appsResult) {
                auto appKey = RegistryKey::Open(RootKey::CurrentUser, eventsPath + L"\\" + app, KEY_READ);
                if (!appKey) continue;

                auto eventsResult = appKey->EnumerateSubKeys();
                if (!eventsResult) continue;

                for (const auto& event : *eventsResult) {
                    String eventPath = eventsPath + L"\\" + app + L"\\" + event + L"\\.Current";
                    if (progress) progress(eventPath, issues.size());

                    auto eventKey = RegistryKey::Open(RootKey::CurrentUser, eventPath, KEY_READ);
                    if (!eventKey) continue;

                    auto valueResult = eventKey->GetValue(L"");
                    if (!valueResult) continue;

                    String soundFile = valueResult->AsString();
                    if (soundFile.empty()) continue;

                    wchar_t expanded[MAX_PATH];
                    if (ExpandEnvironmentStringsW(soundFile.c_str(), expanded, MAX_PATH)) soundFile = expanded;

                    if (!fs::exists(soundFile)) {
                        issues.push_back(CreateIssue(
                            L"HKCU\\" + eventPath, L"",
                            std::format(L"Son introuvable: {} - {}", app, event),
                            L"", Config::Severity::Low, false
                        ));
                    }
                }
            }
        }
    };
} // namespace RegistryCleaner::Scanners
