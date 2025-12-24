// SharedDllScanner.h - Scan for orphaned SharedDLL entries
#pragma once

#include "scanners/BaseScanner.h"

namespace RegistryCleaner::Scanners {

    class SharedDllScanner : public BaseScanner {
    public:
        SharedDllScanner();
        
        [[nodiscard]] std::vector<RegistryIssue> Scan(
            const ProgressCallback& progress = nullptr) override;
    };

} // namespace RegistryCleaner::Scanners
