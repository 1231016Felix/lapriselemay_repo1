#include "DriverScanner.h"
#include <shellapi.h>
#include <sstream>
#include <fstream>
#include <iomanip>
#include <algorithm>

namespace DriverManager {

    // Known device class GUIDs
    const std::vector<std::pair<GUID, DriverType>> DriverScanner::s_classGuids = {
        { GUID_DEVCLASS_DISPLAY,        DriverType::Display },
        { GUID_DEVCLASS_MEDIA,          DriverType::Audio },
        { GUID_DEVCLASS_NET,            DriverType::Network },
        { GUID_DEVCLASS_DISKDRIVE,      DriverType::Storage },
        { GUID_DEVCLASS_USB,            DriverType::USB },
        { GUID_DEVCLASS_BLUETOOTH,      DriverType::Bluetooth },
        { GUID_DEVCLASS_PRINTER,        DriverType::Printer },
        { GUID_DEVCLASS_HIDCLASS,       DriverType::HID },
        { GUID_DEVCLASS_SYSTEM,         DriverType::System },
        { GUID_DEVCLASS_KEYBOARD,       DriverType::HID },
        { GUID_DEVCLASS_MOUSE,          DriverType::HID },
        { GUID_DEVCLASS_MONITOR,        DriverType::Display },
        { GUID_DEVCLASS_VOLUME,         DriverType::Storage },
        { GUID_DEVCLASS_HDC,            DriverType::Storage },
    };

    DriverScanner::DriverScanner() {
        // Initialize categories
        m_categories.push_back({ L"Système", DriverType::System, {}, true });
        m_categories.push_back({ L"Affichage", DriverType::Display, {}, true });
        m_categories.push_back({ L"Audio", DriverType::Audio, {}, true });
        m_categories.push_back({ L"Réseau", DriverType::Network, {}, true });
        m_categories.push_back({ L"Stockage", DriverType::Storage, {}, true });
        m_categories.push_back({ L"USB", DriverType::USB, {}, true });
        m_categories.push_back({ L"Bluetooth", DriverType::Bluetooth, {}, true });
        m_categories.push_back({ L"Imprimante", DriverType::Printer, {}, true });
        m_categories.push_back({ L"Périphériques d'entrée", DriverType::HID, {}, true });
        m_categories.push_back({ L"Autre", DriverType::Other, {}, true });
    }

    DriverScanner::~DriverScanner() {
        m_cancelRequested = true;
    }

    void DriverScanner::ClearCategories() {
        std::lock_guard<std::mutex> lock(m_mutex);
        for (auto& cat : m_categories) {
            cat.drivers.clear();
        }
    }

    DriverCategory& DriverScanner::GetOrCreateCategory(DriverType type) {
        for (auto& cat : m_categories) {
            if (cat.type == type) {
                return cat;
            }
        }
        return m_categories.back(); // Return "Other" category
    }

    void DriverScanner::ScanAllDrivers() {
        if (m_isScanning) return;
        
        m_isScanning = true;
        m_cancelRequested = false;
        ClearCategories();

        int totalClasses = static_cast<int>(s_classGuids.size());
        int currentClass = 0;

        for (const auto& [guid, type] : s_classGuids) {
            if (m_cancelRequested) break;
            
            if (m_progressCallback) {
                m_progressCallback(currentClass, totalClasses, L"Scanning...");
            }
            
            ScanDeviceClass(guid, type);
            currentClass++;
        }

        // Also scan all other devices
        ScanDeviceClass(GUID_NULL, DriverType::Other);

        m_isScanning = false;
    }

    void DriverScanner::ScanCategory(DriverType type) {
        if (m_isScanning) return;
        
        m_isScanning = true;
        m_cancelRequested = false;

        // Find the GUID for this type
        for (const auto& [guid, t] : s_classGuids) {
            if (t == type) {
                ScanDeviceClass(guid, type);
                break;
            }
        }

        m_isScanning = false;
    }

