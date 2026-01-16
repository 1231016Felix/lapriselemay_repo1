#include "UIWidgets.h"
#include "../ManufacturerLinks.h"
#include <shellapi.h>
#include <algorithm>
#include <map>

namespace DriverManager {
namespace UI {

    // Helper function for sorting drivers with grouping by name
    static int CompareDrivers(const DriverInfo* a, const DriverInfo* b, int columnIndex, bool ascending) {
        int result = 0;
        
        switch (columnIndex) {
            case 0: result = a->deviceName.compare(b->deviceName); break;
            case 1: result = a->manufacturer.compare(b->manufacturer); break;
            case 2: result = a->driverVersion.compare(b->driverVersion); break;
            case 3: result = a->driverDate.compare(b->driverDate); break;
            case 4: result = a->driverAgeDays - b->driverAgeDays; break;
            case 5: result = static_cast<int>(a->status) - static_cast<int>(b->status); break;
            default: result = 0;
        }
        
        if (result == 0 && columnIndex != 0) {
            result = a->deviceName.compare(b->deviceName);
        }
        
        if (result == 0) {
            result = a->deviceInstanceId.compare(b->deviceInstanceId);
        }
        
        return ascending ? result : -result;
    }

    void RenderDriverList(AppState& state) {
        using namespace Constants::UI;
        
        const auto& categories = state.scanner.GetCategories();
        
        float availableWidth = ImGui::GetContentRegionAvail().x;
        float categoriesWidth = CATEGORIES_PANEL_WIDTH;
        float detailsWidth = state.selectedDriver ? DETAILS_PANEL_WIDTH : 0.0f;
        float driverListWidth = availableWidth - categoriesWidth - detailsWidth - PANEL_SPACING;
        
        // ========== Left panel - Categories ==========
        ImGui::BeginChild("Categories", ImVec2(categoriesWidth, 0), true);
        
        if (ImGui::Selectable(Constants::Text::CATEGORY_ALL, state.selectedCategory == -1)) {
            state.selectedCategory = -1;
        }
        
        ImGui::Separator();
        
        for (int i = 0; i < (int)categories.size(); i++) {
            const auto& cat = categories[i];
            if (cat.drivers.empty()) continue;
            
            char label[128];
            snprintf(label, sizeof(label), "%s (%zu)", GetTypeText(cat.type), cat.drivers.size());
            
            ImGui::PushID(i);
            if (ImGui::Selectable(label, state.selectedCategory == i)) {
                state.selectedCategory = i;
            }
            ImGui::PopID();
        }
        
        ImGui::EndChild();
        
        ImGui::SameLine();
        
        // ========== Center panel - Driver table ==========
        ImGui::BeginChild("DriverList", ImVec2(driverListWidth, 0), true);
        
        if (ImGui::BeginTable("Drivers", 6, 
            ImGuiTableFlags_Borders | ImGuiTableFlags_RowBg | ImGuiTableFlags_Resizable | 
            ImGuiTableFlags_Sortable | ImGuiTableFlags_SortMulti | ImGuiTableFlags_ScrollY)) {
            
            ImGui::TableSetupColumn("Nom", ImGuiTableColumnFlags_DefaultSort, COLUMN_NAME_WIDTH);
            ImGui::TableSetupColumn("Fabricant", ImGuiTableColumnFlags_None, COLUMN_MANUFACTURER_WIDTH);
            ImGui::TableSetupColumn("Version", ImGuiTableColumnFlags_None, COLUMN_VERSION_WIDTH);
            ImGui::TableSetupColumn("Date", ImGuiTableColumnFlags_None, COLUMN_DATE_WIDTH);
            ImGui::TableSetupColumn("\xc3\x82ge", ImGuiTableColumnFlags_None, COLUMN_AGE_WIDTH);
            ImGui::TableSetupColumn("Status", ImGuiTableColumnFlags_None, COLUMN_STATUS_WIDTH);
            ImGui::TableSetupScrollFreeze(0, 1);
            ImGui::TableHeadersRow();
            
            // Collect drivers to display
            std::vector<DriverInfo*> displayDrivers;
            std::string searchFilter = state.GetSearchFilter();
            std::string filterLower = searchFilter;
            std::transform(filterLower.begin(), filterLower.end(), filterLower.begin(), ::tolower);
            
            for (auto& cat : const_cast<std::vector<DriverCategory>&>(categories)) {
                if (state.selectedCategory >= 0 && state.selectedCategory != (int)(&cat - &categories[0])) {
                    continue;
                }
                
                for (auto& driver : cat.drivers) {
                    // Apply search filter using pre-calculated lowercase fields
                    if (!filterLower.empty() && !driver.MatchesFilter(filterLower)) {
                        continue;
                    }
                    
                    // Apply old drivers filter
                    if (state.filterOldDrivers && driver.ageCategory != DriverAge::VeryOld) {
                        continue;
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
                [&state](const DriverInfo* a, const DriverInfo* b) {
                    return CompareDrivers(a, b, state.sortColumnIndex, state.sortAscending) < 0;
                });
            
            // Group drivers by name
            std::map<std::wstring, std::vector<DriverInfo*>> driverGroups;
            std::vector<std::wstring> groupOrder;
            
            for (auto* driver : displayDrivers) {
                if (driverGroups.find(driver->deviceName) == driverGroups.end()) {
                    groupOrder.push_back(driver->deviceName);
                }
                driverGroups[driver->deviceName].push_back(driver);
            }
            
            // Render driver rows
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
                    if (ImGui::Selectable(WideToUtf8(driver->deviceName).c_str(), 
                        isSelected, ImGuiSelectableFlags_SpanAllColumns)) {
                        state.selectedDriver = driver;
                    }
                    ImGui::PopID();
                    
                    ImGui::TableNextColumn();
                    ImGui::TextUnformatted(WideToUtf8(driver->manufacturer).c_str());
                    
                    ImGui::TableNextColumn();
                    ImGui::TextUnformatted(WideToUtf8(driver->driverVersion).c_str());
                    
                    ImGui::TableNextColumn();
                    ImGui::TextUnformatted(WideToUtf8(driver->driverDate).c_str());
                    
                    ImGui::TableNextColumn();
                    ImGui::TextColored(GetAgeColor(driver->ageCategory), "%s", GetAgeText(driver->ageCategory));
                    
                    ImGui::TableNextColumn();
                    ImGui::TextColored(GetStatusColor(driver->status), "%s", GetStatusText(driver->status));
                } else {
                    // Multiple drivers with same name - render as expandable group
                    bool isExpanded = state.expandedGroups.count(groupName) > 0;
                    
                    ImGui::TableNextRow();
                    ImGui::TableNextColumn();
                    
                    ImGui::PushID(rowId++);
                    
                    bool groupSelected = false;
                    for (auto* d : group) {
                        if (state.selectedDriver == d) {
                            groupSelected = true;
                            break;
                        }
                    }
                    
                    char groupLabel[256];
                    snprintf(groupLabel, sizeof(groupLabel), "%s %s (%zu)",
                        isExpanded ? "v" : ">",
                        WideToUtf8(groupName).c_str(),
                        group.size());
                    
                    if (ImGui::Selectable(groupLabel, groupSelected, ImGuiSelectableFlags_SpanAllColumns)) {
                        if (isExpanded) {
                            state.expandedGroups.erase(groupName);
                        } else {
                            state.expandedGroups.insert(groupName);
                        }
                    }
                    ImGui::PopID();
                    
                    auto* firstDriver = group[0];
                    ImGui::TableNextColumn();
                    ImGui::TextUnformatted(WideToUtf8(firstDriver->manufacturer).c_str());
                    
                    ImGui::TableNextColumn();
                    ImGui::TextDisabled("...");
                    
                    ImGui::TableNextColumn();
                    ImGui::TextDisabled("...");
                    
                    // Age column - show oldest age
                    ImGui::TableNextColumn();
                    DriverAge oldestAge = DriverAge::Current;
                    for (auto* d : group) {
                        if (static_cast<int>(d->ageCategory) > static_cast<int>(oldestAge)) {
                            oldestAge = d->ageCategory;
                        }
                    }
                    ImGui::TextColored(GetAgeColor(oldestAge), "%s", GetAgeText(oldestAge));
                    
                    // Status column - show worst status
                    ImGui::TableNextColumn();
                    DriverStatus worstStatus = DriverStatus::OK;
                    for (auto* d : group) {
                        if (static_cast<int>(d->status) > static_cast<int>(worstStatus)) {
                            worstStatus = d->status;
                        }
                    }
                    ImGui::TextColored(GetStatusColor(worstStatus), "%s", GetStatusText(worstStatus));
                    
                    // Render child rows if expanded
                    if (isExpanded) {
                        int childIndex = 0;
                        for (auto* driver : group) {
                            ImGui::TableNextRow();
                            ImGui::TableNextColumn();
                            
                            bool isSelected = (state.selectedDriver == driver);
                            ImGui::PushID(rowId++);
                            ImGui::Indent(GROUP_INDENT);
                            
                            std::string childLabel = "#" + std::to_string(childIndex + 1);
                            if (!driver->driverVersion.empty()) {
                                childLabel += " (v" + WideToUtf8(driver->driverVersion) + ")";
                            }
                            
                            if (ImGui::Selectable(childLabel.c_str(), isSelected, ImGuiSelectableFlags_SpanAllColumns)) {
                                state.selectedDriver = driver;
                            }
                            
                            ImGui::Unindent(GROUP_INDENT);
                            ImGui::PopID();
                            
                            ImGui::TableNextColumn();
                            ImGui::TextUnformatted(WideToUtf8(driver->manufacturer).c_str());
                            
                            ImGui::TableNextColumn();
                            ImGui::TextUnformatted(WideToUtf8(driver->driverVersion).c_str());
                            
                            ImGui::TableNextColumn();
                            ImGui::TextUnformatted(WideToUtf8(driver->driverDate).c_str());
                            
                            ImGui::TableNextColumn();
                            ImGui::TextColored(GetAgeColor(driver->ageCategory), "%s", GetAgeText(driver->ageCategory));
                            
                            ImGui::TableNextColumn();
                            ImGui::TextColored(GetStatusColor(driver->status), "%s", GetStatusText(driver->status));
                            
                            childIndex++;
                        }
                    }
                }
            }
            
            ImGui::EndTable();
        }
        
        ImGui::EndChild();
        
        // ========== Right panel - Details ==========
        if (state.selectedDriver) {
            ImGui::SameLine();
            RenderDetailsPanel(state, detailsWidth);
        }
    }

