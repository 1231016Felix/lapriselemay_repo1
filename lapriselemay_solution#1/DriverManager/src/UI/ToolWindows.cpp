#include "UIWidgets.h"
#include <shellapi.h>

namespace DriverManager {
namespace UI {

    void RenderDriverStoreCleanupWindow(AppState& state) {
        if (!state.showDriverStoreCleanup) return;
        
        ImGui::SetNextWindowSize(ImVec2(Constants::UI::DRIVER_STORE_WIDTH, 
                                        Constants::UI::DRIVER_STORE_HEIGHT), ImGuiCond_FirstUseEver);
        
        if (ImGui::Begin("Nettoyage du DriverStore", &state.showDriverStoreCleanup)) {
            
            ImGui::TextColored(ImVec4(0.4f, 0.7f, 1.0f, 1.0f), 
                "Anciennes versions de pilotes dans le DriverStore");
            ImGui::Separator();
            ImGui::Spacing();
            
            // Refresh button
            if (ImGui::Button("Actualiser")) {
                state.driverStoreCleanup.ScanDriverStore();
            }
            
            ImGui::SameLine();
            
            // Delete selected button
            ImGui::BeginDisabled(state.isCleaningDriverStore.load());
            ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(Constants::Colors::BUTTON_DELETE[0],
                Constants::Colors::BUTTON_DELETE[1], Constants::Colors::BUTTON_DELETE[2],
                Constants::Colors::BUTTON_DELETE[3]));
            ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(Constants::Colors::BUTTON_DELETE_HOVER[0],
                Constants::Colors::BUTTON_DELETE_HOVER[1], Constants::Colors::BUTTON_DELETE_HOVER[2],
                Constants::Colors::BUTTON_DELETE_HOVER[3]));
            
            if (ImGui::Button("Supprimer la s\xc3\xa9lection")) {
                state.isCleaningDriverStore = true;
                state.lastDeletedCount = 0;
                
                std::thread([&state]() {
                    int deleted = state.driverStoreCleanup.DeleteSelectedPackages();
                    state.lastDeletedCount = deleted;
                    state.isCleaningDriverStore = false;
                    state.needsDriverStoreRefresh = true;
                    state.SetStatusMessage(std::to_string(deleted) + " pilote(s) supprim" "\xc3\xa9" "(s)");
                }).detach();
            }
            
            ImGui::PopStyleColor(2);
            ImGui::EndDisabled();
            
            ImGui::SameLine();
            
            // Select all button
            auto& entries = state.driverStoreCleanup.GetEntries();
            if (ImGui::Button("Tout s\xc3\xa9lectionner")) {
                for (auto& entry : entries) {
                    if (!entry.isCurrentVersion) {
                        entry.isSelected = true;
                    }
                }
            }
            
            ImGui::SameLine();
            
            // Deselect all button
            if (ImGui::Button("Tout d\xc3\xa9s\xc3\xa9lectionner")) {
                for (auto& entry : entries) {
                    entry.isSelected = false;
                }
            }
            
            ImGui::Spacing();
            
            // Progress indicator
            if (state.isCleaningDriverStore.load()) {
                ImGui::TextColored(ImVec4(0.9f, 0.7f, 0.2f, 1.0f), "Suppression en cours...");
                ImGui::SameLine();
                ImGui::Text("Veuillez patienter");
            }
            
            // Refresh if needed
            if (state.needsDriverStoreRefresh.load()) {
                state.driverStoreCleanup.ScanDriverStore();
                state.needsDriverStoreRefresh = false;
            }
            
            ImGui::Separator();
            ImGui::Spacing();
            
