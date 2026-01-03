using CleanUninstaller.Models;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CleanUninstaller.Services;

/// <summary>
/// Service spécialisé pour les applications Windows Store (AppX/MSIX)
/// </summary>
public class WindowsAppService
{
    /// <summary>
    /// Récupère toutes les applications Windows Store installées
    /// </summary>
    public async Task<List<InstalledProgram>> GetWindowsAppsAsync(CancellationToken cancellationToken = default)
    {
        var apps = new List<InstalledProgram>();

        try
        {
            // Script amélioré qui résout les noms de ressources via Get-StartApps et le manifest
            var script = @"
                $startApps = @{}
                try {
                    Get-StartApps | ForEach-Object { 
                        $appId = $_.AppID
                        if ($appId -match '^([^!]+)!') {
                            $pkgName = $matches[1]
                            if (-not $startApps.ContainsKey($pkgName)) {
                                $startApps[$pkgName] = $_.Name
                            }
                        }
                    }
                } catch {}

                Get-AppxPackage | Where-Object { $_.IsFramework -eq $false -and $_.SignatureKind -ne 'System' } | ForEach-Object {
                    $pkg = $_
                    $displayName = $null
                    $publisherName = $null
                    
                    # Essayer Get-StartApps d'abord (noms localisés)
                    $pkgFamilyName = $pkg.PackageFamilyName
                    if ($startApps.ContainsKey($pkgFamilyName)) {
                        $displayName = $startApps[$pkgFamilyName]
                    }
                    
                    # Essayer le manifest
                    if (-not $displayName -or $displayName -match '^ms-resource:') {
                        try {
                            $manifest = Get-AppxPackageManifest $pkg
                            $rawName = $manifest.Package.Properties.DisplayName
                            $rawPub = $manifest.Package.Properties.PublisherDisplayName
                            
                            if ($rawName -and $rawName -notmatch '^ms-resource:') {
                                $displayName = $rawName
                            }
                            if ($rawPub -and $rawPub -notmatch '^ms-resource:') {
                                $publisherName = $rawPub
                            }
                            
                            # Essayer de résoudre ms-resource via pri
                            if ((-not $displayName -or $displayName -match '^ms-resource:') -and $pkg.InstallLocation) {
                                $priPath = Join-Path $pkg.InstallLocation 'resources.pri'
                                if (Test-Path $priPath) {
                                    # Essayer d'obtenir le nom depuis AppxManifest.xml directement
                                    $manifestPath = Join-Path $pkg.InstallLocation 'AppxManifest.xml'
                                    if (Test-Path $manifestPath) {
                                        [xml]$xmlManifest = Get-Content $manifestPath -Encoding UTF8
                                        $appEntries = $xmlManifest.Package.Applications.Application
                                        if ($appEntries) {
                                            $firstApp = if ($appEntries -is [array]) { $appEntries[0] } else { $appEntries }
                                            $vs = $firstApp.VisualElements
                                            if ($vs -and $vs.DisplayName -and $vs.DisplayName -notmatch '^ms-resource:') {
                                                $displayName = $vs.DisplayName
                                            }
                                        }
                                    }
                                }
                            }
                        } catch {}
                    }
                    
                    # Fallback: formater le nom du package
                    if (-not $displayName -or $displayName -match '^ms-resource:') {
                        $displayName = $pkg.Name -replace '^[^.]+\.', '' -creplace '([a-z])([A-Z])', '$1 $2'
                    }
                    
                    if (-not $publisherName -or $publisherName -match '^ms-resource:') {
                        if ($pkg.Publisher -match 'CN=([^,]+)') {
                            $publisherName = $matches[1] -replace '\s*\(.*\)$', ''
                        } else {
                            $publisherName = 'Inconnu'
                        }
                    }
                    
                    [PSCustomObject]@{
                        Name = $pkg.Name
                        PackageFullName = $pkg.PackageFullName
                        Publisher = $pkg.Publisher
                        Version = $pkg.Version.ToString()
                        InstallLocation = $pkg.InstallLocation
                        DisplayName = $displayName
                        PublisherDisplayName = $publisherName
                    }
                } | ConvertTo-Json -Depth 3
            ";

            var result = await ExecutePowerShellAsync(script, cancellationToken);
            
            if (string.IsNullOrWhiteSpace(result))
                return apps;

            // Parser le JSON
            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;

            // Peut être un array ou un seul objet
            var elements = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray()
                : new[] { root }.AsEnumerable();

            foreach (var element in elements)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var name = GetJsonString(element, "Name");
                if (string.IsNullOrEmpty(name) || IsSystemApp(name))
                    continue;

                var displayName = GetJsonString(element, "DisplayName");
                if (string.IsNullOrEmpty(displayName) || displayName.StartsWith("ms-resource:"))
                    displayName = FormatAppName(name);

                var publisher = GetJsonString(element, "PublisherDisplayName");
                if (string.IsNullOrEmpty(publisher) || publisher.StartsWith("CN=") || publisher.StartsWith("ms-resource:"))
                    publisher = ExtractPublisherName(GetJsonString(element, "Publisher"));

                apps.Add(new InstalledProgram
                {
                    Id = GetJsonString(element, "PackageFullName"),
                    DisplayName = displayName,
                    Publisher = publisher,
                    Version = GetJsonString(element, "Version"),
                    InstallLocation = GetJsonString(element, "InstallLocation"),
                    IsWindowsApp = true,
                    IsSystemComponent = false
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur récupération apps Windows: {ex.Message}");
        }

        return apps;
    }

    /// <summary>
    /// Désinstalle une application Windows Store
    /// </summary>
    public async Task<bool> UninstallWindowsAppAsync(InstalledProgram app, CancellationToken cancellationToken = default)
    {
        if (!app.IsWindowsApp || string.IsNullOrEmpty(app.Id))
            return false;

        try
        {
            var script = $"Remove-AppxPackage -Package '{app.Id}' -ErrorAction Stop";
            await ExecutePowerShellAsync(script, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur désinstallation {app.DisplayName}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Trouve les résidus spécifiques aux apps Windows Store
    /// </summary>
    public List<ResidualItem> FindWindowsAppResiduals(InstalledProgram app)
    {
        var residuals = new List<ResidualItem>();
        
        if (!app.IsWindowsApp) return residuals;

        // Extraire le nom du package (sans version/architecture)
        var packageName = ExtractPackageName(app.Id);
        if (string.IsNullOrEmpty(packageName)) return residuals;

        // Emplacements de données des apps Windows
        var appDataPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps")
        };

        foreach (var basePath in appDataPaths.Where(Directory.Exists))
        {
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(basePath))
                {
                    var dirName = Path.GetFileName(dir);
                    if (dirName.Contains(packageName, StringComparison.OrdinalIgnoreCase))
                    {
                        residuals.Add(new ResidualItem
                        {
                            Path = dir,
                            Type = ResidualType.Folder,
                            Size = CalculateDirectorySize(dir),
                            ProgramName = app.DisplayName,
                            Confidence = ConfidenceLevel.VeryHigh,
                            Reason = "Dossier de données de l'application Windows"
                        });
                    }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur scan {basePath}: {ex.Message}");
            }
        }

        return residuals;
    }

    /// <summary>
    /// Vérifie si c'est une application système à ignorer
    /// </summary>
    private static bool IsSystemApp(string packageName)
    {
        var systemPrefixes = new[]
        {
            "Microsoft.Windows",
            "Microsoft.UI",
            "Microsoft.NET",
            "Microsoft.VCLibs",
            "Microsoft.Services",
            "Microsoft.DesktopAppInstaller",
            "Microsoft.XboxGameCallable",
            "Microsoft.AAD",
            "Microsoft.AccountsControl",
            "Microsoft.AsyncTextService",
            "Microsoft.BioEnrollment",
            "Microsoft.CredDialogHost",
            "Microsoft.ECApp",
            "Microsoft.LockApp",
            "Microsoft.Win32WebViewHost",
            "Microsoft.XboxIdentityProvider",
            "MicrosoftWindows.",
            "windows.",
            "NcsiUwpApp",
            "Microsoft.MicrosoftEdge",
            "Microsoft.WindowsStore",
            "Microsoft.StorePurchaseApp",
            "Microsoft.WebMediaExtensions",
            "Microsoft.HEIFImageExtension",
            "Microsoft.HEVCVideoExtension",
            "Microsoft.VP9VideoExtensions",
            "Microsoft.WebpImageExtension",
            "Microsoft.LanguageExperiencePack",
            "Microsoft.DirectX"
        };

        return systemPrefixes.Any(p => 
            packageName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Formate le nom d'un package pour l'affichage
    /// </summary>
    private static string FormatAppName(string packageName)
    {
        // Retirer le préfixe éditeur (ex: "Microsoft.WindowsCalculator" -> "Windows Calculator")
        var parts = packageName.Split('.');
        if (parts.Length >= 2)
        {
            var nameParts = parts.Skip(1).ToList();
            var name = string.Join("", nameParts);
            
            // Ajouter des espaces entre les mots CamelCase
            name = Regex.Replace(name, @"([a-z])([A-Z])", "$1 $2");
            return name;
        }
        return packageName;
    }

    /// <summary>
    /// Extrait le nom de l'éditeur depuis le format CN=...
    /// </summary>
    private static string ExtractPublisherName(string publisher)
    {
        if (string.IsNullOrEmpty(publisher))
            return "Inconnu";

        // Format: "CN=Microsoft Corporation, O=Microsoft Corporation, ..."
        var match = Regex.Match(publisher, @"CN=([^,]+)");
        if (match.Success)
        {
            var name = match.Groups[1].Value;
            // Nettoyer les suffixes de certificat
            name = Regex.Replace(name, @"\s*\(.*\)$", "");
            return name;
        }

        return publisher;
    }

    /// <summary>
    /// Extrait le nom du package sans version ni architecture
    /// </summary>
    private static string? ExtractPackageName(string packageFullName)
    {
        if (string.IsNullOrEmpty(packageFullName))
            return null;

        // Format: "Microsoft.WindowsCalculator_11.2210.0.0_x64__8wekyb3d8bbwe"
        var underscoreIndex = packageFullName.IndexOf('_');
        if (underscoreIndex > 0)
            return packageFullName[..underscoreIndex];

        return packageFullName;
    }

    /// <summary>
    /// Exécute un script PowerShell et retourne la sortie
    /// </summary>
    private static async Task<string> ExecutePowerShellAsync(string script, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NoLogo -ExecutionPolicy Bypass -Command -",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
            return string.Empty;

        // Écrire le script sur stdin et fermer
        await process.StandardInput.WriteAsync(script);
        process.StandardInput.Close();

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return output;
    }

    private static string GetJsonString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static long CalculateDirectorySize(string path)
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
        catch { return 0; }
    }
}
