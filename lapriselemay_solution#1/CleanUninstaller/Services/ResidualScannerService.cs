using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using Microsoft.Win32;
using CleanUninstaller.Models;
using CleanUninstaller.Helpers;

namespace CleanUninstaller.Services;

/// <summary>
/// Service de scan des résidus avancé inspiré de BCUninstaller
/// Détecte les fichiers, dossiers, clés de registre, services et tâches planifiées orphelins
/// SÉCURISÉ: Protège les fichiers système, SDK, et outils de développement
/// </summary>
public partial class ResidualScannerService
{
    // Cache pour les vérifications de chemins protégés (optimisation performance)
    private static readonly ConcurrentDictionary<string, bool> _protectedPathCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, long> _directorySizeCache = new(StringComparer.OrdinalIgnoreCase);
    
    #region Protected Paths and Folders - CRITICAL SAFETY

    /// <summary>
    /// Dossiers ABSOLUMENT protégés - Ne jamais suggérer leur suppression
    /// </summary>
    private static readonly HashSet<string> ProtectedFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Windows Core
        "Windows", "System32", "SysWOW64", "WinSxS", "assembly",
        "Microsoft.NET", "dotnet", ".NET", "Reference Assemblies",
        
        // Visual Studio et outils de développement
        "Microsoft Visual Studio", "Visual Studio", "Visual Studio 2019", "Visual Studio 2022",
        "Visual Studio 2017", "Visual Studio 14.0", "Visual Studio 15.0", "Visual Studio 16.0",
        "Visual Studio 17.0", "VS2019", "VS2022", "VC", "MSVC",
        "MSBuild", "Microsoft SDKs", "Windows Kits", "Windows SDK",
        "Debug Interface Access", "DIA SDK", "Debuggers",
        
        // SDK et runtimes
        "Android", "Android SDK", "Java", "JDK", "JRE", "Oracle",
        "Python", "Python27", "Python38", "Python39", "Python310", "Python311", "Python312",
        "nodejs", "node_modules", "npm", "Go", "Rust", "cargo",
        
        // Frameworks et runtimes Windows
        "IIS", "IIS Express", "SQL Server", "Microsoft SQL Server",
        "Windows Defender", "Windows Security", "WindowsPowerShell", "PowerShell",
        "Package Cache", "installer", "Uninstall Information",
        "Internet Explorer", "Microsoft Edge", "Edge",
        
        // Composants système
        "Windows Mail", "Windows Media Player", "Windows NT",
        "Windows Photo Viewer", "Windows Portable Devices", "Windows Sidebar",
        "WindowsApps", "SystemApps", "Microsoft Office", "Office",
        
        // Outils de développement tiers courants
        "Git", "GitHub", "GitLab", "Perforce", "SVN", "TortoiseSVN", "TortoiseGit",
        "CMake", "Ninja", "LLVM", "Clang", "MinGW", "Cygwin", "MSYS2",
        "Docker", "Containers", "WSL", "Linux",
        
        // Pilotes et matériel
        "NVIDIA", "NVIDIA Corporation", "AMD", "Intel", "Realtek",
        "drivers", "DriverStore",
        
        // Sécurité
        "Kaspersky", "Norton", "Avast", "AVG", "Bitdefender", "McAfee", "ESET",
        "Malwarebytes", "Windows Defender Advanced Threat Protection",
        
