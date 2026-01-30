#ifndef UNICODE
#define UNICODE
#endif

#include "imgui.h"
#include "backends/imgui_impl_win32.h"
#include "backends/imgui_impl_dx11.h"
#include <d3d11.h>
#include "resource.h"
#include <tchar.h>
#include <string>
#include <thread>
#include <mutex>
#include <format>
#include <algorithm>
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
static bool g_showErrors = false;
static bool g_showEstimate = false;
static bool g_startCleaningRequested = false;  // Flag pour éviter deadlock
static bool g_showDismWarning = false;  // Avertissement DISM
static bool g_isMemoryPurging = false;  // Purge mémoire en cours
static std::wstring g_memoryPurgeResult;  // Résultat de la purge mémoire
static int g_progress = 0;
static std::wstring g_statusText = L"Pret";
static std::wstring g_resultText;
static std::vector<TempCleaner::ErrorInfo> g_errorDetails;
static TempCleaner::CleaningEstimate g_estimate;
static std::mutex g_mutex;

// Disk space tracking
static uint64_t g_diskFreeBefore = 0;
static uint64_t g_diskFreeAfter = 0;
static uint64_t g_diskTotal = 0;
static bool g_showDiskChart = false;

uint64_t GetDiskFreeSpace() {
    wchar_t sysDir[MAX_PATH];
    GetSystemDirectoryW(sysDir, MAX_PATH);
    std::wstring root = std::wstring(sysDir, 3);  // "C:\"
    
    ULARGE_INTEGER freeBytesAvailable, totalBytes, totalFreeBytes;
    if (GetDiskFreeSpaceExW(root.c_str(), &freeBytesAvailable, &totalBytes, &totalFreeBytes)) {
        g_diskTotal = totalBytes.QuadPart;
        return freeBytesAvailable.QuadPart;
    }
    return 0;
}

