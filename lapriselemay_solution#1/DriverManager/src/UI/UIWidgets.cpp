#include "UIWidgets.h"
#include "../ManufacturerLinks.h"
#include <shellapi.h>
#include <algorithm>
#include <map>
#include <set>

namespace DriverManager {
namespace UI {

    // ============================================================================
    // Helper Functions
    // ============================================================================

    ImVec4 GetStatusColor(DriverStatus status) {
        switch (status) {
            case DriverStatus::OK:
                return ImVec4(Constants::Colors::STATUS_OK[0], Constants::Colors::STATUS_OK[1], 
                             Constants::Colors::STATUS_OK[2], Constants::Colors::STATUS_OK[3]);
            case DriverStatus::Warning:
                return ImVec4(Constants::Colors::STATUS_WARNING[0], Constants::Colors::STATUS_WARNING[1],
                             Constants::Colors::STATUS_WARNING[2], Constants::Colors::STATUS_WARNING[3]);
            case DriverStatus::Error:
                return ImVec4(Constants::Colors::STATUS_ERROR[0], Constants::Colors::STATUS_ERROR[1],
                             Constants::Colors::STATUS_ERROR[2], Constants::Colors::STATUS_ERROR[3]);
            case DriverStatus::Disabled:
                return ImVec4(Constants::Colors::STATUS_DISABLED[0], Constants::Colors::STATUS_DISABLED[1],
                             Constants::Colors::STATUS_DISABLED[2], Constants::Colors::STATUS_DISABLED[3]);
            default:
                return ImVec4(Constants::Colors::STATUS_UNKNOWN[0], Constants::Colors::STATUS_UNKNOWN[1],
                             Constants::Colors::STATUS_UNKNOWN[2], Constants::Colors::STATUS_UNKNOWN[3]);
        }
    }

    ImVec4 GetAgeColor(DriverAge age) {
        switch (age) {
            case DriverAge::Current:
                return ImVec4(Constants::Colors::AGE_CURRENT[0], Constants::Colors::AGE_CURRENT[1],
                             Constants::Colors::AGE_CURRENT[2], Constants::Colors::AGE_CURRENT[3]);
            case DriverAge::Old:
                return ImVec4(Constants::Colors::AGE_OLD[0], Constants::Colors::AGE_OLD[1],
                             Constants::Colors::AGE_OLD[2], Constants::Colors::AGE_OLD[3]);
            case DriverAge::VeryOld:
                return ImVec4(Constants::Colors::AGE_VERY_OLD[0], Constants::Colors::AGE_VERY_OLD[1],
                             Constants::Colors::AGE_VERY_OLD[2], Constants::Colors::AGE_VERY_OLD[3]);
            default:
                return ImVec4(0.5f, 0.5f, 0.5f, 1.0f);
        }
    }

    std::string FormatFileSize(uint64_t bytes) {
        const char* units[] = {"B", "KB", "MB", "GB"};
        int unitIndex = 0;
        double size = static_cast<double>(bytes);
        
        while (size >= 1024.0 && unitIndex < 3) {
            size /= 1024.0;
            unitIndex++;
        }
        
        char buf[32];
        if (unitIndex == 0) {
            snprintf(buf, sizeof(buf), "%llu %s", static_cast<unsigned long long>(bytes), units[unitIndex]);
        } else {
            snprintf(buf, sizeof(buf), "%.2f %s", size, units[unitIndex]);
        }
        return buf;
    }

    // ============================================================================
    // Menu Bar
    // ============================================================================

