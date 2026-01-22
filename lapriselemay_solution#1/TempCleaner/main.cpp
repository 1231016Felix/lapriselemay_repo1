#ifndef UNICODE
#define UNICODE
#endif

#include "imgui.h"
#include "backends/imgui_impl_win32.h"
#include "backends/imgui_impl_dx11.h"
#include <d3d11.h>
#include <tchar.h>
#include <string>
#include <thread>
#include <mutex>
#include <format>
#include "Cleaner.h"

// DirectX 11 globals
static ID3D11Device*            g_pd3dDevice = nullptr;
static ID3D11DeviceContext*     g_pd3dDeviceContext = nullptr;
static IDXGISwapChain*          g_pSwapChain = nullptr;
static ID3D11RenderTargetView*  g_mainRenderTargetView = nullptr;

// App state
static TempCleaner::Cleaner g_cleaner;
static TempCleaner::CleaningOptions g_options;
static bool g_isRunning = false;
static bool g_showSettings = false;
static int g_progress = 0;
static std::wstring g_statusText = L"Pret";
static std::wstring g_resultText;
static std::mutex g_mutex;

// Forward declarations
bool CreateDeviceD3D(HWND hWnd);
void CleanupDeviceD3D();
void CreateRenderTarget();
void CleanupRenderTarget();
LRESULT WINAPI WndProc(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);
extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);

std::string WideToUtf8(const std::wstring& wide) {
    if (wide.empty()) return "";
    int size = WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), -1, nullptr, 0, nullptr, nullptr);
    std::string result(size - 1, 0);
    WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), -1, result.data(), size, nullptr, nullptr);
    return result;
}

std::wstring FormatBytes(uint64_t bytes) {
    const wchar_t* units[] = { L"octets", L"Ko", L"Mo", L"Go" };
    int unitIndex = 0;
    double size = static_cast<double>(bytes);
    while (size >= 1024.0 && unitIndex < 3) {
        size /= 1024.0;
        unitIndex++;
    }
    if (unitIndex == 0) return std::format(L"{} {}", bytes, units[unitIndex]);
    return std::format(L"{:.2f} {}", size, units[unitIndex]);
}

