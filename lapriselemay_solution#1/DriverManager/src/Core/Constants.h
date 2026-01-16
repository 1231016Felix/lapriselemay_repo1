#pragma once

#include <Windows.h>
#include <cstdint>
#include <ctime>

namespace DriverManager {
namespace Constants {

    // ============================================================================
    // Timeouts (millisecondes)
    // ============================================================================
    constexpr DWORD HTTP_CONNECT_TIMEOUT_MS = 5000;
    constexpr DWORD HTTP_SEND_TIMEOUT_MS = 10000;
    constexpr DWORD HTTP_RECEIVE_TIMEOUT_MS = 15000;
    constexpr DWORD PROCESS_TIMEOUT_MS = 60000;
    constexpr DWORD INSTALL_TIMEOUT_MS = 300000;  // 5 minutes

    // ============================================================================
    // Cache
    // ============================================================================
    constexpr time_t CACHE_DURATION_SECONDS = 86400;  // 24 heures

    // ============================================================================
    // Limites
    // ============================================================================
    constexpr int MAX_CONCURRENT_DOWNLOADS = 6;
    constexpr int MAX_CATALOG_RESULTS = 15;
    constexpr int MAX_FOLDER_SCAN_DEPTH = 500;
    constexpr int MAX_DRIVERS_DISPLAY = 1000;

    // ============================================================================
    // Âge des pilotes (jours)
    // ============================================================================
    constexpr int DRIVER_AGE_OLD_DAYS = 365;       // 1 an
    constexpr int DRIVER_AGE_VERY_OLD_DAYS = 730;  // 2 ans

    // ============================================================================
    // Dimensions UI
    // ============================================================================
    namespace UI {
        // Panneaux principaux
        constexpr float CATEGORIES_PANEL_WIDTH = 180.0f;
        constexpr float DETAILS_PANEL_WIDTH = 300.0f;
        constexpr float PANEL_SPACING = 16.0f;
        
        // Fenêtre principale
        constexpr int DEFAULT_WINDOW_WIDTH = 1200;
        constexpr int DEFAULT_WINDOW_HEIGHT = 800;
        constexpr int MIN_WINDOW_WIDTH = 800;
        constexpr int MIN_WINDOW_HEIGHT = 600;
        
        // Fenêtres modales
        constexpr float ABOUT_WINDOW_WIDTH = 400.0f;
        constexpr float ABOUT_WINDOW_HEIGHT = 200.0f;
        constexpr float UPDATE_PROGRESS_WIDTH = 500.0f;
        constexpr float UPDATE_PROGRESS_HEIGHT = 200.0f;
        constexpr float DRIVER_STORE_WIDTH = 900.0f;
        constexpr float DRIVER_STORE_HEIGHT = 550.0f;
        constexpr float BSOD_ANALYZER_WIDTH = 1000.0f;
        constexpr float BSOD_ANALYZER_HEIGHT = 600.0f;
        constexpr float DOWNLOAD_WINDOW_WIDTH = 800.0f;
        constexpr float DOWNLOAD_WINDOW_HEIGHT = 500.0f;
        constexpr float UPDATE_HELP_WIDTH = 580.0f;
        constexpr float UPDATE_HELP_HEIGHT = 520.0f;
        
        // Éléments
        constexpr float TOOLBAR_BUTTON_PADDING_X = 10.0f;
        constexpr float TOOLBAR_BUTTON_PADDING_Y = 6.0f;
        constexpr float SEARCH_FIELD_WIDTH = 200.0f;
        constexpr float STATUS_BAR_HEIGHT = 30.0f;
        constexpr float PROGRESS_BAR_WIDTH = 200.0f;
        
        // Table des pilotes
        constexpr float COLUMN_NAME_WIDTH = 180.0f;
        constexpr float COLUMN_MANUFACTURER_WIDTH = 100.0f;
        constexpr float COLUMN_VERSION_WIDTH = 70.0f;
        constexpr float COLUMN_DATE_WIDTH = 80.0f;
        constexpr float COLUMN_AGE_WIDTH = 70.0f;
        constexpr float COLUMN_STATUS_WIDTH = 70.0f;
        constexpr float GROUP_INDENT = 20.0f;
    }

