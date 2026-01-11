@echo off
echo Applying modifications to main.cpp...

:: Backup original
copy /Y "C:\git\lapriselemay_solution#1\DriverManager\main.cpp" "C:\git\lapriselemay_solution#1\DriverManager\main.cpp.backup"

:: Create PowerShell script to do the modifications
powershell -Command ^
"$content = Get-Content 'C:\git\lapriselemay_solution#1\DriverManager\main.cpp' -Raw; ^
$content = $content -replace '#include \"src/DriverStoreCleanup.h\"', '#include \"src/DriverStoreCleanup.h\"`n#include \"src/DriverDownloader.h\"'; ^
$content = $content -replace 'DriverManager::DriverStoreCleanup driverStoreCleanup;  // For DriverStore cleanup', 'DriverManager::DriverStoreCleanup driverStoreCleanup;  // For DriverStore cleanup`n    DriverManager::DriverDownloader driverDownloader;       // For driver downloads'; ^
$content = $content -replace 'bool showDriverStoreCleanup = false;    // DriverStore cleanup window', 'bool showDriverStoreCleanup = false;    // DriverStore cleanup window`n    bool showDownloadWindow = false;         // Download manager window`n    bool createRestorePoint = false;         // Option for restore point'; ^
Set-Content 'C:\git\lapriselemay_solution#1\DriverManager\main.cpp' -Value $content -NoNewline"

echo Done! Now you need to manually add RenderDownloadWindow function.
echo See: src\main_download_window.cpp.txt
echo And: src\MODIFICATIONS_MAIN.txt

pause
