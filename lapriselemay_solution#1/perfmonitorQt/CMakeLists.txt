cmake_minimum_required(VERSION 3.20)
project(PerfMonitorQt VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_AUTOMOC ON)
set(CMAKE_AUTORCC ON)
set(CMAKE_AUTOUIC ON)

# Find Qt6
find_package(Qt6 REQUIRED COMPONENTS 
    Core 
    Gui 
    Widgets 
    Charts
    Network
    Sql
)

# Windows-specific libraries
if(WIN32)
    set(PLATFORM_LIBS
        pdh
        iphlpapi
        setupapi
        powrprof
        ntdll
        dxgi
        d3d11
        shlwapi
        taskschd
        wintrust
        crypt32
        version
        ole32
        oleaut32
        uuid
        psapi
        advapi32
        userenv
        wevtapi
    )
endif()

# Source files
set(SOURCES
    src/main.cpp
    src/mainwindow.cpp
    # Monitors
    src/monitors/cpumonitor.cpp
    src/monitors/memorymonitor.cpp
    src/monitors/gpumonitor.cpp
    src/monitors/diskmonitor.cpp
    src/monitors/networkmonitor.cpp
    src/monitors/batterymonitor.cpp
    src/monitors/advancedprocessmonitor.cpp
    src/monitors/startupmonitor.cpp
    src/monitors/temperaturemonitor.cpp
    src/monitors/storagehealthmonitor.cpp
    src/monitors/detailedmemorymonitor.cpp
    src/monitors/diskscannermonitor.cpp
    src/monitors/servicemonitor.cpp
    src/monitors/networkspeedtest.cpp
    src/monitors/processimpactmonitor.cpp
    # Widgets
    src/widgets/sparklinegraph.cpp
    src/widgets/systemtray.cpp
    src/widgets/processdialog.cpp
    src/widgets/advancedprocessdialog.cpp
    src/widgets/floatingwidget.cpp
    src/widgets/energymodedialog.cpp
    src/widgets/startupdialog.cpp
    src/widgets/settingsdialog.cpp
    src/widgets/advancedprocesswidget.cpp
    src/widgets/cleanerdialog.cpp
    src/widgets/storagehealthdialog.cpp
    src/widgets/detailedmemorydialog.cpp
    src/widgets/diskscannerdialog.cpp
    src/widgets/toolswidget.cpp
    src/widgets/servicesdialog.cpp
    src/widgets/historydialog.cpp
    src/widgets/interactivechart.cpp
    src/widgets/timerangeselector.cpp
    src/widgets/comparisonchart.cpp
    src/widgets/servicehistorydialog.cpp
    src/widgets/networkspeedtestdialog.cpp
    src/widgets/processimpactwidget.cpp
    src/widgets/processimpactdialog.cpp
    src/widgets/tempcleanerdialog.cpp
    # Database
    src/database/metricshistory.cpp
    src/database/servicehistory.cpp
    # Utils
    src/utils/systeminfo.cpp
    src/utils/energymode.cpp
    src/utils/monitorworker.cpp
    src/utils/tempcleaner.cpp
    src/utils/tempcleaner_paths.cpp
    # Cleaners
    src/cleaners/systemcleaner.cpp
)

set(HEADERS
    src/mainwindow.h
    # Monitors
    src/monitors/cpumonitor.h
    src/monitors/memorymonitor.h
    src/monitors/gpumonitor.h
    src/monitors/diskmonitor.h
    src/monitors/networkmonitor.h
    src/monitors/batterymonitor.h
    src/monitors/advancedprocessmonitor.h
    src/monitors/startupmonitor.h
    src/monitors/temperaturemonitor.h
    src/monitors/storagehealthmonitor.h
    src/monitors/detailedmemorymonitor.h
    src/monitors/diskscannermonitor.h
    src/monitors/servicemonitor.h
    src/monitors/networkspeedtest.h
    src/monitors/processimpactmonitor.h
    # Widgets
    src/widgets/sparklinegraph.h
    src/widgets/systemtray.h
    src/widgets/processdialog.h
    src/widgets/advancedprocessdialog.h
    src/widgets/floatingwidget.h
    src/widgets/energymodedialog.h
    src/widgets/startupdialog.h
    src/widgets/settingsdialog.h
    src/widgets/advancedprocesswidget.h
    src/widgets/cleanerdialog.h
    src/widgets/storagehealthdialog.h
    src/widgets/detailedmemorydialog.h
    src/widgets/diskscannerdialog.h
    src/widgets/toolswidget.h
    src/widgets/servicesdialog.h
    src/widgets/historydialog.h
    src/widgets/interactivechart.h
    src/widgets/timerangeselector.h
    src/widgets/comparisonchart.h
    src/widgets/servicehistorydialog.h
    src/widgets/networkspeedtestdialog.h
    src/widgets/processimpactwidget.h
    src/widgets/processimpactdialog.h
    src/widgets/tempcleanerdialog.h
    # Database
    src/database/metricshistory.h
    src/database/servicehistory.h
    # Utils
    src/utils/systeminfo.h
    src/utils/common.h
    src/utils/energymode.h
    src/utils/monitorworker.h
    src/utils/tempcleaner.h
    # Cleaners
    src/cleaners/systemcleaner.h
)

# Resources
set(RESOURCES
    resources/resources.qrc
)

# Create executable
add_executable(${PROJECT_NAME} WIN32
    ${SOURCES}
    ${HEADERS}
    ${RESOURCES}
)

# Link libraries
target_link_libraries(${PROJECT_NAME} PRIVATE
    Qt6::Core
    Qt6::Gui
    Qt6::Widgets
    Qt6::Charts
    Qt6::Network
    Qt6::Sql
    ${PLATFORM_LIBS}
)

# Include directories
target_include_directories(${PROJECT_NAME} PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/src
)

# Compiler definitions
target_compile_definitions(${PROJECT_NAME} PRIVATE
    $<$<CONFIG:Debug>:QT_DEBUG>
    $<$<CONFIG:Release>:QT_NO_DEBUG>
    UNICODE
    _UNICODE
    WIN32_LEAN_AND_MEAN
    NOMINMAX
)

# Install rules
install(TARGETS ${PROJECT_NAME}
    BUNDLE DESTINATION .
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
)

# Qt deployment
qt_generate_deploy_app_script(
    TARGET ${PROJECT_NAME}
    OUTPUT_SCRIPT deploy_script
    NO_UNSUPPORTED_PLATFORM_ERROR
)
install(SCRIPT ${deploy_script})

# Auto-deploy Qt DLLs after build (for development)
if(WIN32)
    # Find windeployqt
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
