// SharedDllScanner.cpp - Orphaned SharedDLL scanner
#include "pch.h"
#include "scanners/SharedDllScanner.h"
#include "registry/RegistryUtils.h"
#include "core/ProtectedKeys.h"

namespace RegistryCleaner::Scanners {

    using namespace Registry;
    using namespace Registry::Utils;

    constexpr StringView SHARED_DLLS_PATH = L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\SharedDLLs";

    SharedDllScanner::SharedDllScanner()
        : BaseScanner(IssueCategory::SharedDll, L"DLLs partagées orphelines") {}

    std::vector<RegistryIssue> SharedDllScanner::Scan(const ProgressCallback& progress) {
        std::vector<RegistryIssue> issues;

        String fullPath = std::format(L"HKEY_LOCAL_MACHINE\\{}", SHARED_DLLS_PATH);
        ReportProgress(progress, fullPath, issues.size());

        auto keyResult = RegistryKey::Open(RootKey::LocalMachine, SHARED_DLLS_PATH, KEY_READ);
        if (!keyResult) return issues;

        auto& key = *keyResult;
        auto valuesResult = key.EnumerateValues();
        if (!valuesResult) return issues;

        for (const auto& value : *valuesResult) {
            ReportProgress(progress, value.Name(), issues.size());

            // Value name is the DLL path, value data is reference count
            const String& dllPath = value.Name();
            
            // Skip system DLLs
            if (ProtectedKeys::ContainsCriticalKeyword(dllPath)) continue;

            // Check if DLL exists
            if (!FileExists(dllPath)) {
                // Get reference count
                DWORD refCount = 0;
                if (value.IsDWord()) {
                    refCount = value.AsDWord();
                }

                issues.push_back(CreateIssue(
                    fullPath,
                    dllPath,
                    L"DLL partagée introuvable",
                    std::format(L"Chemin: {} (références: {})", dllPath, refCount),
                    Severity::Low,
                    true
                ));
            }
            // Check for zero reference count (orphaned)
            else if (value.IsDWord() && value.AsDWord() == 0) {
                issues.push_back(CreateIssue(
                    fullPath,
                    dllPath,
                    L"DLL partagée avec zéro références",
                    std::format(L"Chemin: {} (plus utilisée)", dllPath),
                    Severity::Low,
                    true
                ));
            }
        }

        return issues;
    }

} // namespace RegistryCleaner::Scanners
