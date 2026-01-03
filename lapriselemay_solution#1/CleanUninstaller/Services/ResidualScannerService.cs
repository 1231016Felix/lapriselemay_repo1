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
    // Dossiers communs à scanner
    private static readonly string[] CommonDataFolders =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
        Environment.GetFolderPath(Environment.SpecialFolder.Recent),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
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

        // 5. Scanner les raccourcis (95%)
        progress?.Report(new ScanProgress(85, "Scan des raccourcis..."));
        var shortcutResiduals = await ScanShortcutsAsync(keywords, program, cancellationToken);
        residuals.AddRange(shortcutResiduals);

        // 6. Scanner les fichiers temporaires
        progress?.Report(new ScanProgress(95, "Scan des fichiers temporaires..."));
        var tempResiduals = await ScanTempFilesAsync(keywords, cancellationToken);
        residuals.AddRange(tempResiduals);

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

        return keywords;
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

    [GeneratedRegex(@"[^a-zA-Z0-9]")]
    private static partial Regex CleanNameRegex();
}
