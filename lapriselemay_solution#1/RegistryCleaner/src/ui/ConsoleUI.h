// ConsoleUI.h - Console User Interface
#pragma once

#include "pch.h"
#include "core/Config.h"
#include "cleaners/RegistryCleaner.h"

namespace RegistryCleaner::UI {

    using namespace Config;
    using namespace Cleaners;
    using namespace Scanners;

    class ConsoleUI {
    public:
        ConsoleUI();
        ~ConsoleUI();

        // Run the main application loop
        void Run();

    private:
        // Menu functions
        void ShowMainMenu();
        void ShowScannerSelection();
        void RunScan();
        void ShowResults();
        void RunClean();
        void ShowBackups();
        void RestoreBackup();
        void ShowAbout();

        // UI helpers
        void SetColor(ConsoleColor color);
        void ResetColor();
        void ClearScreen();
        void PrintHeader(StringView title);
        void PrintSeparator(wchar_t ch = L'-', int length = 60);
        void PrintProgress(StringView message, size_t current, size_t total);
        void WaitForKey(StringView message = L"Appuyez sur une touche pour continuer...");
        int GetUserChoice(int min, int max);
        bool GetYesNo(StringView prompt);

        // Format helpers
        [[nodiscard]] String FormatDuration(chrono::milliseconds ms) const;
        [[nodiscard]] String FormatIssueCount(size_t count) const;

        RegistryCleaner m_cleaner;
        std::vector<RegistryIssue> m_currentIssues;
        std::vector<size_t> m_selectedIndices;
        HANDLE m_hConsole;
        WORD m_defaultAttribs;
    };

} // namespace RegistryCleaner::UI
