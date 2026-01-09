// DriverManager - Windows Driver Management Tool
// Using Dear ImGui with DirectX 11

#include "imgui/imgui.h"
#include "imgui/imgui_impl_win32.h"
#include "imgui/imgui_impl_dx11.h"
#include <d3d11.h>
#include <tchar.h>
#include <shellapi.h>
#include <thread>
#include <future>
#include <algorithm>
#include <sstream>
#include <iomanip>

#include "src/DriverScanner.h"
#include "src/DriverInfo.h"

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")

// DirectX globals
static ID3D11Device*            g_pd3dDevice = nullptr;
static ID3D11DeviceContext*     g_pd3dDeviceContext = nullptr;
static IDXGISwapChain*          g_pSwapChain = nullptr;
static ID3D11RenderTargetView*  g_mainRenderTargetView = nullptr;
static UINT                     g_ResizeWidth = 0, g_ResizeHeight = 0;

// Forward declarations
bool CreateDeviceD3D(HWND hWnd);
void CleanupDeviceD3D();
void CreateRenderTarget();
void CleanupRenderTarget();
LRESULT WINAPI WndProc(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);
extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);

// Application state
struct AppState {
    DriverManager::DriverScanner scanner;
    bool isScanning = false;
    bool showDetailsWindow = false;
    bool showAboutWindow = false;
    bool showExportDialog = false;
    bool showUpdateHelpWindow = false;
    DriverManager::DriverInfo* selectedDriver = nullptr;
    std::string statusMessage;
    std::string searchFilter;
    int selectedCategory = -1; // -1 = all
    std::future<void> scanFuture;
    float scanProgress = 0.0f;
    std::wstring currentScanItem;
    
    // Sorting state - persisted across frames
    int sortColumnIndex = 0;          // Default sort by Name
    bool sortAscending = true;        // Default ascending
    bool sortSpecsInitialized = false;
};

// Custom ImGui style
void SetupImGuiStyle() {
    ImGuiStyle& style = ImGui::GetStyle();
    
    style.WindowRounding = 8.0f;
    style.FrameRounding = 4.0f;
    style.GrabRounding = 4.0f;
    style.PopupRounding = 4.0f;
    style.ScrollbarRounding = 4.0f;
    style.TabRounding = 4.0f;
    
    style.WindowPadding = ImVec2(12, 12);
    style.FramePadding = ImVec2(8, 4);
    style.ItemSpacing = ImVec2(8, 6);
    style.ItemInnerSpacing = ImVec2(6, 4);
    
    ImVec4* colors = style.Colors;
    
    // Dark theme with blue accent
    colors[ImGuiCol_WindowBg] = ImVec4(0.10f, 0.10f, 0.12f, 1.00f);
    colors[ImGuiCol_ChildBg] = ImVec4(0.12f, 0.12f, 0.14f, 1.00f);
    colors[ImGuiCol_PopupBg] = ImVec4(0.12f, 0.12f, 0.14f, 0.95f);
    colors[ImGuiCol_Border] = ImVec4(0.25f, 0.25f, 0.28f, 1.00f);
    colors[ImGuiCol_FrameBg] = ImVec4(0.15f, 0.15f, 0.18f, 1.00f);
    colors[ImGuiCol_FrameBgHovered] = ImVec4(0.20f, 0.20f, 0.25f, 1.00f);
    colors[ImGuiCol_FrameBgActive] = ImVec4(0.25f, 0.25f, 0.30f, 1.00f);
    colors[ImGuiCol_TitleBg] = ImVec4(0.08f, 0.08f, 0.10f, 1.00f);
    colors[ImGuiCol_TitleBgActive] = ImVec4(0.12f, 0.12f, 0.15f, 1.00f);
    colors[ImGuiCol_MenuBarBg] = ImVec4(0.12f, 0.12f, 0.14f, 1.00f);
    colors[ImGuiCol_Header] = ImVec4(0.20f, 0.40f, 0.70f, 0.50f);
    colors[ImGuiCol_HeaderHovered] = ImVec4(0.25f, 0.50f, 0.80f, 0.70f);
    colors[ImGuiCol_HeaderActive] = ImVec4(0.30f, 0.55f, 0.85f, 0.90f);
    colors[ImGuiCol_Button] = ImVec4(0.20f, 0.40f, 0.70f, 0.60f);
    colors[ImGuiCol_ButtonHovered] = ImVec4(0.25f, 0.50f, 0.80f, 0.80f);
    colors[ImGuiCol_ButtonActive] = ImVec4(0.30f, 0.55f, 0.85f, 1.00f);
    colors[ImGuiCol_Tab] = ImVec4(0.15f, 0.15f, 0.18f, 1.00f);
    colors[ImGuiCol_TabHovered] = ImVec4(0.25f, 0.50f, 0.80f, 0.80f);
    colors[ImGuiCol_TabActive] = ImVec4(0.20f, 0.40f, 0.70f, 1.00f);
    colors[ImGuiCol_ScrollbarBg] = ImVec4(0.10f, 0.10f, 0.12f, 1.00f);
    colors[ImGuiCol_ScrollbarGrab] = ImVec4(0.25f, 0.25f, 0.30f, 1.00f);
    colors[ImGuiCol_ScrollbarGrabHovered] = ImVec4(0.30f, 0.30f, 0.35f, 1.00f);
    colors[ImGuiCol_ScrollbarGrabActive] = ImVec4(0.35f, 0.35f, 0.40f, 1.00f);
    colors[ImGuiCol_TableHeaderBg] = ImVec4(0.15f, 0.15f, 0.18f, 1.00f);
    colors[ImGuiCol_TableRowBg] = ImVec4(0.12f, 0.12f, 0.14f, 1.00f);
    colors[ImGuiCol_TableRowBgAlt] = ImVec4(0.14f, 0.14f, 0.16f, 1.00f);
}

