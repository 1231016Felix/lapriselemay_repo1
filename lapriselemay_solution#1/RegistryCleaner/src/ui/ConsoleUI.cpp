// ConsoleUI.cpp - Console User Interface Implementation
#include "pch.h"
#include "ui/ConsoleUI.h"
#include "scanners/UninstallScanner.h"
#include "scanners/FileExtensionScanner.h"
#include "scanners/MRUScanner.h"
#include "scanners/StartupScanner.h"
#include "scanners/SharedDllScanner.h"

namespace RegistryCleaner::UI {

    ConsoleUI::ConsoleUI() {
        m_hConsole = GetStdHandle(STD_OUTPUT_HANDLE);
        
        CONSOLE_SCREEN_BUFFER_INFO csbi;
        GetConsoleScreenBufferInfo(m_hConsole, &csbi);
        m_defaultAttribs = csbi.wAttributes;

        // Set console to UTF-16
        SetConsoleOutputCP(CP_UTF8);
        _setmode(_fileno(stdout), _O_U16TEXT);

        // Register all scanners
        m_cleaner.AddScanner(std::make_unique<UninstallScanner>());
        m_cleaner.AddScanner(std::make_unique<FileExtensionScanner>());
        m_cleaner.AddScanner(std::make_unique<MRUScanner>());
        m_cleaner.AddScanner(std::make_unique<StartupScanner>());
        m_cleaner.AddScanner(std::make_unique<SharedDllScanner>());
    }

    ConsoleUI::~ConsoleUI() {
        ResetColor();
    }

    void ConsoleUI::Run() {
        bool running = true;

        while (running) {
            ShowMainMenu();
            
            int choice = GetUserChoice(0, 6);
            
            switch (choice) {
                case 1: ShowScannerSelection(); break;
                case 2: RunScan(); break;
                case 3: ShowResults(); break;
                case 4: RunClean(); break;
                case 5: ShowBackups(); break;
                case 6: ShowAbout(); break;
                case 0: running = false; break;
            }
        }

        ClearScreen();
        std::wcout << L"Au revoir!\n";
    }

    void ConsoleUI::ShowMainMenu() {
        ClearScreen();
        PrintHeader(Config::APP_NAME);
        
        std::wcout << L"\n";
        SetColor(ConsoleColor::Cyan);
        std::wcout << L"  [1]";
        ResetColor();
        std::wcout << L" Sélectionner les analyses\n";
        
        SetColor(ConsoleColor::Cyan);
        std::wcout << L"  [2]";
        ResetColor();
        std::wcout << L" Analyser le registre\n";
        
        SetColor(ConsoleColor::Cyan);
        std::wcout << L"  [3]";
        ResetColor();
        std::wcout << L" Voir les résultats (" << m_currentIssues.size() << L" problèmes)\n";
        
        SetColor(ConsoleColor::Cyan);
        std::wcout << L"  [4]";
        ResetColor();
        std::wcout << L" Nettoyer les entrées sélectionnées\n";
        
        SetColor(ConsoleColor::Cyan);
        std::wcout << L"  [5]";
        ResetColor();
        std::wcout << L" Gérer les sauvegardes\n";
        
        SetColor(ConsoleColor::Cyan);
        std::wcout << L"  [6]";
        ResetColor();
        std::wcout << L" À propos\n";
        
        std::wcout << L"\n";
        SetColor(ConsoleColor::Red);
        std::wcout << L"  [0]";
        ResetColor();
        std::wcout << L" Quitter\n";
        
        PrintSeparator();
        std::wcout << L"Votre choix: ";
    }

    void ConsoleUI::ShowScannerSelection() {
        ClearScreen();
        PrintHeader(L"Sélection des analyses");
        
        const auto& scanners = m_cleaner.GetScanners();
        
        std::wcout << L"\n";
        for (size_t i = 0; i < scanners.size(); ++i) {
            SetColor(ConsoleColor::Cyan);
            std::wcout << L"  [" << (i + 1) << L"]";
            ResetColor();
            
            if (scanners[i]->IsEnabled()) {
                SetColor(ConsoleColor::Green);
                std::wcout << L" [X] ";
            } else {
                SetColor(ConsoleColor::Red);
                std::wcout << L" [ ] ";
            }
            ResetColor();
            
            std::wcout << scanners[i]->Name() << L"\n";
        }
        
        std::wcout << L"\n";
        SetColor(ConsoleColor::Yellow);
        std::wcout << L"  [0]";
        ResetColor();
        std::wcout << L" Retour\n";
        
        PrintSeparator();
        std::wcout << L"Entrez le numéro pour activer/désactiver: ";
        
        int choice = GetUserChoice(0, static_cast<int>(scanners.size()));
        
        if (choice > 0 && choice <= static_cast<int>(scanners.size())) {
            auto& scanner = m_cleaner.GetScanners()[choice - 1];
            scanner->SetEnabled(!scanner->IsEnabled());
            ShowScannerSelection(); // Recurse to show updated state
        }
    }

