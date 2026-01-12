// DriverManager - Windows Driver Management Tool
// Using Dear ImGui with DirectX 11

#include "imgui/imgui.h"
#include "imgui/imgui_internal.h"
#include "imgui/imgui_impl_win32.h"
#include "imgui/imgui_impl_dx11.h"
#include <d3d11.h>
#include <tchar.h>
#include <shellapi.h>
#include <thread>
#include <future>
#include <chrono>
#include <algorithm>
#include <sstream>
#include <iomanip>
#include <map>
#include <set>

// For checking admin rights
#include <sddl.h>
#pragma comment(lib, "advapi32.lib")

#include "src/DriverScanner.h"
#include "src/DriverInfo.h"
#include "src/UpdateChecker.h"
#include "src/ManufacturerLinks.h"
#include "src/DriverStoreCleanup.h"
#include "src/DriverDownloader.h"
#include "src/BSODAnalyzer.h"

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

// Helper function to check if running as administrator
bool IsRunningAsAdmin() {
    BOOL isAdmin = FALSE;
    PSID adminGroup = NULL;
    SID_IDENTIFIER_AUTHORITY ntAuthority = SECURITY_NT_AUTHORITY;
    
    if (AllocateAndInitializeSid(&ntAuthority, 2,
        SECURITY_BUILTIN_DOMAIN_RID, DOMAIN_ALIAS_RID_ADMINS,
        0, 0, 0, 0, 0, 0, &adminGroup)) {
        CheckTokenMembership(NULL, adminGroup, &isAdmin);
        FreeSid(adminGroup);
    }
    return isAdmin == TRUE;
}

// Global flag for admin status
static bool g_isAdmin = false;

// Application state
struct AppState {
    DriverManager::DriverScanner scanner;
    DriverManager::UpdateChecker updateChecker;
    DriverManager::DriverStoreCleanup driverStoreCleanup;  // For DriverStore cleanup
    DriverManager::DriverDownloader driverDownloader;       // For driver downloads
    DriverManager::BSODAnalyzer bsodAnalyzer;               // For BSOD analysis
    bool isScanning = false;
    bool isCheckingUpdates = false;
    bool cancelUpdateCheck = false;  // New: flag to cancel update check
    bool showDetailsWindow = false;
    bool showAboutWindow = false;
    bool showExportDialog = false;
    bool showUpdateHelpWindow = false;
    bool showUpdatesWindow = false;
    bool showUpdateProgressWindow = false;  // New: progress window for update check
    bool showDriverStoreCleanup = false;    // DriverStore cleanup window
    bool showDownloadWindow = false;         // Download manager window
    bool showBSODAnalyzer = false;           // BSOD analyzer window
    bool createRestorePoint = false;         // Option for restore point
    bool isCleaningDriverStore = false;     // Cleanup in progress
    bool isScanningBSOD = false;            // BSOD scan in progress
    bool needsDriverStoreRefresh = false;   // Flag to refresh after deletion
    int lastDeletedCount = 0;               // Number of drivers deleted in last operation
    // BSOD scan future for thread safety
    std::future<void> bsodScanFuture;
    int bsodScanProgress = 0;               // Progress of BSOD scan
    int bsodScanTotal = 0;                  // Total dumps to scan
    std::wstring bsodCurrentItem;           // Current dump being scanned
    DriverManager::DriverInfo* selectedDriver = nullptr;
    std::string statusMessage;
    std::string searchFilter;
    int selectedCategory = -1; // -1 = all
    std::future<void> scanFuture;
    std::future<void> updateCheckFuture;
    float scanProgress = 0.0f;
    float updateCheckProgress = 0.0f;
    std::wstring currentScanItem;
    std::wstring currentUpdateItem;  // New: current driver being checked for updates
    int updatesFound = 0;
    int updateSource = 0;  // New: 0=none, 1=Touslesdrivers.com, 2=Windows Update Catalog
    int totalDriversToCheck = 0;  // New: total drivers to check
    int driversChecked = 0;  // New: drivers already checked
    
    // Sorting state - persisted across frames
    int sortColumnIndex = 0;          // Default sort by Name
    bool sortAscending = true;        // Default ascending
    bool sortSpecsInitialized = false;
    
    // Grouping state - tracks which driver groups are expanded
    std::set<std::wstring> expandedGroups;
    
