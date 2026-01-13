using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using CleanUninstaller.Models;

namespace CleanUninstaller.Services;

/// <summary>
/// Service de détection des programmes installés via des gestionnaires de paquets tiers
/// (Chocolatey, Scoop, WinGet)
/// </summary>
public partial class PackageManagerService
{
    private static readonly string ChocolateyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "chocolatey");
    
    private static readonly string ScoopGlobalPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "scoop");
    
    private static readonly string ScoopUserPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "scoop");

    /// <summary>
    /// Détecte si Chocolatey est installé
    /// </summary>
    public static bool IsChocolateyInstalled() => 
        Directory.Exists(ChocolateyPath) && 
        File.Exists(Path.Combine(ChocolateyPath, "choco.exe"));

    /// <summary>
    /// Détecte si Scoop est installé
    /// </summary>
    public static bool IsScoopInstalled() => 
        Directory.Exists(ScoopUserPath) || Directory.Exists(ScoopGlobalPath);

    /// <summary>
    /// Détecte si WinGet est disponible
    /// </summary>
    public static bool IsWinGetAvailable()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "--version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            
            using var process = Process.Start(startInfo);
            return process != null && process.WaitForExit(5000);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Liste tous les paquets Chocolatey installés
    /// </summary>
    public async Task<List<PackageInfo>> GetChocolateyPackagesAsync(CancellationToken cancellationToken = default)
    {
        var packages = new List<PackageInfo>();
        
        if (!IsChocolateyInstalled()) return packages;

        var libPath = Path.Combine(ChocolateyPath, "lib");
        if (!Directory.Exists(libPath)) return packages;

        await Task.Run(() =>
        {
            foreach (var packageDir in Directory.EnumerateDirectories(libPath))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var packageName = Path.GetFileName(packageDir);
                var nuspecFile = Directory.GetFiles(packageDir, "*.nuspec").FirstOrDefault();
                
                var info = new PackageInfo
                {
                    Name = packageName,
                    Source = PackageSource.Chocolatey,
                    InstallPath = packageDir,
                    InstalledAt = Directory.GetCreationTime(packageDir)
                };

                // Parser le nuspec pour plus d'infos
                if (nuspecFile != null && File.Exists(nuspecFile))
                {
                    try
                    {
                        var content = File.ReadAllText(nuspecFile);
                        info.Version = ExtractXmlValue(content, "version");
                        info.DisplayName = ExtractXmlValue(content, "title") ?? packageName;
                        info.Publisher = ExtractXmlValue(content, "authors");
                        info.Description = ExtractXmlValue(content, "description");
                    }
                    catch { }
                }

                // Calculer la taille
                info.Size = CalculateDirectorySize(packageDir);

                packages.Add(info);
            }
        }, cancellationToken);

        return packages;
    }

    /// <summary>
    /// Liste tous les paquets Scoop installés
    /// </summary>
    public async Task<List<PackageInfo>> GetScoopPackagesAsync(CancellationToken cancellationToken = default)
    {
        var packages = new List<PackageInfo>();
        
        if (!IsScoopInstalled()) return packages;

        var scoopPaths = new[] { ScoopUserPath, ScoopGlobalPath }
            .Where(Directory.Exists)
            .ToList();

        await Task.Run(() =>
        {
            foreach (var scoopPath in scoopPaths)
            {
                var appsPath = Path.Combine(scoopPath, "apps");
                if (!Directory.Exists(appsPath)) continue;

                var isGlobal = scoopPath == ScoopGlobalPath;

                foreach (var appDir in Directory.EnumerateDirectories(appsPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var appName = Path.GetFileName(appDir);
                    if (appName == "scoop") continue; // Ignorer scoop lui-même

                    // Trouver le dossier "current" qui pointe vers la version active
                    var currentPath = Path.Combine(appDir, "current");
                    var manifestPath = Path.Combine(currentPath, "manifest.json");

                    var info = new PackageInfo
                    {
                        Name = appName,
                        Source = PackageSource.Scoop,
                        InstallPath = appDir,
                        IsGlobal = isGlobal,
                        InstalledAt = Directory.GetCreationTime(appDir)
                    };

                    // Trouver la version (nom du dossier symlinké)
                    if (Directory.Exists(currentPath))
                    {
                        try
                        {
                            var target = new DirectoryInfo(currentPath);
                            // Le dossier current est souvent un junction vers la version
                            if (target.Attributes.HasFlag(FileAttributes.ReparsePoint))
                            {
                                // Lire le manifest pour la version
                            }
                        }
                        catch { }
                    }

                    // Parser le manifest JSON
                    if (File.Exists(manifestPath))
                    {
                        try
                        {
                            var json = File.ReadAllText(manifestPath);
                            using var doc = JsonDocument.Parse(json);
                            var root = doc.RootElement;

                            info.Version = GetJsonString(root, "version");
                            info.Description = GetJsonString(root, "description");
                            info.DisplayName = GetJsonString(root, "##") ?? appName; // ## est parfois utilisé pour le nom
                        }
                        catch { }
                    }

                    // Calculer la taille totale (toutes les versions)
                    info.Size = CalculateDirectorySize(appDir);

                    packages.Add(info);
                }
            }
        }, cancellationToken);

        return packages;
    }

    /// <summary>
    /// Liste tous les paquets WinGet installés (via winget list)
    /// </summary>
    public async Task<List<PackageInfo>> GetWinGetPackagesAsync(CancellationToken cancellationToken = default)
    {
        var packages = new List<PackageInfo>();
        
        if (!IsWinGetAvailable()) return packages;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "list --disable-interactivity",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            using var process = Process.Start(startInfo);
            if (process == null) return packages;

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            // Parser la sortie de winget list (format tabulaire)
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .SkipWhile(l => !l.Contains("---")) // Sauter l'en-tête
                .Skip(1) // Sauter la ligne de tirets
                .ToList();

            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Le format est: Name  Id  Version  Source
                var parts = SplitWinGetLine(line);
                if (parts.Count >= 3)
                {
                    var info = new PackageInfo
                    {
                        DisplayName = parts[0].Trim(),
                        Name = parts[1].Trim(),
                        Version = parts[2].Trim(),
                        Source = PackageSource.WinGet
                    };

                    if (parts.Count >= 4)
                    {
                        info.WinGetSource = parts[3].Trim();
                    }

                    packages.Add(info);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur WinGet list: {ex.Message}");
        }

        return packages;
    }

    /// <summary>
    /// Désinstalle un paquet Chocolatey
    /// </summary>
    public async Task<bool> UninstallChocolateyPackageAsync(string packageName, bool force = false)
    {
        if (!IsChocolateyInstalled()) return false;

        var args = $"uninstall {packageName} -y" + (force ? " --force" : "");
        return await RunPackageCommandAsync(Path.Combine(ChocolateyPath, "choco.exe"), args);
    }

    /// <summary>
    /// Désinstalle un paquet Scoop
    /// </summary>
    public async Task<bool> UninstallScoopPackageAsync(string packageName, bool global = false)
    {
        if (!IsScoopInstalled()) return false;

        var args = $"uninstall {packageName}" + (global ? " --global" : "");
        return await RunPackageCommandAsync("scoop", args);
    }

    /// <summary>
    /// Désinstalle un paquet WinGet
    /// </summary>
    public async Task<bool> UninstallWinGetPackageAsync(string packageId)
    {
        if (!IsWinGetAvailable()) return false;

        var args = $"uninstall --id {packageId} --silent --disable-interactivity";
        return await RunPackageCommandAsync("winget", args);
    }

    /// <summary>
    /// Détermine si un programme est géré par un gestionnaire de paquets
    /// </summary>
    public PackageInfo? FindPackageForProgram(InstalledProgram program, IEnumerable<PackageInfo> allPackages)
    {
        if (string.IsNullOrEmpty(program.DisplayName)) return null;

        var normalizedName = NormalizeName(program.DisplayName);

        // Correspondance exacte ou partielle
        return allPackages.FirstOrDefault(p =>
            NormalizeName(p.Name) == normalizedName ||
            NormalizeName(p.DisplayName ?? "") == normalizedName ||
            (p.InstallPath != null && 
             !string.IsNullOrEmpty(program.InstallLocation) && 
             program.InstallLocation.Contains(p.InstallPath, StringComparison.OrdinalIgnoreCase)));
    }

    #region Helpers

    private static string NormalizeName(string name) =>
        NormalizeRegex().Replace(name.ToLowerInvariant(), "");

    [GeneratedRegex(@"[^a-z0-9]")]
    private static partial Regex NormalizeRegex();

    private static string? ExtractXmlValue(string xml, string tagName)
    {
        var pattern = $@"<{tagName}[^>]*>(.*?)</{tagName}>";
        var match = Regex.Match(xml, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? GetJsonString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var prop) && 
               prop.ValueKind == JsonValueKind.String 
            ? prop.GetString() 
            : null;
    }

    private static List<string> SplitWinGetLine(string line)
    {
        // WinGet utilise des espaces fixes, on doit parser intelligemment
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        int spaceCount = 0;

        foreach (var c in line)
        {
            if (c == ' ')
            {
                spaceCount++;
                if (spaceCount >= 2 && current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    spaceCount = 0;
                }
            }
            else
            {
                if (spaceCount > 0 && spaceCount < 2)
                {
                    current.Append(' ');
                }
                current.Append(c);
                spaceCount = 0;
            }
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    private static long CalculateDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;

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

    private static async Task<bool> RunPackageCommandAsync(string command, string args)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
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
        catch
        {
            return false;
        }
    }

    #endregion
}

/// <summary>
/// Information sur un paquet installé via un gestionnaire de paquets
/// </summary>
public class PackageInfo
{
    public required string Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Version { get; set; }
    public string? Publisher { get; set; }
    public string? Description { get; set; }
    public string? InstallPath { get; set; }
    public PackageSource Source { get; set; }
    public bool IsGlobal { get; set; }
    public string? WinGetSource { get; set; }
    public long Size { get; set; }
    public DateTime InstalledAt { get; set; }
}

/// <summary>
/// Source du paquet
/// </summary>
public enum PackageSource
{
    Unknown,
    Chocolatey,
    Scoop,
    WinGet
}