// Render the main menu bar
void RenderMenuBar(AppState& state) {
    if (ImGui::BeginMainMenuBar()) {
        if (ImGui::BeginMenu("Fichier")) {
            if (ImGui::MenuItem("Scanner les pilotes", "F5", false, !state.isScanning)) {
                state.isScanning = true;
                state.scanFuture = std::async(std::launch::async, [&state]() {
                    state.scanner.SetProgressCallback([&state](int current, int total, const std::wstring& item) {
                        if (total > 0) {
                            state.scanProgress = (float)current / (float)total;
                        }
                        state.currentScanItem = item;
                    });
                    state.scanner.ScanAllDrivers();
                    state.isScanning = false;
                    state.statusMessage = "Scan terminé - " + std::to_string(state.scanner.GetTotalDriverCount()) + " pilotes trouvés";
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
        
        if (ImGui::BeginMenu("Affichage")) {
            if (ImGui::MenuItem("Détails en fenêtre", nullptr, state.showDetailsWindow)) {
                state.showDetailsWindow = !state.showDetailsWindow;
            }
            if (ImGui::IsItemHovered()) {
                ImGui::SetTooltip("Afficher les détails dans une fenêtre séparée");
            }
            ImGui::EndMenu();
        }
        
        if (ImGui::BeginMenu("Aide")) {
            if (ImGui::MenuItem("Mise à jour des pilotes")) {
                state.showUpdateHelpWindow = true;
            }
            ImGui::Separator();
            if (ImGui::MenuItem("À propos")) {
                state.showAboutWindow = true;
            }
            ImGui::EndMenu();
        }
        
        ImGui::EndMainMenuBar();
    }
}

// Render the toolbar
void RenderToolbar(AppState& state) {
    ImGui::PushStyleVar(ImGuiStyleVar_FramePadding, ImVec2(10, 6));
    
    if (ImGui::Button(state.isScanning ? "Arrêter" : "Scanner")) {
        if (state.isScanning) {
            state.scanner.CancelScan();
        } else {
            state.isScanning = true;
            state.scanFuture = std::async(std::launch::async, [&state]() {
                state.scanner.ScanAllDrivers();
                state.isScanning = false;
                state.statusMessage = "Scan terminé";
            });
        }
    }
    
    ImGui::SameLine();
    ImGui::BeginDisabled(state.selectedDriver == nullptr || state.isScanning);
    
    if (ImGui::Button("Activer")) {
        if (state.selectedDriver) {
            if (state.scanner.EnableDriver(*state.selectedDriver)) {
                state.statusMessage = "Pilote activé";
            } else {
                state.statusMessage = "Erreur lors de l'activation";
            }
        }
    }
    
    ImGui::SameLine();
    if (ImGui::Button("Désactiver")) {
        if (state.selectedDriver) {
            if (state.scanner.DisableDriver(*state.selectedDriver)) {
                state.statusMessage = "Pilote désactivé";
            } else {
                state.statusMessage = "Erreur lors de la désactivation";
            }
        }
    }
    
    ImGui::SameLine();
    if (ImGui::Button("Désinstaller")) {
        if (state.selectedDriver) {
            ImGui::OpenPopup("Confirmer désinstallation");
        }
    }
    
    ImGui::EndDisabled();
    
    // Update button (always enabled)
    ImGui::SameLine();
    ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(0.15f, 0.55f, 0.20f, 0.70f));
    ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(0.20f, 0.65f, 0.25f, 0.85f));
    ImGui::PushStyleColor(ImGuiCol_ButtonActive, ImVec4(0.25f, 0.75f, 0.30f, 1.00f));
    if (ImGui::Button("Mise à jour")) {
        state.showUpdateHelpWindow = true;
    }
    ImGui::PopStyleColor(3);
    
    if (ImGui::IsItemHovered()) {
        ImGui::SetTooltip("Aide sur la mise à jour des pilotes");
    }
    
    // Confirm uninstall popup
    if (ImGui::BeginPopupModal("Confirmer désinstallation", nullptr, ImGuiWindowFlags_AlwaysAutoResize)) {
        ImGui::Text("Voulez-vous vraiment désinstaller ce pilote ?");
        ImGui::Text("Cette action peut rendre certains périphériques inutilisables.");
        ImGui::Separator();
        
        if (ImGui::Button("Oui, désinstaller", ImVec2(150, 0))) {
            if (state.selectedDriver) {
                state.scanner.UninstallDriver(*state.selectedDriver);
                state.statusMessage = "Pilote désinstallé";
            }
            ImGui::CloseCurrentPopup();
        }
        ImGui::SameLine();
        if (ImGui::Button("Annuler", ImVec2(100, 0))) {
            ImGui::CloseCurrentPopup();
        }
        ImGui::EndPopup();
    }
    
    // Search filter
    ImGui::SameLine();
    ImGui::SetNextItemWidth(200);
    char searchBuf[256];
    strncpy_s(searchBuf, state.searchFilter.c_str(), sizeof(searchBuf));
    if (ImGui::InputTextWithHint("##search", "Rechercher...", searchBuf, sizeof(searchBuf))) {
        state.searchFilter = searchBuf;
    }
    
    ImGui::PopStyleVar();
}

// Helper function for sorting drivers with grouping by name
static int CompareDrivers(const DriverManager::DriverInfo* a, const DriverManager::DriverInfo* b, int columnIndex, bool ascending) {
    int result = 0;
    
    // Primary sort by selected column
    switch (columnIndex) {
        case 0: // Nom
            result = a->deviceName.compare(b->deviceName);
            break;
        case 1: // Fabricant
            result = a->manufacturer.compare(b->manufacturer);
            break;
        case 2: // Version
            result = a->driverVersion.compare(b->driverVersion);
            break;
        case 3: // Date
            result = a->driverDate.compare(b->driverDate);
            break;
        case 4: // Status
            result = static_cast<int>(a->status) - static_cast<int>(b->status);
            break;
        default:
            result = 0;
    }
    
    // Secondary sort by name to group drivers with same name together
    // (only if primary column is not already Name)
    if (result == 0 && columnIndex != 0) {
        result = a->deviceName.compare(b->deviceName);
    }
    
    // Tertiary sort by deviceInstanceId for stable ordering
    if (result == 0) {
        result = a->deviceInstanceId.compare(b->deviceInstanceId);
    }
    
    return ascending ? result : -result;
}

// Render driver list with integrated details panel
void RenderDriverList(AppState& state) {
    const auto& categories = state.scanner.GetCategories();
    
    // Calculate panel widths
    float availableWidth = ImGui::GetContentRegionAvail().x;
    float categoriesWidth = 180.0f;
    float detailsWidth = state.selectedDriver ? 300.0f : 0.0f;
    float driverListWidth = availableWidth - categoriesWidth - detailsWidth - 16.0f; // 16 for spacing
    
    // Left panel - Categories
    ImGui::BeginChild("Categories", ImVec2(categoriesWidth, 0), true);
    
    if (ImGui::Selectable("Tous les pilotes", state.selectedCategory == -1)) {
        state.selectedCategory = -1;
    }
    
    ImGui::Separator();
    
    for (int i = 0; i < (int)categories.size(); i++) {
        const auto& cat = categories[i];
        if (cat.drivers.empty()) continue;
        
        char label[128];
        snprintf(label, sizeof(label), "%s (%zu)", 
            DriverManager::GetTypeText(cat.type), cat.drivers.size());
        
        ImGui::PushID(i);
        if (ImGui::Selectable(label, state.selectedCategory == i)) {
            state.selectedCategory = i;
        }
        ImGui::PopID();
    }
    
    ImGui::EndChild();
    
    ImGui::SameLine();
    
    // Center panel - Driver table
    ImGui::BeginChild("DriverList", ImVec2(driverListWidth, 0), true);
    
    if (ImGui::BeginTable("Drivers", 5, 
        ImGuiTableFlags_Borders | ImGuiTableFlags_RowBg | ImGuiTableFlags_Resizable | 
        ImGuiTableFlags_Sortable | ImGuiTableFlags_SortMulti | ImGuiTableFlags_ScrollY)) {
        
        ImGui::TableSetupColumn("Nom", ImGuiTableColumnFlags_DefaultSort, 200.0f);
        ImGui::TableSetupColumn("Fabricant", ImGuiTableColumnFlags_None, 120.0f);
        ImGui::TableSetupColumn("Version", ImGuiTableColumnFlags_None, 80.0f);
        ImGui::TableSetupColumn("Date", ImGuiTableColumnFlags_None, 90.0f);
        ImGui::TableSetupColumn("Status", ImGuiTableColumnFlags_None, 70.0f);
        ImGui::TableSetupScrollFreeze(0, 1);
        ImGui::TableHeadersRow();
        
        // Collect drivers to display
        std::vector<DriverManager::DriverInfo*> displayDrivers;
        
        for (auto& cat : const_cast<std::vector<DriverManager::DriverCategory>&>(categories)) {
            if (state.selectedCategory >= 0 && state.selectedCategory != (int)(&cat - &categories[0])) {
                continue;
            }
            
            for (auto& driver : cat.drivers) {
                // Apply search filter
                if (!state.searchFilter.empty()) {
                    std::string name = DriverManager::WideToUtf8(driver.deviceName);
                    std::string mfg = DriverManager::WideToUtf8(driver.manufacturer);
                    
                    std::string filter = state.searchFilter;
                    std::transform(filter.begin(), filter.end(), filter.begin(), ::tolower);
                    std::transform(name.begin(), name.end(), name.begin(), ::tolower);
                    std::transform(mfg.begin(), mfg.end(), mfg.begin(), ::tolower);
                    
                    if (name.find(filter) == std::string::npos && 
                        mfg.find(filter) == std::string::npos) {
                        continue;
                    }
                }
                
                displayDrivers.push_back(&driver);
            }
        }
        
        // Handle sorting
        if (ImGuiTableSortSpecs* sortSpecs = ImGui::TableGetSortSpecs()) {
            if (sortSpecs->SpecsDirty && sortSpecs->SpecsCount > 0) {
                const ImGuiTableColumnSortSpecs& spec = sortSpecs->Specs[0];
                state.sortColumnIndex = spec.ColumnIndex;
                state.sortAscending = (spec.SortDirection == ImGuiSortDirection_Ascending);
                state.sortSpecsInitialized = true;
                sortSpecs->SpecsDirty = false;
            }
        }
        
        std::sort(displayDrivers.begin(), displayDrivers.end(),
            [&state](const DriverManager::DriverInfo* a, const DriverManager::DriverInfo* b) {
                return CompareDrivers(a, b, state.sortColumnIndex, state.sortAscending) < 0;
            });
        
        // Render driver rows
        ImGuiListClipper clipper;
        clipper.Begin((int)displayDrivers.size());
        
        while (clipper.Step()) {
            for (int row = clipper.DisplayStart; row < clipper.DisplayEnd; row++) {
                auto* driver = displayDrivers[row];
                
                ImGui::TableNextRow();
                
                ImGui::TableNextColumn();
                bool isSelected = (state.selectedDriver == driver);
                
                ImGui::PushID(row);
                if (ImGui::Selectable(DriverManager::WideToUtf8(driver->deviceName).c_str(), 
                    isSelected, ImGuiSelectableFlags_SpanAllColumns)) {
                    state.selectedDriver = driver;
                }
                ImGui::PopID();
                
                ImGui::TableNextColumn();
                ImGui::TextUnformatted(DriverManager::WideToUtf8(driver->manufacturer).c_str());
                
                ImGui::TableNextColumn();
                ImGui::TextUnformatted(DriverManager::WideToUtf8(driver->driverVersion).c_str());
                
                ImGui::TableNextColumn();
                ImGui::TextUnformatted(DriverManager::WideToUtf8(driver->driverDate).c_str());
                
                ImGui::TableNextColumn();
                ImVec4 statusColor;
                switch (driver->status) {
                    case DriverManager::DriverStatus::OK:
                        statusColor = ImVec4(0.2f, 0.8f, 0.2f, 1.0f);
                        break;
                    case DriverManager::DriverStatus::Warning:
                        statusColor = ImVec4(0.9f, 0.7f, 0.0f, 1.0f);
                        break;
                    case DriverManager::DriverStatus::Error:
                        statusColor = ImVec4(0.9f, 0.2f, 0.2f, 1.0f);
                        break;
                    case DriverManager::DriverStatus::Disabled:
                        statusColor = ImVec4(0.5f, 0.5f, 0.5f, 1.0f);
                        break;
                    default:
                        statusColor = ImVec4(0.7f, 0.7f, 0.7f, 1.0f);
                }
                ImGui::TextColored(statusColor, "%s", DriverManager::GetStatusText(driver->status));
            }
        }
        
        ImGui::EndTable();
    }
    
    ImGui::EndChild();
    
    // Right panel - Details (only shown when a driver is selected)
    if (state.selectedDriver) {
        ImGui::SameLine();
        
        ImGui::BeginChild("Details", ImVec2(detailsWidth, 0), true);
        
        auto* d = state.selectedDriver;
        
        // Header with device name
        ImGui::TextColored(ImVec4(0.4f, 0.7f, 1.0f, 1.0f), "Détails du pilote");
        ImGui::Separator();
        ImGui::Spacing();
        
        // Device name (wrapped)
        ImGui::TextWrapped("%s", DriverManager::WideToUtf8(d->deviceName).c_str());
        ImGui::Spacing();
        ImGui::Separator();
        ImGui::Spacing();
        
        // Details in a compact format
        auto addDetailRow = [](const char* label, const std::string& value) {
            if (value.empty()) return;
            ImGui::TextColored(ImVec4(0.6f, 0.6f, 0.7f, 1.0f), "%s", label);
            ImGui::TextWrapped("%s", value.c_str());
            ImGui::Spacing();
        };
        
        addDetailRow("Description:", DriverManager::WideToUtf8(d->deviceDescription));
        addDetailRow("Fabricant:", DriverManager::WideToUtf8(d->manufacturer));
        addDetailRow("Version:", DriverManager::WideToUtf8(d->driverVersion));
        addDetailRow("Date:", DriverManager::WideToUtf8(d->driverDate));
        addDetailRow("Fournisseur:", DriverManager::WideToUtf8(d->driverProvider));
        addDetailRow("Classe:", DriverManager::WideToUtf8(d->deviceClass));
        
        ImGui::Separator();
        ImGui::Spacing();
        
        // Status with color
        ImGui::TextColored(ImVec4(0.6f, 0.6f, 0.7f, 1.0f), "Status:");
        ImVec4 statusColor;
        switch (d->status) {
            case DriverManager::DriverStatus::OK:
                statusColor = ImVec4(0.2f, 0.8f, 0.2f, 1.0f);
                break;
            case DriverManager::DriverStatus::Warning:
                statusColor = ImVec4(0.9f, 0.7f, 0.0f, 1.0f);
                break;
            case DriverManager::DriverStatus::Error:
                statusColor = ImVec4(0.9f, 0.2f, 0.2f, 1.0f);
                break;
            case DriverManager::DriverStatus::Disabled:
                statusColor = ImVec4(0.5f, 0.5f, 0.5f, 1.0f);
                break;
            default:
                statusColor = ImVec4(0.7f, 0.7f, 0.7f, 1.0f);
        }
        ImGui::TextColored(statusColor, "%s", DriverManager::GetStatusText(d->status));
        ImGui::Spacing();
        
        // Enabled status
        ImGui::TextColored(ImVec4(0.6f, 0.6f, 0.7f, 1.0f), "Activé:");
        ImGui::Text("%s", d->isEnabled ? "Oui" : "Non");
        ImGui::Spacing();
        
        if (d->problemCode != 0) {
            ImGui::TextColored(ImVec4(0.6f, 0.6f, 0.7f, 1.0f), "Code problème:");
            ImGui::TextColored(ImVec4(0.9f, 0.5f, 0.2f, 1.0f), "%u", d->problemCode);
            ImGui::Spacing();
        }
        
        ImGui::Separator();
        ImGui::Spacing();
        
        // Hardware ID (collapsible since it can be long)
        if (ImGui::CollapsingHeader("IDs matériel")) {
            ImGui::TextColored(ImVec4(0.6f, 0.6f, 0.7f, 1.0f), "Hardware ID:");
            ImGui::TextWrapped("%s", DriverManager::WideToUtf8(d->hardwareId).c_str());
            ImGui::Spacing();
            
            ImGui::TextColored(ImVec4(0.6f, 0.6f, 0.7f, 1.0f), "Instance ID:");
            ImGui::TextWrapped("%s", DriverManager::WideToUtf8(d->deviceInstanceId).c_str());
        }
        
        ImGui::EndChild();
    }
}

// Render details window
void RenderDetailsWindow(AppState& state) {
    if (!state.showDetailsWindow || !state.selectedDriver) return;
    
    ImGui::SetNextWindowSize(ImVec2(500, 400), ImGuiCond_FirstUseEver);
    if (ImGui::Begin("Détails du pilote", &state.showDetailsWindow)) {
        auto* d = state.selectedDriver;
        
        ImGui::Text("Nom: %s", DriverManager::WideToUtf8(d->deviceName).c_str());
        ImGui::Separator();
        
        if (ImGui::BeginTable("Details", 2, ImGuiTableFlags_Borders)) {
            auto addRow = [](const char* label, const std::string& value) {
                ImGui::TableNextRow();
                ImGui::TableNextColumn();
                ImGui::TextUnformatted(label);
                ImGui::TableNextColumn();
                ImGui::TextWrapped("%s", value.c_str());
            };
            
            addRow("Description", DriverManager::WideToUtf8(d->deviceDescription));
            addRow("Fabricant", DriverManager::WideToUtf8(d->manufacturer));
            addRow("Version", DriverManager::WideToUtf8(d->driverVersion));
            addRow("Date", DriverManager::WideToUtf8(d->driverDate));
            addRow("Fournisseur", DriverManager::WideToUtf8(d->driverProvider));
            addRow("Classe", DriverManager::WideToUtf8(d->deviceClass));
            addRow("Hardware ID", DriverManager::WideToUtf8(d->hardwareId));
            addRow("Instance ID", DriverManager::WideToUtf8(d->deviceInstanceId));
            addRow("Status", DriverManager::GetStatusText(d->status));
            addRow("Code problème", std::to_string(d->problemCode));
            addRow("Activé", d->isEnabled ? "Oui" : "Non");
            
            ImGui::EndTable();
        }
    }
    ImGui::End();
}

// Render about window
void RenderAboutWindow(AppState& state) {
    if (!state.showAboutWindow) return;
    
    ImGui::SetNextWindowSize(ImVec2(400, 200), ImGuiCond_FirstUseEver);
    if (ImGui::Begin("À propos", &state.showAboutWindow, ImGuiWindowFlags_NoResize)) {
        ImGui::Text("Driver Manager");
        ImGui::Text("Version 1.0.0");
        ImGui::Separator();
        ImGui::Text("Gestionnaire de pilotes Windows");
        ImGui::Text("Utilise Dear ImGui pour l'interface graphique");
        ImGui::Separator();
        ImGui::Text("Développé avec C++20 et DirectX 11");
    }
    ImGui::End();
}

// Render update help window
void RenderUpdateHelpWindow(AppState& state) {
    if (!state.showUpdateHelpWindow) return;
    
    ImGui::SetNextWindowSize(ImVec2(580, 520), ImGuiCond_FirstUseEver);
    if (ImGui::Begin("Mise à jour des pilotes", &state.showUpdateHelpWindow)) {
        
        ImGui::TextColored(ImVec4(0.4f, 0.8f, 0.4f, 1.0f), "TousLesDrivers.com - Mes Drivers");
        ImGui::Separator();
        ImGui::Spacing();
        
        ImGui::TextWrapped(
            "Pour mettre à jour vos pilotes, nous vous recommandons d'utiliser "
            "l'outil 'Mes Drivers' de TousLesDrivers.com, un service gratuit et fiable.");
        
        ImGui::Spacing();
        ImGui::Separator();
        ImGui::Spacing();
        
        ImGui::TextColored(ImVec4(0.9f, 0.8f, 0.3f, 1.0f), "Comment fonctionne 'Mes Drivers' :");
        ImGui::Spacing();
        
        ImGui::TextWrapped(
            "1. Cliquez sur le bouton ci-dessous pour ouvrir la page Mes Drivers");
        ImGui::Spacing();
        
        ImGui::TextWrapped(
            "2. T\xe9l\xe9chargez et ex\xe9cutez l'outil de d\xe9tection (DriversCloud.exe)");
        ImGui::Spacing();
        
        ImGui::TextWrapped(
            "3. L'outil analyse automatiquement votre PC et identifie tous vos "
            "composants mat\xe9riels ainsi que les versions de vos pilotes");
        ImGui::Spacing();
        
        ImGui::TextWrapped(
            "4. Une page web s'ouvre avec la liste compl\xe8te de vos pilotes et "
            "les mises \xe0 jour disponibles");
        ImGui::Spacing();
        
        ImGui::TextWrapped(
            "5. T\xe9l\xe9chargez les pilotes n\xe9cessaires directement depuis leur site");
        
        ImGui::Spacing();
        ImGui::Separator();
        ImGui::Spacing();
        
        ImGui::TextColored(ImVec4(0.5f, 0.7f, 1.0f, 1.0f), "Avantages de Mes Drivers :");
        ImGui::Spacing();
        
        ImGui::BulletText("D\xe9tection automatique de tous vos composants");
        ImGui::BulletText("Identification pr\xe9cise des versions install\xe9es");
        ImGui::BulletText("Liens directs vers les pilotes officiels");
        ImGui::BulletText("Service gratuit et sans inscription");
        ImGui::BulletText("Base de donn\xe9es compl\xe8te et \xe0 jour");
        
        ImGui::Spacing();
        ImGui::Separator();
        ImGui::Spacing();
        
        // Main button - Mes Drivers
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
        if (ImGui::Button("Fermer", ImVec2(80, 35))) {
            state.showUpdateHelpWindow = false;
        }
    }
    ImGui::End();
}

// Render status bar
void RenderStatusBar(AppState& state) {
    ImGuiViewport* viewport = ImGui::GetMainViewport();
    ImGui::SetNextWindowPos(ImVec2(viewport->Pos.x, viewport->Pos.y + viewport->Size.y - 30));
    ImGui::SetNextWindowSize(ImVec2(viewport->Size.x, 30));
    
    ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding, ImVec2(10, 5));
    ImGui::Begin("StatusBar", nullptr, 
        ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoResize | 
        ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoScrollbar |
        ImGuiWindowFlags_NoSavedSettings);
    
    if (state.isScanning) {
        ImGui::Text("Scan en cours...");
        ImGui::SameLine();
        ImGui::ProgressBar(state.scanProgress, ImVec2(200, 0));
        ImGui::SameLine();
        ImGui::Text("%s", DriverManager::WideToUtf8(state.currentScanItem).c_str());
    } else {
        ImGui::Text("%s", state.statusMessage.c_str());
        ImGui::SameLine(ImGui::GetWindowWidth() - 200);
        ImGui::Text("Total: %zu pilotes", state.scanner.GetTotalDriverCount());
    }
    
    ImGui::End();
    ImGui::PopStyleVar();
}

// Main entry point
int WINAPI WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine, int nCmdShow) {
    // Create window class
    WNDCLASSEXW wc = { 
        sizeof(wc), CS_CLASSDC, WndProc, 0L, 0L, 
        GetModuleHandle(nullptr), nullptr, nullptr, nullptr, nullptr, 
        L"DriverManagerClass", nullptr 
    };
    RegisterClassExW(&wc);

    // Create window
    HWND hwnd = CreateWindowW(
        wc.lpszClassName, L"Driver Manager", 
        WS_OVERLAPPEDWINDOW,
        100, 100, 1200, 800, 
        nullptr, nullptr, wc.hInstance, nullptr
    );

    // Initialize Direct3D
    if (!CreateDeviceD3D(hwnd)) {
        CleanupDeviceD3D();
        UnregisterClassW(wc.lpszClassName, wc.hInstance);
        return 1;
    }

    ShowWindow(hwnd, SW_SHOWDEFAULT);
    UpdateWindow(hwnd);

    // Setup ImGui context
    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO();
    io.ConfigFlags |= ImGuiConfigFlags_NavEnableKeyboard;

    // Setup style
    SetupImGuiStyle();

    // Setup backends
    ImGui_ImplWin32_Init(hwnd);
    ImGui_ImplDX11_Init(g_pd3dDevice, g_pd3dDeviceContext);

    // Load font (use Windows default if custom font fails)
    io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\segoeui.ttf", 16.0f);

    // Application state
    AppState state;
    state.statusMessage = "Prêt - Appuyez sur Scanner pour commencer";

    // Main loop
    bool done = false;
    while (!done) {
        MSG msg;
        while (PeekMessage(&msg, nullptr, 0U, 0U, PM_REMOVE)) {
            TranslateMessage(&msg);
            DispatchMessage(&msg);
            if (msg.message == WM_QUIT) {
                done = true;
            }
        }
        if (done) break;

        // Handle window resize
        if (g_ResizeWidth != 0 && g_ResizeHeight != 0) {
            CleanupRenderTarget();
            g_pSwapChain->ResizeBuffers(0, g_ResizeWidth, g_ResizeHeight, DXGI_FORMAT_UNKNOWN, 0);
            g_ResizeWidth = g_ResizeHeight = 0;
            CreateRenderTarget();
        }

        // Start ImGui frame
        ImGui_ImplDX11_NewFrame();
        ImGui_ImplWin32_NewFrame();
        ImGui::NewFrame();

        // Keyboard shortcuts
        if (io.KeyCtrl && ImGui::IsKeyPressed(ImGuiKey_E)) {
            state.showExportDialog = true;
        }
        if (ImGui::IsKeyPressed(ImGuiKey_F5) && !state.isScanning) {
            state.isScanning = true;
            state.scanFuture = std::async(std::launch::async, [&state]() {
                state.scanner.ScanAllDrivers();
                state.isScanning = false;
                state.statusMessage = "Scan terminé";
            });
        }

        // Render UI
        RenderMenuBar(state);
        
        // Main window
        ImGuiViewport* viewport = ImGui::GetMainViewport();
        ImGui::SetNextWindowPos(ImVec2(viewport->Pos.x, viewport->Pos.y + 20));
        ImGui::SetNextWindowSize(ImVec2(viewport->Size.x, viewport->Size.y - 50));
        
        ImGui::Begin("Main", nullptr, 
            ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoResize | 
            ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoBringToFrontOnFocus |
            ImGuiWindowFlags_NoNavFocus | ImGuiWindowFlags_NoSavedSettings);
        
        RenderToolbar(state);
        ImGui::Separator();
        RenderDriverList(state);
        
        ImGui::End();
        
        RenderDetailsWindow(state);
        RenderAboutWindow(state);
        RenderUpdateHelpWindow(state);
        RenderStatusBar(state);

        // Rendering
        ImGui::Render();
        const float clear_color[4] = { 0.1f, 0.1f, 0.12f, 1.0f };
        g_pd3dDeviceContext->OMSetRenderTargets(1, &g_mainRenderTargetView, nullptr);
        g_pd3dDeviceContext->ClearRenderTargetView(g_mainRenderTargetView, clear_color);
        ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());

        g_pSwapChain->Present(1, 0); // VSync
    }

    // Cleanup
    ImGui_ImplDX11_Shutdown();
    ImGui_ImplWin32_Shutdown();
    ImGui::DestroyContext();

    CleanupDeviceD3D();
    DestroyWindow(hwnd);
    UnregisterClassW(wc.lpszClassName, wc.hInstance);

    return 0;
}

