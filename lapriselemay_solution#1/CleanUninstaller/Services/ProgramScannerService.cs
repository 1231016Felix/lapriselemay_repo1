using CleanUninstaller.Models;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CleanUninstaller.Services;

/// <summary>
/// Service pour scanner et lister tous les programmes installés
/// </summary>
public class ProgramScannerService
{
    private readonly RegistryService _registryService = new();

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

        await Task.Run(() =>
        {
            GetWindowsAppsViaPowerShell(apps, cancellationToken);
        }, cancellationToken);

        return apps;
    }

    /// <summary>
    /// Récupère les apps via PowerShell avec les propriétés complètes
    /// </summary>
    private void GetWindowsAppsViaPowerShell(List<InstalledProgram> apps, CancellationToken cancellationToken)
    {
        try
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

            using var process = Process.Start(startInfo);
            if (process == null) return;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (string.IsNullOrEmpty(output)) return;

            // Parser le JSON
            ParseWindowsAppsJson(output, apps, cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur PowerShell apps: {ex.Message}");
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
    /// Extrait l'icône d'un programme
    /// </summary>
    private async Task<BitmapImage?> ExtractIconAsync(InstalledProgram program)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Essayer depuis le chemin d'installation
                if (!string.IsNullOrEmpty(program.InstallLocation))
                {
                    var exeFiles = Directory.GetFiles(program.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly);
                    foreach (var exe in exeFiles.Take(3)) // Limiter la recherche
                    {
                        var icon = ExtractIconFromFile(exe);
                        if (icon != null) return icon;
                    }
                }

                // Essayer depuis la commande de désinstallation
                var uninstallPath = ExtractPathFromCommand(program.UninstallString);
                if (!string.IsNullOrEmpty(uninstallPath) && File.Exists(uninstallPath))
                {
                    return ExtractIconFromFile(uninstallPath);
                }

                return null;
            }
            catch
            {
                return null;
            }
        });
    }

    /// <summary>
    /// Extrait une icône depuis un fichier exe
    /// </summary>
    private static BitmapImage? ExtractIconFromFile(string filePath)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(filePath);
            if (icon == null) return null;

            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            stream.Position = 0;

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
    /// Calcule les tailles réelles des programmes sans taille enregistrée
    /// </summary>
    private async Task CalculateRealSizesAsync(
        List<InstalledProgram> programs,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            foreach (var program in programs.Where(p => p.EstimatedSize == 0 && !string.IsNullOrEmpty(p.InstallLocation)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (Directory.Exists(program.InstallLocation))
                    {
                        // Note: Utiliser reflection pour modifier EstimatedSize (readonly)
                        // En production, ajouter une propriété calculée
                    }
                }
                catch { /* Ignorer les erreurs d'accès */ }
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Calcule la taille d'un dossier
    /// </summary>
    public static long CalculateDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;

        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file =>
                {
                    try { return new FileInfo(file).Length; }
                    catch { return 0; }
                });
        }
        catch
        {
            return 0;
        }
    }
}