        // Autres critiques
        "Common Files", "Uninstall Information", "MicrosoftEdgeWebView"
    };

    /// <summary>
    /// Chemins complets protégés (patterns)
    /// </summary>
    private static readonly string[] ProtectedPathPatterns =
    [
        @"Program Files\Microsoft Visual Studio",
        @"Program Files (x86)\Microsoft Visual Studio",
        @"Program Files\Windows Kits",
        @"Program Files (x86)\Windows Kits",
        @"Program Files\Microsoft SDKs",
        @"Program Files (x86)\Microsoft SDKs",
        @"Program Files\dotnet",
        @"Program Files\Microsoft SQL Server",
        @"Program Files (x86)\Microsoft SQL Server",
        @"Program Files\IIS",
        @"Program Files\IIS Express",
        @"Program Files (x86)\IIS Express",
        @"Program Files\Git",
        @"Program Files\NVIDIA",
        @"Program Files\NVIDIA Corporation",
        @"Program Files\AMD",
        @"Program Files\Intel",
        @"Program Files\Common Files\Microsoft",
        @"Program Files (x86)\Common Files\Microsoft",
        @"Program Files\Reference Assemblies",
        @"Program Files (x86)\Reference Assemblies",
        @"Program Files\MSBuild",
        @"Program Files (x86)\MSBuild",
        @"Program Files\PowerShell",
        @"ProgramData\Microsoft",
        @"ProgramData\Package Cache",
        @"Windows\Microsoft.NET",
        @"Windows\assembly",
        @"Windows\System32",
        @"Windows\SysWOW64",
        @"Windows\WinSxS"
    ];

    /// <summary>
    /// Clés de registre protégées (ne jamais supprimer)
    /// </summary>
    private static readonly HashSet<string> ProtectedRegistryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Windows", "Classes", "Policies", "CurrentVersion",
        "Explorer", "Shell", "Run", "RunOnce", "Uninstall",
        ".NET", "dotnet", "Visual Studio", "MSBuild", "DevDiv",
        "NVIDIA", "AMD", "Intel", "Realtek",
        "MicrosoftEdge", "Edge", "Internet Explorer"
    };

    /// <summary>
    /// Mots-clés trop génériques qui ne doivent PAS déclencher une détection seuls
    /// </summary>
    private static readonly HashSet<string> TooGenericKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Termes graphiques/média (pourraient matcher des SDK)
        "video", "audio", "media", "graphics", "image", "picture", "photo",
        "sound", "music", "player", "viewer", "display", "screen", "render",
        "codec", "encoder", "decoder", "stream", "recording", "capture",
        
        // Termes de communication (pourraient matcher des SDK)
        "communication", "network", "connect", "share", "sync", "cloud",
        "remote", "online", "web", "internet", "download", "upload",
        
        // Termes de zoom/vue (confusion avec Zoom app)
        "zoom", "scale", "view", "window", "pane", "panel",
        
        // Termes système génériques
        "system", "service", "helper", "host", "client", "server",
        "manager", "monitor", "updater", "installer", "setup",
        "runtime", "framework", "library", "component", "module",
        "driver", "device", "hardware", "software",
        
        // Termes de données
        "data", "cache", "temp", "log", "config", "settings", "preferences",
        "backup", "restore", "recovery",
        
        // Termes d'application génériques
        "app", "application", "program", "tool", "utility", "launcher"
    };

    #endregion

    // Dossiers communs à scanner (données utilisateur)
    private static readonly string[] CommonDataFolders =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
        Environment.GetFolderPath(Environment.SpecialFolder.Recent),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        // Dossiers d'installation courants
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Common Files"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Common Files"),
        @"C:\Program Files",
        @"C:\Program Files (x86)",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "apps"),
    ];

    // Clés de registre à scanner
    private static readonly (string Path, RegistryKey Root)[] RegistryPaths =
    [
        (@"SOFTWARE", Registry.CurrentUser),
        (@"SOFTWARE", Registry.LocalMachine),
        (@"SOFTWARE\WOW6432Node", Registry.LocalMachine),
        (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", Registry.CurrentUser),
        (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", Registry.LocalMachine),
        (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders", Registry.CurrentUser),
        (@"SOFTWARE\Classes", Registry.CurrentUser),
        (@"SOFTWARE\Classes", Registry.LocalMachine)
    ];


    #region Path Protection Methods

    /// <summary>
    /// Vérifie si un chemin est protégé et ne doit JAMAIS être supprimé (avec cache)
    /// </summary>
    private static bool IsProtectedPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return true; // Par sécurité

        // Utiliser le cache pour éviter les recalculs
        if (_protectedPathCache.TryGetValue(path, out var cached))
            return cached;

        var isProtected = CheckPathProtection(path);
        _protectedPathCache.TryAdd(path, isProtected);
        return isProtected;
    }

    /// <summary>
    /// Vérifie effectivement si un chemin est protégé
    /// </summary>
    private static bool CheckPathProtection(string path)
    {
        var normalizedPath = path.Replace('/', '\\').TrimEnd('\\');

        // Vérifier les patterns de chemins protégés
        foreach (var pattern in ProtectedPathPatterns)
        {
            if (normalizedPath.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Vérifier le nom du dossier
        var folderName = Path.GetFileName(normalizedPath);
        if (ProtectedFolderNames.Contains(folderName))
            return true;

        // Vérifier les dossiers parents
        var parts = normalizedPath.Split('\\');
        foreach (var part in parts)
        {
            if (ProtectedFolderNames.Contains(part))
                return true;
        }

        // Protection spéciale: tout ce qui contient "SDK", "Kit", "Visual Studio"
        if (normalizedPath.Contains("SDK", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Contains("Kit", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Contains("Visual Studio", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Contains("Debugger", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Vérifie si une clé de registre est protégée
    /// </summary>
    private static bool IsProtectedRegistryPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return true;

        // Ne jamais toucher aux clés Microsoft/Windows directement
        if (path.Contains(@"\Microsoft\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(@"\Windows\", StringComparison.OrdinalIgnoreCase))
        {
            // Exception: les clés spécifiques d'applications tierces sous Microsoft
            // Ex: HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AppName
            // Mais pas les clés système comme Run, Shell, etc.
            var lastPart = path.Split('\\').LastOrDefault() ?? "";
            if (ProtectedRegistryKeys.Contains(lastPart))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Filtre les mots-clés pour ne garder que les spécifiques au programme
    /// </summary>
    private static HashSet<string> FilterKeywords(HashSet<string> keywords)
    {
        var filtered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var keyword in keywords)
        {
            // Ignorer les mots-clés trop courts
            if (keyword.Length < 4) continue;
            
            // Ignorer les mots-clés trop génériques
            if (TooGenericKeywords.Contains(keyword)) continue;
            
            // Ignorer les mots-clés qui sont des noms de dossiers protégés
            if (ProtectedFolderNames.Contains(keyword)) continue;
            
            filtered.Add(keyword);
        }

        return filtered;
    }

    #endregion


    #region Main Scan Methods

    /// <summary>
    /// Scan complet des résidus pour un programme
    /// </summary>
    public async Task<List<ResidualItem>> ScanAsync(
        InstalledProgram program,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await ScanResidualsAsync(program, progress, cancellationToken);
    }

    /// <summary>
    /// Scan complet des résidus pour un programme (alias)
    /// </summary>
    public async Task<List<ResidualItem>> ScanResidualsAsync(
        InstalledProgram program,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var residuals = new List<ResidualItem>();
        var rawKeywords = ExtractKeywords(program);
        var keywords = FilterKeywords(rawKeywords); // FILTRER les mots-clés génériques

        if (keywords.Count == 0)
        {
            return residuals;
        }

        // 1. Scanner les dossiers de données (30%)
        progress?.Report(new ScanProgress(0, "Scan des dossiers de données..."));
        var folderResiduals = await ScanDataFoldersAsync(keywords, program, cancellationToken);
        residuals.AddRange(folderResiduals);

        // 2. Scanner le registre (60%)
        progress?.Report(new ScanProgress(30, "Scan du registre..."));
        var registryResiduals = await ScanRegistryAsync(keywords, program, cancellationToken);
        residuals.AddRange(registryResiduals);

        // 3. Scanner les services (75%)
        progress?.Report(new ScanProgress(60, "Scan des services..."));
        var serviceResiduals = await ScanServicesAsync(keywords, cancellationToken);
        residuals.AddRange(serviceResiduals);

        // 4. Scanner les tâches planifiées (85%)
        progress?.Report(new ScanProgress(75, "Scan des tâches planifiées..."));
        var taskResiduals = await ScanScheduledTasksAsync(keywords, cancellationToken);
        residuals.AddRange(taskResiduals);

        // 5. Scanner les raccourcis (70%)
        progress?.Report(new ScanProgress(65, "Scan des raccourcis..."));
        var shortcutResiduals = await ScanShortcutsAsync(keywords, program, cancellationToken);
        residuals.AddRange(shortcutResiduals);

        // 6. Scanner les fichiers temporaires (75%)
        progress?.Report(new ScanProgress(70, "Scan des fichiers temporaires..."));
        var tempResiduals = await ScanTempFilesAsync(keywords, cancellationToken);
        residuals.AddRange(tempResiduals);

        // 7. Scan approfondi du dossier d'installation original (80%)
        progress?.Report(new ScanProgress(75, "Vérification du dossier d'installation..."));
        var installResiduals = await ScanInstallLocationAsync(keywords, program, cancellationToken);
        residuals.AddRange(installResiduals);

        // 8. Scanner les associations de fichiers (85%)
        progress?.Report(new ScanProgress(80, "Scan des associations de fichiers..."));
        var fileAssocResiduals = await ScanFileAssociationsAsync(keywords, program, cancellationToken);
        residuals.AddRange(fileAssocResiduals);

        // 9. Scanner les composants COM/CLSID (90%)
        progress?.Report(new ScanProgress(85, "Scan des composants COM..."));
        var comResiduals = await ScanComComponentsAsync(keywords, program, cancellationToken);
        residuals.AddRange(comResiduals);

        // 10. Scanner les variables PATH (95%)
        progress?.Report(new ScanProgress(90, "Scan des variables d'environnement..."));
        var pathResiduals = await ScanEnvironmentPathAsync(keywords, program, cancellationToken);
        residuals.AddRange(pathResiduals);

        // 11. Deep Scan des Program Files (98%)
        progress?.Report(new ScanProgress(95, "Analyse approfondie..."));
        var deepScanResiduals = await DeepScanProgramFilesAsync(keywords, program, cancellationToken);
        residuals.AddRange(deepScanResiduals);

        progress?.Report(new ScanProgress(100, $"{residuals.Count} résidus trouvés"));

        // FILTRE FINAL DE SÉCURITÉ: Éliminer tout chemin protégé qui aurait passé les filtres
        var safeResiduals = residuals
            .Where(r => !IsProtectedPath(r.Path))
            .Where(r => r.Type != ResidualType.RegistryKey || !IsProtectedRegistryPath(r.Path))
            .ToList();

        // Dédupliquer et trier par confiance
        return safeResiduals
            .GroupBy(r => r.Path.ToLowerInvariant())
            .Select(g => g.OrderByDescending(r => r.Confidence).First())
            .OrderByDescending(r => r.Confidence)
            .ThenByDescending(r => r.Size)
            .ToList();
    }

    /// <summary>
    /// Scan rapide (fichiers et registre seulement)
    /// </summary>
    public async Task<List<ResidualItem>> QuickScanAsync(
        InstalledProgram program,
        CancellationToken cancellationToken = default)
    {
        var residuals = new List<ResidualItem>();
        var rawKeywords = ExtractKeywords(program);
        var keywords = FilterKeywords(rawKeywords);

        if (keywords.Count == 0) return residuals;

        var folderTask = ScanDataFoldersAsync(keywords, program, cancellationToken);
        var registryTask = ScanRegistryAsync(keywords, program, cancellationToken);

        await Task.WhenAll(folderTask, registryTask);

        residuals.AddRange(folderTask.Result);
        residuals.AddRange(registryTask.Result);

        // FILTRE DE SÉCURITÉ
        return residuals
            .Where(r => !IsProtectedPath(r.Path))
            .Where(r => r.Type != ResidualType.RegistryKey || !IsProtectedRegistryPath(r.Path))
            .GroupBy(r => r.Path.ToLowerInvariant())
            .Select(g => g.First())
            .ToList();
    }

    #endregion


    #region Keyword Extraction

    private static HashSet<string> ExtractKeywords(InstalledProgram program)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Nom du programme (mots significatifs)
        if (!string.IsNullOrEmpty(program.DisplayName))
        {
            var words = CleanNameRegex().Replace(program.DisplayName, " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 3 && !IsCommonWord(w));
            
            foreach (var word in words)
            {
                keywords.Add(word);
            }

            // Nom complet nettoyé
            var cleanName = CleanNameRegex().Replace(program.DisplayName, "");
            if (cleanName.Length >= 3)
            {
                keywords.Add(cleanName);
            }
        }

        // Éditeur - SEULEMENT si c'est un éditeur spécifique (pas Microsoft, etc.)
        if (!string.IsNullOrEmpty(program.Publisher))
        {
            var publisherClean = CleanNameRegex().Replace(program.Publisher, "");
            if (publisherClean.Length >= 3 && 
                !IsCommonPublisher(publisherClean) &&
                !ProtectedFolderNames.Contains(publisherClean))
            {
                keywords.Add(publisherClean);
            }
        }

        // Nom du dossier d'installation
        if (!string.IsNullOrEmpty(program.InstallLocation))
        {
            var folderName = Path.GetFileName(program.InstallLocation.TrimEnd('\\'));
            if (!string.IsNullOrEmpty(folderName) && 
                folderName.Length >= 3 &&
                !ProtectedFolderNames.Contains(folderName))
            {
                keywords.Add(folderName);
            }
        }

        // Clé de registre
        if (!string.IsNullOrEmpty(program.RegistryKeyName) && program.RegistryKeyName.Length >= 3)
        {
            keywords.Add(program.RegistryKeyName);
        }

        // Extraire depuis UninstallString
        ExtractKeywordsFromUninstallString(program.UninstallString, keywords);
        ExtractKeywordsFromUninstallString(program.QuietUninstallString, keywords);

        return keywords;
    }

    private static void ExtractKeywordsFromUninstallString(string uninstallString, HashSet<string> keywords)
    {
        if (string.IsNullOrEmpty(uninstallString)) return;

        try
        {
            var exePath = ExtractExecutablePath(uninstallString);
            if (string.IsNullOrEmpty(exePath)) return;

            var directory = Path.GetDirectoryName(exePath);
            if (!string.IsNullOrEmpty(directory))
            {
                var folderName = Path.GetFileName(directory.TrimEnd('\\'));
                if (!string.IsNullOrEmpty(folderName) && 
                    folderName.Length >= 3 && 
                    !IsSystemFolder(folderName) &&
                    !ProtectedFolderNames.Contains(folderName))
                {
                    keywords.Add(folderName);
                }

                if (IsCommonSubfolder(folderName))
                {
                    var parentDir = Path.GetDirectoryName(directory);
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        var parentName = Path.GetFileName(parentDir.TrimEnd('\\'));
                        if (!string.IsNullOrEmpty(parentName) && 
                            parentName.Length >= 3 && 
                            !IsSystemFolder(parentName) &&
                            !ProtectedFolderNames.Contains(parentName))
                        {
                            keywords.Add(parentName);
                        }
                    }
                }
            }

            var exeName = Path.GetFileNameWithoutExtension(exePath);
            if (!string.IsNullOrEmpty(exeName) && exeName.Length >= 3 && !IsCommonUninstallerName(exeName))
            {
                keywords.Add(exeName);
            }
        }
        catch { }
    }

    private static string ExtractExecutablePath(string commandLine)
    {
        if (string.IsNullOrEmpty(commandLine)) return "";

        commandLine = commandLine.Trim();

        if (commandLine.StartsWith('"'))
        {
            var endQuote = commandLine.IndexOf('"', 1);
            if (endQuote > 1)
            {
                return commandLine.Substring(1, endQuote - 1);
            }
        }

        var exeIndex = commandLine.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex > 0)
        {
            return commandLine.Substring(0, exeIndex + 4);
        }

        var spaceIndex = commandLine.IndexOf(' ');
        return spaceIndex > 0 ? commandLine.Substring(0, spaceIndex) : commandLine;
    }

    private static bool IsSystemFolder(string folderName)
    {
        var systemFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Program Files", "Program Files (x86)", "Windows", "System32", "SysWOW64",
            "Common Files", "Microsoft", "WindowsApps", "ProgramData", "Users"
        };
        return systemFolders.Contains(folderName);
    }

    private static bool IsCommonSubfolder(string folderName)
    {
        var commonSubfolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "uninst", "uninstall", "setup", "install", "tools", "lib", "libs"
        };
        return commonSubfolders.Contains(folderName);
    }

    private static bool IsCommonUninstallerName(string exeName)
    {
        var commonNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "unins000", "unins001", "uninstall", "uninst", "setup", "msiexec",
            "unwise", "uninst32", "iun6002", "isuninst", "st5unst"
        };
        return commonNames.Contains(exeName);
    }

    private static bool IsCommonWord(string word)
    {
        var common = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "for", "and", "version", "update", "edition", "pro", "professional",
            "enterprise", "ultimate", "home", "basic", "premium", "plus", "lite",
            "free", "trial", "beta", "alpha", "release", "build", "bit", "x64", "x86"
        };
        return common.Contains(word);
    }

    private static bool IsCommonPublisher(string publisher)
    {
        var common = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "microsoft", "windows", "corporation", "inc", "llc", "ltd", "company",
            "nvidia", "intel", "amd", "google", "apple"
        };
        return common.Contains(publisher);
    }

    #endregion


    #region Folder Scanning

    private async Task<List<ResidualItem>> ScanDataFoldersAsync(
        HashSet<string> keywords,
        InstalledProgram program,
        CancellationToken cancellationToken)
    {
        var residuals = new List<ResidualItem>();

        await Task.Run(() =>
        {
            foreach (var baseFolder in CommonDataFolders)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(baseFolder)) continue;

                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(baseFolder))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // SÉCURITÉ: Ignorer les chemins protégés
                        if (IsProtectedPath(dir)) continue;

                        var dirName = Path.GetFileName(dir);
                        
                        // SÉCURITÉ: Ignorer les dossiers protégés
                        if (ProtectedFolderNames.Contains(dirName)) continue;

                        var confidence = CalculateFolderConfidence(dirName, keywords, program);

                        if (confidence >= ConfidenceLevel.Low)
                        {
                            var size = CalculateDirectorySize(dir);
                            residuals.Add(new ResidualItem
                            {
                                Path = dir,
                                Type = ResidualType.Folder,
                                Size = size,
                                Confidence = confidence,
                                Description = $"Dossier de données: {dirName}"
                            });
                        }
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }

            // Scanner aussi le dossier Program Files si le programme y était
            if (!string.IsNullOrEmpty(program.InstallLocation))
            {
                var parentDir = Path.GetDirectoryName(program.InstallLocation);
                if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                {
                    foreach (var keyword in keywords.Take(3))
                    {
                        try
                        {
                            foreach (var dir in Directory.EnumerateDirectories(parentDir, $"*{keyword}*"))
                            {
                                if (dir.Equals(program.InstallLocation, StringComparison.OrdinalIgnoreCase))
                                    continue;

                                // SÉCURITÉ: Vérifier la protection
                                if (IsProtectedPath(dir)) continue;

                                var size = CalculateDirectorySize(dir);
                                residuals.Add(new ResidualItem
                                {
                                    Path = dir,
                                    Type = ResidualType.Folder,
                                    Size = size,
                                    Confidence = ConfidenceLevel.Medium,
                                    Description = $"Dossier lié: {Path.GetFileName(dir)}"
                                });
                            }
                        }
                        catch { }
                    }
                }
            }
        }, cancellationToken);

        return residuals;
    }

    /// <summary>
    /// Calcul de confiance DURCI - nécessite des correspondances plus fortes
    /// </summary>
    private static ConfidenceLevel CalculateFolderConfidence(
        string folderName,
        HashSet<string> keywords,
        InstalledProgram program)
    {
        var lowerName = folderName.ToLowerInvariant();

        // SÉCURITÉ: Jamais de haute confiance pour les dossiers système
        if (ProtectedFolderNames.Any(p => lowerName.Contains(p.ToLowerInvariant())))
            return ConfidenceLevel.None;

        // Correspondance EXACTE avec le nom du programme nettoyé
        if (!string.IsNullOrEmpty(program.DisplayName))
        {
            var cleanDisplayName = CleanNameRegex().Replace(program.DisplayName, "").ToLowerInvariant();
            
            // VeryHigh: Nom IDENTIQUE (pas juste "contient")
            if (lowerName == cleanDisplayName)
            {
                return ConfidenceLevel.VeryHigh;
            }
            
            // High: Le nom du dossier COMMENCE par le nom du programme
            if (lowerName.StartsWith(cleanDisplayName) && cleanDisplayName.Length >= 5)
            {
                return ConfidenceLevel.High;
            }
        }

        // Correspondance avec la clé de registre (exacte)
        if (!string.IsNullOrEmpty(program.RegistryKeyName))
        {
            var regKeyLower = program.RegistryKeyName.ToLowerInvariant();
            if (lowerName == regKeyLower)
            {
                return ConfidenceLevel.High;
            }
        }

        // Correspondance avec les mots-clés - DURCIE
        // Seuls les mots-clés LONGS et SPÉCIFIQUES comptent
        var strongKeywords = keywords.Where(k => k.Length >= 5 && !TooGenericKeywords.Contains(k)).ToList();
        int exactMatches = strongKeywords.Count(k => lowerName == k.ToLowerInvariant());
        int containsMatches = strongKeywords.Count(k => lowerName.Contains(k.ToLowerInvariant()));
        
        // DURCI: Besoin de correspondance EXACTE ou plusieurs correspondances fortes
        if (exactMatches >= 1) return ConfidenceLevel.High;
        if (containsMatches >= 3) return ConfidenceLevel.Medium;
        if (containsMatches >= 2) return ConfidenceLevel.Low;
        
        return ConfidenceLevel.None;
    }

    /// <summary>
    /// Calcule la taille d'un dossier avec cache
    /// </summary>
    private static long CalculateDirectorySize(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return 0;

        // Utiliser le cache pour éviter les recalculs
        if (_directorySizeCache.TryGetValue(path, out var cached))
            return cached;

        var size = CommonHelpers.CalculateDirectorySize(path);
        _directorySizeCache.TryAdd(path, size);
        return size;
    }

    #endregion


    #region Registry Scanning

    private async Task<List<ResidualItem>> ScanRegistryAsync(
        HashSet<string> keywords,
        InstalledProgram program,
        CancellationToken cancellationToken)
    {
        var residuals = new List<ResidualItem>();

        await Task.Run(() =>
        {
            foreach (var (path, root) in RegistryPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var baseKey = root.OpenSubKey(path);
                    if (baseKey == null) continue;

                    ScanRegistryKeyRecursive(baseKey, $"{GetRootName(root)}\\{path}", 
                        keywords, program, residuals, 0, cancellationToken);
                }
                catch { }
            }
        }, cancellationToken);

        return residuals;
    }

    private static void ScanRegistryKeyRecursive(
        RegistryKey key,
        string fullPath,
        HashSet<string> keywords,
        InstalledProgram program,
        List<ResidualItem> residuals,
        int depth,
        CancellationToken cancellationToken)
    {
        if (depth > 3) return;

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            foreach (var subKeyName in key.GetSubKeyNames())
            {
                var subKeyPath = $"{fullPath}\\{subKeyName}";
                
                // SÉCURITÉ: Ignorer les clés protégées
                if (IsProtectedRegistryPath(subKeyPath)) continue;
                if (ProtectedRegistryKeys.Contains(subKeyName)) continue;

                var confidence = CalculateRegistryConfidence(subKeyName, keywords, program);

                if (confidence >= ConfidenceLevel.Medium)
                {
                    residuals.Add(new ResidualItem
                    {
                        Path = subKeyPath,
                        Type = ResidualType.RegistryKey,
                        Confidence = confidence,
                        Description = $"Clé de registre: {subKeyName}"
                    });
                }
                else if (confidence >= ConfidenceLevel.Low && depth < 2)
                {
                    try
                    {
                        using var subKey = key.OpenSubKey(subKeyName);
                        if (subKey != null)
                        {
                            ScanRegistryKeyRecursive(subKey, subKeyPath, keywords, 
                                program, residuals, depth + 1, cancellationToken);
                        }
                    }
                    catch { }
                }
            }

            // Scanner les valeurs
            foreach (var valueName in key.GetValueNames())
            {
                if (string.IsNullOrEmpty(valueName)) continue;

                var value = key.GetValue(valueName)?.ToString() ?? "";
                
                // SÉCURITÉ: Ne pas signaler de valeurs pointant vers des chemins protégés
                if (IsProtectedPath(value)) continue;
                
                if (ContainsKeyword(value, keywords) || ContainsKeyword(valueName, keywords))
                {
                    residuals.Add(new ResidualItem
                    {
                        Path = $"{fullPath}\\{valueName}",
                        Type = ResidualType.RegistryValue,
                        Confidence = ConfidenceLevel.Medium,
                        Description = $"Valeur de registre: {valueName}"
                    });
                }
            }
        }
        catch { }
    }

    private static ConfidenceLevel CalculateRegistryConfidence(
        string keyName,
        HashSet<string> keywords,
        InstalledProgram program)
    {
        var lowerName = keyName.ToLowerInvariant();

        // SÉCURITÉ: Jamais de haute confiance pour les clés système
        if (ProtectedRegistryKeys.Contains(keyName))
            return ConfidenceLevel.None;

        // Correspondance exacte avec la clé de registre du programme
        if (!string.IsNullOrEmpty(program.RegistryKeyName) &&
            lowerName == program.RegistryKeyName.ToLowerInvariant())
        {
            return ConfidenceLevel.VeryHigh;
        }

        if (!string.IsNullOrEmpty(program.DisplayName))
        {
            var cleanName = CleanNameRegex().Replace(program.DisplayName, "").ToLowerInvariant();
            if (lowerName == cleanName)
            {
                return ConfidenceLevel.High;
            }
        }

        // DURCI: Besoin de correspondances fortes
        var strongKeywords = keywords.Where(k => k.Length >= 5 && !TooGenericKeywords.Contains(k)).ToList();
        int exactMatches = strongKeywords.Count(k => lowerName == k.ToLowerInvariant());
        int containsMatches = strongKeywords.Count(k => lowerName.Contains(k.ToLowerInvariant()));
        
        if (exactMatches >= 1) return ConfidenceLevel.High;
        if (containsMatches >= 2) return ConfidenceLevel.Medium;
        
        return ConfidenceLevel.None;
    }

    private static bool ContainsKeyword(string text, HashSet<string> keywords)
    {
        var lowerText = text.ToLowerInvariant();
        // DURCI: Seuls les mots-clés longs comptent
        return keywords
            .Where(k => k.Length >= 5 && !TooGenericKeywords.Contains(k))
            .Any(k => lowerText.Contains(k.ToLowerInvariant()));
    }

    private static string GetRootName(RegistryKey root)
    {
        if (root == Registry.LocalMachine) return "HKLM";
        if (root == Registry.CurrentUser) return "HKCU";
        if (root == Registry.ClassesRoot) return "HKCR";
        return "HKEY";
    }

    #endregion


    #region Services Scanning

    private async Task<List<ResidualItem>> ScanServicesAsync(
        HashSet<string> keywords,
        CancellationToken cancellationToken)
    {
        var residuals = new List<ResidualItem>();

        await Task.Run(() =>
        {
            try
            {
                using var servicesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
                if (servicesKey == null) return;

                foreach (var serviceName in servicesKey.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using var serviceKey = servicesKey.OpenSubKey(serviceName);
                    if (serviceKey == null) continue;

                    var displayName = serviceKey.GetValue("DisplayName")?.ToString() ?? "";
                    var imagePath = serviceKey.GetValue("ImagePath")?.ToString() ?? "";

                    // SÉCURITÉ: Ignorer les services pointant vers des chemins protégés
                    if (IsProtectedPath(imagePath)) continue;

                    // DURCI: Utiliser uniquement des mots-clés forts
                    var strongKeywords = keywords.Where(k => k.Length >= 5 && !TooGenericKeywords.Contains(k)).ToHashSet();
                    
                    if (ContainsStrongKeyword(serviceName, strongKeywords) ||
                        ContainsStrongKeyword(displayName, strongKeywords) ||
                        ContainsStrongKeyword(imagePath, strongKeywords))
                    {
                        residuals.Add(new ResidualItem
                        {
                            Path = serviceName,
                            Type = ResidualType.Service,
                            Confidence = ConfidenceLevel.High,
                            Description = $"Service Windows: {displayName}"
                        });
                    }
                }
            }
            catch { }
        }, cancellationToken);

        return residuals;
    }

    private static bool ContainsStrongKeyword(string text, HashSet<string> strongKeywords)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var lowerText = text.ToLowerInvariant();
        return strongKeywords.Any(k => lowerText.Contains(k.ToLowerInvariant()));
    }

    #endregion

    #region Scheduled Tasks Scanning

    private async Task<List<ResidualItem>> ScanScheduledTasksAsync(
        HashSet<string> keywords,
        CancellationToken cancellationToken)
    {
        var residuals = new List<ResidualItem>();

        await Task.Run(() =>
        {
            var taskFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "Tasks");

            if (!Directory.Exists(taskFolder)) return;

            var strongKeywords = keywords.Where(k => k.Length >= 5 && !TooGenericKeywords.Contains(k)).ToHashSet();

            try
            {
                foreach (var taskFile in Directory.EnumerateFiles(taskFolder, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var taskName = Path.GetFileNameWithoutExtension(taskFile);
                    
                    if (ContainsStrongKeyword(taskName, strongKeywords))
                    {
                        residuals.Add(new ResidualItem
                        {
                            Path = taskName,
                            Type = ResidualType.ScheduledTask,
                            Confidence = ConfidenceLevel.High,
                            Description = $"Tâche planifiée: {taskName}"
                        });
                    }
                    else
                    {
                        try
                        {
                            var content = File.ReadAllText(taskFile);
                            
                            // SÉCURITÉ: Vérifier que le contenu ne pointe pas vers des chemins protégés
                            if (IsProtectedPath(content)) continue;
                            
                            if (ContainsStrongKeyword(content, strongKeywords))
                            {
                                residuals.Add(new ResidualItem
                                {
                                    Path = taskName,
                                    Type = ResidualType.ScheduledTask,
                                    Confidence = ConfidenceLevel.Medium,
                                    Description = $"Tâche planifiée: {taskName}"
                                });
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }, cancellationToken);

        return residuals;
    }

    #endregion

    #region Shortcuts Scanning

    private async Task<List<ResidualItem>> ScanShortcutsAsync(
        HashSet<string> keywords,
        InstalledProgram program,
        CancellationToken cancellationToken)
    {
        var residuals = new List<ResidualItem>();

        var shortcutFolders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch")
        };

        var strongKeywords = keywords.Where(k => k.Length >= 4 && !TooGenericKeywords.Contains(k)).ToHashSet();

        await Task.Run(() =>
        {
            foreach (var folder in shortcutFolders)
            {
                if (!Directory.Exists(folder)) continue;

                try
                {
                    foreach (var file in Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var fileName = Path.GetFileNameWithoutExtension(file);
                        if (ContainsStrongKeyword(fileName, strongKeywords))
                        {
                            residuals.Add(new ResidualItem
                            {
                                Path = file,
                                Type = ResidualType.File,
                                Size = new FileInfo(file).Length,
                                Confidence = ConfidenceLevel.High,
                                Description = $"Raccourci: {fileName}"
                            });
                        }
                    }
                }
                catch { }
            }
        }, cancellationToken);

        return residuals;
    }

    #endregion

    #region Temp Files Scanning

    private async Task<List<ResidualItem>> ScanTempFilesAsync(
        HashSet<string> keywords,
        CancellationToken cancellationToken)
    {
        var residuals = new List<ResidualItem>();

        var tempFolders = new[]
        {
            Path.GetTempPath(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")
        };

        var strongKeywords = keywords.Where(k => k.Length >= 5 && !TooGenericKeywords.Contains(k)).ToHashSet();

        await Task.Run(() =>
        {
            foreach (var tempFolder in tempFolders)
            {
                if (!Directory.Exists(tempFolder)) continue;

                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(tempFolder))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var dirName = Path.GetFileName(dir);
                        if (ContainsStrongKeyword(dirName, strongKeywords))
                        {
                            var size = CalculateDirectorySize(dir);
                            residuals.Add(new ResidualItem
                            {
                                Path = dir,
                                Type = ResidualType.Folder,
                                Size = size,
                                Confidence = ConfidenceLevel.Low, // Temp = toujours Low
                                Description = $"Dossier temporaire: {dirName}"
                            });
                        }
                    }
                }
                catch { }
            }
        }, cancellationToken);

        return residuals;
    }

    #endregion


    #region Install Location Scanning

    private async Task<List<ResidualItem>> ScanInstallLocationAsync(
        HashSet<string> keywords,
        InstalledProgram program,
        CancellationToken cancellationToken)
    {
        var residuals = new List<ResidualItem>();

        await Task.Run(() =>
        {
            // 1. Vérifier si le dossier d'installation existe toujours
            if (!string.IsNullOrEmpty(program.InstallLocation) && Directory.Exists(program.InstallLocation))
            {
                // SÉCURITÉ: Ne pas supprimer les dossiers protégés même s'ils sont listés comme InstallLocation
                if (!IsProtectedPath(program.InstallLocation))
                {
                    var size = CalculateDirectorySize(program.InstallLocation);
                    residuals.Add(new ResidualItem
                    {
                        Path = program.InstallLocation,
                        Type = ResidualType.Folder,
                        Size = size,
                        Confidence = ConfidenceLevel.VeryHigh,
                        Description = $"Dossier d'installation: {Path.GetFileName(program.InstallLocation)}"
                    });
                }
            }

            // 2. Extraire le chemin depuis UninstallString et vérifier
            var uninstallPath = ExtractInstallPathFromUninstallString(program.UninstallString);
            if (!string.IsNullOrEmpty(uninstallPath) && Directory.Exists(uninstallPath))
            {
                // SÉCURITÉ
                if (!IsProtectedPath(uninstallPath))
                {
                    if (string.IsNullOrEmpty(program.InstallLocation) || 
                        !uninstallPath.Equals(program.InstallLocation, StringComparison.OrdinalIgnoreCase))
                    {
                        var size = CalculateDirectorySize(uninstallPath);
                        residuals.Add(new ResidualItem
                        {
                            Path = uninstallPath,
                            Type = ResidualType.Folder,
                            Size = size,
                            Confidence = ConfidenceLevel.VeryHigh,
                            Description = $"Dossier détecté depuis désinstalleur: {Path.GetFileName(uninstallPath)}"
                        });
                    }
                }
            }

            // 3. Scanner Program Files - UNIQUEMENT pour correspondance EXACTE
            var programFilesFolders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };

            // DURCI: Seuls les mots-clés EXACTS et LONGS
            var exactKeywords = keywords.Where(k => k.Length >= 5 && !TooGenericKeywords.Contains(k)).ToList();

            foreach (var pf in programFilesFolders.Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(pf))
                    {
                        // SÉCURITÉ
                        if (IsProtectedPath(dir)) continue;

                        var dirName = Path.GetFileName(dir);
                        
                        // DURCI: Correspondance EXACTE seulement (pas "contains")
                        if (exactKeywords.Any(k => dirName.Equals(k, StringComparison.OrdinalIgnoreCase)))
                        {
                            if (!residuals.Any(r => r.Path.Equals(dir, StringComparison.OrdinalIgnoreCase)))
                            {
                                var size = CalculateDirectorySize(dir);
                                residuals.Add(new ResidualItem
                                {
                                    Path = dir,
                                    Type = ResidualType.Folder,
                                    Size = size,
                                    Confidence = ConfidenceLevel.High,
                                    Description = $"Dossier Program Files: {dirName}"
                                });
                            }
                        }
                    }
                }
                catch { }
            }
        }, cancellationToken);

        return residuals;
    }

    private static string ExtractInstallPathFromUninstallString(string uninstallString)
    {
        if (string.IsNullOrEmpty(uninstallString)) return "";

        var exePath = ExtractExecutablePath(uninstallString);
        if (string.IsNullOrEmpty(exePath)) return "";

        var directory = Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(directory)) return "";

        var folderName = Path.GetFileName(directory);
        
        if (IsCommonSubfolder(folderName))
        {
            return Path.GetDirectoryName(directory) ?? "";
        }

        return directory;
    }

    #endregion

    #region File Associations Scanning

    private async Task<List<ResidualItem>> ScanFileAssociationsAsync(
        HashSet<string> keywords,
        InstalledProgram program,
        CancellationToken cancellationToken)
    {
        var residuals = new List<ResidualItem>();
        var strongKeywords = keywords.Where(k => k.Length >= 5 && !TooGenericKeywords.Contains(k)).ToHashSet();

        await Task.Run(() =>
        {
            try
            {
                using var classesRoot = Registry.ClassesRoot;
                
                foreach (var subKeyName in classesRoot.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Ignorer les clés système
                    if (ProtectedRegistryKeys.Contains(subKeyName)) continue;

                    try
                    {
                        using var subKey = classesRoot.OpenSubKey(subKeyName);
                        if (subKey == null) continue;

                        if (ContainsStrongKeyword(subKeyName, strongKeywords))
                        {
                            residuals.Add(new ResidualItem
                            {
                                Path = $"HKCR\\{subKeyName}",
                                Type = ResidualType.RegistryKey,
                                Confidence = ConfidenceLevel.Medium,
                                Description = $"Association de fichiers: {subKeyName}"
                            });
                            continue;
                        }

                        if (subKeyName.StartsWith('.'))
                        {
                            var defaultValue = subKey.GetValue("")?.ToString() ?? "";
                            if (ContainsStrongKeyword(defaultValue, strongKeywords))
                            {
                                residuals.Add(new ResidualItem
                                {
                                    Path = $"HKCR\\{subKeyName}",
                                    Type = ResidualType.RegistryKey,
                                    Confidence = ConfidenceLevel.Medium,
                                    Description = $"Extension associée: {subKeyName} -> {defaultValue}"
                                });
                            }

                            using var openWithKey = subKey.OpenSubKey("OpenWithProgids");
                            if (openWithKey != null)
                            {
                                foreach (var progId in openWithKey.GetValueNames())
                                {
                                    if (ContainsStrongKeyword(progId, strongKeywords))
                                    {
                                        residuals.Add(new ResidualItem
                                        {
                                            Path = $"HKCR\\{subKeyName}\\OpenWithProgids\\{progId}",
                                            Type = ResidualType.RegistryValue,
                                            Confidence = ConfidenceLevel.Medium,
                                            Description = $"ProgID orphelin: {progId}"
                                        });
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            ScanUserFileAssociations(strongKeywords, residuals, cancellationToken);

        }, cancellationToken);

        return residuals;
    }

    private static void ScanUserFileAssociations(
        HashSet<string> strongKeywords,
        List<ResidualItem> residuals,
        CancellationToken cancellationToken)
    {
        var userPaths = new[]
        {
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts",
            @"Software\Classes"
        };

        foreach (var basePath in userPaths)
        {
            try
            {
                using var baseKey = Registry.CurrentUser.OpenSubKey(basePath);
                if (baseKey == null) continue;

                foreach (var subKeyName in baseKey.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (ContainsStrongKeyword(subKeyName, strongKeywords))
                    {
                        residuals.Add(new ResidualItem
                        {
                            Path = $"HKCU\\{basePath}\\{subKeyName}",
                            Type = ResidualType.RegistryKey,
                            Confidence = ConfidenceLevel.Low,
                            Description = $"Association utilisateur: {subKeyName}"
                        });
                    }
                }
            }
            catch { }
        }
    }

    #endregion


    #region COM Components Scanning

    private async Task<List<ResidualItem>> ScanComComponentsAsync(
        HashSet<string> keywords,
        InstalledProgram program,
        CancellationToken cancellationToken)
    {
        var residuals = new List<ResidualItem>();
        var strongKeywords = keywords.Where(k => k.Length >= 5 && !TooGenericKeywords.Contains(k)).ToHashSet();

        await Task.Run(() =>
        {
            var clsidPaths = new (string Path, RegistryKey Root)[]
            {
                (@"SOFTWARE\Classes\CLSID", Registry.LocalMachine),
                (@"SOFTWARE\Classes\CLSID", Registry.CurrentUser),
                (@"SOFTWARE\WOW6432Node\Classes\CLSID", Registry.LocalMachine),
                (@"SOFTWARE\Classes\TypeLib", Registry.LocalMachine),
                (@"SOFTWARE\Classes\Interface", Registry.LocalMachine)
            };

            foreach (var (path, root) in clsidPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var baseKey = root.OpenSubKey(path);
                    if (baseKey == null) continue;

                    foreach (var clsid in baseKey.GetSubKeyNames())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            using var clsidKey = baseKey.OpenSubKey(clsid);
                            if (clsidKey == null) continue;

                            var displayName = clsidKey.GetValue("")?.ToString() ?? "";
                            var serverPath = GetComServerPath(clsidKey);

                            // SÉCURITÉ: Ignorer les composants pointant vers des chemins protégés
                            if (!string.IsNullOrEmpty(serverPath) && IsProtectedPath(serverPath))
                                continue;

                            if (ContainsStrongKeyword(displayName, strongKeywords) ||
                                ContainsStrongKeyword(clsid, strongKeywords) ||
                                (!string.IsNullOrEmpty(serverPath) && MatchesInstallPath(serverPath, program, strongKeywords)))
                            {
                                var rootName = root == Registry.LocalMachine ? "HKLM" : "HKCU";
                                residuals.Add(new ResidualItem
                                {
                                    Path = $"{rootName}\\{path}\\{clsid}",
                                    Type = ResidualType.RegistryKey,
                                    Confidence = string.IsNullOrEmpty(serverPath) ? ConfidenceLevel.Low : ConfidenceLevel.High,
                                    Description = $"Composant COM: {(string.IsNullOrEmpty(displayName) ? clsid : displayName)}"
                                });
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }, cancellationToken);

        return residuals;
    }

    private static string GetComServerPath(RegistryKey clsidKey)
    {
        var serverKeys = new[] { "InprocServer32", "LocalServer32", "InprocHandler32" };
        
        foreach (var serverKeyName in serverKeys)
        {
            try
            {
                using var serverKey = clsidKey.OpenSubKey(serverKeyName);
                if (serverKey != null)
                {
                    var path = serverKey.GetValue("")?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(path))
                        return path;
                }
            }
            catch { }
        }
        
        return "";
    }

    private static bool MatchesInstallPath(string path, InstalledProgram program, HashSet<string> strongKeywords)
    {
        if (string.IsNullOrEmpty(path)) return false;

        // SÉCURITÉ: Ne pas matcher si c'est un chemin protégé
        if (IsProtectedPath(path)) return false;

        if (!string.IsNullOrEmpty(program.InstallLocation) &&
            path.Contains(program.InstallLocation, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return strongKeywords.Any(k => path.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Environment PATH Scanning

    private async Task<List<ResidualItem>> ScanEnvironmentPathAsync(
        HashSet<string> keywords,
        InstalledProgram program,
        CancellationToken cancellationToken)
    {
        var residuals = new List<ResidualItem>();

        await Task.Run(() =>
        {
            ScanPathVariable(
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "",
                "Système",
                keywords,
                program,
                residuals,
                cancellationToken);

            ScanPathVariable(
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "",
                "Utilisateur",
                keywords,
                program,
                residuals,
                cancellationToken);

            ScanOtherEnvironmentVariables(keywords, program, residuals);

        }, cancellationToken);

        return residuals;
    }

    private static void ScanPathVariable(
        string pathValue,
        string scope,
        HashSet<string> keywords,
        InstalledProgram program,
        List<ResidualItem> residuals,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(pathValue)) return;

        var paths = pathValue.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var strongKeywords = keywords.Where(k => k.Length >= 5 && !TooGenericKeywords.Contains(k)).ToHashSet();

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var trimmedPath = path.Trim();
            if (string.IsNullOrEmpty(trimmedPath)) continue;

            // SÉCURITÉ: Ignorer les chemins protégés
            if (IsProtectedPath(trimmedPath)) continue;

            bool matches = false;
            string reason = "";

            if (!string.IsNullOrEmpty(program.InstallLocation) &&
                trimmedPath.StartsWith(program.InstallLocation, StringComparison.OrdinalIgnoreCase))
            {
                matches = true;
                reason = "correspond au dossier d'installation";
            }
            else if (strongKeywords.Any(k => trimmedPath.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                matches = true;
                reason = "contient un mot-clé du programme";
            }

            if (matches)
            {
                var pathExists = Directory.Exists(trimmedPath);
                
                residuals.Add(new ResidualItem
                {
                    Path = trimmedPath,
                    Type = ResidualType.EnvironmentPath,
                    Confidence = pathExists ? ConfidenceLevel.Medium : ConfidenceLevel.High,
                    Description = $"Variable PATH ({scope}): {(pathExists ? "existe" : "dossier inexistant")} - {reason}"
                });
            }
        }
    }

    private static void ScanOtherEnvironmentVariables(
        HashSet<string> keywords,
        InstalledProgram program,
        List<ResidualItem> residuals)
    {
        var targets = new[] { EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User };
        var strongKeywords = keywords.Where(k => k.Length >= 5 && !TooGenericKeywords.Contains(k)).ToHashSet();

        foreach (var target in targets)
        {
            try
            {
                var envVars = Environment.GetEnvironmentVariables(target);
                var scope = target == EnvironmentVariableTarget.Machine ? "Système" : "Utilisateur";

                foreach (var key in envVars.Keys)
                {
                    if (key == null) continue;
                    
                    var varName = key.ToString() ?? "";
                    var varValue = envVars[key]?.ToString() ?? "";

                    if (varName.Equals("PATH", StringComparison.OrdinalIgnoreCase) ||
                        IsCommonEnvironmentVariable(varName))
                        continue;

                    // SÉCURITÉ: Ignorer les variables pointant vers des chemins protégés
                    if (IsProtectedPath(varValue)) continue;

                    if (ContainsStrongKeyword(varName, strongKeywords) ||
                        (!string.IsNullOrEmpty(program.InstallLocation) && 
                         varValue.Contains(program.InstallLocation, StringComparison.OrdinalIgnoreCase)))
                    {
                        residuals.Add(new ResidualItem
                        {
                            Path = $"ENV:{scope}:{varName}",
                            Type = ResidualType.EnvironmentVariable,
                            Confidence = ConfidenceLevel.Medium,
                            Description = $"Variable d'environnement ({scope}): {varName}"
                        });
                    }
                }
            }
            catch { }
        }
    }

    private static bool IsCommonEnvironmentVariable(string varName)
    {
        var common = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PATH", "PATHEXT", "TEMP", "TMP", "USERPROFILE", "APPDATA", "LOCALAPPDATA",
            "PROGRAMFILES", "PROGRAMFILES(X86)", "PROGRAMDATA", "SYSTEMROOT", "WINDIR",
            "HOMEDRIVE", "HOMEPATH", "COMPUTERNAME", "USERNAME", "USERDOMAIN", "OS",
            "PROCESSOR_ARCHITECTURE", "NUMBER_OF_PROCESSORS", "COMMONPROGRAMFILES",
            "COMMONPROGRAMFILES(X86)", "COMSPEC", "SYSTEMDRIVE", "PUBLIC", "ALLUSERSPROFILE"
        };
        return common.Contains(varName);
    }

    #endregion


    #region Deep Scan Program Files

    private async Task<List<ResidualItem>> DeepScanProgramFilesAsync(
        HashSet<string> keywords,
        InstalledProgram program,
        CancellationToken cancellationToken)
    {
        var residuals = new List<ResidualItem>();

        await Task.Run(() =>
        {
            var foldersToScan = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
            };

            // NE PAS scanner Common Files - trop de risques de faux positifs
            // var commonFiles = Path.Combine(..., "Common Files");  // RETIRÉ

            foreach (var baseFolder in foldersToScan.Where(Directory.Exists).Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();
                DeepScanFolder(baseFolder, keywords, program, residuals, 0, cancellationToken);
            }

        }, cancellationToken);

        return residuals;
    }

    private void DeepScanFolder(
        string folder,
        HashSet<string> keywords,
        InstalledProgram program,
        List<ResidualItem> residuals,
        int depth,
        CancellationToken cancellationToken)
    {
        // DURCI: Limiter la profondeur à 2 niveaux
        if (depth > 2) return;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var dirName = Path.GetFileName(dir);
                
                // SÉCURITÉ: Ignorer les dossiers protégés
                if (IsProtectedPath(dir)) continue;
                if (IsSystemOrCommonFolder(dirName)) continue;

                var confidence = CalculateDeepScanConfidence(dir, dirName, keywords, program);
                
                if (confidence >= ConfidenceLevel.High) // DURCI: Seulement High ou plus
                {
                    if (!residuals.Any(r => r.Path.Equals(dir, StringComparison.OrdinalIgnoreCase)))
                    {
                        var size = CalculateDirectorySize(dir);
                        residuals.Add(new ResidualItem
                        {
                            Path = dir,
                            Type = ResidualType.Folder,
                            Size = size,
                            Confidence = confidence,
                            Description = $"Dossier détecté (deep scan): {dirName}"
                        });
                    }
                }
                else if (depth < 1) // DURCI: Ne scanner qu'un niveau si pas de correspondance
                {
                    DeepScanFolder(dir, keywords, program, residuals, depth + 1, cancellationToken);
                }
            }

            // Scanner les fichiers orphelins - SEULEMENT au premier niveau
            if (depth == 0)
            {
                ScanOrphanFiles(folder, keywords, program, residuals, cancellationToken);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    /// <summary>
    /// Calcul de confiance TRÈS DURCI pour le deep scan
    /// </summary>
    private static ConfidenceLevel CalculateDeepScanConfidence(
        string path,
        string folderName,
        HashSet<string> keywords,
        InstalledProgram program)
    {
        var lowerName = folderName.ToLowerInvariant();
        var lowerPath = path.ToLowerInvariant();

        // SÉCURITÉ: Jamais pour les chemins protégés
        if (IsProtectedPath(path)) return ConfidenceLevel.None;

        // Le chemin contient InstallLocation (sous-dossier) = VeryHigh
        if (!string.IsNullOrEmpty(program.InstallLocation) &&
            lowerPath.StartsWith(program.InstallLocation.ToLowerInvariant()))
        {
            return ConfidenceLevel.VeryHigh;
        }

        // DURCI: Seuls les mots-clés LONGS et SPÉCIFIQUES
        var strongKeywords = keywords
            .Where(k => k.Length >= 6 && !TooGenericKeywords.Contains(k))
            .ToList();

        // Correspondance EXACTE avec un mot-clé fort = High
        foreach (var keyword in strongKeywords)
        {
            if (lowerName.Equals(keyword.ToLowerInvariant()))
                return ConfidenceLevel.High;
        }

        // DURCI: Correspondance partielle nécessite 3+ mots-clés forts
        int strongMatches = strongKeywords.Count(k => 
            lowerName.Contains(k.ToLowerInvariant()));
        
        if (strongMatches >= 3) return ConfidenceLevel.High;
        if (strongMatches >= 2) return ConfidenceLevel.Medium;

        // Pas de correspondance simple = None (pas Low!)
        return ConfidenceLevel.None;
    }

    private static void ScanOrphanFiles(
        string folder,
        HashSet<string> keywords,
        InstalledProgram program,
        List<ResidualItem> residuals,
        CancellationToken cancellationToken)
    {
        var targetExtensions = new[] { ".dll", ".exe", ".ocx", ".sys", ".drv" };
        var strongKeywords = keywords.Where(k => k.Length >= 6 && !TooGenericKeywords.Contains(k)).ToHashSet();

        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // SÉCURITÉ: Ignorer les fichiers dans des chemins protégés
                if (IsProtectedPath(file)) continue;

                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!targetExtensions.Contains(ext)) continue;

                var fileName = Path.GetFileNameWithoutExtension(file);
                
                // DURCI: Correspondance EXACTE seulement
                if (strongKeywords.Any(k => fileName.Equals(k, StringComparison.OrdinalIgnoreCase)))
                {
                    var fileInfo = new FileInfo(file);
                    residuals.Add(new ResidualItem
                    {
                        Path = file,
                        Type = ResidualType.File,
                        Size = fileInfo.Length,
                        Confidence = ConfidenceLevel.Medium, // Jamais High pour des fichiers isolés
                        Description = $"Fichier orphelin: {Path.GetFileName(file)}"
                    });
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Liste ÉTENDUE des dossiers système et communs à ne JAMAIS toucher
    /// </summary>
    private static bool IsSystemOrCommonFolder(string folderName)
    {
        // Utiliser la liste principale de protection
        if (ProtectedFolderNames.Contains(folderName))
            return true;

        // Vérifications supplémentaires par pattern
        var lowerName = folderName.ToLowerInvariant();
        
        // Tout ce qui contient ces termes est protégé
        if (lowerName.Contains("microsoft") ||
            lowerName.Contains("windows") ||
            lowerName.Contains("visual studio") ||
            lowerName.Contains("sdk") ||
            lowerName.Contains("kit") ||
            lowerName.Contains("debugger") ||
            lowerName.Contains(".net") ||
            lowerName.Contains("dotnet") ||
            lowerName.Contains("nvidia") ||
            lowerName.Contains("intel") ||
            lowerName.Contains("amd"))
        {
            return true;
        }

        return false;
    }

    #endregion

    [GeneratedRegex(@"[^a-zA-Z0-9]")]
    private static partial Regex CleanNameRegex();
}
