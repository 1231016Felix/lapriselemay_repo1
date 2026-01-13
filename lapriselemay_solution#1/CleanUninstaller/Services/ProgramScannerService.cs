using CleanUninstaller.Models;
using CleanUninstaller.Helpers;
using CleanUninstaller.Services.Interfaces;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Dispatching;

namespace CleanUninstaller.Services;

/// <summary>
/// Service pour scanner et lister tous les programmes installés
/// </summary>
public class ProgramScannerService : IProgramScannerService
{
    private readonly IRegistryService _registryService;
    private readonly ILoggerService _logger;
    private readonly DispatcherQueue? _dispatcherQueue;

    public ProgramScannerService(IRegistryService registryService, ILoggerService logger)
    {
        _registryService = registryService;
        _logger = logger;
        // Capturer le DispatcherQueue du thread UI pour les opérations BitmapImage
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    // Constructeur sans paramètre pour compatibilité
    public ProgramScannerService() : this(
        ServiceContainer.GetService<IRegistryService>(),
        ServiceContainer.GetService<ILoggerService>())
    { }

    /// <summary>
    /// Scanne tous les programmes installés (Win32 + Windows Store)
    /// </summary>
    public async Task<List<InstalledProgram>> ScanAllProgramsAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var allPrograms = new List<InstalledProgram>();

        // Phase 1: Scan du registre (programmes classiques)
        progress?.Report(new ScanProgress
        {
            Phase = ScanPhase.ScanningRegistry,
            StatusMessage = "Scan des programmes installés...",
            Percentage = 0
        });

        var registryPrograms = await _registryService.GetInstalledProgramsAsync(progress, cancellationToken);
        allPrograms.AddRange(registryPrograms);

        // Phase 2: Scan des applications Windows Store (MSIX/AppX)
        progress?.Report(new ScanProgress
        {
            Phase = ScanPhase.ScanningWindowsApps,
            StatusMessage = "Scan des applications Windows Store...",
            Percentage = 40
        });

        var windowsApps = await GetWindowsStoreAppsAsync(cancellationToken);
        allPrograms.AddRange(windowsApps);

        // Phase 3: Chargement des icônes
        progress?.Report(new ScanProgress
        {
            Phase = ScanPhase.LoadingIcons,
            StatusMessage = "Chargement des icônes...",
            Percentage = 70
        });

        await LoadProgramIconsAsync(allPrograms, progress, cancellationToken);

        // Phase 4: Calcul des tailles réelles (si nécessaire)
        progress?.Report(new ScanProgress
        {
            Phase = ScanPhase.CalculatingSizes,
            StatusMessage = "Calcul des tailles...",
            Percentage = 90
        });

        await CalculateRealSizesAsync(allPrograms, cancellationToken);

        progress?.Report(new ScanProgress
        {
            Phase = ScanPhase.Completed,
            StatusMessage = $"{allPrograms.Count} programmes trouvés",
            Percentage = 100
        });

        return allPrograms.OrderBy(p => p.DisplayName).ToList();
    }

    /// <summary>
    /// Récupère les applications Windows Store installées via PowerShell
    /// </summary>
    private async Task<List<InstalledProgram>> GetWindowsStoreAppsAsync(CancellationToken cancellationToken)
    {
        var apps = new List<InstalledProgram>();

        try
        {
            var output = await GetWindowsAppsViaPowerShellAsync(cancellationToken);
            if (!string.IsNullOrEmpty(output))
            {
                ParseWindowsAppsJson(output, apps, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw; // Propager l'annulation
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur PowerShell apps: {ex.Message}");
        }

        return apps;
    }

    /// <summary>
    /// Récupère les apps via PowerShell avec timeout et support d'annulation
    /// </summary>
    private static async Task<string> GetWindowsAppsViaPowerShellAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -Command \"Get-AppxPackage | Select-Object Name, Publisher, PublisherDisplayName, Version, InstallLocation, IsFramework, IsBundle, NonRemovable | ConvertTo-Json -Compress\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        // Lire la sortie de manière asynchrone
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        // Créer un token avec timeout de 30 secondes
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            return await outputTask;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout atteint - tuer le processus
            try 
            { 
                process.Kill(entireProcessTree: true); 
            } 
            catch { }
            
            Debug.WriteLine("PowerShell timeout - processus terminé");
            return string.Empty;
        }
        catch (OperationCanceledException)
        {
            // Annulation demandée par l'utilisateur
            try 
            { 
                process.Kill(entireProcessTree: true); 
            } 
            catch { }
            throw;
        }
    }
    