    void ConsoleUI::RunScan() {
        ClearScreen();
        PrintHeader(L"Analyse du registre");
        
        std::wcout << L"\n";
        SetColor(ConsoleColor::Yellow);
        std::wcout << L"Analyse en cours...\n\n";
        ResetColor();

        auto progressCallback = [this](StringView scanner, StringView key, size_t found) {
            // Clear line and print progress
            std::wcout << L"\r                                                                              ";
            std::wcout << L"\r  [" << scanner << L"] " << found << L" problèmes - " << key.substr(0, 50);
            std::wcout.flush();
        };

        m_currentIssues = m_cleaner.Scan(progressCallback);
        m_selectedIndices.clear();
        
        // Select all by default
        for (size_t i = 0; i < m_currentIssues.size(); ++i) {
            if (m_currentIssues[i].severity != Severity::Critical) {
                m_selectedIndices.push_back(i);
            }
        }

        std::wcout << L"\n\n";
        PrintSeparator();
        
        const auto& stats = m_cleaner.GetStats();
        
        SetColor(ConsoleColor::Green);
        std::wcout << L"Analyse terminée!\n";
        ResetColor();
        
        std::wcout << L"  Problèmes trouvés: " << stats.issuesFound << L"\n";
        std::wcout << L"  Durée: " << FormatDuration(stats.scanDuration) << L"\n";
        
        WaitForKey();
    }

    void ConsoleUI::ShowResults() {
        if (m_currentIssues.empty()) {
            ClearScreen();
            PrintHeader(L"Résultats de l'analyse");
            std::wcout << L"\n";
            SetColor(ConsoleColor::Yellow);
            std::wcout << L"Aucun problème trouvé. Lancez d'abord une analyse.\n";
            ResetColor();
            WaitForKey();
            return;
        }

        size_t page = 0;
        constexpr size_t ITEMS_PER_PAGE = 15;
        size_t totalPages = (m_currentIssues.size() + ITEMS_PER_PAGE - 1) / ITEMS_PER_PAGE;

        while (true) {
            ClearScreen();
            PrintHeader(std::format(L"Résultats ({}/{})", m_currentIssues.size(), m_selectedIndices.size()));
            
            std::wcout << L"\n";
            
            size_t start = page * ITEMS_PER_PAGE;
            size_t end = std::min(start + ITEMS_PER_PAGE, m_currentIssues.size());
            
            for (size_t i = start; i < end; ++i) {
                const auto& issue = m_currentIssues[i];
                bool selected = ranges::find(m_selectedIndices, i) != m_selectedIndices.end();
                
                // Severity color
                switch (issue.severity) {
                    case Severity::Low: SetColor(ConsoleColor::Green); break;
                    case Severity::Medium: SetColor(ConsoleColor::Yellow); break;
                    case Severity::High: SetColor(ConsoleColor::Red); break;
                    case Severity::Critical: SetColor(ConsoleColor::Magenta); break;
                }
                
                std::wcout << (selected ? L"[X] " : L"[ ] ");
                ResetColor();
                
                std::wcout << std::format(L"{:3}. ", i + 1);
                std::wcout << issue.description.substr(0, 55) << L"\n";
            }
            
            std::wcout << L"\n";
            std::wcout << L"Page " << (page + 1) << L"/" << totalPages << L"\n";
            PrintSeparator();
            std::wcout << L"[N]ext [P]rev [A]ll [D]eselect [T]oggle# [Q]uit: ";
            
            wchar_t input;
            std::wcin >> input;
            std::wcin.ignore(std::numeric_limits<std::streamsize>::max(), L'\n');
            
            input = towupper(input);
            
            if (input == L'N' && page < totalPages - 1) {
                ++page;
            } else if (input == L'P' && page > 0) {
                --page;
            } else if (input == L'A') {
                m_selectedIndices.clear();
                for (size_t i = 0; i < m_currentIssues.size(); ++i) {
                    if (m_currentIssues[i].severity != Severity::Critical) {
                        m_selectedIndices.push_back(i);
                    }
                }
            } else if (input == L'D') {
                m_selectedIndices.clear();
            } else if (input == L'T') {
                std::wcout << L"Numéro à basculer: ";
                size_t num;
                std::wcin >> num;
                std::wcin.ignore(std::numeric_limits<std::streamsize>::max(), L'\n');
                
                if (num > 0 && num <= m_currentIssues.size()) {
                    size_t idx = num - 1;
                    auto it = ranges::find(m_selectedIndices, idx);
                    if (it != m_selectedIndices.end()) {
                        m_selectedIndices.erase(it);
                    } else if (m_currentIssues[idx].severity != Severity::Critical) {
                        m_selectedIndices.push_back(idx);
                    }
                }
            } else if (input == L'Q') {
                break;
            }
        }
    }

