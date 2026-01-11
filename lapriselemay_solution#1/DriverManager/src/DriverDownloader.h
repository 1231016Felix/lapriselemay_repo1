#pragma once

#include "DriverInfo.h"
#include <Windows.h>
#include <winhttp.h>
#include <string>
#include <vector>
#include <queue>
#include <mutex>
#include <atomic>
#include <functional>
#include <thread>
#include <map>

#pragma comment(lib, "winhttp.lib")

namespace DriverManager {

    // Download state for a single driver
    enum class DownloadState {
        Queued,         // In queue, waiting
        Downloading,    // Currently downloading
        Paused,         // Paused by user
        Extracting,     // Extracting .cab file
        ReadyToInstall, // Downloaded and extracted, waiting for install
        Installing,     // Currently installing
        Completed,      // Successfully installed
        Failed,         // Failed (download or install)
        Cancelled       // Cancelled by user
    };

    // Download task for a single driver
    struct DownloadTask {
        // Identification
        std::wstring taskId;
        std::wstring deviceName;
        std::wstring hardwareId;
        std::wstring currentVersion;
        std::wstring newVersion;
        
        // URLs and paths
        std::wstring downloadUrl;
        std::wstring cabFilePath;
        std::wstring extractPath;
        std::wstring infFilePath;
        
        // State
        DownloadState state = DownloadState::Queued;
        std::wstring errorMessage;
        
        // Progress
        uint64_t totalBytes = 0;
        uint64_t downloadedBytes = 0;
        float progress = 0.0f; // 0.0 to 1.0
        
        // Options
        bool autoInstall = true;
        bool selected = true; // For batch selection
        
        // Timing
        time_t queuedTime = 0;
        time_t startTime = 0;
        time_t endTime = 0;
    };

    // Installation options
    struct InstallOptions {
        bool createRestorePoint = false;
        bool backupCurrentDriver = true;
        bool forceInstall = false;
        bool silentInstall = true;
    };

    // Callbacks
    using DownloadProgressCallback = std::function<void(const std::wstring& taskId, float progress, uint64_t downloaded, uint64_t total)>;
    using StateChangeCallback = std::function<void(const std::wstring& taskId, DownloadState newState)>;
    using CompletionCallback = std::function<void(const std::wstring& taskId, bool success, const std::wstring& message)>;

    class DriverDownloader {
    public:
        DriverDownloader();
        ~DriverDownloader();

        // Queue management
        std::wstring QueueDownload(const DriverInfo& driver, const std::wstring& downloadUrl, bool autoInstall = true);
        void RemoveFromQueue(const std::wstring& taskId);
        void ClearQueue();
        void ClearCompleted();
        
        // Control
        void StartDownloads();
        void PauseDownloads();
        void ResumeDownloads();
        void CancelAll();
        void PauseTask(const std::wstring& taskId);
        void ResumeTask(const std::wstring& taskId);
        void CancelTask(const std::wstring& taskId);
        void RetryTask(const std::wstring& taskId);
        
        // Installation
        bool InstallDriver(const std::wstring& taskId, const InstallOptions& options = InstallOptions());
        bool InstallAllReady(const InstallOptions& options = InstallOptions());
        
        // System restore
        static bool CreateSystemRestorePoint(const std::wstring& description);
        
        // Driver backup
        bool BackupDriver(const DriverInfo& driver, const std::wstring& backupPath);
        
        // Queries
        std::vector<DownloadTask> GetAllTasks() const;
        DownloadTask* GetTask(const std::wstring& taskId);
        int GetQueuedCount() const;
        int GetActiveCount() const;
        int GetCompletedCount() const;
        int GetFailedCount() const;
        bool IsDownloading() const { return m_isDownloading; }
        bool IsPaused() const { return m_isPaused; }
        
        // Settings
        void SetMaxConcurrentDownloads(int max) { m_maxConcurrent = max; }
        void SetDownloadDirectory(const std::wstring& path) { m_downloadDir = path; }
        std::wstring GetDownloadDirectory() const { return m_downloadDir; }
        
        // Callbacks
        void SetProgressCallback(DownloadProgressCallback cb) { m_progressCallback = cb; }
        void SetStateChangeCallback(StateChangeCallback cb) { m_stateChangeCallback = cb; }
        void SetCompletionCallback(CompletionCallback cb) { m_completionCallback = cb; }

    private:
        // Internal download/install methods
        void DownloadWorker();
        bool DownloadFile(DownloadTask& task);
        bool ExtractCabFile(DownloadTask& task);
        bool FindInfFile(DownloadTask& task);
        bool InstallDriverInternal(DownloadTask& task, const InstallOptions& options);
        
        // Helper methods
        std::wstring GenerateTaskId();
        void UpdateTaskState(DownloadTask& task, DownloadState newState);
        void NotifyProgress(const DownloadTask& task);
        std::wstring GetDownloadPath() const;
        
        // Data
        std::vector<DownloadTask> m_tasks;
        mutable std::mutex m_mutex;
        
        // Worker thread
        std::thread m_workerThread;
        std::atomic<bool> m_isDownloading{false};
        std::atomic<bool> m_isPaused{false};
        std::atomic<bool> m_shouldStop{false};
        
        // Settings
        int m_maxConcurrent = 2;
        std::wstring m_downloadDir;
        
        // Callbacks
        DownloadProgressCallback m_progressCallback;
        StateChangeCallback m_stateChangeCallback;
        CompletionCallback m_completionCallback;
        
        // Task ID counter
        std::atomic<int> m_taskCounter{0};
        
        // User agent
        const wchar_t* m_userAgent = L"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
    };

    // Helper functions
    std::wstring GetStateText(DownloadState state);
    std::wstring FormatBytes(uint64_t bytes);
    std::wstring FormatSpeed(uint64_t bytesPerSecond);

} // namespace DriverManager