    void RenderMenuBar(AppState& state, bool isAdmin) {
        if (ImGui::BeginMainMenuBar()) {
            if (ImGui::BeginMenu(Constants::Text::MENU_FILE)) {
                if (ImGui::MenuItem("Scanner les pilotes", "F5", false, !state.isScanning.load())) {
                    state.isScanning = true;
                    state.scanProgress = 0.0f;
                    state.scanFuture = std::async(std::launch::async, [&state]() {
                        state.scanner.SetProgressCallback([&state](int current, int total, const std::wstring& item) {
                            if (total > 0) {
                                state.scanProgress = (float)current / (float)total;
                            }
                            state.SetCurrentScanItem(item);
                        });
                        state.scanner.ScanAllDrivers();
                        state.isScanning = false;
                        state.SetStatusMessage("Scan termin\xc3\xa9 - " + 
                            std::to_string(state.scanner.GetTotalDriverCount()) + " pilotes trouv\xc3\xa9s");
                    });
                }
                ImGui::Separator();
                if (ImGui::MenuItem("Exporter...", "Ctrl+E")) {
                    state.showExportDialog = true;
                }
                ImGui::Separator();
                if (ImGui::MenuItem("Quitter", "Alt+F4")) {
                    PostQuitMessage(0);
                }
                ImGui::EndMenu();
            }
            
            if (ImGui::BeginMenu(Constants::Text::MENU_VIEW)) {
                if (ImGui::MenuItem("D\xc3\xa9tails en fen\xc3\xaatre", nullptr, state.showDetailsWindow)) {
                    state.showDetailsWindow = !state.showDetailsWindow;
                }
                ImGui::EndMenu();
            }
            
            if (ImGui::BeginMenu(Constants::Text::MENU_TOOLS)) {
                if (ImGui::MenuItem("Nettoyer DriverStore...", nullptr, false, !state.isCleaningDriverStore.load())) {
                    state.showDriverStoreCleanup = true;
                    state.driverStoreCleanup.ScanDriverStore();
                }
                if (ImGui::IsItemHovered()) {
                    ImGui::SetTooltip("Supprimer les anciennes versions de pilotes");
                }
                if (ImGui::MenuItem("Analyser les BSOD...", nullptr, false, !state.isScanningBSOD.load())) {
                    state.showBSODAnalyzer = true;
                }
                ImGui::Separator();
                if (ImGui::MenuItem("T" "\xc3\xa9" "l" "\xc3\xa9" "chargements...", nullptr, state.showDownloadWindow)) {
                    state.showDownloadWindow = !state.showDownloadWindow;
                }
                ImGui::EndMenu();
            }
            
            if (ImGui::BeginMenu(Constants::Text::MENU_HELP)) {
                if (ImGui::MenuItem("Mise \xc3\xa0 jour des pilotes")) {
                    state.showUpdateHelpWindow = true;
                }
                ImGui::Separator();
                if (ImGui::MenuItem("\xc3\x80 propos")) {
                    state.showAboutWindow = true;
                }
                ImGui::EndMenu();
            }
            
            ImGui::EndMainMenuBar();
        }
    }

    // ============================================================================
    // Toolbar
    // ============================================================================