            // Driver store entries table
            if (ImGui::BeginTable("DriverStoreEntries", 5, 
                ImGuiTableFlags_Borders | ImGuiTableFlags_RowBg | 
                ImGuiTableFlags_Resizable | ImGuiTableFlags_ScrollY,
                ImVec2(0, -50))) {
                
                ImGui::TableSetupColumn("", ImGuiTableColumnFlags_WidthFixed, 30.0f);
                ImGui::TableSetupColumn("Nom du pilote", ImGuiTableColumnFlags_None, 250.0f);
                ImGui::TableSetupColumn("Version", ImGuiTableColumnFlags_None, 120.0f);
                ImGui::TableSetupColumn("Date", ImGuiTableColumnFlags_None, 100.0f);
                ImGui::TableSetupColumn("Taille", ImGuiTableColumnFlags_None, 80.0f);
                ImGui::TableSetupScrollFreeze(0, 1);
                ImGui::TableHeadersRow();
                
                for (size_t i = 0; i < entries.size(); i++) {
                    auto& entry = entries[i];
                    
                    // Only show old versions (orphaned)
                    if (entry.isCurrentVersion) continue;
                    
                    ImGui::TableNextRow();
                    
                    // Checkbox
                    ImGui::TableNextColumn();
                    ImGui::PushID(static_cast<int>(i));
                    ImGui::Checkbox("##sel", &entry.isSelected);
                    ImGui::PopID();
                    
                    // Name
                    ImGui::TableNextColumn();
                    ImGui::TextUnformatted(WideToUtf8(entry.infName).c_str());
                    
                    // Version
                    ImGui::TableNextColumn();
                    ImGui::TextUnformatted(WideToUtf8(entry.driverVersion).c_str());
                    
                    // Date
                    ImGui::TableNextColumn();
                    ImGui::TextUnformatted(WideToUtf8(entry.driverDate).c_str());
                    
                    // Size
                    ImGui::TableNextColumn();
                    ImGui::Text("%s", FormatFileSize(entry.folderSize).c_str());
                }
                
                ImGui::EndTable();
            }
            
            // Summary
            ImGui::Spacing();
            size_t oldCount = 0;
            uint64_t totalSize = 0;
            for (const auto& e : entries) {
                if (!e.isCurrentVersion) {
                    oldCount++;
                    totalSize += e.folderSize;
                }
            }
            
