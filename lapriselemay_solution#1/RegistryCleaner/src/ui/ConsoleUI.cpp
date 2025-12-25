// ConsoleUI.cpp - Console User Interface Implementation
#include "pch.h"
#include "ui/ConsoleUI.h"
#include "scanners/UninstallScanner.h"
#include "scanners/FileExtensionScanner.h"
#include "scanners/MRUScanner.h"
#include "scanners/StartupScanner.h"
#include "scanners/SharedDllScanner.h"
#include "scanners/ActiveXScanner.h"
#include "scanners/AppPathScanner.h"
#include "scanners/SoftwarePathScanner.h"
#include "scanners/HelpFileScanner.h"
#include "scanners/FirewallScanner.h"
#include "scanners/FontScanner.h"
#include "scanners/StartMenuScanner.h"
#include "scanners/SoundEventScanner.h"
#include "scanners/IEHistoryScanner.h"
#include "scanners/ImageExecutionScanner.h"
#include "scanners/EmptyKeyScanner.h"
#include "scanners/ServiceScanner.h"
#include "scanners/MUICacheScanner.h"
#include "scanners/ContextMenuScanner.h"

namespace RegistryCleaner::UI {

    ConsoleUI::ConsoleUI() {
        m_hConsole = GetStdHandle(STD_OUTPUT_HANDLE);
        
        CONSOLE_SCREEN_BUFFER_INFO csbi;
        GetConsoleScreenBufferInfo(m_hConsole, &csbi);
        m_defaultAttribs = csbi.wAttributes;

        // Register all scanners
        m_cleaner.AddScanner(std::make_unique<ActiveXScanner>());
        m_cleaner.AddScanner(std::make_unique<SoftwarePathScanner>());
        m_cleaner.AddScanner(std::make_unique<AppPathScanner>());
        m_cleaner.AddScanner(std::make_unique<FileExtensionScanner>());
        m_cleaner.AddScanner(std::make_unique<HelpFileScanner>());
        m_cleaner.AddScanner(std::make_unique<FirewallScanner>());
        m_cleaner.AddScanner(std::make_unique<FontScanner>());
        m_cleaner.AddScanner(std::make_unique<SharedDllScanner>());
        m_cleaner.AddScanner(std::make_unique<MRUScanner>());
        m_cleaner.AddScanner(std::make_unique<UninstallScanner>());
        m_cleaner.AddScanner(std::make_unique<StartMenuScanner>());
        m_cleaner.AddScanner(std::make_unique<StartupScanner>());
        m_cleaner.AddScanner(std::make_unique<SoundEventScanner>());
        m_cleaner.AddScanner(std::make_unique<IEHistoryScanner>());
        m_cleaner.AddScanner(std::make_unique<ImageExecutionScanner>());
        m_cleaner.AddScanner(std::make_unique<EmptyKeyScanner>());
        m_cleaner.AddScanner(std::make_unique<ServiceScanner>());
        m_cleaner.AddScanner(std::make_unique<MUICacheScanner>());
        m_cleaner.AddScanner(std::make_unique<ContextMenuScanner>());
    }

    ConsoleUI::~ConsoleUI() {
        ResetColor();
    }

    // Core print function using WriteConsoleW for proper Unicode support
    void ConsoleUI::Print(StringView text) {
        DWORD written;
        WriteConsoleW(m_hConsole, text.data(), static_cast<DWORD>(text.size()), &written, nullptr);
    }

    void ConsoleUI::PrintLn(StringView text) {
        Print(text);
        Print(L"\n");
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
        PrintLn(L"Au revoir!");
    }

