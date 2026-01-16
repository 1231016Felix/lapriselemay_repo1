// DriverManager - Windows Driver Management Tool
// Using Dear ImGui with DirectX 11
//
// Refactored version using modular UI components

#include "imgui/imgui.h"
#include "imgui/imgui_internal.h"
#include "imgui/imgui_impl_win32.h"
#include "imgui/imgui_impl_dx11.h"
#include <d3d11.h>
#include <tchar.h>
#include <shellapi.h>
#include <thread>
#include <future>

// Core components
#include "src/Core/AppState.h"
#include "src/Core/Config.h"
#include "src/Core/Logger.h"
#include "src/Core/Constants.h"

// UI components
#include "src/UI/UIWidgets.h"

// Utilities
#include "src/Utils.h"
#include "src/StringUtils.h"

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "advapi32.lib")

using namespace DriverManager;

// ============================================================================
// DirectX Globals
// ============================================================================
static ID3D11Device*            g_pd3dDevice = nullptr;
static ID3D11DeviceContext*     g_pd3dDeviceContext = nullptr;
static IDXGISwapChain*          g_pSwapChain = nullptr;
static ID3D11RenderTargetView*  g_mainRenderTargetView = nullptr;
static UINT                     g_ResizeWidth = 0, g_ResizeHeight = 0;

// Global state
static bool g_isAdmin = false;

// ============================================================================
// Forward Declarations
// ============================================================================
bool CreateDeviceD3D(HWND hWnd);
void CleanupDeviceD3D();
void CreateRenderTarget();
void CleanupRenderTarget();
LRESULT WINAPI WndProc(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);
extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);

// ============================================================================
// ImGui Style Setup
// ============================================================================
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

