using Microsoft.Win32;
using CleanUninstaller.Models;
using System.Diagnostics;
using System.Globalization;

namespace CleanUninstaller.Services;

/// <summary>
/// Service pour les opérations sur le registre Windows
/// </summary>
public class RegistryService
{
    // Chemins du registre pour les programmes installés
    private static readonly string[] UninstallPaths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    // Racines de registre à scanner
    private static readonly RegistryHive[] RegistryHives =
    [
        RegistryHive.LocalMachine,
        RegistryHive.CurrentUser
    ];

    /// <summary>
    /// Calcule les tailles des dossiers d'installation pour les programmes sans taille connue
    /// Retourne le nombre de programmes dont la taille a été calculée (approximative)
    /// </summary>
    public async Task<int> CalculateMissingSizesAsync(
        List<InstalledProgram> programs,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var programsWithoutSize = programs.Where(p => p.EstimatedSize == 0).ToList();
        
        if (programsWithoutSize.Count == 0) return 0;

        var processed = 0;
        var approximateCount = 0;
        var total = programsWithoutSize.Count;

        await Task.Run(() =>
        {
            foreach (var program in programsWithoutSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Essayer de trouver le dossier d'installation
                    var installDir = FindInstallDirectory(program);
                    
                    if (!string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
                    {
                        var size = CalculateDirectorySize(installDir, cancellationToken);
                        if (size > 0)
                        {
                            program.EstimatedSize = size;
                            program.IsSizeApproximate = true;
                            approximateCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erreur calcul taille {program.DisplayName}: {ex.Message}");
                }

                processed++;
                progress?.Report(new ScanProgress
                {
                    Phase = ScanPhase.CalculatingSizes,
                    Percentage = (processed * 100) / total,
                    StatusMessage = $"Calcul des tailles... ({processed}/{total})",
                    ProcessedCount = processed,
                    TotalCount = total
                });
            }
        }, cancellationToken);

        return approximateCount;
    }

    /// <summary>
    /// Trouve le dossier d'installation d'un programme
    /// </summary>
    private static string? FindInstallDirectory(InstalledProgram program)
    {
        // 1. Utiliser InstallLocation si disponible
        if (!string.IsNullOrEmpty(program.InstallLocation) && Directory.Exists(program.InstallLocation))
        {
            return program.InstallLocation;
        }

        // 2. Extraire le dossier depuis UninstallString
        var dirFromUninstall = ExtractDirectoryFromPath(program.UninstallString);
        if (!string.IsNullOrEmpty(dirFromUninstall) && Directory.Exists(dirFromUninstall))
        {
            return dirFromUninstall;
        }

        // 3. Extraire le dossier depuis QuietUninstallString
        var dirFromQuiet = ExtractDirectoryFromPath(program.QuietUninstallString);
        if (!string.IsNullOrEmpty(dirFromQuiet) && Directory.Exists(dirFromQuiet))
        {
            return dirFromQuiet;
        }

        // 4. Chercher dans tous les emplacements connus par nom
        var dirFromSearch = FindProgramDirectory(program.DisplayName, program.Publisher);
        if (!string.IsNullOrEmpty(dirFromSearch))
        {
            return dirFromSearch;
        }

        return null;
    }

    /// <summary>
    /// Extrait le dossier parent d'un chemin d'exécutable
    /// </summary>
    private static string? ExtractDirectoryFromPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        try
        {
            // Nettoyer le chemin
            var cleanPath = path.Trim();
            
            // Enlever les guillemets
            if (cleanPath.StartsWith('"'))
            {
                var endQuote = cleanPath.IndexOf('"', 1);
                if (endQuote > 0)
                {
                    cleanPath = cleanPath[1..endQuote];
                }
            }

            // Cas MsiExec - pas de dossier exploitable directement
            if (cleanPath.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Trouver le .exe dans le chemin
            var exeIndex = cleanPath.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIndex > 0)
            {
                cleanPath = cleanPath[..(exeIndex + 4)];
            }

            // Vérifier que c'est un chemin valide
            if (!Path.IsPathRooted(cleanPath)) return null;

            var directory = Path.GetDirectoryName(cleanPath);
            
            // Remonter d'un niveau si on est dans un sous-dossier uninst/uninstall
            if (!string.IsNullOrEmpty(directory))
            {
                var dirName = Path.GetFileName(directory)?.ToLowerInvariant();
                if (dirName is "uninst" or "uninstall" or "_uninst" or "bin")
                {
                    directory = Path.GetDirectoryName(directory);
                }
            }

            return directory;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Cherche un dossier correspondant au nom du programme dans tous les emplacements connus
    /// </summary>
    private static string? FindProgramDirectory(string programName, string publisher)
    {
        if (string.IsNullOrEmpty(programName)) return null;

        // Tous les emplacements possibles
        var searchPaths = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"),
        };

        // Ajouter les dossiers d'éditeur dans AppData
        if (!string.IsNullOrEmpty(publisher))
        {
            var publisherName = publisher.Split(' ')[0]; // Premier mot de l'éditeur
            searchPaths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), publisherName));
            searchPaths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), publisherName));
        }

        // Simplifier le nom pour la recherche
        var searchName = SimplifyName(programName);

        // Extraire les mots clés significatifs (> 2 caractères)
        var keywords = programName.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !IsCommonWord(w))
            .Take(3)
            .ToArray();

        foreach (var basePath in searchPaths)
        {
            if (string.IsNullOrEmpty(basePath) || !Directory.Exists(basePath)) continue;

            var result = SearchInDirectory(basePath, searchName, keywords, maxDepth: 2);
            if (!string.IsNullOrEmpty(result))
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>
    /// Recherche récursive dans un dossier
    /// </summary>
    private static string? SearchInDirectory(string basePath, string searchName, string[] keywords, int maxDepth, int currentDepth = 0)
    {
        if (currentDepth > maxDepth) return null;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(basePath))
            {
                var dirName = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(dirName)) continue;

                // Ignorer certains dossiers système
                if (IsSystemDirectory(dirName)) continue;

                var simplifiedDirName = SimplifyName(dirName);

                // Correspondance exacte (simplifiée)
                if (simplifiedDirName == searchName)
                {
                    return dir;
                }

                // Correspondance par mots clés (tous les mots doivent être présents)
                if (keywords.Length > 0 && keywords.All(w => dirName.Contains(w, StringComparison.OrdinalIgnoreCase)))
                {
                    return dir;
                }

                // Le nom du dossier contient le nom du programme ou vice versa
                if (simplifiedDirName.Length >= 4 && searchName.Length >= 4)
                {
                    if (simplifiedDirName.Contains(searchName) || searchName.Contains(simplifiedDirName))
                    {
                        return dir;
                    }
                }

                // Recherche récursive dans les sous-dossiers (pour les structures Publisher/App)
                if (currentDepth < maxDepth)
                {
                    var subResult = SearchInDirectory(dir, searchName, keywords, maxDepth, currentDepth + 1);
                    if (!string.IsNullOrEmpty(subResult))
                    {
                        return subResult;
                    }
                }
            }
        }
        catch { /* Accès refusé - ignorer */ }

        return null;
    }

    /// <summary>
    /// Simplifie un nom pour la comparaison
    /// </summary>
    private static string SimplifyName(string name)
    {
        return name
            .Replace(" ", "")
            .Replace("-", "")
            .Replace("_", "")
            .Replace(".", "")
            .ToLowerInvariant();
    }

    /// <summary>
    /// Vérifie si un mot est commun et doit être ignoré
    /// </summary>
    private static bool IsCommonWord(string word)
    {
        string[] commonWords = ["the", "for", "and", "app", "application", "software", "program", "tool", "tools", "version", "update", "edition", "pro", "free", "lite"];
        return commonWords.Contains(word.ToLowerInvariant());
    }

    /// <summary>
    /// Vérifie si un dossier est un dossier système à ignorer
    /// </summary>
    private static bool IsSystemDirectory(string dirName)
    {
        string[] systemDirs = ["windows", "system32", "syswow64", "microsoft", "packages", "cache", "temp", "tmp", "logs", "crash", "dumps"];
        return systemDirs.Contains(dirName.ToLowerInvariant());
    }

    /// <summary>
    /// Calcule la taille d'un dossier récursivement
    /// </summary>
    private static long CalculateDirectorySize(string path, CancellationToken cancellationToken)
    {
        long size = 0;

        try
        {
            // Fichiers du dossier courant
            foreach (var file in Directory.EnumerateFiles(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var fileInfo = new FileInfo(file);
                    size += fileInfo.Length;
                }
                catch { /* Accès refusé - ignorer */ }
            }

            // Sous-dossiers (récursif)
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    size += CalculateDirectorySize(dir, cancellationToken);
                }
                catch { /* Accès refusé - ignorer */ }
            }
        }
        catch { /* Accès refusé - ignorer */ }

        return size;
    }

    /// <summary>
    /// Lit tous les programmes installés depuis le registre
    /// </summary>
    public async Task<List<InstalledProgram>> GetInstalledProgramsAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var programs = new List<InstalledProgram>();
        var processed = 0;
        var total = UninstallPaths.Length * RegistryHives.Length;

        await Task.Run(() =>
        {
            foreach (var hive in RegistryHives)
            {
                foreach (var path in UninstallPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                        using var uninstallKey = baseKey.OpenSubKey(path);

                        if (uninstallKey == null) continue;

                        var subKeyNames = uninstallKey.GetSubKeyNames();

                        foreach (var subKeyName in subKeyNames)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            try
                            {
                                using var programKey = uninstallKey.OpenSubKey(subKeyName);
                                if (programKey == null) continue;

                                var program = ReadProgramFromRegistry(programKey, subKeyName, hive, path);
                                if (program != null && !string.IsNullOrWhiteSpace(program.DisplayName))
                                {
                                    // Éviter les doublons
                                    if (!programs.Any(p => p.DisplayName == program.DisplayName && p.Version == program.Version))
                                    {
                                        programs.Add(program);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Erreur lecture programme {subKeyName}: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Erreur accès registre {hive}\\{path}: {ex.Message}");
                    }

                    processed++;
                    progress?.Report(new ScanProgress
                    {
                        Phase = ScanPhase.ScanningRegistry,
                        Percentage = (processed * 100) / total,
                        StatusMessage = "Scan du registre...",
                        ProcessedCount = processed,
                        TotalCount = total
                    });
                }
            }
        }, cancellationToken);

        return programs.OrderBy(p => p.DisplayName).ToList();
    }

    /// <summary>
    /// Lit les informations d'un programme depuis une clé de registre
    /// </summary>
    private static InstalledProgram? ReadProgramFromRegistry(
        RegistryKey key, string keyName, RegistryHive hive, string path)
    {
        var displayName = key.GetValue("DisplayName") as string;
        
        // Utiliser le nom de la clé comme fallback si pas de DisplayName
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = FormatKeyNameAsDisplayName(keyName);
        }
        
        // Si toujours vide ou GUID brut non formaté, ignorer
        if (string.IsNullOrWhiteSpace(displayName)) return null;

        // Ignorer les mises à jour et patches Windows
        var releaseType = key.GetValue("ReleaseType") as string;
        var parentKeyName = key.GetValue("ParentKeyName") as string;
        if (!string.IsNullOrEmpty(releaseType) || !string.IsNullOrEmpty(parentKeyName))
        {
            if (releaseType is "Security Update" or "Update Rollup" or "Hotfix" or "Service Pack")
                return null;
        }

        var systemComponentFlag = (key.GetValue("SystemComponent") as int?) == 1;
        var uninstallString = key.GetValue("UninstallString") as string ?? "";
        var publisher = key.GetValue("Publisher") as string ?? "";
        
        // Déterminer si c'est vraiment un composant système
        // Un programme est considéré comme composant système seulement si:
        // - Il a le flag SystemComponent=1 ET
        // - Il est de Microsoft/Windows OU n'a pas de commande de désinstallation
        var isSystemComponent = systemComponentFlag && 
            (IsSystemPublisher(publisher) || string.IsNullOrEmpty(uninstallString));
        
        // Si pas de commande de désinstallation et flag système, ignorer
        if (string.IsNullOrEmpty(uninstallString) && systemComponentFlag)
            return null;
        
        // Ignorer les entrées qui n'ont ni uninstall ni install location (probablement des résidus)
        var installLocation = key.GetValue("InstallLocation") as string ?? "";
        if (string.IsNullOrEmpty(uninstallString) && string.IsNullOrEmpty(installLocation))
            return null;

        // Parser la date d'installation
        DateTime? installDate = null;
        var installDateStr = key.GetValue("InstallDate") as string;
        if (!string.IsNullOrEmpty(installDateStr) && installDateStr.Length == 8)
        {
            if (DateTime.TryParseExact(installDateStr, "yyyyMMdd", 
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                installDate = date;
            }
        }

        // Taille estimée (en Ko dans le registre)
        var estimatedSizeKb = key.GetValue("EstimatedSize") as int? ?? 0;

        return new InstalledProgram
        {
            Id = keyName,
            DisplayName = displayName,
            Publisher = publisher,
            Version = key.GetValue("DisplayVersion") as string ?? "",
            InstallDate = installDate,
            EstimatedSize = estimatedSizeKb * 1024L, // Convertir en octets
            InstallLocation = installLocation,
            UninstallString = uninstallString,
            QuietUninstallString = key.GetValue("QuietUninstallString") as string ?? "",
            ModifyPath = key.GetValue("ModifyPath") as string ?? "",
            RegistryKeyName = keyName,
            RegistrySource = hive.ToString(),
            RegistryPath = $"{hive}\\{path}\\{keyName}",
            HelpLink = key.GetValue("HelpLink") as string ?? "",
            UrlInfoAbout = key.GetValue("URLInfoAbout") as string ?? "",
            IsSystemComponent = isSystemComponent,
            IsWindowsApp = false,
            CanModify = !string.IsNullOrEmpty(key.GetValue("ModifyPath") as string),
            CanRepair = !string.IsNullOrEmpty(key.GetValue("RepairPath") as string)
        };
    }

    /// <summary>
    /// Formate un nom de clé de registre en nom d'affichage lisible
    /// </summary>
    private static string? FormatKeyNameAsDisplayName(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName)) return null;
        
        // Ignorer les GUIDs purs (format {xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx})
        if (keyName.StartsWith('{') && keyName.EndsWith('}') && keyName.Length == 38)
            return null;
        
        // Ignorer les identifiants de mise à jour KB
        if (keyName.StartsWith("KB", StringComparison.OrdinalIgnoreCase) && keyName.Length <= 10)
            return null;
        
        // Ignorer certains préfixes système
        string[] ignoredPrefixes = ["Microsoft.Windows.", "Windows.", "MicrosoftWindows."];
        if (ignoredPrefixes.Any(p => keyName.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return null;
        
        var displayName = keyName;
        
        // Formater les noms de packages (Publisher.AppName_version)
        if (displayName.Contains('_'))
        {
            var underscoreIndex = displayName.IndexOf('_');
            displayName = displayName[..underscoreIndex];
        }
        
        // Remplacer les points par des espaces pour les noms de packages
        if (displayName.Contains('.') && !displayName.Contains(' '))
        {
            var parts = displayName.Split('.');
            // Ignorer le premier segment s'il ressemble à un éditeur (ex: Microsoft, Adobe)
            if (parts.Length >= 2 && IsLikelyPublisher(parts[0]))
            {
                displayName = string.Join(" ", parts.Skip(1));
            }
            else
            {
                displayName = string.Join(" ", parts);
            }
        }
        
        // Nettoyer les caractères spéciaux courants
        displayName = displayName.Replace("_", " ").Trim();
        
        // Vérifier que le résultat est utilisable
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length < 2)
            return null;
        
        return displayName;
    }
    
    /// <summary>
    /// Vérifie si un segment ressemble à un nom d'éditeur
    /// </summary>
    private static bool IsLikelyPublisher(string segment)
    {
        string[] knownPublishers = [
            "Microsoft", "Adobe", "Google", "Apple", "Mozilla", "Oracle", 
            "Intel", "AMD", "NVIDIA", "Realtek", "Logitech", "Dell", "HP",
            "Lenovo", "ASUS", "Acer", "Samsung", "Sony", "LG", "Razer",
            "Corsair", "SteelSeries", "JetBrains", "Autodesk", "Corel"
        ];
        return knownPublishers.Any(p => segment.Equals(p, StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// Vérifie si l'éditeur est un éditeur de composants système (Microsoft, Windows)
    /// Les programmes de ces éditeurs avec SystemComponent=1 sont vraiment des composants système
    /// </summary>
    private static bool IsSystemPublisher(string publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher)) return false;
        
        string[] systemPublishers = [
            "Microsoft",
            "Microsoft Corporation",
            "Microsoft Corporations",
            "Windows"
        ];
        
        return systemPublishers.Any(sp => 
            publisher.Equals(sp, StringComparison.OrdinalIgnoreCase) ||
            publisher.StartsWith(sp + " ", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Supprime une clé de registre (pour le nettoyage des résidus)
    /// </summary>
    public bool DeleteRegistryKey(string fullPath)
    {
        try
        {
            var parts = fullPath.Split('\\', 2);
            if (parts.Length != 2) return false;

            var hive = parts[0] switch
            {
                "HKEY_LOCAL_MACHINE" or "HKLM" => RegistryHive.LocalMachine,
                "HKEY_CURRENT_USER" or "HKCU" => RegistryHive.CurrentUser,
                "HKEY_CLASSES_ROOT" or "HKCR" => RegistryHive.ClassesRoot,
                _ => throw new ArgumentException($"Hive non reconnu: {parts[0]}")
            };

            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            
            // Trouver le parent et le nom de la clé à supprimer
            var lastSlash = parts[1].LastIndexOf('\\');
            if (lastSlash == -1) return false;

            var parentPath = parts[1][..lastSlash];
            var keyName = parts[1][(lastSlash + 1)..];

            using var parentKey = baseKey.OpenSubKey(parentPath, writable: true);
            if (parentKey == null) return false;

            parentKey.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur suppression clé registre {fullPath}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Exporte une clé de registre pour backup
    /// </summary>
    public async Task<bool> ExportRegistryKeyAsync(string keyPath, string outputFile)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = $"export \"{keyPath}\" \"{outputFile}\" /y",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur export registre: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Recherche des clés de registre orphelines liées à un programme
    /// </summary>
    public List<ResidualItem> FindOrphanedRegistryKeys(string programName, string installLocation)
    {
        var residuals = new List<ResidualItem>();
        var searchTerms = GenerateSearchTerms(programName);

        // Emplacements courants où des résidus peuvent rester
        string[] searchPaths =
        [
            @"SOFTWARE",
            @"SOFTWARE\WOW6432Node",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
            @"SOFTWARE\Classes\CLSID",
        ];

        foreach (var searchPath in searchPaths)
        {
            try
            {
                SearchRegistryPath(RegistryHive.LocalMachine, searchPath, searchTerms, residuals, programName);
                SearchRegistryPath(RegistryHive.CurrentUser, searchPath, searchTerms, residuals, programName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur recherche registre {searchPath}: {ex.Message}");
            }
        }

        return residuals;
    }

    private void SearchRegistryPath(RegistryHive hive, string path, string[] searchTerms, 
        List<ResidualItem> residuals, string programName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var searchKey = baseKey.OpenSubKey(path);
            if (searchKey == null) return;

            foreach (var subKeyName in searchKey.GetSubKeyNames())
            {
                foreach (var term in searchTerms)
                {
                    if (subKeyName.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        var fullPath = $"{hive}\\{path}\\{subKeyName}";
                        
                        // Éviter les doublons
                        if (!residuals.Any(r => r.Path == fullPath))
                        {
                            residuals.Add(new ResidualItem
                            {
                                Path = fullPath,
                                Type = ResidualType.RegistryKey,
                                ProgramName = programName,
                                Confidence = (ConfidenceLevel)Math.Min((int)ConfidenceLevel.VeryHigh, CalculateConfidence(subKeyName, searchTerms) / 25),
                                Reason = $"Clé de registre contenant '{term}'"
                            });
                        }
                        break;
                    }
                }
            }
        }
        catch { /* Accès refusé - ignorer */ }
    }

    private static string[] GenerateSearchTerms(string programName)
    {
        var terms = new List<string> { programName };
        
        // Ajouter des variations
        var simplified = programName
            .Replace(" ", "")
            .Replace("-", "")
            .Replace("_", "");
        
        if (simplified != programName)
            terms.Add(simplified);

        // Premiers mots significatifs
        var words = programName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 1 && words[0].Length > 3)
            terms.Add(words[0]);

        return terms.Distinct().ToArray();
    }

    private static int CalculateConfidence(string foundName, string[] searchTerms)
    {
        foreach (var term in searchTerms)
        {
            if (foundName.Equals(term, StringComparison.OrdinalIgnoreCase))
                return 95;
            if (foundName.StartsWith(term, StringComparison.OrdinalIgnoreCase))
                return 85;
        }
        return 60;
    }
}