    void ConsoleUI::ShowMainMenu() {
        ClearScreen();
        PrintHeader(Config::APP_NAME);
        
        PrintLn();
        SetColor(ConsoleColor::Cyan);
        Print(L"  [1]");
        ResetColor();
        PrintLn(L" Selectionner les analyses");
        
        SetColor(ConsoleColor::Cyan);
        Print(L"  [2]");
        ResetColor();
        PrintLn(L" Analyser le registre");
        
        SetColor(ConsoleColor::Cyan);
        Print(L"  [3]");
        ResetColor();
        Print(L" Voir les resultats (");
        Print(std::to_wstring(m_currentIssues.size()));
        PrintLn(L" problemes)");
        
        SetColor(ConsoleColor::Cyan);
        Print(L"  [4]");
        ResetColor();
        PrintLn(L" Nettoyer les entrees selectionnees");
        
        SetColor(ConsoleColor::Cyan);
        Print(L"  [5]");
        ResetColor();
        PrintLn(L" Gerer les sauvegardes");
        
        SetColor(ConsoleColor::Cyan);
        Print(L"  [6]");
        ResetColor();
        PrintLn(L" A propos");
        
        PrintLn();
        SetColor(ConsoleColor::Red);
        Print(L"  [0]");
        ResetColor();
        PrintLn(L" Quitter");
        
        PrintSeparator();
        Print(L"Votre choix: ");
    }

    void ConsoleUI::ShowScannerSelection() {
        ClearScreen();
        PrintHeader(L"Selection des analyses");
        
        const auto& scanners = m_cleaner.GetScanners();
        
        PrintLn();
        for (size_t i = 0; i < scanners.size(); ++i) {
            SetColor(ConsoleColor::Cyan);
            Print(std::format(L"  [{}]", i + 1));
            ResetColor();
            
            if (scanners[i]->IsEnabled()) {
                SetColor(ConsoleColor::Green);
                Print(L" [X] ");
            } else {
                SetColor(ConsoleColor::Red);
                Print(L" [ ] ");
            }
            ResetColor();
            
            PrintLn(scanners[i]->Name());
        }
        
        PrintLn();
        SetColor(ConsoleColor::Yellow);
        Print(L"  [0]");
        ResetColor();
        PrintLn(L" Retour");
        
        PrintSeparator();
        Print(L"Entrez le numero pour activer/desactiver: ");
        
        int choice = GetUserChoice(0, static_cast<int>(scanners.size()));
        
        if (choice > 0 && choice <= static_cast<int>(scanners.size())) {
            auto& scanner = m_cleaner.GetScanners()[choice - 1];
            scanner->SetEnabled(!scanner->IsEnabled());
            ShowScannerSelection();
        }
    }

    void ConsoleUI::RunScan() {
        ClearScreen();
        PrintHeader(L"Analyse du registre");
        
        PrintLn();
        SetColor(ConsoleColor::Yellow);
        PrintLn(L"Analyse en cours...");
        PrintLn();
        ResetColor();

        auto progressCallback = [this](StringView scanner, StringView key, size_t found) {
            String status = std::format(L"\r  [{}] {} problemes - {}", 
                scanner, found, key.substr(0, 50));
            Print(L"\r                                                                              ");
            Print(status);
        };

        m_currentIssues = m_cleaner.Scan(progressCallback);
        m_selectedIndices.clear();
        
        for (size_t i = 0; i < m_currentIssues.size(); ++i) {
            if (m_currentIssues[i].severity != Severity::Critical) {
                m_selectedIndices.push_back(i);
            }
        }

        PrintLn();
        PrintLn();
        PrintSeparator();
        
        const auto& stats = m_cleaner.GetStats();
        
        SetColor(ConsoleColor::Green);
        PrintLn(L"Analyse terminee!");
        ResetColor();
        
        Print(L"  Problemes trouves: ");
        PrintLn(std::to_wstring(stats.issuesFound));
        Print(L"  Duree: ");
        PrintLn(FormatDuration(stats.scanDuration));
        
        WaitForKey();
    }

