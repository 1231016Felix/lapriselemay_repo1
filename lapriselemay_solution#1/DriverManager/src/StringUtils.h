// String Utilities
// Helpers for string conversion and manipulation

#pragma once

#include <string>
#include <Windows.h>
#include <sstream>
#include <iomanip>

namespace DriverManager {

/// <summary>
/// Convert wide string to UTF-8 string
/// </summary>
inline std::string WideToUtf8(const std::wstring& wstr) {
    if (wstr.empty()) return {};
    
    int size = WideCharToMultiByte(CP_UTF8, 0, wstr.c_str(), 
        static_cast<int>(wstr.size()), nullptr, 0, nullptr, nullptr);
    
    if (size <= 0) return {};
    
    std::string result(size, '\0');
    WideCharToMultiByte(CP_UTF8, 0, wstr.c_str(), 
        static_cast<int>(wstr.size()), result.data(), size, nullptr, nullptr);
    
    return result;
}

/// <summary>
/// Convert UTF-8 string to wide string
/// </summary>
inline std::wstring Utf8ToWide(const std::string& str) {
    if (str.empty()) return {};
    
    int size = MultiByteToWideChar(CP_UTF8, 0, str.c_str(), 
        static_cast<int>(str.size()), nullptr, 0);
    
    if (size <= 0) return {};
    
    std::wstring result(size, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, str.c_str(), 
        static_cast<int>(str.size()), result.data(), size);
    
    return result;
}

/// <summary>
/// Format a file size in human-readable format
/// </summary>
inline std::wstring FormatFileSize(uint64_t bytes) {
    constexpr uint64_t KB = 1024;
    constexpr uint64_t MB = KB * 1024;
    constexpr uint64_t GB = MB * 1024;
    
    std::wstringstream ss;
    ss << std::fixed << std::setprecision(1);
    
    if (bytes >= GB) {
        ss << static_cast<double>(bytes) / GB << L" Go";
    } else if (bytes >= MB) {
        ss << static_cast<double>(bytes) / MB << L" Mo";
    } else if (bytes >= KB) {
        ss << static_cast<double>(bytes) / KB << L" Ko";
    } else {
        ss << bytes << L" octets";
    }
    
    return ss.str();
}

/// <summary>
/// Format a date from FILETIME
/// </summary>
inline std::wstring FormatFileTime(const FILETIME& ft) {
    SYSTEMTIME st;
    FileTimeToSystemTime(&ft, &st);
    
    std::wstringstream ss;
    ss << std::setfill(L'0')
       << std::setw(4) << st.wYear << L"-"
       << std::setw(2) << st.wMonth << L"-"
       << std::setw(2) << st.wDay << L" "
       << std::setw(2) << st.wHour << L":"
       << std::setw(2) << st.wMinute;
    
    return ss.str();
}

/// <summary>
/// Case-insensitive string comparison (wide)
/// </summary>
inline bool WideStringContainsNoCase(const std::wstring& haystack, const std::wstring& needle) {
    if (needle.empty()) return true;
    if (haystack.empty()) return false;
    
    auto it = std::search(
        haystack.begin(), haystack.end(),
        needle.begin(), needle.end(),
        [](wchar_t a, wchar_t b) { return towlower(a) == towlower(b); }
    );
    
    return it != haystack.end();
}

/// <summary>
/// Trim whitespace from string
/// </summary>
inline std::wstring TrimWide(const std::wstring& str) {
    const auto start = str.find_first_not_of(L" \t\n\r");
    if (start == std::wstring::npos) return {};
    
    const auto end = str.find_last_not_of(L" \t\n\r");
    return str.substr(start, end - start + 1);
}

} // namespace DriverManager
