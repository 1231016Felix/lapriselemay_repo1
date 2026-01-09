using System.Security.Cryptography;
using System.Diagnostics;
using Microsoft.Win32;
using CleanUninstaller.Models;

namespace CleanUninstaller.Services;

/// <summary>
/// Service de création et comparaison de snapshots système
/// Capture l'état complet du système pour détecter les changements après installation
/// </summary>
public class SnapshotService
{
    // Dossiers à surveiller pour les fichiers
    private static readonly string[] MonitoredFolders =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Common Files"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Common Files"),
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts"),
    ];

    // Clés de registre à surveiller
    private static readonly (RegistryKey Root, string Path)[] MonitoredRegistryPaths =
    [
        (Registry.LocalMachine, @"SOFTWARE"),
        (Registry.LocalMachine, @"SOFTWARE\WOW6432Node"),
        (Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services"),
        (Registry.CurrentUser, @"SOFTWARE"),
        (Registry.ClassesRoot, @""),
    ];

    /// <summary>
    /// Crée un snapshot complet du système
    /// </summary>
    public async Task<InstallationSnapshot> CreateSnapshotAsync(
        SnapshotType type,
        string? installationName = null,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = new InstallationSnapshot
        {
            Type = type,
            InstallationName = installationName
        };

        var totalSteps = 12;
        var currentStep = 0;

        // 1. Capturer les fichiers (30% du temps)
        progress?.Report(new ScanProgress((currentStep * 100) / totalSteps, "Capture des fichiers..."));
        await CaptureFilesAsync(snapshot, progress, cancellationToken);
        currentStep++;

        // 2. Capturer le registre (20% du temps)
        progress?.Report(new ScanProgress((currentStep * 100) / totalSteps, "Capture du registre..."));
        await CaptureRegistryAsync(snapshot, cancellationToken);
        currentStep++;

        // 3. Capturer les services (5%)
        progress?.Report(new ScanProgress((currentStep * 100) / totalSteps, "Capture des services..."));
        await CaptureServicesAsync(snapshot, cancellationToken);
        currentStep++;

        // 4. Capturer les tâches planifiées (5%)
        progress?.Report(new ScanProgress((currentStep * 100) / totalSteps, "Capture des tâches planifiées..."));
        await CaptureScheduledTasksAsync(snapshot, cancellationToken);
        currentStep++;

        // 5. Capturer les règles de pare-feu (5%)
        progress?.Report(new ScanProgress((currentStep * 100) / totalSteps, "Capture des règles pare-feu..."));
        await CaptureFirewallRulesAsync(snapshot, cancellationToken);
        currentStep++;

        // 6. Capturer les variables d'environnement (5%)
        progress?.Report(new ScanProgress((currentStep * 100) / totalSteps, "Capture des variables d'environnement..."));
        CaptureEnvironmentVariables(snapshot);
        currentStep++;

        // 7. Capturer les entrées de démarrage (5%)
        progress?.Report(new ScanProgress((currentStep * 100) / totalSteps, "Capture des entrées de démarrage..."));
        await CaptureStartupEntriesAsync(snapshot, cancellationToken);
        currentStep++;

        // 8. Capturer les pilotes (5%)
        progress?.Report(new ScanProgress((currentStep * 100) / totalSteps, "Capture des pilotes..."));
        await CaptureDriversAsync(snapshot, cancellationToken);
        currentStep++;

        // 9. Capturer les objets COM (5%)
        progress?.Report(new ScanProgress((currentStep * 100) / totalSteps, "Capture des objets COM..."));
        await CaptureCOMObjectsAsync(snapshot, cancellationToken);
        currentStep++;

        // 10. Capturer les associations de fichiers (5%)
        progress?.Report(new ScanProgress((currentStep * 100) / totalSteps, "Capture des associations de fichiers..."));
        CaptureFileAssociations(snapshot);
        currentStep++;

        // 11. Capturer les polices (5%)
        progress?.Report(new ScanProgress((currentStep * 100) / totalSteps, "Capture des polices..."));
        await CaptureFontsAsync(snapshot, cancellationToken);
        currentStep++;

        // 12. Capturer les shell extensions (5%)
        progress?.Report(new ScanProgress((currentStep * 100) / totalSteps, "Capture des extensions shell..."));
        await CaptureShellExtensionsAsync(snapshot, cancellationToken);
        currentStep++;

        progress?.Report(new ScanProgress(100, $"Snapshot terminé: {snapshot.Statistics.FileCount} fichiers, {snapshot.Statistics.RegistryKeyCount} clés registre"));

        return snapshot;
    }

    /// <summary>
    /// Compare deux snapshots et retourne les différences
    /// </summary>
    public async Task<List<SystemChange>> CompareSnapshotsAsync(
        InstallationSnapshot before,
        InstallationSnapshot after,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var changes = new List<SystemChange>();

        progress?.Report(new ScanProgress(0, "Comparaison des fichiers..."));
        await Task.Run(() =>
        {
            // Comparer les fichiers
            CompareFiles(before.Files, after.Files, changes);
            
            // Comparer le registre
            CompareRegistry(before.RegistryKeys, after.RegistryKeys, changes);
            
            // Comparer les services
            CompareServices(before.Services, after.Services, changes);
            
            // Comparer les tâches planifiées
            CompareScheduledTasks(before.ScheduledTasks, after.ScheduledTasks, changes);
            
            // Comparer les règles de pare-feu
            CompareFirewallRules(before.FirewallRules, after.FirewallRules, changes);
            
            // Comparer les variables d'environnement
            CompareEnvironmentVariables(before.SystemEnvironmentVariables, after.SystemEnvironmentVariables, "Système", changes);
            CompareEnvironmentVariables(before.UserEnvironmentVariables, after.UserEnvironmentVariables, "Utilisateur", changes);
            
            // Comparer les entrées de démarrage
            CompareStartupEntries(before.StartupEntries, after.StartupEntries, changes);

            // Comparer les pilotes
            CompareDrivers(before.Drivers, after.Drivers, changes);

            // Comparer les objets COM
            CompareCOMObjects(before.ComObjects, after.ComObjects, changes);

            // Comparer les associations de fichiers
            CompareFileAssociations(before.FileAssociations, after.FileAssociations, changes);

            // Comparer les polices
            CompareFonts(before.InstalledFonts, after.InstalledFonts, changes);

            // Comparer les shell extensions
            CompareShellExtensions(before.ShellExtensions, after.ShellExtensions, changes);

        }, cancellationToken);

        progress?.Report(new ScanProgress(100, $"{changes.Count} changements détectés"));

        return changes;
    }

    #region File Capture

    private async Task CaptureFilesAsync(
        InstallationSnapshot snapshot,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var totalFolders = MonitoredFolders.Length;
        var processedFolders = 0;

        await Task.Run(() =>
        {
            foreach (var folder in MonitoredFolders)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(folder)) continue;

                try
                {
                    ScanDirectory(folder, snapshot.Files, 0, 3, cancellationToken);
                }
                catch (Exception) { }

                processedFolders++;
                var subProgress = (processedFolders * 40) / totalFolders;
                progress?.Report(new ScanProgress(subProgress, $"Scan de {Path.GetFileName(folder)}..."));
            }
        }, cancellationToken);
    }

    private static void ScanDirectory(
        string path,
        HashSet<FileSnapshot> files,
        int depth,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        if (depth > maxDepth) return;

        try
        {
            // Ajouter les fichiers du dossier actuel
            foreach (var file in Directory.EnumerateFiles(path))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var info = new FileInfo(file);
                    files.Add(new FileSnapshot
                    {
                        Path = file,
                        Size = info.Length,
                        LastModified = info.LastWriteTimeUtc
                    });
                }
                catch { }
            }

            // Scanner les sous-dossiers
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Ignorer certains dossiers système volumineux
                var dirName = Path.GetFileName(dir);
                if (ShouldSkipDirectory(dirName)) continue;

                ScanDirectory(dir, files, depth + 1, maxDepth, cancellationToken);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    private static bool ShouldSkipDirectory(string dirName)
    {
        var skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Windows", "Microsoft.NET", "WindowsApps", "Package Cache",
            "Assembly", "installer", "WinSxS", "$Recycle.Bin", "System Volume Information"
        };
        return skipDirs.Contains(dirName);
    }

    #endregion

    #region Registry Capture

    private async Task CaptureRegistryAsync(
        InstallationSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            foreach (var (root, path) in MonitoredRegistryPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var key = root.OpenSubKey(path);
                    if (key != null)
                    {
                        ScanRegistryKey(key, GetRootName(root) + "\\" + path, snapshot.RegistryKeys, 0, 2, cancellationToken);
                    }
                }
                catch { }
            }
        }, cancellationToken);
    }

    private static void ScanRegistryKey(
        RegistryKey key,
        string fullPath,
        HashSet<RegistrySnapshot> registry,
        int depth,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        if (depth > maxDepth) return;

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Ajouter la clé elle-même
            registry.Add(new RegistrySnapshot
            {
                Path = fullPath,
                IsKey = true
            });

            // Ajouter les valeurs
            foreach (var valueName in key.GetValueNames())
            {
                try
                {
                    var value = key.GetValue(valueName);
                    var kind = key.GetValueKind(valueName);
                    
                    registry.Add(new RegistrySnapshot
                    {
                        Path = fullPath,
                        ValueName = valueName,
                        Value = value,
                        ValueKind = kind,
                        IsKey = false
                    });
                }
                catch { }
            }

            // Scanner les sous-clés
            foreach (var subKeyName in key.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Ignorer certaines clés volumineuses
                if (ShouldSkipRegistryKey(subKeyName)) continue;

                try
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey != null)
                    {
                        ScanRegistryKey(subKey, $"{fullPath}\\{subKeyName}", registry, depth + 1, maxDepth, cancellationToken);
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private static bool ShouldSkipRegistryKey(string keyName)
    {
        var skipKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft", "Classes", "Policies", "Windows", "Wow6432Node"
        };
        return skipKeys.Contains(keyName);
    }

    private static string GetRootName(RegistryKey root)
    {
        if (root == Registry.LocalMachine) return "HKLM";
        if (root == Registry.CurrentUser) return "HKCU";
        if (root == Registry.ClassesRoot) return "HKCR";
        if (root == Registry.Users) return "HKU";
        return "HKEY";
    }

    #endregion

    #region Services Capture

    private async Task CaptureServicesAsync(
        InstallationSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            try
            {
                using var servicesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
                if (servicesKey == null) return;

                foreach (var serviceName in servicesKey.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        using var serviceKey = servicesKey.OpenSubKey(serviceName);
                        if (serviceKey == null) continue;

                        snapshot.Services.Add(new ServiceSnapshot
                        {
                            Name = serviceName,
                            DisplayName = serviceKey.GetValue("DisplayName")?.ToString(),
                            ImagePath = serviceKey.GetValue("ImagePath")?.ToString(),
                            Description = serviceKey.GetValue("Description")?.ToString(),
                            StartType = (int)(serviceKey.GetValue("Start") ?? 0)
                        });
                    }
                    catch { }
                }
            }
            catch { }
        }, cancellationToken);
    }

    #endregion

    #region Scheduled Tasks Capture

    private async Task CaptureScheduledTasksAsync(
        InstallationSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            var taskFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks");
            if (!Directory.Exists(taskFolder)) return;

            try
            {
                foreach (var taskFile in Directory.EnumerateFiles(taskFolder, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var taskName = Path.GetRelativePath(taskFolder, taskFile);
                        snapshot.ScheduledTasks.Add(new ScheduledTaskSnapshot
                        {
                            Path = taskFile,
                            Name = taskName
                        });
                    }
                    catch { }
                }
            }
            catch { }
        }, cancellationToken);
    }

    #endregion

    #region Firewall Rules Capture

    private async Task CaptureFirewallRulesAsync(
        InstallationSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            try
            {
                using var firewallKey = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules");
                
                if (firewallKey == null) return;

                foreach (var valueName in firewallKey.GetValueNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var ruleValue = firewallKey.GetValue(valueName)?.ToString() ?? "";
                        var ruleName = ExtractFirewallRuleName(ruleValue) ?? valueName;

                        snapshot.FirewallRules.Add(new FirewallRuleSnapshot
                        {
                            Name = ruleName,
                            ApplicationPath = ExtractFirewallAppPath(ruleValue)
                        });
                    }
                    catch { }
                }
            }
            catch { }
        }, cancellationToken);
    }

    private static string? ExtractFirewallRuleName(string ruleValue)
    {
        // Format: "v2.31|Action=Allow|Active=TRUE|Dir=In|Protocol=6|Name=@{...}|..."
        var parts = ruleValue.Split('|');
        foreach (var part in parts)
        {
            if (part.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
            {
                return part[5..];
            }
        }
        return null;
    }

    private static string? ExtractFirewallAppPath(string ruleValue)
    {
        var parts = ruleValue.Split('|');
        foreach (var part in parts)
        {
            if (part.StartsWith("App=", StringComparison.OrdinalIgnoreCase))
            {
                return part[4..];
            }
        }
        return null;
    }

    #endregion

    #region Environment Variables Capture

    private static void CaptureEnvironmentVariables(InstallationSnapshot snapshot)
    {
        // Variables système
        var systemVars = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Machine);
        foreach (var key in systemVars.Keys)
        {
            if (key != null)
            {
                snapshot.SystemEnvironmentVariables[key.ToString()!] = systemVars[key]?.ToString() ?? "";
            }
        }

        // Variables utilisateur
        var userVars = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.User);
        foreach (var key in userVars.Keys)
        {
            if (key != null)
            {
                snapshot.UserEnvironmentVariables[key.ToString()!] = userVars[key]?.ToString() ?? "";
            }
        }
    }

    #endregion

    #region Startup Entries Capture

    private async Task CaptureStartupEntriesAsync(
        InstallationSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            // Clés de registre de démarrage
            var startupKeys = new[]
            {
                (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
                (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
            };

            foreach (var (root, path) in startupKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var key = root.OpenSubKey(path);
                    if (key == null) continue;

                    var location = $"{GetRootName(root)}\\{path}";
                    
                    foreach (var valueName in key.GetValueNames())
                    {
                        var value = key.GetValue(valueName)?.ToString();
                        snapshot.StartupEntries.Add(new StartupEntrySnapshot
                        {
                            Name = valueName,
                            Location = location,
                            Command = value
                        });
                    }
                }
                catch { }
            }

            // Dossiers de démarrage
            var startupFolders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
            };

            foreach (var folder in startupFolders)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(folder)) continue;

                try
                {
                    foreach (var file in Directory.EnumerateFiles(folder))
                    {
                        snapshot.StartupEntries.Add(new StartupEntrySnapshot
                        {
                            Name = Path.GetFileName(file),
                            Location = folder,
                            Command = file
                        });
                    }
                }
                catch { }
            }
        }, cancellationToken);
    }

    #endregion

    #region Comparison Methods

    private static void CompareFiles(
        HashSet<FileSnapshot> before,
        HashSet<FileSnapshot> after,
        List<SystemChange> changes)
    {
        // Fichiers créés (présents dans after mais pas dans before)
        foreach (var file in after.Except(before))
        {
            changes.Add(new SystemChange
            {
                ChangeType = ChangeType.Created,
                Category = Directory.Exists(file.Path) ? SystemChangeCategory.Folder : SystemChangeCategory.File,
                Path = file.Path,
                Size = file.Size,
                Description = $"Fichier créé: {Path.GetFileName(file.Path)}"
            });
        }

        // Fichiers supprimés (présents dans before mais pas dans after)
        foreach (var file in before.Except(after))
        {
            changes.Add(new SystemChange
            {
                ChangeType = ChangeType.Deleted,
                Category = SystemChangeCategory.File,
                Path = file.Path,
                Size = file.Size,
                Description = $"Fichier supprimé: {Path.GetFileName(file.Path)}"
            });
        }

        // Fichiers modifiés (même chemin, mais taille ou date différente)
        foreach (var afterFile in after)
        {
            var beforeFile = before.FirstOrDefault(f => f.Path.Equals(afterFile.Path, StringComparison.OrdinalIgnoreCase));
            if (beforeFile != null && (beforeFile.Size != afterFile.Size || beforeFile.LastModified != afterFile.LastModified))
            {
                changes.Add(new SystemChange
                {
                    ChangeType = ChangeType.Modified,
                    Category = SystemChangeCategory.File,
                    Path = afterFile.Path,
                    OldValue = $"{beforeFile.Size} octets",
                    NewValue = $"{afterFile.Size} octets",
                    Size = afterFile.Size - beforeFile.Size,
                    Description = $"Fichier modifié: {Path.GetFileName(afterFile.Path)}"
                });
            }
        }
    }

    private static void CompareRegistry(
        HashSet<RegistrySnapshot> before,
        HashSet<RegistrySnapshot> after,
        List<SystemChange> changes)
    {
        // Clés/valeurs créées
        foreach (var reg in after.Except(before))
        {
            changes.Add(new SystemChange
            {
                ChangeType = ChangeType.Created,
                Category = reg.IsKey ? SystemChangeCategory.RegistryKey : SystemChangeCategory.RegistryValue,
                Path = reg.IsKey ? reg.Path : $"{reg.Path}\\{reg.ValueName}",
                NewValue = reg.Value?.ToString(),
                Description = reg.IsKey 
                    ? $"Clé créée: {Path.GetFileName(reg.Path)}"
                    : $"Valeur créée: {reg.ValueName}"
            });
        }

        // Clés/valeurs supprimées
        foreach (var reg in before.Except(after))
        {
            changes.Add(new SystemChange
            {
                ChangeType = ChangeType.Deleted,
                Category = reg.IsKey ? SystemChangeCategory.RegistryKey : SystemChangeCategory.RegistryValue,
                Path = reg.IsKey ? reg.Path : $"{reg.Path}\\{reg.ValueName}",
                OldValue = reg.Value?.ToString(),
                Description = reg.IsKey 
                    ? $"Clé supprimée: {Path.GetFileName(reg.Path)}"
                    : $"Valeur supprimée: {reg.ValueName}"
            });
        }
    }

    private static void CompareServices(
        HashSet<ServiceSnapshot> before,
        HashSet<ServiceSnapshot> after,
        List<SystemChange> changes)
    {
        foreach (var svc in after.Except(before))
        {
            changes.Add(new SystemChange
            {
                ChangeType = ChangeType.Created,
                Category = SystemChangeCategory.Service,
                Path = svc.Name,
                NewValue = svc.ImagePath,
                Description = $"Service créé: {svc.DisplayName ?? svc.Name}"
            });
        }

        foreach (var svc in before.Except(after))
        {
            changes.Add(new SystemChange
            {
                ChangeType = ChangeType.Deleted,
                Category = SystemChangeCategory.Service,
                Path = svc.Name,
                Description = $"Service supprimé: {svc.DisplayName ?? svc.Name}"
            });
        }
    }

    private static void CompareScheduledTasks(
        HashSet<ScheduledTaskSnapshot> before,
        HashSet<ScheduledTaskSnapshot> after,
        List<SystemChange> changes)
    {
        foreach (var task in after.Except(before))
        {
            changes.Add(new SystemChange
            {
                ChangeType = ChangeType.Created,
                Category = SystemChangeCategory.ScheduledTask,
                Path = task.Path,
                Description = $"Tâche créée: {task.Name}"
            });
        }

        foreach (var task in before.Except(after))
        {
            changes.Add(new SystemChange
            {
                ChangeType = ChangeType.Deleted,
                Category = SystemChangeCategory.ScheduledTask,
                Path = task.Path,
                Description = $"Tâche supprimée: {task.Name}"
            });
        }
    }

    private static void CompareFirewallRules(
        HashSet<FirewallRuleSnapshot> before,
        HashSet<FirewallRuleSnapshot> after,
        List<SystemChange> changes)
    {
        foreach (var rule in after.Except(before))
        {
            changes.Add(new SystemChange
            {
                ChangeType = ChangeType.Created,
                Category = SystemChangeCategory.FirewallRule,
                Path = rule.Name,
                NewValue = rule.ApplicationPath,
                Description = $"Règle pare-feu créée: {rule.Name}"
            });
        }

        foreach (var rule in before.Except(after))
        {
            changes.Add(new SystemChange
            {
                ChangeType = ChangeType.Deleted,
                Category = SystemChangeCategory.FirewallRule,
                Path = rule.Name,
                Description = $"Règle pare-feu supprimée: {rule.Name}"
            });
        }
    }

    private static void CompareEnvironmentVariables(
        Dictionary<string, string> before,
        Dictionary<string, string> after,
        string scope,
        List<SystemChange> changes)
    {
        // Variables ajoutées
        foreach (var (key, value) in after)
        {
            if (!before.ContainsKey(key))
            {
                changes.Add(new SystemChange
                {
                    ChangeType = ChangeType.Created,
                    Category = SystemChangeCategory.EnvironmentVariable,
                    Path = $"{scope}:{key}",
                    NewValue = value,
                    Description = $"Variable d'environnement créée ({scope}): {key}"
                });
            }
            else if (before[key] != value)
            {
                changes.Add(new SystemChange
                {
                    ChangeType = ChangeType.Modified,
                    Category = SystemChangeCategory.EnvironmentVariable,
                    Path = $"{scope}:{key}",
                    OldValue = before[key],
                    NewValue = value,
                    Description = $"Variable d'environnement modifiée ({scope}): {key}"
                });
            }
        }

        // Variables supprimées
        foreach (var (key, value) in before)
        {
            if (!after.ContainsKey(key))
            {
                changes.Add(new SystemChange
                {
                    ChangeType = ChangeType.Deleted,
                    Category = SystemChangeCategory.EnvironmentVariable,
                    Path = $"{scope}:{key}",
                    OldValue = value,
                    Description = $"Variable d'environnement supprimée ({scope}): {key}"
                });
            }
        }
    }

    private static void CompareStartupEntries(
        HashSet<StartupEntrySnapshot> before,
        HashSet<StartupEntrySnapshot> after,
        List<SystemChange> changes)
    {
        foreach (var entry in after.Except(before))
        {
            changes.Add(new SystemChange
            {
                ChangeType = ChangeType.Created,
                Category = SystemChangeCategory.StartupEntry,
                Path = $"{entry.Location}\\{entry.Name}",
                NewValue = entry.Command,
                Description = $"Entrée de démarrage créée: {entry.Name}"
            });
        }

        foreach (var entry in before.Except(after))
        {
            changes.Add(new SystemChange
            {
                ChangeType = ChangeType.Deleted,
                Category = SystemChangeCategory.StartupEntry,
                Path = $"{entry.Location}\\{entry.Name}",
                OldValue = entry.Command,
                Description = $"Entrée de démarrage supprimée: {entry.Name}"
            });
        }
    }

    #endregion

    #region Drivers Capture

    private async Task CaptureDriversAsync(
        InstallationSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
                if (key == null) return;

                foreach (var serviceName in key.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        using var serviceKey = key.OpenSubKey(serviceName);
                        var type = (int)(serviceKey?.GetValue("Type") ?? 0);

                        // Types de pilotes: 1=Kernel, 2=FileSystem, 8=Recognizer
                        if (type is 1 or 2 or 8)
                        {
                            var imagePath = serviceKey?.GetValue("ImagePath")?.ToString();
                            var description = serviceKey?.GetValue("Description")?.ToString();

                            snapshot.Drivers.Add(new DriverSnapshot
                            {
                                Name = serviceName,
                                ImagePath = imagePath,
                                Description = description,
                                Type = type
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }, cancellationToken);
    }

    #endregion

    #region COM Objects Capture

    private async Task CaptureCOMObjectsAsync(
        InstallationSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            try
            {
                // Limiter la capture aux CLSID les plus importants
                using var key = Registry.ClassesRoot.OpenSubKey("CLSID");
                if (key == null) return;

                var count = 0;
                const int maxCom = 5000; // Limite pour éviter de ralentir

                foreach (var clsid in key.GetSubKeyNames())
                {
                    if (count++ > maxCom) break;
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        using var clsidKey = key.OpenSubKey(clsid);
                        var name = clsidKey?.GetValue("")?.ToString();

                        string? serverPath = null;
                        using var inprocKey = clsidKey?.OpenSubKey("InprocServer32");
                        serverPath = inprocKey?.GetValue("")?.ToString();

                        if (serverPath == null)
                        {
                            using var localKey = clsidKey?.OpenSubKey("LocalServer32");
                            serverPath = localKey?.GetValue("")?.ToString();
                        }

                        // N'ajouter que si un chemin de serveur existe
                        if (!string.IsNullOrEmpty(serverPath) &&
                            !serverPath.Contains("system32", StringComparison.OrdinalIgnoreCase))
                        {
                            snapshot.ComObjects.Add(new ComObjectSnapshot
                            {
                                CLSID = clsid,
                                Name = name,
                                ServerPath = serverPath
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }, cancellationToken);
    }

    #endregion

    #region File Associations Capture

    private static void CaptureFileAssociations(InstallationSnapshot snapshot)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FileExts");

            if (key == null) return;

            foreach (var ext in key.GetSubKeyNames())
            {
                try
                {
                    using var extKey = key.OpenSubKey(ext);
                    using var choiceKey = extKey?.OpenSubKey("UserChoice");

                    var progId = choiceKey?.GetValue("ProgId")?.ToString();
                    if (!string.IsNullOrEmpty(progId))
                    {
                        snapshot.FileAssociations[ext] = new FileAssociationSnapshot
                        {
                            Extension = ext,
                            ProgId = progId
                        };
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    #endregion

    #region Fonts Capture

    private async Task CaptureFontsAsync(
        InstallationSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            try
            {
                // Polices du registre
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
                if (key != null)
                {
                    foreach (var fontName in key.GetValueNames())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var fontFile = key.GetValue(fontName)?.ToString();
                        if (!string.IsNullOrEmpty(fontFile))
                        {
                            // Construire le chemin complet si nécessaire
                            if (!Path.IsPathRooted(fontFile))
                            {
                                fontFile = Path.Combine(
                                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                    "Fonts",
                                    fontFile);
                            }

                            snapshot.InstalledFonts.Add(new FontSnapshot
                            {
                                Name = fontName,
                                FilePath = fontFile
                            });
                        }
                    }
                }

                // Polices utilisateur (Windows 10+)
                using var userKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
                if (userKey != null)
                {
                    foreach (var fontName in userKey.GetValueNames())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var fontFile = userKey.GetValue(fontName)?.ToString();
                        if (!string.IsNullOrEmpty(fontFile))
                        {
                            snapshot.InstalledFonts.Add(new FontSnapshot
                            {
                                Name = fontName,
                                FilePath = fontFile
                            });
                        }
                    }
                }
            }
            catch { }
        }, cancellationToken);
    }

    #endregion

    #region Shell Extensions Capture

    private async Task CaptureShellExtensionsAsync(
        InstallationSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            var extensionPaths = new[]
            {
                (@"*\shellex\ContextMenuHandlers", "ContextMenu"),
                (@"Directory\shellex\ContextMenuHandlers", "DirContextMenu"),
                (@"Folder\shellex\ContextMenuHandlers", "FolderContextMenu"),
                (@"*\shellex\PropertySheetHandlers", "PropertySheet"),
                (@"Directory\Background\shellex\ContextMenuHandlers", "BackgroundContextMenu"),
            };

            foreach (var (path, type) in extensionPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var key = Registry.ClassesRoot.OpenSubKey(path);
                    if (key == null) continue;

                    foreach (var extName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var extKey = key.OpenSubKey(extName);
                            var clsid = extKey?.GetValue("")?.ToString();

                            // Obtenir la description depuis CLSID si possible
                            string? description = null;
                            if (!string.IsNullOrEmpty(clsid))
                            {
                                using var clsidKey = Registry.ClassesRoot.OpenSubKey($"CLSID\\{clsid}");
                                description = clsidKey?.GetValue("")?.ToString();
                            }

                            snapshot.ShellExtensions.Add(new ShellExtensionSnapshot
                            {
                                Name = extName,
                                Type = type,
                                CLSID = clsid,
                                Description = description ?? extName
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }, cancellationToken);
    }

    #endregion

    #region New Comparison Methods

    private static void CompareDrivers(
        HashSet<DriverSnapshot> before,
        HashSet<DriverSnapshot> after,
        List<SystemChange> changes)
    {
        foreach (var driver in after.Except(before))
        {
            changes.Add(new SystemChange
            {
                ChangeType = ChangeType.Created,
                Category = SystemChangeCategory.Driver,
                Path = driver.Name,
                NewValue = driver.ImagePath,
                Description = $"Pilote installé: {driver.Description ?? driver.Name}"
            });
        }

        foreach (var driver in before.Except(after))
        {
            changes.Add(new SystemChange
            {
                ChangeType = ChangeType.Deleted,
                Category = SystemChangeCategory.Driver,
                Path = driver.Name,
                OldValue = driver.ImagePath,
                Description = $"Pilote supprimé: {driver.Description ?? driver.Name}"
            });
        }
    }

    private static void CompareCOMObjects(
        HashSet<ComObjectSnapshot> before,
        HashSet<ComObjectSnapshot> after,
        List<SystemChange> changes)
    {
        foreach (var com in after.Except(before))
        {
            changes.Add(new SystemChange
            {
                ChangeType = ChangeType.Created,
                Category = SystemChangeCategory.ComObject,
                Path = $"CLSID\\{com.CLSID}",
                NewValue = com.ServerPath,
                Description = $"Objet COM créé: {com.Name ?? com.CLSID}"
            });
        }

        foreach (var com in before.Except(after))
        {
            changes.Add(new SystemChange
            {
                ChangeType = ChangeType.Deleted,
                Category = SystemChangeCategory.ComObject,
                Path = $"CLSID\\{com.CLSID}",
                OldValue = com.ServerPath,
                Description = $"Objet COM supprimé: {com.Name ?? com.CLSID}"
            });
        }
    }

    private static void CompareFileAssociations(
        Dictionary<string, FileAssociationSnapshot> before,
        Dictionary<string, FileAssociationSnapshot> after,
        List<SystemChange> changes)
    {
        foreach (var (ext, assoc) in after)
        {
            if (!before.ContainsKey(ext))
            {
                changes.Add(new SystemChange
                {
                    ChangeType = ChangeType.Created,
                    Category = SystemChangeCategory.FileAssociation,
                    Path = ext,
                    NewValue = assoc.ProgId,
                    Description = $"Association créée: {ext} → {assoc.ProgId}"
                });
            }
            else if (before[ext].ProgId != assoc.ProgId)
            {
                changes.Add(new SystemChange
                {
                    ChangeType = ChangeType.Modified,
                    Category = SystemChangeCategory.FileAssociation,
                    Path = ext,
                    OldValue = before[ext].ProgId,
                    NewValue = assoc.ProgId,
                    Description = $"Association modifiée: {ext}"
                });
            }
        }
    }

    private static void CompareFonts(
        HashSet<FontSnapshot> before,
        HashSet<FontSnapshot> after,
        List<SystemChange> changes)
    {
        foreach (var font in after.Except(before))
        {
            changes.Add(new SystemChange
            {
                ChangeType = ChangeType.Created,
                Category = SystemChangeCategory.Font,
                Path = font.FilePath,
                NewValue = font.Name,
                Description = $"Police installée: {font.Name}"
            });
        }

        foreach (var font in before.Except(after))
        {
            changes.Add(new SystemChange
            {
                ChangeType = ChangeType.Deleted,
                Category = SystemChangeCategory.Font,
                Path = font.FilePath,
                OldValue = font.Name,
                Description = $"Police supprimée: {font.Name}"
            });
        }
    }

    private static void CompareShellExtensions(
        HashSet<ShellExtensionSnapshot> before,
        HashSet<ShellExtensionSnapshot> after,
        List<SystemChange> changes)
    {
        foreach (var ext in after.Except(before))
        {
            changes.Add(new SystemChange
            {
                ChangeType = ChangeType.Created,
                Category = SystemChangeCategory.ShellExtension,
                Path = ext.Name,
                NewValue = ext.CLSID,
                Description = $"Extension shell créée ({ext.Type}): {ext.Description ?? ext.Name}"
            });
        }

        foreach (var ext in before.Except(after))
        {
            changes.Add(new SystemChange
            {
                ChangeType = ChangeType.Deleted,
                Category = SystemChangeCategory.ShellExtension,
                Path = ext.Name,
                OldValue = ext.CLSID,
                Description = $"Extension shell supprimée ({ext.Type}): {ext.Description ?? ext.Name}"
            });
        }
    }

    #endregion
}
