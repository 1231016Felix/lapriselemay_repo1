#pragma once

#include <string>
#include <fstream>
#include <mutex>
#include <chrono>
#include <iomanip>
#include <sstream>

namespace DriverManager {

    enum class LogLevel {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    };

    /// <summary>
    /// Système de logging thread-safe avec support fichier et console
    /// </summary>
    class Logger {
    public:
        static Logger& Instance() {
            static Logger instance;
            return instance;
        }

        void Log(LogLevel level, const std::wstring& message) {
            if (level < m_minLevel) return;

            std::lock_guard<std::mutex> lock(m_mutex);

            std::wstring timestamp = GetTimestamp();
            std::wstring levelStr = GetLevelString(level);
            std::wstring fullMessage = L"[" + timestamp + L"] [" + levelStr + L"] " + message + L"\n";

            // Écrire dans le fichier si ouvert
            if (m_file.is_open()) {
                m_file << fullMessage;
                m_file.flush();
            }

            // Écrire dans la console de debug
#ifdef _DEBUG
            OutputDebugStringW(fullMessage.c_str());
#endif
        }

        void Log(LogLevel level, const std::string& message) {
            Log(level, Utf8ToWide(message));
        }

        bool SetLogFile(const std::wstring& path) {
            std::lock_guard<std::mutex> lock(m_mutex);
            
            if (m_file.is_open()) {
                m_file.close();
            }

            m_file.open(path, std::ios::out | std::ios::app);
            if (m_file.is_open()) {
                m_filePath = path;
                Log(LogLevel::Info, L"=== Session de log démarrée ===");
                return true;
            }
            return false;
        }

        void SetMinLevel(LogLevel level) {
            m_minLevel = level;
        }

        void CloseLogFile() {
            std::lock_guard<std::mutex> lock(m_mutex);
            if (m_file.is_open()) {
                m_file.close();
            }
        }

    private:
        Logger() = default;
        ~Logger() {
            if (m_file.is_open()) {
                m_file.close();
            }
        }

        Logger(const Logger&) = delete;
        Logger& operator=(const Logger&) = delete;

        std::wstring GetTimestamp() const {
            auto now = std::chrono::system_clock::now();
            auto time = std::chrono::system_clock::to_time_t(now);
            auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
                now.time_since_epoch()) % 1000;

            std::tm tm;
            localtime_s(&tm, &time);

            std::wstringstream ss;
            ss << std::put_time(&tm, L"%Y-%m-%d %H:%M:%S")
               << L"." << std::setfill(L'0') << std::setw(3) << ms.count();
            return ss.str();
        }

        std::wstring GetLevelString(LogLevel level) const {
            switch (level) {
                case LogLevel::Debug:   return L"DEBUG";
                case LogLevel::Info:    return L"INFO ";
                case LogLevel::Warning: return L"WARN ";
                case LogLevel::Error:   return L"ERROR";
                default:                return L"?????";
            }
        }

        static std::wstring Utf8ToWide(const std::string& str) {
            if (str.empty()) return {};
            int size = MultiByteToWideChar(CP_UTF8, 0, str.c_str(),
                static_cast<int>(str.size()), nullptr, 0);
            if (size <= 0) return {};
            std::wstring result(size, L'\0');
            MultiByteToWideChar(CP_UTF8, 0, str.c_str(),
                static_cast<int>(str.size()), result.data(), size);
            return result;
        }

        std::mutex m_mutex;
        std::wofstream m_file;
        std::wstring m_filePath;
        LogLevel m_minLevel = LogLevel::Info;
    };

    // Macros de logging pratiques
    #define LOG_DEBUG(msg) DriverManager::Logger::Instance().Log(DriverManager::LogLevel::Debug, msg)
    #define LOG_INFO(msg)  DriverManager::Logger::Instance().Log(DriverManager::LogLevel::Info, msg)
    #define LOG_WARN(msg)  DriverManager::Logger::Instance().Log(DriverManager::LogLevel::Warning, msg)
    #define LOG_ERROR(msg) DriverManager::Logger::Instance().Log(DriverManager::LogLevel::Error, msg)

} // namespace DriverManager
