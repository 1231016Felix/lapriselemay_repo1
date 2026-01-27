#include "Cleaner.h"
#include <Windows.h>
#include <ShlObj.h>
#include <shellapi.h>
#include <winevt.h>
#include <shobjidl.h>
#include <objbase.h>
#include <fstream>
#include <sstream>

#pragma comment(lib, "wevtapi.lib")
#pragma comment(lib, "ole32.lib")

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
            
            while (!message.empty() && (message.back() == L'\n' || message.back() == L'\r')) {
                message.pop_back();
            }
            
            return message;
        }

        std::wstring GetErrorCodeMessage(const std::error_code& ec) {
            if (!ec) return L"";
            std::string msg = ec.message();
            std::wstring wmsg(msg.begin(), msg.end());
            return wmsg;
        }
        
        // Helper pour obtenir le chemin du profil utilisateur
        fs::path getUserProfilePath() {
            wchar_t path[MAX_PATH];
            if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_PROFILE, nullptr, 0, path))) {
                return path;
            }
            return {};
        }
        
        // Helper pour obtenir LocalAppData
        fs::path getLocalAppDataPath() {
            wchar_t path[MAX_PATH];
            if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_LOCAL_APPDATA, nullptr, 0, path))) {
                return path;
            }
            return {};
        }
        
        // Helper pour obtenir AppData (Roaming)
        fs::path getAppDataPath() {
            wchar_t path[MAX_PATH];
            if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_APPDATA, nullptr, 0, path))) {
                return path;
            }
            return {};
        }
    }

    Cleaner::Cleaner() = default;

    CleaningStats Cleaner::clean(const CleaningOptions& options, ProgressCallback progressCallback) {
        CleaningStats stats;
        m_running = true;
        m_stopRequested = false;

        std::vector<std::pair<std::wstring, std::function<void()>>> tasks;
        
        // === Nettoyage de base ===
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
        
        // === Nettoyage système avancé ===
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
        
        // === Options Windows supplémentaires ===
        if (options.cleanDnsCache) {
            tasks.emplace_back(L"Cache DNS", [&]() {
                flushDnsCache(stats);
            });
        }
        if (options.cleanBrokenShortcuts) {
            tasks.emplace_back(L"Raccourcis casses", [&]() {
                cleanBrokenShortcuts(stats);
            });
        }
        if (options.cleanWindowsOld) {
            tasks.emplace_back(L"Windows.old", [&]() {
                cleanWindowsOld(stats);
            });
        }
        if (options.cleanWindowsStoreCache) {
            tasks.emplace_back(L"Cache Windows Store", [&]() {
                cleanWindowsStoreCache(stats);
            });
        }
        if (options.cleanClipboard) {
            tasks.emplace_back(L"Presse-papiers", [&]() {
                clearClipboard(stats);
            });
        }
        if (options.cleanChkdskFiles) {
            tasks.emplace_back(L"Fichiers Chkdsk", [&]() {
                cleanChkdskFiles(stats);
            });
        }
        if (options.cleanNetworkCache) {
            tasks.emplace_back(L"Cache reseau", [&]() {
                cleanNetworkCache(stats);
            });
        }
        
        // === NOUVEAU: Caches de développement ===
        if (options.cleanNpmCache) {
            tasks.emplace_back(L"Cache npm", [&]() {
                cleanNpmCache(stats);
            });
        }
        if (options.cleanPipCache) {
            tasks.emplace_back(L"Cache pip", [&]() {
                cleanPipCache(stats);
            });
        }
        if (options.cleanNuGetCache) {
            tasks.emplace_back(L"Cache NuGet", [&]() {
                cleanNuGetCache(stats);
            });
        }
        if (options.cleanGradleMavenCache) {
            tasks.emplace_back(L"Cache Gradle/Maven", [&]() {
                cleanGradleMavenCache(stats);
            });
        }
        if (options.cleanCargoCache) {
            tasks.emplace_back(L"Cache Cargo (Rust)", [&]() {
                cleanCargoCache(stats);
            });
        }
        if (options.cleanGoCache) {
            tasks.emplace_back(L"Cache Go", [&]() {
                cleanGoCache(stats);
            });
        }
        if (options.cleanVSCache) {
            tasks.emplace_back(L"Cache Visual Studio", [&]() {
                cleanVSCache(stats);
            });
        }
        if (options.cleanVSCodeCache) {
            tasks.emplace_back(L"Cache VS Code", [&]() {
                cleanVSCodeCache(stats);
            });
        }
        
        // === NOUVEAU: Shader cache ===
        if (options.cleanShaderCache) {
            tasks.emplace_back(L"Cache Shaders GPU", [&]() {
                cleanShaderCache(stats);
            });
        }
        
        // === NOUVEAU: Système profond ===
        if (options.cleanComponentStore) {
            tasks.emplace_back(L"Component Store (WinSxS)", [&]() {
                cleanComponentStore(stats);
            });
        }
        if (options.cleanBrowserExtended) {
            tasks.emplace_back(L"Cache navigateurs etendu", [&]() {
                cleanBrowserExtended(stats);
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

    // ========== ESTIMATION ==========
    
    CategoryEstimate Cleaner::estimateDirectory(const fs::path& path, const std::wstring& name) {
        CategoryEstimate est;
        est.name = name;
        
        if (!fs::exists(path)) return est;
        
        std::error_code ec;
        for (const auto& entry : fs::recursive_directory_iterator(path,
            fs::directory_options::skip_permission_denied, ec)) {
            if (m_stopRequested) break;
            try {
                if (entry.is_regular_file()) {
                    est.size += entry.file_size(ec);
                    est.fileCount++;
                }
            } catch (...) {}
        }
        
        return est;
    }
    
    CategoryEstimate Cleaner::estimateDirectories(const std::vector<fs::path>& paths, const std::wstring& name) {
        CategoryEstimate est;
        est.name = name;
        
        for (const auto& path : paths) {
            if (m_stopRequested) break;
            if (!fs::exists(path)) continue;
            
            auto partial = estimateDirectory(path, name);
            est.size += partial.size;
            est.fileCount += partial.fileCount;
        }
        
        return est;
    }

    CleaningEstimate Cleaner::estimate(const CleaningOptions& options, ProgressCallback progressCallback) {
        CleaningEstimate result;
        m_running = true;
        m_stopRequested = false;
        
        std::vector<std::pair<std::wstring, std::function<CategoryEstimate()>>> tasks;
        
        // Base
        if (options.cleanUserTemp) {
            tasks.emplace_back(L"Temp utilisateur", [&]() {
                return estimateDirectory(getUserTempPath(), L"Temp utilisateur");
            });
        }
        if (options.cleanWindowsTemp) {
            tasks.emplace_back(L"Temp Windows", [&]() {
                return estimateDirectory(getWindowsTempPath(), L"Temp Windows");
            });
        }
        if (options.cleanPrefetch) {
            tasks.emplace_back(L"Prefetch", [&]() {
                return estimateDirectory(getPrefetchPath(), L"Prefetch");
            });
        }
        if (options.cleanRecent) {
            tasks.emplace_back(L"Fichiers recents", [&]() {
                return estimateDirectory(getRecentPath(), L"Fichiers recents");
            });
        }
        if (options.cleanBrowserCache) {
            tasks.emplace_back(L"Cache navigateurs", [&]() {
                return estimateDirectories(getBrowserCachePaths(), L"Cache navigateurs");
            });
        }
        
        // Système avancé
        if (options.cleanWindowsUpdate) {
            tasks.emplace_back(L"Cache Windows Update", [&]() {
                return estimateDirectory(getWindowsUpdateCachePath(), L"Cache Windows Update");
            });
        }
        if (options.cleanSystemLogs) {
            tasks.emplace_back(L"Logs systeme", [&]() {
                return estimateDirectories(getSystemLogPaths(), L"Logs systeme");
            });
        }
        if (options.cleanCrashDumps) {
            tasks.emplace_back(L"Crash dumps", [&]() {
                return estimateDirectories(getCrashDumpPaths(), L"Crash dumps");
            });
        }
        if (options.cleanThumbnails) {
            tasks.emplace_back(L"Cache miniatures", [&]() {
                return estimateDirectory(getThumbnailCachePath(), L"Cache miniatures");
            });
        }
        if (options.cleanDeliveryOptimization) {
            tasks.emplace_back(L"Delivery Optimization", [&]() {
                return estimateDirectory(getDeliveryOptimizationPath(), L"Delivery Optimization");
            });
        }
        if (options.cleanWindowsInstaller) {
            tasks.emplace_back(L"Windows Installer", [&]() {
                return estimateDirectory(getWindowsInstallerPatchPath(), L"Windows Installer");
            });
        }
        if (options.cleanFontCache) {
            tasks.emplace_back(L"Cache polices", [&]() {
                return estimateDirectory(getFontCachePath(), L"Cache polices");
            });
        }
        if (options.cleanWindowsOld) {
            tasks.emplace_back(L"Windows.old", [&]() {
                return estimateDirectory(getWindowsOldPath(), L"Windows.old");
            });
        }
        if (options.cleanWindowsStoreCache) {
            tasks.emplace_back(L"Cache Windows Store", [&]() {
                return estimateDirectory(getWindowsStoreCachePath(), L"Cache Windows Store");
            });
        }
        if (options.cleanChkdskFiles) {
            tasks.emplace_back(L"Fichiers Chkdsk", [&]() {
                return estimateDirectories(getChkdskFilePaths(), L"Fichiers Chkdsk");
            });
        }
        if (options.cleanNetworkCache) {
            tasks.emplace_back(L"Cache reseau", [&]() {
                return estimateDirectories(getNetworkCachePaths(), L"Cache reseau");
            });
        }
        
        // Développement
        if (options.cleanNpmCache) {
            tasks.emplace_back(L"Cache npm", [&]() {
                return estimateDirectories(getNpmCachePaths(), L"Cache npm");
            });
        }
        if (options.cleanPipCache) {
            tasks.emplace_back(L"Cache pip", [&]() {
                return estimateDirectories(getPipCachePaths(), L"Cache pip");
            });
        }
        if (options.cleanNuGetCache) {
            tasks.emplace_back(L"Cache NuGet", [&]() {
                return estimateDirectories(getNuGetCachePaths(), L"Cache NuGet");
            });
        }
        if (options.cleanGradleMavenCache) {
            tasks.emplace_back(L"Cache Gradle/Maven", [&]() {
                return estimateDirectories(getGradleMavenCachePaths(), L"Cache Gradle/Maven");
            });
        }
        if (options.cleanCargoCache) {
            tasks.emplace_back(L"Cache Cargo", [&]() {
                return estimateDirectories(getCargoCachePaths(), L"Cache Cargo");
            });
        }
        if (options.cleanGoCache) {
            tasks.emplace_back(L"Cache Go", [&]() {
                return estimateDirectories(getGoCachePaths(), L"Cache Go");
            });
        }
        if (options.cleanVSCache) {
            tasks.emplace_back(L"Cache Visual Studio", [&]() {
                return estimateDirectories(getVSCachePaths(), L"Cache Visual Studio");
            });
        }
        if (options.cleanVSCodeCache) {
            tasks.emplace_back(L"Cache VS Code", [&]() {
                return estimateDirectories(getVSCodeCachePaths(), L"Cache VS Code");
            });
        }
        if (options.cleanShaderCache) {
            tasks.emplace_back(L"Cache Shaders", [&]() {
                return estimateDirectories(getShaderCachePaths(), L"Cache Shaders");
            });
        }
        if (options.cleanBrowserExtended) {
            tasks.emplace_back(L"Cache navigateurs etendu", [&]() {
                return estimateDirectories(getBrowserExtendedPaths(), L"Cache navigateurs etendu");
            });
        }
        
        // Corbeille (estimation spéciale)
        if (options.cleanRecycleBin) {
            tasks.emplace_back(L"Corbeille", [&]() {
                CategoryEstimate est;
                est.name = L"Corbeille";
                SHQUERYRBINFO rbInfo = { sizeof(SHQUERYRBINFO) };
                if (SUCCEEDED(SHQueryRecycleBinW(nullptr, &rbInfo))) {
                    est.size = rbInfo.i64Size;
                    est.fileCount = static_cast<uint64_t>(rbInfo.i64NumItems);
                }
                return est;
            });
        }
        
        int totalTasks = static_cast<int>(tasks.size());
        int currentTask = 0;
        
        for (const auto& [name, estimator] : tasks) {
            if (m_stopRequested) break;
            
            if (progressCallback) {
                int progress = totalTasks > 0 ? (currentTask * 100) / totalTasks : 0;
                progressCallback(L"Analyse: " + name, progress);
            }
            
            auto est = estimator();
            if (est.size > 0 || est.fileCount > 0) {
                result.categories.push_back(est);
                result.totalSize += est.size;
                result.totalFiles += est.fileCount;
            }
            currentTask++;
        }
        
        if (progressCallback) {
            progressCallback(L"Analyse terminee!", 100);
        }
        
        m_running = false;
        return result;
    }

    void Cleaner::stop() {
        m_stopRequested = true;
    }

    bool Cleaner::isRunning() const {
        return m_running;
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
        
        // Supprimer les dossiers vides
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
            for (auto it = emptyDirs.rbegin(); it != emptyDirs.rend(); ++it) {
                if (m_stopRequested) return;
                fs::remove(*it, ec);
            }
        } catch (...) {}
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

    // ========== MÉTHODES WINDOWS ==========

    void Cleaner::flushDnsCache(CleaningStats& stats) {
        STARTUPINFOW si = { sizeof(si) };
        si.dwFlags = STARTF_USESHOWWINDOW;
        si.wShowWindow = SW_HIDE;
        PROCESS_INFORMATION pi;
        
        wchar_t cmd[] = L"ipconfig /flushdns";
        if (CreateProcessW(nullptr, cmd, nullptr, nullptr, FALSE, 
            CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi)) {
            WaitForSingleObject(pi.hProcess, 5000);
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
            stats.filesDeleted++;
        } else {
            stats.errors++;
            ErrorInfo error;
            error.filePath = L"DNS Cache";
            error.category = L"Cache DNS";
            error.errorMessage = GetWindowsErrorMessage(GetLastError());
            stats.errorDetails.push_back(error);
        }
    }

    bool Cleaner::isShortcutBroken(const fs::path& shortcutPath) {
        CoInitialize(nullptr);
        bool isBroken = false;
        
        IShellLinkW* pShellLink = nullptr;
        IPersistFile* pPersistFile = nullptr;
        
        HRESULT hr = CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER,
            IID_IShellLinkW, (void**)&pShellLink);
        
        if (SUCCEEDED(hr)) {
            hr = pShellLink->QueryInterface(IID_IPersistFile, (void**)&pPersistFile);
            if (SUCCEEDED(hr)) {
                hr = pPersistFile->Load(shortcutPath.c_str(), STGM_READ);
                if (SUCCEEDED(hr)) {
                    wchar_t targetPath[MAX_PATH];
                    WIN32_FIND_DATAW findData;
                    hr = pShellLink->GetPath(targetPath, MAX_PATH, &findData, SLGP_RAWPATH);
                    
                    if (SUCCEEDED(hr) && targetPath[0] != L'\0') {
                        if (wcsstr(targetPath, L":\\") != nullptr || 
                            wcsncmp(targetPath, L"\\\\", 2) == 0) {
                            wchar_t expandedPath[MAX_PATH];
                            ExpandEnvironmentStringsW(targetPath, expandedPath, MAX_PATH);
                            
                            if (!fs::exists(expandedPath)) {
                                isBroken = true;
                            }
                        }
                    }
                }
                pPersistFile->Release();
            }
            pShellLink->Release();
        }
        
        CoUninitialize();
        return isBroken;
    }

    void Cleaner::cleanBrokenShortcuts(CleaningStats& stats) {
        auto folders = getShortcutFolders();
        
        for (const auto& folder : folders) {
            if (m_stopRequested) return;
            if (!fs::exists(folder)) continue;
            
            std::error_code ec;
            for (const auto& entry : fs::recursive_directory_iterator(folder,
                fs::directory_options::skip_permission_denied, ec)) {
                
                if (m_stopRequested) return;
                
                try {
                    if (entry.is_regular_file() && 
                        entry.path().extension() == L".lnk") {
                        
                        if (isShortcutBroken(entry.path())) {
                            auto fileSize = entry.file_size(ec);
                            if (fs::remove(entry.path(), ec)) {
                                stats.filesDeleted++;
                                stats.bytesFreed += fileSize;
                            } else {
                                stats.errors++;
                                ErrorInfo error;
                                error.filePath = entry.path().wstring();
                                error.category = L"Raccourcis casses";
                                error.errorMessage = GetErrorCodeMessage(ec);
                                stats.errorDetails.push_back(error);
                            }
                        }
                    }
                } catch (...) {}
            }
        }
    }

    void Cleaner::cleanWindowsOld(CleaningStats& stats) {
        auto windowsOldPath = getWindowsOldPath();
        if (!fs::exists(windowsOldPath)) return;
        
        uint64_t totalSize = 0;
        uint64_t fileCount = 0;
        std::error_code ec;
        
        try {
            for (const auto& entry : fs::recursive_directory_iterator(windowsOldPath,
                fs::directory_options::skip_permission_denied, ec)) {
                if (entry.is_regular_file()) {
                    totalSize += entry.file_size(ec);
                    fileCount++;
                }
            }
        } catch (...) {}
        
        std::wstring takeownCmd = L"cmd.exe /c takeown /f \"" + windowsOldPath.wstring() + 
            L"\" /r /d y && icacls \"" + windowsOldPath.wstring() + 
            L"\" /grant administrators:F /t /q && rd /s /q \"" + windowsOldPath.wstring() + L"\"";
        
        std::vector<wchar_t> cmdBuffer(takeownCmd.begin(), takeownCmd.end());
        cmdBuffer.push_back(L'\0');
        
        STARTUPINFOW si = { sizeof(si) };
        si.dwFlags = STARTF_USESHOWWINDOW;
        si.wShowWindow = SW_HIDE;
        PROCESS_INFORMATION pi;
        
        if (CreateProcessW(nullptr, cmdBuffer.data(), nullptr, nullptr, FALSE,
            CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi)) {
            WaitForSingleObject(pi.hProcess, 300000);
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
            
            if (!fs::exists(windowsOldPath)) {
                stats.filesDeleted += fileCount;
                stats.bytesFreed += totalSize;
            } else {
                stats.errors++;
                ErrorInfo error;
                error.filePath = windowsOldPath.wstring();
                error.category = L"Windows.old";
                error.errorMessage = L"Suppression partielle ou echouee";
                stats.errorDetails.push_back(error);
            }
        } else {
            stats.errors++;
            ErrorInfo error;
            error.filePath = windowsOldPath.wstring();
            error.category = L"Windows.old";
            error.errorMessage = GetWindowsErrorMessage(GetLastError());
            stats.errorDetails.push_back(error);
        }
    }

    void Cleaner::cleanWindowsStoreCache(CleaningStats& stats) {
        STARTUPINFOW si = { sizeof(si) };
        si.dwFlags = STARTF_USESHOWWINDOW;
        si.wShowWindow = SW_HIDE;
        PROCESS_INFORMATION pi;
        
        wchar_t cmd[] = L"wsreset.exe";
        if (CreateProcessW(nullptr, cmd, nullptr, nullptr, FALSE,
            CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi)) {
            WaitForSingleObject(pi.hProcess, 30000);
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
            stats.filesDeleted++;
        }
        
        auto storeCachePath = getWindowsStoreCachePath();
        if (fs::exists(storeCachePath)) {
            cleanDirectory(storeCachePath, stats, nullptr, L"Cache Windows Store");
        }
    }

    void Cleaner::clearClipboard(CleaningStats& stats) {
        if (OpenClipboard(nullptr)) {
            if (EmptyClipboard()) {
                stats.filesDeleted++;
            } else {
                stats.errors++;
                ErrorInfo error;
                error.filePath = L"Clipboard";
                error.category = L"Presse-papiers";
                error.errorMessage = GetWindowsErrorMessage(GetLastError());
                stats.errorDetails.push_back(error);
            }
            CloseClipboard();
        } else {
            stats.errors++;
            ErrorInfo error;
            error.filePath = L"Clipboard";
            error.category = L"Presse-papiers";
            error.errorMessage = L"Impossible d'ouvrir le presse-papiers";
            stats.errorDetails.push_back(error);
        }
    }

    void Cleaner::cleanChkdskFiles(CleaningStats& stats) {
        auto chkdskPaths = getChkdskFilePaths();
        
        for (const auto& path : chkdskPaths) {
            if (m_stopRequested) return;
            
            if (fs::is_directory(path)) {
                cleanDirectory(path, stats, nullptr, L"Fichiers Chkdsk");
            } else if (fs::exists(path)) {
                std::error_code ec;
                auto fileSize = fs::file_size(path, ec);
                if (fs::remove(path, ec)) {
                    stats.filesDeleted++;
                    stats.bytesFreed += fileSize;
                } else {
                    stats.errors++;
                    ErrorInfo error;
                    error.filePath = path.wstring();
                    error.category = L"Fichiers Chkdsk";
                    error.errorMessage = GetErrorCodeMessage(ec);
                    stats.errorDetails.push_back(error);
                }
            }
        }
    }

    void Cleaner::cleanNetworkCache(CleaningStats& stats) {
        auto networkPaths = getNetworkCachePaths();
        
        for (const auto& path : networkPaths) {
            if (m_stopRequested) return;
            if (fs::exists(path)) {
                cleanDirectory(path, stats, nullptr, L"Cache reseau");
            }
        }
        
        STARTUPINFOW si = { sizeof(si) };
        si.dwFlags = STARTF_USESHOWWINDOW;
        si.wShowWindow = SW_HIDE;
        PROCESS_INFORMATION pi;
        
        wchar_t arpCmd[] = L"netsh interface ip delete arpcache";
        if (CreateProcessW(nullptr, arpCmd, nullptr, nullptr, FALSE,
            CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi)) {
            WaitForSingleObject(pi.hProcess, 5000);
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
        }
        
        wchar_t nbtCmd[] = L"nbtstat -R";
        if (CreateProcessW(nullptr, nbtCmd, nullptr, nullptr, FALSE,
            CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi)) {
            WaitForSingleObject(pi.hProcess, 5000);
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
        }
    }

    // ========== NOUVEAU: MÉTHODES DÉVELOPPEMENT ==========

    void Cleaner::cleanNpmCache(CleaningStats& stats) {
        // Utiliser npm cache clean --force si npm est disponible
        STARTUPINFOW si = { sizeof(si) };
        si.dwFlags = STARTF_USESHOWWINDOW;
        si.wShowWindow = SW_HIDE;
        PROCESS_INFORMATION pi;
        
        wchar_t cmd[] = L"npm cache clean --force";
        if (CreateProcessW(nullptr, cmd, nullptr, nullptr, FALSE,
            CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi)) {
            WaitForSingleObject(pi.hProcess, 60000);
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
        }
        
        // Aussi nettoyer manuellement les chemins
        for (const auto& path : getNpmCachePaths()) {
            if (m_stopRequested) return;
            if (fs::exists(path)) {
                cleanDirectory(path, stats, nullptr, L"Cache npm");
            }
        }
    }

    void Cleaner::cleanPipCache(CleaningStats& stats) {
        // pip cache purge si disponible
        STARTUPINFOW si = { sizeof(si) };
        si.dwFlags = STARTF_USESHOWWINDOW;
        si.wShowWindow = SW_HIDE;
        PROCESS_INFORMATION pi;
        
        wchar_t cmd[] = L"pip cache purge";
        if (CreateProcessW(nullptr, cmd, nullptr, nullptr, FALSE,
            CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi)) {
            WaitForSingleObject(pi.hProcess, 30000);
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
        }
        
        for (const auto& path : getPipCachePaths()) {
            if (m_stopRequested) return;
            if (fs::exists(path)) {
                cleanDirectory(path, stats, nullptr, L"Cache pip");
            }
        }
    }

    void Cleaner::cleanNuGetCache(CleaningStats& stats) {
        // dotnet nuget locals all --clear
        STARTUPINFOW si = { sizeof(si) };
        si.dwFlags = STARTF_USESHOWWINDOW;
        si.wShowWindow = SW_HIDE;
        PROCESS_INFORMATION pi;
        
        wchar_t cmd[] = L"dotnet nuget locals all --clear";
        if (CreateProcessW(nullptr, cmd, nullptr, nullptr, FALSE,
            CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi)) {
            WaitForSingleObject(pi.hProcess, 120000);
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
        }
        
        for (const auto& path : getNuGetCachePaths()) {
            if (m_stopRequested) return;
            if (fs::exists(path)) {
                cleanDirectory(path, stats, nullptr, L"Cache NuGet");
            }
        }
    }

    void Cleaner::cleanGradleMavenCache(CleaningStats& stats) {
        for (const auto& path : getGradleMavenCachePaths()) {
            if (m_stopRequested) return;
            if (fs::exists(path)) {
                cleanDirectory(path, stats, nullptr, L"Cache Gradle/Maven");
            }
        }
    }

    void Cleaner::cleanCargoCache(CleaningStats& stats) {
        // cargo cache --autoclean si disponible
        STARTUPINFOW si = { sizeof(si) };
        si.dwFlags = STARTF_USESHOWWINDOW;
        si.wShowWindow = SW_HIDE;
        PROCESS_INFORMATION pi;
        
        wchar_t cmd[] = L"cargo cache --autoclean";
        CreateProcessW(nullptr, cmd, nullptr, nullptr, FALSE,
            CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi);
        if (pi.hProcess) {
            WaitForSingleObject(pi.hProcess, 60000);
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
        }
        
        for (const auto& path : getCargoCachePaths()) {
            if (m_stopRequested) return;
            if (fs::exists(path)) {
                cleanDirectory(path, stats, nullptr, L"Cache Cargo");
            }
        }
    }

    void Cleaner::cleanGoCache(CleaningStats& stats) {
        // go clean -cache -modcache
        STARTUPINFOW si = { sizeof(si) };
        si.dwFlags = STARTF_USESHOWWINDOW;
        si.wShowWindow = SW_HIDE;
        PROCESS_INFORMATION pi;
        
        wchar_t cmd[] = L"go clean -cache -modcache";
        if (CreateProcessW(nullptr, cmd, nullptr, nullptr, FALSE,
            CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi)) {
            WaitForSingleObject(pi.hProcess, 120000);
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
        }
        
        for (const auto& path : getGoCachePaths()) {
            if (m_stopRequested) return;
            if (fs::exists(path)) {
                cleanDirectory(path, stats, nullptr, L"Cache Go");
            }
        }
    }

    void Cleaner::cleanVSCache(CleaningStats& stats) {
        for (const auto& path : getVSCachePaths()) {
            if (m_stopRequested) return;
            if (fs::exists(path)) {
                cleanDirectory(path, stats, nullptr, L"Cache Visual Studio");
            }
        }
    }

    void Cleaner::cleanVSCodeCache(CleaningStats& stats) {
        for (const auto& path : getVSCodeCachePaths()) {
            if (m_stopRequested) return;
            if (fs::exists(path)) {
                cleanDirectory(path, stats, nullptr, L"Cache VS Code");
            }
        }
    }

    // ========== NOUVEAU: SHADER CACHE ==========

    void Cleaner::cleanShaderCache(CleaningStats& stats) {
        for (const auto& path : getShaderCachePaths()) {
            if (m_stopRequested) return;
            if (fs::exists(path)) {
                cleanDirectory(path, stats, nullptr, L"Cache Shaders");
            }
        }
    }

    // ========== NOUVEAU: SYSTÈME PROFOND ==========

    void Cleaner::cleanComponentStore(CleaningStats& stats) {
        STARTUPINFOW si = { sizeof(si) };
        si.dwFlags = STARTF_USESHOWWINDOW;
        si.wShowWindow = SW_HIDE;
        PROCESS_INFORMATION pi = {};
        
        // DISM cleanup - peut prendre plusieurs minutes
        wchar_t cmd[] = L"dism.exe /Online /Cleanup-Image /StartComponentCleanup";
        if (CreateProcessW(nullptr, cmd, nullptr, nullptr, FALSE,
            CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi)) {
            
            // Attendre avec possibilité d'annulation (vérifier toutes les 500ms)
            DWORD waitResult;
            bool cancelled = false;
            int iterations = 0;
            const int maxIterations = 1200; // 1200 * 500ms = 10 minutes
            
            while (true) {
                waitResult = WaitForSingleObject(pi.hProcess, 500);
                
                if (waitResult == WAIT_OBJECT_0) {
                    // Processus terminé
                    break;
                }
                
                if (m_stopRequested) {
                    // Utilisateur a demandé l'arrêt - terminer DISM
                    TerminateProcess(pi.hProcess, 1);
                    WaitForSingleObject(pi.hProcess, 2000);
                    cancelled = true;
                    break;
                }
                
                // Timeout de sécurité: 10 minutes max
                iterations++;
                if (iterations > maxIterations) {
                    TerminateProcess(pi.hProcess, 1);
                    WaitForSingleObject(pi.hProcess, 2000);
                    break;
                }
            }
            
            DWORD exitCode = 0;
            GetExitCodeProcess(pi.hProcess, &exitCode);
            
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
            
            if (cancelled) {
                stats.errors++;
                ErrorInfo error;
                error.filePath = L"Component Store";
                error.category = L"WinSxS Cleanup";
                error.errorMessage = L"Annule par l'utilisateur";
                stats.errorDetails.push_back(error);
            } else if (exitCode == 0) {
                stats.filesDeleted++;
            } else {
                stats.errors++;
                ErrorInfo error;
                error.filePath = L"Component Store";
                error.category = L"WinSxS Cleanup";
                error.errorMessage = L"DISM a retourne le code " + std::to_wstring(exitCode);
                stats.errorDetails.push_back(error);
            }
        } else {
            stats.errors++;
            ErrorInfo error;
            error.filePath = L"Component Store";
            error.category = L"WinSxS Cleanup";
            error.errorMessage = GetWindowsErrorMessage(GetLastError());
            stats.errorDetails.push_back(error);
        }
    }

    void Cleaner::cleanBrowserExtended(CleaningStats& stats) {
        for (const auto& path : getBrowserExtendedPaths()) {
            if (m_stopRequested) return;
            if (fs::exists(path)) {
                cleanDirectory(path, stats, nullptr, L"Cache navigateurs etendu");
            }
        }
    }

    // ========== CHEMINS ==========

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
        auto local = getLocalAppDataPath();
        if (!local.empty()) {
            return local / L"Microsoft" / L"Windows" / L"Explorer";
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

    fs::path Cleaner::getWindowsOldPath() const {
        wchar_t sysDir[MAX_PATH];
        GetSystemDirectoryW(sysDir, MAX_PATH);
        std::wstring sysDrive(sysDir, 2);
        return fs::path(sysDrive) / L"Windows.old";
    }

    fs::path Cleaner::getWindowsStoreCachePath() const {
        auto local = getLocalAppDataPath();
        if (!local.empty()) {
            return local / L"Packages" / L"Microsoft.WindowsStore_8wekyb3d8bbwe" / L"LocalCache";
        }
        return {};
    }

    std::vector<fs::path> Cleaner::getSystemLogPaths() const {
        std::vector<fs::path> paths;
        wchar_t winDir[MAX_PATH];
        GetWindowsDirectoryW(winDir, MAX_PATH);
        fs::path winPath(winDir);
        
        paths.push_back(winPath / L"Logs" / L"CBS");
        paths.push_back(winPath / L"Logs" / L"DISM");
        paths.push_back(winPath / L"Logs" / L"WindowsUpdate");
        paths.push_back(winPath / L"Logs" / L"SIH");
        paths.push_back(winPath / L"Panther");
        paths.push_back(winPath / L"LiveKernelReports");
        
        return paths;
    }

    std::vector<fs::path> Cleaner::getCrashDumpPaths() const {
        std::vector<fs::path> paths;
        wchar_t winDir[MAX_PATH];
        GetWindowsDirectoryW(winDir, MAX_PATH);
        fs::path winPath(winDir);
        
        paths.push_back(winPath / L"Minidump");
        paths.push_back(winPath / L"MEMORY.DMP");
        
        auto local = getLocalAppDataPath();
        if (!local.empty()) {
            paths.push_back(local / L"CrashDumps");
            paths.push_back(local / L"Microsoft" / L"Windows" / L"WER");
        }
        
        wchar_t programData[MAX_PATH];
        if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_COMMON_APPDATA, nullptr, 0, programData))) {
            paths.push_back(fs::path(programData) / L"Microsoft" / L"Windows" / L"WER");
        }
        
        return paths;
    }

    std::vector<fs::path> Cleaner::getBrowserCachePaths() const {
        std::vector<fs::path> paths;
        auto local = getLocalAppDataPath();
        
        if (!local.empty()) {
            // Chrome
            paths.push_back(local / L"Google" / L"Chrome" / L"User Data" / L"Default" / L"Cache");
            paths.push_back(local / L"Google" / L"Chrome" / L"User Data" / L"Default" / L"Code Cache");
            paths.push_back(local / L"Google" / L"Chrome" / L"User Data" / L"Default" / L"GPUCache");
            
            // Edge
            paths.push_back(local / L"Microsoft" / L"Edge" / L"User Data" / L"Default" / L"Cache");
            paths.push_back(local / L"Microsoft" / L"Edge" / L"User Data" / L"Default" / L"Code Cache");
            paths.push_back(local / L"Microsoft" / L"Edge" / L"User Data" / L"Default" / L"GPUCache");
            
            // Firefox
            auto appData = getAppDataPath();
            if (!appData.empty()) {
                fs::path firefoxProfiles = appData / L"Mozilla" / L"Firefox" / L"Profiles";
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
            paths.push_back(local / L"BraveSoftware" / L"Brave-Browser" / L"User Data" / L"Default" / L"Cache");
            
            // Opera
            paths.push_back(local / L"Opera Software" / L"Opera Stable" / L"Cache");
        }
        return paths;
    }

    std::vector<fs::path> Cleaner::getShortcutFolders() const {
        std::vector<fs::path> paths;
        
        wchar_t desktop[MAX_PATH];
        if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_DESKTOP, nullptr, 0, desktop))) {
            paths.push_back(desktop);
        }
        
        wchar_t commonDesktop[MAX_PATH];
        if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_COMMON_DESKTOPDIRECTORY, nullptr, 0, commonDesktop))) {
            paths.push_back(commonDesktop);
        }
        
        wchar_t startMenu[MAX_PATH];
        if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_STARTMENU, nullptr, 0, startMenu))) {
            paths.push_back(startMenu);
        }
        
        wchar_t commonStartMenu[MAX_PATH];
        if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_COMMON_STARTMENU, nullptr, 0, commonStartMenu))) {
            paths.push_back(commonStartMenu);
        }
        
        auto appData = getAppDataPath();
        if (!appData.empty()) {
            paths.push_back(appData / L"Microsoft" / L"Internet Explorer" / L"Quick Launch" / L"User Pinned" / L"TaskBar");
        }
        
        return paths;
    }

    std::vector<fs::path> Cleaner::getChkdskFilePaths() const {
        std::vector<fs::path> paths;
        
        DWORD drives = GetLogicalDrives();
        for (char letter = 'A'; letter <= 'Z'; letter++) {
            if (drives & (1 << (letter - 'A'))) {
                std::wstring root = std::wstring(1, letter) + L":\\";
                UINT driveType = GetDriveTypeW(root.c_str());
                
                if (driveType == DRIVE_FIXED) {
                    std::error_code ec;
                    for (const auto& entry : fs::directory_iterator(root, 
                        fs::directory_options::skip_permission_denied, ec)) {
                        try {
                            if (entry.is_directory()) {
                                std::wstring name = entry.path().filename().wstring();
                                if (name.length() >= 6 && name.substr(0, 6) == L"FOUND.") {
                                    paths.push_back(entry.path());
                                }
                            }
                        } catch (...) {}
                    }
                    
                    for (const auto& entry : fs::directory_iterator(root,
                        fs::directory_options::skip_permission_denied, ec)) {
                        try {
                            if (entry.is_regular_file() && entry.path().extension() == L".chk") {
                                paths.push_back(entry.path());
                            }
                        } catch (...) {}
                    }
                }
            }
        }
        
        return paths;
    }

    std::vector<fs::path> Cleaner::getNetworkCachePaths() const {
        std::vector<fs::path> paths;
        wchar_t winDir[MAX_PATH];
        GetWindowsDirectoryW(winDir, MAX_PATH);
        fs::path winPath(winDir);
        
        wchar_t sysDir[MAX_PATH];
        GetSystemDirectoryW(sysDir, MAX_PATH);
        std::wstring sysDrive(sysDir, 2);
        paths.push_back(fs::path(sysDrive) / L"inetpub" / L"logs" / L"LogFiles");
        
        paths.push_back(winPath / L"System32" / L"LogFiles" / L"HTTPERR");
        paths.push_back(winPath / L"System32" / L"LogFiles" / L"WMI");
        paths.push_back(winPath / L"CSC");
        paths.push_back(winPath / L"Downloaded Program Files");
        
        return paths;
    }

    // ========== NOUVEAU: CHEMINS DÉVELOPPEMENT ==========

    std::vector<fs::path> Cleaner::getNpmCachePaths() const {
        std::vector<fs::path> paths;
        auto home = getUserProfilePath();
        auto local = getLocalAppDataPath();
        
        if (!local.empty()) {
            paths.push_back(local / L"npm-cache");
        }
        if (!home.empty()) {
            paths.push_back(home / L".npm" / L"_cacache");
            paths.push_back(home / L".npm" / L"_logs");
        }
        
        return paths;
    }

    std::vector<fs::path> Cleaner::getPipCachePaths() const {
        std::vector<fs::path> paths;
        auto local = getLocalAppDataPath();
        auto home = getUserProfilePath();
        
        if (!local.empty()) {
            paths.push_back(local / L"pip" / L"cache");
        }
        if (!home.empty()) {
            paths.push_back(home / L".cache" / L"pip");
        }
        
        return paths;
    }

    std::vector<fs::path> Cleaner::getNuGetCachePaths() const {
        std::vector<fs::path> paths;
        auto home = getUserProfilePath();
        auto local = getLocalAppDataPath();
        
        if (!home.empty()) {
            paths.push_back(home / L".nuget" / L"packages");
        }
        if (!local.empty()) {
            paths.push_back(local / L"NuGet" / L"v3-cache");
            paths.push_back(local / L"NuGet" / L"plugins-cache");
        }
        
        return paths;
    }

    std::vector<fs::path> Cleaner::getGradleMavenCachePaths() const {
        std::vector<fs::path> paths;
        auto home = getUserProfilePath();
        
        if (!home.empty()) {
            // Gradle
            paths.push_back(home / L".gradle" / L"caches");
            paths.push_back(home / L".gradle" / L"daemon");
            paths.push_back(home / L".gradle" / L"wrapper" / L"dists");
            
            // Maven
            paths.push_back(home / L".m2" / L"repository");
        }
        
        return paths;
    }

    std::vector<fs::path> Cleaner::getCargoCachePaths() const {
        std::vector<fs::path> paths;
        auto home = getUserProfilePath();
        
        if (!home.empty()) {
            paths.push_back(home / L".cargo" / L"registry" / L"cache");
            paths.push_back(home / L".cargo" / L"registry" / L"index");
            paths.push_back(home / L".cargo" / L"git" / L"db");
            paths.push_back(home / L".cargo" / L"git" / L"checkouts");
        }
        
        return paths;
    }

    std::vector<fs::path> Cleaner::getGoCachePaths() const {
        std::vector<fs::path> paths;
        auto home = getUserProfilePath();
        auto local = getLocalAppDataPath();
        
        if (!home.empty()) {
            paths.push_back(home / L"go" / L"pkg" / L"mod" / L"cache");
        }
        if (!local.empty()) {
            paths.push_back(local / L"go-build");
        }
        
        return paths;
    }

    std::vector<fs::path> Cleaner::getVSCachePaths() const {
        std::vector<fs::path> paths;
        auto local = getLocalAppDataPath();
        
        if (!local.empty()) {
            fs::path vsBase = local / L"Microsoft" / L"VisualStudio";
            
            if (fs::exists(vsBase)) {
                std::error_code ec;
                for (const auto& entry : fs::directory_iterator(vsBase, ec)) {
                    if (entry.is_directory()) {
                        std::wstring name = entry.path().filename().wstring();
                        // Versions comme 17.0_xxx, 16.0_xxx
                        if (name.find(L'.') != std::wstring::npos) {
                            paths.push_back(entry.path() / L"ComponentModelCache");
                            paths.push_back(entry.path() / L"Extensions");
                            paths.push_back(entry.path() / L"Designer" / L"ShadowCache");
                        }
                    }
                }
            }
            
            // Roslyn cache
            paths.push_back(local / L"Microsoft" / L"VisualStudio" / L"Roslyn" / L"Cache");
            
            // Downloaded packages
            paths.push_back(local / L"Microsoft" / L"VisualStudio" / L"Packages");
            
            // Blend cache
            paths.push_back(local / L"Microsoft" / L"Blend" / L"Cache");
            
            // MEF cache
            paths.push_back(local / L"Microsoft" / L"VisualStudio" / L"ComponentModelCache");
        }
        
        // Temp obj files
        auto temp = getUserTempPath();
        if (!temp.empty()) {
            paths.push_back(temp / L"VisualStudioTestExplorerExtensions");
        }
        
        return paths;
    }

    std::vector<fs::path> Cleaner::getVSCodeCachePaths() const {
        std::vector<fs::path> paths;
        auto local = getLocalAppDataPath();
        auto appData = getAppDataPath();
        
        if (!appData.empty()) {
            paths.push_back(appData / L"Code" / L"Cache");
            paths.push_back(appData / L"Code" / L"CachedData");
            paths.push_back(appData / L"Code" / L"CachedExtensions");
            paths.push_back(appData / L"Code" / L"CachedExtensionVSIXs");
            paths.push_back(appData / L"Code" / L"Code Cache");
            paths.push_back(appData / L"Code" / L"GPUCache");
            paths.push_back(appData / L"Code" / L"logs");
            
            // VS Code Insiders
            paths.push_back(appData / L"Code - Insiders" / L"Cache");
            paths.push_back(appData / L"Code - Insiders" / L"CachedData");
        }
        
        if (!local.empty()) {
            // C++ tools cache
            paths.push_back(local / L"Microsoft" / L"vscode-cpptools");
        }
        
        return paths;
    }

    // ========== NOUVEAU: SHADER CACHE ==========

    std::vector<fs::path> Cleaner::getShaderCachePaths() const {
        std::vector<fs::path> paths;
        auto local = getLocalAppDataPath();
        
        if (!local.empty()) {
            // NVIDIA
            paths.push_back(local / L"NVIDIA" / L"DXCache");
            paths.push_back(local / L"NVIDIA" / L"GLCache");
            paths.push_back(local / L"NVIDIA Corporation" / L"NV_Cache");
            
            // AMD
            paths.push_back(local / L"AMD" / L"DxCache");
            paths.push_back(local / L"AMD" / L"GLCache");
            paths.push_back(local / L"AMD" / L"DxcCache");
            paths.push_back(local / L"AMD" / L"VkCache");
            
            // Intel
            paths.push_back(local / L"Intel" / L"ShaderCache");
            
            // DirectX shader cache (Windows 10+)
            paths.push_back(local / L"D3DSCache");
            
            // Unreal Engine
            paths.push_back(local / L"UnrealEngine" / L"DerivedDataCache");
            
            // Unity
            paths.push_back(local / L"Unity" / L"cache");
        }
        
        auto appData = getAppDataPath();
        if (!appData.empty()) {
            // Unity Editor cache
            paths.push_back(appData / L"Unity" / L"Asset Store-5.x");
        }
        
        return paths;
    }

    // ========== NOUVEAU: BROWSER EXTENDED ==========

    std::vector<fs::path> Cleaner::getBrowserExtendedPaths() const {
        std::vector<fs::path> paths;
        auto local = getLocalAppDataPath();
        
        if (!local.empty()) {
            // Chrome extended
            fs::path chromeDefault = local / L"Google" / L"Chrome" / L"User Data" / L"Default";
            paths.push_back(chromeDefault / L"Service Worker" / L"CacheStorage");
            paths.push_back(chromeDefault / L"Service Worker" / L"ScriptCache");
            paths.push_back(chromeDefault / L"File System");
            paths.push_back(chromeDefault / L"blob_storage");
            paths.push_back(chromeDefault / L"IndexedDB");
            paths.push_back(chromeDefault / L"Session Storage");
            paths.push_back(chromeDefault / L"Storage" / L"ext");
            
            // Edge extended
            fs::path edgeDefault = local / L"Microsoft" / L"Edge" / L"User Data" / L"Default";
            paths.push_back(edgeDefault / L"Service Worker" / L"CacheStorage");
            paths.push_back(edgeDefault / L"Service Worker" / L"ScriptCache");
            paths.push_back(edgeDefault / L"File System");
            paths.push_back(edgeDefault / L"blob_storage");
            paths.push_back(edgeDefault / L"IndexedDB");
            paths.push_back(edgeDefault / L"Session Storage");
            
            // Brave extended
            fs::path braveDefault = local / L"BraveSoftware" / L"Brave-Browser" / L"User Data" / L"Default";
            paths.push_back(braveDefault / L"Service Worker" / L"CacheStorage");
            paths.push_back(braveDefault / L"File System");
            paths.push_back(braveDefault / L"blob_storage");
            paths.push_back(braveDefault / L"IndexedDB");
        }
        
        return paths;
    }

    // ========== SAUVEGARDE/CHARGEMENT ==========

    void Cleaner::saveOptions(const CleaningOptions& options) {
        std::wstring configPath = getConfigPath();
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
            // Windows supplémentaires
            file << L"DnsCache=" << (options.cleanDnsCache ? 1 : 0) << L"\n";
            file << L"BrokenShortcuts=" << (options.cleanBrokenShortcuts ? 1 : 0) << L"\n";
            file << L"WindowsOld=" << (options.cleanWindowsOld ? 1 : 0) << L"\n";
            file << L"WindowsStoreCache=" << (options.cleanWindowsStoreCache ? 1 : 0) << L"\n";
            file << L"Clipboard=" << (options.cleanClipboard ? 1 : 0) << L"\n";
            file << L"ChkdskFiles=" << (options.cleanChkdskFiles ? 1 : 0) << L"\n";
            file << L"NetworkCache=" << (options.cleanNetworkCache ? 1 : 0) << L"\n";
            // Développement
            file << L"NpmCache=" << (options.cleanNpmCache ? 1 : 0) << L"\n";
            file << L"PipCache=" << (options.cleanPipCache ? 1 : 0) << L"\n";
            file << L"NuGetCache=" << (options.cleanNuGetCache ? 1 : 0) << L"\n";
            file << L"GradleMavenCache=" << (options.cleanGradleMavenCache ? 1 : 0) << L"\n";
            file << L"CargoCache=" << (options.cleanCargoCache ? 1 : 0) << L"\n";
            file << L"GoCache=" << (options.cleanGoCache ? 1 : 0) << L"\n";
            file << L"VSCache=" << (options.cleanVSCache ? 1 : 0) << L"\n";
            file << L"VSCodeCache=" << (options.cleanVSCodeCache ? 1 : 0) << L"\n";
            // Shader
            file << L"ShaderCache=" << (options.cleanShaderCache ? 1 : 0) << L"\n";
            // Système profond
            file << L"ComponentStore=" << (options.cleanComponentStore ? 1 : 0) << L"\n";
            file << L"BrowserExtended=" << (options.cleanBrowserExtended ? 1 : 0) << L"\n";
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
        // Windows supplémentaires
        options.cleanDnsCache = readBool(L"DnsCache");
        options.cleanBrokenShortcuts = readBool(L"BrokenShortcuts");
        options.cleanWindowsOld = readBool(L"WindowsOld");
        options.cleanWindowsStoreCache = readBool(L"WindowsStoreCache");
        options.cleanClipboard = readBool(L"Clipboard");
        options.cleanChkdskFiles = readBool(L"ChkdskFiles");
        options.cleanNetworkCache = readBool(L"NetworkCache");
        // Développement
        options.cleanNpmCache = readBool(L"NpmCache");
        options.cleanPipCache = readBool(L"PipCache");
        options.cleanNuGetCache = readBool(L"NuGetCache");
        options.cleanGradleMavenCache = readBool(L"GradleMavenCache");
        options.cleanCargoCache = readBool(L"CargoCache");
        options.cleanGoCache = readBool(L"GoCache");
        options.cleanVSCache = readBool(L"VSCache");
        options.cleanVSCodeCache = readBool(L"VSCodeCache");
        // Shader
        options.cleanShaderCache = readBool(L"ShaderCache");
        // Système profond
        options.cleanComponentStore = readBool(L"ComponentStore");
        options.cleanBrowserExtended = readBool(L"BrowserExtended");
        
        return options;
    }

} // namespace TempCleaner
