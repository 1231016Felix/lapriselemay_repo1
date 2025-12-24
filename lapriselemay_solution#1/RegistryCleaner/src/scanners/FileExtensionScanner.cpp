// FileExtensionScanner.cpp - Invalid file extension scanner
#include "pch.h"
#include "scanners/FileExtensionScanner.h"
#include "registry/RegistryUtils.h"
#include "core/ProtectedKeys.h"

namespace RegistryCleaner::Scanners {

    using namespace Registry;
    using namespace Registry::Utils;

    // Common extensions that should always exist
    const std::unordered_set<String> SYSTEM_EXTENSIONS = {
        L".exe", L".dll", L".bat", L".cmd", L".com", L".lnk", L".msi",
        L".txt", L".doc", L".docx", L".pdf", L".jpg", L".png", L".gif",
        L".htm", L".html", L".xml", L".zip", L".rar", L".7z"
    };

    FileExtensionScanner::FileExtensionScanner()
        : BaseScanner(IssueCategory::FileExtension, L"Extensions de fichiers invalides") {}

    std::vector<RegistryIssue> FileExtensionScanner::Scan(const ProgressCallback& progress) {
        std::vector<RegistryIssue> issues;

        auto rootResult = RegistryKey::Open(RootKey::ClassesRoot, L"", KEY_READ);
        if (!rootResult) return issues;

        auto& rootKey = *rootResult;
        auto subKeysResult = rootKey.EnumerateSubKeys();
        if (!subKeysResult) return issues;

        for (const auto& subKeyName : *subKeysResult) {
            // Only check extension keys (start with '.')
            if (subKeyName.empty() || subKeyName[0] != L'.') continue;

            // Skip system extensions
            String lowerExt = subKeyName;
            ranges::transform(lowerExt, lowerExt.begin(), ::towlower);
            if (SYSTEM_EXTENSIONS.contains(lowerExt)) continue;

            String keyPath = std::format(L"HKEY_CLASSES_ROOT\\{}", subKeyName);
            ReportProgress(progress, keyPath, issues.size());

            // Skip protected keys
            if (ProtectedKeys::IsProtectedKey(keyPath)) continue;

            auto extKeyResult = RegistryKey::Open(
                rootKey.Handle(), subKeyName, rootKey.Path(), KEY_READ);
            
            if (!extKeyResult) continue;

            auto issue = CheckExtension(*extKeyResult, subKeyName, keyPath);
            if (issue) {
                issues.push_back(std::move(*issue));
            }
        }

        return issues;
    }

    std::optional<RegistryIssue> FileExtensionScanner::CheckExtension(
        const RegistryKey& key,
        StringView extension,
        StringView keyPath
    ) const {
        // Get the default value (ProgID)
        auto defaultValue = key.GetValue(L"");
        if (!defaultValue || !defaultValue->IsString()) {
            return std::nullopt; // No ProgID, might be okay
        }

        const String& progId = defaultValue->AsString();
        if (progId.empty()) return std::nullopt;

        // Check if the ProgID exists
        if (!IsValidProgId(progId)) {
            return CreateIssue(
                keyPath,
                L"(Default)",
                std::format(L"Extension {} pointe vers ProgID inexistant", extension),
                std::format(L"ProgID manquant: {}", progId),
                Severity::Medium,
                true
            );
        }

        // Check if the open command exists and is valid
        if (!IsValidOpenCommand(progId)) {
            return CreateIssue(
                keyPath,
                L"(Default)",
                std::format(L"Extension {} - commande d'ouverture invalide", extension),
                std::format(L"ProgID: {} - shell\\open\\command invalide", progId),
                Severity::Low,
                true
            );
        }

        return std::nullopt;
    }

    bool FileExtensionScanner::IsValidProgId(StringView progId) const {
        auto keyResult = RegistryKey::Open(RootKey::ClassesRoot, progId, KEY_READ);
        return keyResult.has_value();
    }

    bool FileExtensionScanner::IsValidOpenCommand(StringView progId) const {
        String commandPath = std::format(L"{}\\shell\\open\\command", progId);
        
        auto keyResult = RegistryKey::Open(RootKey::ClassesRoot, commandPath, KEY_READ);
        if (!keyResult) return true; // No open command is okay

        auto& key = *keyResult;
        auto defaultValue = key.GetValue(L"");
        if (!defaultValue || !defaultValue->IsString()) return true;

        auto filePath = ExtractFilePath(defaultValue->AsString());
        if (!filePath) return true;

        return FileExists(*filePath);
    }

} // namespace RegistryCleaner::Scanners
