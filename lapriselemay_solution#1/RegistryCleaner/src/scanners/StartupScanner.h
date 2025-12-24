// StartupScanner.h - Scan for invalid startup entries
#pragma once

#include "scanners/BaseScanner.h"

namespace RegistryCleaner::Scanners {

    class StartupScanner : public BaseScanner {
    public:
        StartupScanner();
        
        [[nodiscard]] std::vector<RegistryIssue> Scan(
            const ProgressCallback& progress = nullptr) override;

    private:
        void ScanStartupPath(
            RootKey root,
            StringView path,
            std::vector<RegistryIssue>& issues,
            const ProgressCallback& progress) const;
    };

} // namespace RegistryCleaner::Scanners
