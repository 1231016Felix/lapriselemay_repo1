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
    // Regex pour extraire les GUIDs
    [GeneratedRegex(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}", RegexOptions.Compiled)]
    private static partial Regex GuidRegex();

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
        "Windows Software Development Kit",  // Nom complet du SDK
        "Windows Kit",
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
        "Realtek",
        // Programmes courants avec désinstalleurs dans Package Cache
        "AdGuard",
        "qBittorrent",
        "PotPlayer",
        "Daum PotPlayer",
        "Rustup",
        "Rust",
        "Visual Studio",
        "Python",
        "Node.js",
        "Git for Windows",
        "Git",
        "CMake",
        "7-Zip",
        "WinRAR",
        "VLC",
        "Discord",
        "Steam",
        "Epic Games",
        "OBS Studio",
        "Notepad++",
        "VSCode",
        "Visual Studio Code"
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
        ],
        ["AdGuard"] = [
            @"C:\Program Files\AdGuard",
            @"C:\Program Files (x86)\AdGuard"
        ],
        ["qBittorrent"] = [
            @"C:\Program Files\qBittorrent",
            @"C:\Program Files (x86)\qBittorrent"
        ],
        ["PotPlayer"] = [
            @"C:\Program Files\DAUM\PotPlayer",
            @"C:\Program Files (x86)\DAUM\PotPlayer",
            @"C:\Program Files\PotPlayer",
            @"C:\Program Files (x86)\PotPlayer"
        ],
        ["Rustup"] = [
            @"%USERPROFILE%\.rustup",
            @"%USERPROFILE%\.cargo"
        ],
        ["Windows Software Development Kit"] = [
            @"C:\Program Files (x86)\Windows Kits",
            @"C:\Program Files\Windows Kits"
        ],
        ["Windows Kit"] = [
            @"C:\Program Files (x86)\Windows Kits",
            @"C:\Program Files\Windows Kits"
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

        // Vérification supplémentaire: si la chaîne de désinstallation contient des chemins connus
        // mais qu'on n'a pas pu extraire le chemin, ne pas marquer comme orphelin
        if (!string.IsNullOrWhiteSpace(uninstallString) && uninstallerPath == null)
        {
            // Vérifier si c'est un chemin Package Cache ou MSI
            if (uninstallString.Contains("Package Cache", StringComparison.OrdinalIgnoreCase) ||
                uninstallString.Contains("MsiExec", StringComparison.OrdinalIgnoreCase) ||
                uninstallString.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
            {
                // Essayer de valider en cherchant le programme dans les emplacements standards
                if (ProgramExistsInStandardLocations(nameToCheck, keyName))
                    return null;

                // Pour les MSI, considérer comme probablement valide si le GUID existe
                if (uninstallString.Contains("MsiExec", StringComparison.OrdinalIgnoreCase))
                {
                    // Extraire le GUID du uninstallString
                    var guidMatch = GuidRegex().Match(uninstallString);
                    if (guidMatch.Success)
                    {
                        var guid = guidMatch.Value;
                        if (IsMsiProductInstalled(guid))
                            return null;
                    }
                    // Si c'est un MSI mais qu'on ne peut pas vérifier, ne pas marquer comme orphelin
                    // car les MSI sont gérés par Windows Installer
                    return null;
                }
            }
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
            if (pathsByKey.Any(p => 
            {
                var expandedPath = Environment.ExpandEnvironmentVariables(p);
                return Directory.Exists(expandedPath) && DirectoryHasContent(expandedPath);
            }))
                return true;
        }

        // Vérifier par nom d'affichage (recherche partielle)
        foreach (var (name, paths) in KnownSystemPaths)
        {
            if (displayName?.Contains(name, StringComparison.OrdinalIgnoreCase) == true ||
                keyName.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                if (paths.Any(p => 
                {
                    var expandedPath = Environment.ExpandEnvironmentVariables(p);
                    return Directory.Exists(expandedPath) && DirectoryHasContent(expandedPath);
                }))
                    return true;
            }
        }

        // Vérifier dans les emplacements d'installation standards
        if (ProgramExistsInStandardLocations(displayName, keyName))
            return true;

        return false;
    }

    /// <summary>
    /// Vérifie si un programme existe dans les emplacements d'installation standards
    /// </summary>
    private bool ProgramExistsInStandardLocations(string? displayName, string keyName)
    {
        if (string.IsNullOrWhiteSpace(displayName) && string.IsNullOrWhiteSpace(keyName))
            return false;

        // Nom à rechercher (nettoyer les caractères spéciaux)
        var searchName = !string.IsNullOrWhiteSpace(displayName) ? displayName : keyName;
        
        // Extraire le nom de base (sans version, architecture, etc.)
        var baseName = ExtractProgramBaseName(searchName);
        if (string.IsNullOrWhiteSpace(baseName))
            return false;

        // Emplacements standards à vérifier
        var standardPaths = new[]
        {
            @"C:\Program Files",
            @"C:\Program Files (x86)",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
            @"C:\ProgramData"
        };

        foreach (var basePath in standardPaths)
        {
            if (!Directory.Exists(basePath))
                continue;

            try
            {
                // Chercher un dossier qui correspond au nom du programme
                var matchingDirs = Directory.EnumerateDirectories(basePath)
                    .Where(d => 
                    {
                        var dirName = Path.GetFileName(d);
                        return dirName.Contains(baseName, StringComparison.OrdinalIgnoreCase) ||
                               baseName.Contains(dirName, StringComparison.OrdinalIgnoreCase);
                    })
                    .Take(1);  // On ne cherche que le premier

                foreach (var dir in matchingDirs)
                {
                    if (DirectoryHasContent(dir))
                        return true;
                }
            }
            catch
            {
                // Ignorer les erreurs d'accès
            }
        }

        // Vérifier spécifiquement dans Package Cache (pour les installeurs)
        var packageCachePath = @"C:\ProgramData\Package Cache";
        if (Directory.Exists(packageCachePath))
        {
            try
            {
                // Vérifier si un désinstalleur existe dans Package Cache pour ce programme
                foreach (var cacheDir in Directory.EnumerateDirectories(packageCachePath))
                {
                    try
                    {
                        // Chercher des fichiers d'installation qui correspondent
                        var files = Directory.EnumerateFiles(cacheDir, "*.exe", SearchOption.TopDirectoryOnly)
                            .Concat(Directory.EnumerateFiles(cacheDir, "*.msi", SearchOption.TopDirectoryOnly));

                        foreach (var file in files)
                        {
                            var fileName = Path.GetFileNameWithoutExtension(file);
                            if (fileName.Contains(baseName, StringComparison.OrdinalIgnoreCase) ||
                                baseName.Contains(fileName, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        // Vérifier dans les dossiers utilisateur courants
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var userPaths = new[]
        {
            Path.Combine(userProfile, ".cargo"),      // Rust/Rustup
            Path.Combine(userProfile, ".rustup"),    // Rustup
            Path.Combine(userProfile, "scoop"),      // Scoop
            Path.Combine(userProfile, ".local"),     // Linux-style apps
        };

        foreach (var path in userPaths)
        {
            if (Directory.Exists(path))
            {
                // Vérifier si le nom du dossier correspond au programme
                var folderName = Path.GetFileName(path).TrimStart('.');
                if (searchName.Contains(folderName, StringComparison.OrdinalIgnoreCase) ||
                    searchName.Contains("rust", StringComparison.OrdinalIgnoreCase) && path.Contains("rust", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Extrait le nom de base d'un programme (sans version, architecture, etc.)
    /// </summary>
    private string ExtractProgramBaseName(string programName)
    {
        if (string.IsNullOrWhiteSpace(programName))
            return "";

        var name = programName;

        // Supprimer les patterns courants
        var patternsToRemove = new[]
        {
            @"\s*\(x64\)",
            @"\s*\(x86\)",
            @"\s*\(64[- ]?bit\)",
            @"\s*\(32[- ]?bit\)",
            @"\s*64[- ]?bit",
            @"\s*32[- ]?bit",
            @"\s*-\s*Windows\s*\d+.*$",  // Ex: "- Windows 10.0.22621.5040"
            @"\s+v?\d+\.\d+.*$",         // Versions
            @"\s+\d+\.\d+\.\d+.*$",      // Versions longues
        };

        foreach (var pattern in patternsToRemove)
        {
            name = Regex.Replace(name, pattern, "", RegexOptions.IgnoreCase);
        }

        return name.Trim();
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
    /// Gère les chemins avec espaces (Program Files, Package Cache, etc.)
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

        // Cas 2: MsiExec - géré séparément
        if (cleaned.StartsWith("MsiExec", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Cas 3: rundll32 - extraire la DLL
        if (cleaned.StartsWith("rundll32", StringComparison.OrdinalIgnoreCase))
        {
            // Format: rundll32.exe "path.dll",EntryPoint ou rundll32.exe path.dll,EntryPoint
            var afterRundll = cleaned.IndexOf("rundll32", StringComparison.OrdinalIgnoreCase) + 8;
            if (afterRundll < cleaned.Length)
            {
                var rest = cleaned[afterRundll..].TrimStart('.', 'e', 'x', 'E', 'X', ' ');
                if (rest.StartsWith('"'))
                {
                    var endQ = rest.IndexOf('"', 1);
                    if (endQ > 1) return rest[1..endQ];
                }
                else
                {
                    var commaIdx = rest.IndexOf(',');
                    if (commaIdx > 0) return rest[..commaIdx].Trim();
                }
            }
            return null;
        }

        // Cas 4: Chemin avec extensions connues (.exe, .cmd, .bat, .msi)
        // Chercher la fin de l'exécutable plutôt que le premier espace
        var extensions = new[] { ".exe", ".cmd", ".bat", ".msi", ".com" };
        foreach (var ext in extensions)
        {
            var extIndex = cleaned.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
            if (extIndex > 0)
            {
                // Vérifier si c'est vraiment la fin du chemin (suivi d'espace, de guillemet ou fin de chaîne)
                var endIndex = extIndex + ext.Length;
                if (endIndex >= cleaned.Length || 
                    cleaned[endIndex] == ' ' || 
                    cleaned[endIndex] == '"' ||
                    cleaned[endIndex] == '/')
                {
                    var path = cleaned[..endIndex];
                    // Nettoyer le chemin
                    return path.TrimStart('"').TrimEnd('"');
                }
            }
        }

        // Cas 5: Chemin sans extension explicite - essayer de trouver un chemin valide
        // Tester des préfixes connus avec espaces
        var knownPrefixes = new[]
        {
            @"C:\Program Files (x86)\",
            @"C:\Program Files\",
            @"C:\ProgramData\Package Cache\",
            @"C:\ProgramData\",
            @"C:\Users\"
        };

        foreach (var prefix in knownPrefixes)
        {
            if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                // Trouver la fin du chemin en cherchant une extension
                foreach (var ext in extensions)
                {
                    var idx = cleaned.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
                    if (idx > prefix.Length)
                    {
                        return cleaned[..(idx + ext.Length)];
                    }
                }
            }
        }

        // Cas 6: Fallback - retourner le chemin entier si c'est un chemin simple
        // Ne pas couper au premier espace si le chemin contient des répertoires connus
        if (cleaned.Contains("Program Files", StringComparison.OrdinalIgnoreCase) ||
            cleaned.Contains("Package Cache", StringComparison.OrdinalIgnoreCase) ||
            cleaned.Contains("AppData", StringComparison.OrdinalIgnoreCase))
        {
            // Essayer de trouver la fin du chemin
            foreach (var ext in extensions)
            {
                var idx = cleaned.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
                if (idx > 0)
                {
                    return cleaned[..(idx + ext.Length)];
                }
            }
        }

        // Cas 7: Dernier recours - chemin simple jusqu'au premier espace
        // Mais seulement si le chemin ne contient pas de répertoires connus avec espaces
        var spaceIndex = cleaned.IndexOf(' ');
        if (spaceIndex > 0)
        {
            var beforeSpace = cleaned[..spaceIndex];
            // Vérifier si c'est un chemin tronqué invalide
            if (beforeSpace.EndsWith(@"\Program", StringComparison.OrdinalIgnoreCase) ||
                beforeSpace.EndsWith(@"\ProgramData\Package", StringComparison.OrdinalIgnoreCase))
            {
                // Chemin tronqué, retourner null pour ne pas avoir un faux positif
                return null;
            }
            return beforeSpace;
        }

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