    // ============================================================================
    // Couleurs UI (format ImVec4: R, G, B, A)
    // ============================================================================
    namespace Colors {
        // Status
        constexpr float STATUS_OK[4] = {0.2f, 0.8f, 0.2f, 1.0f};
        constexpr float STATUS_WARNING[4] = {0.9f, 0.7f, 0.0f, 1.0f};
        constexpr float STATUS_ERROR[4] = {0.9f, 0.2f, 0.2f, 1.0f};
        constexpr float STATUS_DISABLED[4] = {0.5f, 0.5f, 0.5f, 1.0f};
        constexpr float STATUS_UNKNOWN[4] = {0.7f, 0.7f, 0.7f, 1.0f};
        
        // Âge des pilotes
        constexpr float AGE_CURRENT[4] = {0.2f, 0.8f, 0.2f, 1.0f};
        constexpr float AGE_OLD[4] = {0.9f, 0.7f, 0.0f, 1.0f};
        constexpr float AGE_VERY_OLD[4] = {0.9f, 0.4f, 0.1f, 1.0f};
        
        // UI
        constexpr float HEADER_TEXT[4] = {0.4f, 0.7f, 1.0f, 1.0f};
        constexpr float LABEL_TEXT[4] = {0.6f, 0.6f, 0.7f, 1.0f};
        constexpr float SUCCESS_TEXT[4] = {0.4f, 0.9f, 0.4f, 1.0f};
        constexpr float WARNING_BANNER[4] = {0.6f, 0.4f, 0.0f, 0.3f};
        constexpr float WARNING_TEXT[4] = {1.0f, 0.8f, 0.2f, 1.0f};
        
        // Boutons spéciaux
        constexpr float BUTTON_UPDATE[4] = {0.55f, 0.35f, 0.15f, 0.70f};
        constexpr float BUTTON_UPDATE_HOVER[4] = {0.65f, 0.45f, 0.20f, 0.85f};
        constexpr float BUTTON_DELETE[4] = {0.8f, 0.2f, 0.2f, 0.7f};
        constexpr float BUTTON_DELETE_HOVER[4] = {0.9f, 0.3f, 0.3f, 0.85f};
        constexpr float BUTTON_DOWNLOAD[4] = {0.2f, 0.5f, 0.8f, 0.7f};
        constexpr float BUTTON_DOWNLOAD_HOVER[4] = {0.3f, 0.6f, 0.9f, 0.85f};
        constexpr float BUTTON_INSTALL[4] = {0.2f, 0.7f, 0.3f, 0.7f};
        constexpr float BUTTON_INSTALL_HOVER[4] = {0.3f, 0.8f, 0.4f, 0.85f};
    }

    // ============================================================================
    // Textes de l'interface (pour centraliser les traductions)
    // ============================================================================
    namespace Text {
        // Menu
        constexpr const char* MENU_FILE = "Fichier";
        constexpr const char* MENU_VIEW = "Affichage";
        constexpr const char* MENU_TOOLS = "Outils";
        constexpr const char* MENU_HELP = "Aide";
        
        // Actions
        constexpr const char* ACTION_SCAN = "Scanner";
        constexpr const char* ACTION_STOP = "Arr" "\xc3\xaa" "ter";
        constexpr const char* ACTION_ENABLE = "Activer";
        constexpr const char* ACTION_DISABLE = "D" "\xc3\xa9" "sactiver";
        constexpr const char* ACTION_UNINSTALL = "D" "\xc3\xa9" "sinstaller";
        constexpr const char* ACTION_CHECK_UPDATES = "V" "\xc3\xa9" "rifier MAJ";
        constexpr const char* ACTION_DOWNLOAD = "T" "\xc3\xa9" "l" "\xc3\xa9" "charger";
        constexpr const char* ACTION_INSTALL = "Installer";
        constexpr const char* ACTION_CANCEL = "Annuler";
        constexpr const char* ACTION_CLOSE = "Fermer";
        constexpr const char* ACTION_REFRESH = "Actualiser";
        
        // Filtres
        constexpr const char* FILTER_OLD_DRIVERS = "Anciens (>2 ans)";
        constexpr const char* FILTER_SEARCH_HINT = "Rechercher...";
        
        // Catégories
        constexpr const char* CATEGORY_ALL = "Tous les pilotes";
        
        // Messages
        constexpr const char* MSG_SCAN_COMPLETE = "Scan termin" "\xc3\xa9";
        constexpr const char* MSG_NO_UPDATES = "Tous les pilotes sont " "\xc3\xa0" " jour";
        constexpr const char* MSG_ADMIN_REQUIRED = "Red" "\xc3\xa9" "marrez en tant qu'administrateur";
    }

}} // namespace DriverManager::Constants
