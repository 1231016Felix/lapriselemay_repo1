#pragma once

#include "DriverInfo.h"
#include <Windows.h>
#include <SetupAPI.h>
#include <devguid.h>
#include <cfgmgr32.h>
#include <functional>
#include <memory>
#include <mutex>
#include <atomic>

#pragma comment(lib, "setupapi.lib")
#pragma comment(lib, "cfgmgr32.lib")

namespace DriverManager {

    class DriverScanner {
    public:
        DriverScanner();
        ~DriverScanner();

        // Scan all drivers
        void ScanAllDrivers();
        
        // Scan specific category
        void ScanCategory(DriverType type);
        
        // Get all categories with their drivers
        const std::vector<DriverCategory>& GetCategories() const { return m_categories; }
        
        // Get all drivers flat list
        std::vector<DriverInfo> GetAllDrivers() const;
        
        // Get drivers with problems
        std::vector<DriverInfo> GetProblematicDrivers() const;
        
        // Get driver count
        size_t GetTotalDriverCount() const;
        size_t GetProblematicDriverCount() const;
        
        // Driver operations
        bool EnableDriver(const DriverInfo& driver);
        bool DisableDriver(const DriverInfo& driver);
        bool UninstallDriver(const DriverInfo& driver);
        bool UpdateDriver(const DriverInfo& driver);
        
        // Export driver info
        bool ExportToFile(const std::wstring& filePath) const;
        
        // Backup driver
        bool BackupDriver(const DriverInfo& driver, const std::wstring& backupPath);
        
        // Progress callback
        using ProgressCallback = std::function<void(int current, int total, const std::wstring& currentDevice)>;
        void SetProgressCallback(ProgressCallback callback) { m_progressCallback = callback; }
        
        // Check if scanning
        bool IsScanning() const { return m_isScanning; }
        
        // Cancel scan
        void CancelScan() { m_cancelRequested = true; }

    private:
        // Internal scanning methods
        void ScanDeviceClass(const GUID& classGuid, DriverType type);
        DriverInfo GetDriverInfo(HDEVINFO deviceInfoSet, SP_DEVINFO_DATA& deviceInfoData);
        DriverStatus DetermineDriverStatus(HDEVINFO deviceInfoSet, SP_DEVINFO_DATA& deviceInfoData);
        DriverType ClassifyDriverType(const std::wstring& classGuid);
        
        // Helper to get device registry property
        std::wstring GetDeviceRegistryProperty(HDEVINFO deviceInfoSet, SP_DEVINFO_DATA& deviceInfoData, DWORD property);
        
        // Category management
        DriverCategory& GetOrCreateCategory(DriverType type);
        void ClearCategories();
        
        // Data
        std::vector<DriverCategory> m_categories;
        ProgressCallback m_progressCallback;
        std::atomic<bool> m_isScanning{false};
        std::atomic<bool> m_cancelRequested{false};
        mutable std::mutex m_mutex;
        
        // Known class GUIDs
        static const std::vector<std::pair<GUID, DriverType>> s_classGuids;
    };

} // namespace DriverManager
