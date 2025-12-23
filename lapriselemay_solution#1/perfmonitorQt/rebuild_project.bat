@echo off
setlocal

echo ========================================
echo   PerfMonitorQt - Rebuild Project
echo ========================================
echo.

set "PROJECT_DIR=%~dp0"
set "BUILD_DIR=%PROJECT_DIR%build"
set "CMAKE_EXE=C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
set "QT_PATH=C:/Qt/6.10.1/msvc2022_64"

echo CMake: %CMAKE_EXE%
echo Qt: %QT_PATH%
echo.

REM Supprimer le dossier build
echo [1/3] Suppression du dossier build...
if exist "%BUILD_DIR%" (
    rmdir /s /q "%BUILD_DIR%"
    echo       Dossier build supprime.
) else (
    echo       Dossier build n'existait pas.
)

REM Creer le dossier build
echo [2/3] Creation du dossier build...
mkdir "%BUILD_DIR%"
echo       Dossier build cree.

REM Generer le projet
echo [3/3] Generation du projet Visual Studio 2026...
cd /d "%BUILD_DIR%"
"%CMAKE_EXE%" .. -G "Visual Studio 18 2026" -A x64 -DCMAKE_PREFIX_PATH="%QT_PATH%"
if %errorlevel% neq 0 (
    echo ERREUR: CMake a echoue!
    pause
    exit /b 1
)

echo.
echo ========================================
echo   Termine avec succes!
echo ========================================
echo.
echo Solution: %BUILD_DIR%\PerfMonitorQt.sln
echo.

set /p OPEN_SOL="Ouvrir la solution dans Visual Studio? (O/n): "
if /i "%OPEN_SOL%"=="" goto open_solution
if /i "%OPEN_SOL%"=="o" goto open_solution
if /i "%OPEN_SOL%"=="y" goto open_solution
goto end

:open_solution
echo Ouverture de la solution...
start "" "%BUILD_DIR%\PerfMonitorQt.sln"

:end
endlocal