// ============================================================================
// Main Entry Point
// ============================================================================
int WINAPI WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine, int nCmdShow) {
    (void)hPrevInstance;
    (void)lpCmdLine;
    
    // Initialize logging
    Logger::Instance().SetMinLevel(LogLevel::Info);
    
    // Try to set up log file
    std::wstring logPath = GetAppDataPath();
    if (!logPath.empty()) {
        logPath += L"\\DriverManager";
        CreateDirectoryRecursive(logPath);
        logPath += L"\\DriverManager.log";
        Logger::Instance().SetLogFile(logPath);
    }
    
    LOG_INFO(L"DriverManager démarré");
    
    // Load configuration
    Config::Instance().LoadDefault();
    
    // Check admin status
    g_isAdmin = IsRunningAsAdmin();
    LOG_INFO(g_isAdmin ? L"Mode administrateur actif" : L"Mode utilisateur standard");
    
    // Create application window
    WNDCLASSEXW wc = { 
        sizeof(wc), CS_CLASSDC, WndProc, 0L, 0L, 
        hInstance, nullptr, nullptr, nullptr, nullptr, 
        L"DriverManager", nullptr 
    };
    RegisterClassExW(&wc);
    
    int windowWidth = Config::Instance().GetInt(Config::Keys::WindowWidth, Constants::UI::DEFAULT_WINDOW_WIDTH);
    int windowHeight = Config::Instance().GetInt(Config::Keys::WindowHeight, Constants::UI::DEFAULT_WINDOW_HEIGHT);
    
    HWND hwnd = CreateWindowW(
        wc.lpszClassName,
        L"Driver Manager",
        WS_OVERLAPPEDWINDOW,
        CW_USEDEFAULT, CW_USEDEFAULT,
        windowWidth, windowHeight,
        nullptr, nullptr, wc.hInstance, nullptr
    );

    // Initialize Direct3D
    if (!CreateDeviceD3D(hwnd)) {
        CleanupDeviceD3D();
        UnregisterClassW(wc.lpszClassName, wc.hInstance);
        LOG_ERROR(L"Échec de l'initialisation DirectX");
        return 1;
    }

    ShowWindow(hwnd, nCmdShow);
    UpdateWindow(hwnd);

    // Setup Dear ImGui context
    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO();
    io.ConfigFlags |= ImGuiConfigFlags_NavEnableKeyboard;
    io.IniFilename = nullptr;  // Disable imgui.ini

    // Setup style
    SetupImGuiStyle();

    // Setup Platform/Renderer backends
    ImGui_ImplWin32_Init(hwnd);
    ImGui_ImplDX11_Init(g_pd3dDevice, g_pd3dDeviceContext);

    // Application state
    AppState state;
    state.SetStatusMessage("Pr\xc3\xaat - Cliquez sur 'Scanner' pour commencer");
    
    // Load saved preferences
    state.filterOldDrivers = Config::Instance().GetBool(Config::Keys::FilterOldDrivers, false);
    state.createRestorePoint = Config::Instance().GetBool(Config::Keys::CreateRestorePoint, false);

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

        // Handle resize
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

        // Render menu bar
        UI::RenderMenuBar(state, g_isAdmin);

        // Main content area
        ImGuiViewport* viewport = ImGui::GetMainViewport();
        ImGui::SetNextWindowPos(ImVec2(viewport->Pos.x, viewport->Pos.y + ImGui::GetFrameHeight()));
        ImGui::SetNextWindowSize(ImVec2(viewport->Size.x, viewport->Size.y - ImGui::GetFrameHeight() - Constants::UI::STATUS_BAR_HEIGHT));

        ImGuiWindowFlags mainWindowFlags = 
            ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoResize | 
            ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoCollapse |
            ImGuiWindowFlags_NoBringToFrontOnFocus | ImGuiWindowFlags_NoNavFocus;

        ImGui::Begin("MainContent", nullptr, mainWindowFlags);
        
        // Render toolbar
        UI::RenderToolbar(state, g_isAdmin);
        ImGui::Separator();
        ImGui::Spacing();
        
        // Render driver list
        UI::RenderDriverList(state);
        
        ImGui::End();

        // Render status bar
        UI::RenderStatusBar(state);

        // Render modal windows
        UI::RenderAboutWindow(state);
        UI::RenderUpdateProgressWindow(state);
        UI::RenderUpdateHelpWindow(state);
        UI::RenderDriverStoreCleanupWindow(state);
        UI::RenderBSODAnalyzerWindow(state);
        UI::RenderDownloadWindow(state);

        // Rendering
        ImGui::Render();
        const float clear_color[4] = { 0.1f, 0.1f, 0.12f, 1.0f };
        g_pd3dDeviceContext->OMSetRenderTargets(1, &g_mainRenderTargetView, nullptr);
        g_pd3dDeviceContext->ClearRenderTargetView(g_mainRenderTargetView, clear_color);
        ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());

        g_pSwapChain->Present(1, 0);
    }

    // Save configuration
    RECT windowRect;
    if (GetWindowRect(hwnd, &windowRect)) {
        Config::Instance().SetInt(Config::Keys::WindowWidth, windowRect.right - windowRect.left);
        Config::Instance().SetInt(Config::Keys::WindowHeight, windowRect.bottom - windowRect.top);
    }
    Config::Instance().SetBool(Config::Keys::FilterOldDrivers, state.filterOldDrivers);
    Config::Instance().SetBool(Config::Keys::CreateRestorePoint, state.createRestorePoint);
    Config::Instance().Save();

    // Cleanup
    state.CancelAllTasks();
    
    ImGui_ImplDX11_Shutdown();
    ImGui_ImplWin32_Shutdown();
    ImGui::DestroyContext();

    CleanupDeviceD3D();
    DestroyWindow(hwnd);
    UnregisterClassW(wc.lpszClassName, wc.hInstance);

    LOG_INFO(L"DriverManager fermé");
    Logger::Instance().CloseLogFile();

    return 0;
}

// ============================================================================
// DirectX Helper Functions
// ============================================================================
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
    const D3D_FEATURE_LEVEL featureLevelArray[2] = { D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_0, };
    HRESULT res = D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, createDeviceFlags, featureLevelArray, 2, D3D11_SDK_VERSION, &sd, &g_pSwapChain, &g_pd3dDevice, &featureLevel, &g_pd3dDeviceContext);
    if (res == DXGI_ERROR_UNSUPPORTED)
        res = D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, createDeviceFlags, featureLevelArray, 2, D3D11_SDK_VERSION, &sd, &g_pSwapChain, &g_pd3dDevice, &featureLevel, &g_pd3dDeviceContext);
    if (res != S_OK)
        return false;

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

// ============================================================================
// Window Procedure
// ============================================================================
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
    return DefWindowProc(hWnd, msg, wParam, lParam);
}
