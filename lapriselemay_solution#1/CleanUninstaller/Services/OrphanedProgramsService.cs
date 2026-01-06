using Microsoft.Win32;
using CleanUninstaller.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace CleanUninstaller.Services;

/// <summary>
/// Résultat du nettoyage des entrées orphelines
/// </summary>
public class OrphanedCleanupResult
{
    public int TotalCleaned { get; set; }
    public List<OrphanedEntry> DeletedEntries { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
/// Service de détection et nettoyage des programmes orphelins et entrées mortes
/// Scanne le registre pour trouver les entrées de désinstallation invalides
/// </summary>
public partial class OrphanedProgramsService
{
    // Chemins de registre pour les programmes installés
    private static readonly (string Path, RegistryKey Root, string Source)[] UninstallRegistryPaths =
    [
        (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", Registry.LocalMachine, "HKLM"),
        (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", Registry.CurrentUser, "HKCU"),
        (@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", Registry.LocalMachine, "HKLM (x86)")
    ];

    // Programmes système à ignorer (ne jamais marquer comme orphelins)
    // Ces patterns sont vérifiés par Contains (case-insensitive)
    private static readonly string[] SystemProgramsToIgnore =
    [
        "Microsoft Edge",
        "Edge Update",
        "EdgeWebView",
        "Visual Studio Installer",
        "VS Installer",
        "Windows SDK",
        "Microsoft .NET",
        "Windows Desktop Runtime",
        "Microsoft ASP.NET",
        "Office Click-to-Run",
        "Microsoft Update Health Tools",
        "Windows PC Health Check",
        "Microsoft OneDrive",
        "Microsoft Teams",
        "Windows Driver Kit",
        "Microsoft Visual C++",
        "NVIDIA",
        "Intel",
        "AMD Software",
        "Realtek"
    ];

    // Clés de registre exactes à ignorer (GUIDs connus pour des composants système)
    private static readonly HashSet<string> ExactKeysToIgnore = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft Edge Update",
        "Microsoft Edge",
        "Microsoft EdgeWebView",
        "Connection Manager"
    };

    // Patterns de clés à ignorer (StartsWith)
    private static readonly string[] KeyPatternsToIgnore =
    [
        "KB",           // Windows Updates
        "MUI",          // Language packs
        "WIC",          // Windows Imaging Component
        "Hotfix",
        "Security Update",
        "Service Pack"
    ];

    // Chemins connus où des programmes système s'installent sans InstallLocation dans le registre
    private static readonly Dictionary<string, string[]> KnownSystemPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft Edge Update"] = [
            @"C:\Program Files (x86)\Microsoft\EdgeUpdate",
            @"C:\Program Files\Microsoft\EdgeUpdate"
        ],
        ["Microsoft Edge"] = [
            @"C:\Program Files (x86)\Microsoft\Edge",
            @"C:\Program Files\Microsoft\Edge"
        ],
        ["Visual Studio Installer"] = [
            @"C:\Program Files (x86)\Microsoft Visual Studio\Installer",
            @"C:\Program Files\Microsoft Visual Studio\Installer"
        ]
    };

    /// <summary>
    /// Scanne le système pour trouver les entrées orphelines
    /// </summary>
    public async Task<List<OrphanedEntry>> ScanOrphanedEntriesAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var orphanedEntries = new List<OrphanedEntry>();

        await Task.Run(() =>
        {
            var totalPaths = UninstallRegistryPaths.Length;
            var currentPath = 0;

            foreach (var (path, root, source) in UninstallRegistryPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                currentPath++;
                var baseProgress = (currentPath - 1) * 100 / totalPaths;
                progress?.Report(new ScanProgress(baseProgress, $"Scan du registre {source}..."));

                try
                {
                    using var baseKey = root.OpenSubKey(path);
                    if (baseKey == null) continue;

                    var subKeyNames = baseKey.GetSubKeyNames();
                    var subKeyCount = subKeyNames.Length;

                    for (int i = 0; i < subKeyCount; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var subKeyName = subKeyNames[i];
                        var subProgress = baseProgress + (i * 100 / totalPaths / subKeyCount);
                        
                        if (i % 10 == 0)
                        {
                            progress?.Report(new ScanProgress(subProgress, $"Analyse de {subKeyName}..."));
                        }

                        try
                        {
                            using var subKey = baseKey.OpenSubKey(subKeyName);
                            if (subKey == null) continue;

                            var orphanedEntry = AnalyzeRegistryEntry(subKey, subKeyName, $"{source}\\{path}");
                            if (orphanedEntry != null)
                            {
                                orphanedEntries.Add(orphanedEntry);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Erreur analyse {subKeyName}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Erreur accès registre {path}: {ex.Message}");
                }
            }

            progress?.Report(new ScanProgress(100, $"{orphanedEntries.Count} entrées orphelines trouvées"));
        }, cancellationToken);

        return orphanedEntries
            .OrderBy(e => e.Type)
            .ThenBy(e => e.DisplayName)
            .ToList();
    }

    /// <summary>
    /// Analyse une entrée de registre pour déterminer si elle est orpheline
    /// </summary>
    private OrphanedEntry? AnalyzeRegistryEntry(RegistryKey key, string keyName, string basePath)
    {
        var displayName = key.GetValue("DisplayName")?.ToString();
        var uninstallString = key.GetValue("UninstallString")?.ToString();
        var quietUninstallString = key.GetValue("QuietUninstallString")?.ToString();
        var installLocation = key.GetValue("InstallLocation")?.ToString();
        var displayIcon = key.GetValue("DisplayIcon")?.ToString();
        var publisher = key.GetValue("Publisher")?.ToString() ?? "";
        var version = key.GetValue("DisplayVersion")?.ToString() ?? "";
        var systemComponent = key.GetValue("SystemComponent");
        var parentKeyName = key.GetValue("ParentKeyName")?.ToString();
        var windowsInstaller = key.GetValue("WindowsInstaller");

        // === PHASE 1: EXCLUSIONS ABSOLUES ===
        
        // Ignorer les composants système marqués
        if (systemComponent != null && Convert.ToInt32(systemComponent) == 1)
            return null;

        // Ignorer les sous-composants (ont un parent)
        if (!string.IsNullOrEmpty(parentKeyName))
            return null;

        // Ignorer les clés exactes connues
        if (ExactKeysToIgnore.Contains(keyName))
            return null;

        // Ignorer les patterns de clés spécifiques
        foreach (var pattern in KeyPatternsToIgnore)
        {
            if (keyName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        // Nom à vérifier (displayName ou keyName)
        var nameToCheck = !string.IsNullOrWhiteSpace(displayName) ? displayName : keyName;

        // Ignorer les programmes système connus par leur nom
        foreach (var systemProgram in SystemProgramsToIgnore)
        {
            if (nameToCheck.Contains(systemProgram, StringComparison.OrdinalIgnoreCase) ||
                keyName.Contains(systemProgram, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        // Ignorer les mises à jour Windows/Microsoft (avec tolérance)
        if (publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            var updateKeywords = new[] { "Update", "Hotfix", "Security", "Patch", "Redistributable" };
            foreach (var keyword in updateKeywords)
            {
                if (keyName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    nameToCheck.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return null;
            }
        }

        // === PHASE 2: VÉRIFICATION EXISTENCE PROGRAMME ===

        // Nettoyer les chemins (enlever les guillemets)
        var cleanInstallLocation = CleanPath(installLocation);
        var cleanUninstallString = CleanPath(uninstallString);
        var cleanQuietUninstall = CleanPath(quietUninstallString);
        var cleanDisplayIcon = CleanPath(displayIcon);

        // Vérifier si le programme existe via des chemins connus
        if (ProgramExistsViaKnownPaths(nameToCheck, keyName))
            return null;

        // Vérifier si le programme existe via InstallLocation
        if (!string.IsNullOrWhiteSpace(cleanInstallLocation) && Directory.Exists(cleanInstallLocation))
        {
            // Le dossier existe, vérifier s'il contient des fichiers
            if (DirectoryHasContent(cleanInstallLocation))
                return null;
        }

        // Vérifier si le désinstalleur existe
        var uninstallerPath = ExtractExecutablePath(cleanUninstallString);
        if (!string.IsNullOrWhiteSpace(uninstallerPath) && File.Exists(uninstallerPath))
            return null;

        // Vérifier via QuietUninstallString
        var quietUninstallerPath = ExtractExecutablePath(cleanQuietUninstall);
        if (!string.IsNullOrWhiteSpace(quietUninstallerPath) && File.Exists(quietUninstallerPath))
            return null;

        // Vérifier via DisplayIcon
        var iconPath = ExtractExecutablePath(cleanDisplayIcon);
        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
            return null;

        // Vérifier les installeurs MSI
        if (windowsInstaller != null && Convert.ToInt32(windowsInstaller) == 1)
        {
            if (keyName.StartsWith("{") && keyName.EndsWith("}") && IsMsiProductInstalled(keyName))
                return null;
        }

        // === PHASE 3: CLASSIFICATION DE L'ENTRÉE ORPHELINE ===

        var fullPath = $"{basePath}\\{keyName}";
        var name = !string.IsNullOrWhiteSpace(displayName) ? displayName : keyName;

        // Déterminer le type d'orphelin
        var (entryType, reason, invalidPath, confidence) = ClassifyOrphanedEntry(
            displayName, uninstallString, quietUninstallString, installLocation, 
            cleanUninstallString, cleanInstallLocation, uninstallerPath);

        // Ne pas rapporter les entrées avec très peu d'informations qui sont probablement des composants système
        if (entryType == OrphanedEntryType.EmptyEntry && string.IsNullOrWhiteSpace(displayName))
        {
            // Entrée sans nom = probablement un composant système caché
            return null;
        }

        // Réduire la confiance pour les entrées Microsoft sans UninstallString
        // (Beaucoup de composants MS légitimes n'ont pas de désinstalleur visible)
        if (publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) && 
            string.IsNullOrWhiteSpace(uninstallString))
        {
            confidence = ConfidenceLevel.Low;
        }

        return new OrphanedEntry
        {
            DisplayName = name,
            RegistryPath = fullPath,
            RegistryKeyName = keyName,
            Type = entryType,
            Reason = reason,
            InvalidPath = invalidPath,
            Publisher = publisher,
            Version = version,
            InstallDate = ParseInstallDate(key.GetValue("InstallDate")?.ToString()),
            EstimatedSize = GetEstimatedSize(key),
            Confidence = confidence,
            IsSelected = confidence >= ConfidenceLevel.Medium
        };
    }

    /// <summary>
    /// Vérifie si un programme existe via des chemins connus
    /// </summary>
    private bool ProgramExistsViaKnownPaths(string displayName, string keyName)
    {
        // Vérifier les chemins connus par nom exact de clé
        if (KnownSystemPaths.TryGetValue(keyName, out var pathsByKey))
        {
            if (pathsByKey.Any(p => Directory.Exists(p) && DirectoryHasContent(p)))
                return true;
        }

        // Vérifier par nom d'affichage (recherche partielle)
        foreach (var (name, paths) in KnownSystemPaths)
        {
            if (displayName?.Contains(name, StringComparison.OrdinalIgnoreCase) == true ||
                keyName.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                if (paths.Any(p => Directory.Exists(p) && DirectoryHasContent(p)))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Vérifie si un dossier contient du contenu significatif
    /// </summary>
    private bool DirectoryHasContent(string path)
    {
        try
        {
            // Un dossier avec au moins un fichier ou sous-dossier est considéré comme ayant du contenu
            return Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch
        {
            // En cas d'erreur d'accès, on assume qu'il y a du contenu
            return true;
        }
    }

    /// <summary>
    /// Classifie une entrée orpheline et détermine le type et la raison
    /// </summary>
    private (OrphanedEntryType type, string reason, string invalidPath, ConfidenceLevel confidence) 
        ClassifyOrphanedEntry(
            string? displayName, 
            string? uninstallString, 
            string? quietUninstallString,
            string? installLocation,
            string? cleanUninstallString,
            string? cleanInstallLocation,
            string? uninstallerPath)
    {
        // Entrée complètement vide
        if (string.IsNullOrWhiteSpace(displayName) && 
            string.IsNullOrWhiteSpace(uninstallString) && 
            string.IsNullOrWhiteSpace(installLocation))
        {
            return (OrphanedEntryType.EmptyEntry, 
                    "Entrée de registre vide", 
                    "", 
                    ConfidenceLevel.Medium);
        }

        // Désinstalleur manquant
        if (!string.IsNullOrWhiteSpace(uninstallString) && 
            !string.IsNullOrWhiteSpace(uninstallerPath) && 
            !File.Exists(uninstallerPath))
        {
            return (OrphanedEntryType.MissingUninstaller, 
                    "Le programme de désinstallation n'existe plus", 
                    uninstallerPath, 
                    ConfidenceLevel.High);
        }

        // Dossier d'installation manquant
        if (!string.IsNullOrWhiteSpace(cleanInstallLocation) && 
            !Directory.Exists(cleanInstallLocation))
        {
            return (OrphanedEntryType.MissingInstallLocation, 
                    "Le dossier d'installation n'existe plus", 
                    cleanInstallLocation, 
                    ConfidenceLevel.High);
        }

        // Pas de désinstalleur défini mais a un nom
        if (string.IsNullOrWhiteSpace(uninstallString) && 
            string.IsNullOrWhiteSpace(quietUninstallString) &&
            !string.IsNullOrWhiteSpace(displayName))
        {
            return (OrphanedEntryType.InvalidRegistryData, 
                    "Aucun désinstalleur configuré", 
                    "", 
                    ConfidenceLevel.Low);
        }

        // Par défaut: données de registre invalides
        return (OrphanedEntryType.InvalidRegistryData, 
                "Données de registre incomplètes", 
                "", 
                ConfidenceLevel.Medium);
    }

    /// <summary>
    /// Nettoie un chemin (enlève guillemets, espaces)
    /// </summary>
    private string? CleanPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return path.Trim().Trim('"', '\'').Trim();
    }

    /// <summary>
    /// Extrait le chemin de l'exécutable d'une chaîne de commande
    /// </summary>
    private string? ExtractExecutablePath(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return null;

        var cleaned = commandLine.Trim();

        // Cas 1: Chemin entre guillemets
        if (cleaned.StartsWith('"'))
        {
            var endQuote = cleaned.IndexOf('"', 1);
            if (endQuote > 1)
                return cleaned[1..endQuote];
        }

        // Cas 2: MsiExec
        if (cleaned.StartsWith("MsiExec", StringComparison.OrdinalIgnoreCase))
        {
            return null; // MSI géré séparément
        }

        // Cas 3: Chemin simple jusqu'au premier espace
        var spaceIndex = cleaned.IndexOf(' ');
        if (spaceIndex > 0)
            return cleaned[..spaceIndex];

        // Cas 4: Chemin sans arguments
        return cleaned;
    }

    /// <summary>
    /// Vérifie si un produit MSI est installé
    /// </summary>
    private bool IsMsiProductInstalled(string productCode)
    {
        try
        {
            // Vérifier dans le registre des produits MSI
            var msiPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Products";
            var cleanCode = productCode.Trim('{', '}').Replace("-", "");
            
            // Format MSI: inversé par groupes
            var msiCode = ConvertToMsiProductCode(cleanCode);
            
            using var key = Registry.LocalMachine.OpenSubKey($@"{msiPath}\{msiCode}");
            if (key != null)
            {
                using var installProps = key.OpenSubKey("InstallProperties");
                if (installProps != null)
                {
                    var localPackage = installProps.GetValue("LocalPackage")?.ToString();
                    if (!string.IsNullOrEmpty(localPackage) && File.Exists(localPackage))
                        return true;
                }
            }

            // Vérification alternative via la clé Uninstall elle-même
            // Si on peut lire les données, c'est probablement valide
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Convertit un GUID en format de code produit MSI
    /// </summary>
    private string ConvertToMsiProductCode(string guid)
    {
        // MSI inverse les caractères par groupes: 8-4-4-2-2-2-2-2-2-2-2
        var clean = guid.Replace("-", "").ToUpperInvariant();
        if (clean.Length != 32) return guid;

        var result = new char[32];
        var groups = new[] { 8, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2 };
        var pos = 0;
        var destPos = 0;

        foreach (var len in groups)
        {
            for (int i = len - 1; i >= 0; i--)
            {
                result[destPos++] = clean[pos + i];
            }
            pos += len;
        }

        return new string(result);
    }

    /// <summary>
    /// Parse la date d'installation
    /// </summary>
    private DateTime? ParseInstallDate(string? dateString)
    {
        if (string.IsNullOrWhiteSpace(dateString))
            return null;

        // Format YYYYMMDD
        if (dateString.Length == 8 && 
            DateTime.TryParseExact(dateString, "yyyyMMdd", null, 
                System.Globalization.DateTimeStyles.None, out var date))
        {
            return date;
        }

        return null;
    }

    /// <summary>
    /// Obtient la taille estimée depuis le registre
    /// </summary>
    private long GetEstimatedSize(RegistryKey key)
    {
        try
        {
            var size = key.GetValue("EstimatedSize");
            if (size != null)
            {
                return Convert.ToInt64(size) * 1024; // KB to bytes
            }
        }
        catch { }
        
        return 0;
    }

    /// <summary>
    /// Nettoie les entrées orphelines sélectionnées
    /// </summary>
    public async Task<OrphanedCleanupResult> CleanupOrphanedEntriesAsync(
        IEnumerable<OrphanedEntry> entries,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var selectedEntries = entries.Where(e => e.IsSelected).ToList();
        var result = new OrphanedCleanupResult();

        if (selectedEntries.Count == 0)
            return result;

        await Task.Run(() =>
        {
            for (int i = 0; i < selectedEntries.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = selectedEntries[i];
                var progressPercent = (i + 1) * 100 / selectedEntries.Count;
                progress?.Report(new ScanProgress(progressPercent, $"Suppression de {entry.DisplayName}..."));

                try
                {
                    DeleteRegistryEntry(entry);
                    result.TotalCleaned++;
                    result.DeletedEntries.Add(entry);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Erreur suppression {entry.DisplayName}: {ex.Message}");
                    result.Errors.Add($"{entry.DisplayName}: {ex.Message}");
                }
            }

            progress?.Report(new ScanProgress(100, $"{result.TotalCleaned} entrées supprimées"));
        }, cancellationToken);

        return result;
    }

    /// <summary>
    /// Exporte les entrées dans un fichier .reg pour backup
    /// </summary>
    public async Task ExportEntriesAsync(IEnumerable<OrphanedEntry> entries, string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Windows Registry Editor Version 5.00");
        sb.AppendLine();
        sb.AppendLine("; Backup des entrées orphelines - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("; Fichier généré par CleanUninstaller");
        sb.AppendLine();

        foreach (var entry in entries)
        {
            try
            {
                var regPath = ConvertToRegPath(entry.RegistryPath);
                sb.AppendLine($"[{regPath}]");

                // Exporter toutes les valeurs de la clé
                ExportRegistryKey(entry, sb);
                sb.AppendLine();
            }
            catch (Exception ex)
            {
                sb.AppendLine($"; Erreur export {entry.DisplayName}: {ex.Message}");
                sb.AppendLine();
            }
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.Unicode);
    }

    /// <summary>
    /// Convertit un chemin interne vers le format .reg
    /// </summary>
    private string ConvertToRegPath(string internalPath)
    {
        // Convertir "HKLM\\..." en "HKEY_LOCAL_MACHINE\\..."
        if (internalPath.StartsWith("HKLM (x86)\\"))
            return "HKEY_LOCAL_MACHINE\\" + internalPath[12..];
        if (internalPath.StartsWith("HKLM\\"))
            return "HKEY_LOCAL_MACHINE\\" + internalPath[5..];
        if (internalPath.StartsWith("HKCU\\"))
            return "HKEY_CURRENT_USER\\" + internalPath[5..];
        return internalPath;
    }

    /// <summary>
    /// Exporte les valeurs d'une clé de registre
    /// </summary>
    private void ExportRegistryKey(OrphanedEntry entry, StringBuilder sb)
    {
        var parts = entry.RegistryPath.Split('\\', 2);
        if (parts.Length < 2) return;

        var rootStr = parts[0];
        var subPath = parts[1];

        RegistryKey? root = rootStr switch
        {
            "HKLM" or "HKLM (x86)" => Registry.LocalMachine,
            "HKCU" => Registry.CurrentUser,
            _ => null
        };

        if (root == null) return;

        try
        {
            using var key = root.OpenSubKey(subPath);
            if (key == null) return;

            foreach (var valueName in key.GetValueNames())
            {
                var value = key.GetValue(valueName);
                var valueKind = key.GetValueKind(valueName);

                var exportedValue = FormatRegistryValue(valueName, value, valueKind);
                if (!string.IsNullOrEmpty(exportedValue))
                    sb.AppendLine(exportedValue);
            }
        }
        catch { }
    }

    /// <summary>
    /// Formate une valeur de registre pour l'export .reg
    /// </summary>
    private string FormatRegistryValue(string name, object? value, RegistryValueKind kind)
    {
        var quotedName = string.IsNullOrEmpty(name) ? "@" : $"\"{EscapeRegString(name)}\"";

        return kind switch
        {
            RegistryValueKind.String => $"{quotedName}=\"{EscapeRegString(value?.ToString() ?? "")}\"",
            RegistryValueKind.ExpandString => $"{quotedName}=hex(2):{ToHexString(value?.ToString() ?? "")}",
            RegistryValueKind.DWord => $"{quotedName}=dword:{Convert.ToUInt32(value):x8}",
            RegistryValueKind.QWord => $"{quotedName}=hex(b):{ToHexBytes(BitConverter.GetBytes(Convert.ToUInt64(value)))}",
            RegistryValueKind.Binary => $"{quotedName}=hex:{ToHexBytes((byte[]?)value ?? [])}",
            RegistryValueKind.MultiString => $"{quotedName}=hex(7):{ToHexMultiString((string[]?)value ?? [])}",
            _ => ""
        };
    }

    private string EscapeRegString(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    
    private string ToHexString(string s)
    {
        var bytes = Encoding.Unicode.GetBytes(s + "\0");
        return ToHexBytes(bytes);
    }

    private string ToHexBytes(byte[] bytes) => string.Join(",", bytes.Select(b => b.ToString("x2")));

    private string ToHexMultiString(string[] strings)
    {
        var combined = string.Join("\0", strings) + "\0\0";
        return ToHexString(combined);
    }

    /// <summary>
    /// Supprime une entrée de registre
    /// </summary>
    private void DeleteRegistryEntry(OrphanedEntry entry)
    {
        // Parser le chemin de registre
        var parts = entry.RegistryPath.Split('\\', 2);
        if (parts.Length < 2)
            throw new ArgumentException($"Chemin de registre invalide: {entry.RegistryPath}");

        var rootStr = parts[0];
        var subPath = parts[1];

        RegistryKey root = rootStr switch
        {
            "HKLM" or "HKLM (x86)" => Registry.LocalMachine,
            "HKCU" => Registry.CurrentUser,
            _ => throw new ArgumentException($"Racine de registre inconnue: {rootStr}")
        };

        // Extraire le chemin parent et le nom de la clé à supprimer
        var lastBackslash = subPath.LastIndexOf('\\');
        if (lastBackslash < 0)
            throw new ArgumentException($"Chemin de sous-clé invalide: {subPath}");

        var parentPath = subPath[..lastBackslash];
        var keyToDelete = subPath[(lastBackslash + 1)..];

        using var parentKey = root.OpenSubKey(parentPath, writable: true);
        if (parentKey == null)
            throw new InvalidOperationException($"Impossible d'ouvrir le chemin parent: {parentPath}");

        parentKey.DeleteSubKeyTree(keyToDelete);
    }
}