    void RenderDetailsPanel(AppState& state, float width) {
        ImGui::BeginChild("Details", ImVec2(width, 0), true);
        
        auto* d = state.selectedDriver;
        if (!d) {
            ImGui::EndChild();
            return;
        }
        
        // Header
        ImGui::TextColored(ImVec4(Constants::Colors::HEADER_TEXT[0], Constants::Colors::HEADER_TEXT[1],
            Constants::Colors::HEADER_TEXT[2], Constants::Colors::HEADER_TEXT[3]), 
            "D\xc3\xa9tails du pilote");
        ImGui::SameLine(width - 35.0f);
        if (ImGui::Button("X", ImVec2(20, 20))) {
            state.selectedDriver = nullptr;
            ImGui::EndChild();
            return;
        }
        ImGui::Separator();
        ImGui::Spacing();
        
        // Device name
        ImGui::TextWrapped("%s", WideToUtf8(d->deviceName).c_str());
        ImGui::Spacing();
        ImGui::Separator();
        ImGui::Spacing();
        
        // Helper for detail rows
        auto addDetailRow = [](const char* label, const std::string& value) {
            if (value.empty()) return;
            ImGui::TextColored(ImVec4(Constants::Colors::LABEL_TEXT[0], Constants::Colors::LABEL_TEXT[1],
                Constants::Colors::LABEL_TEXT[2], Constants::Colors::LABEL_TEXT[3]), "%s", label);
            ImGui::TextWrapped("%s", value.c_str());
            ImGui::Spacing();
        };
        
        addDetailRow("Description:", WideToUtf8(d->deviceDescription));
        addDetailRow("Fabricant:", WideToUtf8(d->manufacturer));
        addDetailRow("Version:", WideToUtf8(d->driverVersion));
        addDetailRow("Date:", WideToUtf8(d->driverDate));
        
        // Age with color
        ImGui::TextColored(ImVec4(Constants::Colors::LABEL_TEXT[0], Constants::Colors::LABEL_TEXT[1],
            Constants::Colors::LABEL_TEXT[2], Constants::Colors::LABEL_TEXT[3]), "\xc3\x82ge:");
        std::string ageText = FormatAgeDays(d->driverAgeDays);
        if (d->ageCategory == DriverAge::VeryOld) {
            ageText += " (obsolete)";
        }
        ImGui::TextColored(GetAgeColor(d->ageCategory), "%s", ageText.c_str());
        ImGui::Spacing();
        
        addDetailRow("Fournisseur:", WideToUtf8(d->driverProvider));
        addDetailRow("Classe:", WideToUtf8(d->deviceClass));
        
        ImGui::Separator();
        ImGui::Spacing();
        
        // Status
        ImGui::TextColored(ImVec4(Constants::Colors::LABEL_TEXT[0], Constants::Colors::LABEL_TEXT[1],
            Constants::Colors::LABEL_TEXT[2], Constants::Colors::LABEL_TEXT[3]), "Status:");
        ImGui::TextColored(GetStatusColor(d->status), "%s", GetStatusText(d->status));
        ImGui::Spacing();
        
        // Enabled
        ImGui::TextColored(ImVec4(Constants::Colors::LABEL_TEXT[0], Constants::Colors::LABEL_TEXT[1],
            Constants::Colors::LABEL_TEXT[2], Constants::Colors::LABEL_TEXT[3]), "Activ\xc3\xa9:");
        ImGui::Text("%s", d->isEnabled ? "Oui" : "Non");
        ImGui::Spacing();
        
        if (d->problemCode != 0) {
            ImGui::TextColored(ImVec4(Constants::Colors::LABEL_TEXT[0], Constants::Colors::LABEL_TEXT[1],
                Constants::Colors::LABEL_TEXT[2], Constants::Colors::LABEL_TEXT[3]), "Code probl\xc3\xa8me:");
            ImGui::TextColored(ImVec4(0.9f, 0.5f, 0.2f, 1.0f), "%u", d->problemCode);
            ImGui::Spacing();
        }
        
        ImGui::Separator();
        ImGui::Spacing();
        
        // Hardware IDs (collapsible)
        if (ImGui::CollapsingHeader("IDs mat\xc3\xa9riel")) {
            ImGui::TextColored(ImVec4(Constants::Colors::LABEL_TEXT[0], Constants::Colors::LABEL_TEXT[1],
                Constants::Colors::LABEL_TEXT[2], Constants::Colors::LABEL_TEXT[3]), "Hardware ID:");
            ImGui::TextWrapped("%s", WideToUtf8(d->hardwareId).c_str());
            ImGui::Spacing();
            
            ImGui::TextColored(ImVec4(Constants::Colors::LABEL_TEXT[0], Constants::Colors::LABEL_TEXT[1],
                Constants::Colors::LABEL_TEXT[2], Constants::Colors::LABEL_TEXT[3]), "Instance ID:");
            ImGui::TextWrapped("%s", WideToUtf8(d->deviceInstanceId).c_str());
        }
        
        ImGui::Spacing();
        ImGui::Separator();
        ImGui::Spacing();
        
        // Download update button (if update available)
        if (d->hasUpdate && !d->availableUpdate.downloadUrl.empty()) {
            ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(Constants::Colors::BUTTON_INSTALL[0],
                Constants::Colors::BUTTON_INSTALL[1], Constants::Colors::BUTTON_INSTALL[2],
                Constants::Colors::BUTTON_INSTALL[3]));
            ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(Constants::Colors::BUTTON_INSTALL_HOVER[0],
                Constants::Colors::BUTTON_INSTALL_HOVER[1], Constants::Colors::BUTTON_INSTALL_HOVER[2],
                Constants::Colors::BUTTON_INSTALL_HOVER[3]));
            ImGui::PushStyleColor(ImGuiCol_ButtonActive, ImVec4(0.4f, 0.9f, 0.5f, 1.0f));
            if (ImGui::Button("Telecharger MAJ", ImVec2(-1, 0))) {
                state.driverDownloader.QueueDownload(*d, d->availableUpdate.downloadUrl, false);
                state.showDownloadWindow = true;
                state.SetStatusMessage("Pilote ajoute a la file de telechargement");
            }
            ImGui::PopStyleColor(3);
            
