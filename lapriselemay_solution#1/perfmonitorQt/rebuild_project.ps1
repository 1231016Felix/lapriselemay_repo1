# rebuild_project.ps1
# Script pour régénérer le projet Visual Studio de PerfMonitorQt

$ErrorActionPreference = "Stop"
$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$BuildDir = Join-Path $ProjectDir "build"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  PerfMonitorQt - Rebuild Project" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Trouver CMake
$cmakePaths = @(
    "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe",
    "C:\Program Files\CMake\bin\cmake.exe"
)

$cmake = $null
foreach ($path in $cmakePaths) {
    if (Test-Path $path) {
        $cmake = $path
        break
    }
}

# Essayer de trouver cmake dans le PATH
if (-not $cmake) {
    $cmake = Get-Command cmake -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
}

if (-not $cmake) {
    Write-Host "ERREUR: CMake non trouvé!" -ForegroundColor Red
    Write-Host "Installez CMake ou utilisez Visual Studio Developer PowerShell" -ForegroundColor Yellow
    exit 1
}

Write-Host "CMake trouvé: $cmake" -ForegroundColor Green
Write-Host ""

# Étape 1: Supprimer le dossier build
Write-Host "[1/3] Suppression du dossier build..." -ForegroundColor Yellow
if (Test-Path $BuildDir) {
    Remove-Item -Recurse -Force $BuildDir
    Write-Host "      Dossier build supprimé." -ForegroundColor Green
} else {
    Write-Host "      Dossier build n'existait pas." -ForegroundColor Gray
}

# Étape 2: Créer le nouveau dossier build
Write-Host "[2/3] Création du dossier build..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path $BuildDir | Out-Null
Write-Host "      Dossier build créé." -ForegroundColor Green

# Étape 3: Générer le projet Visual Studio
Write-Host "[3/3] Génération du projet Visual Studio..." -ForegroundColor Yellow
Push-Location $BuildDir
try {
    & $cmake .. -G "Visual Studio 17 2022" -A x64
    if ($LASTEXITCODE -ne 0) {
        throw "CMake a échoué"
    }
    Write-Host "      Projet généré avec succès!" -ForegroundColor Green
} catch {
    Write-Host "ERREUR: $($_.Exception.Message)" -ForegroundColor Red
    Pop-Location
    exit 1
}
Pop-Location

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Terminé avec succès!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Solution: $BuildDir\PerfMonitorQt.sln" -ForegroundColor White
Write-Host ""

# Demander si on ouvre la solution
$response = Read-Host "Ouvrir la solution dans Visual Studio? (O/n)"
if ($response -eq "" -or $response -eq "O" -or $response -eq "o" -or $response -eq "Y" -or $response -eq "y") {
    $slnPath = Join-Path $BuildDir "PerfMonitorQt.sln"
    if (Test-Path $slnPath) {
        Write-Host "Ouverture de la solution..." -ForegroundColor Cyan
        Start-Process $slnPath
    }
}
