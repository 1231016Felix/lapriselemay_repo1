#include "DriverDownloader.h"
#include <ShlObj.h>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <iomanip>
#include <chrono>
#include <algorithm>

// For System Restore
#include <comdef.h>
#include <wbemidl.h>
#pragma comment(lib, "wbemuuid.lib")

namespace DriverManager {

    // ============================================================================
    // Helper functions
    // ============================================================================
    
    std::wstring GetStateText(DownloadState state) {
        switch (state) {
            case DownloadState::Queued:         return L"En attente";
            case DownloadState::Downloading:    return L"Téléchargement...";
            case DownloadState::Paused:         return L"En pause";
            case DownloadState::Extracting:     return L"Extraction...";
            case DownloadState::ReadyToInstall: return L"Prêt à installer";
            case DownloadState::Installing:     return L"Installation...";
            case DownloadState::Completed:      return L"Terminé";
            case DownloadState::Failed:         return L"Échec";
            case DownloadState::Cancelled:      return L"Annulé";
            default:                            return L"Inconnu";
        }
    }

    std::wstring FormatBytes(uint64_t bytes) {
        const wchar_t* units[] = { L"o", L"Ko", L"Mo", L"Go" };
        int unitIndex = 0;
        double size = static_cast<double>(bytes);
        
        while (size >= 1024.0 && unitIndex < 3) {
            size /= 1024.0;
            unitIndex++;
        }
        
        wchar_t buf[32];
        if (unitIndex == 0) {
            swprintf_s(buf, L"%llu %s", bytes, units[unitIndex]);
        } else {
            swprintf_s(buf, L"%.1f %s", size, units[unitIndex]);
        }
        return buf;
    }

    std::wstring FormatSpeed(uint64_t bytesPerSecond) {
        return FormatBytes(bytesPerSecond) + L"/s";
    }

    // ============================================================================
    // Constructor / Destructor
    // ============================================================================