    void DriverScanner::ScanDeviceClass(const GUID& classGuid, DriverType type) {
        HDEVINFO deviceInfoSet;
        
        if (classGuid == GUID_NULL) {
            deviceInfoSet = SetupDiGetClassDevsW(nullptr, nullptr, nullptr, 
                DIGCF_ALLCLASSES | DIGCF_PRESENT);
        } else {
            deviceInfoSet = SetupDiGetClassDevsW(&classGuid, nullptr, nullptr, 
                DIGCF_PRESENT);
        }

        if (deviceInfoSet == INVALID_HANDLE_VALUE) {
            return;
        }

        SP_DEVINFO_DATA deviceInfoData;
        deviceInfoData.cbSize = sizeof(SP_DEVINFO_DATA);

        for (DWORD i = 0; SetupDiEnumDeviceInfo(deviceInfoSet, i, &deviceInfoData); i++) {
            if (m_cancelRequested) break;

            DriverInfo info = GetDriverInfo(deviceInfoSet, deviceInfoData);
            
            // Determine type if scanning all classes
            if (classGuid == GUID_NULL) {
                info.type = ClassifyDriverType(info.deviceClassGuid);
            } else {
                info.type = type;
            }

            // Add to appropriate category
            std::lock_guard<std::mutex> lock(m_mutex);
            auto& category = GetOrCreateCategory(info.type);
            
            // Avoid duplicates
            bool exists = false;
            for (const auto& existing : category.drivers) {
                if (existing.deviceInstanceId == info.deviceInstanceId) {
                    exists = true;
                    break;
                }
            }
            
            if (!exists && !info.deviceName.empty()) {
                category.drivers.push_back(info);
            }

            if (m_progressCallback) {
                m_progressCallback(i, -1, info.deviceName);
            }
        }

        SetupDiDestroyDeviceInfoList(deviceInfoSet);
    }

    DriverInfo DriverScanner::GetDriverInfo(HDEVINFO deviceInfoSet, SP_DEVINFO_DATA& deviceInfoData) {
        DriverInfo info;

        // Get device description (friendly name)
        info.deviceDescription = GetDeviceRegistryProperty(deviceInfoSet, deviceInfoData, SPDRP_DEVICEDESC);
        info.deviceName = GetDeviceRegistryProperty(deviceInfoSet, deviceInfoData, SPDRP_FRIENDLYNAME);
        
        if (info.deviceName.empty()) {
            info.deviceName = info.deviceDescription;
        }

        // Get manufacturer
        info.manufacturer = GetDeviceRegistryProperty(deviceInfoSet, deviceInfoData, SPDRP_MFG);

        // Get hardware ID
        info.hardwareId = GetDeviceRegistryProperty(deviceInfoSet, deviceInfoData, SPDRP_HARDWAREID);

        // Get device class
        info.deviceClass = GetDeviceRegistryProperty(deviceInfoSet, deviceInfoData, SPDRP_CLASS);
        
        // Get class GUID
        wchar_t guidStr[64] = {0};
        DWORD guidSize = sizeof(guidStr);
        if (SetupDiGetDeviceRegistryPropertyW(deviceInfoSet, &deviceInfoData, SPDRP_CLASSGUID,
            nullptr, (PBYTE)guidStr, guidSize, nullptr)) {
            info.deviceClassGuid = guidStr;
        }

        // Get device instance ID
        wchar_t instanceId[MAX_DEVICE_ID_LEN] = {0};
        if (SetupDiGetDeviceInstanceIdW(deviceInfoSet, &deviceInfoData, instanceId, 
            MAX_DEVICE_ID_LEN, nullptr)) {
            info.deviceInstanceId = instanceId;
        }

        // Get driver info
        SP_DRVINFO_DATA_W driverInfoData;
        driverInfoData.cbSize = sizeof(SP_DRVINFO_DATA_W);
        
        if (SetupDiBuildDriverInfoList(deviceInfoSet, &deviceInfoData, SPDIT_COMPATDRIVER)) {
            if (SetupDiEnumDriverInfoW(deviceInfoSet, &deviceInfoData, SPDIT_COMPATDRIVER, 0, &driverInfoData)) {
                info.driverProvider = driverInfoData.ProviderName;
                info.driverVersion = std::to_wstring(HIWORD(driverInfoData.DriverVersion)) + L"." +
                                    std::to_wstring(LOWORD(driverInfoData.DriverVersion));
                
                // Format driver date
                SYSTEMTIME st;
                FileTimeToSystemTime(&driverInfoData.DriverDate, &st);
                wchar_t dateStr[32];
                swprintf_s(dateStr, L"%04d-%02d-%02d", st.wYear, st.wMonth, st.wDay);
                info.driverDate = dateStr;
            }
            SetupDiDestroyDriverInfoList(deviceInfoSet, &deviceInfoData, SPDIT_COMPATDRIVER);
        }

        // Get driver status
        info.status = DetermineDriverStatus(deviceInfoSet, deviceInfoData);

        // Check if device is enabled
        DWORD status = 0, problem = 0;
        if (CM_Get_DevNode_Status(&status, &problem, deviceInfoData.DevInst, 0) == CR_SUCCESS) {
            info.isEnabled = !(status & DN_DISABLEABLE && problem == CM_PROB_DISABLED);
            info.problemCode = problem;
        }

        return info;
    }

