# migrate_proxies.ps1
# Migre les accès proxy AppSettings vers les sections (Search, Appearance, Integrations)

$base = "C:\git\lapriselemay_solution#1\QuickLauncher"

# Files to skip
$skipFiles = @("AppSettings.cs")

# Build replacement rules: each rule is [regex_pattern, replacement]
# The pattern matches ".PropName" preceded by a word character, NOT already sectioned
$rules = @()

# Search section properties
$searchProps = @(
    "MaxResults", "SearchDepth", "IndexHiddenFolders", "IndexBrowserBookmarks",
    "EnableAliases", "SystemSearchDepth", "EnableSearchHistory", "MaxSearchHistory",
    "SearchHistory", "ScoringWeights", "IndexedFolders", "FileExtensions",
    "PinnedItems", "Scripts", "SearchEngines", "EnableFileWatcher",
    "AutoReindexEnabled", "AutoReindexMode", "AutoReindexIntervalMinutes",
    "AutoReindexScheduledTime"
)

$appearanceProps = @(
    "Theme", "ThemeMode", "AccentColor", "WindowOpacity", "WindowPosition",
    "LastWindowLeft", "LastWindowTop", "ShowInTaskbar", "ShowSettingsButton",
    "ShowPreviewPanel", "ShowShortcutHints", "ShowCategoryBadges",
    "ShowIndexingStatus", "ShowGhostSuggestions", "EnableAnimations",
    "AnimationDurationMs", "AnimationStyle", "StaggerDelayMs",
    "AutoThemeLightStart", "AutoThemeDarkStart", "LightThemeStartTime",
    "DarkThemeStartTime"
)

$integrationProps = @(
    "WeatherCity", "WeatherUnit", "TranslateTargetLang", "TranslateSourceLang",
    "AiProvider", "AiApiUrl", "AiApiKey", "AiModel", "AiSystemPrompt",
    "NoteWidgets", "TimerWidgets", "Notes"
)

# Search methods to migrate
$searchMethods = @(
    "PinItem", "UnpinItem", "IsPinned", "MovePinnedItemUp", "MovePinnedItemDown",
    "AddToSearchHistory", "ClearSearchHistory"
)

$totalChanges = 0
$filesChanged = 0

# Get all .cs files, excluding bin/obj/Settings
$files = Get-ChildItem -Path $base -Recurse -Filter "*.cs" |
    Where-Object { 
        $_.FullName -notmatch "\\bin\\" -and 
        $_.FullName -notmatch "\\obj\\" -and
        $_.FullName -notmatch "\\Settings\\" -and
        $_.Name -notin $skipFiles
    }

foreach ($file in $files) {
    $content = Get-Content -Path $file.FullName -Raw -Encoding UTF8
    $original = $content
    $fileChanges = 0
    
    # Process each property
    foreach ($prop in $searchProps) {
        # Match .PropName but NOT .Search.PropName, NOT in comments, NOT enum refs
        # Pattern: word-char followed by .PropName followed by non-word-or-dot
        $pattern = "(?<=\w)\.${prop}(?=\b)(?![\w.])"
        $replacement = ".Search.${prop}"
        
        # Count matches (excluding already sectioned and special cases)
        $matches = [regex]::Matches($content, $pattern) | Where-Object {
            $line = $content.Substring(0, $_.Index).Split("`n")[-1]
            $fullLine = ($content.Split("`n") | Where-Object { $_ -match [regex]::Escape($_.Value) } | Select-Object -First 1)
            # Skip if already has section prefix
            $content.Substring([Math]::Max(0, $_.Index - 8), 8) -notmatch "Search\." -and
            $content.Substring([Math]::Max(0, $_.Index - 12), 12) -notmatch "Appearance\." -and
            $content.Substring([Math]::Max(0, $_.Index - 14), 14) -notmatch "Integrations\."
        }
        
        $content = $content -replace $pattern, $replacement
        $fileChanges += $matches.Count
    }
    
    foreach ($prop in $appearanceProps) {
        $pattern = "(?<=\w)\.${prop}(?=\b)(?![\w.])"
        $replacement = ".Appearance.${prop}"
        $before = $content
        $content = $content -replace $pattern, $replacement
        if ($content -ne $before) { $fileChanges++ }
    }
    
    foreach ($prop in $integrationProps) {
        $pattern = "(?<=\w)\.${prop}(?=\b)(?![\w.])"
        $replacement = ".Integrations.${prop}"
        $before = $content
        $content = $content -replace $pattern, $replacement
        if ($content -ne $before) { $fileChanges++ }
    }
    
    foreach ($method in $searchMethods) {
        $pattern = "(?<=\w)\.${method}\("
        $replacement = ".Search.${method}("
        $before = $content
        $content = $content -replace $pattern, $replacement
        if ($content -ne $before) { $fileChanges++ }
    }
    
    if ($content -ne $original) {
        $filesChanged++
        $relPath = $file.FullName.Substring($base.Length + 1)
        Write-Output "Changed: $relPath"
        Set-Content -Path $file.FullName -Value $content -Encoding UTF8 -NoNewline
        $totalChanges += $fileChanges
    }
}

Write-Output ""
Write-Output "Done: $totalChanges property groups changed in $filesChanged files"
