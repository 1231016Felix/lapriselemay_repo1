#include "DriverScanner.h"
#include "Core/Logger.h"
#include <shellapi.h>
#include <sstream>
#include <fstream>
#include <iomanip>
#include <algorithm>
#include <unordered_map>
#include <unordered_set>

namespace DriverManager {

    // Helper pour convertir un GUID en wstring
    static std::wstring GuidToWString(const GUID& guid) {
        wchar_t buffer[64];
        swprintf_s(buffer, L"{%08lX-%04hX-%04hX-%02hhX%02hhX-%02hhX%02hhX%02hhX%02hhX%02hhX%02hhX}",
            guid.Data1, guid.Data2, guid.Data3,
            guid.Data4[0], guid.Data4[1], guid.Data4[2], guid.Data4[3],
            guid.Data4[4], guid.Data4[5], guid.Data4[6], guid.Data4[7]);
        return buffer;
    }

    // RAII wrapper local pour HDEVINFO (compatible avec le code existant)
    class DeviceInfoSetHandle {
    public:
        explicit DeviceInfoSetHandle(HDEVINFO handle = INVALID_HANDLE_VALUE) 
            : m_handle(handle) {}
        
        ~DeviceInfoSetHandle() {
            if (IsValid()) {
                SetupDiDestroyDeviceInfoList(m_handle);
            }
        }
        
        DeviceInfoSetHandle(const DeviceInfoSetHandle&) = delete;
        DeviceInfoSetHandle& operator=(const DeviceInfoSetHandle&) = delete;
        
        DeviceInfoSetHandle(DeviceInfoSetHandle&& other) noexcept 
            : m_handle(other.m_handle) {
            other.m_handle = INVALID_HANDLE_VALUE;
        }
        
        HDEVINFO Get() const { return m_handle; }
        operator HDEVINFO() const { return m_handle; }
        bool IsValid() const { return m_handle != INVALID_HANDLE_VALUE; }
        
        void Reset(HDEVINFO handle = INVALID_HANDLE_VALUE) {
            if (IsValid()) {
                SetupDiDestroyDeviceInfoList(m_handle);
            }
            m_handle = handle;
        }

    private:
        HDEVINFO m_handle;
    };

    // Hash map pour lookup O(1) des GUIDs par string
    static std::unordered_map<std::wstring, DriverType> CreateGuidLookupMap() {
        std::unordered_map<std::wstring, DriverType> map;
        map[GuidToWString(GUID_DEVCLASS_DISPLAY)] = DriverType::Display;
        map[GuidToWString(GUID_DEVCLASS_MEDIA)] = DriverType::Audio;
        map[GuidToWString(GUID_DEVCLASS_NET)] = DriverType::Network;
        map[GuidToWString(GUID_DEVCLASS_DISKDRIVE)] = DriverType::Storage;
        map[GuidToWString(GUID_DEVCLASS_USB)] = DriverType::USB;
        map[GuidToWString(GUID_DEVCLASS_BLUETOOTH)] = DriverType::Bluetooth;
        map[GuidToWString(GUID_DEVCLASS_PRINTER)] = DriverType::Printer;
        map[GuidToWString(GUID_DEVCLASS_HIDCLASS)] = DriverType::HID;
        map[GuidToWString(GUID_DEVCLASS_SYSTEM)] = DriverType::System;
        map[GuidToWString(GUID_DEVCLASS_KEYBOARD)] = DriverType::HID;
        map[GuidToWString(GUID_DEVCLASS_MOUSE)] = DriverType::HID;
        map[GuidToWString(GUID_DEVCLASS_MONITOR)] = DriverType::Display;
        map[GuidToWString(GUID_DEVCLASS_VOLUME)] = DriverType::Storage;
        map[GuidToWString(GUID_DEVCLASS_HDC)] = DriverType::Storage;
        return map;
    }

