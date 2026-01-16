#pragma once

#include <string>
#include <map>
#include <vector>
#include <algorithm>
#include <Windows.h>
#include <shellapi.h>
#include "StringUtils.h"

namespace DriverManager {

    // Map of known manufacturers to their driver download pages
    inline const std::map<std::wstring, std::wstring> g_manufacturerUrls = {
        // GPU
        {L"nvidia", L"https://www.nvidia.com/Download/index.aspx"},
        {L"amd", L"https://www.amd.com/en/support"},
        {L"intel", L"https://www.intel.com/content/www/us/en/download-center/home.html"},
        
        // Audio
        {L"realtek", L"https://www.realtek.com/en/component/zoo/category/pc-audio-codecs-high-definition-audio-codecs-software"},
        {L"creative", L"https://support.creative.com/"},
        {L"conexant", L"https://www.synaptics.com/products/audio-codecs"},
        {L"synaptics", L"https://www.synaptics.com/products"},
        
        // Network
        {L"qualcomm", L"https://www.qualcomm.com/support"},
        {L"broadcom", L"https://www.broadcom.com/support/download-search"},
        {L"mediatek", L"https://www.mediatek.com/products/connectivity-and-networking"},
        {L"killer", L"https://www.intel.com/content/www/us/en/download/19729/killer-performance-driver-suite.html"},
        {L"tp-link", L"https://www.tp-link.com/en/support/download/"},
        {L"netgear", L"https://www.netgear.com/support/"},
        {L"asus", L"https://www.asus.com/support/Download-Center/"},
        
        // Peripherals
        {L"logitech", L"https://support.logi.com/hc/en-us/categories/360001702893"},
        {L"corsair", L"https://www.corsair.com/us/en/downloads"},
        {L"razer", L"https://www.razer.com/synapse-3"},
        {L"steelseries", L"https://steelseries.com/gg"},
        {L"hyperx", L"https://hyperx.com/pages/support"},
        {L"roccat", L"https://support.roccat.com/"},
        
        // Storage
        {L"samsung", L"https://semiconductor.samsung.com/consumer-storage/support/tools/"},
        {L"western digital", L"https://support-en.wd.com/"},
        {L"seagate", L"https://www.seagate.com/support/downloads/"},
        {L"crucial", L"https://www.crucial.com/support"},
        {L"kingston", L"https://www.kingston.com/en/support"},
        {L"sandisk", L"https://www.westerndigital.com/support"},
        
        // Motherboard/Chipset
        {L"msi", L"https://www.msi.com/support"},
        {L"gigabyte", L"https://www.gigabyte.com/Support"},
        {L"asrock", L"https://www.asrock.com/support/index.asp"},
        
        // Printers
        {L"hp", L"https://support.hp.com/drivers"},
        {L"canon", L"https://www.usa.canon.com/support"},
        {L"epson", L"https://epson.com/Support/sl/s"},
        {L"brother", L"https://support.brother.com/"},
        {L"xerox", L"https://www.support.xerox.com/"},
        
        // Other
        {L"microsoft", L"https://www.microsoft.com/en-us/download/"},
        {L"dell", L"https://www.dell.com/support/home/"},
        {L"lenovo", L"https://support.lenovo.com/"},
        {L"acer", L"https://www.acer.com/ac/en/US/content/drivers"},
        {L"toshiba", L"https://support.dynabook.com/drivers"},
        {L"sony", L"https://www.sony.com/electronics/support"},
        {L"lg", L"https://www.lg.com/us/support"},
        {L"benq", L"https://www.benq.com/en-us/support/downloads-faq.html"},
    };

    // Find manufacturer URL by searching for known names in the manufacturer string
    inline std::wstring FindManufacturerUrl(const std::wstring& manufacturer) {
        std::wstring lowerMfr = ToLowerW(manufacturer);
        
        for (const auto& [name, url] : g_manufacturerUrls) {
            if (lowerMfr.find(name) != std::wstring::npos) {
                return url;
            }
        }
        return L"";
    }

    // URL encode a string for search queries
    inline std::wstring UrlEncode(const std::wstring& str) {
        std::wstring encoded;
        for (wchar_t c : str) {
            if ((c >= L'A' && c <= L'Z') || (c >= L'a' && c <= L'z') || 
                (c >= L'0' && c <= L'9') || c == L'-' || c == L'_' || c == L'.' || c == L'~') {
                encoded += c;
            } else if (c == L' ') {
                encoded += L'+';
            } else {
                // Simple encoding for common chars
                wchar_t buf[8];
                swprintf(buf, 8, L"%%%02X", (int)c & 0xFF);
                encoded += buf;
            }
        }
        return encoded;
    }

    // Open URL in default browser
    inline void OpenUrl(const std::wstring& url) {
        ShellExecuteW(nullptr, L"open", url.c_str(), nullptr, nullptr, SW_SHOWNORMAL);
    }

    // Open manufacturer's driver page
    inline void OpenManufacturerPage(const std::wstring& manufacturer) {
        std::wstring url = FindManufacturerUrl(manufacturer);
        if (!url.empty()) {
            OpenUrl(url);
        }
    }

    // Open Google search for driver
    inline void SearchGoogleForDriver(const std::wstring& manufacturer, const std::wstring& deviceName) {
        std::wstring query = manufacturer + L" " + deviceName + L" driver download";
        std::wstring url = L"https://www.google.com/search?q=" + UrlEncode(query);
        OpenUrl(url);
    }

    // Open TousLesDrivers search
    inline void SearchTousLesDrivers(const std::wstring& deviceName) {
        std::wstring url = L"https://www.touslesdrivers.com/index.php?v_page=29&v_code=" + UrlEncode(deviceName);
        OpenUrl(url);
    }

} // namespace DriverManager
