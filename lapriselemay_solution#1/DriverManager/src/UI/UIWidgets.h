#pragma once

// Common UI includes
#include "../../imgui/imgui.h"
#include "../../imgui/imgui_internal.h"
#include "../Core/AppState.h"
#include "../Core/Constants.h"
#include "../DriverInfo.h"
#include "../StringUtils.h"

#include <string>
#include <future>
#include <thread>

namespace DriverManager {
namespace UI {

    // ========== Main UI Components ==========
    void RenderMenuBar(AppState& state, bool isAdmin);
    void RenderToolbar(AppState& state, bool isAdmin);
    void RenderDriverList(AppState& state);
    void RenderDetailsPanel(AppState& state, float width);
    void RenderStatusBar(AppState& state);
    
    // ========== Modal Windows ==========
    void RenderAboutWindow(AppState& state);
    void RenderUpdateProgressWindow(AppState& state);
    void RenderUpdateHelpWindow(AppState& state);
    void RenderDriverStoreCleanupWindow(AppState& state);
    void RenderBSODAnalyzerWindow(AppState& state);
    void RenderDownloadWindow(AppState& state);
    
    // ========== Helper Functions ==========
    ImVec4 GetStatusColor(DriverStatus status);
    ImVec4 GetAgeColor(DriverAge age);
    std::string FormatFileSize(uint64_t bytes);

}} // namespace DriverManager::UI