void DrawDonutChart(ImVec2 center, float radius, float thickness, float usedRatio, float freedRatio, bool showFreed) {
    ImDrawList* draw = ImGui::GetWindowDrawList();
    const int segments = 64;
    const float pi2 = 3.14159265f * 2.0f;
    const float startAngle = -3.14159265f / 2.0f;  // Start from top
    
    ImU32 colorUsed = IM_COL32(180, 80, 80, 255);      // Rouge - utilisé
    ImU32 colorFreed = IM_COL32(100, 200, 130, 255);   // Vert - libéré
    ImU32 colorFree = IM_COL32(70, 130, 100, 255);     // Vert foncé - libre
    ImU32 colorBg = IM_COL32(40, 40, 45, 255);         // Fond
    
    // Background ring
    draw->PathArcTo(center, radius, 0, pi2, segments);
    draw->PathStroke(colorBg, 0, thickness);
    
    float usedAngle = usedRatio * pi2;
    float freedAngle = freedRatio * pi2;
    float freeAngle = (1.0f - usedRatio) * pi2;
    
    // Free space (dark green)
    if (freeAngle > 0.01f) {
        draw->PathArcTo(center, radius, startAngle + usedAngle, startAngle + pi2, segments);
        draw->PathStroke(colorFree, 0, thickness);
    }
    
    // Freed space highlight (bright green) - only after cleaning
    if (showFreed && freedAngle > 0.001f) {
        float freedStart = startAngle + (usedRatio + freedRatio) * pi2 - freedAngle;
        draw->PathArcTo(center, radius, freedStart, freedStart + freedAngle, segments);
        draw->PathStroke(colorFreed, 0, thickness + 2);
    }
    
    // Used space (red)
    if (usedAngle > 0.01f) {
        draw->PathArcTo(center, radius, startAngle, startAngle + usedAngle, segments);
        draw->PathStroke(colorUsed, 0, thickness);
    }
}

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
    
    style.WindowRounding = 8.0f;
    style.ChildRounding = 6.0f;
    style.FrameRounding = 6.0f;
    style.PopupRounding = 6.0f;
    style.ScrollbarRounding = 6.0f;
    style.GrabRounding = 6.0f;
    style.TabRounding = 6.0f;
    
    style.WindowPadding = ImVec2(15, 15);
    style.FramePadding = ImVec2(12, 8);
    style.ItemSpacing = ImVec2(10, 10);
    
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
    // Capture disk space before cleaning
    g_diskFreeBefore = GetDiskFreeSpace();
    g_showDiskChart = false;
    
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        g_isRunning = true;
        g_progress = 0;
        g_resultText.clear();
        g_errorDetails.clear();
        g_statusText = L"Nettoyage en cours...";
    }

    std::thread([]() {
        try {
            auto callback = [](const std::wstring& status, int progress) {
                std::lock_guard<std::mutex> lock(g_mutex);
                g_statusText = status;
                g_progress = progress;
            };

            auto stats = g_cleaner.clean(g_options, callback);
            
            // Capture disk space after cleaning
            g_diskFreeAfter = GetDiskFreeSpace();

            std::lock_guard<std::mutex> lock(g_mutex);
            g_resultText = std::format(L"{} fichiers supprimes\n{} liberes",
                stats.filesDeleted, FormatBytes(stats.bytesFreed));
            if (stats.errors > 0) {
                g_resultText += std::format(L"\n({} erreurs)", stats.errors);
                g_errorDetails = std::move(stats.errorDetails);
                g_showErrors = true;
            }
            g_statusText = L"Termine!";
            g_progress = 100;
            g_isRunning = false;
            g_showDiskChart = (g_diskFreeAfter > g_diskFreeBefore);
        } catch (const std::exception& e) {
            std::lock_guard<std::mutex> lock(g_mutex);
            std::string what = e.what();
            g_resultText = L"Erreur: " + std::wstring(what.begin(), what.end());
            g_statusText = L"Erreur!";
            g_progress = 100;
            g_isRunning = false;
        } catch (...) {
            std::lock_guard<std::mutex> lock(g_mutex);
            g_resultText = L"Une erreur inattendue s'est produite";
            g_statusText = L"Erreur!";
            g_progress = 100;
            g_isRunning = false;
        }
    }).detach();
}

void StartEstimate() {
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        g_isRunning = true;
        g_progress = 0;
        g_statusText = L"Analyse en cours...";
        g_estimate = {};
    }

    std::thread([]() {
        try {
            auto callback = [](const std::wstring& status, int progress) {
                std::lock_guard<std::mutex> lock(g_mutex);
                g_statusText = status;
                g_progress = progress;
            };

            auto est = g_cleaner.estimate(g_options, callback);

            std::lock_guard<std::mutex> lock(g_mutex);
            g_estimate = std::move(est);
            g_statusText = L"Analyse terminee!";
            g_progress = 100;
            g_isRunning = false;
            g_showEstimate = true;
        } catch (...) {
            std::lock_guard<std::mutex> lock(g_mutex);
            g_statusText = L"Erreur d'analyse";
            g_progress = 100;
            g_isRunning = false;
        }
    }).detach();
}

void StopCleaning() {
    g_cleaner.stop();
    g_statusText = L"Arret en cours...";
}

void StartMemoryPurge() {
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        g_isMemoryPurging = true;
        g_memoryPurgeResult.clear();
        g_statusText = L"Purge de la memoire...";
    }

    std::thread([]() {
        try {
            auto stats = TempCleaner::Cleaner::purgeMemory();

            std::lock_guard<std::mutex> lock(g_mutex);
            g_memoryPurgeResult = std::format(L"{} liberes\n{} processus optimises",
                FormatBytes(stats.memoryFreed), stats.processesOptimized);
            if (stats.processesFailed > 0) {
                g_memoryPurgeResult += std::format(L"\n({} processus inaccessibles)", stats.processesFailed);
            }
            g_statusText = L"Purge terminee!";
            g_isMemoryPurging = false;
        } catch (...) {
            std::lock_guard<std::mutex> lock(g_mutex);
            g_memoryPurgeResult = L"Erreur lors de la purge";
            g_statusText = L"Erreur!";
            g_isMemoryPurging = false;
        }
    }).detach();
}

