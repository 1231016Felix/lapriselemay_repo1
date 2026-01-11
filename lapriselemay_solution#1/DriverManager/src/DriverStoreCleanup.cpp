#include "DriverStoreCleanup.h"
#include <sstream>
#include <algorithm>
#include <filesystem>
#include <fstream>
#include <map>
#include <set>
#include <regex>
#include <cctype>

namespace DriverManager {

    // Helper function to convert date from MM/DD/YYYY to comparable integer YYYYMMDD
    static int ParseDateToInt(const std::wstring& date) {
        // Expected format: MM/DD/YYYY
        if (date.length() < 10) return 0;
        
        try {
            size_t firstSlash = date.find(L'/');
            size_t secondSlash = date.find(L'/', firstSlash + 1);
            
            if (firstSlash == std::wstring::npos || secondSlash == std::wstring::npos) return 0;
            
            int month = std::stoi(date.substr(0, firstSlash));
            int day = std::stoi(date.substr(firstSlash + 1, secondSlash - firstSlash - 1));
            int year = std::stoi(date.substr(secondSlash + 1, 4));
            
            // Return as YYYYMMDD for proper chronological comparison
            return year * 10000 + month * 100 + day;
        } catch (...) {
            return 0;
        }
    }

    // Helper function to compare driver versions (e.g., "1.2.3.4" vs "1.2.3.5")
    static int CompareVersions(const std::wstring& v1, const std::wstring& v2) {
        std::vector<int> parts1, parts2;
        
        // Parse version string into numeric parts
        auto parseVersion = [](const std::wstring& ver) {
            std::vector<int> parts;
            std::wstring current;
            for (wchar_t c : ver) {
                if (c == L'.' || c == L',') {
                    if (!current.empty()) {
                        try { parts.push_back(std::stoi(current)); } catch (...) { parts.push_back(0); }
                        current.clear();
                    }
                } else if (iswdigit(c)) {
                    current += c;
                }
            }
            if (!current.empty()) {
                try { parts.push_back(std::stoi(current)); } catch (...) { parts.push_back(0); }
            }
            return parts;
        };
        
        parts1 = parseVersion(v1);
        parts2 = parseVersion(v2);
        
        // Compare each part
        size_t maxParts = (std::max)(parts1.size(), parts2.size());
        for (size_t i = 0; i < maxParts; i++) {
            int p1 = (i < parts1.size()) ? parts1[i] : 0;
            int p2 = (i < parts2.size()) ? parts2[i] : 0;
            if (p1 != p2) return p1 - p2;
        }
        return 0;
    }

    DriverStoreCleanup::DriverStoreCleanup() {
    }

    DriverStoreCleanup::~DriverStoreCleanup() {
    }

    std::string DriverStoreCleanup::ExecutePnpUtil(const std::wstring& args) {
        std::string output;
        
        SECURITY_ATTRIBUTES sa;
        sa.nLength = sizeof(sa);
        sa.lpSecurityDescriptor = nullptr;
        sa.bInheritHandle = TRUE;

        HANDLE hReadPipe, hWritePipe;
        if (!CreatePipe(&hReadPipe, &hWritePipe, &sa, 0)) {
            m_lastError = L"Failed to create pipe";
            return output;
        }

        SetHandleInformation(hReadPipe, HANDLE_FLAG_INHERIT, 0);

        STARTUPINFOW si = {};
        si.cb = sizeof(si);
        si.dwFlags = STARTF_USESTDHANDLES | STARTF_USESHOWWINDOW;
        si.hStdOutput = hWritePipe;
        si.hStdError = hWritePipe;
        si.wShowWindow = SW_HIDE;

        PROCESS_INFORMATION pi = {};

        std::wstring cmdLine = L"pnputil.exe " + args;
        
        if (!CreateProcessW(nullptr, const_cast<LPWSTR>(cmdLine.c_str()),
            nullptr, nullptr, TRUE, CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi)) {
            CloseHandle(hReadPipe);
            CloseHandle(hWritePipe);
            m_lastError = L"Failed to execute pnputil";
            return output;
        }

        CloseHandle(hWritePipe);

        char buffer[4096];
        DWORD bytesRead;
        while (ReadFile(hReadPipe, buffer, sizeof(buffer) - 1, &bytesRead, nullptr) && bytesRead > 0) {
            buffer[bytesRead] = '\0';
            output += buffer;
        }

        WaitForSingleObject(pi.hProcess, INFINITE);
        
        CloseHandle(pi.hProcess);
        CloseHandle(pi.hThread);
        CloseHandle(hReadPipe);

        return output;
    }

