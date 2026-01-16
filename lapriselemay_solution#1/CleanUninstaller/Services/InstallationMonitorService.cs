using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using CleanUninstaller.Models;
using CleanUninstaller.Services.Interfaces;
using Shared.Logging;

namespace CleanUninstaller.Services;

/// <summary>
/// Service principal de monitoring d'installation
/// Combine la surveillance en temps réel (FileSystemWatcher) et les snapshots
/// pour une détection complète des changements
/// </summary>
public class InstallationMonitorService : IInstallationMonitorService, IDisposable
{
    private readonly SnapshotService _snapshotService;
    private readonly Shared.Logging.ILoggerService _logger;
    private readonly List<FileSystemWatcher> _fileWatchers = [];
    private readonly ConcurrentDictionary<string, SystemChange> _realTimeChanges = new();
    private readonly string _dataFolder;

    private MonitoredInstallation? _currentMonitoring;
    private InstallationSnapshot? _beforeSnapshot;
    private CancellationTokenSource? _monitoringCts;
    private bool _isDisposed;

    // Dossiers à surveiller en temps réel
    private static readonly string[] WatchedFolders =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
    ];

    /// <summary>
    /// Événement déclenché lors d'un changement en temps réel
    /// </summary>
    public event EventHandler<SystemChange>? RealTimeChangeDetected;

    /// <summary>
    /// Événement déclenché lors d'un changement de statut
    /// </summary>
    public event EventHandler<MonitoringStatus>? StatusChanged;

    /// <summary>
    /// Monitoring en cours
    /// </summary>
    public MonitoredInstallation? CurrentMonitoring => _currentMonitoring;

    /// <summary>
    /// Indique si un monitoring est actif
    /// </summary>
    public bool IsMonitoring => _currentMonitoring?.Status == MonitoringStatus.Monitoring;

    #region IInstallationMonitorService Implementation

    /// <summary>
    /// Démarre le monitoring (implémentation de l'interface)
    /// </summary>
    void IInstallationMonitorService.StartMonitoring()
    {
        // Démarre le monitoring de façon synchrone
        _ = StartMonitoringAsync();
    }

    /// <summary>
    /// Arrête le monitoring (implémentation de l'interface)
    /// </summary>
    void IInstallationMonitorService.StopMonitoring()
    {
        // Arrête le monitoring de façon synchrone
        _ = StopMonitoringAsync();
    }

    /// <summary>
    /// Prend un snapshot du système (implémentation de l'interface)
    /// </summary>
    async Task<InstallationSnapshot> IInstallationMonitorService.TakeSnapshotAsync(CancellationToken cancellationToken)
    {
        return await _snapshotService.CreateSnapshotAsync(
            SnapshotType.Manual,
            "Manual Snapshot",
            null,
            cancellationToken);
    }

    /// <summary>
    /// Compare deux snapshots (implémentation de l'interface)
    /// </summary>
    async Task<List<SystemChange>> IInstallationMonitorService.CompareSnapshotsAsync(
        InstallationSnapshot before,
        InstallationSnapshot after,
        CancellationToken cancellationToken)
    {
        return await _snapshotService.CompareSnapshotsAsync(before, after, null, cancellationToken);
    }

    #endregion

    /// <summary>
    /// Nombre de changements détectés en temps réel
    /// </summary>
    public int RealTimeChangeCount => _realTimeChanges.Count;

    public InstallationMonitorService(Shared.Logging.ILoggerService logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _snapshotService = new SnapshotService();
        _dataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanUninstaller",
            "MonitoredInstallations");

        Directory.CreateDirectory(_dataFolder);
    }

    /// <summary>
    /// Démarre le monitoring d'une nouvelle installation
    /// </summary>
    public async Task<MonitoredInstallation> StartMonitoringAsync(
        string? installationName = null,
        string? installerPath = null,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Arrêter tout monitoring existant
        await StopMonitoringAsync();

        _monitoringCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _realTimeChanges.Clear();

        _currentMonitoring = new MonitoredInstallation
        {
            Name = installationName ?? "Installation en cours",
            InstallerPath = installerPath,
            Status = MonitoringStatus.TakingSnapshot
        };

        StatusChanged?.Invoke(this, MonitoringStatus.TakingSnapshot);

        try
        {
            // 1. Créer le snapshot "avant"
            progress?.Report(new ScanProgress(0, "Capture de l'état initial du système..."));
            _beforeSnapshot = await _snapshotService.CreateSnapshotAsync(
                SnapshotType.Before,
                installationName,
                progress,
                _monitoringCts.Token);

            _currentMonitoring.BeforeSnapshotId = _beforeSnapshot.Id;

            // 2. Démarrer la surveillance en temps réel
            progress?.Report(new ScanProgress(95, "Démarrage de la surveillance..."));
            StartFileWatchers();

            _currentMonitoring.Status = MonitoringStatus.Monitoring;
            StatusChanged?.Invoke(this, MonitoringStatus.Monitoring);

            progress?.Report(new ScanProgress(100, "Surveillance active - Lancez l'installation"));

            return _currentMonitoring;
        }
        catch (Exception ex)
        {
            _currentMonitoring.Status = MonitoringStatus.Error;
            _currentMonitoring.Description = ex.Message;
            StatusChanged?.Invoke(this, MonitoringStatus.Error);
            throw;
        }
    }

    /// <summary>
    /// Arrête le monitoring et analyse les changements
    /// </summary>
    public async Task<MonitoredInstallation?> StopMonitoringAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_currentMonitoring == null) return null;

        try
        {
            // 1. Arrêter la surveillance en temps réel
            StopFileWatchers();

            _currentMonitoring.Status = MonitoringStatus.Analyzing;
            StatusChanged?.Invoke(this, MonitoringStatus.Analyzing);

            // 2. Créer le snapshot "après"
            progress?.Report(new ScanProgress(0, "Capture de l'état final du système..."));
            var afterSnapshot = await _snapshotService.CreateSnapshotAsync(
                SnapshotType.After,
                _currentMonitoring.Name,
                progress,
                cancellationToken);

            _currentMonitoring.AfterSnapshotId = afterSnapshot.Id;

            // 3. Comparer les snapshots
            if (_beforeSnapshot != null)
            {
                progress?.Report(new ScanProgress(80, "Analyse des changements..."));
                var snapshotChanges = await _snapshotService.CompareSnapshotsAsync(
                    _beforeSnapshot,
                    afterSnapshot,
                    progress,
                    cancellationToken);

                // 4. Fusionner avec les changements temps réel
                var allChanges = MergeChanges(snapshotChanges, _realTimeChanges.Values);
                _currentMonitoring.Changes.Clear();
                _currentMonitoring.Changes.AddRange(allChanges);
            }

            _currentMonitoring.EndTime = DateTime.Now;
            _currentMonitoring.Status = MonitoringStatus.Completed;
            StatusChanged?.Invoke(this, MonitoringStatus.Completed);

            // 5. Sauvegarder
            await SaveMonitoredInstallationAsync(_currentMonitoring);

            progress?.Report(new ScanProgress(100, $"Analyse terminée: {_currentMonitoring.Statistics.TotalChanges} changements détectés"));

            var result = _currentMonitoring;
            _currentMonitoring = null;
            _beforeSnapshot = null;

            return result;
        }
        catch (Exception ex)
        {
            if (_currentMonitoring != null)
            {
                _currentMonitoring.Status = MonitoringStatus.Error;
                _currentMonitoring.Description = ex.Message;
            }
            StatusChanged?.Invoke(this, MonitoringStatus.Error);
            throw;
        }
        finally
        {
            _monitoringCts?.Cancel();
            _monitoringCts?.Dispose();
            _monitoringCts = null;
        }
    }

    /// <summary>
    /// Annule le monitoring en cours
    /// </summary>
    public void CancelMonitoring()
    {
        StopFileWatchers();
        _monitoringCts?.Cancel();

        if (_currentMonitoring != null)
        {
            _currentMonitoring.Status = MonitoringStatus.Cancelled;
            _currentMonitoring.EndTime = DateTime.Now;
            StatusChanged?.Invoke(this, MonitoringStatus.Cancelled);
        }

        _currentMonitoring = null;
        _beforeSnapshot = null;
        _realTimeChanges.Clear();
    }

    /// <summary>
    /// Met en pause le monitoring
    /// </summary>
    public void PauseMonitoring()
    {
        if (_currentMonitoring?.Status != MonitoringStatus.Monitoring) return;

        foreach (var watcher in _fileWatchers)
        {
            watcher.EnableRaisingEvents = false;
        }

        _currentMonitoring.Status = MonitoringStatus.Paused;
        StatusChanged?.Invoke(this, MonitoringStatus.Paused);
    }

    /// <summary>
    /// Reprend le monitoring
    /// </summary>
    public void ResumeMonitoring()
    {
        if (_currentMonitoring?.Status != MonitoringStatus.Paused) return;

        foreach (var watcher in _fileWatchers)
        {
            watcher.EnableRaisingEvents = true;
        }

        _currentMonitoring.Status = MonitoringStatus.Monitoring;
        StatusChanged?.Invoke(this, MonitoringStatus.Monitoring);
    }

    #region File System Watchers

    private void StartFileWatchers()
    {
        foreach (var folder in WatchedFolders)
        {
            if (!Directory.Exists(folder)) continue;

            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    NotifyFilter = NotifyFilters.FileName
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.Size
                                 | NotifyFilters.CreationTime,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };

                watcher.Created += OnFileCreated;
                watcher.Changed += OnFileChanged;
                watcher.Deleted += OnFileDeleted;
                watcher.Renamed += OnFileRenamed;
                watcher.Error += OnWatcherError;

                _fileWatchers.Add(watcher);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur création watcher pour {folder}: {ex.Message}");
            }
        }
    }

    private void StopFileWatchers()
    {
        foreach (var watcher in _fileWatchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Created -= OnFileCreated;
                watcher.Changed -= OnFileChanged;
                watcher.Deleted -= OnFileDeleted;
                watcher.Renamed -= OnFileRenamed;
                watcher.Error -= OnWatcherError;
                watcher.Dispose();
            }
            catch { }
        }
        _fileWatchers.Clear();
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        if (ShouldIgnorePath(e.FullPath)) return;

        var isDirectory = Directory.Exists(e.FullPath);
        long size = 0;

        if (!isDirectory)
        {
            try
            {
                size = new FileInfo(e.FullPath).Length;
            }
            catch { }
        }

        var change = new SystemChange
        {
            ChangeType = ChangeType.Created,
            Category = isDirectory ? SystemChangeCategory.Folder : SystemChangeCategory.File,
            Path = e.FullPath,
            Size = size,
            ProcessName = GetCurrentInstallerProcess(),
            Description = isDirectory 
                ? $"Dossier créé: {Path.GetFileName(e.FullPath)}"
                : $"Fichier créé: {Path.GetFileName(e.FullPath)}"
        };

        _realTimeChanges[e.FullPath] = change;
        RealTimeChangeDetected?.Invoke(this, change);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (ShouldIgnorePath(e.FullPath)) return;

        // Éviter les doublons avec Created
        if (_realTimeChanges.TryGetValue(e.FullPath, out var existing) &&
            existing.ChangeType == ChangeType.Created)
        {
            return;
        }

        var change = new SystemChange
        {
            ChangeType = ChangeType.Modified,
            Category = SystemChangeCategory.File,
            Path = e.FullPath,
            ProcessName = GetCurrentInstallerProcess(),
            Description = $"Fichier modifié: {Path.GetFileName(e.FullPath)}"
        };

        _realTimeChanges[e.FullPath] = change;
        RealTimeChangeDetected?.Invoke(this, change);
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (ShouldIgnorePath(e.FullPath)) return;

        var change = new SystemChange
        {
            ChangeType = ChangeType.Deleted,
            Category = SystemChangeCategory.File,
            Path = e.FullPath,
            ProcessName = GetCurrentInstallerProcess(),
            Description = $"Fichier supprimé: {Path.GetFileName(e.FullPath)}"
        };

        _realTimeChanges[e.FullPath] = change;
        RealTimeChangeDetected?.Invoke(this, change);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (ShouldIgnorePath(e.FullPath)) return;

        var change = new SystemChange
        {
            ChangeType = ChangeType.Renamed,
            Category = Directory.Exists(e.FullPath) ? SystemChangeCategory.Folder : SystemChangeCategory.File,
            Path = e.FullPath,
            OldValue = e.OldFullPath,
            NewValue = e.FullPath,
            ProcessName = GetCurrentInstallerProcess(),
            Description = $"Renommé: {e.OldName} → {e.Name}"
        };

        // Supprimer l'ancien chemin s'il existait
        _realTimeChanges.TryRemove(e.OldFullPath, out _);
        _realTimeChanges[e.FullPath] = change;
        RealTimeChangeDetected?.Invoke(this, change);
    }

    private static void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Debug.WriteLine($"Erreur FileSystemWatcher: {e.GetException().Message}");
    }

    private static bool ShouldIgnorePath(string path)
    {
        var lowerPath = path.ToLowerInvariant();
        
        // Ignorer les fichiers temporaires et les caches
        if (lowerPath.Contains("\\temp\\") ||
            lowerPath.Contains("\\cache\\") ||
            lowerPath.Contains("\\logs\\") ||
            lowerPath.EndsWith(".tmp") ||
            lowerPath.EndsWith(".log") ||
            lowerPath.Contains("\\__pycache__\\") ||
            lowerPath.Contains("\\.git\\") ||
            lowerPath.Contains("\\node_modules\\"))
        {
            return true;
        }

        // Ignorer certains dossiers système
        if (lowerPath.Contains("\\windows\\") ||
            lowerPath.Contains("\\microsoft\\edge\\") ||
            lowerPath.Contains("\\microsoft\\windows\\"))
        {
            return true;
        }

        return false;
    }

    private static string? GetCurrentInstallerProcess()
    {
        try
        {
            // Chercher les processus d'installation courants
            var installerNames = new[] { "setup", "install", "msiexec", "uninst" };
            var processes = Process.GetProcesses();

            foreach (var proc in processes)
            {
                try
                {
                    var name = proc.ProcessName.ToLowerInvariant();
                    if (installerNames.Any(n => name.Contains(n)))
                    {
                        return proc.ProcessName;
                    }
                }
                catch { }
            }
        }
        catch { }

        return null;
    }

    #endregion

    #region Merge and Storage

    private static List<SystemChange> MergeChanges(
        List<SystemChange> snapshotChanges,
        IEnumerable<SystemChange> realTimeChanges)
    {
        var merged = new Dictionary<string, SystemChange>(StringComparer.OrdinalIgnoreCase);

        // Ajouter d'abord les changements temps réel (plus précis)
        foreach (var change in realTimeChanges)
        {
            merged[change.Path] = change;
        }

        // Ajouter les changements du snapshot qui ne sont pas déjà présents
        foreach (var change in snapshotChanges)
        {
            if (!merged.ContainsKey(change.Path))
            {
                merged[change.Path] = change;
            }
        }

        return merged.Values
            .OrderBy(c => c.Category)
            .ThenBy(c => c.Path)
            .ToList();
    }

    /// <summary>
    /// Sauvegarde une installation surveillée
    /// </summary>
    public async Task SaveMonitoredInstallationAsync(MonitoredInstallation installation)
    {
        var filePath = Path.Combine(_dataFolder, $"{installation.Id}.json");
        var json = JsonSerializer.Serialize(installation, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Charge toutes les installations surveillées
    /// </summary>
    public async Task<List<MonitoredInstallation>> LoadAllMonitoredInstallationsAsync()
    {
        var installations = new List<MonitoredInstallation>();

        if (!Directory.Exists(_dataFolder)) return installations;

        foreach (var file in Directory.EnumerateFiles(_dataFolder, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var installation = JsonSerializer.Deserialize<MonitoredInstallation>(json);
                if (installation != null)
                {
                    installations.Add(installation);
                }
            }
            catch { }
        }

        return installations.OrderByDescending(i => i.StartTime).ToList();
    }

    /// <summary>
    /// Charge une installation surveillée par ID
    /// </summary>
    public async Task<MonitoredInstallation?> LoadMonitoredInstallationAsync(string id)
    {
        var filePath = Path.Combine(_dataFolder, $"{id}.json");
        if (!File.Exists(filePath)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<MonitoredInstallation>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Supprime une installation surveillée
    /// </summary>
    public void DeleteMonitoredInstallation(string id)
    {
        var filePath = Path.Combine(_dataFolder, $"{id}.json");
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    #endregion

    #region Perfect Uninstall

    /// <summary>
    /// Effectue une désinstallation parfaite basée sur les changements enregistrés
    /// </summary>
    public async Task<UninstallResult> PerfectUninstallAsync(
        MonitoredInstallation installation,
        bool removeSelectedOnly = true,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new UninstallResult { Success = true };
        var changes = removeSelectedOnly
            ? installation.Changes.Where(c => c.IsSelected).ToList()
            : installation.Changes;

        var total = changes.Count;
        var processed = 0;

        // Trier: services d'abord, puis tâches, puis fichiers, puis registre
        var orderedChanges = changes
            .Where(c => c.ChangeType == ChangeType.Created)
            .OrderBy(c => c.Category switch
            {
                SystemChangeCategory.Service => 0,
                SystemChangeCategory.ScheduledTask => 1,
                SystemChangeCategory.FirewallRule => 2,
                SystemChangeCategory.StartupEntry => 3,
                SystemChangeCategory.File => 4,
                SystemChangeCategory.Folder => 5,
                SystemChangeCategory.RegistryValue => 6,
                SystemChangeCategory.RegistryKey => 7,
                SystemChangeCategory.EnvironmentVariable => 8,
                _ => 9
            })
            .ToList();

        foreach (var change in orderedChanges)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                progress?.Report(new ScanProgress(
                    (processed * 100) / total,
                    $"Suppression de {change.DisplayPath}..."));

                var success = await RevertChangeAsync(change);
                
                if (success)
                {
                    change.IsReverted = true;
                    result.DeletedCount++;
                    result.SpaceFreed += change.Size;
                }
                else
                {
                    result.FailedCount++;
                }
            }
            catch (Exception ex)
            {
                change.ErrorMessage = ex.Message;
                result.FailedCount++;
            }

            processed++;
        }

        // Marquer l'installation comme désinstallée
        installation.IsUninstalled = true;
        installation.UninstallDate = DateTime.Now;
        await SaveMonitoredInstallationAsync(installation);

        progress?.Report(new ScanProgress(100,
            $"Terminé: {result.DeletedCount} supprimés, {result.FailedCount} échecs"));

        return result;
    }

    private static async Task<bool> RevertChangeAsync(SystemChange change)
    {
        return await Task.Run(() =>
        {
            try
            {
                switch (change.Category)
                {
                    case SystemChangeCategory.File:
                        if (File.Exists(change.Path))
                        {
                            File.Delete(change.Path);
                        }
                        return true;

                    case SystemChangeCategory.Folder:
                        if (Directory.Exists(change.Path))
                        {
                            Directory.Delete(change.Path, recursive: true);
                        }
                        return true;

                    case SystemChangeCategory.RegistryKey:
                        return DeleteRegistryKey(change.Path);

                    case SystemChangeCategory.RegistryValue:
                        return DeleteRegistryValue(change.Path);

                    case SystemChangeCategory.Service:
                        return StopAndDeleteService(change.Path);

                    case SystemChangeCategory.ScheduledTask:
                        return DeleteScheduledTask(change.Path);

                    case SystemChangeCategory.FirewallRule:
                        return DeleteFirewallRule(change.Path);

                    case SystemChangeCategory.StartupEntry:
                        return DeleteStartupEntry(change.Path);

                    case SystemChangeCategory.EnvironmentVariable:
                        return DeleteEnvironmentVariable(change.Path);

                    case SystemChangeCategory.Driver:
                        return DeleteDriver(change.Path);

                    case SystemChangeCategory.ComObject:
                        return DeleteComObject(change.Path);

                    case SystemChangeCategory.FileAssociation:
                        return ResetFileAssociation(change.Path);

                    case SystemChangeCategory.Font:
                        return UninstallFont(change.Path, change.NewValue ?? "");

                    case SystemChangeCategory.ShellExtension:
                        return DeleteShellExtension(change.Path, change.NewValue);

                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        });
    }

    private static bool DeleteRegistryKey(string path)
    {
        try
        {
            var (root, subPath) = ParseRegistryPath(path);
            if (root == null) return false;

            root.DeleteSubKeyTree(subPath, throwOnMissingSubKey: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool DeleteRegistryValue(string path)
    {
        try
        {
            var lastBackslash = path.LastIndexOf('\\');
            if (lastBackslash < 0) return false;

            var keyPath = path[..lastBackslash];
            var valueName = path[(lastBackslash + 1)..];

            var (root, subPath) = ParseRegistryPath(keyPath);
            if (root == null) return false;

            using var key = root.OpenSubKey(subPath, writable: true);
            key?.DeleteValue(valueName, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static (Microsoft.Win32.RegistryKey? Root, string SubPath) ParseRegistryPath(string path)
    {
        if (path.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase))
            return (Microsoft.Win32.Registry.LocalMachine, path[5..]);
        if (path.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
            return (Microsoft.Win32.Registry.CurrentUser, path[5..]);
        if (path.StartsWith("HKCR\\", StringComparison.OrdinalIgnoreCase))
            return (Microsoft.Win32.Registry.ClassesRoot, path[5..]);
        return (null, "");
    }

    private static bool StopAndDeleteService(string serviceName)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"delete \"{serviceName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            var process = Process.Start(startInfo);
            process?.WaitForExit(10000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool DeleteScheduledTask(string taskPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Delete /TN \"{taskPath}\" /F",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            var process = Process.Start(startInfo);
            process?.WaitForExit(10000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool DeleteFirewallRule(string ruleName)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = $"advfirewall firewall delete rule name=\"{ruleName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            var process = Process.Start(startInfo);
            process?.WaitForExit(10000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool DeleteStartupEntry(string path)
    {
        try
        {
            // Si c'est un fichier (dossier Startup)
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }

            // Si c'est une clé de registre
            return DeleteRegistryValue(path);
        }
        catch
        {
            return false;
        }
    }

    private static bool DeleteEnvironmentVariable(string path)
    {
        try
        {
            // Format: "Scope:VariableName"
            var parts = path.Split(':');
            if (parts.Length != 2) return false;

            var scope = parts[0].Equals("Système", StringComparison.OrdinalIgnoreCase)
                ? EnvironmentVariableTarget.Machine
                : EnvironmentVariableTarget.User;
            
            var varName = parts[1];
            
            Environment.SetEnvironmentVariable(varName, null, scope);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool DeleteDriver(string driverName)
    {
        try
        {
            // Arrêter le pilote d'abord
            var stopInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"stop \"{driverName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            var stopProcess = Process.Start(stopInfo);
            stopProcess?.WaitForExit(5000);

            // Supprimer le pilote
            var deleteInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"delete \"{driverName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            var deleteProcess = Process.Start(deleteInfo);
            deleteProcess?.WaitForExit(10000);
            return deleteProcess?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool DeleteComObject(string clsidPath)
    {
        try
        {
            // Le chemin est au format "CLSID\{guid}"
            var clsid = clsidPath.Replace("CLSID\\", "");
            
            // Supprimer de HKCR\CLSID
            using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey("CLSID", writable: true);
            key?.DeleteSubKeyTree(clsid, throwOnMissingSubKey: false);

            // Supprimer de HKLM\SOFTWARE\Classes\CLSID si présent
            using var lmKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes\CLSID", writable: true);
            lmKey?.DeleteSubKeyTree(clsid, throwOnMissingSubKey: false);

            // Supprimer de WOW6432Node si présent
            using var wow64Key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Classes\CLSID", writable: true);
            wow64Key?.DeleteSubKeyTree(clsid, throwOnMissingSubKey: false);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ResetFileAssociation(string extension)
    {
        try
        {
            // Supprimer l'association utilisateur
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FileExts", writable: true);
            key?.DeleteSubKeyTree(extension, throwOnMissingSubKey: false);

            // Notifier le shell du changement
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
            
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool UninstallFont(string fontFilePath, string fontName)
    {
        try
        {
            // Supprimer du registre système
            using var systemKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts", writable: true);
            
            if (systemKey != null)
            {
                foreach (var name in systemKey.GetValueNames())
                {
                    if (name.Contains(fontName, StringComparison.OrdinalIgnoreCase))
                    {
                        systemKey.DeleteValue(name, throwOnMissingValue: false);
                    }
                }
            }

            // Supprimer du registre utilisateur
            using var userKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts", writable: true);
            
            if (userKey != null)
            {
                foreach (var name in userKey.GetValueNames())
                {
                    if (name.Contains(fontName, StringComparison.OrdinalIgnoreCase))
                    {
                        userKey.DeleteValue(name, throwOnMissingValue: false);
                    }
                }
            }

            // Supprimer le fichier de police
            if (File.Exists(fontFilePath))
            {
                File.Delete(fontFilePath);
            }

            // Notifier Windows du changement
            var result = RemoveFontResource(fontFilePath);
            SendMessage(HWND_BROADCAST, WM_FONTCHANGE, IntPtr.Zero, IntPtr.Zero);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool DeleteShellExtension(string name, string? clsid)
    {
        try
        {
            // Chemins possibles pour les extensions shell
            var extensionPaths = new[]
            {
                $@"*\shellex\ContextMenuHandlers\{name}",
                $@"Directory\shellex\ContextMenuHandlers\{name}",
                $@"Folder\shellex\ContextMenuHandlers\{name}",
                $@"*\shellex\PropertySheetHandlers\{name}",
                $@"Directory\Background\shellex\ContextMenuHandlers\{name}",
            };

            foreach (var path in extensionPaths)
            {
                try
                {
                    Microsoft.Win32.Registry.ClassesRoot.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
                }
                catch { }
            }

            // Si un CLSID est fourni, le supprimer aussi
            if (!string.IsNullOrEmpty(clsid))
            {
                DeleteComObject($"CLSID\\{clsid}");
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    // P/Invoke pour les opérations sur les polices et le shell
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern int RemoveFontResource(string lpFileName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
    private const uint WM_FONTCHANGE = 0x001D;

    #endregion

    public void Dispose()
    {
        if (_isDisposed) return;

        StopFileWatchers();
        _monitoringCts?.Cancel();
        _monitoringCts?.Dispose();
        _isDisposed = true;

        GC.SuppressFinalize(this);
    }
}