    void RenderToolbar(AppState& state, bool isAdmin) {
        using namespace Constants::UI;
        
        // Warning banner if not admin
        if (!isAdmin) {
            ImGui::PushStyleColor(ImGuiCol_ChildBg, ImVec4(Constants::Colors::WARNING_BANNER[0],
                Constants::Colors::WARNING_BANNER[1], Constants::Colors::WARNING_BANNER[2],
                Constants::Colors::WARNING_BANNER[3]));
            ImGui::BeginChild("AdminWarning", ImVec2(0, 28), false);
            ImGui::TextColored(ImVec4(Constants::Colors::WARNING_TEXT[0], Constants::Colors::WARNING_TEXT[1],
                Constants::Colors::WARNING_TEXT[2], Constants::Colors::WARNING_TEXT[3]), 
                "   Mode limit" "\xc3\xa9" " : Les boutons Activer/D" "\xc3\xa9" "sactiver n" "\xc3\xa9" "cessitent les droits administrateur");
            ImGui::EndChild();
            ImGui::PopStyleColor();
            ImGui::Spacing();
        }
        
        ImGui::PushStyleVar(ImGuiStyleVar_FramePadding, ImVec2(TOOLBAR_BUTTON_PADDING_X, TOOLBAR_BUTTON_PADDING_Y));
        
        // Scan button
        if (ImGui::Button(state.isScanning.load() ? Constants::Text::ACTION_STOP : Constants::Text::ACTION_SCAN)) {
            if (state.isScanning.load()) {
                state.scanner.CancelScan();
            } else {
                state.isScanning = true;
                state.scanProgress = 0.0f;
                state.scanFuture = std::async(std::launch::async, [&state]() {
                    state.scanner.SetProgressCallback([&state](int current, int total, const std::wstring& item) {
                        if (total > 0) {
                            state.scanProgress = (float)current / (float)total;
                        }
                        state.SetCurrentScanItem(item);
                    });
                    state.scanner.ScanAllDrivers();
                    state.isScanning = false;
                    state.SetStatusMessage(Constants::Text::MSG_SCAN_COMPLETE);
                });
            }
        }
        
        ImGui::SameLine();
        ImGui::BeginDisabled(state.selectedDriver == nullptr || state.isScanning.load());
        
        // Enable button
        if (ImGui::Button(Constants::Text::ACTION_ENABLE)) {
            if (state.selectedDriver) {
                auto result = state.scanner.EnableDriver(*state.selectedDriver);
                if (result.IsSuccess()) {
                    state.SetStatusMessage("Pilote activ\xc3\xa9 avec succ\xc3\xa8s");
                } else {
                    state.SetStatusMessage("Erreur: " + WideToUtf8(result.ErrorMessage()));
                }
            }
        }
        
        ImGui::SameLine();
        
        // Disable button
        if (ImGui::Button(Constants::Text::ACTION_DISABLE)) {
            if (state.selectedDriver) {
                auto result = state.scanner.DisableDriver(*state.selectedDriver);
                if (result.IsSuccess()) {
                    state.SetStatusMessage("Pilote d\xc3\xa9sactiv\xc3\xa9 avec succ\xc3\xa8s");
                } else {
                    state.SetStatusMessage("Erreur: " + WideToUtf8(result.ErrorMessage()));
                }
            }
        }
        
        ImGui::SameLine();
        
        // Uninstall button
        if (ImGui::Button(Constants::Text::ACTION_UNINSTALL)) {
            if (state.selectedDriver) {
                ImGui::OpenPopup("Confirmer d\xc3\xa9sinstallation");
            }
        }
        
        ImGui::EndDisabled();
        
        // Check for updates button
        ImGui::SameLine();
        ImGui::BeginDisabled(state.isCheckingUpdates.load() || state.isScanning.load() || 
                            state.scanner.GetTotalDriverCount() == 0);
        
        ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(Constants::Colors::BUTTON_UPDATE[0],
            Constants::Colors::BUTTON_UPDATE[1], Constants::Colors::BUTTON_UPDATE[2],
            Constants::Colors::BUTTON_UPDATE[3]));
        ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(Constants::Colors::BUTTON_UPDATE_HOVER[0],
            Constants::Colors::BUTTON_UPDATE_HOVER[1], Constants::Colors::BUTTON_UPDATE_HOVER[2],
            Constants::Colors::BUTTON_UPDATE_HOVER[3]));
        ImGui::PushStyleColor(ImGuiCol_ButtonActive, ImVec4(0.75f, 0.55f, 0.25f, 1.0f));
        
        if (ImGui::Button(state.isCheckingUpdates.load() ? "V\xc3\xa9rification..." : Constants::Text::ACTION_CHECK_UPDATES)) {
            state.isCheckingUpdates = true;
            state.showUpdateProgressWindow = true;
            state.updatesFound = 0;
            state.updateCheckProgress = 0.0f;
            state.updateSource = 2;
            state.cancelUpdateCheck = false;
            state.SetCurrentUpdateItem(L"Initialisation...");
            
            state.updateCheckFuture = std::async(std::launch::async, [&state]() {
                auto drivers = state.scanner.GetAllDrivers();
                state.totalDriversToCheck = static_cast<int>(drivers.size());
                state.driversChecked = 0;
                
                state.updateChecker.SetProgressCallback([&state](int current, int total, const std::wstring& device) {
                    state.updateCheckProgress = total > 0 ? (float)current / (float)total : 0;
                    state.SetCurrentUpdateItem(device);
                    state.driversChecked = current;
                });
                
                state.updateChecker.CheckWindowsUpdate(drivers);
                state.updatesFound = state.updateChecker.GetLastCheckUpdatesFound();
                state.isCheckingUpdates = false;
                
                if (state.updatesFound.load() > 0) {
                    state.SetStatusMessage(std::to_string(state.updatesFound.load()) + " mise(s) \xc3\xa0 jour disponible(s)");
                } else {
                    state.SetStatusMessage(Constants::Text::MSG_NO_UPDATES);
                }
            });
        }
        