    std::vector<PublishedDriverInfo> DriverStoreCleanup::GetPublishedDrivers() {
        std::vector<PublishedDriverInfo> drivers;
        
        std::string output = ExecutePnpUtil(L"/enum-drivers");
        if (output.empty()) return drivers;

        // Convert to wstring
        int wlen = MultiByteToWideChar(CP_ACP, 0, output.c_str(), -1, nullptr, 0);
        if (wlen == 0) return drivers;
        
        std::wstring woutput(wlen, 0);
        MultiByteToWideChar(CP_ACP, 0, output.c_str(), -1, woutput.data(), wlen);

        std::wistringstream stream(woutput);
        std::wstring line;
        PublishedDriverInfo currentDriver;
        bool inEntry = false;

        while (std::getline(stream, line)) {
            // Trim
            size_t start = line.find_first_not_of(L" \t\r\n");
            if (start == std::wstring::npos) {
                if (inEntry && !currentDriver.oemInfName.empty()) {
                    drivers.push_back(currentDriver);
                    currentDriver = PublishedDriverInfo();
                    inEntry = false;
                }
                continue;
            }
            line = line.substr(start);
            
            size_t colonPos = line.find(L":");
            if (colonPos == std::wstring::npos) continue;
            
            std::wstring key = line.substr(0, colonPos);
            std::wstring value = (colonPos + 1 < line.length()) ? line.substr(colonPos + 1) : L"";
            
            // Trim value
            size_t valStart = value.find_first_not_of(L" \t");
            if (valStart != std::wstring::npos) {
                value = value.substr(valStart);
            }
            size_t valEnd = value.find_last_not_of(L" \t\r\n");
            if (valEnd != std::wstring::npos) {
                value = value.substr(0, valEnd + 1);
            }

            // Published Name / Nom publié
            if ((key.find(L"Published") != std::wstring::npos || key.find(L"publi") != std::wstring::npos) &&
                value.find(L"oem") != std::wstring::npos && value.find(L".inf") != std::wstring::npos) {
                if (inEntry && !currentDriver.oemInfName.empty()) {
                    drivers.push_back(currentDriver);
                    currentDriver = PublishedDriverInfo();
                }
                currentDriver.oemInfName = value;
                inEntry = true;
            }
            else if (inEntry) {
                // Original Name / Nom d'origine
                if (key.find(L"Original") != std::wstring::npos || key.find(L"origine") != std::wstring::npos) {
                    currentDriver.originalInfName = value;
                }
                // Driver Version / Version du pilote (contains "DATE VERSION")
                else if (key.find(L"Version") != std::wstring::npos && 
                         (key.find(L"pilote") != std::wstring::npos || key.find(L"Driver") != std::wstring::npos)) {
                    // Parse "MM/DD/YYYY VERSION"
                    size_t spacePos = value.find(L' ');
                    if (spacePos != std::wstring::npos) {
                        currentDriver.driverDate = value.substr(0, spacePos);
                        size_t verStart = value.find_first_not_of(L" \t", spacePos);
                        if (verStart != std::wstring::npos) {
                            currentDriver.driverVersion = value.substr(verStart);
                            // Trim version end (remove any trailing whitespace/newlines)
                            size_t verEnd = currentDriver.driverVersion.find_last_not_of(L" \t\r\n");
                            if (verEnd != std::wstring::npos) {
                                currentDriver.driverVersion = currentDriver.driverVersion.substr(0, verEnd + 1);
                            }
                        }
                        // Trim date end
                        size_t dateEnd = currentDriver.driverDate.find_last_not_of(L" \t\r\n");
                        if (dateEnd != std::wstring::npos) {
                            currentDriver.driverDate = currentDriver.driverDate.substr(0, dateEnd + 1);
                        }
                    }
                }
            }
        }

        // Don't forget last entry
        if (inEntry && !currentDriver.oemInfName.empty()) {
            drivers.push_back(currentDriver);
        }

        return drivers;
    }

    uint64_t DriverStoreCleanup::CalculateFolderSize(const std::wstring& folderPath) {
        uint64_t totalSize = 0;
        try {
            for (const auto& entry : std::filesystem::recursive_directory_iterator(folderPath)) {
                if (entry.is_regular_file()) {
                    totalSize += entry.file_size();
                }
            }
        } catch (...) {
            // Ignore errors
        }
        return totalSize;
    }