void SetupImGuiStyle() {
    ImGuiStyle& style = ImGui::GetStyle();
    
    // Coins arrondis
    style.WindowRounding = 8.0f;
    style.ChildRounding = 6.0f;
    style.FrameRounding = 6.0f;
    style.PopupRounding = 6.0f;
    style.ScrollbarRounding = 6.0f;
    style.GrabRounding = 6.0f;
    style.TabRounding = 6.0f;
    
    // Padding
    style.WindowPadding = ImVec2(15, 15);
    style.FramePadding = ImVec2(12, 8);
    style.ItemSpacing = ImVec2(10, 10);
    
    // Couleurs - Theme sombre moderne
    ImVec4* colors = style.Colors;
    colors[ImGuiCol_WindowBg] = ImVec4(0.10f, 0.10f, 0.12f, 1.00f);
    colors[ImGuiCol_ChildBg] = ImVec4(0.14f, 0.14f, 0.16f, 1.00f);
    colors[ImGuiCol_PopupBg] = ImVec4(0.12f, 0.12f, 0.14f, 1.00f);
    colors[ImGuiCol_Border] = ImVec4(0.30f, 0.30f, 0.35f, 0.50f);
    colors[ImGuiCol_FrameBg] = ImVec4(0.18f, 0.18f, 0.22f, 1.00f);
    colors[ImGuiCol_FrameBgHovered] = ImVec4(0.22f, 0.22f, 0.28f, 1.00f);
    colors[ImGuiCol_FrameBgActive] = ImVec4(0.26f, 0.26f, 0.32f, 1.00f);
    colors[ImGuiCol_TitleBg] = ImVec4(0.08f, 0.08f, 0.10f, 1.00f);
    colors[ImGuiCol_TitleBgActive] = ImVec4(0.12f, 0.12f, 0.14f, 1.00f);
    colors[ImGuiCol_MenuBarBg] = ImVec4(0.12f, 0.12f, 0.14f, 1.00f);
    colors[ImGuiCol_ScrollbarBg] = ImVec4(0.10f, 0.10f, 0.12f, 1.00f);
    colors[ImGuiCol_ScrollbarGrab] = ImVec4(0.30f, 0.30f, 0.35f, 1.00f);
    colors[ImGuiCol_ScrollbarGrabHovered] = ImVec4(0.40f, 0.40f, 0.45f, 1.00f);
    colors[ImGuiCol_ScrollbarGrabActive] = ImVec4(0.50f, 0.50f, 0.55f, 1.00f);
    colors[ImGuiCol_CheckMark] = ImVec4(0.40f, 0.80f, 0.60f, 1.00f);
    colors[ImGuiCol_SliderGrab] = ImVec4(0.40f, 0.80f, 0.60f, 1.00f);
    colors[ImGuiCol_SliderGrabActive] = ImVec4(0.50f, 0.90f, 0.70f, 1.00f);
    colors[ImGuiCol_Button] = ImVec4(0.25f, 0.60f, 0.45f, 1.00f);
    colors[ImGuiCol_ButtonHovered] = ImVec4(0.30f, 0.70f, 0.52f, 1.00f);
    colors[ImGuiCol_ButtonActive] = ImVec4(0.35f, 0.80f, 0.60f, 1.00f);
    colors[ImGuiCol_Header] = ImVec4(0.25f, 0.60f, 0.45f, 0.40f);
    colors[ImGuiCol_HeaderHovered] = ImVec4(0.25f, 0.60f, 0.45f, 0.60f);
    colors[ImGuiCol_HeaderActive] = ImVec4(0.25f, 0.60f, 0.45f, 0.80f);
    colors[ImGuiCol_Separator] = ImVec4(0.30f, 0.30f, 0.35f, 0.50f);
    colors[ImGuiCol_Text] = ImVec4(0.95f, 0.95f, 0.95f, 1.00f);
    colors[ImGuiCol_TextDisabled] = ImVec4(0.50f, 0.50f, 0.55f, 1.00f);
    colors[ImGuiCol_PlotHistogram] = ImVec4(0.40f, 0.80f, 0.60f, 1.00f);
}

void StartCleaning() {
    g_isRunning = true;
    g_progress = 0;
    g_resultText.clear();
    g_statusText = L"Nettoyage en cours...";

    std::thread([]() {
        auto callback = [](const std::wstring& status, int progress) {
            std::lock_guard<std::mutex> lock(g_mutex);
            g_statusText = status;
            g_progress = progress;
        };

        auto stats = g_cleaner.clean(g_options, callback);

        std::lock_guard<std::mutex> lock(g_mutex);
        g_resultText = std::format(L"{} fichiers supprimes\n{} liberes",
            stats.filesDeleted, FormatBytes(stats.bytesFreed));
        if (stats.errors > 0) {
            g_resultText += std::format(L"\n({} erreurs)", stats.errors);
        }
        g_statusText = L"Termine!";
        g_progress = 100;
        g_isRunning = false;
    }).detach();
}

void StopCleaning() {
    g_cleaner.stop();
    g_statusText = L"Arret en cours...";
}

