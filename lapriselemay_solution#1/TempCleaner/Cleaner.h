#pragma once

#include <string>
#include <vector>
#include <functional>
#include <filesystem>
#include <atomic>

namespace TempCleaner {

    // Structure pour les informations d'erreur détaillées
    struct ErrorInfo {
        std::wstring filePath;
        std::wstring errorMessage;
        std::wstring category;  // Catégorie de nettoyage où l'erreur s'est produite
    };

    // Structure pour les statistiques de nettoyage
    struct CleaningStats {
        uint64_t filesDeleted = 0;
        uint64_t bytesFreed = 0;
        uint64_t errors = 0;
        std::vector<ErrorInfo> errorDetails;  // Liste détaillée des erreurs
    };

    // Options de nettoyage
    struct CleaningOptions {
        // Nettoyage de base
        bool cleanUserTemp = true;
        bool cleanWindowsTemp = true;
        bool cleanPrefetch = false;
        bool cleanRecent = false;
        bool cleanRecycleBin = false;
        bool cleanBrowserCache = false;
        
        // Nettoyage système avancé
        bool cleanWindowsUpdate = false;
        bool cleanSystemLogs = false;
        bool cleanCrashDumps = false;
        bool cleanThumbnails = false;
        bool cleanDeliveryOptimization = false;
        bool cleanWindowsInstaller = false;
        bool cleanFontCache = false;
    };

    // Callback pour le progrès
    using ProgressCallback = std::function<void(const std::wstring& currentFile, int percentage)>;

    class Cleaner {
    public:
        Cleaner();
        ~Cleaner() = default;

        // Lance le nettoyage (retourne les stats)
        CleaningStats clean(const CleaningOptions& options, ProgressCallback progressCallback = nullptr);

        // Arrête le nettoyage en cours
        void stop();

        // Vérifie si un nettoyage est en cours
        bool isRunning() const;

        // Sauvegarde/charge les options
        static void saveOptions(const CleaningOptions& options);
        static CleaningOptions loadOptions();

    private:
        std::atomic<bool> m_running{ false };
        std::atomic<bool> m_stopRequested{ false };

        // Méthodes de nettoyage pour chaque type
        void cleanDirectory(const std::filesystem::path& path, CleaningStats& stats, ProgressCallback callback, const std::wstring& category = L"");
        void cleanDirectoryContents(const std::filesystem::path& path, CleaningStats& stats, const std::wstring& category = L"");
        void cleanRecycleBin(CleaningStats& stats);
        void cleanEventLogs(CleaningStats& stats);
        void cleanWithCommand(const std::wstring& command, CleaningStats& stats);
        
        // Récupère les chemins des dossiers
        std::filesystem::path getUserTempPath() const;
        std::filesystem::path getWindowsTempPath() const;
        std::filesystem::path getPrefetchPath() const;
        std::filesystem::path getRecentPath() const;
        std::filesystem::path getWindowsUpdateCachePath() const;
        std::filesystem::path getDeliveryOptimizationPath() const;
        std::filesystem::path getThumbnailCachePath() const;
        std::filesystem::path getWindowsInstallerPatchPath() const;
        std::filesystem::path getFontCachePath() const;
        std::vector<std::filesystem::path> getSystemLogPaths() const;
        std::vector<std::filesystem::path> getCrashDumpPaths() const;
        std::vector<std::filesystem::path> getBrowserCachePaths() const;
    };

} // namespace TempCleaner
