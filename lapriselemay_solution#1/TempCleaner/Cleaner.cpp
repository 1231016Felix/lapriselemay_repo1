#include "Cleaner.h"
#include <Windows.h>
#include <ShlObj.h>
#include <shellapi.h>
#include <winevt.h>
#include <fstream>

#pragma comment(lib, "wevtapi.lib")

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

        std::vector<std::pair<std::wstring, std::function<void()>>> tasks;
        
        // Nettoyage de base
        if (options.cleanUserTemp) {
            tasks.emplace_back(L"Temp utilisateur", [&]() {
                cleanDirectory(getUserTempPath(), stats, progressCallback, L"Temp utilisateur");
            });
        }
        if (options.cleanWindowsTemp) {
            tasks.emplace_back(L"Temp Windows", [&]() {
                cleanDirectory(getWindowsTempPath(), stats, progressCallback, L"Temp Windows");
            });
        }
        if (options.cleanPrefetch) {
            tasks.emplace_back(L"Prefetch", [&]() {
                cleanDirectory(getPrefetchPath(), stats, progressCallback, L"Prefetch");
            });
        }
        if (options.cleanRecent) {
            tasks.emplace_back(L"Fichiers recents", [&]() {
                cleanDirectory(getRecentPath(), stats, progressCallback, L"Fichiers recents");
            });
        }
        if (options.cleanBrowserCache) {
            tasks.emplace_back(L"Cache navigateurs", [&]() {
                for (const auto& path : getBrowserCachePaths()) {
                    if (m_stopRequested) break;
                    if (fs::exists(path)) {
                        cleanDirectory(path, stats, progressCallback, L"Cache navigateurs");
                    }
                }
            });
        }
        
        // Nettoyage système avancé
        if (options.cleanWindowsUpdate) {
            tasks.emplace_back(L"Cache Windows Update", [&]() {
                cleanDirectory(getWindowsUpdateCachePath(), stats, progressCallback, L"Cache Windows Update");
            });
        }
        if (options.cleanSystemLogs) {
            tasks.emplace_back(L"Logs systeme", [&]() {
                for (const auto& path : getSystemLogPaths()) {
                    if (m_stopRequested) break;
                    if (fs::exists(path)) {
                        cleanDirectory(path, stats, progressCallback, L"Logs systeme");
                    }
                }
                cleanEventLogs(stats);
            });
        }
        if (options.cleanCrashDumps) {
            tasks.emplace_back(L"Crash dumps", [&]() {
                for (const auto& path : getCrashDumpPaths()) {
                    if (m_stopRequested) break;
                    if (fs::exists(path)) {
                        cleanDirectory(path, stats, progressCallback, L"Crash dumps");
                    }
                }
            });
        }
        if (options.cleanThumbnails) {
            tasks.emplace_back(L"Cache miniatures", [&]() {
                cleanDirectory(getThumbnailCachePath(), stats, progressCallback, L"Cache miniatures");
            });
        }
        if (options.cleanDeliveryOptimization) {
            tasks.emplace_back(L"Delivery Optimization", [&]() {
                cleanDirectory(getDeliveryOptimizationPath(), stats, progressCallback, L"Delivery Optimization");
            });
        }
        if (options.cleanWindowsInstaller) {
            tasks.emplace_back(L"Windows Installer cache", [&]() {
                auto path = getWindowsInstallerPatchPath();
                if (fs::exists(path)) {
                    cleanDirectory(path, stats, progressCallback, L"Windows Installer cache");
                }
            });
        }
        if (options.cleanFontCache) {
            tasks.emplace_back(L"Cache polices", [&]() {
                cleanDirectory(getFontCachePath(), stats, progressCallback, L"Cache polices");
            });
        }
        
        // Corbeille en dernier
        if (options.cleanRecycleBin) {
            tasks.emplace_back(L"Corbeille", [&]() {
                cleanRecycleBin(stats);
            });
        }

        int totalTasks = static_cast<int>(tasks.size());
        int currentTask = 0;

        for (const auto& [name, action] : tasks) {
            if (m_stopRequested) break;
            
            if (progressCallback) {
                int progress = totalTasks > 0 ? (currentTask * 100) / totalTasks : 0;
                progressCallback(L"Nettoyage: " + name, progress);
            }
            
            action();
            currentTask++;
        }

        if (progressCallback) {
            progressCallback(L"Termine!", 100);
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

    // Fonction helper pour obtenir le message d'erreur Windows
    std::wstring GetWindowsErrorMessage(DWORD errorCode) {
        if (errorCode == 0) return L"Succes";
        
        LPWSTR messageBuffer = nullptr;
        size_t size = FormatMessageW(
            FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
            nullptr, errorCode, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
            (LPWSTR)&messageBuffer, 0, nullptr);
        
        std::wstring message(messageBuffer, size);
        LocalFree(messageBuffer);
        
        // Supprimer les retours à la ligne en fin de message
        while (!message.empty() && (message.back() == L'\n' || message.back() == L'\r')) {
            message.pop_back();
        }
        
        return message;
    }

    std::wstring GetErrorCodeMessage(const std::error_code& ec) {
        if (!ec) return L"";
        
        // Convertir le message d'erreur en wstring
        std::string msg = ec.message();
        std::wstring wmsg(msg.begin(), msg.end());
        return wmsg;
    }

    void Cleaner::cleanDirectory(const fs::path& path, CleaningStats& stats, ProgressCallback, const std::wstring& category) {
        if (!fs::exists(path)) return;
        
        std::error_code ec;
        
        for (const auto& entry : fs::recursive_directory_iterator(path, 
            fs::directory_options::skip_permission_denied, ec)) {
            
            if (m_stopRequested) return;
            
            try {
                if (entry.is_regular_file()) {
                    auto fileSize = entry.file_size(ec);
                    if (!ec && fs::remove(entry.path(), ec)) {
                        stats.filesDeleted++;
                        stats.bytesFreed += fileSize;
                    } else {
                        stats.errors++;
                        ErrorInfo error;
                        error.filePath = entry.path().wstring();
                        error.category = category;
                        if (ec) {
                            error.errorMessage = GetErrorCodeMessage(ec);
                        } else {
                            error.errorMessage = GetWindowsErrorMessage(GetLastError());
                        }
                        if (error.errorMessage.empty()) {
                            error.errorMessage = L"Erreur inconnue lors de la suppression";
                        }
                        stats.errorDetails.push_back(error);
                    }
                }
            } catch (const std::exception& e) {
                stats.errors++;
                ErrorInfo error;
                error.filePath = entry.path().wstring();
                error.category = category;
                std::string what = e.what();
                error.errorMessage = std::wstring(what.begin(), what.end());
                stats.errorDetails.push_back(error);
            } catch (...) {
                stats.errors++;
                ErrorInfo error;
                error.filePath = entry.path().wstring();
                error.category = category;
                error.errorMessage = L"Exception inconnue";
                stats.errorDetails.push_back(error);
            }
        }
        
        // Supprimer les dossiers vides (collecter d'abord, puis supprimer)
        try {
            std::vector<fs::path> emptyDirs;
            for (const auto& entry : fs::recursive_directory_iterator(path, 
                fs::directory_options::skip_permission_denied, ec)) {
                if (m_stopRequested) return;
                try {
                    if (entry.is_directory() && fs::is_empty(entry.path(), ec)) {
                        emptyDirs.push_back(entry.path());
                    }
                } catch (...) {}
            }
            // Supprimer en ordre inverse (les sous-dossiers d'abord)
            for (auto it = emptyDirs.rbegin(); it != emptyDirs.rend(); ++it) {
                if (m_stopRequested) return;
                fs::remove(*it, ec);
            }
        } catch (...) {
            // Ignorer les erreurs d'iteration
        }
    }

    void Cleaner::cleanDirectoryContents(const fs::path& path, CleaningStats& stats, const std::wstring& category) {
        if (!fs::exists(path)) return;
        
        std::error_code ec;
        for (const auto& entry : fs::directory_iterator(path, 
            fs::directory_options::skip_permission_denied, ec)) {
            if (m_stopRequested) return;
            try {
                auto size = entry.is_regular_file() ? entry.file_size(ec) : 0;
                if (fs::remove_all(entry.path(), ec) > 0) {
                    stats.filesDeleted++;
                    stats.bytesFreed += size;
                } else if (ec) {
                    stats.errors++;
                    ErrorInfo error;
                    error.filePath = entry.path().wstring();
                    error.category = category;
                    error.errorMessage = GetErrorCodeMessage(ec);
                    stats.errorDetails.push_back(error);
                }
            } catch (const std::exception& e) {
                stats.errors++;
                ErrorInfo error;
                error.filePath = entry.path().wstring();
                error.category = category;
                std::string what = e.what();
                error.errorMessage = std::wstring(what.begin(), what.end());
                stats.errorDetails.push_back(error);
            } catch (...) {
                stats.errors++;
                ErrorInfo error;
                error.filePath = entry.path().wstring();
                error.category = category;
                error.errorMessage = L"Exception inconnue";
                stats.errorDetails.push_back(error);
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

    void Cleaner::cleanEventLogs(CleaningStats& stats) {
        // Nettoyer les Event Logs principaux
        const wchar_t* logs[] = { L"Application", L"System", L"Security", L"Setup" };
        
        for (const auto& logName : logs) {
            if (m_stopRequested) return;
            
            EVT_HANDLE hLog = EvtOpenLog(nullptr, logName, EvtOpenChannelPath);
            if (hLog) {
                if (EvtClearLog(nullptr, logName, nullptr, 0)) {
                    stats.filesDeleted++;
                }
                EvtClose(hLog);
            }
        }
    }

    // Chemins de base
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

    // Chemins avancés
    fs::path Cleaner::getWindowsUpdateCachePath() const {
        wchar_t winDir[MAX_PATH];
        GetWindowsDirectoryW(winDir, MAX_PATH);
        return fs::path(winDir) / L"SoftwareDistribution" / L"Download";
    }

    fs::path Cleaner::getDeliveryOptimizationPath() const {
        wchar_t winDir[MAX_PATH];
        GetWindowsDirectoryW(winDir, MAX_PATH);
        return fs::path(winDir) / L"ServiceProfiles" / L"NetworkService" / L"AppData" / L"Local" / L"Microsoft" / L"Windows" / L"DeliveryOptimization" / L"Cache";
    }

    fs::path Cleaner::getThumbnailCachePath() const {
        wchar_t localAppData[MAX_PATH];
        if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_LOCAL_APPDATA, nullptr, 0, localAppData))) {
            return fs::path(localAppData) / L"Microsoft" / L"Windows" / L"Explorer";
        }
        return {};
    }

    fs::path Cleaner::getWindowsInstallerPatchPath() const {
        wchar_t winDir[MAX_PATH];
        GetWindowsDirectoryW(winDir, MAX_PATH);
        return fs::path(winDir) / L"Installer" / L"$PatchCache$";
    }

    fs::path Cleaner::getFontCachePath() const {
        wchar_t winDir[MAX_PATH];
        GetWindowsDirectoryW(winDir, MAX_PATH);
        return fs::path(winDir) / L"ServiceProfiles" / L"LocalService" / L"AppData" / L"Local" / L"FontCache";
    }

    std::vector<fs::path> Cleaner::getSystemLogPaths() const {
        std::vector<fs::path> paths;
        wchar_t winDir[MAX_PATH];
        GetWindowsDirectoryW(winDir, MAX_PATH);
        fs::path winPath(winDir);
        
        // Logs CBS
        paths.push_back(winPath / L"Logs" / L"CBS");
        // Logs DISM
        paths.push_back(winPath / L"Logs" / L"DISM");
        // Logs Windows Update
        paths.push_back(winPath / L"Logs" / L"WindowsUpdate");
        // Logs SIH (Service Initiated Healing)
        paths.push_back(winPath / L"Logs" / L"SIH");
        // Panther (installation logs)
        paths.push_back(winPath / L"Panther");
        // Memory dumps log
        paths.push_back(winPath / L"LiveKernelReports");
        
        return paths;
    }

    std::vector<fs::path> Cleaner::getCrashDumpPaths() const {
        std::vector<fs::path> paths;
        wchar_t winDir[MAX_PATH];
        GetWindowsDirectoryW(winDir, MAX_PATH);
        fs::path winPath(winDir);
        
        // Minidumps
        paths.push_back(winPath / L"Minidump");
        
        // Memory.dmp
        paths.push_back(winPath / L"MEMORY.DMP");
        
        // Local crash dumps
        wchar_t localAppData[MAX_PATH];
        if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_LOCAL_APPDATA, nullptr, 0, localAppData))) {
            paths.push_back(fs::path(localAppData) / L"CrashDumps");
            paths.push_back(fs::path(localAppData) / L"Microsoft" / L"Windows" / L"WER");
        }
        
        // ProgramData crash reports
        wchar_t programData[MAX_PATH];
        if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_COMMON_APPDATA, nullptr, 0, programData))) {
            paths.push_back(fs::path(programData) / L"Microsoft" / L"Windows" / L"WER");
        }
        
        return paths;
    }

    std::vector<fs::path> Cleaner::getBrowserCachePaths() const {
        std::vector<fs::path> paths;
        wchar_t localAppData[MAX_PATH];
        
        if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_LOCAL_APPDATA, nullptr, 0, localAppData))) {
            fs::path base(localAppData);
            
            // Chrome
            paths.push_back(base / L"Google" / L"Chrome" / L"User Data" / L"Default" / L"Cache");
            paths.push_back(base / L"Google" / L"Chrome" / L"User Data" / L"Default" / L"Code Cache");
            paths.push_back(base / L"Google" / L"Chrome" / L"User Data" / L"Default" / L"GPUCache");
            
            // Edge
            paths.push_back(base / L"Microsoft" / L"Edge" / L"User Data" / L"Default" / L"Cache");
            paths.push_back(base / L"Microsoft" / L"Edge" / L"User Data" / L"Default" / L"Code Cache");
            paths.push_back(base / L"Microsoft" / L"Edge" / L"User Data" / L"Default" / L"GPUCache");
            
            // Firefox
            wchar_t appData[MAX_PATH];
            if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_APPDATA, nullptr, 0, appData))) {
                fs::path firefoxProfiles = fs::path(appData) / L"Mozilla" / L"Firefox" / L"Profiles";
                if (fs::exists(firefoxProfiles)) {
                    std::error_code ec;
                    for (const auto& profile : fs::directory_iterator(firefoxProfiles, ec)) {
                        if (profile.is_directory()) {
                            paths.push_back(profile.path() / L"cache2");
                        }
                    }
                }
            }
            
            // Brave
            paths.push_back(base / L"BraveSoftware" / L"Brave-Browser" / L"User Data" / L"Default" / L"Cache");
            
            // Opera
            paths.push_back(base / L"Opera Software" / L"Opera Stable" / L"Cache");
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
            // Base
            file << L"UserTemp=" << (options.cleanUserTemp ? 1 : 0) << L"\n";
            file << L"WindowsTemp=" << (options.cleanWindowsTemp ? 1 : 0) << L"\n";
            file << L"Prefetch=" << (options.cleanPrefetch ? 1 : 0) << L"\n";
            file << L"Recent=" << (options.cleanRecent ? 1 : 0) << L"\n";
            file << L"RecycleBin=" << (options.cleanRecycleBin ? 1 : 0) << L"\n";
            file << L"BrowserCache=" << (options.cleanBrowserCache ? 1 : 0) << L"\n";
            // Avancé
            file << L"WindowsUpdate=" << (options.cleanWindowsUpdate ? 1 : 0) << L"\n";
            file << L"SystemLogs=" << (options.cleanSystemLogs ? 1 : 0) << L"\n";
            file << L"CrashDumps=" << (options.cleanCrashDumps ? 1 : 0) << L"\n";
            file << L"Thumbnails=" << (options.cleanThumbnails ? 1 : 0) << L"\n";
            file << L"DeliveryOptimization=" << (options.cleanDeliveryOptimization ? 1 : 0) << L"\n";
            file << L"WindowsInstaller=" << (options.cleanWindowsInstaller ? 1 : 0) << L"\n";
            file << L"FontCache=" << (options.cleanFontCache ? 1 : 0) << L"\n";
        }
    }

    CleaningOptions Cleaner::loadOptions() {
        CleaningOptions options;
        std::wstring configPath = getConfigPath();
        
        wchar_t buffer[16];
        auto readBool = [&](const wchar_t* key, bool defaultVal = false) -> bool {
            GetPrivateProfileStringW(L"Options", key, defaultVal ? L"1" : L"0", buffer, 16, configPath.c_str());
            return buffer[0] == L'1';
        };
        
        // Base
        options.cleanUserTemp = readBool(L"UserTemp", true);
        options.cleanWindowsTemp = readBool(L"WindowsTemp", true);
        options.cleanPrefetch = readBool(L"Prefetch");
        options.cleanRecent = readBool(L"Recent");
        options.cleanRecycleBin = readBool(L"RecycleBin");
        options.cleanBrowserCache = readBool(L"BrowserCache");
        // Avancé
        options.cleanWindowsUpdate = readBool(L"WindowsUpdate");
        options.cleanSystemLogs = readBool(L"SystemLogs");
        options.cleanCrashDumps = readBool(L"CrashDumps");
        options.cleanThumbnails = readBool(L"Thumbnails");
        options.cleanDeliveryOptimization = readBool(L"DeliveryOptimization");
        options.cleanWindowsInstaller = readBool(L"WindowsInstaller");
        options.cleanFontCache = readBool(L"FontCache");
        
        return options;
    }

} // namespace TempCleaner
