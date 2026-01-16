#pragma once

#include <Windows.h>
#include <ShlObj.h>
#include <memory>
#include <string>

#pragma comment(lib, "shell32.lib")

namespace DriverManager {

    // ============================================================================
    // RAII Wrappers
    // ============================================================================

    /// <summary>
    /// RAII wrapper générique pour les handles Windows
    /// </summary>
    template<typename HandleType, typename Deleter>
    class UniqueHandle {
    public:
        explicit UniqueHandle(HandleType handle = nullptr, Deleter deleter = Deleter())
            : m_handle(handle), m_deleter(deleter) {}
        
        ~UniqueHandle() {
            if (m_handle) {
                m_deleter(m_handle);
            }
        }
        
        // Non-copyable
        UniqueHandle(const UniqueHandle&) = delete;
        UniqueHandle& operator=(const UniqueHandle&) = delete;
        
        // Movable
        UniqueHandle(UniqueHandle&& other) noexcept
            : m_handle(other.m_handle), m_deleter(std::move(other.m_deleter)) {
            other.m_handle = nullptr;
        }
        
        UniqueHandle& operator=(UniqueHandle&& other) noexcept {
            if (this != &other) {
                if (m_handle) {
                    m_deleter(m_handle);
                }
                m_handle = other.m_handle;
                m_deleter = std::move(other.m_deleter);
                other.m_handle = nullptr;
            }
            return *this;
        }
        
        HandleType Get() const { return m_handle; }
        HandleType* GetAddressOf() { return &m_handle; }
        operator HandleType() const { return m_handle; }
        explicit operator bool() const { return m_handle != nullptr && m_handle != INVALID_HANDLE_VALUE; }
        
        HandleType Release() {
            HandleType temp = m_handle;
            m_handle = nullptr;
            return temp;
        }
        
        void Reset(HandleType handle = nullptr) {
            if (m_handle) {
                m_deleter(m_handle);
            }
            m_handle = handle;
        }

    private:
        HandleType m_handle;
        Deleter m_deleter;
    };

    // Deleters spécifiques
    struct HandleDeleter {
        void operator()(HANDLE h) const {
            if (h && h != INVALID_HANDLE_VALUE) {
                CloseHandle(h);
            }
        }
    };

    struct FindHandleDeleter {
        void operator()(HANDLE h) const {
            if (h && h != INVALID_HANDLE_VALUE) {
                FindClose(h);
            }
        }
    };

    struct RegKeyDeleter {
        void operator()(HKEY h) const {
            if (h) {
                RegCloseKey(h);
            }
        }
    };

    // Types pratiques
    using UniqueWinHandle = UniqueHandle<HANDLE, HandleDeleter>;
    using UniqueFindHandle = UniqueHandle<HANDLE, FindHandleDeleter>;
    using UniqueRegKey = UniqueHandle<HKEY, RegKeyDeleter>;

    // ============================================================================
    // Helper Functions
    // ============================================================================

    /// <summary>
    /// Obtient le message d'erreur Windows pour un code d'erreur
    /// </summary>
    inline std::wstring GetErrorMessage(DWORD errorCode) {
        wchar_t buffer[512] = {0};
        FormatMessageW(
            FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
            nullptr, errorCode,
            MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
            buffer, 512, nullptr);
        return buffer;
    }

    /// <summary>
    /// Vérifie si le processus a les droits administrateur
    /// </summary>
    inline bool IsRunningAsAdmin() {
        BOOL isAdmin = FALSE;
        PSID adminGroup = nullptr;
        
        SID_IDENTIFIER_AUTHORITY ntAuthority = SECURITY_NT_AUTHORITY;
        if (AllocateAndInitializeSid(&ntAuthority, 2,
            SECURITY_BUILTIN_DOMAIN_RID, DOMAIN_ALIAS_RID_ADMINS,
            0, 0, 0, 0, 0, 0, &adminGroup)) {
            CheckTokenMembership(nullptr, adminGroup, &isAdmin);
            FreeSid(adminGroup);
        }
        
        return isAdmin != FALSE;
    }

    /// <summary>
    /// Obtient le chemin du dossier temporaire
    /// </summary>
    inline std::wstring GetTempPath() {
        wchar_t buffer[MAX_PATH];
        DWORD len = ::GetTempPathW(MAX_PATH, buffer);
        if (len > 0 && len < MAX_PATH) {
            return buffer;
        }
        return L"";
    }

    /// <summary>
    /// Obtient le chemin du dossier AppData
    /// </summary>
    inline std::wstring GetAppDataPath() {
        wchar_t buffer[MAX_PATH];
        if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_APPDATA, nullptr, 0, buffer))) {
            return buffer;
        }
        return L"";
    }

    /// <summary>
    /// Crée un dossier récursivement si nécessaire
    /// </summary>
    inline bool CreateDirectoryRecursive(const std::wstring& path) {
        if (path.empty()) return false;
        
        DWORD attr = GetFileAttributesW(path.c_str());
        if (attr != INVALID_FILE_ATTRIBUTES && (attr & FILE_ATTRIBUTE_DIRECTORY)) {
            return true;  // Already exists
        }
        
        // Find parent
        size_t pos = path.find_last_of(L"\\/");
        if (pos != std::wstring::npos && pos > 0) {
            std::wstring parent = path.substr(0, pos);
            if (!CreateDirectoryRecursive(parent)) {
                return false;
            }
        }
        
        return CreateDirectoryW(path.c_str(), nullptr) != 0 || GetLastError() == ERROR_ALREADY_EXISTS;
    }

    /// <summary>
    /// Formate une taille en bytes de manière lisible
    /// </summary>
    inline std::wstring FormatBytesW(uint64_t bytes) {
        const wchar_t* units[] = {L"B", L"KB", L"MB", L"GB", L"TB"};
        int unitIndex = 0;
        double size = static_cast<double>(bytes);
        
        while (size >= 1024.0 && unitIndex < 4) {
            size /= 1024.0;
            unitIndex++;
        }
        
        wchar_t buffer[64];
        if (unitIndex == 0) {
            swprintf_s(buffer, L"%llu %s", bytes, units[unitIndex]);
        } else {
            swprintf_s(buffer, L"%.2f %s", size, units[unitIndex]);
        }
        return buffer;
    }

} // namespace DriverManager