// Direct3D helper functions
bool CreateDeviceD3D(HWND hWnd) {
    DXGI_SWAP_CHAIN_DESC sd;
    ZeroMemory(&sd, sizeof(sd));
    sd.BufferCount = 2;
    sd.BufferDesc.Width = 0;
    sd.BufferDesc.Height = 0;
    sd.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    sd.BufferDesc.RefreshRate.Numerator = 60;
    sd.BufferDesc.RefreshRate.Denominator = 1;
    sd.Flags = DXGI_SWAP_CHAIN_FLAG_ALLOW_MODE_SWITCH;
    sd.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    sd.OutputWindow = hWnd;
    sd.SampleDesc.Count = 1;
    sd.SampleDesc.Quality = 0;
    sd.Windowed = TRUE;
    sd.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;

    UINT createDeviceFlags = 0;
    D3D_FEATURE_LEVEL featureLevel;
    const D3D_FEATURE_LEVEL featureLevelArray[2] = { D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_0 };
    
    HRESULT res = D3D11CreateDeviceAndSwapChain(
        nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, createDeviceFlags,
        featureLevelArray, 2, D3D11_SDK_VERSION, &sd,
        &g_pSwapChain, &g_pd3dDevice, &featureLevel, &g_pd3dDeviceContext
    );
    
    if (res == DXGI_ERROR_UNSUPPORTED) {
        res = D3D11CreateDeviceAndSwapChain(
            nullptr, D3D_DRIVER_TYPE_WARP, nullptr, createDeviceFlags,
            featureLevelArray, 2, D3D11_SDK_VERSION, &sd,
            &g_pSwapChain, &g_pd3dDevice, &featureLevel, &g_pd3dDeviceContext
        );
    }
    
    if (res != S_OK) return false;

    CreateRenderTarget();
    return true;
}