        ImGui::PopStyleColor(3);
        ImGui::EndDisabled();
        
        // Separator before filters
        ImGui::SameLine();
        ImGui::SeparatorEx(ImGuiSeparatorFlags_Vertical);
        ImGui::SameLine();
        
        // Filter checkbox
        ImGui::Checkbox(Constants::Text::FILTER_OLD_DRIVERS, &state.filterOldDrivers);
        
        // Confirm uninstall popup
        if (ImGui::BeginPopupModal("Confirmer d\xc3\xa9sinstallation", nullptr, ImGuiWindowFlags_AlwaysAutoResize)) {
            ImGui::Text("Voulez-vous vraiment d\xc3\xa9sinstaller ce pilote ?");
            ImGui::Text("Cette action peut rendre certains p\xc3\xa9riph\xc3\xa9riques inutilisables.");
            ImGui::Separator();
            
            if (ImGui::Button("Oui, d\xc3\xa9sinstaller", ImVec2(150, 0))) {
                if (state.selectedDriver) {
                    auto result = state.scanner.UninstallDriver(*state.selectedDriver);
                    if (result.IsSuccess()) {
                        state.SetStatusMessage("Pilote d\xc3\xa9sinstall\xc3\xa9");
                    } else {
                        state.SetStatusMessage("Erreur: " + WideToUtf8(result.ErrorMessage()));
                    }
                }
                ImGui::CloseCurrentPopup();
            }
            ImGui::SameLine();
            if (ImGui::Button(Constants::Text::ACTION_CANCEL, ImVec2(100, 0))) {
                ImGui::CloseCurrentPopup();
            }
            ImGui::EndPopup();
        }
        
        // Search filter
        ImGui::SameLine();
        ImGui::SetNextItemWidth(SEARCH_FIELD_WIDTH);
        
        std::string searchFilter = state.GetSearchFilter();
        char searchBuf[256];
        strncpy_s(searchBuf, searchFilter.c_str(), sizeof(searchBuf) - 1);
        
        if (ImGui::InputTextWithHint("##search", Constants::Text::FILTER_SEARCH_HINT, searchBuf, sizeof(searchBuf))) {
            state.SetSearchFilter(searchBuf);
        }
        
