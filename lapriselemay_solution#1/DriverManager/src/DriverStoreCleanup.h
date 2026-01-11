#pragma once

#include <string>
#include <vector>
#include <functional>
#include <windows.h>
#include <cstdint>

namespace DriverManager {

    // Entry for an orphaned driver folder in FileRepository
    struct OrphanedDriverEntry {
        std::wstring folderName;          // Full folder name (e.g., "heci.inf_amd64_abc123")
        std::wstring folderPath;          // Full path to the folder
        std::wstring infName;             // Original INF name (e.g., "heci.inf")
        std::wstring architecture;        // Architecture (amd64, x86, arm64)
        std::wstring driverVersion;       // Version extracted from INF file
        std::wstring driverDate;          // Date extracted from INF file
        std::wstring providerName;        // Provider name from INF
        std::wstring className;           // Device class
        uint64_t folderSize = 0;          // Total size of the folder
        bool isSelected = false;          // Selected for deletion
        bool isCurrentVersion = false;    // True if this is the currently published version
    };

    // Published driver info (from pnputil)
    struct PublishedDriverInfo {
        std::wstring oemInfName;          // OEM name (oem123.inf)
        std::wstring originalInfName;     // Original INF name
        std::wstring driverVersion;       // Version string
        std::wstring driverDate;          // Date string
    };

    class DriverStoreCleanup {
    public:
        DriverStoreCleanup();
        ~DriverStoreCleanup();

        // Scan FileRepository for orphaned drivers
        bool ScanDriverStore();

        // Get all entries (orphans + current versions for comparison)
        std::vector<OrphanedDriverEntry>& GetEntries() { return m_entries; }
        const std::vector<OrphanedDriverEntry>& GetEntries() const { return m_entries; }

        // Get only orphaned entries (old versions that can be deleted)
        std::vector<OrphanedDriverEntry*> GetOrphanedEntries();

        // Delete selected packages
        int DeleteSelectedPackages();

        // Get sizes
        uint64_t GetSelectedSize() const;
        uint64_t GetTotalOrphanedSize() const;

        // Get last error
        std::wstring GetLastError() const { return m_lastError; }

        // Check if scanning
        bool IsScanning() const { return m_isScanning; }

        // Progress callback
        using ProgressCallback = std::function<void(int current, int total, const std::wstring& item)>;
        void SetProgressCallback(ProgressCallback callback) { m_progressCallback = callback; }

    private:
        // Get list of published drivers from pnputil
        std::vector<PublishedDriverInfo> GetPublishedDrivers();

        // Execute pnputil command and get output
        std::string ExecutePnpUtil(const std::wstring& args);

        // Scan FileRepository folder
        bool ScanFileRepository(const std::vector<PublishedDriverInfo>& publishedDrivers);

        // Calculate folder size
        uint64_t CalculateFolderSize(const std::wstring& folderPath);

        // Parse INF file to extract version info
        bool ParseInfFile(const std::wstring& infPath, std::wstring& version, std::wstring& date, 
                          std::wstring& provider, std::wstring& className);

        // Delete a folder recursively
        bool DeleteFolder(const std::wstring& folderPath);

        std::vector<OrphanedDriverEntry> m_entries;
        std::wstring m_lastError;
        bool m_isScanning = false;
        ProgressCallback m_progressCallback;
    };

    // Helper functions (declared in DriverInfo.h but we need them here too)
    std::string WideToUtf8(const std::wstring& wide);

} // namespace DriverManager