    void ConsoleUI::RunClean() {
        if (m_selectedIndices.empty()) {
            ClearScreen();
            PrintHeader(L"Nettoyage");
            std::wcout << L"\n";
            SetColor(ConsoleColor::Yellow);
            std::wcout << L"Aucun élément sélectionné pour le nettoyage.\n";
            ResetColor();
            WaitForKey();
            return;
        }

        ClearScreen();
        PrintHeader(L"Nettoyage du registre");
        
        std::wcout << L"\n";
        SetColor(ConsoleColor::Red);
        std::wcout << L"ATTENTION: Cette opération va modifier le registre Windows!\n";
        ResetColor();
        std::wcout << L"Éléments à nettoyer: " << m_selectedIndices.size() << L"\n";
        std::wcout << L"Une sauvegarde sera créée automatiquement.\n\n";

        if (!GetYesNo(L"Voulez-vous continuer?")) {
            return;
        }

        // Build list of issues to clean
        std::vector<RegistryIssue> toClean;
        for (size_t idx : m_selectedIndices) {
            if (idx < m_currentIssues.size()) {
                toClean.push_back(m_currentIssues[idx]);
            }
        }

        std::wcout << L"\nNettoyage en cours...\n";

        auto progressCallback = [this](size_t current, size_t total, const RegistryIssue& issue) {
            PrintProgress(issue.description.substr(0, 40), current, total);
        };

        auto stats = m_cleaner.Clean(toClean, true, progressCallback);

        std::wcout << L"\n\n";
        PrintSeparator();
        SetColor(ConsoleColor::Green);
        std::wcout << L"Nettoyage terminé!\n";
        ResetColor();
        
        std::wcout << L"  Nettoyés: " << stats.issuesCleaned << L"\n";
        std::wcout << L"  Échoués: " << stats.issuesFailed << L"\n";
        std::wcout << L"  Ignorés: " << stats.issuesSkipped << L"\n";
        std::wcout << L"  Durée: " << FormatDuration(stats.cleanDuration) << L"\n";

        // Clear cleaned issues from current list
        m_currentIssues.clear();
        m_selectedIndices.clear();

        WaitForKey();
    }

    void ConsoleUI::ShowBackups() {
        ClearScreen();
        PrintHeader(L"Sauvegardes");
        
        auto backups = m_cleaner.GetBackupManager().ListBackups();
        
        if (backups.empty()) {
            std::wcout << L"\n";
            SetColor(ConsoleColor::Yellow);
            std::wcout << L"Aucune sauvegarde disponible.\n";
            ResetColor();
            WaitForKey();
            return;
        }

        std::wcout << L"\n";
        for (size_t i = 0; i < backups.size(); ++i) {
            SetColor(ConsoleColor::Cyan);
            std::wcout << L"  [" << (i + 1) << L"]";
            ResetColor();
            std::wcout << L" " << backups[i].filename().wstring() << L"\n";
        }

        std::wcout << L"\n";
        SetColor(ConsoleColor::Yellow);
        std::wcout << L"  [R]";
        ResetColor();
        std::wcout << L" Restaurer une sauvegarde\n";
        
        SetColor(ConsoleColor::Red);
        std::wcout << L"  [0]";
        ResetColor();
        std::wcout << L" Retour\n";

        PrintSeparator();
        std::wcout << L"Votre choix: ";
        
        wchar_t input;
        std::wcin >> input;
        std::wcin.ignore(std::numeric_limits<std::streamsize>::max(), L'\n');

        if (towupper(input) == L'R') {
            RestoreBackup();
        }
    }

    void ConsoleUI::RestoreBackup() {
        auto backups = m_cleaner.GetBackupManager().ListBackups();
        if (backups.empty()) return;

        std::wcout << L"Numéro de la sauvegarde à restaurer (0 pour annuler): ";
        int choice = GetUserChoice(0, static_cast<int>(backups.size()));
        
        if (choice == 0) return;

        const auto& backupPath = backups[choice - 1];
        
        std::wcout << L"\n";
        SetColor(ConsoleColor::Red);
        std::wcout << L"ATTENTION: La restauration va modifier le registre!\n";
        ResetColor();

        if (!GetYesNo(L"Confirmer la restauration?")) {
            return;
        }

        std::wcout << L"Restauration en cours...\n";
        
        auto result = m_cleaner.GetBackupManager().RestoreBackup(backupPath);
        
        if (result) {
            SetColor(ConsoleColor::Green);
            std::wcout << L"Restauration réussie!\n";
        } else {
            SetColor(ConsoleColor::Red);
            std::wcout << L"Échec: " << result.error() << L"\n";
        }
        ResetColor();
        
        WaitForKey();
    }

