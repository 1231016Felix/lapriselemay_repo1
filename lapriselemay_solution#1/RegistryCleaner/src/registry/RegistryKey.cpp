// RegistryKey.cpp - Registry Key Implementation
#include "pch.h"
#include "registry/RegistryKey.h"

namespace RegistryCleaner::Registry {

    RegistryError RegistryKey::MakeError(LSTATUS code, StringView keyPath) {
        LPWSTR messageBuffer = nullptr;
        FormatMessageW(
            FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
            nullptr,
            code,
            MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
            reinterpret_cast<LPWSTR>(&messageBuffer),
            0,
            nullptr
        );
        
        String message = messageBuffer ? messageBuffer : L"Unknown error";
        if (messageBuffer) LocalFree(messageBuffer);
        
        // Remove trailing newline
        while (!message.empty() && (message.back() == L'\n' || message.back() == L'\r')) {
            message.pop_back();
        }
        
        return RegistryError{ code, std::move(message), String(keyPath) };
    }

    RegistryResult<RegistryKey> RegistryKey::Open(
        RootKey root,
        StringView subKey,
        REGSAM access
    ) {
        HKEY hKey = nullptr;
        String fullPath = std::format(L"{}\\{}", ToString(root), subKey);
        
        LSTATUS result = RegOpenKeyExW(
            ToHKey(root),
            String(subKey).c_str(),
            0,
            access,
            &hKey
        );
        
        if (result != ERROR_SUCCESS) {
            return std::unexpected(MakeError(result, fullPath));
        }
        
        return RegistryKey(hKey, std::move(fullPath), true);
    }

    RegistryResult<RegistryKey> RegistryKey::Open(
        HKEY parentKey,
        StringView subKey,
        StringView parentPath,
        REGSAM access
    ) {
        HKEY hKey = nullptr;
        String fullPath = std::format(L"{}\\{}", parentPath, subKey);
        
        LSTATUS result = RegOpenKeyExW(
            parentKey,
            String(subKey).c_str(),
            0,
            access,
            &hKey
        );
        
        if (result != ERROR_SUCCESS) {
            return std::unexpected(MakeError(result, fullPath));
        }
        
        return RegistryKey(hKey, std::move(fullPath), true);
    }

    RegistryResult<RegistryKey> RegistryKey::Create(
        RootKey root,
        StringView subKey,
        REGSAM access
    ) {
        HKEY hKey = nullptr;
        DWORD disposition = 0;
        String fullPath = std::format(L"{}\\{}", ToString(root), subKey);
        
        LSTATUS result = RegCreateKeyExW(
            ToHKey(root),
            String(subKey).c_str(),
            0,
            nullptr,
            REG_OPTION_NON_VOLATILE,
            access,
            nullptr,
            &hKey,
            &disposition
        );
        
        if (result != ERROR_SUCCESS) {
            return std::unexpected(MakeError(result, fullPath));
        }
        
        return RegistryKey(hKey, std::move(fullPath), true);
    }

    void RegistryKey::Close() noexcept {
        if (m_hKey && m_ownsHandle) {
            RegCloseKey(m_hKey);
        }
        m_hKey = nullptr;
        m_ownsHandle = false;
    }

    RegistryResult<std::vector<String>> RegistryKey::EnumerateSubKeys() const {
        if (!IsValid()) {
            return std::unexpected(MakeError(ERROR_INVALID_HANDLE, m_path));
        }

        std::vector<String> subKeys;
        DWORD index = 0;
        wchar_t nameBuffer[256];
        DWORD nameSize;
        
        while (true) {
            nameSize = static_cast<DWORD>(std::size(nameBuffer));
            LSTATUS result = RegEnumKeyExW(
                m_hKey,
                index,
                nameBuffer,
                &nameSize,
                nullptr,
                nullptr,
                nullptr,
                nullptr
            );
            
            if (result == ERROR_NO_MORE_ITEMS) {
                break;
            }
            
            if (result != ERROR_SUCCESS) {
                return std::unexpected(MakeError(result, m_path));
            }
            
            subKeys.emplace_back(nameBuffer, nameSize);
            ++index;
        }
        
        return subKeys;
    }

    RegistryResult<std::vector<RegistryValue>> RegistryKey::EnumerateValues() const {
        if (!IsValid()) {
            return std::unexpected(MakeError(ERROR_INVALID_HANDLE, m_path));
        }

        std::vector<RegistryValue> values;
        DWORD index = 0;
        wchar_t nameBuffer[16384];
        DWORD nameSize;
        DWORD type;
        std::vector<BYTE> dataBuffer(65536);
        DWORD dataSize;
        
        while (true) {
            nameSize = static_cast<DWORD>(std::size(nameBuffer));
            dataSize = static_cast<DWORD>(dataBuffer.size());
            
            LSTATUS result = RegEnumValueW(
                m_hKey,
                index,
                nameBuffer,
                &nameSize,
                nullptr,
                &type,
                dataBuffer.data(),
                &dataSize
            );
            
            if (result == ERROR_NO_MORE_ITEMS) {
                break;
            }
            
            if (result == ERROR_MORE_DATA) {
                // Resize buffer and retry
                dataBuffer.resize(dataSize);
                continue;
            }
            
            if (result != ERROR_SUCCESS) {
                return std::unexpected(MakeError(result, m_path));
            }
            
            values.push_back(RegistryValue::FromBytes(
                String(nameBuffer, nameSize),
                static_cast<ValueType>(type),
                std::span(dataBuffer.data(), dataSize)
            ));
            
            ++index;
        }
        
        return values;
    }

