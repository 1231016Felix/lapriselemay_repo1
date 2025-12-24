// UninstallScanner.h - Scan for orphaned uninstall entries
#pragma once

#include "scanners/BaseScanner.h"

namespace RegistryCleaner::Scanners {

    class UninstallScanner : public BaseScanner {
    public:
        UninstallScanner();
        
        [[nodiscard]] std::vector<RegistryIssue> Scan(
            const ProgressCallback& progress = nullptr) override;

    private:
        [[nodiscard]] bool IsValidUninstallEntry(const RegistryKey& key) const;
        [[nodiscard]] std::optional<RegistryIssue> CheckUninstallKey(
            const RegistryKey& key,
            StringView keyPath) const;
    };

} // namespace RegistryCleaner::Scanners