    void ConsoleUI::ShowResults() {
        if (m_currentIssues.empty()) {
            ClearScreen();
            PrintHeader(L"Resultats de l'analyse");
            PrintLn();
            SetColor(ConsoleColor::Yellow);
            PrintLn(L"Aucun probleme trouve. Lancez d'abord une analyse.");
            ResetColor();
            WaitForKey();
            return;
        }

        size_t page = 0;
        constexpr size_t ITEMS_PER_PAGE = 15;
        size_t totalPages = (m_currentIssues.size() + ITEMS_PER_PAGE - 1) / ITEMS_PER_PAGE;

        while (true) {
            ClearScreen();
            PrintHeader(std::format(L"Resultats ({}/{})", m_currentIssues.size(), m_selectedIndices.size()));
            
            PrintLn();
            
            size_t start = page * ITEMS_PER_PAGE;
            size_t end = std::min(start + ITEMS_PER_PAGE, m_currentIssues.size());
            
            for (size_t i = start; i < end; ++i) {
                const auto& issue = m_currentIssues[i];
                bool selected = ranges::find(m_selectedIndices, i) != m_selectedIndices.end();
                
                switch (issue.severity) {
                    case Severity::Low: SetColor(ConsoleColor::Green); break;
                    case Severity::Medium: SetColor(ConsoleColor::Yellow); break;
                    case Severity::High: SetColor(ConsoleColor::Red); break;
                    case Severity::Critical: SetColor(ConsoleColor::Magenta); break;
                }
                
                Print(selected ? L"[X] " : L"[ ] ");
                ResetColor();
                
                PrintLn(std::format(L"{:3}. {}", i + 1, issue.description.substr(0, 55)));
            }
            
            PrintLn();
            PrintLn(std::format(L"Page {}/{}", page + 1, totalPages));
            PrintSeparator();
            Print(L"[N]ext [P]rev [A]ll [D]eselect [T]oggle# [Q]uit: ");
            
            wchar_t input;
            std::wcin >> input;
            std::wcin.ignore((std::numeric_limits<std::streamsize>::max)(), L'\n');
            
            input = static_cast<wchar_t>(towupper(input));
            
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
                Print(L"Numero a basculer: ");
                size_t num;
                std::wcin >> num;
                std::wcin.ignore((std::numeric_limits<std::streamsize>::max)(), L'\n');
                
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
            PrintLn();
            SetColor(ConsoleColor::Yellow);
            PrintLn(L"Aucun element selectionne pour le nettoyage.");
            ResetColor();
            WaitForKey();
            return;
        }

        ClearScreen();
        PrintHeader(L"Nettoyage du registre");
        
        PrintLn();
        SetColor(ConsoleColor::Red);
        PrintLn(L"ATTENTION: Cette operation va modifier le registre Windows!");
        ResetColor();
        Print(L"Elements a nettoyer: ");
        PrintLn(std::to_wstring(m_selectedIndices.size()));
        PrintLn(L"Une sauvegarde sera creee automatiquement.");
        PrintLn();

        if (!GetYesNo(L"Voulez-vous continuer?")) {
            return;
        }

        // Ask about force delete mode
        PrintLn();
        SetColor(ConsoleColor::Yellow);
        PrintLn(L"Mode suppression forcee:");
        ResetColor();
        PrintLn(L"  Ce mode permet de supprimer les cles protegees par le systeme");
        PrintLn(L"  en prenant possession des cles (TrustedInstaller/SYSTEM).");
        PrintLn(L"  Les cles verrouillees seront programmees pour suppression au redemarrage.");
        PrintLn();
        SetColor(ConsoleColor::Red);
        PrintLn(L"  RISQUE: Peut causer des instabilites si des cles systeme sont supprimees!");
        ResetColor();
        PrintLn();
        
        bool forceDelete = GetYesNo(L"Activer le mode suppression forcee?");

        std::vector<RegistryIssue> toClean;
        for (size_t idx : m_selectedIndices) {
            if (idx < m_currentIssues.size()) {
                toClean.push_back(m_currentIssues[idx]);
            }
        }

        PrintLn();
        PrintLn(L"Nettoyage en cours...");

        auto progressCallback = [this](size_t current, size_t total, const RegistryIssue& issue) {
            PrintProgress(issue.description.substr(0, 40), current, total);
        };

        auto stats = m_cleaner.Clean(toClean, true, progressCallback, forceDelete);

        PrintLn();
        PrintLn();
        PrintSeparator();
        SetColor(ConsoleColor::Green);
        PrintLn(L"Nettoyage termine!");
        ResetColor();
        
        Print(L"  Nettoyes: ");
        PrintLn(std::to_wstring(stats.issuesCleaned));
        
        if (forceDelete && (stats.forcedDeletes > 0 || stats.scheduledForReboot > 0)) {
            Print(L"    - Suppressions forcees: ");
            PrintLn(std::to_wstring(stats.forcedDeletes));
            Print(L"    - Programmees au redemarrage: ");
            PrintLn(std::to_wstring(stats.scheduledForReboot));
        }
        
        Print(L"  Echoues: ");
        PrintLn(std::to_wstring(stats.issuesFailed));
        Print(L"  Ignores: ");
        PrintLn(std::to_wstring(stats.issuesSkipped));
        Print(L"  Duree: ");
        PrintLn(FormatDuration(stats.cleanDuration));

        // Show reboot notice if needed
        if (stats.scheduledForReboot > 0) {
            PrintLn();
            SetColor(ConsoleColor::Cyan);
            PrintLn(L"*** Un redemarrage est necessaire pour completer certaines suppressions ***");
            ResetColor();
        }

        // Show failed items if any
        if (!stats.failedItems.empty()) {
            PrintLn();
            SetColor(ConsoleColor::Yellow);
            PrintLn(L"Elements non supprimes (acces refuse ou cle protegee):");
            ResetColor();
            size_t showCount = std::min(stats.failedItems.size(), size_t(10));
            for (size_t i = 0; i < showCount; ++i) {
                Print(L"  - ");
                // Truncate long paths
                if (stats.failedItems[i].length() > 70) {
                    PrintLn(stats.failedItems[i].substr(0, 67) + L"...");
                } else {
                    PrintLn(stats.failedItems[i]);
                }
            }
            if (stats.failedItems.size() > 10) {
                Print(L"  ... et ");
                Print(std::to_wstring(stats.failedItems.size() - 10));
                PrintLn(L" autres");
            }
        }

        m_currentIssues.clear();
        m_selectedIndices.clear();

        WaitForKey();
    }