    RegistryResult<RegistryValue> RegistryKey::GetValue(StringView valueName) const {
        if (!IsValid()) {
            return std::unexpected(MakeError(ERROR_INVALID_HANDLE, m_path));
        }

        DWORD type = 0;
        DWORD dataSize = 0;
        String name(valueName);
        
        // First call to get size
        LSTATUS result = RegQueryValueExW(
            m_hKey,
            name.c_str(),
            nullptr,
            &type,
            nullptr,
            &dataSize
        );
        
        if (result != ERROR_SUCCESS && result != ERROR_MORE_DATA) {
            return std::unexpected(MakeError(result, m_path));
        }
        
        // Second call to get data
        std::vector<BYTE> data(dataSize);
        result = RegQueryValueExW(
            m_hKey,
            name.c_str(),
            nullptr,
            &type,
            data.data(),
            &dataSize
        );
        
        if (result != ERROR_SUCCESS) {
            return std::unexpected(MakeError(result, m_path));
        }
        
        return RegistryValue::FromBytes(
            std::move(name),
            static_cast<ValueType>(type),
            std::span(data.data(), dataSize)
        );
    }

    RegistryResult<void> RegistryKey::SetValue(const RegistryValue& value) {
        if (!IsValid()) {
            return std::unexpected(MakeError(ERROR_INVALID_HANDLE, m_path));
        }

        auto bytes = value.ToBytes();
        
        LSTATUS result = RegSetValueExW(
            m_hKey,
            value.Name().c_str(),
            0,
            static_cast<DWORD>(value.Type()),
            bytes.data(),
            static_cast<DWORD>(bytes.size())
        );
        
        if (result != ERROR_SUCCESS) {
            return std::unexpected(MakeError(result, m_path));
        }
        
        return {};
    }

    RegistryResult<void> RegistryKey::DeleteValue(StringView valueName) {
        if (!IsValid()) {
            return std::unexpected(MakeError(ERROR_INVALID_HANDLE, m_path));
        }

        LSTATUS result = RegDeleteValueW(m_hKey, String(valueName).c_str());
        
        if (result != ERROR_SUCCESS) {
            return std::unexpected(MakeError(result, m_path));
        }
        
        return {};
    }

    RegistryResult<void> RegistryKey::DeleteSubKey(StringView subKeyName) {
        if (!IsValid()) {
            return std::unexpected(MakeError(ERROR_INVALID_HANDLE, m_path));
        }

        LSTATUS result = RegDeleteKeyW(m_hKey, String(subKeyName).c_str());
        
        if (result != ERROR_SUCCESS) {
            return std::unexpected(MakeError(result, m_path));
        }
        
        return {};
    }

    RegistryResult<void> RegistryKey::DeleteSubKeyTree(StringView subKeyName) {
        if (!IsValid()) {
            return std::unexpected(MakeError(ERROR_INVALID_HANDLE, m_path));
        }

        LSTATUS result = RegDeleteTreeW(m_hKey, String(subKeyName).c_str());
        
        if (result != ERROR_SUCCESS) {
            return std::unexpected(MakeError(result, m_path));
        }
        
        return {};
    }

    bool RegistryKey::SubKeyExists(StringView subKeyName) const {
        if (!IsValid()) return false;
        
        HKEY hSubKey = nullptr;
        LSTATUS result = RegOpenKeyExW(
            m_hKey,
            String(subKeyName).c_str(),
            0,
            KEY_READ,
            &hSubKey
        );
        
        if (result == ERROR_SUCCESS) {
            RegCloseKey(hSubKey);
            return true;
        }
        
        return false;
    }

    bool RegistryKey::ValueExists(StringView valueName) const {
        if (!IsValid()) return false;
        
        LSTATUS result = RegQueryValueExW(
            m_hKey,
            String(valueName).c_str(),
            nullptr,
            nullptr,
            nullptr,
            nullptr
        );
        
        return result == ERROR_SUCCESS;
    }

    RegistryResult<DWORD> RegistryKey::GetSubKeyCount() const {
        if (!IsValid()) {
            return std::unexpected(MakeError(ERROR_INVALID_HANDLE, m_path));
        }

        DWORD subKeyCount = 0;
        LSTATUS result = RegQueryInfoKeyW(
            m_hKey,
            nullptr, nullptr, nullptr,
            &subKeyCount,
            nullptr, nullptr, nullptr,
            nullptr, nullptr, nullptr, nullptr
        );
        
        if (result != ERROR_SUCCESS) {
            return std::unexpected(MakeError(result, m_path));
        }
        
        return subKeyCount;
    }

    RegistryResult<DWORD> RegistryKey::GetValueCount() const {
        if (!IsValid()) {
            return std::unexpected(MakeError(ERROR_INVALID_HANDLE, m_path));
        }

        DWORD valueCount = 0;
        LSTATUS result = RegQueryInfoKeyW(
            m_hKey,
            nullptr, nullptr, nullptr,
            nullptr, nullptr, nullptr,
            &valueCount,
            nullptr, nullptr, nullptr, nullptr
        );
        
        if (result != ERROR_SUCCESS) {
            return std::unexpected(MakeError(result, m_path));
        }
        
        return valueCount;
    }

} // namespace RegistryCleaner::Registry