int WINAPI wWinMain(HINSTANCE hInstance, HINSTANCE, PWSTR, int nCmdShow) {
    // Charger les options
    g_options = TempCleaner::Cleaner::loadOptions();

    // Creer la fenetre
    WNDCLASSEXW wc = { sizeof(wc), CS_CLASSDC, WndProc, 0L, 0L, 
                       GetModuleHandle(nullptr), nullptr, nullptr, nullptr, nullptr, 
                       L"TempCleanerClass", nullptr };
    RegisterClassExW(&wc);
    
    HWND hwnd = CreateWindowW(wc.lpszClassName, L"TempCleaner", 
                              WS_OVERLAPPEDWINDOW & ~WS_MAXIMIZEBOX & ~WS_THICKFRAME,
                              100, 100, 420, 320, nullptr, nullptr, wc.hInstance, nullptr);

    if (!CreateDeviceD3D(hwnd)) {
        CleanupDeviceD3D();
        UnregisterClassW(wc.lpszClassName, wc.hInstance);
        return 1;
    }

    ShowWindow(hwnd, nCmdShow);
    UpdateWindow(hwnd);

    // Setup ImGui
    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO();
    io.ConfigFlags |= ImGuiConfigFlags_NavEnableKeyboard;
    io.IniFilename = nullptr; // Pas de fichier ini
    
    // Charger une police plus grande
    io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\segoeui.ttf", 18.0f);
    
    SetupImGuiStyle();
    
    ImGui_ImplWin32_Init(hwnd);
    ImGui_ImplDX11_Init(g_pd3dDevice, g_pd3dDeviceContext);

    // Boucle principale
    bool done = false;
    while (!done) {
        MSG msg;
        while (PeekMessage(&msg, nullptr, 0U, 0U, PM_REMOVE)) {
            TranslateMessage(&msg);
            DispatchMessage(&msg);
            if (msg.message == WM_QUIT)
                done = true;
        }
        if (done) break;

        ImGui_ImplDX11_NewFrame();
        ImGui_ImplWin32_NewFrame();
        ImGui::NewFrame();

        // Fenetre principale (plein ecran dans la fenetre)
        ImGui::SetNextWindowPos(ImVec2(0, 0));
        ImGui::SetNextWindowSize(io.DisplaySize);
        ImGui::Begin("TempCleaner", nullptr, 
                     ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoResize | 
                     ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoCollapse);

        // Titre
        ImGui::SetCursorPosX((ImGui::GetWindowWidth() - ImGui::CalcTextSize("TempCleaner").x) * 0.5f);
        ImGui::TextColored(ImVec4(0.4f, 0.8f, 0.6f, 1.0f), "TempCleaner");
        ImGui::Spacing();
        ImGui::Separator();
        ImGui::Spacing();
        ImGui::Spacing();

        // Bouton principal
        float buttonWidth = 280.0f;
        float buttonHeight = 50.0f;
        ImGui::SetCursorPosX((ImGui::GetWindowWidth() - buttonWidth) * 0.5f);
        
        if (g_isRunning) {
            ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(0.70f, 0.25f, 0.25f, 1.0f));
            ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(0.80f, 0.30f, 0.30f, 1.0f));
            ImGui::PushStyleColor(ImGuiCol_ButtonActive, ImVec4(0.90f, 0.35f, 0.35f, 1.0f));
            if (ImGui::Button("Arreter", ImVec2(buttonWidth, buttonHeight))) {
                StopCleaning();
            }
            ImGui::PopStyleColor(3);
        } else {
            if (ImGui::Button("Demarrer le nettoyage", ImVec2(buttonWidth, buttonHeight))) {
                StartCleaning();
            }
        }

        ImGui::Spacing();
        ImGui::Spacing();

        // Statut
        {
            std::lock_guard<std::mutex> lock(g_mutex);
            std::string status = WideToUtf8(g_statusText);
            ImGui::SetCursorPosX((ImGui::GetWindowWidth() - ImGui::CalcTextSize(status.c_str()).x) * 0.5f);
            ImGui::TextDisabled("%s", status.c_str());
        }

        ImGui::Spacing();

        // Barre de progression
        ImGui::SetCursorPosX((ImGui::GetWindowWidth() - buttonWidth) * 0.5f);
        ImGui::ProgressBar(g_progress / 100.0f, ImVec2(buttonWidth, 8), "");

        ImGui::Spacing();
        ImGui::Spacing();

        // Resultat
        if (!g_resultText.empty()) {
            std::lock_guard<std::mutex> lock(g_mutex);
            std::string result = WideToUtf8(g_resultText);
            
            // Centrer le texte multiligne
            std::istringstream stream(result);
            std::string line;
            while (std::getline(stream, line)) {
                ImGui::SetCursorPosX((ImGui::GetWindowWidth() - ImGui::CalcTextSize(line.c_str()).x) * 0.5f);
                ImGui::Text("%s", line.c_str());
            }
        }

        // Bouton parametres en bas
        ImGui::SetCursorPos(ImVec2(15, ImGui::GetWindowHeight() - 45));
        if (ImGui::Button("Parametres", ImVec2(100, 30))) {
            g_showSettings = true;
        }

        ImGui::End();

        // Fenetre parametres
        if (g_showSettings) {
            ImGui::SetNextWindowSize(ImVec2(320, 280), ImGuiCond_FirstUseEver);
            ImGui::Begin("Parametres", &g_showSettings, ImGuiWindowFlags_NoResize);
            
            ImGui::Text("Dossiers a nettoyer:");
            ImGui::Spacing();
            ImGui::Separator();
            ImGui::Spacing();
            
            ImGui::Checkbox("Temp utilisateur (%TEMP%)", &g_options.cleanUserTemp);
            ImGui::Checkbox("Temp Windows", &g_options.cleanWindowsTemp);
            ImGui::Checkbox("Prefetch", &g_options.cleanPrefetch);
            ImGui::Checkbox("Fichiers recents", &g_options.cleanRecent);
            ImGui::Checkbox("Corbeille", &g_options.cleanRecycleBin);
            ImGui::Checkbox("Cache navigateurs", &g_options.cleanBrowserCache);
            
            ImGui::Spacing();
            ImGui::Separator();
            ImGui::Spacing();
            
            if (ImGui::Button("Sauvegarder", ImVec2(120, 30))) {
                TempCleaner::Cleaner::saveOptions(g_options);
                g_showSettings = false;
            }
            ImGui::SameLine();
            if (ImGui::Button("Annuler", ImVec2(120, 30))) {
                g_options = TempCleaner::Cleaner::loadOptions();
                g_showSettings = false;
            }
            
            ImGui::End();
        }

        // Rendu
        ImGui::Render();
        const float clear_color[4] = { 0.10f, 0.10f, 0.12f, 1.00f };
        g_pd3dDeviceContext->OMSetRenderTargets(1, &g_mainRenderTargetView, nullptr);
        g_pd3dDeviceContext->ClearRenderTargetView(g_mainRenderTargetView, clear_color);
        ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());

        g_pSwapChain->Present(1, 0);
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

bool CreateDeviceD3D(HWND hWnd) {
    DXGI_SWAP_CHAIN_DESC sd = {};
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
    HRESULT res = D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 
        createDeviceFlags, featureLevelArray, 2, D3D11_SDK_VERSION, &sd, &g_pSwapChain, 
        &g_pd3dDevice, &featureLevel, &g_pd3dDeviceContext);
    if (res == DXGI_ERROR_UNSUPPORTED)
        res = D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, 
            createDeviceFlags, featureLevelArray, 2, D3D11_SDK_VERSION, &sd, &g_pSwapChain, 
            &g_pd3dDevice, &featureLevel, &g_pd3dDeviceContext);
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

LRESULT WINAPI WndProc(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam) {
    if (ImGui_ImplWin32_WndProcHandler(hWnd, msg, wParam, lParam))
        return true;

    switch (msg) {
    case WM_SIZE:
        if (g_pd3dDevice != nullptr && wParam != SIZE_MINIMIZED) {
            CleanupRenderTarget();
            g_pSwapChain->ResizeBuffers(0, (UINT)LOWORD(lParam), (UINT)HIWORD(lParam), 
                                        DXGI_FORMAT_UNKNOWN, 0);
            CreateRenderTarget();
        }
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
