cmake_minimum_required(VERSION 3.23)  # FILE_SET requires CMake 3.23+
project(PerfMonitorQt 
    VERSION 1.0.0 
    DESCRIPTION "Moniteur de performances système pour Windows"
    LANGUAGES CXX
)

# ============================================================================
# Configuration C++ et Qt
# ============================================================================

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

set(CMAKE_AUTOMOC ON)
set(CMAKE_AUTORCC ON)
set(CMAKE_AUTOUIC ON)

# Optimisations pour les builds Release
set(CMAKE_INTERPROCEDURAL_OPTIMIZATION_RELEASE ON)

# ============================================================================
# Dépendances Qt6
# ============================================================================

find_package(Qt6 6.5 REQUIRED COMPONENTS 
    Core 
    Gui 
    Widgets 
    Charts
    Network
    Sql
)

qt_standard_project_setup()

# ============================================================================
# Bibliothèques Windows
# ============================================================================

if(WIN32)
    set(PLATFORM_LIBS
        # Performance et système
        pdh iphlpapi setupapi powrprof ntdll psapi advapi32 userenv
        # Graphiques
        dxgi d3d11
        # Shell et utilitaires
        shlwapi ole32 oleaut32 uuid version
        # Tâches et sécurité
        taskschd wintrust crypt32 wevtapi
    )
endif()

# ============================================================================
# Création de l'exécutable
# ============================================================================

add_executable(${PROJECT_NAME} WIN32)

# ============================================================================
# Sources organisées avec FILE_SET (CMake 3.23+)
# ============================================================================

target_sources(${PROJECT_NAME}
    PRIVATE
    # Point d'entrée
    src/main.cpp
    src/mainwindow.cpp
    src/mainwindow.h
)

# Moniteurs système
target_sources(${PROJECT_NAME}
    PRIVATE
    src/monitors/cpumonitor.cpp
    src/monitors/cpumonitor.h
    src/monitors/memorymonitor.cpp
    src/monitors/memorymonitor.h
    src/monitors/gpumonitor.cpp
    src/monitors/gpumonitor.h
    src/monitors/diskmonitor.cpp
    src/monitors/diskmonitor.h
    src/monitors/networkmonitor.cpp
    src/monitors/networkmonitor.h
    src/monitors/batterymonitor.cpp
    src/monitors/batterymonitor.h
    src/monitors/advancedprocessmonitor.cpp
    src/monitors/advancedprocessmonitor.h
    src/monitors/startupmonitor.cpp
    src/monitors/startupmonitor.h
    src/monitors/temperaturemonitor.cpp
    src/monitors/temperaturemonitor.h
    src/monitors/storagehealthmonitor.cpp
    src/monitors/storagehealthmonitor.h
    src/monitors/detailedmemorymonitor.cpp
    src/monitors/detailedmemorymonitor.h
    src/monitors/diskscannermonitor.cpp
    src/monitors/diskscannermonitor.h
    src/monitors/servicemonitor.cpp
    src/monitors/servicemonitor.h
    src/monitors/networkspeedtest.cpp
    src/monitors/networkspeedtest.h
    src/monitors/processimpactmonitor.cpp
    src/monitors/processimpactmonitor.h
)

# Widgets UI
target_sources(${PROJECT_NAME}
    PRIVATE
    src/widgets/sparklinegraph.cpp
    src/widgets/sparklinegraph.h
    src/widgets/systemtray.cpp
    src/widgets/systemtray.h
    src/widgets/processdialog.cpp
    src/widgets/processdialog.h
    src/widgets/advancedprocessdialog.cpp
    src/widgets/advancedprocessdialog.h
    src/widgets/floatingwidget.cpp
    src/widgets/floatingwidget.h
    src/widgets/energymodedialog.cpp
    src/widgets/energymodedialog.h
    src/widgets/startupdialog.cpp
    src/widgets/startupdialog.h
    src/widgets/settingsdialog.cpp
    src/widgets/settingsdialog.h
    src/widgets/advancedprocesswidget.cpp
    src/widgets/advancedprocesswidget.h
    src/widgets/cleanerdialog.cpp
    src/widgets/cleanerdialog.h
    src/widgets/storagehealthdialog.cpp
    src/widgets/storagehealthdialog.h
    src/widgets/detailedmemorydialog.cpp
    src/widgets/detailedmemorydialog.h
    src/widgets/diskscannerdialog.cpp
    src/widgets/diskscannerdialog.h
    src/widgets/toolswidget.cpp
    src/widgets/toolswidget.h
    src/widgets/servicesdialog.cpp
    src/widgets/servicesdialog.h
    src/widgets/historydialog.cpp
    src/widgets/historydialog.h
    src/widgets/interactivechart.cpp
    src/widgets/interactivechart.h
    src/widgets/timerangeselector.cpp
    src/widgets/timerangeselector.h
    src/widgets/comparisonchart.cpp
    src/widgets/comparisonchart.h
    src/widgets/servicehistorydialog.cpp
    src/widgets/servicehistorydialog.h
    src/widgets/networkspeedtestdialog.cpp
    src/widgets/networkspeedtestdialog.h
    src/widgets/processimpactwidget.cpp
    src/widgets/processimpactwidget.h
    src/widgets/processimpactdialog.cpp
    src/widgets/processimpactdialog.h
    src/widgets/tempcleanerdialog.cpp
    src/widgets/tempcleanerdialog.h
)