    DriverStatus DriverScanner::DetermineDriverStatus(HDEVINFO deviceInfoSet, SP_DEVINFO_DATA& deviceInfoData) {
        DWORD status = 0, problem = 0;
        
        if (CM_Get_DevNode_Status(&status, &problem, deviceInfoData.DevInst, 0) != CR_SUCCESS) {
            return DriverStatus::Unknown;
        }

        if (problem == CM_PROB_DISABLED) {
            return DriverStatus::Disabled;
        }
        
        if (problem != 0) {
            // Device has a problem
            if (problem == CM_PROB_FAILED_START || 
                problem == CM_PROB_FAILED_INSTALL ||
                problem == CM_PROB_FAILED_ADD ||
                problem == CM_PROB_DRIVER_FAILED_LOAD) {
                return DriverStatus::Error;
            }
            return DriverStatus::Warning;
        }

        if (status & DN_STARTED) {
            return DriverStatus::OK;
        }

        return DriverStatus::Unknown;
    }

    DriverType DriverScanner::ClassifyDriverType(const std::wstring& classGuid) {
        // Convert string GUID to GUID structure
        GUID guid;
        if (CLSIDFromString(classGuid.c_str(), &guid) != S_OK) {
            return DriverType::Other;
        }

        for (const auto& [knownGuid, type] : s_classGuids) {
            if (IsEqualGUID(guid, knownGuid)) {
                return type;
            }
        }

        return DriverType::Other;
    }

    std::wstring DriverScanner::GetDeviceRegistryProperty(HDEVINFO deviceInfoSet, 
        SP_DEVINFO_DATA& deviceInfoData, DWORD property) {
        
        DWORD requiredSize = 0;
        SetupDiGetDeviceRegistryPropertyW(deviceInfoSet, &deviceInfoData, property,
            nullptr, nullptr, 0, &requiredSize);

        if (requiredSize == 0) {
            return L"";
        }

        std::vector<BYTE> buffer(requiredSize);
        if (SetupDiGetDeviceRegistryPropertyW(deviceInfoSet, &deviceInfoData, property,
            nullptr, buffer.data(), requiredSize, nullptr)) {
            return reinterpret_cast<wchar_t*>(buffer.data());
        }

        return L"";
    }

    std::vector<DriverInfo> DriverScanner::GetAllDrivers() const {
        std::lock_guard<std::mutex> lock(m_mutex);
        std::vector<DriverInfo> allDrivers;
        
        for (const auto& cat : m_categories) {
            allDrivers.insert(allDrivers.end(), cat.drivers.begin(), cat.drivers.end());
        }
        
        return allDrivers;
    }

    std::vector<DriverInfo> DriverScanner::GetProblematicDrivers() const {
        std::lock_guard<std::mutex> lock(m_mutex);
        std::vector<DriverInfo> problematic;
        
        for (const auto& cat : m_categories) {
            for (const auto& driver : cat.drivers) {
                if (driver.status == DriverStatus::Error || 
                    driver.status == DriverStatus::Warning) {
                    problematic.push_back(driver);
                }
            }
        }
        
        return problematic;
    }

    size_t DriverScanner::GetTotalDriverCount() const {
        std::lock_guard<std::mutex> lock(m_mutex);
        size_t count = 0;
        for (const auto& cat : m_categories) {
            count += cat.drivers.size();
        }
        return count;
    }

    size_t DriverScanner::GetProblematicDriverCount() const {
        return GetProblematicDrivers().size();
    }

    bool DriverScanner::EnableDriver(const DriverInfo& driver) {
        // Create an empty device info set
        HDEVINFO deviceInfoSet = SetupDiCreateDeviceInfoList(nullptr, nullptr);
        
        if (deviceInfoSet == INVALID_HANDLE_VALUE) {
            return false;
        }

        SP_DEVINFO_DATA deviceInfoData;
        deviceInfoData.cbSize = sizeof(SP_DEVINFO_DATA);
        
        bool success = false;
        
        // Open the specific device by its instance ID
        if (SetupDiOpenDeviceInfoW(deviceInfoSet, driver.deviceInstanceId.c_str(), 
            nullptr, 0, &deviceInfoData)) {
            
            SP_PROPCHANGE_PARAMS params;
            params.ClassInstallHeader.cbSize = sizeof(SP_CLASSINSTALL_HEADER);
            params.ClassInstallHeader.InstallFunction = DIF_PROPERTYCHANGE;
            params.StateChange = DICS_ENABLE;
            params.Scope = DICS_FLAG_GLOBAL;
            params.HwProfile = 0;

            if (SetupDiSetClassInstallParamsW(deviceInfoSet, &deviceInfoData, 
                &params.ClassInstallHeader, sizeof(params))) {
                success = SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, deviceInfoSet, &deviceInfoData);
            }
        }

