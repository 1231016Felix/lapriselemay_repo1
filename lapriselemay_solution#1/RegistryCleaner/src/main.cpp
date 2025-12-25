// main.cpp - Application Entry Point
#include "pch.h"
#include "ui/ConsoleUI.h"

namespace {
    HANDLE g_hConsole = nullptr;
    
    void Print(std::wstring_view text) {
        if (!g_hConsole) g_hConsole = GetStdHandle(STD_OUTPUT_HANDLE);
        DWORD written;
        WriteConsoleW(g_hConsole, text.data(), static_cast<DWORD>(text.size()), &written, nullptr);
    }
    
    void PrintLn(std::wstring_view text = L"") {
        Print(text);
        Print(L"\n");
    }
}

// Check if running as administrator
bool IsRunningAsAdmin() {
    BOOL isAdmin = FALSE;
    PSID adminGroup = nullptr;

    SID_IDENTIFIER_AUTHORITY ntAuthority = SECURITY_NT_AUTHORITY;
    if (AllocateAndInitializeSid(
        &ntAuthority,
        2,
        SECURITY_BUILTIN_DOMAIN_RID,
        DOMAIN_ALIAS_RID_ADMINS,
        0, 0, 0, 0, 0, 0,
        &adminGroup
    )) {
        CheckTokenMembership(nullptr, adminGroup, &isAdmin);
        FreeSid(adminGroup);
    }

    return isAdmin != FALSE;
}

// Request elevation if not admin
bool RequestElevation() {
    wchar_t szPath[MAX_PATH];
    if (GetModuleFileNameW(nullptr, szPath, MAX_PATH)) {
        SHELLEXECUTEINFOW sei = { sizeof(sei) };
        sei.lpVerb = L"runas";
        sei.lpFile = szPath;
        sei.hwnd = nullptr;
        sei.nShow = SW_NORMAL;

        if (ShellExecuteExW(&sei)) {
            return true;
        }
    }
    return false;
}

int wmain(int argc, wchar_t* argv[]) {
    g_hConsole = GetStdHandle(STD_OUTPUT_HANDLE);
    
    // Set console title
    SetConsoleTitleW(L"Windows Registry Cleaner");

    // Check for command line arguments
    bool forceNoAdmin = false;
    for (int i = 1; i < argc; ++i) {
        if (wcscmp(argv[i], L"--no-admin") == 0 || wcscmp(argv[i], L"-n") == 0) {
            forceNoAdmin = true;
        }
    }

    // Check admin rights
    if (!forceNoAdmin && !IsRunningAsAdmin()) {
        PrintLn(L"Ce programme necessite des droits administrateur.");
        Print(L"Voulez-vous relancer en tant qu'administrateur? (O/N): ");
        
        wchar_t input;
        std::wcin >> input;
        
        if (towupper(input) == L'O' || towupper(input) == L'Y') {
            if (RequestElevation()) {
                return 0;
            } else {
                PrintLn(L"Impossible d'obtenir les droits administrateur.");
                PrintLn(L"Certaines fonctionnalites seront limitees.");
                PrintLn();
                Print(L"Appuyez sur une touche pour continuer...");
                std::wcin.ignore();
                std::wcin.get();
            }
        } else {
            PrintLn(L"Execution sans droits administrateur.");
            PrintLn(L"Certaines cles du registre ne seront pas accessibles.");
            PrintLn();
            Print(L"Appuyez sur une touche pour continuer...");
            std::wcin.ignore();
            std::wcin.get();
        }
    }

    try {
        RegistryCleaner::UI::ConsoleUI ui;
        ui.Run();
    }
    catch (const std::exception& e) {
        Print(L"Erreur fatale: ");
        // Convert narrow string to wide
        std::string msg = e.what();
        std::wstring wmsg(msg.begin(), msg.end());
        PrintLn(wmsg);
        return 1;
    }
    catch (...) {
        PrintLn(L"Erreur fatale inconnue");
        return 1;
    }

    return 0;
}