int WINAPI wWinMain(HINSTANCE hInstance, HINSTANCE, PWSTR, int nCmdShow) {
    g_options = TempCleaner::Cleaner::loadOptions();

    HINSTANCE hInst = GetModuleHandle(nullptr);
    WNDCLASSEXW wc = { sizeof(wc), CS_CLASSDC, WndProc, 0L, 0L, 
                       hInst, 
                       LoadIcon(hInst, MAKEINTRESOURCE(IDI_TEMPCLEANER)),
                       LoadCursor(nullptr, IDC_ARROW), nullptr, nullptr, 
                       L"TempCleanerClass", 
                       LoadIcon(hInst, MAKEINTRESOURCE(IDI_TEMPCLEANER)) };
    RegisterClassExW(&wc);
    
    HWND hwnd = CreateWindowW(wc.lpszClassName, L"TempCleaner", 
                              WS_OVERLAPPEDWINDOW & ~WS_MAXIMIZEBOX & ~WS_THICKFRAME,
                              100, 100, 500, 600, nullptr, nullptr, wc.hInstance, nullptr);

    if (!CreateDeviceD3D(hwnd)) {
        CleanupDeviceD3D();
        UnregisterClassW(wc.lpszClassName, wc.hInstance);
        return 1;
    }

    ShowWindow(hwnd, nCmdShow);
    UpdateWindow(hwnd);

    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO();
    io.ConfigFlags |= ImGuiConfigFlags_NavEnableKeyboard;
    io.IniFilename = nullptr;
    
    io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\segoeui.ttf", 18.0f);
    
    SetupImGuiStyle();
    
    ImGui_ImplWin32_Init(hwnd);
    ImGui_ImplDX11_Init(g_pd3dDevice, g_pd3dDeviceContext);

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

        // Boutons principaux
        float buttonWidth = 200.0f;
        float buttonHeight = 45.0f;
        float totalWidth = buttonWidth * 2 + 10;
        ImGui::SetCursorPosX((ImGui::GetWindowWidth() - totalWidth) * 0.5f);
        
        if (g_isRunning) {
            ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(0.70f, 0.25f, 0.25f, 1.0f));
            ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(0.80f, 0.30f, 0.30f, 1.0f));
            ImGui::PushStyleColor(ImGuiCol_ButtonActive, ImVec4(0.90f, 0.35f, 0.35f, 1.0f));
            if (ImGui::Button("Arreter", ImVec2(totalWidth, buttonHeight))) {
                StopCleaning();
            }
            ImGui::PopStyleColor(3);
        } else {
            // Bouton Analyser
            ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(0.35f, 0.50f, 0.70f, 1.0f));
            ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(0.40f, 0.55f, 0.75f, 1.0f));
            ImGui::PushStyleColor(ImGuiCol_ButtonActive, ImVec4(0.45f, 0.60f, 0.80f, 1.0f));
            if (ImGui::Button("Analyser", ImVec2(buttonWidth, buttonHeight))) {
                StartEstimate();
            }
            ImGui::PopStyleColor(3);
            
            ImGui::SameLine();
            
            // Bouton Nettoyer
            if (ImGui::Button("Nettoyer", ImVec2(buttonWidth, buttonHeight))) {
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
        ImGui::SetCursorPosX((ImGui::GetWindowWidth() - totalWidth) * 0.5f);
        ImGui::ProgressBar(g_progress / 100.0f, ImVec2(totalWidth, 8), "");

        ImGui::Spacing();
        ImGui::Spacing();

        // Resultat
        {
            std::lock_guard<std::mutex> lock(g_mutex);
            if (!g_resultText.empty()) {
                std::string result = WideToUtf8(g_resultText);
                
                std::istringstream stream(result);
                std::string line;
                while (std::getline(stream, line)) {
                    ImGui::SetCursorPosX((ImGui::GetWindowWidth() - ImGui::CalcTextSize(line.c_str()).x) * 0.5f);
                    ImGui::Text("%s", line.c_str());
                }
                
                // Donut chart - espace disque
                if (g_showDiskChart && g_diskTotal > 0) {
                    ImGui::Spacing();
                    
                    float chartRadius = 35.0f;
                    float chartThickness = 10.0f;
                    ImVec2 chartCenter(ImGui::GetWindowWidth() * 0.5f, ImGui::GetCursorPosY() + chartRadius + 5);
                    
                    // Convert to screen coords
                    ImVec2 windowPos = ImGui::GetWindowPos();
                    ImVec2 screenCenter(windowPos.x + chartCenter.x, windowPos.y + chartCenter.y);
                    
                    float usedBefore = static_cast<float>(g_diskTotal - g_diskFreeBefore) / g_diskTotal;
                    float usedAfter = static_cast<float>(g_diskTotal - g_diskFreeAfter) / g_diskTotal;
                    float freedRatio = usedBefore - usedAfter;
                    
                    DrawDonutChart(screenCenter, chartRadius, chartThickness, usedAfter, freedRatio, true);
                    
                    // Reserve space and add legend
                    ImGui::Dummy(ImVec2(0, chartRadius * 2 + 15));
                    
                    // Compact legend
                    std::string legendText = WideToUtf8(FormatBytes(g_diskFreeAfter)) + " libre";
                    ImGui::SetCursorPosX((ImGui::GetWindowWidth() - ImGui::CalcTextSize(legendText.c_str()).x) * 0.5f);
                    ImGui::TextColored(ImVec4(0.4f, 0.8f, 0.6f, 1.0f), "%s", legendText.c_str());
                }
                
                if (!g_errorDetails.empty()) {
                    ImGui::Spacing();
                    float errBtnWidth = 150.0f;
                    ImGui::SetCursorPosX((ImGui::GetWindowWidth() - errBtnWidth) * 0.5f);
                    ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(0.60f, 0.35f, 0.35f, 1.0f));
                    ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(0.70f, 0.40f, 0.40f, 1.0f));
                    ImGui::PushStyleColor(ImGuiCol_ButtonActive, ImVec4(0.80f, 0.45f, 0.45f, 1.0f));
                    if (ImGui::Button("Voir les erreurs", ImVec2(errBtnWidth, 28))) {
                        g_showErrors = true;
                    }
                    ImGui::PopStyleColor(3);
                }
            }
            
            // Résultat purge mémoire
            if (!g_memoryPurgeResult.empty()) {
                ImGui::Spacing();
                std::string memResult = WideToUtf8(g_memoryPurgeResult);
                
                std::istringstream stream(memResult);
                std::string line;
                while (std::getline(stream, line)) {
                    ImGui::SetCursorPosX((ImGui::GetWindowWidth() - ImGui::CalcTextSize(line.c_str()).x) * 0.5f);
                    ImGui::TextColored(ImVec4(0.6f, 0.8f, 1.0f, 1.0f), "%s", line.c_str());
                }
            }
        }

        // Boutons en bas
        ImGui::SetCursorPos(ImVec2(15, ImGui::GetWindowHeight() - 45));
        if (ImGui::Button("Parametres", ImVec2(100, 30))) {
            g_showSettings = true;
        }
        
        // Bouton Purger memoire en bas a droite
        ImGui::SetCursorPos(ImVec2(ImGui::GetWindowWidth() - 145, ImGui::GetWindowHeight() - 45));
        bool canPurge = !g_isRunning && !g_isMemoryPurging;
        if (!canPurge) {
            ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(0.3f, 0.3f, 0.35f, 1.0f));
            ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(0.3f, 0.3f, 0.35f, 1.0f));
            ImGui::PushStyleColor(ImGuiCol_ButtonActive, ImVec4(0.3f, 0.3f, 0.35f, 1.0f));
        } else {
            ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(0.50f, 0.35f, 0.60f, 1.0f));
            ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(0.55f, 0.40f, 0.65f, 1.0f));
            ImGui::PushStyleColor(ImGuiCol_ButtonActive, ImVec4(0.60f, 0.45f, 0.70f, 1.0f));
        }
        if (ImGui::Button("Purger memoire", ImVec2(130, 30)) && canPurge) {
            g_memoryPurgeResult.clear();
            g_resultText.clear();
            g_showDiskChart = false;
            StartMemoryPurge();
        }
        ImGui::PopStyleColor(3);

        ImGui::End();

        // Fenetre parametres
        if (g_showSettings) {
            ImGui::SetNextWindowSize(ImVec2(380, 480));
            ImGui::Begin("Parametres", &g_showSettings, ImGuiWindowFlags_NoResize);
            
            ImGui::PushStyleVar(ImGuiStyleVar_ItemSpacing, ImVec2(8, 5));
            
            if (ImGui::BeginTabBar("SettingsTabs")) {
                // Onglet Base
                if (ImGui::BeginTabItem("Base")) {
                    ImGui::Checkbox("Temp utilisateur (%TEMP%)", &g_options.cleanUserTemp);
                    ImGui::Checkbox("Temp Windows", &g_options.cleanWindowsTemp);
                    ImGui::Checkbox("Prefetch", &g_options.cleanPrefetch);
                    ImGui::Checkbox("Fichiers recents", &g_options.cleanRecent);
                    ImGui::Checkbox("Corbeille", &g_options.cleanRecycleBin);
                    ImGui::Checkbox("Cache navigateurs", &g_options.cleanBrowserCache);
                    ImGui::EndTabItem();
                }
                
                // Onglet Systeme
                if (ImGui::BeginTabItem("Systeme")) {
                    ImGui::TextColored(ImVec4(1.0f, 0.7f, 0.3f, 1.0f), "Necessite droits admin");
                    ImGui::Checkbox("Cache Windows Update", &g_options.cleanWindowsUpdate);
                    ImGui::Checkbox("Logs systeme", &g_options.cleanSystemLogs);
                    ImGui::Checkbox("Crash dumps", &g_options.cleanCrashDumps);
                    ImGui::Checkbox("Cache miniatures", &g_options.cleanThumbnails);
                    ImGui::Checkbox("Delivery Optimization", &g_options.cleanDeliveryOptimization);
                    ImGui::Checkbox("Windows Installer cache", &g_options.cleanWindowsInstaller);
                    ImGui::Checkbox("Cache polices", &g_options.cleanFontCache);
                    ImGui::Separator();
                    ImGui::Checkbox("Cache DNS", &g_options.cleanDnsCache);
                    ImGui::Checkbox("Raccourcis casses", &g_options.cleanBrokenShortcuts);
                    ImGui::Checkbox("Cache Windows Store", &g_options.cleanWindowsStoreCache);
                    ImGui::Checkbox("Presse-papiers", &g_options.cleanClipboard);
                    ImGui::Checkbox("Fichiers Chkdsk", &g_options.cleanChkdskFiles);
                    ImGui::Checkbox("Cache reseau / IIS", &g_options.cleanNetworkCache);
                    ImGui::EndTabItem();
                }
                
                // Onglet Developpement
                if (ImGui::BeginTabItem("Dev")) {
                    ImGui::TextColored(ImVec4(0.4f, 0.8f, 1.0f, 1.0f), "Caches de build");
                    ImGui::Checkbox("npm cache", &g_options.cleanNpmCache);
                    ImGui::Checkbox("pip cache (Python)", &g_options.cleanPipCache);
                    ImGui::Checkbox("Cargo cache (Rust)", &g_options.cleanCargoCache);
                    ImGui::Checkbox("Go cache", &g_options.cleanGoCache);
                    ImGui::Separator();
                    ImGui::TextColored(ImVec4(0.4f, 0.8f, 1.0f, 1.0f), "IDE");
                    ImGui::Checkbox("Visual Studio cache", &g_options.cleanVSCache);
                    ImGui::Checkbox("VS Code cache", &g_options.cleanVSCodeCache);
                    ImGui::Separator();
                    ImGui::TextColored(ImVec4(1.0f, 0.6f, 0.4f, 1.0f), "Attention: rebuild requis");
                    ImGui::Checkbox("NuGet packages", &g_options.cleanNuGetCache);
                    if (ImGui::IsItemHovered()) {
                        ImGui::SetTooltip("Supprime tous les packages NuGet.\nIls seront re-telecharges au prochain build.");
                    }
                    ImGui::Checkbox("Gradle/Maven cache", &g_options.cleanGradleMavenCache);
                    if (ImGui::IsItemHovered()) {
                        ImGui::SetTooltip("Supprime le cache Java.\nPeut etre volumineux (plusieurs Go).");
                    }
                    ImGui::EndTabItem();
                }
                
                // Onglet GPU/Browser
                if (ImGui::BeginTabItem("GPU")) {
                    ImGui::TextColored(ImVec4(0.4f, 0.8f, 1.0f, 1.0f), "Cache GPU");
                    ImGui::Checkbox("Shader cache (NVIDIA/AMD/Intel)", &g_options.cleanShaderCache);
                    if (ImGui::IsItemHovered()) {
                        ImGui::SetTooltip("Cache DirectX, OpenGL, Vulkan.\nSera regenere au lancement des jeux/apps.");
                    }
                    ImGui::Separator();
                    ImGui::TextColored(ImVec4(0.4f, 0.8f, 1.0f, 1.0f), "Navigateurs (etendu)");
                    ImGui::Checkbox("IndexedDB, Service Workers, etc.", &g_options.cleanBrowserExtended);
                    if (ImGui::IsItemHovered()) {
                        ImGui::SetTooltip("Donnees hors-ligne des sites web.\nVous serez deconnecte de certains sites.");
                    }
                    ImGui::EndTabItem();
                }
                
                // Onglet Danger
                if (ImGui::BeginTabItem("Danger")) {
                    ImGui::TextColored(ImVec4(1.0f, 0.4f, 0.4f, 1.0f), "Options risquees!");
                    ImGui::Spacing();
                    
                    ImGui::Checkbox("Windows.old", &g_options.cleanWindowsOld);
                    if (ImGui::IsItemHovered()) {
                        ImGui::SetTooltip("Supprime l'ancienne installation Windows.\nImpossible de revenir en arriere!");
                    }
                    
                    ImGui::Checkbox("Component Store (WinSxS)", &g_options.cleanComponentStore);
                    if (ImGui::IsItemHovered()) {
                        ImGui::SetTooltip("Nettoie le Component Store avec DISM.\nPeut prendre plusieurs minutes.\nLibere 1-10 Go typiquement.");
                    }
                    
                    ImGui::EndTabItem();
                }
                
                ImGui::EndTabBar();
            }
            
            ImGui::Separator();
            
            if (ImGui::Button("Sauvegarder", ImVec2(100, 28))) {
                TempCleaner::Cleaner::saveOptions(g_options);
                g_showSettings = false;
            }
            ImGui::SameLine();
            if (ImGui::Button("Annuler", ImVec2(100, 28))) {
                g_options = TempCleaner::Cleaner::loadOptions();
                g_showSettings = false;
            }
            
            ImGui::PopStyleVar();
            ImGui::End();
        }

        // Fenetre estimation (copie locale pour éviter de garder le mutex pendant le rendu)
        bool showEstimateLocal = false;
        TempCleaner::CleaningEstimate estimateLocal;
        {
            std::lock_guard<std::mutex> lock(g_mutex);
            showEstimateLocal = g_showEstimate && g_estimate.totalSize > 0;
            if (showEstimateLocal) {
                estimateLocal = g_estimate;
            }
        }
        
        if (showEstimateLocal) {
            ImVec2 popupSize(420, 380);
            ImVec2 popupPos((io.DisplaySize.x - popupSize.x) * 0.5f, 
                           (io.DisplaySize.y - popupSize.y) * 0.5f);
            ImGui::SetNextWindowPos(popupPos, ImGuiCond_Always);
            ImGui::SetNextWindowSize(popupSize, ImGuiCond_Always);
            
            ImGui::Begin("Estimation", &g_showEstimate, 
                ImGuiWindowFlags_NoResize | ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoCollapse);
            
            // Total
            std::string totalStr = WideToUtf8(FormatBytes(estimateLocal.totalSize));
            std::string filesStr = std::format("{} fichiers", estimateLocal.totalFiles);
            ImGui::TextColored(ImVec4(0.4f, 0.8f, 0.6f, 1.0f), "Total estimé: %s", totalStr.c_str());
            ImGui::TextDisabled("%s", filesStr.c_str());
            ImGui::Separator();
            ImGui::Spacing();
            
            // Liste par catégorie (triée par taille décroissante)
            if (ImGui::BeginChild("EstimateList", ImVec2(0, -45), true)) {
                // Trier les catégories par taille
                auto sortedCategories = estimateLocal.categories;
                std::sort(sortedCategories.begin(), sortedCategories.end(),
                    [](const auto& a, const auto& b) { return a.size > b.size; });
                
                for (const auto& cat : sortedCategories) {
                    if (cat.size == 0) continue;
                    
                    std::string catName = WideToUtf8(cat.name);
                    std::string catSize = WideToUtf8(FormatBytes(cat.size));
                    std::string catFiles = std::format("({} fichiers)", cat.fileCount);
                    
                    // Barre de proportion
                    float proportion = static_cast<float>(cat.size) / static_cast<float>(estimateLocal.totalSize);
                    
                    ImGui::Text("%s", catName.c_str());
                    ImGui::SameLine(200);
                    ImGui::TextColored(ImVec4(0.4f, 0.8f, 0.6f, 1.0f), "%s", catSize.c_str());
                    ImGui::SameLine(300);
                    ImGui::TextDisabled("%s", catFiles.c_str());
                    
                    // Petite barre de progression
                    ImGui::ProgressBar(proportion, ImVec2(-1, 4), "");
                    ImGui::Spacing();
                }
            }
            ImGui::EndChild();
            
            // Boutons
            float btnWidth = 120.0f;
            ImGui::SetCursorPosX((ImGui::GetWindowWidth() - btnWidth * 2 - 10) * 0.5f);
            
            if (ImGui::Button("Nettoyer", ImVec2(btnWidth, 32))) {
                g_showEstimate = false;
                g_startCleaningRequested = true;
            }
            ImGui::SameLine();
            if (ImGui::Button("Fermer", ImVec2(btnWidth, 32))) {
                g_showEstimate = false;
            }
            
            ImGui::End();
        }

        // Traiter la demande de nettoyage (en dehors du mutex)
        if (g_startCleaningRequested) {
            g_startCleaningRequested = false;
            StartCleaning();
        }

        // Popup d'avertissement DISM
        if (g_showDismWarning) {
            ImVec2 popupSize(380, 200);
            ImVec2 popupPos((io.DisplaySize.x - popupSize.x) * 0.5f, 
                           (io.DisplaySize.y - popupSize.y) * 0.5f);
            ImGui::SetNextWindowPos(popupPos, ImGuiCond_Always);
            ImGui::SetNextWindowSize(popupSize, ImGuiCond_Always);
            
            ImGui::Begin("Attention", &g_showDismWarning, 
                ImGuiWindowFlags_NoResize | ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoCollapse);
            
            ImGui::TextColored(ImVec4(1.0f, 0.8f, 0.3f, 1.0f), "Component Store (WinSxS) active");
            ImGui::Spacing();
            ImGui::TextWrapped("Le nettoyage du Component Store utilise DISM et peut prendre 2 a 5 minutes.");
            ImGui::Spacing();
            ImGui::TextWrapped("L'interface peut sembler figee pendant ce temps. Vous pouvez annuler avec le bouton Arreter.");
            ImGui::Spacing();
            ImGui::Separator();
            ImGui::Spacing();
            
            float btnWidth = 100.0f;
            ImGui::SetCursorPosX((ImGui::GetWindowWidth() - btnWidth * 2 - 10) * 0.5f);
            
            if (ImGui::Button("Continuer", ImVec2(btnWidth, 30))) {
                g_showDismWarning = false;
                g_startCleaningRequested = true;
            }
            ImGui::SameLine();
            if (ImGui::Button("Annuler", ImVec2(btnWidth, 30))) {
                g_showDismWarning = false;
            }
            
            ImGui::End();
        }

        // Fenetre des erreurs
        {
            std::lock_guard<std::mutex> lock(g_mutex);
            if (g_showErrors && !g_errorDetails.empty()) {
                ImVec2 popupSize(420, 340);
                ImVec2 popupPos((io.DisplaySize.x - popupSize.x) * 0.5f, 
                               (io.DisplaySize.y - popupSize.y) * 0.5f);
                ImGui::SetNextWindowPos(popupPos, ImGuiCond_Always);
                ImGui::SetNextWindowSize(popupSize, ImGuiCond_Always);
                
                ImGui::Begin("Rapport d'erreurs", &g_showErrors, 
                    ImGuiWindowFlags_NoResize | ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoCollapse);
                
                std::string summary = std::format("{} erreur(s)", g_errorDetails.size());
                ImGui::TextColored(ImVec4(1.0f, 0.5f, 0.5f, 1.0f), "%s", summary.c_str());
                ImGui::Separator();
                ImGui::Spacing();
                
                if (ImGui::BeginChild("ErrorList", ImVec2(0, -40), true)) {
                    std::wstring currentCategory;
                    for (size_t i = 0; i < g_errorDetails.size(); i++) {
                        const auto& error = g_errorDetails[i];
                        
                        if (error.category != currentCategory) {
                            currentCategory = error.category;
                            if (i > 0) ImGui::Spacing();
                            std::string cat = WideToUtf8(currentCategory);
                            ImGui::TextColored(ImVec4(1.0f, 0.8f, 0.4f, 1.0f), "[%s]", cat.c_str());
                        }
                        
                        std::string fullPath = WideToUtf8(error.filePath);
                        std::string fileName = fullPath;
                        size_t lastSlash = fullPath.find_last_of("\\/");
                        if (lastSlash != std::string::npos) {
                            fileName = fullPath.substr(lastSlash + 1);
                        }
                        if (fileName.length() > 45) {
                            fileName = fileName.substr(0, 42) + "...";
                        }
                        
                        ImGui::BulletText("%s", fileName.c_str());
                        
                        std::string errorMsg = WideToUtf8(error.errorMessage);
                        ImGui::SetCursorPosX(ImGui::GetCursorPosX() + 20.0f);
                        ImGui::TextColored(ImVec4(1.0f, 0.6f, 0.6f, 1.0f), "%s", errorMsg.c_str());
                    }
                }
                ImGui::EndChild();
                
                float buttonWidth = 100.0f;
                ImGui::SetCursorPosX((ImGui::GetWindowWidth() - buttonWidth) * 0.5f);
                if (ImGui::Button("Fermer", ImVec2(buttonWidth, 30))) {
                    g_showErrors = false;
                }
                
                ImGui::End();
            }
        }

        // Rendu
        ImGui::Render();
        const float clear_color[4] = { 0.10f, 0.10f, 0.12f, 1.00f };
        g_pd3dDeviceContext->OMSetRenderTargets(1, &g_mainRenderTargetView, nullptr);
        g_pd3dDeviceContext->ClearRenderTargetView(g_mainRenderTargetView, clear_color);
        ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());

        g_pSwapChain->Present(1, 0);
    }

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
