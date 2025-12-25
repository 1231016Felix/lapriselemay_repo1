// RegistryPermissions.h - Advanced registry permission handling
#pragma once

#include "pch.h"
#include "registry/RegistryKey.h"

namespace RegistryCleaner::Registry {

    class RegistryPermissions {
    public:
        // Take ownership of a registry key
        [[nodiscard]] static std::expected<void, String> TakeOwnership(
            RootKey root, StringView subKey);

        // Grant full control to Administrators
        [[nodiscard]] static std::expected<void, String> GrantFullControl(
            RootKey root, StringView subKey);

        // Force delete a protected key (takes ownership first)
        [[nodiscard]] static std::expected<void, String> ForceDeleteKey(
            RootKey root, StringView subKey);

        // Force delete a protected value
        [[nodiscard]] static std::expected<void, String> ForceDeleteValue(
            RootKey root, StringView subKey, StringView valueName);

        // Schedule key deletion on reboot (for locked keys)
        [[nodiscard]] static std::expected<void, String> ScheduleDeleteOnReboot(
            RootKey root, StringView subKey);

    private:
        // Enable a privilege for the current process
        [[nodiscard]] static bool EnablePrivilege(LPCWSTR privilegeName);

        // Get the SID for Administrators group
        [[nodiscard]] static PSID GetAdministratorsSid();

        // Get the SID for current user
        [[nodiscard]] static PSID GetCurrentUserSid();
    };

} // namespace RegistryCleaner::Registry