            ImGui::Text("%zu ancienne(s) version(s) trouv" "\xc3\xa9" "e(s), %s r" "\xc3\xa9" "cup" "\xc3\xa9" "rable(s)",
                oldCount, FormatFileSize(totalSize).c_str());
        }
        ImGui::End();
    }

    void RenderBSODAnalyzerWindow(AppState& state) {
        if (!state.showBSODAnalyzer) return;
        
        ImGui::SetNextWindowSize(ImVec2(Constants::UI::BSOD_ANALYZER_WIDTH, 
                                        Constants::UI::BSOD_ANALYZER_HEIGHT), ImGuiCond_FirstUseEver);
        
        if (ImGui::Begin("Analyseur BSOD", &state.showBSODAnalyzer)) {
            
            ImGui::TextColored(ImVec4(0.4f, 0.7f, 1.0f, 1.0f), 
                "Analyse des fichiers minidump Windows");
            ImGui::Separator();
            ImGui::Spacing();
            
            // Scan button
            ImGui::BeginDisabled(state.isScanningBSOD.load());
            if (ImGui::Button("Analyser les crashs")) {
                state.isScanningBSOD = true;
                state.bsodScanProgress = 0;
                state.bsodScanTotal = 0;
                state.SetBsodCurrentItem(L"Initialisation...");
                
                state.bsodScanFuture = std::async(std::launch::async, [&state]() {
                    state.bsodAnalyzer.SetProgressCallback([&state](int current, int total, const std::wstring& item) {
                        state.bsodScanProgress = current;
                        state.bsodScanTotal = total;
                        state.SetBsodCurrentItem(item);
                    });
                    state.bsodAnalyzer.ScanMinidumps();
                    state.isScanningBSOD = false;
                    state.SetStatusMessage("Analyse BSOD termin\xc3\xa9" "e");
                });
            }
            ImGui::EndDisabled();
            
            ImGui::SameLine();
            
            if (ImGui::Button("Ouvrir le dossier Minidump")) {
                ShellExecuteW(nullptr, L"explore", L"C:\\Windows\\Minidump", nullptr, nullptr, SW_SHOWNORMAL);
            }
            
            ImGui::Spacing();
            
            // Progress
            if (state.isScanningBSOD.load()) {
                if (state.bsodScanTotal.load() > 0) {
                    float progress = (float)state.bsodScanProgress.load() / (float)state.bsodScanTotal.load();
                    char buf[64];
                    snprintf(buf, sizeof(buf), "%d / %d", state.bsodScanProgress.load(), state.bsodScanTotal.load());
                    ImGui::ProgressBar(progress, ImVec2(-1, 0), buf);
                } else {
                    ImGui::ProgressBar(-1.0f * (float)ImGui::GetTime(), ImVec2(-1, 0), "Recherche des fichiers...");
                }
                ImGui::Text("Analyse: %s", WideToUtf8(state.GetBsodCurrentItem()).c_str());
            }
            
            ImGui::Separator();
            ImGui::Spacing();
            
            // Results table
            const auto& crashes = state.bsodAnalyzer.GetCrashes();
            
            if (crashes.empty() && !state.isScanningBSOD.load()) {
                ImGui::TextColored(ImVec4(0.4f, 0.8f, 0.4f, 1.0f), 
                    "Aucun crash trouv\xc3\xa9 ou analyse non effectu\xc3\xa9" "e.");
                ImGui::Text("Cliquez sur 'Analyser les crashs' pour scanner les minidumps.");
            } else if (!crashes.empty()) {
                if (ImGui::BeginTable("BSODCrashes", 4, 
                    ImGuiTableFlags_Borders | ImGuiTableFlags_RowBg | 
                    ImGuiTableFlags_Resizable | ImGuiTableFlags_ScrollY)) {
                    
                    ImGui::TableSetupColumn("Date", ImGuiTableColumnFlags_None, 150.0f);
                    ImGui::TableSetupColumn("Code d'arr\xc3\xaat", ImGuiTableColumnFlags_None, 150.0f);
                    ImGui::TableSetupColumn("Module fautif", ImGuiTableColumnFlags_None, 200.0f);
                    ImGui::TableSetupColumn("Fichier", ImGuiTableColumnFlags_None, 200.0f);
                    ImGui::TableSetupScrollFreeze(0, 1);
                    ImGui::TableHeadersRow();
                    
                    for (const auto& crash : crashes) {
                        ImGui::TableNextRow();
                        
                        ImGui::TableNextColumn();
                        // Format crash time
                        char timeBuf[64];
                        snprintf(timeBuf, sizeof(timeBuf), "%04d-%02d-%02d %02d:%02d:%02d",
                            crash.crashTime.wYear, crash.crashTime.wMonth, crash.crashTime.wDay,
                            crash.crashTime.wHour, crash.crashTime.wMinute, crash.crashTime.wSecond);
                        ImGui::TextUnformatted(timeBuf);
                        
                        ImGui::TableNextColumn();
                        ImGui::TextColored(ImVec4(0.9f, 0.4f, 0.4f, 1.0f), "%s", 
                            WideToUtf8(crash.bugCheckName).c_str());
                        
                        ImGui::TableNextColumn();
                        if (!crash.faultingModule.empty()) {
                            ImGui::TextColored(ImVec4(0.9f, 0.7f, 0.3f, 1.0f), "%s", 
                                WideToUtf8(crash.faultingModule).c_str());
                        } else {
                            ImGui::TextDisabled("Inconnu");
                        }
                        
                        ImGui::TableNextColumn();
                        ImGui::TextUnformatted(WideToUtf8(crash.dumpFileName).c_str());
                    }
                    
                    ImGui::EndTable();
                }
                
                ImGui::Spacing();
                ImGui::Text("%zu crash(s) trouv\xc3\xa9(s)", crashes.size());
            }
        }
        ImGui::End();
    }

    void RenderDownloadWindow(AppState& state) {
        if (!state.showDownloadWindow) return;
        
        ImGui::SetNextWindowSize(ImVec2(Constants::UI::DOWNLOAD_WINDOW_WIDTH, 
                                        Constants::UI::DOWNLOAD_WINDOW_HEIGHT), ImGuiCond_FirstUseEver);
        
        if (ImGui::Begin("T\xc3\xa9l\xc3\xa9" "chargements", &state.showDownloadWindow)) {
            
            ImGui::TextColored(ImVec4(0.4f, 0.7f, 1.0f, 1.0f), "File d'attente des t\xc3\xa9l\xc3\xa9" "chargements");
            ImGui::Separator();
            ImGui::Spacing();
            
            // Controls
            if (ImGui::Button("Ouvrir le dossier")) {
                std::wstring downloadPath = state.driverDownloader.GetDownloadDirectory();
                if (!downloadPath.empty()) {
                    ShellExecuteW(nullptr, L"explore", downloadPath.c_str(), nullptr, nullptr, SW_SHOWNORMAL);
                }
            }
            
            ImGui::SameLine();
            
            if (ImGui::Button("Effacer termin\xc3\xa9s")) {
                state.driverDownloader.ClearCompleted();
            }
            
            ImGui::Spacing();
            ImGui::Separator();
            ImGui::Spacing();
            
            // Downloads table
            const auto tasks = state.driverDownloader.GetAllTasks();
            
            if (tasks.empty()) {
                ImGui::TextDisabled("Aucun t\xc3\xa9l\xc3\xa9" "chargement en cours");
            } else {
                if (ImGui::BeginTable("Downloads", 4, 
                    ImGuiTableFlags_Borders | ImGuiTableFlags_RowBg | 
                    ImGuiTableFlags_Resizable | ImGuiTableFlags_ScrollY)) {
                    
                    ImGui::TableSetupColumn("Pilote", ImGuiTableColumnFlags_None, 250.0f);
                    ImGui::TableSetupColumn("Status", ImGuiTableColumnFlags_None, 120.0f);
                    ImGui::TableSetupColumn("Progression", ImGuiTableColumnFlags_None, 150.0f);
                    ImGui::TableSetupColumn("Actions", ImGuiTableColumnFlags_WidthFixed, 80.0f);
                    ImGui::TableSetupScrollFreeze(0, 1);
                    ImGui::TableHeadersRow();
                    
                    int rowIndex = 0;
                    for (const auto& task : tasks) {
                        ImGui::TableNextRow();
                        ImGui::PushID(rowIndex++);
                        
                        // Driver name
                        ImGui::TableNextColumn();
                        ImGui::TextUnformatted(WideToUtf8(task.deviceName).c_str());
                        
                        // Status
                        ImGui::TableNextColumn();
                        ImGui::Text("%s", WideToUtf8(GetStateText(task.state)).c_str());
                        
                        // Progress
                        ImGui::TableNextColumn();
                        if (task.state == DownloadState::Downloading) {
                            char buf[64];
                            snprintf(buf, sizeof(buf), "%.1f%%", task.progress * 100.0f);
                            ImGui::ProgressBar(task.progress, ImVec2(-1, 0), buf);
                        } else if (task.state == DownloadState::Completed) {
                            ImGui::Text("%s", FormatFileSize(task.downloadedBytes).c_str());
                        } else {
                            ImGui::TextDisabled("-");
                        }
                        
                        // Actions
                        ImGui::TableNextColumn();
                        if (task.state == DownloadState::Downloading || 
                            task.state == DownloadState::Queued) {
                            if (ImGui::SmallButton("Annuler")) {
                                state.driverDownloader.CancelTask(task.taskId);
                            }
                        } else if (task.state == DownloadState::Completed || 
                                   task.state == DownloadState::ReadyToInstall) {
                            if (ImGui::SmallButton("Ouvrir")) {
                                ShellExecuteW(nullptr, L"open", task.extractPath.c_str(), 
                                             nullptr, nullptr, SW_SHOWNORMAL);
                            }
                        } else if (task.state == DownloadState::Failed) {
                            if (ImGui::SmallButton("R\xc3\xa9" "essayer")) {
                                state.driverDownloader.RetryTask(task.taskId);
                            }
                        }
                        
                        ImGui::PopID();
                    }
                    
                    ImGui::EndTable();
                }
            }
            
            // Stats
            ImGui::Spacing();
            ImGui::Separator();
            ImGui::Text("T\xc3\xa9l\xc3\xa9" "chargements: %zu en file", tasks.size());
        }
        ImGui::End();
    }

}} // namespace DriverManager::UI
