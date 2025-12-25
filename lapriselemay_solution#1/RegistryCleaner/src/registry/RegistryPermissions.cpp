// RegistryPermissions.cpp - Advanced registry permission handling
#include "pch.h"
#include "registry/RegistryPermissions.h"
#include <aclapi.h>
#include <sddl.h>

#pragma comment(lib, "advapi32.lib")

namespace RegistryCleaner::Registry {

    bool RegistryPermissions::EnablePrivilege(LPCWSTR privilegeName) {
        HANDLE hToken;
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, &hToken)) {
            return false;
        }

        LUID luid;
        if (!LookupPrivilegeValueW(nullptr, privilegeName, &luid)) {
            CloseHandle(hToken);
            return false;
        }

        TOKEN_PRIVILEGES tp{};
        tp.PrivilegeCount = 1;
        tp.Privileges[0].Luid = luid;
        tp.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;

        BOOL result = AdjustTokenPrivileges(hToken, FALSE, &tp, sizeof(tp), nullptr, nullptr);
        DWORD error = GetLastError();
        CloseHandle(hToken);

        return result && error == ERROR_SUCCESS;
    }

    PSID RegistryPermissions::GetAdministratorsSid() {
        static BYTE sidBuffer[SECURITY_MAX_SID_SIZE];
        static bool initialized = false;
        
        if (!initialized) {
            DWORD sidSize = sizeof(sidBuffer);
            CreateWellKnownSid(WinBuiltinAdministratorsSid, nullptr, sidBuffer, &sidSize);
            initialized = true;
        }
        return reinterpret_cast<PSID>(sidBuffer);
    }

    PSID RegistryPermissions::GetCurrentUserSid() {
        static std::vector<BYTE> sidBuffer;
        static bool initialized = false;

        if (!initialized) {
            HANDLE hToken;
            if (OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &hToken)) {
                DWORD size = 0;
                GetTokenInformation(hToken, TokenUser, nullptr, 0, &size);
                
                std::vector<BYTE> buffer(size);
                if (GetTokenInformation(hToken, TokenUser, buffer.data(), size, &size)) {
                    TOKEN_USER* tokenUser = reinterpret_cast<TOKEN_USER*>(buffer.data());
                    DWORD sidLen = GetLengthSid(tokenUser->User.Sid);
                    sidBuffer.resize(sidLen);
                    CopySid(sidLen, sidBuffer.data(), tokenUser->User.Sid);
                    initialized = true;
                }
                CloseHandle(hToken);
            }
        }
        return initialized ? reinterpret_cast<PSID>(sidBuffer.data()) : nullptr;
    }

    std::expected<void, String> RegistryPermissions::TakeOwnership(RootKey root, StringView subKey) {
        // Enable required privileges
        if (!EnablePrivilege(SE_TAKE_OWNERSHIP_NAME)) {
            return std::unexpected(L"Impossible d'activer SE_TAKE_OWNERSHIP_NAME");
        }
        if (!EnablePrivilege(SE_RESTORE_NAME)) {
            return std::unexpected(L"Impossible d'activer SE_RESTORE_NAME");
        }
        if (!EnablePrivilege(SE_BACKUP_NAME)) {
            return std::unexpected(L"Impossible d'activer SE_BACKUP_NAME");
        }

        // Open key with WRITE_OWNER permission
        HKEY hRootKey = ToHKey(root);
        HKEY hKey;
        LONG result = RegOpenKeyExW(hRootKey, String(subKey).c_str(), 0, WRITE_OWNER, &hKey);
        
        if (result != ERROR_SUCCESS) {
            return std::unexpected(std::format(L"Impossible d'ouvrir la cle: {}", result));
        }

        // Set owner to Administrators
        PSID adminSid = GetAdministratorsSid();
        result = SetSecurityInfo(hKey, SE_REGISTRY_KEY, OWNER_SECURITY_INFORMATION,
                                  adminSid, nullptr, nullptr, nullptr);
        
        RegCloseKey(hKey);

        if (result != ERROR_SUCCESS) {
            return std::unexpected(std::format(L"Impossible de prendre possession: {}", result));
        }

        return {};
    }

    std::expected<void, String> RegistryPermissions::GrantFullControl(RootKey root, StringView subKey) {
        // First take ownership
        auto ownerResult = TakeOwnership(root, subKey);
        if (!ownerResult) {
            return ownerResult;
        }

        // Open key with WRITE_DAC permission
        HKEY hRootKey = ToHKey(root);
        HKEY hKey;
        LONG result = RegOpenKeyExW(hRootKey, String(subKey).c_str(), 0, WRITE_DAC, &hKey);
        
        if (result != ERROR_SUCCESS) {
            return std::unexpected(std::format(L"Impossible d'ouvrir la cle pour DACL: {}", result));
        }

        // Create a new DACL with full control for Administrators
        PSID adminSid = GetAdministratorsSid();
        
        EXPLICIT_ACCESSW ea{};
        ea.grfAccessPermissions = KEY_ALL_ACCESS;
        ea.grfAccessMode = SET_ACCESS;
        ea.grfInheritance = SUB_CONTAINERS_AND_OBJECTS_INHERIT;
        ea.Trustee.TrusteeForm = TRUSTEE_IS_SID;
        ea.Trustee.TrusteeType = TRUSTEE_IS_GROUP;
        ea.Trustee.ptstrName = reinterpret_cast<LPWSTR>(adminSid);

        PACL pNewDacl = nullptr;
        DWORD dwRes = SetEntriesInAclW(1, &ea, nullptr, &pNewDacl);
        
        if (dwRes != ERROR_SUCCESS) {
            RegCloseKey(hKey);
            return std::unexpected(std::format(L"Impossible de creer DACL: {}", dwRes));
        }

        // Apply the new DACL
        result = SetSecurityInfo(hKey, SE_REGISTRY_KEY, DACL_SECURITY_INFORMATION,
                                  nullptr, nullptr, pNewDacl, nullptr);
        
        LocalFree(pNewDacl);
        RegCloseKey(hKey);

        if (result != ERROR_SUCCESS) {
            return std::unexpected(std::format(L"Impossible d'appliquer DACL: {}", result));
        }

        return {};
    }

    std::expected<void, String> RegistryPermissions::ForceDeleteKey(RootKey root, StringView subKey) {
        // First grant full control
        auto aclResult = GrantFullControl(root, subKey);
        if (!aclResult) {
            // Try anyway, might work with existing permissions
        }

        // First delete all subkeys recursively
        HKEY hRootKey = ToHKey(root);
        HKEY hKey;
        LONG result = RegOpenKeyExW(hRootKey, String(subKey).c_str(), 0, 
                                     KEY_READ | KEY_WRITE | DELETE, &hKey);
        
        if (result != ERROR_SUCCESS) {
            return std::unexpected(std::format(L"Impossible d'ouvrir pour suppression: {}", result));
        }

        // Enumerate and delete subkeys
        wchar_t subKeyName[256];
        DWORD subKeyLen;
        
        while (true) {
            subKeyLen = 256;
            result = RegEnumKeyExW(hKey, 0, subKeyName, &subKeyLen, nullptr, nullptr, nullptr, nullptr);
            
            if (result == ERROR_NO_MORE_ITEMS) break;
            if (result != ERROR_SUCCESS) break;

            // Recursively delete subkey
            String fullSubKey = String(subKey) + L"\\" + subKeyName;
            auto subResult = ForceDeleteKey(root, fullSubKey);
            if (!subResult) {
                // Try RegDeleteTree as fallback
                RegDeleteTreeW(hKey, subKeyName);
            }
        }

        RegCloseKey(hKey);

        // Now delete the key itself
        result = RegDeleteKeyExW(hRootKey, String(subKey).c_str(), KEY_WOW64_64KEY, 0);
        
        if (result != ERROR_SUCCESS) {
            // Try RegDeleteKeyW as fallback
            result = RegDeleteKeyW(hRootKey, String(subKey).c_str());
        }

        if (result != ERROR_SUCCESS) {
            return std::unexpected(std::format(L"Echec suppression finale: {}", result));
        }

        return {};
    }

    std::expected<void, String> RegistryPermissions::ForceDeleteValue(
        RootKey root, StringView subKey, StringView valueName) {
        
        // First grant full control
        auto aclResult = GrantFullControl(root, subKey);
        // Continue even if this fails

        HKEY hRootKey = ToHKey(root);
        HKEY hKey;
        LONG result = RegOpenKeyExW(hRootKey, String(subKey).c_str(), 0, KEY_SET_VALUE, &hKey);
        
        if (result != ERROR_SUCCESS) {
            return std::unexpected(std::format(L"Impossible d'ouvrir pour suppression valeur: {}", result));
        }

        result = RegDeleteValueW(hKey, String(valueName).c_str());
        RegCloseKey(hKey);

        if (result != ERROR_SUCCESS) {
            return std::unexpected(std::format(L"Echec suppression valeur: {}", result));
        }

        return {};
    }

    std::expected<void, String> RegistryPermissions::ScheduleDeleteOnReboot(
        RootKey root, StringView subKey) {
        
        // Build the registry path for PendingFileRenameOperations style
        // Format: \Registry\Machine\... or \Registry\User\...
        String regPath;
        switch (root) {
            case RootKey::LocalMachine:
                regPath = L"\\Registry\\Machine\\" + String(subKey);
                break;
            case RootKey::CurrentUser:
                regPath = L"\\Registry\\User\\" + String(subKey);
                break;
            case RootKey::ClassesRoot:
                regPath = L"\\Registry\\Machine\\SOFTWARE\\Classes\\" + String(subKey);
                break;
            default:
                return std::unexpected(L"Root key non supporte pour suppression au redemarrage");
        }

        // Add to RunOnce to delete on next boot using reg.exe
        String rootStr;
        switch (root) {
            case RootKey::LocalMachine: rootStr = L"HKLM"; break;
            case RootKey::CurrentUser: rootStr = L"HKCU"; break;
            case RootKey::ClassesRoot: rootStr = L"HKCR"; break;
            default: rootStr = L"HKLM"; break;
        }

        String command = std::format(L"reg delete \"{}\\{}\" /f", rootStr, String(subKey));
        
        HKEY hRunOnce;
        LONG result = RegOpenKeyExW(HKEY_LOCAL_MACHINE,
            L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\RunOnce",
            0, KEY_SET_VALUE, &hRunOnce);
        
        if (result != ERROR_SUCCESS) {
            return std::unexpected(L"Impossible d'ouvrir RunOnce");
        }

        // Create unique value name
        String valueName = std::format(L"RegistryCleaner_Delete_{}", 
            std::hash<std::wstring>{}(String(subKey)));

        result = RegSetValueExW(hRunOnce, valueName.c_str(), 0, REG_SZ,
            reinterpret_cast<const BYTE*>(command.c_str()),
            static_cast<DWORD>((command.length() + 1) * sizeof(wchar_t)));
        
        RegCloseKey(hRunOnce);

        if (result != ERROR_SUCCESS) {
            return std::unexpected(L"Impossible de programmer la suppression");
        }

        return {};
    }

} // namespace RegistryCleaner::Registry
