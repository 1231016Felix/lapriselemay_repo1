// UserPreferences.h - User preferences persistence for DriverManager
// Stores and loads user preferences in JSON format

#pragma once

#include <string>
#include <fstream>
#include <shlobj.h>
#include <filesystem>

namespace DriverManager {

/// <summary>
/// Structure containing all user preferences
/// </summary>
struct UserPreferences {
    // Window settings
    int windowPosX = 100;
    int windowPosY = 100;
    int windowWidth = 1200;
    int windowHeight = 800;
    bool windowMaximized = false;
    
    // UI settings
    bool darkTheme = true;
    float uiScale = 1.0f;
    bool showDetailsPanel = true;
    float detailsPanelWidth = 300.0f;
    float categoriesPanelWidth = 180.0f;
    
    // Table settings
    int sortColumn = 0;
    bool sortAscending = true;
    std::vector<float> columnWidths;
    
    // Filter settings
    bool filterOldDrivers = false;
    bool filterUpdatesAvailable = false;
    int selectedCategory = -1;
    
    // Behavior settings
    bool confirmUninstall = true;
    bool createRestorePoint = true;
    bool autoScanOnStartup = false;
    bool minimizeToTray = false;
    bool checkUpdatesOnStartup = false;
    
    // Recent searches
    std::vector<std::string> recentSearches;
    int maxRecentSearches = 10;
    
    // Expanded groups in driver list
    std::set<std::wstring> expandedGroups;
};

/// <summary>
/// Service for persisting user preferences to disk
/// </summary>
class PreferencesService {
public:
    static PreferencesService& Instance() {
        static PreferencesService instance;
        return instance;
    }
    
    /// <summary>
    /// Load preferences from disk
    /// </summary>
    bool Load() {
        auto path = GetPreferencesPath();
        if (!std::filesystem::exists(path)) {
            return false;
        }
        
        try {
            std::ifstream file(path);
            if (!file.is_open()) return false;
            
            std::string json((std::istreambuf_iterator<char>(file)),
                            std::istreambuf_iterator<char>());
            file.close();
            
            return ParseJson(json);
        }
        catch (...) {
            return false;
        }
    }
    
    /// <summary>
    /// Save preferences to disk
    /// </summary>
    bool Save() {
        auto path = GetPreferencesPath();
        
        // Ensure directory exists
        auto dir = std::filesystem::path(path).parent_path();
        if (!std::filesystem::exists(dir)) {
            std::filesystem::create_directories(dir);
        }
        
        try {
            std::ofstream file(path);
            if (!file.is_open()) return false;
            
            file << ToJson();
            file.close();
            return true;
        }
        catch (...) {
            return false;
        }
    }
    
    /// <summary>
    /// Get current preferences
    /// </summary>
    UserPreferences& GetPreferences() { return m_prefs; }
    
    /// <summary>
    /// Reset to default preferences
    /// </summary>
    void Reset() { m_prefs = UserPreferences(); }
    
private:
    PreferencesService() { Load(); }
    ~PreferencesService() { Save(); }
    
    PreferencesService(const PreferencesService&) = delete;
    PreferencesService& operator=(const PreferencesService&) = delete;
    
    UserPreferences m_prefs;
    
