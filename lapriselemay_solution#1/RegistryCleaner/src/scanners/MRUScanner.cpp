// MRUScanner.cpp - MRU entries scanner
#include "pch.h"
#include "scanners/MRUScanner.h"
#include "registry/RegistryUtils.h"

namespace RegistryCleaner::Scanners {

    using namespace Registry;
    using namespace Registry::Utils;

    // MRU paths to scan
    const std::vector<std::pair<RootKey, StringView>> MRU_PATHS = {
        { RootKey::CurrentUser, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\ComDlg32\\OpenSaveMRU" },
        { RootKey::CurrentUser, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\ComDlg32\\LastVisitedPidlMRU" },
        { RootKey::CurrentUser, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\ComDlg32\\LastVisitedPidlMRULegacy" },
        { RootKey::CurrentUser, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\RecentDocs" },
        { RootKey::CurrentUser, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\RunMRU" },
        { RootKey::CurrentUser, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\TypedPaths" },
        { RootKey::CurrentUser, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\ComDlg32\\CIDSizeMRU" },
        { RootKey::CurrentUser, L"SOFTWARE\\Microsoft\\Office" }, // Office MRU (will scan subkeys)
    };

    MRUScanner::MRUScanner()
        : BaseScanner(IssueCategory::MRUEntry, L"Entrées MRU (fichiers récents)") {}

    std::vector<RegistryIssue> MRUScanner::Scan(const ProgressCallback& progress) {
        std::vector<RegistryIssue> issues;

        for (const auto& [root, path] : MRU_PATHS) {
            ScanMRUPath(root, path, issues, progress);
        }

        return issues;
    }

    void MRUScanner::ScanMRUPath(
        RootKey root,
        StringView path,
        std::vector<RegistryIssue>& issues,
        const ProgressCallback& progress
    ) const {
        String fullPath = std::format(L"{}\\{}", ToString(root), path);
        ReportProgress(progress, fullPath, issues.size());

        auto keyResult = RegistryKey::Open(root, path, KEY_READ);
        if (!keyResult) return;

        auto& key = *keyResult;

        // Get values in this key
        auto valuesResult = key.EnumerateValues();
        if (valuesResult) {
            size_t mruCount = 0;
            for (const auto& value : *valuesResult) {
                // Skip MRUList/MRUListEx ordering values
                if (value.Name() == L"MRUList" || value.Name() == L"MRUListEx") continue;
                
                // Count MRU entries
                if (value.IsString() || value.IsBinary()) {
                    mruCount++;
                }
            }

            // Report if there are many MRU entries (privacy concern)
            if (mruCount > 10) {
                issues.push_back(CreateIssue(
                    fullPath,
                    L"",
                    std::format(L"{} entrées de fichiers récents", mruCount),
                    L"Ces entrées contiennent l'historique de vos fichiers récemment utilisés",
                    Severity::Low,
                    false
                ));
            }
        }

        // Recursively scan subkeys
        auto subKeysResult = key.EnumerateSubKeys();
        if (subKeysResult) {
            for (const auto& subKeyName : *subKeysResult) {
                String subPath = std::format(L"{}\\{}", path, subKeyName);
                
                // Look for "File MRU" or similar in Office paths
                if (subKeyName.find(L"MRU") != String::npos || 
                    subKeyName.find(L"Recent") != String::npos) {
                    ScanMRUPath(root, subPath, issues, progress);
                }
                // Scan one level deep for other paths
                else if (path.find(L"Office") != StringView::npos) {
                    auto subKeyResult = RegistryKey::Open(key.Handle(), subKeyName, key.Path(), KEY_READ);
                    if (subKeyResult) {
                        auto subSubKeys = subKeyResult->EnumerateSubKeys();
                        if (subSubKeys) {
                            for (const auto& ssk : *subSubKeys) {
                                if (ssk.find(L"MRU") != String::npos || 
                                    ssk.find(L"Recent") != String::npos) {
                                    String subSubPath = std::format(L"{}\\{}", subPath, ssk);
                                    ScanMRUPath(root, subSubPath, issues, progress);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

} // namespace RegistryCleaner::Scanners
