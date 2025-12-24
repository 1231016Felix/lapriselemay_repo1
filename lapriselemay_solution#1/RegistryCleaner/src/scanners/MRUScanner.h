// MRUScanner.h - Scan for MRU (Most Recently Used) entries
#pragma once

#include "scanners/BaseScanner.h"

namespace RegistryCleaner::Scanners {

    class MRUScanner : public BaseScanner {
    public:
        MRUScanner();
        
        [[nodiscard]] std::vector<RegistryIssue> Scan(
            const ProgressCallback& progress = nullptr) override;

    private:
        void ScanMRUPath(
            RootKey root,
            StringView path,
            std::vector<RegistryIssue>& issues,
            const ProgressCallback& progress) const;
    };

} // namespace RegistryCleaner::Scanners