    void ConsoleUI::ShowBackups() {
        ClearScreen();
        PrintHeader(L"Sauvegardes");
        
        auto backups = m_cleaner.GetBackupManager().ListBackups();
        
        if (backups.empty()) {
            PrintLn();
            SetColor(ConsoleColor::Yellow);
            PrintLn(L"Aucune sauvegarde disponible.");
            ResetColor();
            WaitForKey();
            return;
        }

        PrintLn();
        for (size_t i = 0; i < backups.size(); ++i) {
            SetColor(ConsoleColor::Cyan);
            Print(std::format(L"  [{}]", i + 1));
            ResetColor();
            Print(L" ");
            PrintLn(backups[i].filename().wstring());
        }

        PrintLn();
        SetColor(ConsoleColor::Yellow);
        Print(L"  [R]");
        ResetColor();
        PrintLn(L" Restaurer une sauvegarde");
        
        SetColor(ConsoleColor::Red);
        Print(L"  [0]");
        ResetColor();
        PrintLn(L" Retour");

        PrintSeparator();
        Print(L"Votre choix: ");
        
        wchar_t input;
        std::wcin >> input;
        std::wcin.ignore((std::numeric_limits<std::streamsize>::max)(), L'\n');

        if (towupper(input) == L'R') {
            RestoreBackup();
        }
    }

