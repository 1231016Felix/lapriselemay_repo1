#pragma once

#include <Windows.h>
#include <string>
#include <algorithm>
#include <cctype>

namespace DriverManager {

    /// <summary>
    /// Convertit une chaîne UTF-8 en wide string (UTF-16)
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
    /// Convertit une wide string (UTF-16) en UTF-8
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
    /// Convertit une chaîne en minuscules (ASCII seulement)
    /// </summary>
    inline std::string ToLowerAscii(const std::string& str) {
        std::string result = str;
        std::transform(result.begin(), result.end(), result.begin(),
            [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
        return result;
    }

    /// <summary>
    /// Convertit une wide string en minuscules
    /// </summary>
    inline std::wstring ToLowerW(const std::wstring& str) {
        std::wstring result = str;
        std::transform(result.begin(), result.end(), result.begin(), ::towlower);
        return result;
    }

    /// <summary>
    /// Supprime les espaces au début et à la fin d'une chaîne
    /// </summary>
    inline std::string Trim(const std::string& str) {
        size_t start = str.find_first_not_of(" \t\r\n");
        if (start == std::string::npos) return "";
        size_t end = str.find_last_not_of(" \t\r\n");
        return str.substr(start, end - start + 1);
    }

    /// <summary>
    /// Supprime les espaces au début et à la fin d'une wide string
    /// </summary>
    inline std::wstring TrimW(const std::wstring& str) {
        size_t start = str.find_first_not_of(L" \t\r\n");
        if (start == std::wstring::npos) return L"";
        size_t end = str.find_last_not_of(L" \t\r\n");
        return str.substr(start, end - start + 1);
    }

    /// <summary>
    /// Vérifie si une chaîne contient une sous-chaîne (insensible à la casse)
    /// </summary>
    inline bool ContainsIgnoreCase(const std::string& haystack, const std::string& needle) {
        std::string haystackLower = ToLowerAscii(haystack);
        std::string needleLower = ToLowerAscii(needle);
        return haystackLower.find(needleLower) != std::string::npos;
    }

    /// <summary>
    /// Vérifie si une wide string contient une sous-chaîne (insensible à la casse)
    /// </summary>
    inline bool ContainsIgnoreCaseW(const std::wstring& haystack, const std::wstring& needle) {
        std::wstring haystackLower = ToLowerW(haystack);
        std::wstring needleLower = ToLowerW(needle);
        return haystackLower.find(needleLower) != std::wstring::npos;
    }

    /// <summary>
    /// Remplace toutes les occurrences d'une sous-chaîne
    /// </summary>
    inline std::string ReplaceAll(std::string str, const std::string& from, const std::string& to) {
        if (from.empty()) return str;
        size_t pos = 0;
        while ((pos = str.find(from, pos)) != std::string::npos) {
            str.replace(pos, from.length(), to);
            pos += to.length();
        }
        return str;
    }

    /// <summary>
    /// Remplace toutes les occurrences d'une sous-chaîne (wide)
    /// </summary>
    inline std::wstring ReplaceAllW(std::wstring str, const std::wstring& from, const std::wstring& to) {
        if (from.empty()) return str;
        size_t pos = 0;
        while ((pos = str.find(from, pos)) != std::wstring::npos) {
            str.replace(pos, from.length(), to);
            pos += to.length();
        }
        return str;
    }

} // namespace DriverManager