    static const std::unordered_map<std::wstring, DriverType> s_guidLookup = CreateGuidLookupMap();

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
        LOG_INFO(L"DriverScanner initialisé");
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
        LOG_INFO(L"DriverScanner détruit");
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
        return m_categories.back();
    }

    void DriverScanner::ScanAllDrivers() {
        if (m_isScanning) return;
        
        LOG_INFO(L"Démarrage du scan des pilotes");
        m_isScanning = true;
        m_cancelRequested = false;
        ClearCategories();

        int totalClasses = static_cast<int>(s_classGuids.size()) + 1;
        int currentClass = 0;

        for (const auto& [guid, type] : s_classGuids) {
            if (m_cancelRequested) {
                LOG_INFO(L"Scan annulé par l'utilisateur");
                break;
            }
            
            if (m_progressCallback) {
                m_progressCallback(currentClass, totalClasses, L"Scanning...");
            }
            
            ScanDeviceClass(guid, type);
            currentClass++;
        }

        if (m_progressCallback) {
            m_progressCallback(currentClass, totalClasses, L"Scanning autres périphériques...");
        }
        ScanDeviceClass(GUID_NULL, DriverType::Other);
        currentClass++;
        
        // Préparer les champs de recherche pour tous les drivers
        {
            std::lock_guard<std::mutex> lock(m_mutex);
            for (auto& cat : m_categories) {
                for (auto& driver : cat.drivers) {
                    driver.PrepareSearchFields();
                }
            }
        }
        
        if (m_progressCallback) {
            m_progressCallback(totalClasses, totalClasses, L"Terminé");
        }

        LOG_INFO(L"Scan terminé: " + std::to_wstring(GetTotalDriverCount()) + L" pilotes trouvés");
        m_isScanning = false;
    }

    void DriverScanner::ScanCategory(DriverType type) {
        if (m_isScanning) return;
        
        m_isScanning = true;
        m_cancelRequested = false;

        for (const auto& [guid, t] : s_classGuids) {
            if (t == type) {
                ScanDeviceClass(guid, type);
                break;
            }
        }

        m_isScanning = false;
    }

    void DriverScanner::ScanDeviceClass(const GUID& classGuid, DriverType type) {
        DeviceInfoSetHandle deviceInfoSet;
        
        if (classGuid == GUID_NULL) {
            deviceInfoSet.Reset(SetupDiGetClassDevsW(nullptr, nullptr, nullptr, 
                DIGCF_ALLCLASSES | DIGCF_PRESENT));
        } else {
            deviceInfoSet.Reset(SetupDiGetClassDevsW(&classGuid, nullptr, nullptr, 
                DIGCF_PRESENT));
        }

        if (!deviceInfoSet.IsValid()) {
            LOG_WARN(L"SetupDiGetClassDevsW a échoué");
            return;
        }

        std::vector<DriverInfo> scannedDrivers;
        scannedDrivers.reserve(50);

        SP_DEVINFO_DATA deviceInfoData;
        deviceInfoData.cbSize = sizeof(SP_DEVINFO_DATA);

        for (DWORD i = 0; SetupDiEnumDeviceInfo(deviceInfoSet, i, &deviceInfoData); i++) {
            if (m_cancelRequested) break;

            DriverInfo info = GetDriverInfo(deviceInfoSet, deviceInfoData);
            
            if (classGuid == GUID_NULL) {
                info.type = ClassifyDriverType(info.deviceClassGuid);
            } else {
                info.type = type;
            }

            if (!info.deviceName.empty()) {
                scannedDrivers.push_back(std::move(info));
            }

            if (m_progressCallback) {
                m_progressCallback(i, -1, scannedDrivers.empty() ? L"" : scannedDrivers.back().deviceName);
            }
        }

        {
            std::lock_guard<std::mutex> lock(m_mutex);
            
            std::unordered_set<std::wstring> existingIds;
            for (const auto& cat : m_categories) {
                for (const auto& driver : cat.drivers) {
                    existingIds.insert(driver.deviceInstanceId);
                }
            }
            
            for (auto& info : scannedDrivers) {
                if (existingIds.find(info.deviceInstanceId) == existingIds.end()) {
                    auto& category = GetOrCreateCategory(info.type);
                    existingIds.insert(info.deviceInstanceId);
                    category.drivers.push_back(std::move(info));
                }
            }
        }
    }

    DriverInfo DriverScanner::GetDriverInfo(HDEVINFO deviceInfoSet, SP_DEVINFO_DATA& deviceInfoData) {
        DriverInfo info;

        info.deviceDescription = GetDeviceRegistryProperty(deviceInfoSet, deviceInfoData, SPDRP_DEVICEDESC);
        info.deviceName = GetDeviceRegistryProperty(deviceInfoSet, deviceInfoData, SPDRP_FRIENDLYNAME);
        
        if (info.deviceName.empty()) {
            info.deviceName = info.deviceDescription;
        }

        info.manufacturer = GetDeviceRegistryProperty(deviceInfoSet, deviceInfoData, SPDRP_MFG);
        info.hardwareId = GetDeviceRegistryProperty(deviceInfoSet, deviceInfoData, SPDRP_HARDWAREID);
        info.deviceClass = GetDeviceRegistryProperty(deviceInfoSet, deviceInfoData, SPDRP_CLASS);
        
        wchar_t guidStr[64] = {0};
        DWORD guidSize = sizeof(guidStr);
        if (SetupDiGetDeviceRegistryPropertyW(deviceInfoSet, &deviceInfoData, SPDRP_CLASSGUID,
            nullptr, (PBYTE)guidStr, guidSize, nullptr)) {
            info.deviceClassGuid = guidStr;
        }

        wchar_t instanceId[MAX_DEVICE_ID_LEN] = {0};
        if (SetupDiGetDeviceInstanceIdW(deviceInfoSet, &deviceInfoData, instanceId, 
            MAX_DEVICE_ID_LEN, nullptr)) {
            info.deviceInstanceId = instanceId;
        }

        SP_DRVINFO_DATA_W driverInfoData;
        driverInfoData.cbSize = sizeof(SP_DRVINFO_DATA_W);
        
        if (SetupDiBuildDriverInfoList(deviceInfoSet, &deviceInfoData, SPDIT_COMPATDRIVER)) {
            if (SetupDiEnumDriverInfoW(deviceInfoSet, &deviceInfoData, SPDIT_COMPATDRIVER, 0, &driverInfoData)) {
                info.driverProvider = driverInfoData.ProviderName;
                info.driverVersion = std::to_wstring(HIWORD(driverInfoData.DriverVersion)) + L"." +
                                    std::to_wstring(LOWORD(driverInfoData.DriverVersion));
                
                SYSTEMTIME st;
                FileTimeToSystemTime(&driverInfoData.DriverDate, &st);
                wchar_t dateStr[32];
                swprintf_s(dateStr, L"%04d-%02d-%02d", st.wYear, st.wMonth, st.wDay);
                info.driverDate = dateStr;
            }
            SetupDiDestroyDriverInfoList(deviceInfoSet, &deviceInfoData, SPDIT_COMPATDRIVER);
        }

        info.status = DetermineDriverStatus(deviceInfoSet, deviceInfoData);

        DWORD status = 0, problem = 0;
        if (CM_Get_DevNode_Status(&status, &problem, deviceInfoData.DevInst, 0) == CR_SUCCESS) {
            info.isEnabled = !(status & DN_DISABLEABLE && problem == CM_PROB_DISABLED);
            info.problemCode = problem;
        }

        info.CalculateAge();

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
        auto it = s_guidLookup.find(classGuid);
        if (it != s_guidLookup.end()) {
            return it->second;
        }

        std::wstring upperGuid = classGuid;
        std::transform(upperGuid.begin(), upperGuid.end(), upperGuid.begin(), ::towupper);
        
        it = s_guidLookup.find(upperGuid);
        if (it != s_guidLookup.end()) {
            return it->second;
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

    VoidResult DriverScanner::EnableDriver(const DriverInfo& driver) {
        LOG_INFO(L"Tentative d'activation du pilote: " + driver.deviceName);
        
        // Method 1: Try CM_Enable_DevNode first
        DEVINST devInst = 0;
        CONFIGRET cr = CM_Locate_DevNodeW(&devInst, 
            const_cast<DEVINSTID_W>(driver.deviceInstanceId.c_str()), 
            CM_LOCATE_DEVNODE_NORMAL);
        
        if (cr != CR_SUCCESS) {
            LOG_ERROR(L"CM_Locate_DevNodeW a échoué: " + std::to_wstring(cr));
            return Results::Fail(L"Impossible de localiser le périphérique (code " + std::to_wstring(cr) + L")", cr);
        }
        
        cr = CM_Enable_DevNode(devInst, 0);
        if (cr == CR_SUCCESS) {
            LOG_INFO(L"Pilote activé avec succès via CM_Enable_DevNode");
            return Results::Ok();
        }
        
        LOG_WARN(L"CM_Enable_DevNode a échoué (code " + std::to_wstring(cr) + L"), tentative SetupDi...");
        
        // Method 2: Fallback to SetupDi approach
        DeviceInfoSetHandle deviceInfoSet(SetupDiGetClassDevsW(nullptr, nullptr, nullptr, 
            DIGCF_ALLCLASSES | DIGCF_PRESENT));
        
        if (!deviceInfoSet.IsValid()) {
            return Results::FailureFromLastError(L"SetupDiGetClassDevsW a échoué");
        }

        SP_DEVINFO_DATA deviceInfoData;
        deviceInfoData.cbSize = sizeof(SP_DEVINFO_DATA);
        
        for (DWORD i = 0; SetupDiEnumDeviceInfo(deviceInfoSet, i, &deviceInfoData); i++) {
            wchar_t instanceId[MAX_DEVICE_ID_LEN] = {0};
            if (SetupDiGetDeviceInstanceIdW(deviceInfoSet, &deviceInfoData, instanceId, 
                MAX_DEVICE_ID_LEN, nullptr)) {
                if (_wcsicmp(instanceId, driver.deviceInstanceId.c_str()) == 0) {
                    SP_PROPCHANGE_PARAMS params;
                    params.ClassInstallHeader.cbSize = sizeof(SP_CLASSINSTALL_HEADER);
                    params.ClassInstallHeader.InstallFunction = DIF_PROPERTYCHANGE;
                    params.StateChange = DICS_ENABLE;
                    params.Scope = DICS_FLAG_GLOBAL;
                    params.HwProfile = 0;

                    if (SetupDiSetClassInstallParamsW(deviceInfoSet, &deviceInfoData, 
                        &params.ClassInstallHeader, sizeof(params))) {
                        if (SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, deviceInfoSet, &deviceInfoData)) {
                            LOG_INFO(L"Pilote activé avec succès via SetupDi (global)");
                            return Results::Ok();
                        }
                        
                        // Try config-specific
                        params.Scope = DICS_FLAG_CONFIGSPECIFIC;
                        if (SetupDiSetClassInstallParamsW(deviceInfoSet, &deviceInfoData, 
                            &params.ClassInstallHeader, sizeof(params))) {
                            if (SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, deviceInfoSet, &deviceInfoData)) {
                                LOG_INFO(L"Pilote activé avec succès via SetupDi (config-specific)");
                                return Results::Ok();
                            }
                        }
                    }
                    
                    return Results::FailureFromLastError(L"SetupDiCallClassInstaller a échoué");
                }
            }
        }

        return Results::Fail(L"Périphérique non trouvé dans la liste des périphériques");
    }

    VoidResult DriverScanner::DisableDriver(const DriverInfo& driver) {
        LOG_INFO(L"Tentative de désactivation du pilote: " + driver.deviceName);
        
        DEVINST devInst = 0;
        CONFIGRET cr = CM_Locate_DevNodeW(&devInst, 
            const_cast<DEVINSTID_W>(driver.deviceInstanceId.c_str()), 
            CM_LOCATE_DEVNODE_NORMAL);
        
        if (cr != CR_SUCCESS) {
            LOG_ERROR(L"CM_Locate_DevNodeW a échoué: " + std::to_wstring(cr));
            return Results::Fail(L"Impossible de localiser le périphérique (code " + std::to_wstring(cr) + L")", cr);
        }
        
        cr = CM_Disable_DevNode(devInst, CM_DISABLE_UI_NOT_OK);
        if (cr == CR_SUCCESS) {
            LOG_INFO(L"Pilote désactivé avec succès via CM_Disable_DevNode");
            return Results::Ok();
        }
        
        LOG_WARN(L"CM_Disable_DevNode a échoué (code " + std::to_wstring(cr) + L"), tentative SetupDi...");
        
        DeviceInfoSetHandle deviceInfoSet(SetupDiGetClassDevsW(nullptr, nullptr, nullptr, 
            DIGCF_ALLCLASSES | DIGCF_PRESENT));
        
        if (!deviceInfoSet.IsValid()) {
            return Results::FailureFromLastError(L"SetupDiGetClassDevsW a échoué");
        }

        SP_DEVINFO_DATA deviceInfoData;
        deviceInfoData.cbSize = sizeof(SP_DEVINFO_DATA);
        
        for (DWORD i = 0; SetupDiEnumDeviceInfo(deviceInfoSet, i, &deviceInfoData); i++) {
            wchar_t instanceId[MAX_DEVICE_ID_LEN] = {0};
            if (SetupDiGetDeviceInstanceIdW(deviceInfoSet, &deviceInfoData, instanceId, 
                MAX_DEVICE_ID_LEN, nullptr)) {
                if (_wcsicmp(instanceId, driver.deviceInstanceId.c_str()) == 0) {
                    SP_PROPCHANGE_PARAMS params;
                    params.ClassInstallHeader.cbSize = sizeof(SP_CLASSINSTALL_HEADER);
                    params.ClassInstallHeader.InstallFunction = DIF_PROPERTYCHANGE;
                    params.StateChange = DICS_DISABLE;
                    params.Scope = DICS_FLAG_GLOBAL;
                    params.HwProfile = 0;

                    if (SetupDiSetClassInstallParamsW(deviceInfoSet, &deviceInfoData, 
                        &params.ClassInstallHeader, sizeof(params))) {
                        if (SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, deviceInfoSet, &deviceInfoData)) {
                            LOG_INFO(L"Pilote désactivé avec succès via SetupDi (global)");
                            return Results::Ok();
                        }
                        
                        params.Scope = DICS_FLAG_CONFIGSPECIFIC;
                        if (SetupDiSetClassInstallParamsW(deviceInfoSet, &deviceInfoData, 
                            &params.ClassInstallHeader, sizeof(params))) {
                            if (SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, deviceInfoSet, &deviceInfoData)) {
                                LOG_INFO(L"Pilote désactivé avec succès via SetupDi (config-specific)");
                                return Results::Ok();
                            }
                        }
                    }
                    
                    return Results::FailureFromLastError(L"SetupDiCallClassInstaller a échoué");
                }
            }
        }

        return Results::Fail(L"Périphérique non trouvé dans la liste des périphériques");
    }

    VoidResult DriverScanner::UninstallDriver(const DriverInfo& driver) {
        LOG_INFO(L"Tentative de désinstallation du pilote: " + driver.deviceName);
        
        DEVINST devInst = 0;
        CONFIGRET cr = CM_Locate_DevNodeW(&devInst, 
            const_cast<DEVINSTID_W>(driver.deviceInstanceId.c_str()), 
            CM_LOCATE_DEVNODE_NORMAL);
        
        if (cr != CR_SUCCESS) {
            LOG_ERROR(L"CM_Locate_DevNodeW a échoué: " + std::to_wstring(cr));
            return Results::Fail(L"Impossible de localiser le périphérique (code " + std::to_wstring(cr) + L")", cr);
        }
        
        cr = CM_Uninstall_DevNode(devInst, 0);
        if (cr == CR_SUCCESS) {
            LOG_INFO(L"Pilote désinstallé avec succès");
            return Results::Ok();
        }
        
        return Results::Fail(L"CM_Uninstall_DevNode a échoué (code " + std::to_wstring(cr) + L")", cr);
    }

    VoidResult DriverScanner::UpdateDriver(const DriverInfo& driver) {
        LOG_INFO(L"Ouverture du gestionnaire de périphériques pour mise à jour");
        
        std::wstring command = L"devmgmt.msc";
        
        SHELLEXECUTEINFOW sei = { sizeof(sei) };
        sei.lpVerb = L"open";
        sei.lpFile = command.c_str();
        sei.nShow = SW_SHOWNORMAL;
        
        if (ShellExecuteExW(&sei)) {
            return Results::Ok();
        }
        
        return Results::FailureFromLastError(L"Impossible d'ouvrir le gestionnaire de périphériques");
    }

    VoidResult DriverScanner::ExportToFile(const std::wstring& filePath) const {
        LOG_INFO(L"Export vers: " + filePath);
        
        std::wofstream file(filePath);
        if (!file.is_open()) {
            return Results::Fail(L"Impossible d'ouvrir le fichier pour écriture: " + filePath);
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

        LOG_INFO(L"Export terminé avec succès");
        return Results::Ok();
    }

    VoidResult DriverScanner::BackupDriver(const DriverInfo& driver, const std::wstring& backupPath) {
        LOG_INFO(L"Backup du pilote vers: " + backupPath);
        
        if (driver.infPath.empty()) {
            return Results::Fail(L"Chemin INF du pilote non disponible");
        }
        
        std::wstring command = L"pnputil /export-driver \"" + driver.infPath + L"\" \"" + backupPath + L"\"";
        
        STARTUPINFOW si = { sizeof(si) };
        PROCESS_INFORMATION pi;
        
        if (!CreateProcessW(nullptr, const_cast<wchar_t*>(command.c_str()), nullptr, nullptr, 
            FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi)) {
            return Results::FailureFromLastError(L"Impossible de lancer pnputil");
        }
        
        WaitForSingleObject(pi.hProcess, INFINITE);
        DWORD exitCode;
        GetExitCodeProcess(pi.hProcess, &exitCode);
        CloseHandle(pi.hProcess);
        CloseHandle(pi.hThread);
        
        if (exitCode == 0) {
            LOG_INFO(L"Backup terminé avec succès");
            return Results::Ok();
        }
        
        return Results::Fail(L"pnputil a retourné le code " + std::to_wstring(exitCode), exitCode);
    }

} // namespace DriverManager