    // Filter for showing only old drivers
    bool filterOldDrivers = false;
    bool filterUpdatesAvailable = false;
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
                state.scanProgress = 0.0f; // Reset progress
                state.scanFuture = std::async(std::launch::async, [&state]() {
                    state.scanner.SetProgressCallback([&state](int current, int total, const std::wstring& item) {
                        if (total > 0) {
                            state.scanProgress = (float)current / (float)total;
                        }
                        state.currentScanItem = item;
                    });
                    state.scanner.ScanAllDrivers();
                    state.isScanning = false;
                    state.statusMessage = "Scan termin\xc3\xa9 - " + std::to_string(state.scanner.GetTotalDriverCount()) + " pilotes trouv\xc3\xa9s";
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
            if (ImGui::MenuItem("D\xc3\xa9tails en fen\xc3\xaatre", nullptr, state.showDetailsWindow)) {
                state.showDetailsWindow = !state.showDetailsWindow;
            }
            if (ImGui::IsItemHovered()) {
                ImGui::SetTooltip("Afficher les d\xc3\xa9tails dans une fen\xc3\xaatre s\xc3\xa9par\xc3\xa9" "e");
            }
            ImGui::EndMenu();
        }
        
        if (ImGui::BeginMenu("Outils")) {
            if (ImGui::MenuItem("Nettoyer DriverStore...", nullptr, false, !state.isCleaningDriverStore)) {
                state.showDriverStoreCleanup = true;
                // Scan DriverStore when opening the window
                state.driverStoreCleanup.ScanDriverStore();
            }
            if (ImGui::IsItemHovered()) {
                ImGui::SetTooltip("Supprimer les anciennes versions de pilotes pour lib\xc3\xa9rer de l'espace");
            }
            if (ImGui::MenuItem("Analyser les BSOD...", nullptr, false, !state.isScanningBSOD)) {
                state.showBSODAnalyzer = true;
            }
            if (ImGui::IsItemHovered()) {
                ImGui::SetTooltip("Analyser les minidumps pour d\xc3\xa9tecter les pilotes probl\xc3\xa9matiques");
            }
            ImGui::Separator();
            if (ImGui::MenuItem("Telechargements...", nullptr, state.showDownloadWindow)) {
                state.showDownloadWindow = !state.showDownloadWindow;
            }
            if (ImGui::IsItemHovered()) {
                ImGui::SetTooltip("Gerer les telechargements et installations de pilotes");
            }
            ImGui::EndMenu();
        }
        
        if (ImGui::BeginMenu("Aide")) {
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

// Render the toolbar
void RenderToolbar(AppState& state) {
    // Warning banner if not admin
    if (!g_isAdmin) {
        ImGui::PushStyleColor(ImGuiCol_ChildBg, ImVec4(0.6f, 0.4f, 0.0f, 0.3f));
        ImGui::BeginChild("AdminWarning", ImVec2(0, 28), false);
        ImGui::TextColored(ImVec4(1.0f, 0.8f, 0.2f, 1.0f), 
            "   Mode limit\xc3\xa9" " : Les boutons Activer/D\xc3\xa9" "sactiver n\xc3\xa9" "cessitent les droits administrateur");
        ImGui::EndChild();
        ImGui::PopStyleColor();
        ImGui::Spacing();
    }
    
    ImGui::PushStyleVar(ImGuiStyleVar_FramePadding, ImVec2(10, 6));
    
    if (ImGui::Button(state.isScanning ? "Arr\xc3\xaater" : "Scanner")) {
        if (state.isScanning) {
            state.scanner.CancelScan();
        } else {
            state.isScanning = true;
            state.scanProgress = 0.0f;
            state.scanFuture = std::async(std::launch::async, [&state]() {
                state.scanner.SetProgressCallback([&state](int current, int total, const std::wstring& item) {
                    if (total > 0) {
                        state.scanProgress = (float)current / (float)total;
                    }
                    state.currentScanItem = item;
                });
                state.scanner.ScanAllDrivers();
                state.isScanning = false;
                state.statusMessage = "Scan termin\xc3\xa9";
            });
        }
    }
    
    ImGui::SameLine();
    ImGui::BeginDisabled(state.selectedDriver == nullptr || state.isScanning);
    
    if (ImGui::Button("Activer")) {
        if (state.selectedDriver) {
            if (state.scanner.EnableDriver(*state.selectedDriver)) {
                state.statusMessage = "Pilote activ\xc3\xa9" " avec succ\xc3\xa8" "s";
            } else {
                if (!g_isAdmin) {
                    state.statusMessage = "Erreur: Red\xc3\xa9" "marrez en tant qu'administrateur";
                } else {
                    state.statusMessage = "Erreur: Ce pilote ne peut pas \xc3\xaatre activ\xc3\xa9";
                }
            }
        }
    }
    
    ImGui::SameLine();
    if (ImGui::Button("D\xc3\xa9sactiver")) {
        if (state.selectedDriver) {
            if (state.scanner.DisableDriver(*state.selectedDriver)) {
                state.statusMessage = "Pilote d\xc3\xa9" "sactiv\xc3\xa9" " avec succ\xc3\xa8" "s";
            } else {
                if (!g_isAdmin) {
                    state.statusMessage = "Erreur: Red\xc3\xa9" "marrez en tant qu'administrateur";
                } else {
                    state.statusMessage = "Erreur: Ce pilote ne peut pas \xc3\xaatre d\xc3\xa9" "sactiv\xc3\xa9";
                }
            }
        }
    }
    
    ImGui::SameLine();
    if (ImGui::Button("D\xc3\xa9sinstaller")) {
        if (state.selectedDriver) {
            ImGui::OpenPopup("Confirmer d\xc3\xa9sinstallation");
        }
    }
    
    ImGui::EndDisabled();
    
    // Check for updates button
    ImGui::SameLine();
    ImGui::BeginDisabled(state.isCheckingUpdates || state.isScanning || state.scanner.GetTotalDriverCount() == 0);
    ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(0.55f, 0.35f, 0.15f, 0.70f));
    ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(0.65f, 0.45f, 0.20f, 0.85f));
    ImGui::PushStyleColor(ImGuiCol_ButtonActive, ImVec4(0.75f, 0.55f, 0.25f, 1.00f));
    if (ImGui::Button(state.isCheckingUpdates ? "V\xc3\xa9rification..." : "V\xc3\xa9rifier MAJ")) {
        state.isCheckingUpdates = true;
        state.showUpdateProgressWindow = true;
        state.updatesFound = 0;
        state.updateCheckProgress = 0.0f;
        state.updateSource = 2;
        state.cancelUpdateCheck = false;
        state.currentUpdateItem = L"Initialisation...";
        state.updateCheckFuture = std::async(std::launch::async, [&state]() {
            auto drivers = state.scanner.GetAllDrivers();
            state.totalDriversToCheck = static_cast<int>(drivers.size());
            state.driversChecked = 0;
            state.updateChecker.SetProgressCallback([&state](int current, int total, const std::wstring& device) {
                state.updateCheckProgress = total > 0 ? (float)current / (float)total : 0;
                state.currentUpdateItem = device;
                state.driversChecked = current;
            });
            state.updateChecker.CheckWindowsUpdate(drivers);
            state.updatesFound = state.updateChecker.GetLastCheckUpdatesFound();
            state.isCheckingUpdates = false;
            if (state.updatesFound > 0) {
                state.statusMessage = std::to_string(state.updatesFound) + " mise(s) \xc3\xa0 jour disponible(s)";
            } else {
                state.statusMessage = "Tous les pilotes sont \xc3\xa0 jour";
            }
        });
    }
    ImGui::PopStyleColor(3);
    ImGui::EndDisabled();
    
    if (ImGui::IsItemHovered() && !state.isCheckingUpdates) {
        ImGui::SetTooltip("V\xc3\xa9rifier les mises \xc3\xa0 jour via Windows Update Catalog");
    }
    
    // Separator before filters
    ImGui::SameLine();
    ImGui::SeparatorEx(ImGuiSeparatorFlags_Vertical);
    ImGui::SameLine();
    
    // Filter checkboxes
    if (ImGui::Checkbox("Anciens (>2 ans)", &state.filterOldDrivers)) {
        // Filter will be applied in RenderDriverList
    }
    if (ImGui::IsItemHovered()) {
        ImGui::SetTooltip("Afficher uniquement les pilotes de plus de 2 ans");
    }
    
    // Confirm uninstall popup
    if (ImGui::BeginPopupModal("Confirmer d\xc3\xa9sinstallation", nullptr, ImGuiWindowFlags_AlwaysAutoResize)) {
        ImGui::Text("Voulez-vous vraiment d\xc3\xa9sinstaller ce pilote ?");
        ImGui::Text("Cette action peut rendre certains p\xc3\xa9riph\xc3\xa9riques inutilisables.");
        ImGui::Separator();
        
        if (ImGui::Button("Oui, d\xc3\xa9sinstaller", ImVec2(150, 0))) {
            if (state.selectedDriver) {
                state.scanner.UninstallDriver(*state.selectedDriver);
                state.statusMessage = "Pilote d\xc3\xa9sinstall\xc3\xa9";
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
    // Single-click focus: if hovering and clicked, set focus before drawing
    if (ImGui::IsMouseClicked(0)) {
        ImVec2 mousePos = ImGui::GetMousePos();
        ImVec2 cursorPos = ImGui::GetCursorScreenPos();
        if (mousePos.x >= cursorPos.x && mousePos.x <= cursorPos.x + 200 &&
            mousePos.y >= cursorPos.y && mousePos.y <= cursorPos.y + ImGui::GetFrameHeight()) {
            ImGui::SetKeyboardFocusHere();
        }
    }
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
        case 4: // Age
            result = a->driverAgeDays - b->driverAgeDays;
            break;
        case 5: // Status
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
    
    if (ImGui::BeginTable("Drivers", 6, 
        ImGuiTableFlags_Borders | ImGuiTableFlags_RowBg | ImGuiTableFlags_Resizable | 
        ImGuiTableFlags_Sortable | ImGuiTableFlags_SortMulti | ImGuiTableFlags_ScrollY)) {
        
        ImGui::TableSetupColumn("Nom", ImGuiTableColumnFlags_DefaultSort, 180.0f);
        ImGui::TableSetupColumn("Fabricant", ImGuiTableColumnFlags_None, 100.0f);
        ImGui::TableSetupColumn("Version", ImGuiTableColumnFlags_None, 70.0f);
        ImGui::TableSetupColumn("Date", ImGuiTableColumnFlags_None, 80.0f);
        ImGui::TableSetupColumn("\xc3\x82ge", ImGuiTableColumnFlags_None, 70.0f);
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
                
                // Apply old drivers filter
                if (state.filterOldDrivers) {
                    if (driver.ageCategory != DriverManager::DriverAge::VeryOld) {
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
        
        // Group drivers by name
        std::map<std::wstring, std::vector<DriverManager::DriverInfo*>> driverGroups;
        std::vector<std::wstring> groupOrder; // Preserve sort order
        
        for (auto* driver : displayDrivers) {
            if (driverGroups.find(driver->deviceName) == driverGroups.end()) {
                groupOrder.push_back(driver->deviceName);
            }
            driverGroups[driver->deviceName].push_back(driver);
        }
        
        // Helper lambda for status color
        auto getStatusColor = [](DriverManager::DriverStatus status) -> ImVec4 {
            switch (status) {
                case DriverManager::DriverStatus::OK:
                    return ImVec4(0.2f, 0.8f, 0.2f, 1.0f);
                case DriverManager::DriverStatus::Warning:
                    return ImVec4(0.9f, 0.7f, 0.0f, 1.0f);
                case DriverManager::DriverStatus::Error:
                    return ImVec4(0.9f, 0.2f, 0.2f, 1.0f);
                case DriverManager::DriverStatus::Disabled:
                    return ImVec4(0.5f, 0.5f, 0.5f, 1.0f);
                default:
                    return ImVec4(0.7f, 0.7f, 0.7f, 1.0f);
            }
        };
        
        // Render driver rows with grouping
        int rowId = 0;
        for (const auto& groupName : groupOrder) {
            auto& group = driverGroups[groupName];
            
            if (group.size() == 1) {
                // Single driver - render normally
                auto* driver = group[0];
                ImGui::TableNextRow();
                
                ImGui::TableNextColumn();
                bool isSelected = (state.selectedDriver == driver);
                
                ImGui::PushID(rowId++);
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
                
                // Age column
                ImGui::TableNextColumn();
                {
                    ImVec4 ageColor;
                    switch (driver->ageCategory) {
                        case DriverManager::DriverAge::Current:
                            ageColor = ImVec4(0.2f, 0.8f, 0.2f, 1.0f); // Green
                            break;
                        case DriverManager::DriverAge::Old:
                            ageColor = ImVec4(0.9f, 0.7f, 0.0f, 1.0f); // Yellow
                            break;
                        case DriverManager::DriverAge::VeryOld:
                            ageColor = ImVec4(0.9f, 0.4f, 0.1f, 1.0f); // Orange
                            break;
                        default:
                            ageColor = ImVec4(0.5f, 0.5f, 0.5f, 1.0f); // Gray
                    }
                    ImGui::TextColored(ageColor, "%s", DriverManager::GetAgeText(driver->ageCategory));
                }
                
                ImGui::TableNextColumn();
                ImGui::TextColored(getStatusColor(driver->status), "%s", 
                    DriverManager::GetStatusText(driver->status));
            } else {
                // Multiple drivers with same name - render as expandable group
                bool isExpanded = state.expandedGroups.count(groupName) > 0;
                
                // Group header row
                ImGui::TableNextRow();
                ImGui::TableNextColumn();
                
                ImGui::PushID(rowId++);
                
                // Check if any driver in group is selected
                bool groupSelected = false;
                for (auto* d : group) {
                    if (state.selectedDriver == d) {
                        groupSelected = true;
                        break;
                    }
                }
                
                // Arrow icon and name with count
                char groupLabel[256];
                snprintf(groupLabel, sizeof(groupLabel), "%s %s (%zu)",
                    isExpanded ? "\xef\x81\xb8" : "\xef\x81\xb6", // Down/Right arrow (FontAwesome-style, fallback to text)
                    DriverManager::WideToUtf8(groupName).c_str(),
                    group.size());
                
                // Use simple text arrows if font doesn't support icons
                snprintf(groupLabel, sizeof(groupLabel), "%s %s (%zu)",
                    isExpanded ? "v" : ">",
                    DriverManager::WideToUtf8(groupName).c_str(),
                    group.size());
                
                if (ImGui::Selectable(groupLabel, groupSelected, ImGuiSelectableFlags_SpanAllColumns)) {
                    // Toggle expansion
                    if (isExpanded) {
                        state.expandedGroups.erase(groupName);
                    } else {
                        state.expandedGroups.insert(groupName);
                    }
                }
                ImGui::PopID();
                
                // Show summary info for group (use first driver's info)
                auto* firstDriver = group[0];
                ImGui::TableNextColumn();
                ImGui::TextUnformatted(DriverManager::WideToUtf8(firstDriver->manufacturer).c_str());
                
                ImGui::TableNextColumn();
                ImGui::TextDisabled("..."); // Multiple versions
                
                ImGui::TableNextColumn();
                ImGui::TextDisabled("..."); // Multiple dates
                
                // Age column for group - show oldest age
                ImGui::TableNextColumn();
                {
                    DriverManager::DriverAge oldestAge = DriverManager::DriverAge::Current;
                    for (auto* d : group) {
                        if (static_cast<int>(d->ageCategory) > static_cast<int>(oldestAge)) {
                            oldestAge = d->ageCategory;
                        }
                    }
                    ImVec4 ageColor;
                    switch (oldestAge) {
                        case DriverManager::DriverAge::Current:
                            ageColor = ImVec4(0.2f, 0.8f, 0.2f, 1.0f);
                            break;
                        case DriverManager::DriverAge::Old:
                            ageColor = ImVec4(0.9f, 0.7f, 0.0f, 1.0f);
                            break;
                        case DriverManager::DriverAge::VeryOld:
                            ageColor = ImVec4(0.9f, 0.4f, 0.1f, 1.0f);
                            break;
                        default:
                            ageColor = ImVec4(0.5f, 0.5f, 0.5f, 1.0f);
                    }
                    ImGui::TextColored(ageColor, "%s", DriverManager::GetAgeText(oldestAge));
                }
                
                ImGui::TableNextColumn();
                // Show worst status in group
                DriverManager::DriverStatus worstStatus = DriverManager::DriverStatus::OK;
                for (auto* d : group) {
                    if (static_cast<int>(d->status) > static_cast<int>(worstStatus)) {
                        worstStatus = d->status;
                    }
                }
                ImGui::TextColored(getStatusColor(worstStatus), "%s", 
                    DriverManager::GetStatusText(worstStatus));
                
                // Render child rows if expanded
                if (isExpanded) {
                    int childIndex = 0;
                    for (auto* driver : group) {
                        ImGui::TableNextRow();
                        
                        ImGui::TableNextColumn();
                        bool isSelected = (state.selectedDriver == driver);
                        
                        ImGui::PushID(rowId++);
                        
                        // Indent child rows
                        ImGui::Indent(20.0f);
                        
                        // Build a meaningful label for the child row
                        std::string childLabel;
                        std::string desc = DriverManager::WideToUtf8(driver->deviceDescription);
                        std::string hwid = DriverManager::WideToUtf8(driver->hardwareId);
                        std::string instId = DriverManager::WideToUtf8(driver->deviceInstanceId);
                        std::string version = DriverManager::WideToUtf8(driver->driverVersion);
                        std::string date = DriverManager::WideToUtf8(driver->driverDate);
                        
                        // Strategy 1: Use description if different from name
                        if (!desc.empty() && desc != DriverManager::WideToUtf8(driver->deviceName)) {
                            childLabel = desc;
                        }
                        // Strategy 2: Use hardware ID subsystem part
                        else if (!hwid.empty() && hwid.length() > 5) {
                            // Try to extract a meaningful part (e.g., VEN_xxxx, DEV_xxxx)
                            childLabel = hwid;
                            if (childLabel.length() > 40) {
                                childLabel = childLabel.substr(0, 37) + "...";
                            }
                        }
                        // Strategy 3: Use version + date if available
                        else if (!version.empty() || !date.empty()) {
                            childLabel = "#" + std::to_string(childIndex + 1);
                            if (!version.empty()) {
                                childLabel += " (v" + version + ")";
                            }
                        }
                        // Strategy 4: Use a portion of instance ID
                        else if (!instId.empty()) {
                            // Show more of the instance ID for context
                            size_t firstSlash = instId.find('\\');
                            if (firstSlash != std::string::npos && firstSlash + 1 < instId.length()) {
                                childLabel = instId.substr(firstSlash + 1);
                            } else {
                                childLabel = instId;
                            }
                            if (childLabel.length() > 40) {
                                childLabel = childLabel.substr(0, 37) + "...";
                            }
                        }
                        // Strategy 5: Simple index fallback
                        else {
                            childLabel = "Instance #" + std::to_string(childIndex + 1);
                        }
                        
                        if (ImGui::Selectable(childLabel.c_str(), isSelected, 
                            ImGuiSelectableFlags_SpanAllColumns)) {
                            state.selectedDriver = driver;
                        }
                        
                        ImGui::Unindent(20.0f);
                        ImGui::PopID();
                        
                        ImGui::TableNextColumn();
                        ImGui::TextUnformatted(DriverManager::WideToUtf8(driver->manufacturer).c_str());
                        
                        ImGui::TableNextColumn();
                        ImGui::TextUnformatted(DriverManager::WideToUtf8(driver->driverVersion).c_str());
                        
                        ImGui::TableNextColumn();
                        ImGui::TextUnformatted(DriverManager::WideToUtf8(driver->driverDate).c_str());
                        
                        // Age column for child
                        ImGui::TableNextColumn();
                        {
                            ImVec4 ageColor;
                            switch (driver->ageCategory) {
                                case DriverManager::DriverAge::Current:
                                    ageColor = ImVec4(0.2f, 0.8f, 0.2f, 1.0f);
                                    break;
                                case DriverManager::DriverAge::Old:
                                    ageColor = ImVec4(0.9f, 0.7f, 0.0f, 1.0f);
                                    break;
                                case DriverManager::DriverAge::VeryOld:
                                    ageColor = ImVec4(0.9f, 0.4f, 0.1f, 1.0f);
                                    break;
                                default:
                                    ageColor = ImVec4(0.5f, 0.5f, 0.5f, 1.0f);
                            }
                            ImGui::TextColored(ageColor, "%s", DriverManager::GetAgeText(driver->ageCategory));
                        }
                        
                        ImGui::TableNextColumn();
                        ImGui::TextColored(getStatusColor(driver->status), "%s", 
                            DriverManager::GetStatusText(driver->status));
                        
                        childIndex++;
                    }
                }
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
        
        // Header with device name and close button
        ImGui::TextColored(ImVec4(0.4f, 0.7f, 1.0f, 1.0f), "D\xc3\xa9tails du pilote");
        ImGui::SameLine(detailsWidth - 35.0f);
        if (ImGui::Button("X", ImVec2(20, 20))) {
            state.selectedDriver = nullptr;
        }
        if (ImGui::IsItemHovered()) {
            ImGui::SetTooltip("Fermer les d\xc3\xa9tails");
        }
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
        
        // Age display with color coding
        ImGui::TextColored(ImVec4(0.6f, 0.6f, 0.7f, 1.0f), "\xc3\x82ge:");
        {
            ImVec4 ageColor;
            switch (d->ageCategory) {
                case DriverManager::DriverAge::Current:
                    ageColor = ImVec4(0.2f, 0.8f, 0.2f, 1.0f);
                    break;
                case DriverManager::DriverAge::Old:
                    ageColor = ImVec4(0.9f, 0.7f, 0.0f, 1.0f);
                    break;
                case DriverManager::DriverAge::VeryOld:
                    ageColor = ImVec4(0.9f, 0.4f, 0.1f, 1.0f);
                    break;
                default:
                    ageColor = ImVec4(0.5f, 0.5f, 0.5f, 1.0f);
            }
            std::string ageText = DriverManager::FormatAgeDays(d->driverAgeDays);
            if (d->ageCategory == DriverManager::DriverAge::VeryOld) {
                ageText += " (obsolete)";
            }
            ImGui::TextColored(ageColor, "%s", ageText.c_str());
        }
        ImGui::Spacing();
        
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
        ImGui::TextColored(ImVec4(0.6f, 0.6f, 0.7f, 1.0f), "Activ\xc3\xa9:");
        ImGui::Text("%s", d->isEnabled ? "Oui" : "Non");
        ImGui::Spacing();
        
        if (d->problemCode != 0) {
            ImGui::TextColored(ImVec4(0.6f, 0.6f, 0.7f, 1.0f), "Code probl\xc3\xa8me:");
            ImGui::TextColored(ImVec4(0.9f, 0.5f, 0.2f, 1.0f), "%u", d->problemCode);
            ImGui::Spacing();
        }
        
        ImGui::Separator();
        ImGui::Spacing();
        
        // Hardware ID (collapsible since it can be long)
        if (ImGui::CollapsingHeader("IDs mat\xc3\xa9riel")) {
            ImGui::TextColored(ImVec4(0.6f, 0.6f, 0.7f, 1.0f), "Hardware ID:");
            ImGui::TextWrapped("%s", DriverManager::WideToUtf8(d->hardwareId).c_str());
            ImGui::Spacing();
            
            ImGui::TextColored(ImVec4(0.6f, 0.6f, 0.7f, 1.0f), "Instance ID:");
            ImGui::TextWrapped("%s", DriverManager::WideToUtf8(d->deviceInstanceId).c_str());
        }
        
        ImGui::Spacing();
        ImGui::Separator();
        ImGui::Spacing();
        
        // Download update button (if update available)
        if (d->hasUpdate && !d->availableUpdate.downloadUrl.empty()) {
            ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(0.2f, 0.7f, 0.3f, 0.7f));
            ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(0.3f, 0.8f, 0.4f, 0.85f));
            ImGui::PushStyleColor(ImGuiCol_ButtonActive, ImVec4(0.4f, 0.9f, 0.5f, 1.0f));
            if (ImGui::Button("Telecharger MAJ", ImVec2(-1, 0))) {
                state.driverDownloader.QueueDownload(*d, d->availableUpdate.downloadUrl, false);
                state.showDownloadWindow = true;
                state.statusMessage = "Pilote ajoute a la file de telechargement";
            }
            ImGui::PopStyleColor(3);
            if (ImGui::IsItemHovered()) {
                ImGui::SetTooltip("Telecharger depuis Windows Update Catalog");
            }
            
            ImGui::TextColored(ImVec4(0.4f, 0.8f, 0.4f, 1.0f), "MAJ disponible: %s", 
                DriverManager::WideToUtf8(d->availableUpdate.newVersion).c_str());
            ImGui::Spacing();
        }
        
        // Download driver button
        ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(0.2f, 0.5f, 0.8f, 0.7f));
        ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(0.3f, 0.6f, 0.9f, 0.85f));
        ImGui::PushStyleColor(ImGuiCol_ButtonActive, ImVec4(0.4f, 0.7f, 1.0f, 1.0f));
        if (ImGui::Button("T\xc3\xa9l\xc3\xa9" "charger pilote \xe2\x96\xbc", ImVec2(-1, 0))) {
            ImGui::OpenPopup("DownloadDriverPopup");
        }
        ImGui::PopStyleColor(3);
        
        // Download popup menu
        if (ImGui::BeginPopup("DownloadDriverPopup")) {
            std::wstring mfrUrl = DriverManager::FindManufacturerUrl(d->manufacturer);
            
            // Direct manufacturer link (if known)
            if (!mfrUrl.empty()) {
                std::string menuLabel = "Site " + DriverManager::WideToUtf8(d->manufacturer);
                if (ImGui::MenuItem(menuLabel.c_str())) {
                    DriverManager::OpenUrl(mfrUrl);
                }
                if (ImGui::IsItemHovered()) {
                    ImGui::SetTooltip("Ouvrir la page de t\xc3\xa9l\xc3\xa9" "chargement officielle");
                }
                ImGui::Separator();
            }
            
            // Google search
            if (ImGui::MenuItem("Rechercher sur Google")) {
                DriverManager::SearchGoogleForDriver(d->manufacturer, d->deviceName);
            }
            if (ImGui::IsItemHovered()) {
                ImGui::SetTooltip("Rechercher ce pilote sur Google");
            }
            
            // TousLesDrivers search
            if (ImGui::MenuItem("Rechercher sur TousLesDrivers.com")) {
                DriverManager::SearchTousLesDrivers(d->deviceName);
            }
            if (ImGui::IsItemHovered()) {
                ImGui::SetTooltip("Rechercher ce pilote sur TousLesDrivers.com");
            }
            
            ImGui::EndPopup();
        }
        
        ImGui::EndChild();
    }
}

// Render details window
void RenderDetailsWindow(AppState& state) {
    if (!state.showDetailsWindow || !state.selectedDriver) return;
    
    ImGui::SetNextWindowSize(ImVec2(500, 400), ImGuiCond_FirstUseEver);
    if (ImGui::Begin("D\xc3\xa9tails du pilote", &state.showDetailsWindow)) {
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
            addRow("Code probl\xc3\xa8me", std::to_string(d->problemCode));
            addRow("Activ\xc3\xa9", d->isEnabled ? "Oui" : "Non");
            
            ImGui::EndTable();
        }
    }
    ImGui::End();
}

// Render update progress window
void RenderUpdateProgressWindow(AppState& state) {
    if (!state.showUpdateProgressWindow) return;
    
    // Check if window was closed while checking (user clicked X)
    bool windowOpen = state.showUpdateProgressWindow;
    
    ImGui::SetNextWindowSize(ImVec2(500, 200), ImGuiCond_FirstUseEver);
    ImGuiWindowFlags flags = ImGuiWindowFlags_NoResize | ImGuiWindowFlags_NoCollapse;
    
    std::string windowTitle = "V\xc3\xa9rification des mises \xc3\xa0 jour";
    if (state.updateSource == 1) {
        windowTitle += " - TousLesDrivers.com";
    } else if (state.updateSource == 2) {
        windowTitle += " - Windows Update Catalog";
    }
    
    if (ImGui::Begin(windowTitle.c_str(), &windowOpen, flags)) {
        // Source info
        if (state.updateSource == 1) {
            ImGui::TextColored(ImVec4(0.4f, 0.8f, 0.4f, 1.0f), "Source: TousLesDrivers.com");
        } else if (state.updateSource == 2) {
            ImGui::TextColored(ImVec4(0.4f, 0.7f, 1.0f, 1.0f), "Source: Windows Update Catalog");
        }
        
        ImGui::Separator();
        ImGui::Spacing();
        
        if (state.isCheckingUpdates) {
            // Progress bar
            char progressText[64];
            snprintf(progressText, sizeof(progressText), "%d / %d pilotes (%.0f%%)", 
                state.driversChecked, state.totalDriversToCheck, state.updateCheckProgress * 100.0f);
            ImGui::ProgressBar(state.updateCheckProgress, ImVec2(-1, 0), progressText);
            
            ImGui::Spacing();
            
            // Current item
            ImGui::TextColored(ImVec4(0.7f, 0.7f, 0.7f, 1.0f), "V\xc3\xa9rification en cours:");
            ImGui::TextWrapped("%s", DriverManager::WideToUtf8(state.currentUpdateItem).c_str());
            
            ImGui::Spacing();
            ImGui::Separator();
            ImGui::Spacing();
            
            // Cancel button
            if (ImGui::Button("Annuler", ImVec2(120, 0))) {
                state.cancelUpdateCheck = true;
                state.updateChecker.CancelCheck();
                state.isCheckingUpdates = false;
                state.statusMessage = "V\xc3\xa9rification annul\xc3\xa9" "e";
            }
        } else {
            // Completed
            ImGui::TextColored(ImVec4(0.4f, 0.9f, 0.4f, 1.0f), "V\xc3\xa9rification termin\xc3\xa9" "e!");
            ImGui::Spacing();
            
            if (state.updatesFound > 0) {
                ImGui::TextColored(ImVec4(0.9f, 0.8f, 0.2f, 1.0f), "%d mise(s) \xc3\xa0 jour trouv\xc3\xa9" "e(s)", state.updatesFound);
            } else {
                ImGui::Text("Tous les pilotes sont \xc3\xa0 jour.");
            }
            
            ImGui::Spacing();
            ImGui::Separator();
            ImGui::Spacing();
            
            if (ImGui::Button("Fermer", ImVec2(120, 0))) {
                state.showUpdateProgressWindow = false;
            }
        }
    }
    ImGui::End();
    
    // Handle window close via X button
    if (!windowOpen && state.showUpdateProgressWindow) {
        state.showUpdateProgressWindow = false;
        if (state.isCheckingUpdates) {
            state.cancelUpdateCheck = true;
            state.updateChecker.CancelCheck();
            state.isCheckingUpdates = false;
            state.statusMessage = "V\xc3\xa9rification annul\xc3\xa9" "e";
        }
    }
}

// Render about window
void RenderAboutWindow(AppState& state) {
    if (!state.showAboutWindow) return;
    
    ImGui::SetNextWindowSize(ImVec2(400, 200), ImGuiCond_FirstUseEver);
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

// Helper to format file size
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
        snprintf(buf, sizeof(buf), "%llu %s", bytes, units[unitIndex]);
    } else {
        snprintf(buf, sizeof(buf), "%.2f %s", size, units[unitIndex]);
    }
    return buf;
}

// Render DriverStore cleanup window
void RenderDriverStoreCleanupWindow(AppState& state) {
    if (!state.showDriverStoreCleanup) return;
    
    // Handle deferred refresh (must be done BEFORE getting entries reference)
    if (state.needsDriverStoreRefresh) {
        state.needsDriverStoreRefresh = false;
        state.driverStoreCleanup.ScanDriverStore();
        state.statusMessage = std::to_string(state.lastDeletedCount) + " dossier(s) supprim\xc3\xa9(s)";
    }
    
    ImGui::SetNextWindowSize(ImVec2(900, 550), ImGuiCond_FirstUseEver);
    if (ImGui::Begin("Nettoyage DriverStore - Anciennes versions", &state.showDriverStoreCleanup)) {
        
        // Header info
        ImGui::TextColored(ImVec4(0.4f, 0.7f, 1.0f, 1.0f), "Anciennes versions de pilotes dans FileRepository");
        ImGui::Separator();
        ImGui::Spacing();
        
        ImGui::TextWrapped(
            "Cet outil trouve les ANCIENNES VERSIONS de pilotes qui ont " "\xc3\xa9" "t" "\xc3\xa9" " remplac" "\xc3\xa9" "es par des versions plus r" "\xc3\xa9" "centes. "
            "Ces dossiers ne sont plus utilis" "\xc3\xa9" "s et peuvent " "\xc3\xaa" "tre supprim" "\xc3\xa9" "s en toute s" "\xc3\xa9" "curit" "\xc3\xa9" ".");
        ImGui::Spacing();
        
        // Statistics
        auto& entries = state.driverStoreCleanup.GetEntries();
        int totalCount = static_cast<int>(entries.size());
        int selectedCount = 0;
        
        for (const auto& e : entries) {
            if (e.isSelected) selectedCount++;
        }
        
        uint64_t selectedSize = state.driverStoreCleanup.GetSelectedSize();
        uint64_t totalOrphanedSize = state.driverStoreCleanup.GetTotalOrphanedSize();
        
        if (totalCount == 0) {
            ImGui::TextColored(ImVec4(0.4f, 0.9f, 0.4f, 1.0f), 
                "Aucune ancienne version trouv" "\xc3\xa9" "e - Votre DriverStore est propre !");
        } else {
            ImGui::Text("Anciennes versions trouv" "\xc3\xa9" "es: %d | S" "\xc3\xa9" "lectionn" "\xc3\xa9" "s: %d", 
                totalCount, selectedCount);
            ImGui::Text("Espace lib\xc3\xa9rable (s\xc3\xa9lection): %s | Total lib\xc3\xa9rable: %s",
                FormatFileSize(selectedSize).c_str(), FormatFileSize(totalOrphanedSize).c_str());
        }
        
        ImGui::Spacing();
        
        // Buttons
        if (ImGui::Button("Actualiser")) {
            state.driverStoreCleanup.ScanDriverStore();
        }
        ImGui::SameLine();
        
        ImGui::BeginDisabled(totalCount == 0);
        if (ImGui::Button("Tout s\xc3\xa9lectionner")) {
            for (auto& e : entries) {
                e.isSelected = true;
            }
        }
        ImGui::SameLine();
        
        if (ImGui::Button("Tout d\xc3\xa9s\xc3\xa9lectionner")) {
            for (auto& e : entries) {
                e.isSelected = false;
            }
        }
        ImGui::EndDisabled();
        
        ImGui::SameLine();
        
        ImGui::BeginDisabled(selectedCount == 0 || state.isCleaningDriverStore);
        ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(0.8f, 0.2f, 0.2f, 0.7f));
        ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(0.9f, 0.3f, 0.3f, 0.85f));
        ImGui::PushStyleColor(ImGuiCol_ButtonActive, ImVec4(1.0f, 0.4f, 0.4f, 1.0f));
        if (ImGui::Button("Supprimer la s\xc3\xa9lection")) {
            ImGui::OpenPopup("ConfirmCleanup");
        }
        ImGui::PopStyleColor(3);
        ImGui::EndDisabled();
        
        // Confirmation popup
        if (ImGui::BeginPopupModal("ConfirmCleanup", nullptr, ImGuiWindowFlags_AlwaysAutoResize)) {
            ImGui::Text("Voulez-vous vraiment supprimer %d dossier(s) de pilotes ?", selectedCount);
            ImGui::Text("Espace \xc3\xa0 lib\xc3\xa9rer: %s", FormatFileSize(selectedSize).c_str());
            ImGui::Separator();
            ImGui::TextColored(ImVec4(0.4f, 0.9f, 0.4f, 1.0f), 
                "Ces dossiers contiennent d'anciennes versions qui ne sont plus utilis" "\xc3\xa9" "es.");
            ImGui::Spacing();
            
            if (ImGui::Button("Oui, supprimer", ImVec2(120, 0))) {
                // Close popup first, then perform deletion
                ImGui::CloseCurrentPopup();
                
                // Perform deletion
                state.isCleaningDriverStore = true;
                state.lastDeletedCount = state.driverStoreCleanup.DeleteSelectedPackages();
                state.isCleaningDriverStore = false;
                
                // Schedule refresh for next frame
                state.needsDriverStoreRefresh = true;
            }
            ImGui::SameLine();
            if (ImGui::Button("Annuler", ImVec2(120, 0))) {
                ImGui::CloseCurrentPopup();
            }
            ImGui::EndPopup();
        }
        
        ImGui::Spacing();
        ImGui::Separator();
        ImGui::Spacing();
        
        // Table of orphaned drivers
        if (totalCount > 0 && ImGui::BeginTable("DriverStoreTable", 7, 
            ImGuiTableFlags_Borders | ImGuiTableFlags_RowBg | ImGuiTableFlags_Resizable |
            ImGuiTableFlags_ScrollY | ImGuiTableFlags_Sortable)) {
            
            ImGui::TableSetupScrollFreeze(0, 1);
            ImGui::TableSetupColumn("", ImGuiTableColumnFlags_WidthFixed, 30.0f);
            ImGui::TableSetupColumn("Nom INF", ImGuiTableColumnFlags_WidthFixed, 150.0f);
            ImGui::TableSetupColumn("Fournisseur", ImGuiTableColumnFlags_WidthStretch);
            ImGui::TableSetupColumn("Classe", ImGuiTableColumnFlags_WidthFixed, 80.0f);
            ImGui::TableSetupColumn("Date", ImGuiTableColumnFlags_WidthFixed, 90.0f);
            ImGui::TableSetupColumn("Version", ImGuiTableColumnFlags_WidthFixed, 110.0f);
            ImGui::TableSetupColumn("Taille", ImGuiTableColumnFlags_WidthFixed, 90.0f);
            ImGui::TableHeadersRow();
            
            for (size_t i = 0; i < entries.size(); i++) {
                auto& entry = entries[i];
                ImGui::TableNextRow();
                
                // Checkbox column
                ImGui::TableNextColumn();
                ImGui::PushID(static_cast<int>(i));
                ImGui::Checkbox("##sel", &entry.isSelected);
                
                // INF name
                ImGui::TableNextColumn();
                ImGui::TextUnformatted(DriverManager::WideToUtf8(entry.infName).c_str());
                if (ImGui::IsItemHovered()) {
                    ImGui::SetTooltip("Dossier: %s", DriverManager::WideToUtf8(entry.folderName).c_str());
                }
                
                // Provider
                ImGui::TableNextColumn();
                ImGui::TextUnformatted(DriverManager::WideToUtf8(entry.providerName).c_str());
                
                // Class
                ImGui::TableNextColumn();
                ImGui::TextUnformatted(DriverManager::WideToUtf8(entry.className).c_str());
                
                // Date
                ImGui::TableNextColumn();
                ImGui::TextUnformatted(DriverManager::WideToUtf8(entry.driverDate).c_str());
                
                // Version
                ImGui::TableNextColumn();
                ImGui::TextUnformatted(DriverManager::WideToUtf8(entry.driverVersion).c_str());
                
                // Size
                ImGui::TableNextColumn();
                if (entry.folderSize > 0) {
                    // Color code large files
                    if (entry.folderSize > 100 * 1024 * 1024) { // > 100 MB
                        ImGui::TextColored(ImVec4(0.9f, 0.4f, 0.1f, 1.0f), "%s", 
                            FormatFileSize(entry.folderSize).c_str());
                    } else if (entry.folderSize > 10 * 1024 * 1024) { // > 10 MB
                        ImGui::TextColored(ImVec4(0.9f, 0.7f, 0.2f, 1.0f), "%s", 
                            FormatFileSize(entry.folderSize).c_str());
                    } else {
                        ImGui::TextUnformatted(FormatFileSize(entry.folderSize).c_str());
                    }
                } else {
                    ImGui::TextColored(ImVec4(0.5f, 0.5f, 0.5f, 1.0f), "N/A");
                }
                
                ImGui::PopID();
            }
            
            ImGui::EndTable();
        }
    }
    ImGui::End();
}

