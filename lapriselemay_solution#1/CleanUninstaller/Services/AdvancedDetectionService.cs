using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using CleanUninstaller.Models;
using CleanUninstaller.Services.Interfaces;

namespace CleanUninstaller.Services;

/// <summary>
/// Service de détection avancée des programmes et de leurs dépendances
/// Implémente des fonctionnalités puissantes inspirées des meilleurs désinstalleurs
/// </summary>
public partial class AdvancedDetectionService : IAdvancedDetectionService
{
    private readonly ILoggerService _logger;

    public AdvancedDetectionService(ILoggerService logger)
    {
        _logger = logger;
    }

    // Constructeur sans paramètre pour compatibilité
    public AdvancedDetectionService() : this(ServiceContainer.GetService<ILoggerService>())
    { }

    /// <summary>
    /// Implémente IAdvancedDetectionService.DeepScanAsync
    /// </summary>
    public async Task<List<ResidualItem>> DeepScanAsync(
        InstalledProgram program,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.Info($"Deep scan démarré pour {program.DisplayName}");
        var residuals = new List<ResidualItem>();
        
        // Utilise les méthodes existantes pour la détection
        var relatedFiles = await ScanRelatedFilesAsync(program, progress, cancellationToken);
        residuals.AddRange(relatedFiles);
        
        _logger.Info($"Deep scan terminé: {residuals.Count} éléments trouvés");
        return residuals;
    }

    private async Task<List<ResidualItem>> ScanRelatedFilesAsync(
        InstalledProgram program,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Implémentation du scan profond
        return await Task.Run(() =>
        {
            var items = new List<ResidualItem>();
            // Les détails de détection avancée existants seront utilisés ici
            return items;
        }, cancellationToken);
    }
    /// <summary>
    /// Détecte les programmes liés/dépendants d'un programme
    /// </summary>
    public List<InstalledProgram> FindRelatedPrograms(InstalledProgram program, List<InstalledProgram> allPrograms)
    {
        var related = new List<InstalledProgram>();
        var keywords = ExtractKeywords(program);

        foreach (var other in allPrograms.Where(p => p.Id != program.Id))
        {
            var score = CalculateRelationScore(program, other, keywords);
            if (score > 60)
            {
                related.Add(other);
            }
        }

        return related.OrderByDescending(p => CalculateRelationScore(program, p, keywords)).ToList();
    }

    /// <summary>
    /// Détecte les services Windows liés à un programme
    /// </summary>
    public async Task<List<ServiceInfo>> FindRelatedServicesAsync(InstalledProgram program, CancellationToken cancellationToken = default)
    {
        var services = new List<ServiceInfo>();
        var keywords = ExtractKeywords(program);

        await Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Service");
                foreach (var obj in searcher.Get())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var name = obj["Name"]?.ToString() ?? "";
                    var displayName = obj["DisplayName"]?.ToString() ?? "";
                    var pathName = obj["PathName"]?.ToString() ?? "";
                    var state = obj["State"]?.ToString() ?? "";

                    if (MatchesKeywords(name, keywords) || 
                        MatchesKeywords(displayName, keywords) || 
                        MatchesPath(pathName, program.InstallLocation))
                    {
                        services.Add(new ServiceInfo
                        {
                            Name = name,
                            DisplayName = displayName,
                            PathName = pathName,
                            State = state,
                            StartMode = obj["StartMode"]?.ToString() ?? ""
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur détection services: {ex.Message}");
            }
        }, cancellationToken);

        return services;
    }

