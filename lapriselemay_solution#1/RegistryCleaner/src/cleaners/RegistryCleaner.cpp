// RegistryCleaner.cpp - Main Registry Cleaner Implementation
#include "pch.h"
#include "cleaners/RegistryCleaner.h"
#include "registry/RegistryUtils.h"
#include "core/ProtectedKeys.h"

namespace RegistryCleaner::Cleaners {

    using namespace Registry;
    using namespace Registry::Utils;

    RegistryCleaner::RegistryCleaner() {
        [[maybe_unused]] auto result = m_backupManager.Initialize();
    }

    void RegistryCleaner::AddScanner(std::unique_ptr<BaseScanner> scanner) {
        if (scanner) {
            m_scanners.push_back(std::move(scanner));
        }
    }

    void RegistryCleaner::SetScannerEnabled(StringView name, bool enabled) {
        for (auto& scanner : m_scanners) {
            if (scanner->Name() == name) {
                scanner->SetEnabled(enabled);
                break;
            }
        }
    }

    std::vector<RegistryIssue> RegistryCleaner::Scan(const ScanProgressCallback& progress) {
        std::vector<RegistryIssue> allIssues;
        m_stats = CleaningStats{};

        auto startTime = chrono::steady_clock::now();

        for (const auto& scanner : m_scanners) {
            if (!scanner->IsEnabled()) continue;

            auto scannerProgress = [&](StringView key, size_t found) {
                if (progress) {
                    progress(scanner->Name(), key, found);
                }
            };

            auto issues = scanner->Scan(scannerProgress);
            
            m_stats.issuesFound += issues.size();
            allIssues.insert(allIssues.end(),
                std::make_move_iterator(issues.begin()),
                std::make_move_iterator(issues.end()));
        }

        auto endTime = chrono::steady_clock::now();
        m_stats.scanDuration = chrono::duration_cast<chrono::milliseconds>(endTime - startTime);

        return allIssues;
    }

    CleaningStats RegistryCleaner::Clean(
        const std::vector<RegistryIssue>& issues,
        bool createBackup,
        const CleanProgressCallback& progress
    ) {
        CleaningStats stats;
        stats.issuesFound = issues.size();

        if (issues.empty()) return stats;

        auto startTime = chrono::steady_clock::now();

        // Create backup before cleaning
        if (createBackup) {
            auto backupResult = m_backupManager.CreateBackup(issues, L"Pre-nettoyage");
            if (!backupResult) {
                // Backup failed, but we can continue (user was warned)
            }
        }

        size_t current = 0;
        for (const auto& issue : issues) {
            ++current;
            
            if (progress) {
                progress(current, issues.size(), issue);
            }

            // Double-check protected keys
            if (ProtectedKeys::IsProtectedKey(issue.keyPath)) {
                ++stats.issuesSkipped;
                continue;
            }

            // Critical severity items require explicit confirmation
            if (issue.severity == Config::Severity::Critical) {
                ++stats.issuesSkipped;
                continue;
            }

            if (DeleteRegistryItem(issue)) {
                ++stats.issuesCleaned;
            } else {
                ++stats.issuesFailed;
            }
        }

        auto endTime = chrono::steady_clock::now();
        stats.cleanDuration = chrono::duration_cast<chrono::milliseconds>(endTime - startTime);

        // Update global stats
        m_stats.issuesCleaned += stats.issuesCleaned;
        m_stats.issuesFailed += stats.issuesFailed;
        m_stats.issuesSkipped += stats.issuesSkipped;
        m_stats.cleanDuration += stats.cleanDuration;

        // Cleanup old backups
        m_backupManager.CleanupOldBackups(Config::MAX_BACKUP_FILES);

        return stats;
    }

    bool RegistryCleaner::DeleteRegistryItem(const RegistryIssue& issue) {
        auto [rootOpt, subKey] = SplitKeyPath(issue.keyPath);
        if (!rootOpt) return false;

        if (issue.isValueIssue && !issue.valueName.empty()) {
            // Delete a specific value
            auto keyResult = RegistryKey::Open(*rootOpt, subKey, KEY_SET_VALUE);
            if (!keyResult) return false;

            auto deleteResult = keyResult->DeleteValue(issue.valueName);
            return deleteResult.has_value();
        } else {
            // Delete entire key
            // Find parent key path
            size_t lastSlash = subKey.rfind(L'\\');
            if (lastSlash == String::npos) {
                // Direct child of root - use root to delete
                LSTATUS result = RegDeleteKeyW(ToHKey(*rootOpt), subKey.c_str());
                return result == ERROR_SUCCESS;
            }

            String parentPath = subKey.substr(0, lastSlash);
            String keyName = subKey.substr(lastSlash + 1);

            auto parentResult = RegistryKey::Open(*rootOpt, parentPath, KEY_ALL_ACCESS);
            if (!parentResult) return false;

            // Try to delete the key (will fail if has subkeys)
            auto deleteResult = parentResult->DeleteSubKey(keyName);
            if (deleteResult) return true;

            // If failed, try recursive delete (be very careful!)
            auto treeResult = parentResult->DeleteSubKeyTree(keyName);
            return treeResult.has_value();
        }
    }

} // namespace RegistryCleaner::Cleaners
