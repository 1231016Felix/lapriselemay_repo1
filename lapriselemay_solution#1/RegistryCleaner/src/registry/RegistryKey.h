// RegistryKey.h - RAII Wrapper for Windows Registry Keys
#pragma once

#include "pch.h"
#include "RegistryValue.h"

namespace RegistryCleaner::Registry {

    // Forward declarations
    class RegistryKey;

    // Registry error type
    struct RegistryError {
        LSTATUS code;
        String message;
        String keyPath;

        [[nodiscard]] String ToString() const {
            return std::format(L"Erreur registre [{}]: {} ({})", code, message, keyPath);
        }
    };

    // Result type for registry operations
    template<typename T>
    using RegistryResult = std::expected<T, RegistryError>;

    // Predefined root keys
    enum class RootKey {
        ClassesRoot,
        CurrentUser,
        LocalMachine,
        Users,
        CurrentConfig
    };

    // Convert RootKey to HKEY
    [[nodiscard]] inline HKEY ToHKey(RootKey root) {
        switch (root) {
            case RootKey::ClassesRoot:   return HKEY_CLASSES_ROOT;
            case RootKey::CurrentUser:   return HKEY_CURRENT_USER;
            case RootKey::LocalMachine:  return HKEY_LOCAL_MACHINE;
            case RootKey::Users:         return HKEY_USERS;
            case RootKey::CurrentConfig: return HKEY_CURRENT_CONFIG;
            default:                     return nullptr;
        }
    }

    // Convert RootKey to string
    [[nodiscard]] inline String ToString(RootKey root) {
        switch (root) {
            case RootKey::ClassesRoot:   return L"HKEY_CLASSES_ROOT";
            case RootKey::CurrentUser:   return L"HKEY_CURRENT_USER";
            case RootKey::LocalMachine:  return L"HKEY_LOCAL_MACHINE";
            case RootKey::Users:         return L"HKEY_USERS";
            case RootKey::CurrentConfig: return L"HKEY_CURRENT_CONFIG";
            default:                     return L"UNKNOWN";
        }
    }

    // RAII Registry Key Wrapper
    class RegistryKey {
    public:
        // Default constructor (invalid key)
        RegistryKey() noexcept = default;

        // Move constructor
        RegistryKey(RegistryKey&& other) noexcept
            : m_hKey(std::exchange(other.m_hKey, nullptr))
            , m_path(std::move(other.m_path))
            , m_ownsHandle(std::exchange(other.m_ownsHandle, false)) {}

        // Move assignment
        RegistryKey& operator=(RegistryKey&& other) noexcept {
            if (this != &other) {
                Close();
                m_hKey = std::exchange(other.m_hKey, nullptr);
                m_path = std::move(other.m_path);
                m_ownsHandle = std::exchange(other.m_ownsHandle, false);
            }
            return *this;
        }

        // No copy
        RegistryKey(const RegistryKey&) = delete;
        RegistryKey& operator=(const RegistryKey&) = delete;

        // Destructor
        ~RegistryKey() { Close(); }

        // Open an existing key
        [[nodiscard]] static RegistryResult<RegistryKey> Open(
            RootKey root,
            StringView subKey,
            REGSAM access = KEY_READ
        );

        [[nodiscard]] static RegistryResult<RegistryKey> Open(
            HKEY parentKey,
            StringView subKey,
            StringView parentPath,
            REGSAM access = KEY_READ
        );

        // Create or open a key
        [[nodiscard]] static RegistryResult<RegistryKey> Create(
            RootKey root,
            StringView subKey,
            REGSAM access = KEY_ALL_ACCESS
        );

        // Close the key
        void Close() noexcept;

        // Check if valid
        [[nodiscard]] bool IsValid() const noexcept { return m_hKey != nullptr; }
        [[nodiscard]] explicit operator bool() const noexcept { return IsValid(); }

        // Get handle
        [[nodiscard]] HKEY Handle() const noexcept { return m_hKey; }

        // Get path
        [[nodiscard]] const String& Path() const noexcept { return m_path; }

        // Enumerate subkeys
        [[nodiscard]] RegistryResult<std::vector<String>> EnumerateSubKeys() const;

        // Enumerate values
        [[nodiscard]] RegistryResult<std::vector<RegistryValue>> EnumerateValues() const;

        // Get a specific value
        [[nodiscard]] RegistryResult<RegistryValue> GetValue(StringView valueName) const;

        // Set a value
        [[nodiscard]] RegistryResult<void> SetValue(const RegistryValue& value);

        // Delete a value
        [[nodiscard]] RegistryResult<void> DeleteValue(StringView valueName);

        // Delete a subkey (must be empty)
        [[nodiscard]] RegistryResult<void> DeleteSubKey(StringView subKeyName);

        // Delete a subkey tree (recursive)
        [[nodiscard]] RegistryResult<void> DeleteSubKeyTree(StringView subKeyName);

        // Check if subkey exists
        [[nodiscard]] bool SubKeyExists(StringView subKeyName) const;

        // Check if value exists
        [[nodiscard]] bool ValueExists(StringView valueName) const;

        // Get subkey count
        [[nodiscard]] RegistryResult<DWORD> GetSubKeyCount() const;

        // Get value count
        [[nodiscard]] RegistryResult<DWORD> GetValueCount() const;

    private:
        RegistryKey(HKEY hKey, String path, bool ownsHandle = true) noexcept
            : m_hKey(hKey), m_path(std::move(path)), m_ownsHandle(ownsHandle) {}

        [[nodiscard]] static RegistryError MakeError(LSTATUS code, StringView keyPath);

        HKEY m_hKey = nullptr;
        String m_path;
        bool m_ownsHandle = false;
    };

} // namespace RegistryCleaner::Registry
