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

        var isSystemComponent = (key.GetValue("SystemComponent") as int?) == 1;
        var uninstallString = key.GetValue("UninstallString") as string ?? "";
        
        // Si pas de commande de désinstallation et composant système, ignorer
        if (string.IsNullOrEmpty(uninstallString) && isSystemComponent)
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
            Publisher = key.GetValue("Publisher") as string ?? "",
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
