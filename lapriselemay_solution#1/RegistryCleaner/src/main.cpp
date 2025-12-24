// main.cpp - Application Entry Point
#include "pch.h"
#include "ui/ConsoleUI.h"

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
    // Set console title
    SetConsoleTitleW(L"Windows Registry Cleaner");

    // Enable ANSI escape sequences for modern terminals
    HANDLE hOut = GetStdHandle(STD_OUTPUT_HANDLE);
    DWORD dwMode = 0;
    GetConsoleMode(hOut, &dwMode);
    SetConsoleMode(hOut, dwMode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);

    // Check for command line arguments
    bool forceNoAdmin = false;
    for (int i = 1; i < argc; ++i) {
        if (wcscmp(argv[i], L"--no-admin") == 0 || wcscmp(argv[i], L"-n") == 0) {
            forceNoAdmin = true;
        }
    }

    // Check admin rights
    if (!forceNoAdmin && !IsRunningAsAdmin()) {
        std::wcout << L"Ce programme nécessite des droits administrateur.\n";
        std::wcout << L"Voulez-vous relancer en tant qu'administrateur? (O/N): ";
        
        wchar_t input;
        std::wcin >> input;
        
        if (towupper(input) == L'O' || towupper(input) == L'Y') {
            if (RequestElevation()) {
                return 0; // Exit this instance
            } else {
                std::wcout << L"Impossible d'obtenir les droits administrateur.\n";
                std::wcout << L"Certaines fonctionnalités seront limitées.\n\n";
                std::wcout << L"Appuyez sur une touche pour continuer...";
                std::wcin.ignore();
                std::wcin.get();
            }
        } else {
            std::wcout << L"Exécution sans droits administrateur.\n";
            std::wcout << L"Certaines clés du registre ne seront pas accessibles.\n\n";
            std::wcout << L"Appuyez sur une touche pour continuer...";
            std::wcin.ignore();
            std::wcin.get();
        }
    }

    try {
        RegistryCleaner::UI::ConsoleUI ui;
        ui.Run();
    }
    catch (const std::exception& e) {
        std::wcerr << L"Erreur fatale: " << e.what() << L"\n";
        return 1;
    }
    catch (...) {
        std::wcerr << L"Erreur fatale inconnue\n";
        return 1;
    }

    return 0;
}