    /// <summary>
    /// Get the path to preferences file
    /// </summary>
    std::wstring GetPreferencesPath() {
        wchar_t* appDataPath = nullptr;
        if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_RoamingAppData, 0, nullptr, &appDataPath))) {
            std::wstring path = appDataPath;
            CoTaskMemFree(appDataPath);
            path += L"\\DriverManager\\preferences.json";
            return path;
        }
        return L"preferences.json";
    }
    
    /// <summary>
    /// Simple JSON serialization (without external library)
    /// </summary>
    std::string ToJson() {
        std::ostringstream json;
        json << "{\n";
        
        // Window settings
        json << "  \"windowPosX\": " << m_prefs.windowPosX << ",\n";
        json << "  \"windowPosY\": " << m_prefs.windowPosY << ",\n";
        json << "  \"windowWidth\": " << m_prefs.windowWidth << ",\n";
        json << "  \"windowHeight\": " << m_prefs.windowHeight << ",\n";
        json << "  \"windowMaximized\": " << (m_prefs.windowMaximized ? "true" : "false") << ",\n";
        
        // UI settings
        json << "  \"darkTheme\": " << (m_prefs.darkTheme ? "true" : "false") << ",\n";
        json << "  \"uiScale\": " << std::fixed << std::setprecision(2) << m_prefs.uiScale << ",\n";
        json << "  \"showDetailsPanel\": " << (m_prefs.showDetailsPanel ? "true" : "false") << ",\n";
        json << "  \"detailsPanelWidth\": " << m_prefs.detailsPanelWidth << ",\n";
        json << "  \"categoriesPanelWidth\": " << m_prefs.categoriesPanelWidth << ",\n";
        
        // Table settings
        json << "  \"sortColumn\": " << m_prefs.sortColumn << ",\n";
        json << "  \"sortAscending\": " << (m_prefs.sortAscending ? "true" : "false") << ",\n";
        
        // Filter settings
        json << "  \"filterOldDrivers\": " << (m_prefs.filterOldDrivers ? "true" : "false") << ",\n";
        json << "  \"filterUpdatesAvailable\": " << (m_prefs.filterUpdatesAvailable ? "true" : "false") << ",\n";
        json << "  \"selectedCategory\": " << m_prefs.selectedCategory << ",\n";
        
        // Behavior settings
        json << "  \"confirmUninstall\": " << (m_prefs.confirmUninstall ? "true" : "false") << ",\n";
        json << "  \"createRestorePoint\": " << (m_prefs.createRestorePoint ? "true" : "false") << ",\n";
        json << "  \"autoScanOnStartup\": " << (m_prefs.autoScanOnStartup ? "true" : "false") << ",\n";
        json << "  \"minimizeToTray\": " << (m_prefs.minimizeToTray ? "true" : "false") << ",\n";
        json << "  \"checkUpdatesOnStartup\": " << (m_prefs.checkUpdatesOnStartup ? "true" : "false") << "\n";
        
        json << "}";
        return json.str();
    }
    
    /// <summary>
    /// Simple JSON parsing (without external library)
    /// </summary>
    bool ParseJson(const std::string& json) {
        auto getValue = [&json](const std::string& key) -> std::string {
            auto keyPos = json.find("\"" + key + "\"");
            if (keyPos == std::string::npos) return "";
            
            auto colonPos = json.find(':', keyPos);
            if (colonPos == std::string::npos) return "";
            
            auto valueStart = json.find_first_not_of(" \t\n\r", colonPos + 1);
            if (valueStart == std::string::npos) return "";
            
            // Handle different value types
            if (json[valueStart] == '"') {
                auto valueEnd = json.find('"', valueStart + 1);
                if (valueEnd == std::string::npos) return "";
                return json.substr(valueStart + 1, valueEnd - valueStart - 1);
            }
            else {
                auto valueEnd = json.find_first_of(",}\n", valueStart);
                if (valueEnd == std::string::npos) return "";
                auto value = json.substr(valueStart, valueEnd - valueStart);
                // Trim whitespace
                while (!value.empty() && std::isspace(value.back())) value.pop_back();
                return value;
            }
        };
        
        auto getInt = [&getValue](const std::string& key, int defaultValue) -> int {
            auto val = getValue(key);
            if (val.empty()) return defaultValue;
            try { return std::stoi(val); }
            catch (...) { return defaultValue; }
        };
        
        auto getFloat = [&getValue](const std::string& key, float defaultValue) -> float {
            auto val = getValue(key);
            if (val.empty()) return defaultValue;
            try { return std::stof(val); }
            catch (...) { return defaultValue; }
        };
        
        auto getBool = [&getValue](const std::string& key, bool defaultValue) -> bool {
            auto val = getValue(key);
            if (val.empty()) return defaultValue;
            return val == "true";
        };
        
        // Parse all settings
        m_prefs.windowPosX = getInt("windowPosX", 100);
        m_prefs.windowPosY = getInt("windowPosY", 100);
        m_prefs.windowWidth = getInt("windowWidth", 1200);
        m_prefs.windowHeight = getInt("windowHeight", 800);
        m_prefs.windowMaximized = getBool("windowMaximized", false);
        
        m_prefs.darkTheme = getBool("darkTheme", true);
        m_prefs.uiScale = getFloat("uiScale", 1.0f);
        m_prefs.showDetailsPanel = getBool("showDetailsPanel", true);
        m_prefs.detailsPanelWidth = getFloat("detailsPanelWidth", 300.0f);
        m_prefs.categoriesPanelWidth = getFloat("categoriesPanelWidth", 180.0f);
        
        m_prefs.sortColumn = getInt("sortColumn", 0);
        m_prefs.sortAscending = getBool("sortAscending", true);
        
        m_prefs.filterOldDrivers = getBool("filterOldDrivers", false);
        m_prefs.filterUpdatesAvailable = getBool("filterUpdatesAvailable", false);
        m_prefs.selectedCategory = getInt("selectedCategory", -1);
        
        m_prefs.confirmUninstall = getBool("confirmUninstall", true);
        m_prefs.createRestorePoint = getBool("createRestorePoint", true);
        m_prefs.autoScanOnStartup = getBool("autoScanOnStartup", false);
        m_prefs.minimizeToTray = getBool("minimizeToTray", false);
        m_prefs.checkUpdatesOnStartup = getBool("checkUpdatesOnStartup", false);
        
        return true;
    }
};

} // namespace DriverManager