    DriverDownloader::DriverDownloader() {
        // Set default download directory
        wchar_t path[MAX_PATH];
        if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_LOCAL_APPDATA, nullptr, 0, path))) {
            m_downloadDir = std::wstring(path) + L"\\DriverManager\\Downloads";
            std::filesystem::create_directories(m_downloadDir);
        }
    }

    DriverDownloader::~DriverDownloader() {
        m_shouldStop = true;
        if (m_workerThread.joinable()) {
            m_workerThread.join();
        }
    }

    // ============================================================================
    // Queue Management
    // ============================================================================

    std::wstring DriverDownloader::GenerateTaskId() {
        int id = m_taskCounter.fetch_add(1);
        wchar_t buf[32];
        swprintf_s(buf, L"task_%d_%lld", id, std::chrono::system_clock::now().time_since_epoch().count());
        return buf;
    }

    std::wstring DriverDownloader::QueueDownload(const DriverInfo& driver, 
                                                  const std::wstring& downloadUrl, 
                                                  bool autoInstall) {
        std::lock_guard<std::mutex> lock(m_mutex);
        
        DownloadTask task;
        task.taskId = GenerateTaskId();
        task.deviceName = driver.deviceName;
        task.hardwareId = driver.hardwareId;
        task.currentVersion = driver.driverVersion;
        task.newVersion = driver.availableUpdate.newVersion;
        task.downloadUrl = downloadUrl;
        task.state = DownloadState::Queued;
        task.autoInstall = autoInstall;
        task.queuedTime = std::time(nullptr);
        
        // Generate file paths
        std::wstring safeName = task.deviceName;
        // Remove invalid filename characters
        for (wchar_t& c : safeName) {
            if (c == L'\\' || c == L'/' || c == L':' || c == L'*' || 
                c == L'?' || c == L'"' || c == L'<' || c == L'>' || c == L'|') {
                c = L'_';
            }
        }
        
        task.cabFilePath = m_downloadDir + L"\\" + safeName + L".cab";
        task.extractPath = m_downloadDir + L"\\" + safeName + L"_extracted";
        
        m_tasks.push_back(task);
        
        return task.taskId;
    }

    void DriverDownloader::RemoveFromQueue(const std::wstring& taskId) {
        std::lock_guard<std::mutex> lock(m_mutex);
        
        auto it = std::find_if(m_tasks.begin(), m_tasks.end(),
            [&taskId](const DownloadTask& t) { return t.taskId == taskId; });
        
        if (it != m_tasks.end()) {
            // Only remove if not currently downloading
            if (it->state == DownloadState::Queued || 
                it->state == DownloadState::Completed ||
                it->state == DownloadState::Failed ||
                it->state == DownloadState::Cancelled) {
                m_tasks.erase(it);
            }
        }
    }

    void DriverDownloader::ClearQueue() {
        std::lock_guard<std::mutex> lock(m_mutex);
        
        // Remove only queued items
        m_tasks.erase(
            std::remove_if(m_tasks.begin(), m_tasks.end(),
                [](const DownloadTask& t) { return t.state == DownloadState::Queued; }),
            m_tasks.end());
    }

    void DriverDownloader::ClearCompleted() {
        std::lock_guard<std::mutex> lock(m_mutex);
        
        m_tasks.erase(
            std::remove_if(m_tasks.begin(), m_tasks.end(),
                [](const DownloadTask& t) { 
                    return t.state == DownloadState::Completed || 
                           t.state == DownloadState::Failed ||
                           t.state == DownloadState::Cancelled; 
                }),
            m_tasks.end());
    }

    // ============================================================================
    // Control Methods
    // ============================================================================

    void DriverDownloader::StartDownloads() {
        if (m_isDownloading) return;
        
        m_isDownloading = true;
        m_isPaused = false;
        m_shouldStop = false;
        
        // Start worker thread
        if (m_workerThread.joinable()) {
            m_workerThread.join();
        }
        m_workerThread = std::thread(&DriverDownloader::DownloadWorker, this);
    }

    void DriverDownloader::PauseDownloads() {
        m_isPaused = true;
    }

    void DriverDownloader::ResumeDownloads() {
        m_isPaused = false;
        if (!m_isDownloading) {
            StartDownloads();
        }
    }

    void DriverDownloader::CancelAll() {
        m_shouldStop = true;
        m_isDownloading = false;
        
        std::lock_guard<std::mutex> lock(m_mutex);
        for (auto& task : m_tasks) {
            if (task.state == DownloadState::Queued || 
                task.state == DownloadState::Downloading ||
                task.state == DownloadState::Paused) {
                task.state = DownloadState::Cancelled;
            }
        }
    }

    void DriverDownloader::PauseTask(const std::wstring& taskId) {
        std::lock_guard<std::mutex> lock(m_mutex);
        for (auto& task : m_tasks) {
            if (task.taskId == taskId && task.state == DownloadState::Downloading) {
                task.state = DownloadState::Paused;
                break;
            }
        }
    }

    void DriverDownloader::ResumeTask(const std::wstring& taskId) {
        std::lock_guard<std::mutex> lock(m_mutex);
        for (auto& task : m_tasks) {
            if (task.taskId == taskId && task.state == DownloadState::Paused) {
                task.state = DownloadState::Queued;
                break;
            }
        }
        
        if (!m_isDownloading) {
            StartDownloads();
        }
    }

    void DriverDownloader::CancelTask(const std::wstring& taskId) {
        std::lock_guard<std::mutex> lock(m_mutex);
        for (auto& task : m_tasks) {
            if (task.taskId == taskId) {
                task.state = DownloadState::Cancelled;
                break;
            }
        }
    }

    void DriverDownloader::RetryTask(const std::wstring& taskId) {
        std::lock_guard<std::mutex> lock(m_mutex);
        for (auto& task : m_tasks) {
            if (task.taskId == taskId && 
                (task.state == DownloadState::Failed || task.state == DownloadState::Cancelled)) {
                task.state = DownloadState::Queued;
                task.errorMessage.clear();
                task.downloadedBytes = 0;
                task.progress = 0.0f;
                break;
            }
        }
        
        if (!m_isDownloading) {
            StartDownloads();
        }
    }

    // ============================================================================
    // Download Worker Thread
    // ============================================================================

    void DriverDownloader::DownloadWorker() {
        while (!m_shouldStop) {
            // Wait if paused
            if (m_isPaused) {
                std::this_thread::sleep_for(std::chrono::milliseconds(100));
                continue;
            }
            
            // Find next task to process
            DownloadTask* taskToProcess = nullptr;
            {
                std::lock_guard<std::mutex> lock(m_mutex);
                
                // Count active downloads
                int activeCount = 0;
                for (auto& task : m_tasks) {
                    if (task.state == DownloadState::Downloading) {
                        activeCount++;
                    }
                }
                
                // Find a queued task if we have capacity
                if (activeCount < m_maxConcurrent) {
                    for (auto& task : m_tasks) {
                        if (task.state == DownloadState::Queued) {
                            taskToProcess = &task;
                            task.state = DownloadState::Downloading;
                            task.startTime = std::time(nullptr);
                            break;
                        }
                    }
                }
            }
            
            if (taskToProcess) {
                // Process this task
                bool success = DownloadFile(*taskToProcess);
                
                if (success && !m_shouldStop) {
                    // Extract the cab file
                    UpdateTaskState(*taskToProcess, DownloadState::Extracting);
                    success = ExtractCabFile(*taskToProcess);
                    
                    if (success) {
                        success = FindInfFile(*taskToProcess);
                    }
                }
                
                if (success && !m_shouldStop) {
                    if (taskToProcess->autoInstall) {
                        // Auto-install
                        UpdateTaskState(*taskToProcess, DownloadState::ReadyToInstall);
                    } else {
                        UpdateTaskState(*taskToProcess, DownloadState::ReadyToInstall);
                    }
                    taskToProcess->endTime = std::time(nullptr);
                    
                    if (m_completionCallback) {
                        m_completionCallback(taskToProcess->taskId, true, L"Téléchargement terminé");
                    }
                } else if (!m_shouldStop && taskToProcess->state != DownloadState::Cancelled) {
                    UpdateTaskState(*taskToProcess, DownloadState::Failed);
                    taskToProcess->endTime = std::time(nullptr);
                    
                    if (m_completionCallback) {
                        m_completionCallback(taskToProcess->taskId, false, taskToProcess->errorMessage);
                    }
                }
            } else {
                // No work to do, check if we should exit
                bool hasWork = false;
                {
                    std::lock_guard<std::mutex> lock(m_mutex);
                    for (const auto& task : m_tasks) {
                        if (task.state == DownloadState::Queued || 
                            task.state == DownloadState::Downloading) {
                            hasWork = true;
                            break;
                        }
                    }
                }
                
                if (!hasWork) {
                    break; // Exit worker thread
                }
                
                std::this_thread::sleep_for(std::chrono::milliseconds(100));
            }
        }
        
        m_isDownloading = false;
    }

    // ============================================================================
    // Download Implementation
    // ============================================================================

    bool DriverDownloader::DownloadFile(DownloadTask& task) {
        if (task.downloadUrl.empty()) {
            task.errorMessage = L"URL de téléchargement manquante";
            return false;
        }

        // Parse URL
        URL_COMPONENTS urlComp = {};
        urlComp.dwStructSize = sizeof(urlComp);
        
        wchar_t hostName[256] = {};
        wchar_t urlPath[2048] = {};
        urlComp.lpszHostName = hostName;
        urlComp.dwHostNameLength = 256;
        urlComp.lpszUrlPath = urlPath;
        urlComp.dwUrlPathLength = 2048;
        
        if (!WinHttpCrackUrl(task.downloadUrl.c_str(), 0, 0, &urlComp)) {
            task.errorMessage = L"URL invalide";
            return false;
        }

        // Open session
        HINTERNET hSession = WinHttpOpen(m_userAgent, WINHTTP_ACCESS_TYPE_DEFAULT_PROXY,
            WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
        if (!hSession) {
            task.errorMessage = L"Impossible d'ouvrir la session HTTP";
            return false;
        }

        // Set timeouts
        DWORD timeout = 30000;
        WinHttpSetOption(hSession, WINHTTP_OPTION_CONNECT_TIMEOUT, &timeout, sizeof(timeout));
        WinHttpSetOption(hSession, WINHTTP_OPTION_RECEIVE_TIMEOUT, &timeout, sizeof(timeout));

        // Connect
        HINTERNET hConnect = WinHttpConnect(hSession, hostName, urlComp.nPort, 0);
        if (!hConnect) {
            WinHttpCloseHandle(hSession);
            task.errorMessage = L"Impossible de se connecter au serveur";
            return false;
        }

        // Open request
        DWORD flags = (urlComp.nScheme == INTERNET_SCHEME_HTTPS) ? WINHTTP_FLAG_SECURE : 0;
        HINTERNET hRequest = WinHttpOpenRequest(hConnect, L"GET", urlPath,
            nullptr, WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, flags);
        if (!hRequest) {
            WinHttpCloseHandle(hConnect);
            WinHttpCloseHandle(hSession);
            task.errorMessage = L"Impossible d'ouvrir la requête";
            return false;
        }

        // Send request
        if (!WinHttpSendRequest(hRequest, WINHTTP_NO_ADDITIONAL_HEADERS, 0,
            WINHTTP_NO_REQUEST_DATA, 0, 0, 0)) {
            WinHttpCloseHandle(hRequest);
            WinHttpCloseHandle(hConnect);
            WinHttpCloseHandle(hSession);
            task.errorMessage = L"Impossible d'envoyer la requête";
            return false;
        }

        if (!WinHttpReceiveResponse(hRequest, nullptr)) {
            WinHttpCloseHandle(hRequest);
            WinHttpCloseHandle(hConnect);
            WinHttpCloseHandle(hSession);
            task.errorMessage = L"Pas de réponse du serveur";
            return false;
        }

        // Get content length
        DWORD contentLength = 0;
        DWORD bufferSize = sizeof(contentLength);
        WinHttpQueryHeaders(hRequest, 
            WINHTTP_QUERY_CONTENT_LENGTH | WINHTTP_QUERY_FLAG_NUMBER,
            WINHTTP_HEADER_NAME_BY_INDEX, &contentLength, &bufferSize, 
            WINHTTP_NO_HEADER_INDEX);
        task.totalBytes = contentLength;

        // Create output file
        std::filesystem::create_directories(std::filesystem::path(task.cabFilePath).parent_path());
        std::ofstream outFile(task.cabFilePath, std::ios::binary);
        if (!outFile.is_open()) {
            WinHttpCloseHandle(hRequest);
            WinHttpCloseHandle(hConnect);
            WinHttpCloseHandle(hSession);
            task.errorMessage = L"Impossible de créer le fichier de destination";
            return false;
        }

        // Download data
        bool success = true;
        DWORD bytesAvailable = 0;
        
        do {
            // Check for cancellation or pause
            if (m_shouldStop || task.state == DownloadState::Cancelled) {
                success = false;
                break;
            }
            
            while (task.state == DownloadState::Paused && !m_shouldStop) {
                std::this_thread::sleep_for(std::chrono::milliseconds(100));
            }

            bytesAvailable = 0;
            if (!WinHttpQueryDataAvailable(hRequest, &bytesAvailable)) {
                break;
            }
            
            if (bytesAvailable > 0) {
                std::vector<char> buffer(bytesAvailable);
                DWORD bytesRead = 0;
                
                if (WinHttpReadData(hRequest, buffer.data(), bytesAvailable, &bytesRead)) {
                    outFile.write(buffer.data(), bytesRead);
                    task.downloadedBytes += bytesRead;
                    
                    if (task.totalBytes > 0) {
                        task.progress = static_cast<float>(task.downloadedBytes) / task.totalBytes;
                    }
                    
                    NotifyProgress(task);
                }
            }
        } while (bytesAvailable > 0);

        outFile.close();
        WinHttpCloseHandle(hRequest);
        WinHttpCloseHandle(hConnect);
        WinHttpCloseHandle(hSession);

        if (success && task.downloadedBytes == 0) {
            task.errorMessage = L"Aucune donnée téléchargée";
            success = false;
        }

        return success;
    }

    // ============================================================================
    // Extraction Implementation
    // ============================================================================

    bool DriverDownloader::ExtractCabFile(DownloadTask& task) {
        // Create extraction directory
        std::filesystem::create_directories(task.extractPath);
        
        // Use expand.exe to extract .cab file
        std::wstring command = L"expand \"" + task.cabFilePath + L"\" -F:* \"" + task.extractPath + L"\"";
        
        STARTUPINFOW si = { sizeof(si) };
        si.dwFlags = STARTF_USESHOWWINDOW;
        si.wShowWindow = SW_HIDE;
        PROCESS_INFORMATION pi;
        
        if (!CreateProcessW(nullptr, const_cast<wchar_t*>(command.c_str()), 
            nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi)) {
            task.errorMessage = L"Impossible de lancer l'extraction";
            return false;
        }
        
        // Wait for extraction with timeout
        DWORD waitResult = WaitForSingleObject(pi.hProcess, 60000); // 60 second timeout
        
        if (waitResult == WAIT_TIMEOUT) {
            TerminateProcess(pi.hProcess, 1);
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
            task.errorMessage = L"Extraction trop longue (timeout)";
            return false;
        }
        
        DWORD exitCode;
        GetExitCodeProcess(pi.hProcess, &exitCode);
        CloseHandle(pi.hProcess);
        CloseHandle(pi.hThread);
        
        if (exitCode != 0) {
            task.errorMessage = L"Échec de l'extraction (code " + std::to_wstring(exitCode) + L")";
            return false;
        }
        
        return true;
    }

    bool DriverDownloader::FindInfFile(DownloadTask& task) {
        // Search for .inf file in extracted directory
        try {
            for (const auto& entry : std::filesystem::recursive_directory_iterator(task.extractPath)) {
                if (entry.is_regular_file()) {
                    std::wstring ext = entry.path().extension().wstring();
                    std::transform(ext.begin(), ext.end(), ext.begin(), ::towlower);
                    
                    if (ext == L".inf") {
                        task.infFilePath = entry.path().wstring();
                        return true;
                    }
                }
            }
        } catch (...) {
            task.errorMessage = L"Erreur lors de la recherche du fichier INF";
            return false;
        }
        
        task.errorMessage = L"Fichier INF non trouvé dans l'archive";
        return false;
    }

    // ============================================================================
    // Installation Implementation
    // ============================================================================

    bool DriverDownloader::InstallDriver(const std::wstring& taskId, const InstallOptions& options) {
        DownloadTask* task = nullptr;
        {
            std::lock_guard<std::mutex> lock(m_mutex);
            for (auto& t : m_tasks) {
                if (t.taskId == taskId) {
                    task = &t;
                    break;
                }
            }
        }
        
        if (!task) return false;
        if (task->state != DownloadState::ReadyToInstall) return false;
        
        return InstallDriverInternal(*task, options);
    }

    bool DriverDownloader::InstallAllReady(const InstallOptions& options) {
        std::vector<DownloadTask*> tasksToInstall;
        
        {
            std::lock_guard<std::mutex> lock(m_mutex);
            for (auto& task : m_tasks) {
                if (task.state == DownloadState::ReadyToInstall && task.selected) {
                    tasksToInstall.push_back(&task);
                }
            }
        }
        
        if (tasksToInstall.empty()) return true;
        
        // Create restore point if requested (only once for all drivers)
        if (options.createRestorePoint) {
            CreateSystemRestorePoint(L"Avant installation de pilotes - DriverManager");
        }
        
        bool allSuccess = true;
        for (auto* task : tasksToInstall) {
            InstallOptions taskOptions = options;
            taskOptions.createRestorePoint = false; // Already created
            
            if (!InstallDriverInternal(*task, taskOptions)) {
                allSuccess = false;
            }
        }
        
        return allSuccess;
    }

    bool DriverDownloader::InstallDriverInternal(DownloadTask& task, const InstallOptions& options) {
        UpdateTaskState(task, DownloadState::Installing);
        
        if (task.infFilePath.empty()) {
            task.errorMessage = L"Fichier INF non trouvé";
            UpdateTaskState(task, DownloadState::Failed);
            return false;
        }
        
        // Build pnputil command
        std::wstring command = L"pnputil /add-driver \"" + task.infFilePath + L"\" /install";
        
        if (options.forceInstall) {
            command += L" /force";
        }
        
        STARTUPINFOW si = { sizeof(si) };
        si.dwFlags = STARTF_USESHOWWINDOW;
        si.wShowWindow = SW_HIDE;
        PROCESS_INFORMATION pi;
        
        // Create pipes for output capture
        SECURITY_ATTRIBUTES sa = { sizeof(sa), nullptr, TRUE };
        HANDLE hReadPipe, hWritePipe;
        CreatePipe(&hReadPipe, &hWritePipe, &sa, 0);
        SetHandleInformation(hReadPipe, HANDLE_FLAG_INHERIT, 0);
        
        si.hStdOutput = hWritePipe;
        si.hStdError = hWritePipe;
        si.dwFlags |= STARTF_USESTDHANDLES;
        
        if (!CreateProcessW(nullptr, const_cast<wchar_t*>(command.c_str()), 
            nullptr, nullptr, TRUE, CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi)) {
            CloseHandle(hReadPipe);
            CloseHandle(hWritePipe);
            task.errorMessage = L"Impossible de lancer pnputil";
            UpdateTaskState(task, DownloadState::Failed);
            return false;
        }
        
        CloseHandle(hWritePipe);
        
        // Wait for installation
        DWORD waitResult = WaitForSingleObject(pi.hProcess, 300000); // 5 minute timeout
        
        // Read output
        std::string output;
        char buffer[4096];
        DWORD bytesRead;
        while (ReadFile(hReadPipe, buffer, sizeof(buffer) - 1, &bytesRead, nullptr) && bytesRead > 0) {
            buffer[bytesRead] = '\0';
            output += buffer;
        }
        CloseHandle(hReadPipe);
        
        if (waitResult == WAIT_TIMEOUT) {
            TerminateProcess(pi.hProcess, 1);
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
            task.errorMessage = L"Installation trop longue (timeout)";
            UpdateTaskState(task, DownloadState::Failed);
            return false;
        }
        
        DWORD exitCode;
        GetExitCodeProcess(pi.hProcess, &exitCode);
        CloseHandle(pi.hProcess);
        CloseHandle(pi.hThread);
        
        // Check result
        bool success = (exitCode == 0) || 
                       (output.find("successfully") != std::string::npos) ||
                       (output.find("added") != std::string::npos);
        
        if (success) {
            UpdateTaskState(task, DownloadState::Completed);
            task.endTime = std::time(nullptr);
            
            if (m_completionCallback) {
                m_completionCallback(task.taskId, true, L"Installation réussie");
            }
        } else {
            task.errorMessage = L"Échec de l'installation (code " + std::to_wstring(exitCode) + L")";
            UpdateTaskState(task, DownloadState::Failed);
            
            if (m_completionCallback) {
                m_completionCallback(task.taskId, false, task.errorMessage);
            }
        }
        
        return success;
    }

    // ============================================================================
    // System Restore Point
    // ============================================================================

    bool DriverDownloader::CreateSystemRestorePoint(const std::wstring& description) {
        // Use WMI to create system restore point
        HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        if (FAILED(hr) && hr != RPC_E_CHANGED_MODE) {
            return false;
        }
        
        hr = CoInitializeSecurity(nullptr, -1, nullptr, nullptr,
            RPC_C_AUTHN_LEVEL_DEFAULT, RPC_C_IMP_LEVEL_IMPERSONATE,
            nullptr, EOAC_NONE, nullptr);
        
        IWbemLocator* pLoc = nullptr;
        hr = CoCreateInstance(CLSID_WbemLocator, nullptr, CLSCTX_INPROC_SERVER,
            IID_IWbemLocator, reinterpret_cast<LPVOID*>(&pLoc));
        
        if (FAILED(hr)) {
            CoUninitialize();
            return false;
        }
        
        IWbemServices* pSvc = nullptr;
        hr = pLoc->ConnectServer(_bstr_t(L"ROOT\\DEFAULT"), nullptr, nullptr, nullptr,
            0, nullptr, nullptr, &pSvc);
        
        if (FAILED(hr)) {
            pLoc->Release();
            CoUninitialize();
            return false;
        }
        
        // Set security
        hr = CoSetProxyBlanket(pSvc, RPC_C_AUTHN_WINNT, RPC_C_AUTHZ_NONE, nullptr,
            RPC_C_AUTHN_LEVEL_CALL, RPC_C_IMP_LEVEL_IMPERSONATE, nullptr, EOAC_NONE);
        
        // Get SystemRestore class
        IWbemClassObject* pClass = nullptr;
        hr = pSvc->GetObject(_bstr_t(L"SystemRestore"), 0, nullptr, &pClass, nullptr);
        
        if (FAILED(hr)) {
            pSvc->Release();
            pLoc->Release();
            CoUninitialize();
            return false;
        }
        
        // Get CreateRestorePoint method
        IWbemClassObject* pInParamsDefinition = nullptr;
        hr = pClass->GetMethod(L"CreateRestorePoint", 0, &pInParamsDefinition, nullptr);
        
        if (FAILED(hr)) {
            pClass->Release();
            pSvc->Release();
            pLoc->Release();
            CoUninitialize();
            return false;
        }
        
        // Create instance of input parameters
        IWbemClassObject* pInParams = nullptr;
        hr = pInParamsDefinition->SpawnInstance(0, &pInParams);
        
        // Set parameters
        VARIANT var;
        var.vt = VT_BSTR;
        var.bstrVal = SysAllocString(description.c_str());
        pInParams->Put(L"Description", 0, &var, 0);
        VariantClear(&var);
        
        var.vt = VT_I4;
        var.lVal = 10; // APPLICATION_INSTALL
        pInParams->Put(L"RestorePointType", 0, &var, 0);
        
        var.lVal = 100; // BEGIN_SYSTEM_CHANGE
        pInParams->Put(L"EventType", 0, &var, 0);
        
        // Execute method
        IWbemClassObject* pOutParams = nullptr;
        hr = pSvc->ExecMethod(_bstr_t(L"SystemRestore"), _bstr_t(L"CreateRestorePoint"),
            0, nullptr, pInParams, &pOutParams, nullptr);
        
        bool success = SUCCEEDED(hr);
        
        // Cleanup
        if (pOutParams) pOutParams->Release();
        pInParams->Release();
        pInParamsDefinition->Release();
        pClass->Release();
        pSvc->Release();
        pLoc->Release();
        CoUninitialize();
        
        return success;
    }

    // ============================================================================
    // Driver Backup
    // ============================================================================

    bool DriverDownloader::BackupDriver(const DriverInfo& driver, const std::wstring& backupPath) {
        if (driver.infPath.empty()) {
            return false;
        }
        
        std::filesystem::create_directories(backupPath);
        
        std::wstring command = L"pnputil /export-driver \"" + driver.infPath + L"\" \"" + backupPath + L"\"";
        
        STARTUPINFOW si = { sizeof(si) };
        si.dwFlags = STARTF_USESHOWWINDOW;
        si.wShowWindow = SW_HIDE;
        PROCESS_INFORMATION pi;
        
        if (!CreateProcessW(nullptr, const_cast<wchar_t*>(command.c_str()), 
            nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi)) {
            return false;
        }
        
        WaitForSingleObject(pi.hProcess, INFINITE);
        
        DWORD exitCode;
        GetExitCodeProcess(pi.hProcess, &exitCode);
        CloseHandle(pi.hProcess);
        CloseHandle(pi.hThread);
        
        return exitCode == 0;
    }

    // ============================================================================
    // Query Methods
    // ============================================================================

    std::vector<DownloadTask> DriverDownloader::GetAllTasks() const {
        std::lock_guard<std::mutex> lock(m_mutex);
        return m_tasks;
    }

    DownloadTask* DriverDownloader::GetTask(const std::wstring& taskId) {
        std::lock_guard<std::mutex> lock(m_mutex);
        for (auto& task : m_tasks) {
            if (task.taskId == taskId) {
                return &task;
            }
        }
        return nullptr;
    }

    int DriverDownloader::GetQueuedCount() const {
        std::lock_guard<std::mutex> lock(m_mutex);
        int count = 0;
        for (const auto& task : m_tasks) {
            if (task.state == DownloadState::Queued) count++;
        }
        return count;
    }

    int DriverDownloader::GetActiveCount() const {
        std::lock_guard<std::mutex> lock(m_mutex);
        int count = 0;
        for (const auto& task : m_tasks) {
            if (task.state == DownloadState::Downloading || 
                task.state == DownloadState::Extracting ||
                task.state == DownloadState::Installing) {
                count++;
            }
        }
        return count;
    }

    int DriverDownloader::GetCompletedCount() const {
        std::lock_guard<std::mutex> lock(m_mutex);
        int count = 0;
        for (const auto& task : m_tasks) {
            if (task.state == DownloadState::Completed) count++;
        }
        return count;
    }

    int DriverDownloader::GetFailedCount() const {
        std::lock_guard<std::mutex> lock(m_mutex);
        int count = 0;
        for (const auto& task : m_tasks) {
            if (task.state == DownloadState::Failed) count++;
        }
        return count;
    }

    // ============================================================================
    // Helper Methods
    // ============================================================================

    void DriverDownloader::UpdateTaskState(DownloadTask& task, DownloadState newState) {
        task.state = newState;
        
        if (m_stateChangeCallback) {
            m_stateChangeCallback(task.taskId, newState);
        }
    }

    void DriverDownloader::NotifyProgress(const DownloadTask& task) {
        if (m_progressCallback) {
            m_progressCallback(task.taskId, task.progress, task.downloadedBytes, task.totalBytes);
        }
    }

    std::wstring DriverDownloader::GetDownloadPath() const {
        return m_downloadDir;
    }

} // namespace DriverManager