void CleanupDeviceD3D() {
    CleanupRenderTarget();
    if (g_pSwapChain) { g_pSwapChain->Release(); g_pSwapChain = nullptr; }
    if (g_pd3dDeviceContext) { g_pd3dDeviceContext->Release(); g_pd3dDeviceContext = nullptr; }
    if (g_pd3dDevice) { g_pd3dDevice->Release(); g_pd3dDevice = nullptr; }
}

void CreateRenderTarget() {
    ID3D11Texture2D* pBackBuffer;
    g_pSwapChain->GetBuffer(0, IID_PPV_ARGS(&pBackBuffer));
    g_pd3dDevice->CreateRenderTargetView(pBackBuffer, nullptr, &g_mainRenderTargetView);
    pBackBuffer->Release();
}

void CleanupRenderTarget() {
    if (g_mainRenderTargetView) { g_mainRenderTargetView->Release(); g_mainRenderTargetView = nullptr; }
}

LRESULT WINAPI WndProc(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam) {
    if (ImGui_ImplWin32_WndProcHandler(hWnd, msg, wParam, lParam))
        return true;

    switch (msg) {
    case WM_SIZE:
        if (wParam == SIZE_MINIMIZED)
            return 0;
        g_ResizeWidth = (UINT)LOWORD(lParam);
        g_ResizeHeight = (UINT)HIWORD(lParam);
        return 0;
    case WM_SYSCOMMAND:
        if ((wParam & 0xfff0) == SC_KEYMENU)
            return 0;
        break;
    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(hWnd, msg, wParam, lParam);
}