        ImGui::PopStyleVar();
    }

    // ============================================================================
    // Status Bar
    // ============================================================================

    void RenderStatusBar(AppState& state) {
        ImGuiViewport* viewport = ImGui::GetMainViewport();
        ImGui::SetNextWindowPos(ImVec2(viewport->Pos.x, viewport->Pos.y + viewport->Size.y - Constants::UI::STATUS_BAR_HEIGHT));
        ImGui::SetNextWindowSize(ImVec2(viewport->Size.x, Constants::UI::STATUS_BAR_HEIGHT));
        
        ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding, ImVec2(10, 5));
        ImGui::Begin("StatusBar", nullptr, 
            ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoResize | 
            ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoScrollbar |
            ImGuiWindowFlags_NoSavedSettings);
        
        if (state.isScanning.load()) {
            ImGui::Text("Scan en cours...");
            ImGui::SameLine();
            ImGui::ProgressBar(state.scanProgress.load(), ImVec2(Constants::UI::PROGRESS_BAR_WIDTH, 0));
            ImGui::SameLine();
            ImGui::Text("%s", WideToUtf8(state.GetCurrentScanItem()).c_str());
        } else {
            ImGui::Text("%s", state.GetStatusMessage().c_str());
            ImGui::SameLine(ImGui::GetWindowWidth() - 200);
            ImGui::Text("Total: %zu pilotes", state.scanner.GetTotalDriverCount());
        }
        
        ImGui::End();
        ImGui::PopStyleVar();
    }

    // ============================================================================
    // About Window
    // ============================================================================

    void RenderAboutWindow(AppState& state) {
        if (!state.showAboutWindow) return;
        
        ImGui::SetNextWindowSize(ImVec2(Constants::UI::ABOUT_WINDOW_WIDTH, 
                                        Constants::UI::ABOUT_WINDOW_HEIGHT), ImGuiCond_FirstUseEver);
        if (ImGui::Begin("\xc3\x80 propos", &state.showAboutWindow, ImGuiWindowFlags_NoResize)) {
            ImGui::Text("Driver Manager");
            ImGui::Text("Version 1.0.0");
            ImGui::Separator();
            ImGui::Text("Gestionnaire de pilotes Windows");
            ImGui::Text("Utilise Dear ImGui pour l'interface graphique");
            ImGui::Separator();
            ImGui::Text("D\xc3\xa9velopp\xc3\xa9 avec C++20 et DirectX 11");
        }
        ImGui::End();
    }

    // ============================================================================
    // Update Progress Window
    // ============================================================================

    void RenderUpdateProgressWindow(AppState& state) {
        if (!state.showUpdateProgressWindow) return;
        
        bool windowOpen = state.showUpdateProgressWindow;
        
        ImGui::SetNextWindowSize(ImVec2(Constants::UI::UPDATE_PROGRESS_WIDTH, 
                                        Constants::UI::UPDATE_PROGRESS_HEIGHT), ImGuiCond_FirstUseEver);
        ImGuiWindowFlags flags = ImGuiWindowFlags_NoResize | ImGuiWindowFlags_NoCollapse;
        
        std::string windowTitle = "V\xc3\xa9rification des mises \xc3\xa0 jour";
        if (state.updateSource.load() == 2) {
            windowTitle += " - Windows Update Catalog";
        }
        
        if (ImGui::Begin(windowTitle.c_str(), &windowOpen, flags)) {
            if (state.updateSource.load() == 2) {
                ImGui::TextColored(ImVec4(0.4f, 0.7f, 1.0f, 1.0f), "Source: Windows Update Catalog");
            }
            
            ImGui::Separator();
            ImGui::Spacing();
            
            if (state.isCheckingUpdates.load()) {
                char progressText[64];
                snprintf(progressText, sizeof(progressText), "%d / %d pilotes (%.0f%%)", 
                    state.driversChecked.load(), state.totalDriversToCheck.load(), 
                    state.updateCheckProgress.load() * 100.0f);
                ImGui::ProgressBar(state.updateCheckProgress.load(), ImVec2(-1, 0), progressText);
                
                ImGui::Spacing();
                ImGui::TextColored(ImVec4(0.7f, 0.7f, 0.7f, 1.0f), "V\xc3\xa9rification en cours:");
                ImGui::TextWrapped("%s", WideToUtf8(state.GetCurrentUpdateItem()).c_str());
                
                ImGui::Spacing();
                ImGui::Separator();
                ImGui::Spacing();
                
                if (ImGui::Button(Constants::Text::ACTION_CANCEL, ImVec2(120, 0))) {
                    state.cancelUpdateCheck = true;
                    state.updateChecker.CancelCheck();
                    state.isCheckingUpdates = false;
                    state.SetStatusMessage("V" "\xc3\xa9" "rification annul" "\xc3\xa9" "e");
                }
            } else {
                ImGui::TextColored(ImVec4(0.4f, 0.9f, 0.4f, 1.0f), "V" "\xc3\xa9" "rification termin" "\xc3\xa9" "e!");
                ImGui::Spacing();
                
                if (state.updatesFound.load() > 0) {
                    ImGui::TextColored(ImVec4(0.9f, 0.8f, 0.2f, 1.0f), 
                        "%d mise(s) " "\xc3\xa0" " jour trouv" "\xc3\xa9" "e(s)", state.updatesFound.load());
                } else {
                    ImGui::Text("Tous les pilotes sont \xc3\xa0 jour.");
                }
                
                ImGui::Spacing();
                ImGui::Separator();
                ImGui::Spacing();
                
                if (ImGui::Button(Constants::Text::ACTION_CLOSE, ImVec2(120, 0))) {
                    state.showUpdateProgressWindow = false;
                }
            }
        }
        ImGui::End();
        
        // Handle window close via X button
        if (!windowOpen && state.showUpdateProgressWindow) {
            state.showUpdateProgressWindow = false;
            if (state.isCheckingUpdates.load()) {
                state.cancelUpdateCheck = true;
                state.updateChecker.CancelCheck();
                state.isCheckingUpdates = false;
                state.SetStatusMessage("V" "\xc3\xa9" "rification annul" "\xc3\xa9" "e");
            }
        }
    }

    // ============================================================================
    // Update Help Window
    // ============================================================================

    void RenderUpdateHelpWindow(AppState& state) {
        if (!state.showUpdateHelpWindow) return;
        
        ImGui::SetNextWindowSize(ImVec2(Constants::UI::UPDATE_HELP_WIDTH, 
                                        Constants::UI::UPDATE_HELP_HEIGHT), ImGuiCond_FirstUseEver);
        if (ImGui::Begin("Mise \xc3\xa0 jour des pilotes", &state.showUpdateHelpWindow)) {
            
            ImGui::TextColored(ImVec4(0.4f, 0.8f, 0.4f, 1.0f), "TousLesDrivers.com - Mes Drivers");
            ImGui::Separator();
            ImGui::Spacing();
            
            ImGui::TextWrapped(
                "Pour mettre \xc3\xa0 jour vos pilotes, nous vous recommandons d'utiliser "
                "l'outil 'Mes Drivers' de TousLesDrivers.com, un service gratuit et fiable.");
            
            ImGui::Spacing();
            ImGui::Separator();
            ImGui::Spacing();
            
            ImGui::TextColored(ImVec4(0.9f, 0.8f, 0.3f, 1.0f), "Comment fonctionne 'Mes Drivers' :");
            ImGui::Spacing();
            
            ImGui::TextWrapped("1. Cliquez sur le bouton ci-dessous pour ouvrir la page Mes Drivers");
            ImGui::Spacing();
            ImGui::TextWrapped("2. T" "\xc3\xa9" "l" "\xc3\xa9" "chargez et ex" "\xc3\xa9" "cutez l'outil de d" "\xc3\xa9" "tection");
            ImGui::Spacing();
            ImGui::TextWrapped("3. L'outil analyse automatiquement votre PC");
            ImGui::Spacing();
            ImGui::TextWrapped("4. Une page web s'ouvre avec les mises \xc3\xa0 jour disponibles");
            ImGui::Spacing();
            ImGui::TextWrapped("5. T" "\xc3\xa9" "l" "\xc3\xa9" "chargez les pilotes n" "\xc3\xa9" "cessaires");
            
            ImGui::Spacing();
            ImGui::Separator();
            ImGui::Spacing();
            
            // Main button
            ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(0.15f, 0.55f, 0.20f, 0.80f));
            ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(0.20f, 0.65f, 0.25f, 0.90f));
            ImGui::PushStyleColor(ImGuiCol_ButtonActive, ImVec4(0.25f, 0.75f, 0.30f, 1.00f));
            if (ImGui::Button("Ouvrir Mes Drivers", ImVec2(200, 35))) {
                ShellExecuteW(nullptr, L"open", L"https://www.touslesdrivers.com/index.php?v_page=29", 
                             nullptr, nullptr, SW_SHOWNORMAL);
            }
            ImGui::PopStyleColor(3);
            
            ImGui::SameLine();
            if (ImGui::Button("TousLesDrivers.com", ImVec2(150, 35))) {
                ShellExecuteW(nullptr, L"open", L"https://www.touslesdrivers.com", 
                             nullptr, nullptr, SW_SHOWNORMAL);
            }
            
            ImGui::SameLine();
            if (ImGui::Button(Constants::Text::ACTION_CLOSE, ImVec2(80, 35))) {
                state.showUpdateHelpWindow = false;
            }
        }
        ImGui::End();
    }

}} // namespace DriverManager::UI
