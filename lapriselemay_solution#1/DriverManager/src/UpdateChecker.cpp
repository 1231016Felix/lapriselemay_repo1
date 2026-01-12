#include "UpdateChecker.h"
#include "Utils.h"
#include <sstream>
#include <algorithm>
#include <regex>
#include <iomanip>
#include <future>
#include <queue>
#include <fstream>
#include <filesystem>
#include <ShlObj.h>

namespace DriverManager {

    // ============================================================================
    // OPTIMIZATION 1: Pre-compiled static regex patterns (compiled once at startup)
    // ============================================================================
    static const std::regex s_titleRegex(
        R"(<a[^>]*id=['"]([^'"]+)_link['"][^>]*>([^<]+)</a>)", 
        std::regex::icase | std::regex::optimize);
    static const std::regex s_versionRegex(
        R"(>(\d+\.\d+[\.\d]*)<)", 
        std::regex::icase | std::regex::optimize);
    static const std::regex s_dateRegex(
        R"((\d{1,2}/\d{1,2}/\d{4}))",
        std::regex::optimize);
    static const std::regex s_sizeRegex(
        R"(>(\d+(?:\.\d+)?\s*[KMGT]?B)<)", 
        std::regex::icase | std::regex::optimize);
    static const std::regex s_downloadUrlRegex(
        R"(http[s]?://[^'"]+\.cab|http[s]?://[^'"]+\.msu)", 
        std::regex::icase | std::regex::optimize);

    // ============================================================================
    // OPTIMIZATION 2: Hardware IDs to skip (Microsoft system drivers that don't 
    // need catalog updates - they're updated via Windows Update automatically)
    // ============================================================================
    static const std::vector<std::wstring> s_skipPrefixes = {
        L"ACPI\\",
        L"ACPI_HAL\\",
        L"ROOT\\",
        L"STORAGE\\",
        L"SW\\",
        L"HTREE\\",
        L"UMB\\",
        L"UEFI\\",
    };

    static const std::vector<std::wstring> s_skipManufacturers = {
        L"(Standard system devices)",
        L"(Standard disk drives)",
        L"(Standard CD-ROM drives)",
        L"Generic",
    };

    static bool ShouldSkipDriver(const DriverInfo& driver) {
        // Skip drivers without hardware ID
        if (driver.hardwareId.empty()) {
            return true;
        }

        // Skip system/virtual devices
        for (const auto& prefix : s_skipPrefixes) {
            if (driver.hardwareId.find(prefix) == 0) {
                return true;
            }
        }

        // Skip generic/standard Microsoft drivers
        for (const auto& mfr : s_skipManufacturers) {
            if (driver.manufacturer.find(mfr) != std::wstring::npos) {
                return true;
            }
        }

        // Skip Microsoft drivers (updated via Windows Update)
        if (driver.driverProvider == L"Microsoft" && 
            driver.manufacturer.find(L"Microsoft") != std::wstring::npos) {
            return true;
        }

        return false;
    }

