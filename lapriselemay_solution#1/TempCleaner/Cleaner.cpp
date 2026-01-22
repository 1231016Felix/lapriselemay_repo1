#include "Cleaner.h"
#include <Windows.h>
#include <ShlObj.h>
#include <shellapi.h>
#include <fstream>

namespace fs = std::filesystem;

namespace TempCleaner {

    namespace {
        const wchar_t* CONFIG_FILE = L"TempCleaner.ini";
        
        std::wstring getConfigPath() {
            wchar_t path[MAX_PATH];
            if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_APPDATA, nullptr, 0, path))) {
                return std::wstring(path) + L"\\TempCleaner\\" + CONFIG_FILE;
            }
            return CONFIG_FILE;
        }
    }

    Cleaner::Cleaner() = default;

    CleaningStats Cleaner::clean(const CleaningOptions& options, ProgressCallback progressCallback) {
        CleaningStats stats;
        m_running = true;
        m_stopRequested = false;

        std::vector<std::pair<std::wstring, fs::path>> tasks;
        
        if (options.cleanUserTemp) {
            tasks.emplace_back(L"Temp utilisateur", getUserTempPath());
        }
        if (options.cleanWindowsTemp) {
            tasks.emplace_back(L"Temp Windows", getWindowsTempPath());
        }
        if (options.cleanPrefetch) {
            tasks.emplace_back(L"Prefetch", getPrefetchPath());
        }
        if (options.cleanRecent) {
            tasks.emplace_back(L"Fichiers récents", getRecentPath());
        }
        if (options.cleanBrowserCache) {
            for (const auto& path : getBrowserCachePaths()) {
                tasks.emplace_back(L"Cache navigateur", path);
            }
        }

        int totalTasks = static_cast<int>(tasks.size()) + (options.cleanRecycleBin ? 1 : 0);
        int currentTask = 0;

        for (const auto& [name, path] : tasks) {
            if (m_stopRequested) break;
            
            if (progressCallback) {
                int progress = (currentTask * 100) / totalTasks;
                progressCallback(L"Nettoyage: " + name, progress);
            }
            
            if (fs::exists(path)) {
                cleanDirectory(path, stats, progressCallback);
            }
            currentTask++;
        }

        if (options.cleanRecycleBin && !m_stopRequested) {
            if (progressCallback) {
                progressCallback(L"Vidage de la corbeille...", 95);
            }
            cleanRecycleBin(stats);
        }

        if (progressCallback) {
            progressCallback(L"Terminé!", 100);
        }

        m_running = false;
        return stats;
    }

    void Cleaner::stop() {
        m_stopRequested = true;
    }

    bool Cleaner::isRunning() const {
        return m_running;
    }

    void Cleaner::cleanDirectory(const fs::path& path, CleaningStats& stats, ProgressCallback callback) {
        std::error_code ec;
        
        for (const auto& entry : fs::recursive_directory_iterator(path, 
            fs::directory_options::skip_permission_denied, ec)) {
            
            if (m_stopRequested) return;
            
            try {
                if (entry.is_regular_file()) {
                    auto fileSize = entry.file_size();
                    if (fs::remove(entry.path(), ec)) {
                        stats.filesDeleted++;
                        stats.bytesFreed += fileSize;
                    } else {
                        stats.errors++;
                    }
                }
            } catch (...) {
                stats.errors++;
            }
        }
    }

    void Cleaner::cleanRecycleBin(CleaningStats& stats) {
        SHQUERYRBINFO rbInfo = { sizeof(SHQUERYRBINFO) };
        if (SUCCEEDED(SHQueryRecycleBinW(nullptr, &rbInfo))) {
            stats.bytesFreed += rbInfo.i64Size;
            stats.filesDeleted += static_cast<uint64_t>(rbInfo.i64NumItems);
        }
        
        if (FAILED(SHEmptyRecycleBinW(nullptr, nullptr, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND))) {
            stats.errors++;
        }
    }

    fs::path Cleaner::getUserTempPath() const {
        wchar_t path[MAX_PATH];
        GetTempPathW(MAX_PATH, path);
        return path;
    }

    fs::path Cleaner::getWindowsTempPath() const {
        wchar_t winDir[MAX_PATH];
        GetWindowsDirectoryW(winDir, MAX_PATH);
        return fs::path(winDir) / L"Temp";
    }

    fs::path Cleaner::getPrefetchPath() const {
        wchar_t winDir[MAX_PATH];
        GetWindowsDirectoryW(winDir, MAX_PATH);
        return fs::path(winDir) / L"Prefetch";
    }

    fs::path Cleaner::getRecentPath() const {
        wchar_t path[MAX_PATH];
        if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_RECENT, nullptr, 0, path))) {
            return path;
        }
        return {};
    }

    std::vector<fs::path> Cleaner::getBrowserCachePaths() const {
        std::vector<fs::path> paths;
        wchar_t localAppData[MAX_PATH];
        
        if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_LOCAL_APPDATA, nullptr, 0, localAppData))) {
            fs::path base(localAppData);
            
            // Chrome
            paths.push_back(base / L"Google\\Chrome\\User Data\\Default\\Cache");
            // Edge
            paths.push_back(base / L"Microsoft\\Edge\\User Data\\Default\\Cache");
            // Firefox
            wchar_t appData[MAX_PATH];
            if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_APPDATA, nullptr, 0, appData))) {
                paths.push_back(fs::path(appData) / L"Mozilla\\Firefox\\Profiles");
            }
        }
        return paths;
    }

    void Cleaner::saveOptions(const CleaningOptions& options) {
        std::wstring configPath = getConfigPath();
        
        // Créer le dossier si nécessaire
        fs::create_directories(fs::path(configPath).parent_path());
        
        std::wofstream file(configPath);
        if (file.is_open()) {
            file << L"[Options]\n";
            file << L"UserTemp=" << (options.cleanUserTemp ? 1 : 0) << L"\n";
            file << L"WindowsTemp=" << (options.cleanWindowsTemp ? 1 : 0) << L"\n";
            file << L"Prefetch=" << (options.cleanPrefetch ? 1 : 0) << L"\n";
            file << L"Recent=" << (options.cleanRecent ? 1 : 0) << L"\n";
            file << L"RecycleBin=" << (options.cleanRecycleBin ? 1 : 0) << L"\n";
            file << L"BrowserCache=" << (options.cleanBrowserCache ? 1 : 0) << L"\n";
        }
    }

    CleaningOptions Cleaner::loadOptions() {
        CleaningOptions options;
        std::wstring configPath = getConfigPath();
        
        wchar_t buffer[16];
        auto readBool = [&](const wchar_t* key) -> bool {
            GetPrivateProfileStringW(L"Options", key, L"0", buffer, 16, configPath.c_str());
            return buffer[0] == L'1';
        };
        
        options.cleanUserTemp = readBool(L"UserTemp");
        options.cleanWindowsTemp = readBool(L"WindowsTemp");
        options.cleanPrefetch = readBool(L"Prefetch");
        options.cleanRecent = readBool(L"Recent");
        options.cleanRecycleBin = readBool(L"RecycleBin");
        options.cleanBrowserCache = readBool(L"BrowserCache");
        
        // Valeurs par défaut si fichier n'existe pas
        if (!fs::exists(configPath)) {
            options.cleanUserTemp = true;
            options.cleanWindowsTemp = true;
        }
        
        return options;
    }

} // namespace TempCleaner