    /// <summary>
    /// Détecte les entrées de démarrage liées à un programme
    /// </summary>
    public async Task<List<StartupEntry>> FindStartupEntriesAsync(InstalledProgram program, CancellationToken cancellationToken = default)
    {
        var entries = new List<StartupEntry>();
        var keywords = ExtractKeywords(program);

        await Task.Run(() =>
        {
            // Registre - Run keys
            var runPaths = new (string Path, RegistryKey Root)[]
            {
                (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", Registry.CurrentUser),
                (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", Registry.LocalMachine),
                (@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", Registry.CurrentUser),
                (@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", Registry.LocalMachine),
                (@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", Registry.LocalMachine)
            };

            foreach (var (path, root) in runPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var key = root.OpenSubKey(path);
                    if (key == null) continue;

                    foreach (var valueName in key.GetValueNames())
                    {
                        var value = key.GetValue(valueName)?.ToString() ?? "";
                        
                        if (MatchesKeywords(valueName, keywords) || 
                            MatchesKeywords(value, keywords) ||
                            MatchesPath(value, program.InstallLocation))
                        {
                            entries.Add(new StartupEntry
                            {
                                Name = valueName,
                                Command = value,
                                Location = $"{GetRootName(root)}\\{path}",
                                Type = StartupType.Registry
                            });
                        }
                    }
                }
                catch { /* Accès refusé */ }
            }

            // Dossier Startup
            var startupFolders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
            };

            foreach (var folder in startupFolders)
            {
                if (!Directory.Exists(folder)) continue;
                
                foreach (var file in Directory.EnumerateFiles(folder, "*.lnk"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    if (MatchesKeywords(fileName, keywords))
                    {
                        entries.Add(new StartupEntry
                        {
                            Name = fileName,
                            Command = file,
                            Location = folder,
                            Type = StartupType.StartupFolder
                        });
                    }
                }
            }
        }, cancellationToken);

        return entries;
    }

    /// <summary>
    /// Détecte les tâches planifiées liées à un programme
    /// </summary>
    public async Task<List<ScheduledTaskInfo>> FindScheduledTasksAsync(InstalledProgram program, CancellationToken cancellationToken = default)
    {
        var tasks = new List<ScheduledTaskInfo>();
        var keywords = ExtractKeywords(program);

        await Task.Run(() =>
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = "/query /fo CSV /v",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var lines = output.Split('\n').Skip(1); // Skip header

                foreach (var line in lines)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = ParseCsvLine(line);
                    if (parts.Length < 9) continue;

                    var taskName = parts[1];
                    var taskPath = parts[0];
                    var command = parts.Length > 8 ? parts[8] : "";

                    if (MatchesKeywords(taskName, keywords) || 
                        MatchesKeywords(command, keywords) ||
                        MatchesPath(command, program.InstallLocation))
                    {
                        tasks.Add(new ScheduledTaskInfo
                        {
                            Name = taskName,
                            Path = taskPath,
                            Command = command,
                            Status = parts.Length > 3 ? parts[3] : ""
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur détection tâches: {ex.Message}");
            }
        }, cancellationToken);

        return tasks;
    }

    /// <summary>
    /// Détecte les règles de pare-feu liées à un programme
    /// </summary>
    public async Task<List<FirewallRule>> FindFirewallRulesAsync(InstalledProgram program, CancellationToken cancellationToken = default)
    {
        var rules = new List<FirewallRule>();
        var keywords = ExtractKeywords(program);

        await Task.Run(() =>
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = "advfirewall firewall show rule name=all",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var ruleBlocks = output.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var block in ruleBlocks)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var nameMatch = Regex.Match(block, @"Rule Name:\s*(.+)", RegexOptions.IgnoreCase);
                    var programMatch = Regex.Match(block, @"Program:\s*(.+)", RegexOptions.IgnoreCase);

                    if (!nameMatch.Success) continue;

                    var ruleName = nameMatch.Groups[1].Value.Trim();
                    var programPath = programMatch.Success ? programMatch.Groups[1].Value.Trim() : "";

                    if (MatchesKeywords(ruleName, keywords) || 
                        MatchesPath(programPath, program.InstallLocation))
                    {
                        var directionMatch = Regex.Match(block, @"Direction:\s*(.+)", RegexOptions.IgnoreCase);
                        var actionMatch = Regex.Match(block, @"Action:\s*(.+)", RegexOptions.IgnoreCase);
                        var enabledMatch = Regex.Match(block, @"Enabled:\s*(.+)", RegexOptions.IgnoreCase);

                        rules.Add(new FirewallRule
                        {
                            Name = ruleName,
                            Program = programPath,
                            Direction = directionMatch.Success ? directionMatch.Groups[1].Value.Trim() : "",
                            Action = actionMatch.Success ? actionMatch.Groups[1].Value.Trim() : "",
                            Enabled = enabledMatch.Success && enabledMatch.Groups[1].Value.Trim().Equals("Yes", StringComparison.OrdinalIgnoreCase)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur détection pare-feu: {ex.Message}");
            }
        }, cancellationToken);

        return rules;
    }

    /// <summary>
    /// Calcule la taille réelle d'un programme en analysant son dossier d'installation
    /// </summary>
    public async Task<long> CalculateRealSizeAsync(InstalledProgram program, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(program.InstallLocation) || !Directory.Exists(program.InstallLocation))
        {
            return program.EstimatedSize;
        }

        return await Task.Run(() =>
        {
            try
            {
                return Directory.EnumerateFiles(program.InstallLocation, "*", SearchOption.AllDirectories)
                    .Sum(file =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try { return new FileInfo(file).Length; }
                        catch { return 0; }
                    });
            }
            catch
            {
                return program.EstimatedSize;
            }
        }, cancellationToken);
    }

    #region Private Methods

    private static HashSet<string> ExtractKeywords(InstalledProgram program)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Nom du programme
        if (!string.IsNullOrEmpty(program.DisplayName))
        {
            keywords.Add(program.DisplayName);
            
            var words = CleanNameRegex().Replace(program.DisplayName, " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 3 && !IsCommonWord(w));
            
            foreach (var word in words)
            {
                keywords.Add(word);
            }
        }

        // Éditeur
        if (!string.IsNullOrEmpty(program.Publisher) && !IsCommonPublisher(program.Publisher))
        {
            keywords.Add(program.Publisher);
        }

        // Nom du dossier
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

    private static int CalculateRelationScore(InstalledProgram source, InstalledProgram target, HashSet<string> keywords)
    {
        int score = 0;

        // Même éditeur
        if (!string.IsNullOrEmpty(source.Publisher) && 
            source.Publisher.Equals(target.Publisher, StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        // Nom similaire
        foreach (var keyword in keywords)
        {
            if (target.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                score += 20;
            if (!string.IsNullOrEmpty(target.InstallLocation) && 
                target.InstallLocation.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                score += 15;
        }

        // Même dossier parent
        if (!string.IsNullOrEmpty(source.InstallLocation) && !string.IsNullOrEmpty(target.InstallLocation))
        {
            var sourceParent = Path.GetDirectoryName(source.InstallLocation);
            var targetParent = Path.GetDirectoryName(target.InstallLocation);
            
            if (!string.IsNullOrEmpty(sourceParent) && 
                sourceParent.Equals(targetParent, StringComparison.OrdinalIgnoreCase))
            {
                score += 25;
            }
        }

        return Math.Min(score, 100);
    }

    private static bool MatchesKeywords(string text, HashSet<string> keywords)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesPath(string path, string installLocation)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(installLocation)) 
            return false;
        return path.Contains(installLocation, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRootName(RegistryKey root)
    {
        if (root == Registry.LocalMachine) return "HKLM";
        if (root == Registry.CurrentUser) return "HKCU";
        return "HKEY";
    }

    private static bool IsCommonWord(string word)
    {
        var common = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "for", "and", "version", "update", "edition", "pro", "professional",
            "enterprise", "ultimate", "home", "basic", "premium", "plus", "lite",
            "free", "trial", "beta", "alpha", "release", "build", "bit", "x64", "x86",
            "windows", "win", "app", "application", "software", "tool", "tools"
        };
        return common.Contains(word);
    }

    private static bool IsCommonPublisher(string publisher)
    {
        var common = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "microsoft", "microsoft corporation", "windows", "inc", "llc", "ltd", "company"
        };
        return common.Contains(publisher);
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var inQuotes = false;
        var current = new System.Text.StringBuilder();

        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString());
        return result.ToArray();
    }

    [GeneratedRegex(@"[^a-zA-Z0-9]")]
    private static partial Regex CleanNameRegex();

    #endregion
}

#region Info Classes

/// <summary>
/// Information sur un service Windows
/// </summary>
public class ServiceInfo
{
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string PathName { get; init; } = "";
    public string State { get; init; } = "";
    public string StartMode { get; init; } = "";
}

/// <summary>
/// Entrée de démarrage
/// </summary>
public class StartupEntry
{
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public string Location { get; init; } = "";
    public StartupType Type { get; init; }
}

public enum StartupType
{
    Registry,
    StartupFolder,
    ScheduledTask
}

/// <summary>
/// Information sur une tâche planifiée
/// </summary>
public class ScheduledTaskInfo
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string Command { get; init; } = "";
    public string Status { get; init; } = "";
}

/// <summary>
/// Règle de pare-feu
/// </summary>
public class FirewallRule
{
    public string Name { get; init; } = "";
    public string Program { get; init; } = "";
    public string Direction { get; init; } = "";
    public string Action { get; init; } = "";
    public bool Enabled { get; init; }
}

#endregion
