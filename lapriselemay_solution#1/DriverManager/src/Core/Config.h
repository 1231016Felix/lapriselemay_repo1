#pragma once

#include <string>
#include <map>
#include <mutex>
#include <fstream>
#include <sstream>
#include <Windows.h>
#include <ShlObj.h>

namespace DriverManager {

    /// <summary>
    /// Gestionnaire de configuration persistante (format INI simplifié)
    /// Thread-safe et sauvegarde automatique
    /// </summary>
    class Config {
    public:
        static Config& Instance() {
            static Config instance;
            return instance;
        }

        /// <summary>
        /// Charge la configuration depuis un fichier
        /// </summary>
        bool Load(const std::wstring& path) {
            std::lock_guard<std::mutex> lock(m_mutex);
            m_path = path;
            m_values.clear();

            std::wifstream file(path);
            if (!file.is_open()) {
                // Créer un fichier par défaut
                return CreateDefaultConfig();
            }

            std::wstring line;
            while (std::getline(file, line)) {
                // Ignorer les commentaires et lignes vides
                if (line.empty() || line[0] == L'#' || line[0] == L';') continue;

                size_t pos = line.find(L'=');
                if (pos != std::wstring::npos) {
                    std::wstring key = Trim(line.substr(0, pos));
                    std::wstring value = Trim(line.substr(pos + 1));
                    m_values[key] = value;
                }
            }

            return true;
        }

        /// <summary>
        /// Sauvegarde la configuration
        /// </summary>
        bool Save() {
            std::lock_guard<std::mutex> lock(m_mutex);
            
            if (m_path.empty()) return false;

            std::wofstream file(m_path);
            if (!file.is_open()) return false;

            file << L"# DriverManager Configuration\n";
            file << L"# Généré automatiquement\n\n";

            for (const auto& [key, value] : m_values) {
                file << key << L"=" << value << L"\n";
            }

            return true;
        }

        /// <summary>
        /// Charge depuis le chemin par défaut (%APPDATA%\DriverManager\config.ini)
        /// </summary>
        bool LoadDefault() {
            wchar_t appDataPath[MAX_PATH];
            if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_APPDATA, nullptr, 0, appDataPath))) {
                std::wstring configDir = std::wstring(appDataPath) + L"\\DriverManager";
                CreateDirectoryW(configDir.c_str(), nullptr);
                return Load(configDir + L"\\config.ini");
            }
            return false;
        }

        // ========== Getters typés ==========

        bool GetBool(const std::wstring& key, bool defaultValue = false) const {
            std::lock_guard<std::mutex> lock(m_mutex);
            auto it = m_values.find(key);
            if (it == m_values.end()) return defaultValue;
            return it->second == L"1" || it->second == L"true" || it->second == L"yes";
        }

        int GetInt(const std::wstring& key, int defaultValue = 0) const {
            std::lock_guard<std::mutex> lock(m_mutex);
            auto it = m_values.find(key);
            if (it == m_values.end()) return defaultValue;
            try {
                return std::stoi(it->second);
            } catch (...) {
                return defaultValue;
            }
        }

        float GetFloat(const std::wstring& key, float defaultValue = 0.0f) const {
            std::lock_guard<std::mutex> lock(m_mutex);
            auto it = m_values.find(key);
            if (it == m_values.end()) return defaultValue;
            try {
                return std::stof(it->second);
            } catch (...) {
                return defaultValue;
            }
        }

        std::wstring GetString(const std::wstring& key, const std::wstring& defaultValue = L"") const {
            std::lock_guard<std::mutex> lock(m_mutex);
            auto it = m_values.find(key);
            return (it != m_values.end()) ? it->second : defaultValue;
        }

        // ========== Setters ==========

        void SetBool(const std::wstring& key, bool value) {
            std::lock_guard<std::mutex> lock(m_mutex);
            m_values[key] = value ? L"1" : L"0";
            if (m_autoSave) SaveInternal();
        }

        void SetInt(const std::wstring& key, int value) {
            std::lock_guard<std::mutex> lock(m_mutex);
            m_values[key] = std::to_wstring(value);
            if (m_autoSave) SaveInternal();
        }

        void SetFloat(const std::wstring& key, float value) {
            std::lock_guard<std::mutex> lock(m_mutex);
            m_values[key] = std::to_wstring(value);
            if (m_autoSave) SaveInternal();
        }

        void SetString(const std::wstring& key, const std::wstring& value) {
            std::lock_guard<std::mutex> lock(m_mutex);
            m_values[key] = value;
            if (m_autoSave) SaveInternal();
        }

        void SetAutoSave(bool enabled) { m_autoSave = enabled; }

        // ========== Clés de configuration ==========
        struct Keys {
            static constexpr const wchar_t* WindowWidth = L"window.width";
            static constexpr const wchar_t* WindowHeight = L"window.height";
            static constexpr const wchar_t* WindowX = L"window.x";
            static constexpr const wchar_t* WindowY = L"window.y";
            static constexpr const wchar_t* FilterOldDrivers = L"filter.old_drivers";
            static constexpr const wchar_t* CreateRestorePoint = L"install.create_restore_point";
            static constexpr const wchar_t* MaxConcurrentDownloads = L"download.max_concurrent";
            static constexpr const wchar_t* DownloadDirectory = L"download.directory";
            static constexpr const wchar_t* LogLevel = L"logging.level";
            static constexpr const wchar_t* LastScanDate = L"scan.last_date";
        };

    private:
        Config() = default;
        ~Config() { Save(); }

        Config(const Config&) = delete;
        Config& operator=(const Config&) = delete;

        bool CreateDefaultConfig() {
            m_values[Keys::WindowWidth] = L"1200";
            m_values[Keys::WindowHeight] = L"800";
            m_values[Keys::FilterOldDrivers] = L"0";
            m_values[Keys::CreateRestorePoint] = L"0";
            m_values[Keys::MaxConcurrentDownloads] = L"2";
            m_values[Keys::LogLevel] = L"1";
            return SaveInternal();
        }

        bool SaveInternal() {
            if (m_path.empty()) return false;
            std::wofstream file(m_path);
            if (!file.is_open()) return false;
            file << L"# DriverManager Configuration\n\n";
            for (const auto& [key, value] : m_values) {
                file << key << L"=" << value << L"\n";
            }
            return true;
        }

        static std::wstring Trim(const std::wstring& str) {
            size_t start = str.find_first_not_of(L" \t\r\n");
            if (start == std::wstring::npos) return L"";
            size_t end = str.find_last_not_of(L" \t\r\n");
            return str.substr(start, end - start + 1);
        }

        mutable std::mutex m_mutex;
        std::map<std::wstring, std::wstring> m_values;
        std::wstring m_path;
        bool m_autoSave = false;
    };

} // namespace DriverManager
