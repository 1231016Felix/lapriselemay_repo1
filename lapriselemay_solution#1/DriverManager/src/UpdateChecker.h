#pragma once

#include "DriverInfo.h"
#include <Windows.h>
#include <winhttp.h>
#include <string>
#include <vector>
#include <functional>
#include <mutex>
#include <atomic>
#include <map>
#include <thread>
#include <ctime>

#pragma comment(lib, "winhttp.lib")

namespace DriverManager {

    // Structure for Windows Update Catalog search result
    struct CatalogEntry {
        std::wstring title;
        std::wstring version;
        std::wstring classification;
        std::wstring lastUpdated;
        std::wstring size;
        std::wstring downloadUrl;
        std::wstring updateId;
        std::vector<std::wstring> supportedProducts;
        std::vector<std::wstring> supportedHardwareIds;
    };

    // Update check result for a single driver
    struct UpdateCheckResult {
        std::wstring hardwareId;
        std::wstring currentVersion;
        bool updateAvailable = false;
        std::wstring newVersion;
        std::wstring downloadUrl;
        std::wstring description;
        std::wstring lastError;
    };

    // Cached result for disk persistence
    struct CachedResult {
        time_t timestamp = 0;
        bool hasUpdate = false;
        std::wstring checkedVersion;
    };

    class UpdateChecker {
    public:
        UpdateChecker();
        ~UpdateChecker();

        // Progress callback: (current, total, currentItem)
        using ProgressCallback = std::function<void(int, int, const std::wstring&)>;
        void SetProgressCallback(ProgressCallback callback) { m_progressCallback = callback; }

        // Check for updates for a single driver
        UpdateCheckResult CheckDriverUpdate(const DriverInfo& driver);

        // Check for updates for multiple drivers (async with parallel processing)
        void CheckAllUpdatesAsync(std::vector<DriverInfo>& drivers);
        
        // Check for updates via Windows Update (parallel processing)
        void CheckWindowsUpdate(std::vector<DriverInfo>& drivers);

        // Search Windows Update Catalog for a hardware ID
        std::vector<CatalogEntry> SearchWindowsCatalog(const std::wstring& hardwareId);

        // Get system hardware IDs for Windows Update
        std::vector<std::wstring> GetSystemHardwareIds();

        // Check if update check is in progress
        bool IsChecking() const { return m_isChecking; }

        // Cancel ongoing check
        void CancelCheck() { m_cancelRequested = true; }

        // Get last error message
        std::wstring GetLastError() const { return m_lastError; }

        // Statistics
        int GetTotalChecked() const { return m_totalChecked; }
        int GetUpdatesFound() const { return m_updatesFound; }
        int GetLastCheckUpdatesFound() const { return m_updatesFound; }

        // Cache management
        void ClearCache();

    private:
        // HTTP helper functions (with timeouts and optimizations)
        std::string HttpGet(const std::wstring& url);
        std::string HttpPost(const std::wstring& url, const std::string& data, 
                            const std::wstring& contentType = L"application/x-www-form-urlencoded");
        
        // Parse Windows Update Catalog HTML response
        std::vector<CatalogEntry> ParseCatalogResults(const std::string& html);
        
        // Extract download URL from catalog entry page
        std::wstring GetCatalogDownloadUrl(const std::wstring& updateId);

        // Compare driver versions (returns: -1 if v1<v2, 0 if equal, 1 if v1>v2)
        int CompareVersions(const std::wstring& v1, const std::wstring& v2);

        // Clean hardware ID for search
        std::wstring CleanHardwareIdForSearch(const std::wstring& hardwareId);

        // Disk cache management
        void LoadDiskCache();
        void SaveDiskCache();

        // Member variables
        ProgressCallback m_progressCallback;
        std::atomic<bool> m_isChecking{false};
        std::atomic<bool> m_cancelRequested{false};
        std::wstring m_lastError;
        std::mutex m_mutex;

        std::atomic<int> m_totalChecked{0};
        std::atomic<int> m_updatesFound{0};

        // Memory cache for catalog searches (hardware ID -> results)
        std::map<std::wstring, std::vector<CatalogEntry>> m_searchCache;

        // Disk cache for persistent results (survives between sessions)
        std::map<std::wstring, CachedResult> m_diskCache;
        std::wstring m_cacheDirectory;

        // Download URL cache
        std::map<std::wstring, std::wstring> m_downloadUrlCache;

        // User agent for HTTP requests
        const wchar_t* m_userAgent = L"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
    };

} // namespace DriverManager