        SetupDiDestroyDeviceInfoList(deviceInfoSet);
        return success;
    }

    bool DriverScanner::DisableDriver(const DriverInfo& driver) {
        // Create an empty device info set
        HDEVINFO deviceInfoSet = SetupDiCreateDeviceInfoList(nullptr, nullptr);
        
        if (deviceInfoSet == INVALID_HANDLE_VALUE) {
            return false;
        }

        SP_DEVINFO_DATA deviceInfoData;
        deviceInfoData.cbSize = sizeof(SP_DEVINFO_DATA);
        
        bool success = false;
        
        // Open the specific device by its instance ID
        if (SetupDiOpenDeviceInfoW(deviceInfoSet, driver.deviceInstanceId.c_str(), 
            nullptr, 0, &deviceInfoData)) {
            
            SP_PROPCHANGE_PARAMS params;
            params.ClassInstallHeader.cbSize = sizeof(SP_CLASSINSTALL_HEADER);
            params.ClassInstallHeader.InstallFunction = DIF_PROPERTYCHANGE;
            params.StateChange = DICS_DISABLE;
            params.Scope = DICS_FLAG_GLOBAL;
            params.HwProfile = 0;

            if (SetupDiSetClassInstallParamsW(deviceInfoSet, &deviceInfoData, 
                &params.ClassInstallHeader, sizeof(params))) {
                success = SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, deviceInfoSet, &deviceInfoData);
            }
        }

        SetupDiDestroyDeviceInfoList(deviceInfoSet);
        return success;
    }

    bool DriverScanner::UninstallDriver(const DriverInfo& driver) {
        // Create an empty device info set
        HDEVINFO deviceInfoSet = SetupDiCreateDeviceInfoList(nullptr, nullptr);
        
        if (deviceInfoSet == INVALID_HANDLE_VALUE) {
            return false;
        }

        SP_DEVINFO_DATA deviceInfoData;
        deviceInfoData.cbSize = sizeof(SP_DEVINFO_DATA);
        
        bool success = false;
        
        // Open the specific device by its instance ID
        if (SetupDiOpenDeviceInfoW(deviceInfoSet, driver.deviceInstanceId.c_str(), 
            nullptr, 0, &deviceInfoData)) {
            success = SetupDiCallClassInstaller(DIF_REMOVE, deviceInfoSet, &deviceInfoData);
        }

        SetupDiDestroyDeviceInfoList(deviceInfoSet);
        return success;
    }

    bool DriverScanner::UpdateDriver(const DriverInfo& driver) {
        // Open Device Manager for manual update
        // Using ShellExecute to open device manager properties
        std::wstring command = L"devmgmt.msc";
        
        SHELLEXECUTEINFOW sei = { sizeof(sei) };
        sei.lpVerb = L"open";
        sei.lpFile = command.c_str();
        sei.nShow = SW_SHOWNORMAL;
        
        return ShellExecuteExW(&sei) != FALSE;
    }

    bool DriverScanner::ExportToFile(const std::wstring& filePath) const {
        std::wofstream file(filePath);
        if (!file.is_open()) {
            return false;
        }

        file << L"Driver Manager - Export\n";
        file << L"========================\n\n";

        std::lock_guard<std::mutex> lock(m_mutex);
        for (const auto& cat : m_categories) {
            if (cat.drivers.empty()) continue;
            
            file << L"\n[" << cat.name << L"]\n";
            file << L"----------------------------------------\n";
            
            for (const auto& driver : cat.drivers) {
                file << L"Nom: " << driver.deviceName << L"\n";
                file << L"  Description: " << driver.deviceDescription << L"\n";
                file << L"  Fabricant: " << driver.manufacturer << L"\n";
                file << L"  Version: " << driver.driverVersion << L"\n";
                file << L"  Date: " << driver.driverDate << L"\n";
                file << L"  Hardware ID: " << driver.hardwareId << L"\n";
                file << L"  Status: " << (driver.status == DriverStatus::OK ? L"OK" : 
                                         driver.status == DriverStatus::Warning ? L"Avertissement" :
                                         driver.status == DriverStatus::Error ? L"Erreur" : L"Inconnu") << L"\n";
                file << L"\n";
            }
        }

        return true;
    }

    bool DriverScanner::BackupDriver(const DriverInfo& driver, const std::wstring& backupPath) {
        // Use DISM or pnputil to backup driver
        // This is a simplified implementation
        std::wstring command = L"pnputil /export-driver \"" + driver.infPath + L"\" \"" + backupPath + L"\"";
        
        STARTUPINFOW si = { sizeof(si) };
        PROCESS_INFORMATION pi;
        
        if (CreateProcessW(nullptr, const_cast<wchar_t*>(command.c_str()), nullptr, nullptr, 
            FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi)) {
            WaitForSingleObject(pi.hProcess, INFINITE);
            DWORD exitCode;
            GetExitCodeProcess(pi.hProcess, &exitCode);
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
            return exitCode == 0;
        }
        
        return false;
    }

} // namespace DriverManager
