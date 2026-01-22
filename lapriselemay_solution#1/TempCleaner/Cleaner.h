#pragma once

#include <string>
#include <vector>
#include <functional>
#include <filesystem>
#include <atomic>

namespace TempCleaner {

    // Structure pour les statistiques de nettoyage
    struct CleaningStats {
        uint64_t filesDeleted = 0;
        uint64_t bytesFreed = 0;
        uint64_t errors = 0;
    };

    // Options de nettoyage
    struct CleaningOptions {
        bool cleanUserTemp = true;
        bool cleanWindowsTemp = true;
        bool cleanPrefetch = false;
        bool cleanRecent = false;
        bool cleanRecycleBin = false;
        bool cleanBrowserCache = false;
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
        void cleanDirectory(const std::filesystem::path& path, CleaningStats& stats, ProgressCallback callback);
        void cleanRecycleBin(CleaningStats& stats);
        
        // Récupère les chemins des dossiers temporaires
        std::filesystem::path getUserTempPath() const;
        std::filesystem::path getWindowsTempPath() const;
        std::filesystem::path getPrefetchPath() const;
        std::filesystem::path getRecentPath() const;
        std::vector<std::filesystem::path> getBrowserCachePaths() const;
    };

} // namespace TempCleaner