            ImGui::TextColored(ImVec4(0.4f, 0.8f, 0.4f, 1.0f), "MAJ disponible: %s", 
                WideToUtf8(d->availableUpdate.newVersion).c_str());
            ImGui::Spacing();
        }
        
        // Download driver button
        ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(Constants::Colors::BUTTON_DOWNLOAD[0],
            Constants::Colors::BUTTON_DOWNLOAD[1], Constants::Colors::BUTTON_DOWNLOAD[2],
            Constants::Colors::BUTTON_DOWNLOAD[3]));
        ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(Constants::Colors::BUTTON_DOWNLOAD_HOVER[0],
            Constants::Colors::BUTTON_DOWNLOAD_HOVER[1], Constants::Colors::BUTTON_DOWNLOAD_HOVER[2],
            Constants::Colors::BUTTON_DOWNLOAD_HOVER[3]));
        ImGui::PushStyleColor(ImGuiCol_ButtonActive, ImVec4(0.4f, 0.7f, 1.0f, 1.0f));
        if (ImGui::Button("T\xc3\xa9l\xc3\xa9" "charger pilote \xe2\x96\xbc", ImVec2(-1, 0))) {
            ImGui::OpenPopup("DownloadDriverPopup");
        }
        ImGui::PopStyleColor(3);
        
        // Download popup menu
        if (ImGui::BeginPopup("DownloadDriverPopup")) {
            std::wstring mfrUrl = FindManufacturerUrl(d->manufacturer);
            
            if (!mfrUrl.empty()) {
                std::string menuLabel = "Site " + WideToUtf8(d->manufacturer);
                if (ImGui::MenuItem(menuLabel.c_str())) {
                    OpenUrl(mfrUrl);
                }
                ImGui::Separator();
            }
            
            if (ImGui::MenuItem("Rechercher sur Google")) {
                SearchGoogleForDriver(d->manufacturer, d->deviceName);
            }
            
            if (ImGui::MenuItem("Rechercher sur TousLesDrivers.com")) {
                SearchTousLesDrivers(d->deviceName);
            }
            
            ImGui::EndPopup();
        }
        
        ImGui::EndChild();
    }

}} // namespace DriverManager::UI