// Render BSOD Analyzer window
void RenderBSODAnalyzerWindow(AppState& state) {
    if (!state.showBSODAnalyzer) return;
    
    ImGui::SetNextWindowSize(ImVec2(1000, 600), ImGuiCond_FirstUseEver);
    if (ImGui::Begin("Analyse des BSOD - Pilotes probl\xc3\xa9matiques", &state.showBSODAnalyzer)) {
        
        // Header info
        ImGui::TextColored(ImVec4(0.9f, 0.4f, 0.4f, 1.0f), "D\xc3\xa9" "tection des pilotes causant des \xc3\xa9" "crans bleus (BSOD)");
        ImGui::Separator();
        ImGui::Spacing();
        
        ImGui::TextWrapped(
            "Cet outil analyse les fichiers minidump dans C:\\Windows\\Minidump pour identifier "
            "les pilotes responsables des plantages syst\xc3\xa8" "me.");
        ImGui::Spacing();
        
        // Check if minidump folder exists
        if (!state.bsodAnalyzer.MinidumpFolderExists()) {
            ImGui::TextColored(ImVec4(0.4f, 0.9f, 0.4f, 1.0f), 
                "Aucun dossier Minidump trouv\xc3\xa9 - Bonne nouvelle, aucun BSOD r\xc3\xa9" "cent!");
            ImGui::Spacing();
            ImGui::TextWrapped(
                "Windows cr\xc3\xa9" "e des fichiers minidump quand un BSOD survient. "
                "L'absence de ce dossier signifie qu'aucun \xc3\xa9" "cran bleu n'a eu lieu r\xc3\xa9" "cemment.");
        } else {
            // Scan button
            if (!state.isScanningBSOD) {
                if (ImGui::Button("Scanner les minidumps", ImVec2(200, 30))) {
                    state.isScanningBSOD = true;
                    state.bsodScanProgress = 0;
                    state.bsodScanTotal = 0;
                    
                    state.bsodAnalyzer.SetProgressCallback([&state](int current, int total, const std::wstring& item) {
                        state.bsodScanProgress = current;
                        state.bsodScanTotal = total;
                        state.bsodCurrentItem = item;
                    });
                    
                    // Use std::async instead of detached thread for safer lifecycle management
                    state.bsodScanFuture = std::async(std::launch::async, [&state]() {
                        state.bsodAnalyzer.ScanMinidumps();
                        state.isScanningBSOD = false;
                    });
                }
                ImGui::SameLine();
                if (ImGui::Button("Ouvrir dossier Minidump", ImVec2(200, 30))) {
                    ShellExecuteW(nullptr, L"explore", state.bsodAnalyzer.GetMinidumpPath().c_str(), 
                        nullptr, nullptr, SW_SHOWNORMAL);
                }
            } else {
                // Progress bar during scan
                ImGui::Text("Analyse en cours...");
                if (state.bsodScanTotal > 0) {
                    float progress = (float)state.bsodScanProgress / (float)state.bsodScanTotal;
                    ImGui::ProgressBar(progress, ImVec2(-1, 0));
                    ImGui::Text("%d / %d - %s", state.bsodScanProgress, state.bsodScanTotal,
                        DriverManager::WideToUtf8(state.bsodCurrentItem).c_str());
                }
            }
            
            ImGui::Spacing();
            ImGui::Separator();
            ImGui::Spacing();
            
            // Show results
            const auto& crashes = state.bsodAnalyzer.GetCrashes();
            auto problematicDrivers = state.bsodAnalyzer.GetProblematicDrivers();
            
            if (crashes.empty() && !state.isScanningBSOD) {
                auto error = state.bsodAnalyzer.GetLastError();
                if (!error.empty()) {
                    ImGui::TextColored(ImVec4(0.9f, 0.7f, 0.3f, 1.0f), "%s", 
                        DriverManager::WideToUtf8(error).c_str());
                }
            } else if (!crashes.empty()) {
                // Tab bar for different views
                if (ImGui::BeginTabBar("BSODTabs")) {
                    
                    // Tab 1: Problematic drivers summary
                    if (ImGui::BeginTabItem("Pilotes probl\xc3\xa9matiques")) {
                        ImGui::Spacing();
                        
                        if (problematicDrivers.empty()) {
                            ImGui::TextColored(ImVec4(0.9f, 0.7f, 0.3f, 1.0f), 
                                "Aucun pilote identifi\xc3\xa9 comme responsable dans les minidumps.");
                            ImGui::TextWrapped(
                                "Les minidumps ne contiennent pas toujours l'information sur le pilote fautif.");
                        } else {
                            ImGui::TextColored(ImVec4(0.9f, 0.5f, 0.5f, 1.0f), 
                                "%d pilote(s) identifi\xc3\xa9(s) comme probl\xc3\xa9matique(s):", 
                                (int)problematicDrivers.size());
                            ImGui::Spacing();
                            
                            if (ImGui::BeginTable("ProblematicDriversTable", 5, 
                                ImGuiTableFlags_Borders | ImGuiTableFlags_RowBg | ImGuiTableFlags_Resizable)) {
                                
                                ImGui::TableSetupColumn("Pilote", ImGuiTableColumnFlags_WidthFixed, 180);
                                ImGui::TableSetupColumn("Crashes", ImGuiTableColumnFlags_WidthFixed, 70);
                                ImGui::TableSetupColumn("Codes d'erreur", ImGuiTableColumnFlags_WidthStretch);
                                ImGui::TableSetupColumn("Dernier crash", ImGuiTableColumnFlags_WidthFixed, 120);
                                ImGui::TableSetupColumn("Action", ImGuiTableColumnFlags_WidthFixed, 120);
                                ImGui::TableHeadersRow();
                                
                                for (const auto& driver : problematicDrivers) {
                                    ImGui::TableNextRow();
                                    
                                    // Driver name
                                    ImGui::TableNextColumn();
                                    if (driver.crashCount >= 3) {
                                        ImGui::TextColored(ImVec4(0.9f, 0.3f, 0.3f, 1.0f), "%s",
                                            DriverManager::WideToUtf8(driver.driverName).c_str());
                                    } else {
                                        ImGui::TextColored(ImVec4(0.9f, 0.7f, 0.3f, 1.0f), "%s",
                                            DriverManager::WideToUtf8(driver.driverName).c_str());
                                    }
                                    if (ImGui::IsItemHovered() && !driver.driverPath.empty()) {
                                        ImGui::SetTooltip("%s", DriverManager::WideToUtf8(driver.driverPath).c_str());
                                    }
                                    
                                    // Crash count
                                    ImGui::TableNextColumn();
                                    if (driver.crashCount >= 3) {
                                        ImGui::TextColored(ImVec4(0.9f, 0.3f, 0.3f, 1.0f), "%d", driver.crashCount);
                                    } else {
                                        ImGui::Text("%d", driver.crashCount);
                                    }
                                    
                                    // Bug check codes
                                    ImGui::TableNextColumn();
                                    std::string codes;
                                    std::set<uint32_t> uniqueCodes(driver.bugCheckCodes.begin(), driver.bugCheckCodes.end());
                                    for (auto code : uniqueCodes) {
                                        if (!codes.empty()) codes += ", ";
                                        codes += DriverManager::WideToUtf8(
                                            DriverManager::BSODAnalyzer::GetBugCheckName(code));
                                    }
                                    ImGui::TextWrapped("%s", codes.c_str());
                                    
                                    // Last crash date
                                    ImGui::TableNextColumn();
                                    char dateBuf[32];
                                    snprintf(dateBuf, sizeof(dateBuf), "%02d/%02d/%04d",
                                        driver.lastCrash.wDay, driver.lastCrash.wMonth, driver.lastCrash.wYear);
                                    ImGui::TextUnformatted(dateBuf);
                                    
                                    // Action buttons
                                    ImGui::TableNextColumn();
                                    ImGui::PushID(DriverManager::WideToUtf8(driver.driverName).c_str());
                                    if (ImGui::SmallButton("Mettre \xc3\xa0 jour")) {
                                        // Search for update
                                        std::wstring searchUrl = L"https://www.google.com/search?q=" + 
                                            driver.driverName + L"+driver+download";
                                        ShellExecuteW(nullptr, L"open", searchUrl.c_str(), nullptr, nullptr, SW_SHOWNORMAL);
                                    }
                                    ImGui::PopID();
                                }
                                
                                ImGui::EndTable();
                            }
                        }
                        ImGui::EndTabItem();
                    }
                    
                    // Tab 2: All crashes list
                    char tabLabel[64];
                    snprintf(tabLabel, sizeof(tabLabel), "Tous les crashes (%d)", (int)crashes.size());
                    if (ImGui::BeginTabItem(tabLabel)) {
                        ImGui::Spacing();
                        
                        if (ImGui::BeginTable("CrashesTable", 5,
                            ImGuiTableFlags_Borders | ImGuiTableFlags_RowBg | ImGuiTableFlags_Resizable |
                            ImGuiTableFlags_ScrollY, ImVec2(0, 350))) {
                            
                            ImGui::TableSetupColumn("Date", ImGuiTableColumnFlags_WidthFixed, 100);
                            ImGui::TableSetupColumn("Code erreur", ImGuiTableColumnFlags_WidthFixed, 200);
                            ImGui::TableSetupColumn("Description", ImGuiTableColumnFlags_WidthStretch);
                            ImGui::TableSetupColumn("Pilote fautif", ImGuiTableColumnFlags_WidthFixed, 150);
                            ImGui::TableSetupColumn("Fichier", ImGuiTableColumnFlags_WidthFixed, 150);
                            ImGui::TableHeadersRow();
                            
                            for (const auto& crash : crashes) {
                                ImGui::TableNextRow();
                                
                                // Date
                                ImGui::TableNextColumn();
                                char dateBuf[32];
                                snprintf(dateBuf, sizeof(dateBuf), "%02d/%02d/%04d",
                                    crash.crashTime.wDay, crash.crashTime.wMonth, crash.crashTime.wYear);
                                ImGui::TextUnformatted(dateBuf);
                                
                                // Bug check code
                                ImGui::TableNextColumn();
                                ImGui::TextColored(ImVec4(0.9f, 0.5f, 0.5f, 1.0f), "%s",
                                    DriverManager::WideToUtf8(crash.bugCheckName).c_str());
                                
                                // Description
                                ImGui::TableNextColumn();
                                ImGui::TextWrapped("%s", 
                                    DriverManager::WideToUtf8(crash.bugCheckDescription).c_str());
                                
                                // Faulting module
                                ImGui::TableNextColumn();
                                if (!crash.faultingModule.empty()) {
                                    ImGui::TextColored(ImVec4(0.9f, 0.7f, 0.3f, 1.0f), "%s",
                                        DriverManager::WideToUtf8(crash.faultingModule).c_str());
                                } else {
                                    ImGui::TextColored(ImVec4(0.5f, 0.5f, 0.5f, 1.0f), "Non identifi\xc3\xa9");
                                }
                                
                                // Dump file
                                ImGui::TableNextColumn();
                                ImGui::TextUnformatted(
                                    DriverManager::WideToUtf8(crash.dumpFileName).c_str());
                                if (ImGui::IsItemHovered()) {
                                    ImGui::SetTooltip("Taille: %s\nOS: %s",
                                        FormatFileSize(crash.dumpFileSize).c_str(),
                                        DriverManager::WideToUtf8(crash.osVersion).c_str());
                                }
                            }
                            
                            ImGui::EndTable();
                        }
                        ImGui::EndTabItem();
                    }
                    
                    // Tab 3: Recommendations
                    if (ImGui::BeginTabItem("Recommandations")) {
                        ImGui::Spacing();
                        
                        ImGui::TextColored(ImVec4(0.4f, 0.8f, 0.4f, 1.0f), "Actions recommand\xc3\xa9" "es :");
                        ImGui::Spacing();
                        
                        ImGui::BulletText("Mettre \xc3\xa0 jour les pilotes identifi\xc3\xa9s comme probl\xc3\xa9matiques");
                        ImGui::BulletText("V\xc3\xa9rifier les mises \xc3\xa0 jour Windows Update");
                        ImGui::BulletText("Utiliser 'Mes Drivers' de TousLesDrivers.com");
                        ImGui::BulletText("Si un pilote continue de causer des probl\xc3\xa8mes, essayer un rollback");
                        
                        ImGui::Spacing();
                        ImGui::Separator();
                        ImGui::Spacing();
                        
                        ImGui::TextColored(ImVec4(0.9f, 0.7f, 0.3f, 1.0f), "Codes d'erreur fr\xc3\xa9quents :");
                        ImGui::Spacing();
                        
                        ImGui::TextWrapped(
                            "DRIVER_IRQL_NOT_LESS_OR_EQUAL (0xD1) - Pilote acc\xc3\xa9" "dant \xc3\xa0 une mauvaise adresse m\xc3\xa9" "moire");
                        ImGui::Spacing();
                        ImGui::TextWrapped(
                            "VIDEO_TDR_FAILURE (0x116) - Pilote graphique ne r\xc3\xa9" "pondant pas");
                        ImGui::Spacing();
                        ImGui::TextWrapped(
                            "SYSTEM_SERVICE_EXCEPTION (0x3B) - Exception dans un service syst\xc3\xa8" "me");
                        ImGui::Spacing();
                        ImGui::TextWrapped(
                            "KERNEL_SECURITY_CHECK_FAILURE (0x139) - Corruption de donn\xc3\xa9" "es d\xc3\xa9" "tect\xc3\xa9" "e");
                        
                        ImGui::EndTabItem();
                    }
                    
                    ImGui::EndTabBar();
                }
            }
        }
    }
    ImGui::End();
}