    bool DriverStoreCleanup::ParseInfFile(const std::wstring& infPath, std::wstring& version, 
                                           std::wstring& date, std::wstring& provider, std::wstring& className) {
        // Read file as binary to handle different encodings (UTF-16 LE, UTF-8, ANSI)
        std::ifstream file(infPath, std::ios::binary);
        if (!file.is_open()) return false;

        // Read entire file into buffer
        std::vector<char> buffer((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
        file.close();
        
        if (buffer.size() < 2) return false;

        std::wstring content;
        
        // Detect encoding by BOM
        if (buffer.size() >= 2 && static_cast<unsigned char>(buffer[0]) == 0xFF && 
            static_cast<unsigned char>(buffer[1]) == 0xFE) {
            // UTF-16 LE with BOM
            const wchar_t* wdata = reinterpret_cast<const wchar_t*>(buffer.data() + 2);
            size_t wlen = (buffer.size() - 2) / sizeof(wchar_t);
            content.assign(wdata, wlen);
        }
        else if (buffer.size() >= 3 && static_cast<unsigned char>(buffer[0]) == 0xEF && 
                 static_cast<unsigned char>(buffer[1]) == 0xBB && static_cast<unsigned char>(buffer[2]) == 0xBF) {
            // UTF-8 with BOM
            int wlen = MultiByteToWideChar(CP_UTF8, 0, buffer.data() + 3, static_cast<int>(buffer.size() - 3), nullptr, 0);
            if (wlen > 0) {
                content.resize(wlen);
                MultiByteToWideChar(CP_UTF8, 0, buffer.data() + 3, static_cast<int>(buffer.size() - 3), content.data(), wlen);
            }
        }
        else {
            // Assume ANSI/UTF-8 without BOM
            int wlen = MultiByteToWideChar(CP_ACP, 0, buffer.data(), static_cast<int>(buffer.size()), nullptr, 0);
            if (wlen > 0) {
                content.resize(wlen);
                MultiByteToWideChar(CP_ACP, 0, buffer.data(), static_cast<int>(buffer.size()), content.data(), wlen);
            }
        }

        if (content.empty()) return false;

        // Parse line by line
        std::wistringstream stream(content);
        std::wstring line;
        bool inVersionSection = false;

        while (std::getline(stream, line)) {
            // Trim
            size_t start = line.find_first_not_of(L" \t\r\n");
            if (start == std::wstring::npos) continue;
            line = line.substr(start);
            size_t end = line.find_last_not_of(L" \t\r\n");
            if (end != std::wstring::npos) {
                line = line.substr(0, end + 1);
            }

            // Check for section
            if (!line.empty() && line[0] == L'[') {
                inVersionSection = (_wcsicmp(line.c_str(), L"[Version]") == 0);
                continue;
            }

            if (inVersionSection) {
                size_t eqPos = line.find(L'=');
                if (eqPos == std::wstring::npos) continue;

                std::wstring key = line.substr(0, eqPos);
                std::wstring value = line.substr(eqPos + 1);

                // Trim key and value
                size_t keyEnd = key.find_last_not_of(L" \t");
                if (keyEnd != std::wstring::npos) key = key.substr(0, keyEnd + 1);
                size_t valStart = value.find_first_not_of(L" \t");
                if (valStart != std::wstring::npos) value = value.substr(valStart);
                
                // Remove quotes and comments
                if (!value.empty() && value[0] == L'"') {
                    size_t endQuote = value.find(L'"', 1);
                    if (endQuote != std::wstring::npos) {
                        value = value.substr(1, endQuote - 1);
                    }
                }
                size_t commentPos = value.find(L';');
                if (commentPos != std::wstring::npos) {
                    value = value.substr(0, commentPos);
                }
                // Trim again
                size_t valEnd = value.find_last_not_of(L" \t\r\n");
                if (valEnd != std::wstring::npos) {
                    value = value.substr(0, valEnd + 1);
                }

                if (_wcsicmp(key.c_str(), L"DriverVer") == 0) {
                    // Format: MM/DD/YYYY,VERSION
                    size_t commaPos = value.find(L',');
                    if (commaPos != std::wstring::npos) {
                        date = value.substr(0, commaPos);
                        version = value.substr(commaPos + 1);
                        
                        // Trim date (start and end)
                        size_t dateStart = date.find_first_not_of(L" \t\r\n");
                        size_t dateEnd = date.find_last_not_of(L" \t\r\n");
                        if (dateStart != std::wstring::npos && dateEnd != std::wstring::npos) {
                            date = date.substr(dateStart, dateEnd - dateStart + 1);
                        }
                        
                        // Trim version (start and end)
                        size_t verStart = version.find_first_not_of(L" \t\r\n");
                        size_t verEnd = version.find_last_not_of(L" \t\r\n");
                        if (verStart != std::wstring::npos && verEnd != std::wstring::npos) {
                            version = version.substr(verStart, verEnd - verStart + 1);
                        }
                    }
                }
                else if (_wcsicmp(key.c_str(), L"Provider") == 0) {
                    // Remove %...% markers
                    if (value.length() > 2 && value[0] == L'%') {
                        size_t endPct = value.find(L'%', 1);
                        if (endPct != std::wstring::npos) {
                            // Keep the token name without %
                            value = value.substr(1, endPct - 1);
                        }
                    }
                    provider = value;
                }
                else if (_wcsicmp(key.c_str(), L"Class") == 0) {
                    className = value;
                }
            }
        }

        return !version.empty() || !date.empty();
    }

    bool DriverStoreCleanup::ScanFileRepository(const std::vector<PublishedDriverInfo>& publishedDrivers) {
        std::wstring repoPath = L"C:\\Windows\\System32\\DriverStore\\FileRepository";
        
        // Build a multimap of published drivers by original INF name -> version
        // Using multimap because the same INF can have multiple published versions
        std::multimap<std::wstring, PublishedDriverInfo> publishedMap;
        for (const auto& driver : publishedDrivers) {
            std::wstring lowerName = driver.originalInfName;
            std::transform(lowerName.begin(), lowerName.end(), lowerName.begin(), ::towlower);
            publishedMap.insert({lowerName, driver});
        }

        // Group folders by INF name
        std::map<std::wstring, std::vector<OrphanedDriverEntry>> folderGroups;
        
        int folderCount = 0;
        try {
            for (const auto& entry : std::filesystem::directory_iterator(repoPath)) {
                folderCount++;
            }
        } catch (...) {
            m_lastError = L"Cannot access FileRepository";
            return false;
        }

        int current = 0;
        try {
            for (const auto& entry : std::filesystem::directory_iterator(repoPath)) {
                if (!entry.is_directory()) continue;
                
                current++;
                std::wstring folderName = entry.path().filename().wstring();
                
                if (m_progressCallback) {
                    m_progressCallback(current, folderCount, folderName);
                }

                // Parse folder name: infname.inf_arch_hash
                std::wstring infName, arch;
                size_t firstUnderscore = folderName.find(L".inf_");
                if (firstUnderscore == std::wstring::npos) continue;
                
                infName = folderName.substr(0, firstUnderscore + 4); // Include .inf
                
                // Skip inbox (Windows built-in) drivers - they are NOT orphans
                // Inbox drivers have their INF in C:\Windows\INF
                std::wstring inboxPath = L"C:\\Windows\\INF\\" + infName;
                if (std::filesystem::exists(inboxPath)) {
                    continue;  // This is an inbox driver, skip it
                }
                
                std::wstring rest = folderName.substr(firstUnderscore + 5);
                
                size_t secondUnderscore = rest.find(L'_');
                if (secondUnderscore != std::wstring::npos) {
                    arch = rest.substr(0, secondUnderscore);
                }

                OrphanedDriverEntry driverEntry;
                driverEntry.folderName = folderName;
                driverEntry.folderPath = entry.path().wstring();
                driverEntry.infName = infName;
                driverEntry.architecture = arch;

                // Find and parse the INF file
                for (const auto& file : std::filesystem::directory_iterator(entry.path())) {
                    if (file.path().extension() == L".inf") {
                        ParseInfFile(file.path().wstring(), driverEntry.driverVersion, 
                                    driverEntry.driverDate, driverEntry.providerName, driverEntry.className);
                        break;
                    }
                }

                // Calculate size
                driverEntry.folderSize = CalculateFolderSize(entry.path().wstring());

                // Check if this is a current published version
                std::wstring lowerInfName = infName;
                std::transform(lowerInfName.begin(), lowerInfName.end(), lowerInfName.begin(), ::towlower);
                
                // Check all published versions for this INF (multimap can have multiple entries)
                auto range = publishedMap.equal_range(lowerInfName);
                for (auto it = range.first; it != range.second; ++it) {
                    // Compare versions - if they match, this is a current version
                    if (it->second.driverVersion == driverEntry.driverVersion &&
                        it->second.driverDate == driverEntry.driverDate) {
                        driverEntry.isCurrentVersion = true;
                        break;
                    }
                }

                // Group by INF name + architecture
                std::wstring groupKey = lowerInfName + L"_" + arch;
                folderGroups[groupKey].push_back(driverEntry);
            }
        } catch (const std::exception&) {
            // Continue with what we have
        }

        // Now process groups to find orphans
        m_entries.clear();
        
        for (auto& [groupKey, folders] : folderGroups) {
            if (folders.size() == 1) {
                // Only one version - only add if it's NOT current (truly orphaned)
                if (!folders[0].isCurrentVersion) {
                    m_entries.push_back(folders[0]);
                }
            } else {
                // Multiple versions - find which ones are orphans
                // Sort by date and version (newest first)
                std::sort(folders.begin(), folders.end(), [](const OrphanedDriverEntry& a, const OrphanedDriverEntry& b) {
                    // First compare by date (format: MM/DD/YYYY)
                    int dateA = ParseDateToInt(a.driverDate);
                    int dateB = ParseDateToInt(b.driverDate);
                    if (dateA != dateB) return dateA > dateB;  // Newer date first
                    
                    // If same date, compare by version
                    return CompareVersions(a.driverVersion, b.driverVersion) > 0;
                });

                // If none is marked as current, assume the newest is current
                bool hasCurrentVersion = false;
                for (const auto& f : folders) {
                    if (f.isCurrentVersion) {
                        hasCurrentVersion = true;
                        break;
                    }
                }
                
                if (!hasCurrentVersion && !folders.empty()) {
                    folders[0].isCurrentVersion = true;
                }

                // Add all non-current versions as orphans
                for (auto& folder : folders) {
                    if (!folder.isCurrentVersion) {
                        m_entries.push_back(folder);
                    }
                }
            }
        }

        // Sort entries by size (largest first)
        std::sort(m_entries.begin(), m_entries.end(), [](const OrphanedDriverEntry& a, const OrphanedDriverEntry& b) {
            return a.folderSize > b.folderSize;
        });

        return true;
    }

    bool DriverStoreCleanup::ScanDriverStore() {
        if (m_isScanning) return false;
        
        m_isScanning = true;
        m_entries.clear();
        
        if (m_progressCallback) {
            m_progressCallback(0, 1, L"R\x00e9" L"cup\x00e9ration des pilotes publi\x00e9s...");
        }

        // Get published drivers
        std::vector<PublishedDriverInfo> publishedDrivers = GetPublishedDrivers();
        
        if (m_progressCallback) {
            m_progressCallback(0, 1, L"Scan du FileRepository...");
        }

        // Scan FileRepository
        bool result = ScanFileRepository(publishedDrivers);
        
        if (m_progressCallback) {
            m_progressCallback(1, 1, L"Scan termin\x00e9");
        }
        
        m_isScanning = false;
        return result;
    }

    std::vector<OrphanedDriverEntry*> DriverStoreCleanup::GetOrphanedEntries() {
        std::vector<OrphanedDriverEntry*> orphans;
        for (auto& entry : m_entries) {
            if (!entry.isCurrentVersion) {
                orphans.push_back(&entry);
            }
        }
        return orphans;
    }

    bool DriverStoreCleanup::DeleteFolder(const std::wstring& folderPath) {
        try {
            std::filesystem::remove_all(folderPath);
            return true;
        } catch (...) {
            // Try with system command as fallback (may need admin)
            std::wstring cmd = L"rd /s /q \"" + folderPath + L"\"";
            int result = _wsystem(cmd.c_str());
            return (result == 0);
        }
    }

    int DriverStoreCleanup::DeleteSelectedPackages() {
        int deleted = 0;
        int total = 0;
        
        // Count selected
        for (const auto& entry : m_entries) {
            if (entry.isSelected && !entry.isCurrentVersion) {
                total++;
            }
        }
        
        int current = 0;
        for (auto& entry : m_entries) {
            if (entry.isSelected && !entry.isCurrentVersion) {
                current++;
                if (m_progressCallback) {
                    m_progressCallback(current, total, entry.folderName);
                }
                
                if (DeleteFolder(entry.folderPath)) {
                    deleted++;
                    entry.isSelected = false;
                }
            }
        }
        
        return deleted;
    }

    uint64_t DriverStoreCleanup::GetSelectedSize() const {
        uint64_t total = 0;
        for (const auto& entry : m_entries) {
            if (entry.isSelected) {
                total += entry.folderSize;
            }
        }
        return total;
    }

    uint64_t DriverStoreCleanup::GetTotalOrphanedSize() const {
        uint64_t total = 0;
        for (const auto& entry : m_entries) {
            if (!entry.isCurrentVersion) {
                total += entry.folderSize;
            }
        }
        return total;
    }

} // namespace DriverManager
