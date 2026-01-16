#pragma once

#include "../DriverScanner.h"
#include "../DriverInfo.h"
#include "../UpdateChecker.h"
#include "../DriverStoreCleanup.h"
#include "../DriverDownloader.h"
#include "../BSODAnalyzer.h"

#include <atomic>
#include <mutex>
#include <future>
#include <set>
#include <string>

namespace DriverManager {

    /// <summary>
    /// État global de l'application avec synchronisation thread-safe
    /// </summary>
    struct AppState {
        // ========== Services principaux ==========
        DriverScanner scanner;
        UpdateChecker updateChecker;
        DriverStoreCleanup driverStoreCleanup;
        DriverDownloader driverDownloader;
        BSODAnalyzer bsodAnalyzer;

        // ========== Flags atomiques (thread-safe sans lock) ==========
        std::atomic<bool> isScanning{false};
        std::atomic<bool> isCheckingUpdates{false};
        std::atomic<bool> cancelUpdateCheck{false};
        std::atomic<bool> isCleaningDriverStore{false};
        std::atomic<bool> isScanningBSOD{false};
        std::atomic<bool> needsDriverStoreRefresh{false};
        
        std::atomic<float> scanProgress{0.0f};
        std::atomic<float> updateCheckProgress{0.0f};
        std::atomic<int> updatesFound{0};
        std::atomic<int> updateSource{0};  // 0=none, 1=Touslesdrivers, 2=Windows Update
        std::atomic<int> totalDriversToCheck{0};
        std::atomic<int> driversChecked{0};
        std::atomic<int> lastDeletedCount{0};
        std::atomic<int> bsodScanProgress{0};
        std::atomic<int> bsodScanTotal{0};

        // ========== États d'interface (non-atomiques, thread principal seulement) ==========
        bool showDetailsWindow = false;
        bool showAboutWindow = false;
        bool showExportDialog = false;
        bool showUpdateHelpWindow = false;
        bool showUpdatesWindow = false;
        bool showUpdateProgressWindow = false;
        bool showDriverStoreCleanup = false;
        bool showDownloadWindow = false;
        bool showBSODAnalyzer = false;
        bool createRestorePoint = false;
        bool filterOldDrivers = false;
        bool filterUpdatesAvailable = false;

        // ========== Données protégées par mutex ==========
        mutable std::mutex dataMutex;
        std::wstring currentScanItem;
        std::wstring currentUpdateItem;
        std::wstring bsodCurrentItem;
        std::string statusMessage;
        std::string searchFilter;

        // ========== Sélection et tri (thread principal) ==========
        DriverInfo* selectedDriver = nullptr;
        int selectedCategory = -1;  // -1 = all
        int sortColumnIndex = 0;
        bool sortAscending = true;
        bool sortSpecsInitialized = false;
        std::set<std::wstring> expandedGroups;

        // ========== Futures pour les tâches async ==========
        std::future<void> scanFuture;
        std::future<void> updateCheckFuture;
        std::future<void> bsodScanFuture;

        // ========== Accesseurs thread-safe ==========
        
        std::wstring GetCurrentScanItem() const {
            std::lock_guard<std::mutex> lock(dataMutex);
            return currentScanItem;
        }
        
        void SetCurrentScanItem(const std::wstring& item) {
            std::lock_guard<std::mutex> lock(dataMutex);
            currentScanItem = item;
        }
        
        std::wstring GetCurrentUpdateItem() const {
            std::lock_guard<std::mutex> lock(dataMutex);
            return currentUpdateItem;
        }
        
        void SetCurrentUpdateItem(const std::wstring& item) {
            std::lock_guard<std::mutex> lock(dataMutex);
            currentUpdateItem = item;
        }
        
        std::wstring GetBsodCurrentItem() const {
            std::lock_guard<std::mutex> lock(dataMutex);
            return bsodCurrentItem;
        }
        
        void SetBsodCurrentItem(const std::wstring& item) {
            std::lock_guard<std::mutex> lock(dataMutex);
            bsodCurrentItem = item;
        }
        
        std::string GetStatusMessage() const {
            std::lock_guard<std::mutex> lock(dataMutex);
            return statusMessage;
        }
        
        void SetStatusMessage(const std::string& msg) {
            std::lock_guard<std::mutex> lock(dataMutex);
            statusMessage = msg;
        }
        
        std::string GetSearchFilter() const {
            std::lock_guard<std::mutex> lock(dataMutex);
            return searchFilter;
        }
        
        void SetSearchFilter(const std::string& filter) {
            std::lock_guard<std::mutex> lock(dataMutex);
            searchFilter = filter;
        }

        // ========== Méthodes utilitaires ==========
        
        void ResetScanState() {
            isScanning = false;
            scanProgress = 0.0f;
            SetCurrentScanItem(L"");
        }
        
        void ResetUpdateCheckState() {
            isCheckingUpdates = false;
            cancelUpdateCheck = false;
            updateCheckProgress = 0.0f;
            updatesFound = 0;
            driversChecked = 0;
            SetCurrentUpdateItem(L"");
        }
        
        void ResetBsodScanState() {
            isScanningBSOD = false;
            bsodScanProgress = 0;
            bsodScanTotal = 0;
            SetBsodCurrentItem(L"");
        }
        
        /// <summary>
        /// Vérifie si une tâche async est encore en cours
        /// </summary>
        bool IsAnyTaskRunning() const {
            return isScanning.load() || 
                   isCheckingUpdates.load() || 
                   isScanningBSOD.load() ||
                   isCleaningDriverStore.load();
        }
        
        /// <summary>
        /// Annule toutes les tâches en cours
        /// </summary>
        void CancelAllTasks() {
            if (isScanning.load()) {
                scanner.CancelScan();
            }
            if (isCheckingUpdates.load()) {
                cancelUpdateCheck = true;
                updateChecker.CancelCheck();
            }
        }
    };

} // namespace DriverManager