// Render update help window
void RenderUpdateHelpWindow(AppState& state) {
    if (!state.showUpdateHelpWindow) return;
    
    ImGui::SetNextWindowSize(ImVec2(580, 520), ImGuiCond_FirstUseEver);
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
        
        ImGui::TextWrapped(
            "1. Cliquez sur le bouton ci-dessous pour ouvrir la page Mes Drivers");
        ImGui::Spacing();
        
        ImGui::TextWrapped(
            "2. T\xc3\xa9" "l\xc3\xa9" "chargez et ex\xc3\xa9" "cutez l'outil de d\xc3\xa9" "tection (DriversCloud.exe)");
        ImGui::Spacing();
        
        ImGui::TextWrapped(
            "3. L'outil analyse automatiquement votre PC et identifie tous vos "
            "composants mat\xc3\xa9riels ainsi que les versions de vos pilotes");
        ImGui::Spacing();
        
        ImGui::TextWrapped(
            "4. Une page web s'ouvre avec la liste compl\xc3\xa8te de vos pilotes et "
            "les mises \xc3\xa0 jour disponibles");
        ImGui::Spacing();
        
        ImGui::TextWrapped(
            "5. T\xc3\xa9" "l\xc3\xa9" "chargez les pilotes n\xc3\xa9" "cessaires directement depuis leur site");
        
        ImGui::Spacing();
        ImGui::Separator();
        ImGui::Spacing();
        
        ImGui::TextColored(ImVec4(0.5f, 0.7f, 1.0f, 1.0f), "Avantages de Mes Drivers :");
        ImGui::Spacing();
        
        ImGui::BulletText("D\xc3\xa9tection automatique de tous vos composants");
        ImGui::BulletText("Identification pr\xc3\xa9" "cise des versions install\xc3\xa9" "es");
        ImGui::BulletText("Liens directs vers les pilotes officiels");
        ImGui::BulletText("Service gratuit et sans inscription");
        ImGui::BulletText("Base de donn\xc3\xa9" "es compl\xc3\xa8" "te et \xc3\xa0 jour");
        
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

// Render download manager window
void RenderDownloadWindow(AppState& state) {
    if (!state.showDownloadWindow) return;
    
    ImGui::SetNextWindowSize(ImVec2(800, 500), ImGuiCond_FirstUseEver);
    if (ImGui::Begin("Gestionnaire de telechargements", &state.showDownloadWindow)) {
        
        auto tasks = state.driverDownloader.GetAllTasks();
        
        int queued = state.driverDownloader.GetQueuedCount();
        int active = state.driverDownloader.GetActiveCount();
        int completed = state.driverDownloader.GetCompletedCount();
        int failed = state.driverDownloader.GetFailedCount();
        
        ImGui::TextColored(ImVec4(0.4f, 0.7f, 1.0f, 1.0f), "File d'attente des pilotes");
        ImGui::Separator();
        ImGui::Spacing();
        
        ImGui::Text("En attente: %d | Actifs: %d | Termines: %d | Echecs: %d", queued, active, completed, failed);
        ImGui::Spacing();
        
        bool isDownloading = state.driverDownloader.IsDownloading();
        bool isPaused = state.driverDownloader.IsPaused();
        
        if (!isDownloading && queued > 0) {
            if (ImGui::Button("Demarrer")) state.driverDownloader.StartDownloads();
        } else if (isDownloading && !isPaused) {
            if (ImGui::Button("Pause")) state.driverDownloader.PauseDownloads();
        } else if (isPaused) {
            if (ImGui::Button("Reprendre")) state.driverDownloader.ResumeDownloads();
        }
        
        ImGui::SameLine();
        ImGui::BeginDisabled(!isDownloading && queued == 0);
        if (ImGui::Button("Tout annuler")) state.driverDownloader.CancelAll();
        ImGui::EndDisabled();
        
        ImGui::SameLine();
        if (ImGui::Button("Nettoyer")) state.driverDownloader.ClearCompleted();
        
        ImGui::Spacing();
        ImGui::Separator();
        ImGui::Spacing();
        
        ImGui::Checkbox("Creer point de restauration", &state.createRestorePoint);
        if (ImGui::IsItemHovered()) {
            ImGui::SetTooltip("Recommande pour pouvoir revenir en arriere");
        }
        
        ImGui::Spacing();
        
        int readyCount = 0;
        for (const auto& task : tasks) {
            if (task.state == DriverManager::DownloadState::ReadyToInstall && task.selected) readyCount++;
        }
        
        ImGui::BeginDisabled(readyCount == 0);
        ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(0.2f, 0.6f, 0.2f, 0.7f));
        ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(0.3f, 0.7f, 0.3f, 0.85f));
        ImGui::PushStyleColor(ImGuiCol_ButtonActive, ImVec4(0.4f, 0.8f, 0.4f, 1.0f));
        char installBtn[64];
        snprintf(installBtn, sizeof(installBtn), "Installer %d pilote(s)", readyCount);
        if (ImGui::Button(installBtn, ImVec2(200, 0))) {
            DriverManager::InstallOptions options;
            options.createRestorePoint = state.createRestorePoint;
            state.driverDownloader.InstallAllReady(options);
        }
        ImGui::PopStyleColor(3);
        ImGui::EndDisabled();
        
        ImGui::Spacing();
        ImGui::Separator();
        ImGui::Spacing();
        
        if (tasks.empty()) {
            ImGui::TextColored(ImVec4(0.5f, 0.5f, 0.5f, 1.0f), "Aucun telechargement.\nSelectionnez un pilote avec MAJ disponible.");
        } else if (ImGui::BeginTable("Downloads", 5, ImGuiTableFlags_Borders | ImGuiTableFlags_RowBg | ImGuiTableFlags_ScrollY)) {
            ImGui::TableSetupScrollFreeze(0, 1);
            ImGui::TableSetupColumn("Pilote", ImGuiTableColumnFlags_WidthStretch);
            ImGui::TableSetupColumn("Version", ImGuiTableColumnFlags_WidthFixed, 100.0f);
            ImGui::TableSetupColumn("Progression", ImGuiTableColumnFlags_WidthFixed, 120.0f);
            ImGui::TableSetupColumn("Etat", ImGuiTableColumnFlags_WidthFixed, 100.0f);
            ImGui::TableSetupColumn("Actions", ImGuiTableColumnFlags_WidthFixed, 80.0f);
            ImGui::TableHeadersRow();
            
            int idx = 0;
            for (auto& task : tasks) {
                ImGui::TableNextRow();
                ImGui::PushID(idx++);
                
                ImGui::TableNextColumn();
                ImGui::TextWrapped("%s", DriverManager::WideToUtf8(task.deviceName).c_str());
                
                ImGui::TableNextColumn();
                ImGui::Text("%s", DriverManager::WideToUtf8(task.newVersion).c_str());
                
                ImGui::TableNextColumn();
                ImGui::ProgressBar(task.progress, ImVec2(-1, 0));
                
                ImGui::TableNextColumn();
                ImVec4 col = ImVec4(0.6f, 0.6f, 0.6f, 1.0f);
                if (task.state == DriverManager::DownloadState::Completed) col = ImVec4(0.2f, 0.8f, 0.2f, 1.0f);
                else if (task.state == DriverManager::DownloadState::Failed) col = ImVec4(0.9f, 0.2f, 0.2f, 1.0f);
                else if (task.state == DriverManager::DownloadState::Downloading) col = ImVec4(0.3f, 0.6f, 0.9f, 1.0f);
                else if (task.state == DriverManager::DownloadState::ReadyToInstall) col = ImVec4(0.9f, 0.7f, 0.2f, 1.0f);
                ImGui::TextColored(col, "%s", DriverManager::WideToUtf8(DriverManager::GetStateText(task.state)).c_str());
                
                ImGui::TableNextColumn();
                if (task.state == DriverManager::DownloadState::Failed) {
                    if (ImGui::SmallButton("Retry")) state.driverDownloader.RetryTask(task.taskId);
                } else if (task.state == DriverManager::DownloadState::ReadyToInstall) {
                    if (ImGui::SmallButton("Install")) {
                        DriverManager::InstallOptions opt;
                        opt.createRestorePoint = state.createRestorePoint;
                        state.driverDownloader.InstallDriver(task.taskId, opt);
                    }
                } else if (task.state == DriverManager::DownloadState::Queued) {
                    if (ImGui::SmallButton("Retirer")) state.driverDownloader.RemoveFromQueue(task.taskId);
                }
                
                ImGui::PopID();
            }
            ImGui::EndTable();
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
    // Check admin status
    g_isAdmin = IsRunningAsAdmin();
    
    // Create window class
    WNDCLASSEXW wc = { 
        sizeof(wc), CS_CLASSDC, WndProc, 0L, 0L, 
        GetModuleHandle(nullptr), nullptr, nullptr, nullptr, nullptr, 
        L"DriverManagerClass", nullptr 
    };
    RegisterClassExW(&wc);

    // Create window - add admin indicator to title if running as admin
    const wchar_t* windowTitle = g_isAdmin ? L"Driver Manager [Administrateur]" : L"Driver Manager";
    HWND hwnd = CreateWindowW(
        wc.lpszClassName, windowTitle, 
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

    // Load font with extended glyph ranges for French characters
    ImFontConfig fontConfig;
    fontConfig.OversampleH = 2;
    fontConfig.OversampleV = 2;
    
    // Build glyph ranges that include Latin Extended characters
    static const ImWchar ranges[] = {
        0x0020, 0x00FF, // Basic Latin + Latin Supplement (includes accented chars)
        0x0100, 0x017F, // Latin Extended-A
        0,
    };
    
    io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\segoeui.ttf", 16.0f, &fontConfig, ranges);

    // Application state
    AppState state;
    if (g_isAdmin) {
        state.statusMessage = "Pr\xc3\xaat - Appuyez sur Scanner pour commencer";
    } else {
        state.statusMessage = "Mode limit\xc3\xa9 - Red\xc3\xa9marrez en tant qu'administrateur pour activer/d\xc3\xa9sactiver les pilotes";
    }

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
            state.scanProgress = 0.0f;
            state.scanFuture = std::async(std::launch::async, [&state]() {
                state.scanner.SetProgressCallback([&state](int current, int total, const std::wstring& item) {
                    if (total > 0) {
                        state.scanProgress = (float)current / (float)total;
                    }
                    state.currentScanItem = item;
                });
                state.scanner.ScanAllDrivers();
                state.isScanning = false;
                state.statusMessage = "Scan termin\xc3\xa9";
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
        RenderUpdateProgressWindow(state);
        RenderAboutWindow(state);
        RenderUpdateHelpWindow(state);
        RenderDriverStoreCleanupWindow(state);
        RenderBSODAnalyzerWindow(state);
        RenderDownloadWindow(state);
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
