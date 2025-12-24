// FileExtensionScanner.h - Scan for invalid file extension handlers
#pragma once

#include "scanners/BaseScanner.h"

namespace RegistryCleaner::Scanners {

    class FileExtensionScanner : public BaseScanner {
    public:
        FileExtensionScanner();
        
        [[nodiscard]] std::vector<RegistryIssue> Scan(
            const ProgressCallback& progress = nullptr) override;

    private:
        [[nodiscard]] std::optional<RegistryIssue> CheckExtension(
            const RegistryKey& key,
            StringView extension,
            StringView keyPath) const;
            
        [[nodiscard]] bool IsValidProgId(StringView progId) const;
        [[nodiscard]] bool IsValidOpenCommand(StringView progId) const;
    };

} // namespace RegistryCleaner::Scanners