    void ConsoleUI::ShowAbout() {
        ClearScreen();
        PrintHeader(L"À propos");
        
        std::wcout << L"\n";
        SetColor(ConsoleColor::Cyan);
        std::wcout << L"  " << Config::APP_NAME << L"\n";
        ResetColor();
        std::wcout << L"  Version " << Config::APP_VERSION << L"\n\n";
        
        std::wcout << L"  Un outil moderne de nettoyage du registre Windows\n";
        std::wcout << L"  écrit en C++20 pour Visual Studio 2022.\n\n";
        
        SetColor(ConsoleColor::Yellow);
        std::wcout << L"  Fonctionnalités:\n";
        ResetColor();
        std::wcout << L"  - Détection des entrées orphelines\n";
        std::wcout << L"  - Sauvegarde automatique avant nettoyage\n";
        std::wcout << L"  - Protection des clés système critiques\n";
        std::wcout << L"  - Restauration des sauvegardes\n\n";
        
        SetColor(ConsoleColor::Red);
        std::wcout << L"  AVERTISSEMENT:\n";
        ResetColor();
        std::wcout << L"  Modifier le registre peut rendre votre système\n";
        std::wcout << L"  instable. Utilisez cet outil avec précaution.\n";
        
        WaitForKey();
    }

    // UI Helper implementations
    void ConsoleUI::SetColor(ConsoleColor color) {
        SetConsoleTextAttribute(m_hConsole, static_cast<WORD>(color));
    }

    void ConsoleUI::ResetColor() {
        SetConsoleTextAttribute(m_hConsole, m_defaultAttribs);
    }

    void ConsoleUI::ClearScreen() {
        CONSOLE_SCREEN_BUFFER_INFO csbi;
        GetConsoleScreenBufferInfo(m_hConsole, &csbi);
        DWORD count, size = csbi.dwSize.X * csbi.dwSize.Y;
        COORD coord = { 0, 0 };
        FillConsoleOutputCharacterW(m_hConsole, L' ', size, coord, &count);
        FillConsoleOutputAttribute(m_hConsole, m_defaultAttribs, size, coord, &count);
        SetConsoleCursorPosition(m_hConsole, coord);
    }

    void ConsoleUI::PrintHeader(StringView title) {
        PrintSeparator(L'=');
        SetColor(ConsoleColor::White);
        std::wcout << L"  " << title << L"\n";
        ResetColor();
        PrintSeparator(L'=');
    }

    void ConsoleUI::PrintSeparator(wchar_t ch, int length) {
        for (int i = 0; i < length; ++i) {
            std::wcout << ch;
        }
        std::wcout << L"\n";
    }

    void ConsoleUI::PrintProgress(StringView message, size_t current, size_t total) {
        int percent = static_cast<int>((current * 100) / total);
        std::wcout << L"\r  [";
        
        int barWidth = 30;
        int pos = (percent * barWidth) / 100;
        
        for (int i = 0; i < barWidth; ++i) {
            if (i < pos) std::wcout << L"█";
            else if (i == pos) std::wcout << L"▓";
            else std::wcout << L"░";
        }
        
        std::wcout << L"] " << percent << L"% - " << message;
        std::wcout.flush();
    }

    void ConsoleUI::WaitForKey(StringView message) {
        std::wcout << L"\n" << message;
        std::wcin.get();
    }

    int ConsoleUI::GetUserChoice(int min, int max) {
        int choice;
        while (!(std::wcin >> choice) || choice < min || choice > max) {
            std::wcin.clear();
            std::wcin.ignore(std::numeric_limits<std::streamsize>::max(), L'\n');
            std::wcout << L"Choix invalide. Réessayez: ";
        }
        std::wcin.ignore(std::numeric_limits<std::streamsize>::max(), L'\n');
        return choice;
    }

    bool ConsoleUI::GetYesNo(StringView prompt) {
        std::wcout << prompt << L" (O/N): ";
        wchar_t input;
        std::wcin >> input;
        std::wcin.ignore(std::numeric_limits<std::streamsize>::max(), L'\n');
        return towupper(input) == L'O' || towupper(input) == L'Y';
    }

    String ConsoleUI::FormatDuration(chrono::milliseconds ms) const {
        if (ms.count() < 1000) {
            return std::format(L"{} ms", ms.count());
        }
        auto secs = chrono::duration_cast<chrono::seconds>(ms);
        return std::format(L"{}.{:03d} s", secs.count(), ms.count() % 1000);
    }

    String ConsoleUI::FormatIssueCount(size_t count) const {
        if (count == 0) return L"aucun problème";
        if (count == 1) return L"1 problème";
        return std::format(L"{} problèmes", count);
    }

} // namespace RegistryCleaner::UI
