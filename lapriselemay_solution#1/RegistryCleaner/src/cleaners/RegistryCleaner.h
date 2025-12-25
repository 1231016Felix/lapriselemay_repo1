// RegistryCleaner.h - Main Registry Cleaner Class
#pragma once

#include "pch.h"
#include "scanners/BaseScanner.h"
#include "backup/BackupManager.h"

namespace RegistryCleaner::Cleaners {

    using namespace Scanners;
    using namespace Backup;

    // Statistics for cleaning operations
    struct CleaningStats {
        size_t totalScanned = 0;
        size_t issuesFound = 0;
        size_t issuesCleaned = 0;
        size_t issuesFailed = 0;
        size_t issuesSkipped = 0;
        size_t forcedDeletes = 0;       // Items deleted with force mode
        size_t scheduledForReboot = 0;  // Items scheduled for deletion at reboot
        chrono::milliseconds scanDuration{ 0 };
        chrono::milliseconds cleanDuration{ 0 };
        std::vector<String> failedItems;  // Store failed item paths for debugging
    };

    // Callback types
    using ScanProgressCallback = std::function<void(StringView scanner, StringView key, size_t found)>;
    using CleanProgressCallback = std::function<void(size_t current, size_t total, const RegistryIssue& issue)>;

    class RegistryCleaner {
    public:
        RegistryCleaner();
        ~RegistryCleaner() = default;

        // Add a scanner
        void AddScanner(std::unique_ptr<BaseScanner> scanner);

        // Enable/disable specific scanner
        void SetScannerEnabled(StringView name, bool enabled);

        // Run all enabled scanners
        [[nodiscard]] std::vector<RegistryIssue> Scan(
            const ScanProgressCallback& progress = nullptr);

        // Clean specific issues (with backup)
        // forceDelete: If true, take ownership and force delete protected keys
        [[nodiscard]] CleaningStats Clean(
            const std::vector<RegistryIssue>& issues,
            bool createBackup = true,
            const CleanProgressCallback& progress = nullptr,
            bool forceDelete = false);

        // Get statistics
        [[nodiscard]] const CleaningStats& GetStats() const { return m_stats; }

        // Get backup manager
        [[nodiscard]] BackupManager& GetBackupManager() { return m_backupManager; }

        // Get all registered scanners
        [[nodiscard]] const std::vector<std::unique_ptr<BaseScanner>>& GetScanners() const { 
            return m_scanners; 
        }

    private:
        // Normal deletion attempt
        [[nodiscard]] bool DeleteRegistryItem(const RegistryIssue& issue);
        
        // Force deletion (takes ownership, modifies ACL)
        [[nodiscard]] bool ForceDeleteRegistryItem(const RegistryIssue& issue);
        
        // Helper to store failed items
        void StoreFailedItem(CleaningStats& stats, const RegistryIssue& issue);

        std::vector<std::unique_ptr<BaseScanner>> m_scanners;
        BackupManager m_backupManager;
        CleaningStats m_stats;
    };

} // namespace RegistryCleaner::Cleaners