    /// <summary>
    /// Parse le JSON des apps Windows et les ajoute à la liste
    /// </summary>
    private void ParseWindowsAppsJson(string json, List<InstalledProgram> apps, CancellationToken cancellationToken)
    {
        try
        {
            // Utiliser System.Text.Json pour un parsing plus robuste
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            // Gérer le cas d'un tableau ou d'un objet unique
            var elements = root.ValueKind == System.Text.Json.JsonValueKind.Array 
                ? root.EnumerateArray() 
                : new[] { root }.AsEnumerable();
            
            foreach (var element in elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var name = GetJsonString(element, "Name");
                if (string.IsNullOrEmpty(name)) continue;
                
                // Ignorer les apps système et frameworks
                if (IsSystemWindowsApp(name)) continue;
                if (GetJsonBool(element, "IsFramework")) continue;
                if (GetJsonBool(element, "IsBundle")) continue;
                
                var installLocation = GetJsonString(element, "InstallLocation") ?? "";
                
                // Essayer d'obtenir le vrai nom depuis le manifeste
                var displayName = TryGetDisplayNameFromManifest(installLocation);
                
                // Sinon, formater le nom technique
                if (string.IsNullOrEmpty(displayName))
                {
                    displayName = FormatWindowsAppName(name);
                }
                
                // Éviter les doublons
                if (apps.Any(a => a.Id == name || a.DisplayName == displayName))
                    continue;
                
                var publisher = GetJsonString(element, "PublisherDisplayName") 
                    ?? ExtractPublisherName(GetJsonString(element, "Publisher") ?? "");
                
                apps.Add(new InstalledProgram
                {
                    Id = name,
                    DisplayName = displayName,
                    Publisher = publisher,
                    Version = GetJsonString(element, "Version") ?? "",
                    InstallLocation = installLocation,
                    IsWindowsApp = true,
                    IsSystemComponent = GetJsonBool(element, "NonRemovable")
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur parsing JSON apps: {ex.Message}");
        }
    }
    
    private static string? GetJsonString(System.Text.Json.JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String 
            ? prop.GetString() 
            : null;
    }
    
    private static bool GetJsonBool(System.Text.Json.JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var prop) && 
               prop.ValueKind == System.Text.Json.JsonValueKind.True;
    }
    
    /// <summary>
    /// Extrait le nom lisible d'un publisher depuis le format CN=...
    /// </summary>
    private static string ExtractPublisherName(string publisher)
    {
        if (string.IsNullOrEmpty(publisher)) return "";
        
        // Format: CN=Microsoft Corporation, O=Microsoft Corporation, ...
        if (publisher.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
        {
            var endIndex = publisher.IndexOf(',');
            if (endIndex > 3)
            {
                return publisher[3..endIndex];
            }
            return publisher[3..];
        }
        
        return publisher;
    }

    /// <summary>
    /// Vérifie si c'est une app système Windows à ignorer
    /// </summary>
    private static bool IsSystemWindowsApp(string name)
    {
        string[] systemPrefixes =
        [
            "Microsoft.Windows",
            "Microsoft.UI",
            "Microsoft.NET",
            "Microsoft.VCLibs",
            "Microsoft.Services",
            "Microsoft.DesktopAppInstaller",
            "windows.",
            "Microsoft.XboxGameCallableUI",
            "Microsoft.AAD",
            "Microsoft.AccountsControl",
            "Microsoft.Advertising",
            "Microsoft.AsyncTextService",
            "Microsoft.BioEnrollment",
            "Microsoft.CredDialogHost",
            "Microsoft.ECApp",
            "Microsoft.LockApp",
            "Microsoft.MicrosoftEdge",
            "Microsoft.Win32WebViewHost",
            "Microsoft.XboxIdentityProvider",
            "MicrosoftWindows.",
            "NcsiUwpApp",
            "1527c705-839a-4832",
            "c5e2524a-ea46-4f67"
        ];

        return systemPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Formate le nom d'une app Windows pour l'affichage
    /// </summary>
    private static string FormatWindowsAppName(string packageName)
    {
        if (string.IsNullOrEmpty(packageName)) return packageName;
        
        var result = packageName;
        
        // Enlever le suffixe de version (après le underscore)
        var underscoreIndex = result.IndexOf('_');
        if (underscoreIndex > 0)
        {
            result = result[..underscoreIndex];
        }
        
        // Séparer par les points
        var parts = result.Split('.');
        
        // Si le premier segment est un éditeur connu, le retirer
        string[] knownPublishers = ["Microsoft", "Adobe", "Google", "Apple", "Mozilla", 
            "Spotify", "Discord", "Slack", "Zoom", "WhatsApp", "Telegram"];
            
        if (parts.Length >= 2 && knownPublishers.Any(p => 
            parts[0].Equals(p, StringComparison.OrdinalIgnoreCase)))
        {
            parts = parts.Skip(1).ToArray();
        }
        
        // Joindre les parties restantes
        result = string.Join(" ", parts);
        
        // Ajouter des espaces avant les majuscules (CamelCase -> Camel Case)
        result = System.Text.RegularExpressions.Regex.Replace(result, "([a-z])([A-Z])", "$1 $2");
        
        // Nettoyer les espaces multiples
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ").Trim();
        
        return string.IsNullOrWhiteSpace(result) ? packageName : result;
    }
    
    /// <summary>
    /// Essaie de lire le DisplayName depuis le manifeste AppX
    /// </summary>
    private static string? TryGetDisplayNameFromManifest(string installLocation)
    {
        if (string.IsNullOrEmpty(installLocation)) return null;
        
        try
        {
            var manifestPath = Path.Combine(installLocation, "AppxManifest.xml");
            if (!File.Exists(manifestPath)) return null;
            
            var xml = System.Xml.Linq.XDocument.Load(manifestPath);
            var ns = xml.Root?.GetDefaultNamespace();
            if (ns == null) return null;
            
            var displayName = xml.Root?
                .Element(ns + "Properties")?
                .Element(ns + "DisplayName")?
                .Value;
            
            // Ignorer les références de ressources
            if (!string.IsNullOrEmpty(displayName) && !displayName.StartsWith("ms-resource:"))
            {
                return displayName;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur lecture manifeste: {ex.Message}");
        }
        
        return null;
    }

    /// <summary>
    /// Charge les icônes des programmes
    /// </summary>
    private async Task LoadProgramIconsAsync(
        List<InstalledProgram> programs,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var processed = 0;
        var total = programs.Count;

        foreach (var program in programs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var icon = await ExtractIconAsync(program);
                if (icon != null)
                {
                    program.Icon = icon;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur chargement icône {program.DisplayName}: {ex.Message}");
            }

            processed++;
            if (processed % 10 == 0) // Mise à jour tous les 10 programmes
            {
                progress?.Report(new ScanProgress
                {
                    Phase = ScanPhase.LoadingIcons,
                    StatusMessage = $"Chargement des icônes ({processed}/{total})...",
                    Percentage = 70 + (processed * 20) / total,
                    ProcessedCount = processed,
                    TotalCount = total
                });
            }
        }
    }

    /// <summary>
    /// Extrait l'icône d'un programme (thread-safe pour UI)
    /// </summary>
    private async Task<BitmapImage?> ExtractIconAsync(InstalledProgram program)
    {
        // Extraire les bytes de l'icône sur un thread pool
        var iconBytes = await Task.Run(() => ExtractIconBytesFromProgram(program));
        
        if (iconBytes == null || iconBytes.Length == 0)
            return null;

        // Créer le BitmapImage sur le UI thread
        return await CreateBitmapImageOnUIThreadAsync(iconBytes);
    }

    /// <summary>
    /// Extrait les bytes de l'icône (peut être appelé sur n'importe quel thread)
    /// </summary>
    private static byte[]? ExtractIconBytesFromProgram(InstalledProgram program)
    {
        try
        {
            // Essayer depuis le chemin d'installation
            if (!string.IsNullOrEmpty(program.InstallLocation))
            {
                var exeFiles = Directory.GetFiles(program.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly);
                foreach (var exe in exeFiles.Take(3)) // Limiter la recherche
                {
                    var bytes = ExtractIconBytesFromFile(exe);
                    if (bytes != null) return bytes;
                }
            }

            // Essayer depuis la commande de désinstallation
            var uninstallPath = ExtractPathFromCommand(program.UninstallString);
            if (!string.IsNullOrEmpty(uninstallPath) && File.Exists(uninstallPath))
            {
                return ExtractIconBytesFromFile(uninstallPath);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extrait les bytes d'une icône depuis un fichier exe (thread-safe)
    /// </summary>
    private static byte[]? ExtractIconBytesFromFile(string filePath)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(filePath);
            if (icon == null) return null;

            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Crée un BitmapImage sur le UI thread de manière sûre
    /// </summary>
    private async Task<BitmapImage?> CreateBitmapImageOnUIThreadAsync(byte[] imageBytes)
    {
        // Si on a un DispatcherQueue et qu'on n'est pas sur le UI thread
        if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
        {
            var tcs = new TaskCompletionSource<BitmapImage?>();
            
            var success = _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    var bitmap = CreateBitmapFromBytes(imageBytes);
                    tcs.SetResult(bitmap);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            if (!success)
            {
                return null;
            }

            return await tcs.Task;
        }

        // Déjà sur le UI thread ou pas de DispatcherQueue
        return CreateBitmapFromBytes(imageBytes);
    }

    /// <summary>
    /// Crée un BitmapImage à partir de bytes (doit être appelé sur UI thread)
    /// </summary>
    private static BitmapImage? CreateBitmapFromBytes(byte[] imageBytes)
    {
        try
        {
            using var stream = new MemoryStream(imageBytes);
            var bitmapImage = new BitmapImage();
            bitmapImage.SetSource(stream.AsRandomAccessStream());
            return bitmapImage;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extrait le chemin d'un exécutable depuis une commande
    /// </summary>
    private static string? ExtractPathFromCommand(string command)
    {
        if (string.IsNullOrEmpty(command)) return null;

        // Gérer les chemins entre guillemets
        if (command.StartsWith('"'))
        {
            var endQuote = command.IndexOf('"', 1);
            if (endQuote > 1)
            {
                return command[1..endQuote];
            }
        }

        // Chemin sans guillemets
        var spaceIndex = command.IndexOf(' ');
        return spaceIndex > 0 ? command[..spaceIndex] : command;
    }

    /// <summary>
    /// Calcule les tailles réelles des programmes sans taille enregistrée (PARALLÉLISÉ)
    /// </summary>
    private async Task CalculateRealSizesAsync(
        List<InstalledProgram> programs,
        CancellationToken cancellationToken)
    {
        var programsWithoutSize = programs.Where(p => p.EstimatedSize == 0).ToList();
        
        if (programsWithoutSize.Count == 0) return;

        await Task.Run(() =>
        {
            // Utiliser Parallel.ForEach pour accélérer le calcul des tailles
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 4), // Limiter à 4 threads I/O
                CancellationToken = cancellationToken
            };

            Parallel.ForEach(programsWithoutSize, options, program =>
            {
                try
                {
                    // Essayer de trouver le dossier d'installation
                    var installDir = FindInstallDirectory(program);
                    
                    if (!string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
                    {
                        var size = CalculateDirectorySize(installDir);
                        if (size > 0)
                        {
                            program.EstimatedSize = size;
                            program.IsSizeApproximate = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erreur calcul taille {program.DisplayName}: {ex.Message}");
                }
            });
        }, cancellationToken);
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
        var dirFromUninstall = ExtractDirectoryFromUninstall(program.UninstallString);
        if (!string.IsNullOrEmpty(dirFromUninstall) && Directory.Exists(dirFromUninstall))
        {
            return dirFromUninstall;
        }

        // 3. Extraire le dossier depuis QuietUninstallString
        var dirFromQuiet = ExtractDirectoryFromUninstall(program.QuietUninstallString);
        if (!string.IsNullOrEmpty(dirFromQuiet) && Directory.Exists(dirFromQuiet))
        {
            return dirFromQuiet;
        }

        // 4. Chercher dans Program Files par nom
        var dirFromSearch = SearchProgramInCommonLocations(program.DisplayName, program.Publisher);
        if (!string.IsNullOrEmpty(dirFromSearch))
        {
            return dirFromSearch;
        }

        return null;
    }

    /// <summary>
    /// Extrait le dossier parent d'un chemin d'exécutable depuis une commande de désinstallation
    /// </summary>
    private static string? ExtractDirectoryFromUninstall(string? command)
    {
        if (string.IsNullOrEmpty(command)) return null;

        try
        {
            var cleanPath = command.Trim();
            
            // Enlever les guillemets
            if (cleanPath.StartsWith('"'))
            {
                var endQuote = cleanPath.IndexOf('"', 1);
                if (endQuote > 0)
                {
                    cleanPath = cleanPath[1..endQuote];
                }
            }

            // Ignorer MsiExec
            if (cleanPath.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Trouver le .exe
            var exeIndex = cleanPath.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIndex > 0)
            {
                cleanPath = cleanPath[..(exeIndex + 4)];
            }

            if (!Path.IsPathRooted(cleanPath)) return null;

            var directory = Path.GetDirectoryName(cleanPath);
            
            // Remonter si dans un sous-dossier de désinstallation
            if (!string.IsNullOrEmpty(directory))
            {
                var dirName = Path.GetFileName(directory)?.ToLowerInvariant();
                if (dirName is "uninst" or "uninstall" or "_uninst" or "bin" or "_iu14d2n.tmp")
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
    /// Recherche un programme dans les emplacements courants
    /// </summary>
    private static string? SearchProgramInCommonLocations(string programName, string publisher)
    {
        if (string.IsNullOrEmpty(programName)) return null;

        var searchPaths = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        };

        // Simplifier le nom pour la recherche
        var searchName = programName
            .Replace(" ", "")
            .Replace("-", "")
            .Replace("_", "")
            .ToLowerInvariant();

        // Mots clés significatifs
        var keywords = programName
            .Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .Take(3)
            .ToArray();

        foreach (var basePath in searchPaths)
        {
            if (string.IsNullOrEmpty(basePath) || !Directory.Exists(basePath)) continue;

            try
            {
                foreach (var dir in Directory.EnumerateDirectories(basePath))
                {
                    var dirName = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(dirName)) continue;

                    var simplifiedDirName = dirName
                        .Replace(" ", "")
                        .Replace("-", "")
                        .Replace("_", "")
                        .ToLowerInvariant();

                    // Correspondance exacte
                    if (simplifiedDirName == searchName)
                    {
                        return dir;
                    }

                    // Correspondance par mots clés
                    if (keywords.Length > 0 && keywords.All(w => 
                        dirName.Contains(w, StringComparison.OrdinalIgnoreCase)))
                    {
                        return dir;
                    }

                    // Correspondance partielle
                    if (simplifiedDirName.Length >= 4 && searchName.Length >= 4)
                    {
                        if (simplifiedDirName.Contains(searchName) || searchName.Contains(simplifiedDirName))
                        {
                            return dir;
                        }
                    }
                }
            }
            catch { /* Accès refusé - ignorer */ }
        }

        return null;
    }

    /// <summary>
    /// Calcule la taille d'un dossier (utilise le helper commun)
    /// </summary>
    public static long CalculateDirectorySize(string path) => CommonHelpers.CalculateDirectorySize(path);
}