    // ============================================================================
    // OPTIMIZATION 3: Disk cache for catalog results (persists between sessions)
    // ============================================================================
    static std::wstring GetCacheDirectory() {
        wchar_t path[MAX_PATH];
        if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_LOCAL_APPDATA, nullptr, 0, path))) {
            std::wstring cacheDir = std::wstring(path) + L"\\DriverManager\\Cache";
            std::filesystem::create_directories(cacheDir);
            return cacheDir;
        }
        return L"";
    }

    static std::wstring HashHardwareId(const std::wstring& hardwareId) {
        // Simple hash for filename
        size_t hash = 0;
        for (wchar_t c : hardwareId) {
            hash = hash * 31 + c;
        }
        wchar_t buf[32];
        swprintf_s(buf, L"%016zx", hash);
        return buf;
    }

    // ============================================================================
    // Constructor / Destructor
    // ============================================================================
    UpdateChecker::UpdateChecker() {
        m_cacheDirectory = GetCacheDirectory();
        LoadDiskCache();
    }

    UpdateChecker::~UpdateChecker() {
        m_cancelRequested = true;
        SaveDiskCache();
    }

    // ============================================================================
    // OPTIMIZATION 4: Disk cache load/save
    // ============================================================================
    void UpdateChecker::LoadDiskCache() {
        if (m_cacheDirectory.empty()) return;

        try {
            std::wstring indexFile = m_cacheDirectory + L"\\cache_index.dat";
            std::wifstream file(indexFile);
            if (!file.is_open()) return;

            std::wstring line;
            while (std::getline(file, line)) {
                if (line.empty()) continue;
                
                // Format: hardwareId|timestamp|hasUpdate|version
                size_t pos1 = line.find(L'|');
                size_t pos2 = line.find(L'|', pos1 + 1);
                size_t pos3 = line.find(L'|', pos2 + 1);
                
                if (pos1 != std::wstring::npos && pos2 != std::wstring::npos) {
                    std::wstring hwId = line.substr(0, pos1);
                    std::wstring timestamp = line.substr(pos1 + 1, pos2 - pos1 - 1);
                    
                    // Check if cache is still valid (24 hours)
                    time_t cachedTime = std::stoll(timestamp);
                    time_t now = std::time(nullptr);
                    if (now - cachedTime < Constants::CACHE_DURATION_SECONDS) {
                        CachedResult cached;
                        cached.timestamp = cachedTime;
                        cached.checkedVersion = (pos3 != std::wstring::npos) ? 
                            line.substr(pos3 + 1) : L"";
                        cached.hasUpdate = (line.substr(pos2 + 1, 1) == L"1");
                        
                        std::lock_guard<std::mutex> lock(m_mutex);
                        m_diskCache[hwId] = cached;
                    }
                }
            }
        } catch (...) {
            // Ignore cache load errors
        }
    }

    void UpdateChecker::SaveDiskCache() {
        if (m_cacheDirectory.empty()) return;

        try {
            std::wstring indexFile = m_cacheDirectory + L"\\cache_index.dat";
            std::wofstream file(indexFile);
            if (!file.is_open()) return;

            std::lock_guard<std::mutex> lock(m_mutex);
            for (const auto& [hwId, cached] : m_diskCache) {
                file << hwId << L"|" << cached.timestamp << L"|" 
                     << (cached.hasUpdate ? L"1" : L"0") << L"|" 
                     << cached.checkedVersion << L"\n";
            }
        } catch (...) {
            // Ignore cache save errors
        }
    }

    // ============================================================================
    // OPTIMIZATION 5: HTTP with timeouts and connection reuse hints
    // ============================================================================
    std::string UpdateChecker::HttpGet(const std::wstring& url) {
        std::string result;
        
        URL_COMPONENTS urlComp = {};
        urlComp.dwStructSize = sizeof(urlComp);
        
        wchar_t hostName[256] = {};
        wchar_t urlPath[2048] = {};
        urlComp.lpszHostName = hostName;
        urlComp.dwHostNameLength = 256;
        urlComp.lpszUrlPath = urlPath;
        urlComp.dwUrlPathLength = 2048;
        
        if (!WinHttpCrackUrl(url.c_str(), 0, 0, &urlComp)) {
            m_lastError = L"Invalid URL";
            return result;
        }

        // Open session with async hint for better performance
        HINTERNET hSession = WinHttpOpen(
            m_userAgent, 
            WINHTTP_ACCESS_TYPE_DEFAULT_PROXY,
            WINHTTP_NO_PROXY_NAME, 
            WINHTTP_NO_PROXY_BYPASS, 
            0);
        
        if (!hSession) {
            m_lastError = L"Failed to open HTTP session";
            return result;
        }

        // Set aggressive timeouts to avoid blocking
        DWORD connectTimeout = Constants::HTTP_CONNECT_TIMEOUT_MS;
        DWORD sendTimeout = Constants::HTTP_SEND_TIMEOUT_MS;
        DWORD receiveTimeout = Constants::HTTP_RECEIVE_TIMEOUT_MS;
        
        WinHttpSetOption(hSession, WINHTTP_OPTION_CONNECT_TIMEOUT, &connectTimeout, sizeof(connectTimeout));
        WinHttpSetOption(hSession, WINHTTP_OPTION_SEND_TIMEOUT, &sendTimeout, sizeof(sendTimeout));
        WinHttpSetOption(hSession, WINHTTP_OPTION_RECEIVE_TIMEOUT, &receiveTimeout, sizeof(receiveTimeout));

        // Enable keep-alive for connection reuse
        DWORD enableHttp2 = WINHTTP_PROTOCOL_FLAG_HTTP2;
        WinHttpSetOption(hSession, WINHTTP_OPTION_ENABLE_HTTP_PROTOCOL, &enableHttp2, sizeof(enableHttp2));

        HINTERNET hConnect = WinHttpConnect(hSession, hostName, urlComp.nPort, 0);
        if (!hConnect) {
            WinHttpCloseHandle(hSession);
            m_lastError = L"Failed to connect";
            return result;
        }

        DWORD flags = (urlComp.nScheme == INTERNET_SCHEME_HTTPS) ? WINHTTP_FLAG_SECURE : 0;
        HINTERNET hRequest = WinHttpOpenRequest(
            hConnect, L"GET", urlPath,
            nullptr, WINHTTP_NO_REFERER, 
            WINHTTP_DEFAULT_ACCEPT_TYPES, 
            flags);
        
        if (!hRequest) {
            WinHttpCloseHandle(hConnect);
            WinHttpCloseHandle(hSession);
            m_lastError = L"Failed to open request";
            return result;
        }

        // Add headers for faster response
        WinHttpAddRequestHeaders(hRequest, 
            L"Accept-Encoding: gzip, deflate\r\n"
            L"Connection: keep-alive\r\n",
            -1, WINHTTP_ADDREQ_FLAG_ADD);

        if (WinHttpSendRequest(hRequest, WINHTTP_NO_ADDITIONAL_HEADERS, 0,
            WINHTTP_NO_REQUEST_DATA, 0, 0, 0)) {
            
            if (WinHttpReceiveResponse(hRequest, nullptr)) {
                // Pre-allocate buffer based on content-length if available
                DWORD contentLength = 0;
                DWORD bufferSize = sizeof(contentLength);
                if (WinHttpQueryHeaders(hRequest, 
                    WINHTTP_QUERY_CONTENT_LENGTH | WINHTTP_QUERY_FLAG_NUMBER,
                    WINHTTP_HEADER_NAME_BY_INDEX, &contentLength, &bufferSize, 
                    WINHTTP_NO_HEADER_INDEX)) {
                    result.reserve(contentLength);
                }

                DWORD bytesAvailable = 0;
                do {
                    bytesAvailable = 0;
                    WinHttpQueryDataAvailable(hRequest, &bytesAvailable);
                    
                    if (bytesAvailable > 0) {
                        std::vector<char> buffer(bytesAvailable + 1);
                        DWORD bytesRead = 0;
                        if (WinHttpReadData(hRequest, buffer.data(), bytesAvailable, &bytesRead)) {
                            result.append(buffer.data(), bytesRead);
                        }
                    }
                } while (bytesAvailable > 0 && !m_cancelRequested);
            }
        }

        WinHttpCloseHandle(hRequest);
        WinHttpCloseHandle(hConnect);
        WinHttpCloseHandle(hSession);
        
        return result;
    }

    std::string UpdateChecker::HttpPost(const std::wstring& url, const std::string& data, 
                                         const std::wstring& contentType) {
        std::string result;
        
        URL_COMPONENTS urlComp = {};
        urlComp.dwStructSize = sizeof(urlComp);
        
        wchar_t hostName[256] = {};
        wchar_t urlPath[2048] = {};
        urlComp.lpszHostName = hostName;
        urlComp.dwHostNameLength = 256;
        urlComp.lpszUrlPath = urlPath;
        urlComp.dwUrlPathLength = 2048;
        
        if (!WinHttpCrackUrl(url.c_str(), 0, 0, &urlComp)) {
            return result;
        }

        HINTERNET hSession = WinHttpOpen(m_userAgent, WINHTTP_ACCESS_TYPE_DEFAULT_PROXY,
            WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
        if (!hSession) return result;

        // Set timeouts
        DWORD timeout = Constants::HTTP_SEND_TIMEOUT_MS;
        WinHttpSetOption(hSession, WINHTTP_OPTION_CONNECT_TIMEOUT, &timeout, sizeof(timeout));
        WinHttpSetOption(hSession, WINHTTP_OPTION_SEND_TIMEOUT, &timeout, sizeof(timeout));
        WinHttpSetOption(hSession, WINHTTP_OPTION_RECEIVE_TIMEOUT, &timeout, sizeof(timeout));

        HINTERNET hConnect = WinHttpConnect(hSession, hostName, urlComp.nPort, 0);
        if (!hConnect) {
            WinHttpCloseHandle(hSession);
            return result;
        }

        DWORD flags = (urlComp.nScheme == INTERNET_SCHEME_HTTPS) ? WINHTTP_FLAG_SECURE : 0;
        HINTERNET hRequest = WinHttpOpenRequest(hConnect, L"POST", urlPath,
            nullptr, WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, flags);
        if (!hRequest) {
            WinHttpCloseHandle(hConnect);
            WinHttpCloseHandle(hSession);
            return result;
        }

        std::wstring headers = L"Content-Type: " + contentType;
        
        if (WinHttpSendRequest(hRequest, headers.c_str(), -1,
            (LPVOID)data.c_str(), (DWORD)data.size(), (DWORD)data.size(), 0)) {
            
            if (WinHttpReceiveResponse(hRequest, nullptr)) {
                DWORD bytesAvailable = 0;
                do {
                    WinHttpQueryDataAvailable(hRequest, &bytesAvailable);
                    if (bytesAvailable > 0) {
                        std::vector<char> buffer(bytesAvailable + 1);
                        DWORD bytesRead = 0;
                        if (WinHttpReadData(hRequest, buffer.data(), bytesAvailable, &bytesRead)) {
                            result.append(buffer.data(), bytesRead);
                        }
                    }
                } while (bytesAvailable > 0 && !m_cancelRequested);
            }
        }

        WinHttpCloseHandle(hRequest);
        WinHttpCloseHandle(hConnect);
        WinHttpCloseHandle(hSession);
        
        return result;
    }

    // ============================================================================
    // Hardware ID cleaning for search
    // ============================================================================
    std::wstring UpdateChecker::CleanHardwareIdForSearch(const std::wstring& hardwareId) {
        std::wstring cleaned = hardwareId;
        std::wstring searchTerms;
        
        // Look for VEN_xxxx (PCI)
        size_t venPos = cleaned.find(L"VEN_");
        if (venPos != std::wstring::npos) {
            size_t end = cleaned.find(L"&", venPos);
            if (end == std::wstring::npos) end = cleaned.length();
            searchTerms += cleaned.substr(venPos, (std::min)((size_t)8, end - venPos)) + L" ";
        }
        
        // Look for DEV_xxxx (PCI)
        size_t devPos = cleaned.find(L"DEV_");
        if (devPos != std::wstring::npos) {
            size_t end = cleaned.find(L"&", devPos);
            if (end == std::wstring::npos) end = cleaned.length();
            searchTerms += cleaned.substr(devPos, (std::min)((size_t)8, end - devPos)) + L" ";
        }
        
        // Look for VID_xxxx (USB)
        size_t vidPos = cleaned.find(L"VID_");
        if (vidPos != std::wstring::npos) {
            size_t end = cleaned.find(L"&", vidPos);
            if (end == std::wstring::npos) end = cleaned.length();
            searchTerms += cleaned.substr(vidPos, (std::min)((size_t)8, end - vidPos)) + L" ";
        }
        
        // Look for PID_xxxx (USB)
        size_t pidPos = cleaned.find(L"PID_");
        if (pidPos != std::wstring::npos) {
            size_t end = cleaned.find(L"&", pidPos);
            if (end == std::wstring::npos) end = cleaned.length();
            searchTerms += cleaned.substr(pidPos, (std::min)((size_t)8, end - pidPos)) + L" ";
        }
        
        if (searchTerms.empty()) {
            size_t slashPos = cleaned.find(L"\\");
            if (slashPos != std::wstring::npos && slashPos + 1 < cleaned.length()) {
                cleaned = cleaned.substr(slashPos + 1);
            }
            size_t ampPos = cleaned.find(L"&");
            if (ampPos != std::wstring::npos) {
                cleaned = cleaned.substr(0, ampPos);
            }
            return cleaned;
        }
        
        return searchTerms;
    }

    // ============================================================================
    // Version comparison
    // ============================================================================
    int UpdateChecker::CompareVersions(const std::wstring& v1, const std::wstring& v2) {
        std::vector<int> parts1, parts2;
        
        std::wstringstream ss1(v1), ss2(v2);
        std::wstring token;
        
        while (std::getline(ss1, token, L'.')) {
            try {
                parts1.push_back(std::stoi(token));
            } catch (...) {
                parts1.push_back(0);
            }
        }
        
        while (std::getline(ss2, token, L'.')) {
            try {
                parts2.push_back(std::stoi(token));
            } catch (...) {
                parts2.push_back(0);
            }
        }
        
        while (parts1.size() < parts2.size()) parts1.push_back(0);
        while (parts2.size() < parts1.size()) parts2.push_back(0);
        
        for (size_t i = 0; i < parts1.size(); i++) {
            if (parts1[i] < parts2[i]) return -1;
            if (parts1[i] > parts2[i]) return 1;
        }
        
        return 0;
    }

    // ============================================================================
    // Windows Catalog search with memory cache
    // ============================================================================
    std::vector<CatalogEntry> UpdateChecker::SearchWindowsCatalog(const std::wstring& hardwareId) {
        std::vector<CatalogEntry> results;
        
        // Check memory cache first
        {
            std::lock_guard<std::mutex> lock(m_mutex);
            auto it = m_searchCache.find(hardwareId);
            if (it != m_searchCache.end()) {
                return it->second;
            }
        }
        
        std::wstring searchQuery = CleanHardwareIdForSearch(hardwareId);
        if (searchQuery.empty()) {
            return results;
        }
        
        // URL encode the search query
        std::wstring encodedQuery;
        for (wchar_t c : searchQuery) {
            if ((c >= L'A' && c <= L'Z') || (c >= L'a' && c <= L'z') || 
                (c >= L'0' && c <= L'9') || c == L'_' || c == L'-') {
                encodedQuery += c;
            } else if (c == L' ') {
                encodedQuery += L"+";
            } else {
                wchar_t buf[8];
                swprintf_s(buf, L"%%%02X", (unsigned int)c);
                encodedQuery += buf;
            }
        }
        
        std::wstring url = L"https://www.catalog.update.microsoft.com/Search.aspx?q=" + encodedQuery;
        
        std::string html = HttpGet(url);
        if (!html.empty()) {
            results = ParseCatalogResults(html);
        }
        
        // Cache results in memory
        {
            std::lock_guard<std::mutex> lock(m_mutex);
            m_searchCache[hardwareId] = results;
        }
        
        return results;
    }

    // ============================================================================
    // OPTIMIZATION 6: Optimized HTML parsing with pre-compiled regex
    // ============================================================================
    std::vector<CatalogEntry> UpdateChecker::ParseCatalogResults(const std::string& html) {
        std::vector<CatalogEntry> results;
        
        // Quick check if page has any drivers
        if (html.find("Driver") == std::string::npos && 
            html.find("driver") == std::string::npos) {
            return results;
        }

        size_t pos = 0;
        const std::string rowStart = "<tr";
        const std::string rowEnd = "</tr>";
        
        while ((pos = html.find(rowStart, pos)) != std::string::npos) {
            size_t endPos = html.find(rowEnd, pos);
            if (endPos == std::string::npos) break;
            
            std::string row = html.substr(pos, endPos - pos + rowEnd.length());
            pos = endPos + 1;
            
            // Quick filter - skip non-driver rows
            if (row.find("Driver") == std::string::npos && 
                row.find("driver") == std::string::npos) {
                continue;
            }
            
            CatalogEntry entry;
            
            // Extract title and update ID
            std::smatch titleMatch;
            if (std::regex_search(row, titleMatch, s_titleRegex)) {
                entry.updateId = Utf8ToWide(titleMatch[1].str());
                entry.title = Utf8ToWide(titleMatch[2].str());
            }
            
            // Extract version (find the longest/best match)
            std::smatch versionMatch;
            std::string::const_iterator searchStart(row.cbegin());
            while (std::regex_search(searchStart, row.cend(), versionMatch, s_versionRegex)) {
                std::wstring ver = Utf8ToWide(versionMatch[1].str());
                if (ver.length() > entry.version.length()) {
                    entry.version = ver;
                }
                searchStart = versionMatch.suffix().first;
            }
            
            // Extract date
            std::smatch dateMatch;
            if (std::regex_search(row, dateMatch, s_dateRegex)) {
                entry.lastUpdated = Utf8ToWide(dateMatch[1].str());
            }
            
            // Extract size
            std::smatch sizeMatch;
            if (std::regex_search(row, sizeMatch, s_sizeRegex)) {
                entry.size = Utf8ToWide(sizeMatch[1].str());
            }
            
            // Only add if we found meaningful data
            if (!entry.title.empty() || !entry.updateId.empty()) {
                entry.classification = L"Pilote";
                results.push_back(entry);
            }
            
            // Limit results to avoid excessive processing
            if (results.size() >= Constants::MAX_CATALOG_RESULTS) break;
        }
        
        return results;
    }

    // ============================================================================
    // Get download URL (with caching)
    // ============================================================================
    std::wstring UpdateChecker::GetCatalogDownloadUrl(const std::wstring& updateId) {
        // Check URL cache
        {
            std::lock_guard<std::mutex> lock(m_mutex);
            auto it = m_downloadUrlCache.find(updateId);
            if (it != m_downloadUrlCache.end()) {
                return it->second;
            }
        }

        std::wstring url = L"https://www.catalog.update.microsoft.com/DownloadDialog.aspx";
        
        std::string postData = "updateIDs=[{\"size\":0,\"uidInfo\":\"" + 
            WideToUtf8(updateId) + "\",\"updateID\":\"" + WideToUtf8(updateId) + "\"}]";
        
        std::string response = HttpPost(url, postData);
        
        std::wstring downloadUrl;
        std::smatch match;
        if (std::regex_search(response, match, s_downloadUrlRegex)) {
            downloadUrl = Utf8ToWide(match[0].str());
        }

        // Cache the URL
        if (!downloadUrl.empty()) {
            std::lock_guard<std::mutex> lock(m_mutex);
            m_downloadUrlCache[updateId] = downloadUrl;
        }
        
        return downloadUrl;
    }

    // ============================================================================
    // Check single driver update
    // ============================================================================
    UpdateCheckResult UpdateChecker::CheckDriverUpdate(const DriverInfo& driver) {
        UpdateCheckResult result;
        result.hardwareId = driver.hardwareId;
        result.currentVersion = driver.driverVersion;
        
        if (driver.hardwareId.empty()) {
            result.lastError = L"Hardware ID manquant";
            return result;
        }
        
        // Search the catalog
        auto catalogEntries = SearchWindowsCatalog(driver.hardwareId);
        
        if (catalogEntries.empty()) {
            result.lastError = L"Aucun pilote trouvé dans le catalogue";
            return result;
        }
        
        // Find the newest version
        std::wstring newestVersion;
        CatalogEntry* bestEntry = nullptr;
        
        for (auto& entry : catalogEntries) {
            if (!entry.version.empty()) {
                if (newestVersion.empty() || CompareVersions(entry.version, newestVersion) > 0) {
                    newestVersion = entry.version;
                    bestEntry = &entry;
                }
            }
        }
        
        if (bestEntry && !newestVersion.empty()) {
            // Compare with current version
            if (CompareVersions(newestVersion, driver.driverVersion) > 0) {
                result.updateAvailable = true;
                result.newVersion = newestVersion;
                result.description = bestEntry->title;
                
                // Get download URL if needed (lazy - don't fetch until needed)
                // This saves time during scanning
            }
        }
        
        return result;
    }

    // ============================================================================
    // OPTIMIZATION 7: Fully parallel update checking with smart filtering
    // ============================================================================
    void UpdateChecker::CheckAllUpdatesAsync(std::vector<DriverInfo>& drivers) {
        if (m_isChecking) return;
        
        m_isChecking = true;
        m_cancelRequested = false;
        m_totalChecked = 0;
        m_updatesFound = 0;
        
        // Pre-filter drivers to check (skip system/generic drivers)
        std::vector<DriverInfo*> driversToCheck;
        for (auto& driver : drivers) {
            driver.updateCheckPending = true;
            driver.hasUpdate = false;
            
            if (!ShouldSkipDriver(driver)) {
                driversToCheck.push_back(&driver);
            } else {
                driver.updateCheckPending = false; // Mark as done (skipped)
            }
        }
        
        const int total = static_cast<int>(drivers.size());
        const int toCheck = static_cast<int>(driversToCheck.size());
        const int skipped = total - toCheck;
        
        // Update progress for skipped drivers immediately
        m_totalChecked = skipped;
        
        if (m_progressCallback && skipped > 0) {
            m_progressCallback(skipped, total, L"Drivers système ignorés...");
        }
        
        // Parallel configuration
        const int maxConcurrent = Constants::MAX_CONCURRENT_DOWNLOADS;
        
        std::atomic<int> currentIndex{0};
        std::atomic<int> completedCount{skipped};
        std::vector<std::future<void>> futures;
        
        // Worker function
        auto worker = [this, &driversToCheck, &currentIndex, &completedCount, total, toCheck]() {
            while (!m_cancelRequested) {
                int idx = currentIndex.fetch_add(1);
                if (idx >= toCheck) break;
                
                auto* driver = driversToCheck[idx];
                
                // Check disk cache first
                bool usedCache = false;
                {
                    std::lock_guard<std::mutex> lock(m_mutex);
                    auto it = m_diskCache.find(driver->hardwareId);
                    if (it != m_diskCache.end()) {
                        // Use cached result
                        driver->hasUpdate = it->second.hasUpdate;
                        if (driver->hasUpdate) {
                            driver->availableUpdate.newVersion = it->second.checkedVersion;
                            m_updatesFound++;
                        }
                        driver->updateCheckPending = false;
                        usedCache = true;
                    }
                }
                
                if (!usedCache) {
                    // Perform actual check
                    auto result = CheckDriverUpdate(*driver);
                    
                    driver->updateCheckPending = false;
                    
                    if (result.updateAvailable) {
                        driver->hasUpdate = true;
                        driver->availableUpdate.newVersion = result.newVersion;
                        driver->availableUpdate.downloadUrl = result.downloadUrl;
                        driver->availableUpdate.description = result.description;
                        m_updatesFound++;
                    }
                    
                    // Save to disk cache
                    {
                        std::lock_guard<std::mutex> lock(m_mutex);
                        CachedResult cached;
                        cached.timestamp = std::time(nullptr);
                        cached.hasUpdate = result.updateAvailable;
                        cached.checkedVersion = result.newVersion;
                        m_diskCache[driver->hardwareId] = cached;
                    }
                }
                
                int completed = completedCount.fetch_add(1) + 1;
                m_totalChecked = completed;
                
                if (m_progressCallback) {
                    m_progressCallback(completed, total, driver->deviceName);
                }
                
                // Minimal delay to avoid rate limiting (15ms)
                if (!m_cancelRequested) {
                    std::this_thread::sleep_for(std::chrono::milliseconds(15));
                }
            }
        };
        
        // Launch worker threads
        for (int i = 0; i < maxConcurrent && i < toCheck; i++) {
            futures.push_back(std::async(std::launch::async, worker));
        }
        
        // Wait for all workers to complete
        for (auto& f : futures) {
            f.wait();
        }
        
        // Save disk cache
        SaveDiskCache();
        
        m_isChecking = false;
    }

    std::vector<std::wstring> UpdateChecker::GetSystemHardwareIds() {
        std::vector<std::wstring> hardwareIds;
        return hardwareIds;
    }

    // ============================================================================
    // OPTIMIZATION 8: Windows Update check with same optimizations
    // ============================================================================
    void UpdateChecker::CheckWindowsUpdate(std::vector<DriverInfo>& drivers) {
        if (m_isChecking) return;
        
        m_isChecking = true;
        m_cancelRequested = false;
        m_totalChecked = 0;
        m_updatesFound = 0;
        
        // Pre-filter drivers
        std::vector<DriverInfo*> driversToCheck;
        for (auto& driver : drivers) {
            if (!ShouldSkipDriver(driver)) {
                driversToCheck.push_back(&driver);
            }
        }
        
        const int total = static_cast<int>(drivers.size());
        const int toCheck = static_cast<int>(driversToCheck.size());
        const int skipped = total - toCheck;
        
        m_totalChecked = skipped;
        
        if (m_progressCallback && skipped > 0) {
            m_progressCallback(skipped, total, L"Drivers système ignorés...");
        }
        
        // Parallel configuration - use same constant as CheckAllUpdatesAsync
        const int maxConcurrent = Constants::MAX_CONCURRENT_DOWNLOADS;
        
        std::atomic<int> currentIndex{0};
        std::atomic<int> completedCount{skipped};
        std::vector<std::future<void>> futures;
        
        // Worker function
        auto worker = [this, &driversToCheck, &currentIndex, &completedCount, total, toCheck]() {
            while (!m_cancelRequested) {
                int idx = currentIndex.fetch_add(1);
                if (idx >= toCheck) break;
                
                auto* driver = driversToCheck[idx];
                
                // Check disk cache first
                bool usedCache = false;
                {
                    std::lock_guard<std::mutex> lock(m_mutex);
                    auto it = m_diskCache.find(driver->hardwareId);
                    if (it != m_diskCache.end()) {
                        driver->hasUpdate = it->second.hasUpdate;
                        if (driver->hasUpdate) {
                            driver->availableUpdate.newVersion = it->second.checkedVersion;
                            m_updatesFound++;
                        }
                        usedCache = true;
                    }
                }
                
                if (!usedCache) {
                    auto result = CheckDriverUpdate(*driver);
                    
                    if (result.updateAvailable) {
                        driver->hasUpdate = true;
                        driver->availableUpdate.newVersion = result.newVersion;
                        driver->availableUpdate.downloadUrl = result.downloadUrl;
                        driver->availableUpdate.description = result.description;
                        m_updatesFound++;
                    }
                    
                    // Save to disk cache
                    {
                        std::lock_guard<std::mutex> lock(m_mutex);
                        CachedResult cached;
                        cached.timestamp = std::time(nullptr);
                        cached.hasUpdate = result.updateAvailable;
                        cached.checkedVersion = result.newVersion;
                        m_diskCache[driver->hardwareId] = cached;
                    }
                }
                
                int completed = completedCount.fetch_add(1) + 1;
                m_totalChecked = completed;
                
                if (m_progressCallback) {
                    m_progressCallback(completed, total, driver->deviceName);
                }
                
                // Minimal delay
                if (!m_cancelRequested && idx < toCheck - 1) {
                    std::this_thread::sleep_for(std::chrono::milliseconds(15));
                }
            }
        };
        
        // Launch worker threads
        for (int i = 0; i < maxConcurrent && i < toCheck; i++) {
            futures.push_back(std::async(std::launch::async, worker));
        }
        
        // Wait for all workers
        for (auto& f : futures) {
            f.wait();
        }
        
        // Save disk cache
        SaveDiskCache();
        
        m_isChecking = false;
    }

    // ============================================================================
    // Clear caches (can be called to force fresh data)
    // ============================================================================
    void UpdateChecker::ClearCache() {
        std::lock_guard<std::mutex> lock(m_mutex);
        m_searchCache.clear();
        m_diskCache.clear();
        m_downloadUrlCache.clear();
        
        // Delete cache file
        if (!m_cacheDirectory.empty()) {
            try {
                std::filesystem::remove(m_cacheDirectory + L"\\cache_index.dat");
            } catch (...) {}
        }
    }

} // namespace DriverManager