# Base de données et historique
target_sources(${PROJECT_NAME}
    PRIVATE
    src/database/metricshistory.cpp
    src/database/metricshistory.h
    src/database/servicehistory.cpp
    src/database/servicehistory.h
)

# Utilitaires
target_sources(${PROJECT_NAME}
    PRIVATE
    src/utils/systeminfo.cpp
    src/utils/systeminfo.h
    src/utils/common.h
    src/utils/energymode.cpp
    src/utils/energymode.h
    src/utils/monitorworker.cpp
    src/utils/monitorworker.h
    src/utils/tempcleaner.cpp
    src/utils/tempcleaner.h
    src/utils/tempcleaner_paths.cpp
)

# Nettoyeurs
target_sources(${PROJECT_NAME}
    PRIVATE
    src/cleaners/systemcleaner.cpp
    src/cleaners/systemcleaner.h
)

# Ressources Qt
target_sources(${PROJECT_NAME}
    PRIVATE
    resources/resources.qrc
)

# ============================================================================
# Configuration de la compilation
# ============================================================================

target_include_directories(${PROJECT_NAME} 
    PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/src
)

target_compile_definitions(${PROJECT_NAME} 
    PRIVATE
    $<$<CONFIG:Debug>:QT_DEBUG>
    $<$<CONFIG:Release>:QT_NO_DEBUG>
    UNICODE
    _UNICODE
    WIN32_LEAN_AND_MEAN
    NOMINMAX
)

# Avertissements stricts
if(MSVC)
    target_compile_options(${PROJECT_NAME} PRIVATE /W4)
else()
    target_compile_options(${PROJECT_NAME} PRIVATE -Wall -Wextra -Wpedantic)
endif()

# ============================================================================
# Liaison des bibliothèques
# ============================================================================

target_link_libraries(${PROJECT_NAME} 
    PRIVATE
    Qt6::Core
    Qt6::Gui
    Qt6::Widgets
    Qt6::Charts
    Qt6::Network
    Qt6::Sql
    ${PLATFORM_LIBS}
)

# ============================================================================
# Installation et déploiement
# ============================================================================

install(TARGETS ${PROJECT_NAME}
    BUNDLE DESTINATION .
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
)

# Génération du script de déploiement Qt
qt_generate_deploy_app_script(
    TARGET ${PROJECT_NAME}
    OUTPUT_SCRIPT deploy_script
    NO_UNSUPPORTED_PLATFORM_ERROR
)
install(SCRIPT ${deploy_script})

# ============================================================================
# Déploiement automatique des DLLs Qt (développement)
# ============================================================================

if(WIN32)
    get_target_property(_qmake_executable Qt6::qmake IMPORTED_LOCATION)
    get_filename_component(_qt_bin_dir "${_qmake_executable}" DIRECTORY)
    find_program(WINDEPLOYQT_EXECUTABLE windeployqt HINTS "${_qt_bin_dir}")
    
    if(WINDEPLOYQT_EXECUTABLE)
        add_custom_command(TARGET ${PROJECT_NAME} POST_BUILD
            COMMAND "${WINDEPLOYQT_EXECUTABLE}"
                    --no-translations
                    --no-system-d3d-compiler
                    --no-opengl-sw
                    --no-compiler-runtime
                    "$<TARGET_FILE:${PROJECT_NAME}>"
            COMMENT "Deploying Qt dependencies..."
            VERBATIM
        )
    endif()
endif()
