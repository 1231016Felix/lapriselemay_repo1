#pragma once

#include <Windows.h>
#include <SetupAPI.h>
#include <memory>

#pragma comment(lib, "setupapi.lib")

namespace DriverManager {

    // ============================================================================
    // Application Constants
    // ============================================================================
    
    namespace Constants {
        // Timeouts (milliseconds)
        constexpr DWORD HTTP_CONNECT_TIMEOUT_MS = 5000;
        constexpr DWORD HTTP_SEND_TIMEOUT_MS = 10000;
        constexpr DWORD HTTP_RECEIVE_TIMEOUT_MS = 15000;
        constexpr DWORD PROCESS_TIMEOUT_MS = 60000;
        constexpr DWORD INSTALL_TIMEOUT_MS = 300000;  // 5 minutes
        
        // Cache durations (seconds)
        constexpr time_t CACHE_DURATION_SECONDS = 86400;  // 24 hours
        
        // Limits
        constexpr int MAX_CONCURRENT_DOWNLOADS = 6;
        constexpr int MAX_CATALOG_RESULTS = 15;
        constexpr int MAX_FOLDER_SCAN_DEPTH = 500;
        
        // Driver age thresholds (days)
        constexpr int DRIVER_AGE_OLD_DAYS = 365;
        constexpr int DRIVER_AGE_VERY_OLD_DAYS = 730;
        
        // UI sizes
        constexpr float CATEGORIES_PANEL_WIDTH = 180.0f;
        constexpr float DETAILS_PANEL_WIDTH = 300.0f;
        constexpr float PROGRESS_BAR_HEIGHT = 0.0f;  // Use default
    }

    // ============================================================================
    // RAII Handle Wrapper
    // ============================================================================
    
    struct HandleDeleter {
        void operator()(HANDLE h) const noexcept {
            if (h && h != INVALID_HANDLE_VALUE) {
                CloseHandle(h);
            }
        }
    };
    
    using UniqueHandle = std::unique_ptr<void, HandleDeleter>;
    
    inline UniqueHandle MakeUniqueHandle(HANDLE h) {
        return UniqueHandle(h);
    }

    // ============================================================================
    // RAII HDEVINFO Wrapper for SetupAPI
    // ============================================================================
    
    struct DevInfoDeleter {
        void operator()(HDEVINFO h) const noexcept {
            if (h && h != INVALID_HANDLE_VALUE) {
                SetupDiDestroyDeviceInfoList(h);
            }
        }
    };
    
    using UniqueDevInfo = std::unique_ptr<void, DevInfoDeleter>;

    // ============================================================================
    // Scope Guard for cleanup
    // ============================================================================
    
    template<typename F>
    class ScopeGuard {
    public:
        explicit ScopeGuard(F&& func) : m_func(std::forward<F>(func)), m_active(true) {}
        
        ~ScopeGuard() {
            if (m_active) {
                try { m_func(); } catch (...) {}
            }
        }
        
        void dismiss() noexcept { m_active = false; }
        
        ScopeGuard(const ScopeGuard&) = delete;
        ScopeGuard& operator=(const ScopeGuard&) = delete;
        ScopeGuard(ScopeGuard&& other) noexcept : m_func(std::move(other.m_func)), m_active(other.m_active) {
            other.dismiss();
        }
        
    private:
        F m_func;
        bool m_active;
    };
    
    template<typename F>
    ScopeGuard<F> MakeScopeGuard(F&& func) {
        return ScopeGuard<F>(std::forward<F>(func));
    }

} // namespace DriverManager