    void ConsoleUI::RestoreBackup() {
        auto backups = m_cleaner.GetBackupManager().ListBackups();
        if (backups.empty()) return;

        Print(L"Numero de la sauvegarde a restaurer (0 pour annuler): ");
        int choice = GetUserChoice(0, static_cast<int>(backups.size()));
        
        if (choice == 0) return;

        const auto& backupPath = backups[choice - 1];
        
        PrintLn();
        SetColor(ConsoleColor::Red);
        PrintLn(L"ATTENTION: La restauration va modifier le registre!");
        ResetColor();

        if (!GetYesNo(L"Confirmer la restauration?")) {
            return;
        }

        PrintLn(L"Restauration en cours...");
        
        auto result = m_cleaner.GetBackupManager().RestoreBackup(backupPath);
        
        if (result) {
            SetColor(ConsoleColor::Green);
            PrintLn(L"Restauration reussie!");
        } else {
            SetColor(ConsoleColor::Red);
            Print(L"Echec: ");
            PrintLn(result.error());
        }
        ResetColor();
        
        WaitForKey();
    }

    void ConsoleUI::ShowAbout() {
        ClearScreen();
        PrintHeader(L"A propos");
        
        PrintLn();
        SetColor(ConsoleColor::Cyan);
        Print(L"  ");
        PrintLn(Config::APP_NAME);
        ResetColor();
        Print(L"  Version ");
        PrintLn(Config::APP_VERSION);
        PrintLn();
        
        PrintLn(L"  Un outil moderne de nettoyage du registre Windows");
        PrintLn(L"  ecrit en C++23 pour Visual Studio 2025.");
        PrintLn();
        
        SetColor(ConsoleColor::Yellow);
        PrintLn(L"  Fonctionnalites:");
        ResetColor();
        PrintLn(L"  - Detection des entrees orphelines");
        PrintLn(L"  - Sauvegarde automatique avant nettoyage");
        PrintLn(L"  - Protection des cles systeme critiques");
        PrintLn(L"  - Restauration des sauvegardes");
        PrintLn();
        
        SetColor(ConsoleColor::Red);
        PrintLn(L"  AVERTISSEMENT:");
        ResetColor();
        PrintLn(L"  Modifier le registre peut rendre votre systeme");
        PrintLn(L"  instable. Utilisez cet outil avec precaution.");
        
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
        Print(L"  ");
        PrintLn(title);
        ResetColor();
        PrintSeparator(L'=');
    }

    void ConsoleUI::PrintSeparator(wchar_t ch, int length) {
        String sep(length, ch);
        PrintLn(sep);
    }

    void ConsoleUI::PrintProgress(StringView message, size_t current, size_t total) {
        int percent = static_cast<int>((current * 100) / total);
        
        String bar = L"\r  [";
        int barWidth = 30;
        int pos = (percent * barWidth) / 100;
        
        for (int i = 0; i < barWidth; ++i) {
            if (i < pos) bar += L'#';
            else if (i == pos) bar += L'>';
            else bar += L'-';
        }
        
        bar += std::format(L"] {}% - {}", percent, message);
        Print(bar);
    }

    void ConsoleUI::WaitForKey(StringView message) {
        PrintLn();
        Print(message);
        std::wcin.get();
    }

    int ConsoleUI::GetUserChoice(int min, int max) {
        int choice;
        while (!(std::wcin >> choice) || choice < min || choice > max) {
            std::wcin.clear();
            std::wcin.ignore((std::numeric_limits<std::streamsize>::max)(), L'\n');
            Print(L"Choix invalide. Reessayez: ");
        }
        std::wcin.ignore((std::numeric_limits<std::streamsize>::max)(), L'\n');
        return choice;
    }

    bool ConsoleUI::GetYesNo(StringView prompt) {
        Print(prompt);
        Print(L" (O/N): ");
        wchar_t input;
        std::wcin >> input;
        std::wcin.ignore((std::numeric_limits<std::streamsize>::max)(), L'\n');
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
        if (count == 0) return L"aucun probleme";
        if (count == 1) return L"1 probleme";
        return std::format(L"{} problemes", count);
    }

} // namespace RegistryCleaner::UI
