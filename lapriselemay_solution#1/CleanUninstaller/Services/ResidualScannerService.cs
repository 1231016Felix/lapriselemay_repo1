using System.Text.RegularExpressions;
using Microsoft.Win32;
using CleanUninstaller.Models;

namespace CleanUninstaller.Services;

/// <summary>
/// Service de scan des résidus avancé inspiré de BCUninstaller
/// Détecte les fichiers, dossiers, clés de registre, services et tâches planifiées orphelins
/// </summary>
public partial class ResidualScannerService
{
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
        // Dossiers d'installation courants - CRITIQUE pour détecter les résidus Program Files
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Common Files"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Common Files"),
        // Autres emplacements d'installation courants
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
        var keywords = ExtractKeywords(program);

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

        // Dédupliquer et trier par confiance
        return residuals
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
        var keywords = ExtractKeywords(program);

        if (keywords.Count == 0) return residuals;

        var folderTask = ScanDataFoldersAsync(keywords, program, cancellationToken);
        var registryTask = ScanRegistryAsync(keywords, program, cancellationToken);

        await Task.WhenAll(folderTask, registryTask);

        residuals.AddRange(folderTask.Result);
        residuals.AddRange(registryTask.Result);

        return residuals
            .GroupBy(r => r.Path.ToLowerInvariant())
            .Select(g => g.First())
            .ToList();
    }

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

        // Éditeur
        if (!string.IsNullOrEmpty(program.Publisher))
        {
            var publisherClean = CleanNameRegex().Replace(program.Publisher, "");
            if (publisherClean.Length >= 3 && !IsCommonPublisher(publisherClean))
            {
                keywords.Add(publisherClean);
            }
        }

        // Nom du dossier d'installation
        if (!string.IsNullOrEmpty(program.InstallLocation))
        {
            var folderName = Path.GetFileName(program.InstallLocation.TrimEnd('\\'));
            if (!string.IsNullOrEmpty(folderName) && folderName.Length >= 3)
            {
                keywords.Add(folderName);
            }
        }

        // Clé de registre
        if (!string.IsNullOrEmpty(program.RegistryKeyName) && program.RegistryKeyName.Length >= 3)
        {
            keywords.Add(program.RegistryKeyName);
        }

        // NOUVEAU: Extraire depuis UninstallString - critique pour les résidus Program Files
        ExtractKeywordsFromUninstallString(program.UninstallString, keywords);
        ExtractKeywordsFromUninstallString(program.QuietUninstallString, keywords);

        return keywords;
    }

    /// <summary>
    /// Extrait les mots-clés depuis une chaîne de désinstallation
    /// Permet de détecter le dossier d'installation même si InstallLocation est vide
    /// </summary>
    private static void ExtractKeywordsFromUninstallString(string uninstallString, HashSet<string> keywords)
    {
        if (string.IsNullOrEmpty(uninstallString)) return;

        try
        {
            // Extraire le chemin de l'exécutable
            var exePath = ExtractExecutablePath(uninstallString);
            if (string.IsNullOrEmpty(exePath)) return;

            // Nom du dossier parent (souvent le nom du programme)
            var directory = Path.GetDirectoryName(exePath);
            if (!string.IsNullOrEmpty(directory))
            {
                var folderName = Path.GetFileName(directory.TrimEnd('\\'));
                if (!string.IsNullOrEmpty(folderName) && folderName.Length >= 3 && !IsSystemFolder(folderName))
                {
                    keywords.Add(folderName);
                }

                // Remonter d'un niveau si c'est un sous-dossier commun
                if (IsCommonSubfolder(folderName))
                {
                    var parentDir = Path.GetDirectoryName(directory);
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        var parentName = Path.GetFileName(parentDir.TrimEnd('\\'));
                        if (!string.IsNullOrEmpty(parentName) && parentName.Length >= 3 && !IsSystemFolder(parentName))
                        {
                            keywords.Add(parentName);
                        }
                    }
                }
            }

            // Nom de l'exécutable (sans extension)
            var exeName = Path.GetFileNameWithoutExtension(exePath);
            if (!string.IsNullOrEmpty(exeName) && exeName.Length >= 3 && !IsCommonUninstallerName(exeName))
            {
                keywords.Add(exeName);
            }
        }
        catch
        {
            // Ignorer les erreurs de parsing
        }
    }

    /// <summary>
    /// Extrait le chemin d'un exécutable depuis une ligne de commande
    /// </summary>
    private static string ExtractExecutablePath(string commandLine)
    {
        if (string.IsNullOrEmpty(commandLine)) return "";

        commandLine = commandLine.Trim();

        // Chemin entre guillemets
        if (commandLine.StartsWith('"'))
        {
            var endQuote = commandLine.IndexOf('"', 1);
            if (endQuote > 1)
            {
                return commandLine.Substring(1, endQuote - 1);
            }
        }

        // Chemin sans guillemets (prendre jusqu'au premier espace après .exe)
        var exeIndex = commandLine.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex > 0)
        {
            return commandLine.Substring(0, exeIndex + 4);
        }

        // Prendre le premier mot
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
            "microsoft", "windows", "corporation", "inc", "llc", "ltd", "company"
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

                        var dirName = Path.GetFileName(dir);
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

    private static ConfidenceLevel CalculateFolderConfidence(
        string folderName,
        HashSet<string> keywords,
        InstalledProgram program)
    {
        var lowerName = folderName.ToLowerInvariant();

        // Correspondance exacte avec le nom du programme
        if (!string.IsNullOrEmpty(program.DisplayName))
        {
            var cleanDisplayName = CleanNameRegex().Replace(program.DisplayName, "").ToLowerInvariant();
            if (lowerName == cleanDisplayName || lowerName.Contains(cleanDisplayName))
            {
                return ConfidenceLevel.VeryHigh;
            }
        }

        // Correspondance avec la clé de registre
        if (!string.IsNullOrEmpty(program.RegistryKeyName) &&
            lowerName.Contains(program.RegistryKeyName.ToLowerInvariant()))
        {
            return ConfidenceLevel.High;
        }

        // Correspondance avec les mots-clés
        int matchCount = keywords.Count(k => lowerName.Contains(k.ToLowerInvariant()));
        
        return matchCount switch
        {
            >= 3 => ConfidenceLevel.High,
            2 => ConfidenceLevel.Medium,
            1 => ConfidenceLevel.Low,
            _ => ConfidenceLevel.None
        };
    }

    private static long CalculateDirectorySize(string path)
    {
        try
        {
            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
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
        if (depth > 3) return; // Limiter la profondeur

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            foreach (var subKeyName in key.GetSubKeyNames())
            {
                var subKeyPath = $"{fullPath}\\{subKeyName}";
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
                    // Scanner plus profondément
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

        // Correspondance exacte
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

        int matchCount = keywords.Count(k => lowerName.Contains(k.ToLowerInvariant()));
        return matchCount switch
        {
            >= 2 => ConfidenceLevel.High,
            1 => ConfidenceLevel.Medium,
            _ => ConfidenceLevel.None
        };
    }

    private static bool ContainsKeyword(string text, HashSet<string> keywords)
    {
        var lowerText = text.ToLowerInvariant();
        return keywords.Any(k => lowerText.Contains(k.ToLowerInvariant()));
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

                    if (ContainsKeyword(serviceName, keywords) ||
                        ContainsKeyword(displayName, keywords) ||
                        ContainsKeyword(imagePath, keywords))
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

            try
            {
                foreach (var taskFile in Directory.EnumerateFiles(taskFolder, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var taskName = Path.GetFileNameWithoutExtension(taskFile);
                    
                    if (ContainsKeyword(taskName, keywords))
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
                        // Lire le contenu du fichier pour vérifier
                        try
                        {
                            var content = File.ReadAllText(taskFile);
                            if (ContainsKeyword(content, keywords))
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
                        if (ContainsKeyword(fileName, keywords))
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
                        if (ContainsKeyword(dirName, keywords))
                        {
                            var size = CalculateDirectorySize(dir);
                            residuals.Add(new ResidualItem
                            {
                                Path = dir,
                                Type = ResidualType.Folder,
                                Size = size,
                                Confidence = ConfidenceLevel.Low,
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

    /// <summary>
    /// Scan approfondi du dossier d'installation et des Program Files
    /// Détecte les dossiers orphelins laissés après désinstallation
    /// </summary>
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

            // 2. Extraire le chemin depuis UninstallString et vérifier
            var uninstallPath = ExtractInstallPathFromUninstallString(program.UninstallString);
            if (!string.IsNullOrEmpty(uninstallPath) && Directory.Exists(uninstallPath))
            {
                // Éviter les doublons avec InstallLocation
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

            // 3. Scanner Program Files pour les dossiers correspondants
            var programFilesFolders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };

            foreach (var pf in programFilesFolders.Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(pf))
                    {
                        var dirName = Path.GetFileName(dir);
                        
                        // Correspondance forte avec les mots-clés
                        if (keywords.Any(k => dirName.Equals(k, StringComparison.OrdinalIgnoreCase)))
                        {
                            // Éviter les doublons
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

    /// <summary>
    /// Extrait le dossier d'installation depuis UninstallString
    /// </summary>
    private static string ExtractInstallPathFromUninstallString(string uninstallString)
    {
        if (string.IsNullOrEmpty(uninstallString)) return "";

        var exePath = ExtractExecutablePath(uninstallString);
        if (string.IsNullOrEmpty(exePath)) return "";

        var directory = Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(directory)) return "";

        var folderName = Path.GetFileName(directory);
        
        // Si c'est un sous-dossier commun, remonter d'un niveau
        if (IsCommonSubfolder(folderName))
        {
            return Path.GetDirectoryName(directory) ?? "";
        }

        return directory;
    }

    #endregion

    #region File Associations Scanning

    /// <summary>
    /// Scanne les associations de fichiers orphelines dans le registre
    /// </summary>
    private async Task<List<ResidualItem>> ScanFileAssociationsAsync(
        HashSet<string> keywords,
        InstalledProgram program,
        CancellationToken cancellationToken)
    {
        var residuals = new List<ResidualItem>();

        await Task.Run(() =>
        {
            // Scanner HKCR pour les extensions et ProgIDs
            try
            {
                using var classesRoot = Registry.ClassesRoot;
                
                foreach (var subKeyName in classesRoot.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        using var subKey = classesRoot.OpenSubKey(subKeyName);
                        if (subKey == null) continue;

                        // Vérifier le nom de la clé
                        if (ContainsKeyword(subKeyName, keywords))
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

                        // Pour les extensions (.xxx), vérifier la valeur par défaut et OpenWithProgids
                        if (subKeyName.StartsWith('.'))
                        {
                            var defaultValue = subKey.GetValue("")?.ToString() ?? "";
                            if (ContainsKeyword(defaultValue, keywords))
                            {
                                residuals.Add(new ResidualItem
                                {
                                    Path = $"HKCR\\{subKeyName}",
                                    Type = ResidualType.RegistryKey,
                                    Confidence = ConfidenceLevel.Medium,
                                    Description = $"Extension associée: {subKeyName} -> {defaultValue}"
                                });
                            }

                            // Vérifier OpenWithProgids
                            using var openWithKey = subKey.OpenSubKey("OpenWithProgids");
                            if (openWithKey != null)
                            {
                                foreach (var progId in openWithKey.GetValueNames())
                                {
                                    if (ContainsKeyword(progId, keywords))
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

            // Scanner les associations utilisateur dans HKCU
            ScanUserFileAssociations(keywords, residuals, cancellationToken);

        }, cancellationToken);

        return residuals;
    }

    private static void ScanUserFileAssociations(
        HashSet<string> keywords,
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

                    if (ContainsKeyword(subKeyName, keywords))
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

    /// <summary>
    /// Scanne les composants COM/CLSID orphelins
    /// </summary>
    private async Task<List<ResidualItem>> ScanComComponentsAsync(
        HashSet<string> keywords,
        InstalledProgram program,
        CancellationToken cancellationToken)
    {
        var residuals = new List<ResidualItem>();

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

                            // Vérifier le nom d'affichage
                            var displayName = clsidKey.GetValue("")?.ToString() ?? "";
                            
                            // Vérifier InprocServer32 ou LocalServer32
                            var serverPath = GetComServerPath(clsidKey);

                            if (ContainsKeyword(displayName, keywords) ||
                                ContainsKeyword(clsid, keywords) ||
                                (!string.IsNullOrEmpty(serverPath) && MatchesInstallPath(serverPath, program, keywords)))
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

    private static bool MatchesInstallPath(string path, InstalledProgram program, HashSet<string> keywords)
    {
        if (string.IsNullOrEmpty(path)) return false;

        // Vérifier correspondance avec InstallLocation
        if (!string.IsNullOrEmpty(program.InstallLocation) &&
            path.Contains(program.InstallLocation, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Vérifier correspondance avec les mots-clés
        return keywords.Any(k => path.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Environment PATH Scanning

    /// <summary>
    /// Scanne les entrées PATH invalides ou orphelines
    /// </summary>
    private async Task<List<ResidualItem>> ScanEnvironmentPathAsync(
        HashSet<string> keywords,
        InstalledProgram program,
        CancellationToken cancellationToken)
    {
        var residuals = new List<ResidualItem>();

        await Task.Run(() =>
        {
            // PATH système
            ScanPathVariable(
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "",
                "Système",
                keywords,
                program,
                residuals,
                cancellationToken);

            // PATH utilisateur
            ScanPathVariable(
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "",
                "Utilisateur",
                keywords,
                program,
                residuals,
                cancellationToken);

            // Autres variables d'environnement potentiellement liées
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

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var trimmedPath = path.Trim();
            if (string.IsNullOrEmpty(trimmedPath)) continue;

            // Vérifier si le chemin correspond au programme
            bool matches = false;
            string reason = "";

            // Correspondance avec InstallLocation
            if (!string.IsNullOrEmpty(program.InstallLocation) &&
                trimmedPath.StartsWith(program.InstallLocation, StringComparison.OrdinalIgnoreCase))
            {
                matches = true;
                reason = "correspond au dossier d'installation";
            }
            // Correspondance avec les mots-clés
            else if (keywords.Any(k => trimmedPath.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                matches = true;
                reason = "contient un mot-clé du programme";
            }

            if (matches)
            {
                // Vérifier si le chemin existe toujours
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

                    // Ignorer PATH (déjà traité) et les variables système communes
                    if (varName.Equals("PATH", StringComparison.OrdinalIgnoreCase) ||
                        IsCommonEnvironmentVariable(varName))
                        continue;

                    // Vérifier si le nom ou la valeur correspond
                    if (ContainsKeyword(varName, keywords) ||
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

    /// <summary>
    /// Scan approfondi des Program Files pour trouver des fichiers orphelins
    /// </summary>
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

            // Ajouter Common Files
            var commonFiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Common Files");
            if (Directory.Exists(commonFiles)) foldersToScan.Add(commonFiles);
            
            var commonFilesX86 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Common Files");
            if (Directory.Exists(commonFilesX86)) foldersToScan.Add(commonFilesX86);

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
        // Limiter la profondeur pour éviter les scans trop longs
        if (depth > 3) return;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var dirName = Path.GetFileName(dir);
                
                // Ignorer certains dossiers système
                if (IsSystemOrCommonFolder(dirName)) continue;

                // Vérifier correspondance
                var confidence = CalculateDeepScanConfidence(dir, dirName, keywords, program);
                
                if (confidence >= ConfidenceLevel.Medium)
                {
                    // Éviter les doublons
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
                else if (depth < 2)
                {
                    // Scanner récursivement si pas de correspondance directe
                    DeepScanFolder(dir, keywords, program, residuals, depth + 1, cancellationToken);
                }
            }

            // Scanner aussi les fichiers à la racine (DLLs orphelines, etc.)
            if (depth <= 1)
            {
                ScanOrphanFiles(folder, keywords, program, residuals, cancellationToken);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    private static ConfidenceLevel CalculateDeepScanConfidence(
        string path,
        string folderName,
        HashSet<string> keywords,
        InstalledProgram program)
    {
        var lowerName = folderName.ToLowerInvariant();
        var lowerPath = path.ToLowerInvariant();

        // Correspondance exacte avec un mot-clé
        foreach (var keyword in keywords)
        {
            if (lowerName.Equals(keyword.ToLowerInvariant()))
                return ConfidenceLevel.High;
        }

        // Le chemin contient InstallLocation (sous-dossier)
        if (!string.IsNullOrEmpty(program.InstallLocation) &&
            lowerPath.StartsWith(program.InstallLocation.ToLowerInvariant()))
        {
            return ConfidenceLevel.VeryHigh;
        }

        // Correspondance partielle forte
        int strongMatches = keywords.Count(k => 
            lowerName.Contains(k.ToLowerInvariant()) && k.Length >= 4);
        
        if (strongMatches >= 2) return ConfidenceLevel.High;
        if (strongMatches == 1) return ConfidenceLevel.Medium;

        return ConfidenceLevel.None;
    }

    private static void ScanOrphanFiles(
        string folder,
        HashSet<string> keywords,
        InstalledProgram program,
        List<ResidualItem> residuals,
        CancellationToken cancellationToken)
    {
        // Extensions de fichiers potentiellement orphelins
        var targetExtensions = new[] { ".dll", ".exe", ".ocx", ".sys", ".drv" };

        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!targetExtensions.Contains(ext)) continue;

                var fileName = Path.GetFileNameWithoutExtension(file);
                
                if (ContainsKeyword(fileName, keywords))
                {
                    var fileInfo = new FileInfo(file);
                    residuals.Add(new ResidualItem
                    {
                        Path = file,
                        Type = ResidualType.File,
                        Size = fileInfo.Length,
                        Confidence = ConfidenceLevel.Medium,
                        Description = $"Fichier orphelin: {Path.GetFileName(file)}"
                    });
                }
            }
        }
        catch { }
    }

    private static bool IsSystemOrCommonFolder(string folderName)
    {
        var systemFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft", "Windows", "WindowsApps", "Microsoft.NET", "dotnet",
            "Internet Explorer", "Windows Defender", "Windows Mail", "Windows Media Player",
            "Windows NT", "Windows Photo Viewer", "Windows Portable Devices",
            "Windows Security", "Windows Sidebar", "WindowsPowerShell",
            "Reference Assemblies", "MSBuild", "IIS", "IIS Express",
            "Microsoft SDKs", "Microsoft SQL Server", "Microsoft Visual Studio",
            "Package Cache", "Uninstall Information", "installer"
        };
        return systemFolders.Contains(folderName);
    }

    #endregion

    [GeneratedRegex(@"[^a-zA-Z0-9]")]
    private static partial Regex CleanNameRegex();
}
