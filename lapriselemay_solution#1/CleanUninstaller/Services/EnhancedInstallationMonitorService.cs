using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using CleanUninstaller.Models;

namespace CleanUninstaller.Services;

/// <summary>
/// Service de monitoring d'installation amélioré avec surveillance complète
/// Combine: FileSystemWatcher + RegistryWatcher + Process Monitoring + Snapshots
/// </summary>
public class EnhancedInstallationMonitorService : IDisposable
{
    private readonly SnapshotService _snapshotService;
    private readonly AdvancedMonitoringService _advancedService;
    private readonly BackupService _backupService;
    private readonly List<FileSystemWatcher> _fileWatchers = [];
    private readonly ConcurrentDictionary<string, SystemChange> _realTimeChanges = new();
    private readonly string _dataFolder;

    private MonitoredInstallation? _currentMonitoring;
    private InstallationSnapshot? _beforeSnapshot;
    private CancellationTokenSource? _monitoringCts;
    private bool _isDisposed;

    // Statistiques temps réel
    private int _filesDetected;
    private int _foldersDetected;
    private int _registryDetected;
    private int _servicesDetected;
    private int _processesDetected;

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
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts"),
    ];

    #region Events

    public event EventHandler<SystemChange>? RealTimeChangeDetected;
    public event EventHandler<MonitoringStatus>? StatusChanged;
    public event EventHandler<ProcessInfo>? InstallerProcessDetected;
    public event EventHandler<ProcessInfo>? InstallerProcessExited;
    public event EventHandler<MonitoringStatistics>? StatisticsUpdated;

    #endregion

    #region Properties

    public MonitoredInstallation? CurrentMonitoring => _currentMonitoring;
    public bool IsMonitoring => _currentMonitoring?.Status == MonitoringStatus.Monitoring;
    public int RealTimeChangeCount => _realTimeChanges.Count;
    
    public MonitoringStatistics Statistics => new()
    {
        FilesDetected = _filesDetected,
        FoldersDetected = _foldersDetected,
        RegistryChangesDetected = _registryDetected,
        ServicesDetected = _servicesDetected,
        ProcessesDetected = _processesDetected,
        TotalChanges = _realTimeChanges.Count,
        TrackedProcesses = _advancedService.GetTrackedProcesses().ToList()
    };

    #endregion

    public EnhancedInstallationMonitorService()
    {
        _snapshotService = new SnapshotService();
        _advancedService = new AdvancedMonitoringService();
        _backupService = new BackupService();
        
        _dataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanUninstaller",
            "MonitoredInstallations");

        Directory.CreateDirectory(_dataFolder);

        // Connecter les événements du service avancé
        _advancedService.RegistryChangeDetected += OnAdvancedRegistryChange;
        _advancedService.InstallerProcessDetected += OnInstallerProcessDetected;
        _advancedService.InstallerProcessExited += OnInstallerProcessExited;
    }

    /// <summary>
    /// Démarre le monitoring complet d'une nouvelle installation
    /// </summary>
    public async Task<MonitoredInstallation> StartMonitoringAsync(
        string? installationName = null,
        string? installerPath = null,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await StopMonitoringAsync();

        _monitoringCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _realTimeChanges.Clear();
        ResetStatistics();

        _currentMonitoring = new MonitoredInstallation
        {
            Name = installationName ?? "Installation en cours",
            InstallerPath = installerPath,
            Status = MonitoringStatus.TakingSnapshot
        };

        StatusChanged?.Invoke(this, MonitoringStatus.TakingSnapshot);

        try
        {
            // 1. Créer le snapshot "avant" (état initial complet)
            progress?.Report(new ScanProgress(0, "📸 Capture de l'état initial du système..."));
            _beforeSnapshot = await _snapshotService.CreateSnapshotAsync(
                SnapshotType.Before,
                installationName,
                progress,
                _monitoringCts.Token);

            _currentMonitoring.BeforeSnapshotId = _beforeSnapshot.Id;

            // 2. Démarrer la surveillance temps réel des fichiers
            progress?.Report(new ScanProgress(85, "🔍 Démarrage de la surveillance des fichiers..."));
            StartFileWatchers();

            // 3. Démarrer la surveillance temps réel du registre
            progress?.Report(new ScanProgress(90, "🔍 Démarrage de la surveillance du registre..."));
            _advancedService.StartRegistryWatching();

            // 4. Démarrer la surveillance des processus d'installation
            progress?.Report(new ScanProgress(95, "🔍 Démarrage de la surveillance des processus..."));
            _advancedService.StartProcessMonitoring();

            _currentMonitoring.Status = MonitoringStatus.Monitoring;
            StatusChanged?.Invoke(this, MonitoringStatus.Monitoring);

            progress?.Report(new ScanProgress(100, "✅ Surveillance active - Lancez l'installation maintenant"));

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
    /// Arrête le monitoring et analyse tous les changements
    /// </summary>
    public async Task<MonitoredInstallation?> StopMonitoringAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_currentMonitoring == null) return null;

        try
        {
            // 1. Arrêter toute la surveillance temps réel
            StopFileWatchers();
            _advancedService.StopRegistryWatching();
            _advancedService.StopProcessMonitoring();

            _currentMonitoring.Status = MonitoringStatus.Analyzing;
            StatusChanged?.Invoke(this, MonitoringStatus.Analyzing);

            // 2. Créer le snapshot "après" (état final)
            progress?.Report(new ScanProgress(0, "📸 Capture de l'état final du système..."));
            var afterSnapshot = await _snapshotService.CreateSnapshotAsync(
                SnapshotType.After,
                _currentMonitoring.Name,
                progress,
                cancellationToken);

            _currentMonitoring.AfterSnapshotId = afterSnapshot.Id;

            // 3. Comparer les snapshots pour une détection complète
            if (_beforeSnapshot != null)
            {
                progress?.Report(new ScanProgress(70, "🔬 Analyse des changements..."));
                var snapshotChanges = await _snapshotService.CompareSnapshotsAsync(
                    _beforeSnapshot,
                    afterSnapshot,
                    progress,
                    cancellationToken);

                // 4. Fusionner avec les changements temps réel et du registre avancé
                var registryChanges = _advancedService.GetRegistryChanges();
                var allRealTimeChanges = _realTimeChanges.Values.Concat(registryChanges);
                
                var allChanges = MergeChanges(snapshotChanges, allRealTimeChanges);
                
                // 5. Filtrer et dédupliquer
                var filteredChanges = FilterAndDeduplicateChanges(allChanges);
                
                _currentMonitoring.Changes.Clear();
                _currentMonitoring.Changes.AddRange(filteredChanges);
            }

            _currentMonitoring.EndTime = DateTime.Now;
            _currentMonitoring.Status = MonitoringStatus.Completed;
            StatusChanged?.Invoke(this, MonitoringStatus.Completed);

            // 6. Sauvegarder
            await SaveMonitoredInstallationAsync(_currentMonitoring);

            progress?.Report(new ScanProgress(100, 
                $"✅ Analyse terminée: {_currentMonitoring.Statistics.TotalChanges} changements détectés"));

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
        _advancedService.StopRegistryWatching();
        _advancedService.StopProcessMonitoring();
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
        ResetStatistics();
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

        _advancedService.StopRegistryWatching();

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

        _advancedService.StartRegistryWatching();

        _currentMonitoring.Status = MonitoringStatus.Monitoring;
        StatusChanged?.Invoke(this, MonitoringStatus.Monitoring);
    }

    #region Perfect Uninstall

    /// <summary>
    /// Effectue une désinstallation parfaite avec sauvegarde préalable
    /// </summary>
    public async Task<UninstallResult> PerfectUninstallAsync(
        MonitoredInstallation installation,
        bool createBackup = true,
        bool removeSelectedOnly = true,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new UninstallResult { Success = true };
        UninstallBackup? backup = null;

        try
        {
            // 1. Créer une sauvegarde si demandé
            if (createBackup)
            {
                progress?.Report(new ScanProgress(0, "💾 Création de la sauvegarde..."));
                backup = await _backupService.CreateBackupAsync(
                    installation,
                    new Progress<ScanProgress>(p => progress?.Report(new ScanProgress(p.Percentage / 4, p.StatusMessage))),
                    cancellationToken);
                
                result.BackupId = backup.Id;
            }

            // 2. Effectuer la désinstallation
            var changes = removeSelectedOnly
                ? installation.Changes.Where(c => c.IsSelected).ToList()
                : installation.Changes;

            var total = changes.Count;
            var processed = 0;

            // Trier par ordre de suppression: services -> tâches -> fichiers -> registre
            var orderedChanges = changes
                .Where(c => c.ChangeType == ChangeType.Created)
                .OrderBy(c => GetDeletionOrder(c.Category))
                .ToList();

            foreach (var change in orderedChanges)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var percentage = 25 + ((processed * 75) / total);
                    progress?.Report(new ScanProgress(percentage, $"🗑️ Suppression de {change.DisplayPath}..."));

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

            // 3. Nettoyer les dossiers vides
            progress?.Report(new ScanProgress(95, "🧹 Nettoyage des dossiers vides..."));
            CleanEmptyFolders(orderedChanges);

            // 4. Marquer l'installation comme désinstallée
            installation.IsUninstalled = true;
            installation.UninstallDate = DateTime.Now;
            await SaveMonitoredInstallationAsync(installation);

            progress?.Report(new ScanProgress(100,
                $"✅ Terminé: {result.DeletedCount} supprimés, {result.FailedCount} échecs"));
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Restaure une installation depuis une sauvegarde
    /// </summary>
    public async Task<RestoreResult> RestoreInstallationAsync(
        string backupId,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var backups = await _backupService.GetBackupsAsync();
        var backup = backups.FirstOrDefault(b => b.Id == backupId);

        if (backup == null)
        {
            return new RestoreResult { Errors = { "Sauvegarde non trouvée" } };
        }

        return await _backupService.RestoreBackupAsync(backup, progress, cancellationToken);
    }

    private static int GetDeletionOrder(SystemChangeCategory category) => category switch
    {
        SystemChangeCategory.Service => 0,
        SystemChangeCategory.Driver => 1,
        SystemChangeCategory.ScheduledTask => 2,
        SystemChangeCategory.FirewallRule => 3,
        SystemChangeCategory.StartupEntry => 4,
        SystemChangeCategory.ShellExtension => 5,
        SystemChangeCategory.ComObject => 6,
        SystemChangeCategory.File => 7,
        SystemChangeCategory.Folder => 8,
        SystemChangeCategory.RegistryValue => 9,
        SystemChangeCategory.RegistryKey => 10,
        SystemChangeCategory.EnvironmentVariable => 11,
        SystemChangeCategory.FileAssociation => 12,
        SystemChangeCategory.Font => 13,
        _ => 99
    };

    private static void CleanEmptyFolders(List<SystemChange> changes)
    {
        // Obtenir tous les dossiers potentiellement vides (parents des fichiers supprimés)
        var foldersToCheck = changes
            .Where(c => c.Category == SystemChangeCategory.File && c.IsReverted)
            .Select(c => Path.GetDirectoryName(c.Path))
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct()
            .OrderByDescending(p => p!.Length) // Commencer par les plus profonds
            .ToList();

        foreach (var folder in foldersToCheck)
        {
            try
            {
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder!).Any())
                {
                    Directory.Delete(folder!, recursive: false);
                }
            }
            catch { }
        }
    }

    #endregion

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
                    InternalBufferSize = 65536, // 64KB buffer pour éviter les pertes
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
            try { size = new FileInfo(e.FullPath).Length; } catch { }
            Interlocked.Increment(ref _filesDetected);
        }
        else
        {
            Interlocked.Increment(ref _foldersDetected);
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
        UpdateStatistics();
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

        _realTimeChanges.TryRemove(e.OldFullPath, out _);
        _realTimeChanges[e.FullPath] = change;
        RealTimeChangeDetected?.Invoke(this, change);
    }

    private static void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Debug.WriteLine($"Erreur FileSystemWatcher: {e.GetException().Message}");
    }

    #endregion

    #region Advanced Monitoring Event Handlers

    private void OnAdvancedRegistryChange(object? sender, SystemChange change)
    {
        Interlocked.Increment(ref _registryDetected);
        
        // Éviter les doublons
        var key = $"REG:{change.Path}";
        _realTimeChanges[key] = change;
        
        RealTimeChangeDetected?.Invoke(this, change);
        UpdateStatistics();
    }

    private void OnInstallerProcessDetected(object? sender, ProcessInfo info)
    {
        Interlocked.Increment(ref _processesDetected);
        InstallerProcessDetected?.Invoke(this, info);
        UpdateStatistics();
    }

    private void OnInstallerProcessExited(object? sender, ProcessInfo info)
    {
        InstallerProcessExited?.Invoke(this, info);
    }

    #endregion

    #region Helper Methods

    private static bool ShouldIgnorePath(string path)
    {
        var lowerPath = path.ToLowerInvariant();

        // Extensions à ignorer
        var ignoredExtensions = new[] { ".tmp", ".log", ".etl", ".pf", ".db-wal", ".db-shm" };
        if (ignoredExtensions.Any(ext => lowerPath.EndsWith(ext)))
            return true;

        // Dossiers à ignorer
        var ignoredFolders = new[]
        {
            "\\temp\\", "\\cache\\", "\\logs\\", "\\__pycache__\\",
            "\\.git\\", "\\node_modules\\", "\\webcache\\",
            "\\microsoft\\edge\\", "\\microsoft\\windows\\",
            "\\windows\\", "\\inetcache\\", "\\thumbnails\\"
        };

        return ignoredFolders.Any(folder => lowerPath.Contains(folder));
    }

    private static string? GetCurrentInstallerProcess()
    {
        try
        {
            var installerNames = new[] { "setup", "install", "msiexec", "uninst", "update", "patch" };
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

    private void ResetStatistics()
    {
        _filesDetected = 0;
        _foldersDetected = 0;
        _registryDetected = 0;
        _servicesDetected = 0;
        _processesDetected = 0;
    }

    private void UpdateStatistics()
    {
        StatisticsUpdated?.Invoke(this, Statistics);
    }

    private static List<SystemChange> MergeChanges(
        List<SystemChange> snapshotChanges,
        IEnumerable<SystemChange> realTimeChanges)
    {
        var merged = new Dictionary<string, SystemChange>(StringComparer.OrdinalIgnoreCase);

        // Les changements temps réel ont priorité (plus précis sur le timing)
        foreach (var change in realTimeChanges)
        {
            merged[change.Path] = change;
        }

        // Ajouter les changements du snapshot non présents
        foreach (var change in snapshotChanges)
        {
            if (!merged.ContainsKey(change.Path))
            {
                merged[change.Path] = change;
            }
        }

        return [.. merged.Values
            .OrderBy(c => c.Category)
            .ThenBy(c => c.Path)];
    }

    private static List<SystemChange> FilterAndDeduplicateChanges(List<SystemChange> changes)
    {
        var filtered = new List<SystemChange>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var change in changes)
        {
            // Ignorer les chemins déjà traités
            if (seenPaths.Contains(change.Path)) continue;

            // Ignorer les changements sur des fichiers/dossiers qui n'existent plus 
            // (sauf si c'est une suppression)
            if (change.ChangeType == ChangeType.Created)
            {
                var exists = change.Category switch
                {
                    SystemChangeCategory.File => File.Exists(change.Path),
                    SystemChangeCategory.Folder => Directory.Exists(change.Path),
                    _ => true // Pour les autres types, on garde
                };

                if (!exists) continue;
            }

            // Ignorer les fichiers système critiques
            if (IsCriticalSystemPath(change.Path)) continue;

            seenPaths.Add(change.Path);
            filtered.Add(change);
        }

        return filtered;
    }

    private static bool IsCriticalSystemPath(string path)
    {
        var lowerPath = path.ToLowerInvariant();
        var criticalPaths = new[]
        {
            "\\windows\\system32\\",
            "\\windows\\syswow64\\",
            "\\windows\\winsxs\\",
            "\\program files\\windowsapps\\"
        };

        return criticalPaths.Any(cp => lowerPath.Contains(cp));
    }

    #endregion

    #region Revert Changes

    private static async Task<bool> RevertChangeAsync(SystemChange change)
    {
        return await Task.Run(() =>
        {
            try
            {
                return change.Category switch
                {
                    SystemChangeCategory.File => DeleteFile(change.Path),
                    SystemChangeCategory.Folder => DeleteFolder(change.Path),
                    SystemChangeCategory.RegistryKey => DeleteRegistryKey(change.Path),
                    SystemChangeCategory.RegistryValue => DeleteRegistryValue(change.Path),
                    SystemChangeCategory.Service => StopAndDeleteService(change.Path),
                    SystemChangeCategory.ScheduledTask => DeleteScheduledTask(change.Path),
                    SystemChangeCategory.FirewallRule => DeleteFirewallRule(change.Path),
                    SystemChangeCategory.StartupEntry => DeleteStartupEntry(change.Path),
                    SystemChangeCategory.EnvironmentVariable => DeleteEnvironmentVariable(change.Path),
                    SystemChangeCategory.Driver => DeleteDriver(change.Path),
                    SystemChangeCategory.ComObject => DeleteComObject(change.Path),
                    SystemChangeCategory.FileAssociation => ResetFileAssociation(change.Path),
                    SystemChangeCategory.Font => UninstallFont(change.Path, change.NewValue ?? ""),
                    SystemChangeCategory.ShellExtension => DeleteShellExtension(change.Path, change.NewValue),
                    _ => false
                };
            }
            catch
            {
                return false;
            }
        });
    }

    private static bool DeleteFile(string path)
    {
        if (!File.Exists(path)) return true;
        
        try
        {
            // Retirer l'attribut readonly si présent
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
            {
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
            }
            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool DeleteFolder(string path)
    {
        if (!Directory.Exists(path)) return true;
        
        try
        {
            Directory.Delete(path, recursive: true);
            return true;
        }
        catch
        {
            return false;
        }
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
            // Arrêter le service
            RunCommand("sc.exe", $"stop \"{serviceName}\"", 5000);
            // Supprimer le service
            return RunCommand("sc.exe", $"delete \"{serviceName}\"", 10000);
        }
        catch
        {
            return false;
        }
    }

    private static bool DeleteScheduledTask(string taskPath)
    {
        return RunCommand("schtasks.exe", $"/Delete /TN \"{taskPath}\" /F", 10000);
    }

    private static bool DeleteFirewallRule(string ruleName)
    {
        return RunCommand("netsh.exe", $"advfirewall firewall delete rule name=\"{ruleName}\"", 10000);
    }

    private static bool DeleteStartupEntry(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
            return true;
        }
        return DeleteRegistryValue(path);
    }

    private static bool DeleteEnvironmentVariable(string path)
    {
        try
        {
            var parts = path.Split(':');
            if (parts.Length != 2) return false;

            var scope = parts[0].Equals("Système", StringComparison.OrdinalIgnoreCase)
                ? EnvironmentVariableTarget.Machine
                : EnvironmentVariableTarget.User;

            Environment.SetEnvironmentVariable(parts[1], null, scope);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool DeleteDriver(string driverName)
    {
        RunCommand("sc.exe", $"stop \"{driverName}\"", 5000);
        return RunCommand("sc.exe", $"delete \"{driverName}\"", 10000);
    }

    private static bool DeleteComObject(string clsidPath)
    {
        try
        {
            var clsid = clsidPath.Replace("CLSID\\", "");

            using (var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey("CLSID", writable: true))
                key?.DeleteSubKeyTree(clsid, throwOnMissingSubKey: false);

            using (var lmKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes\CLSID", writable: true))
                lmKey?.DeleteSubKeyTree(clsid, throwOnMissingSubKey: false);

            using (var wow64Key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Classes\CLSID", writable: true))
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
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FileExts", writable: true);
            key?.DeleteSubKeyTree(extension, throwOnMissingSubKey: false);

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
            // Supprimer du registre
            using (var systemKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts", writable: true))
            {
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
            }

            // Supprimer le fichier
            if (File.Exists(fontFilePath))
            {
                RemoveFontResource(fontFilePath);
                File.Delete(fontFilePath);
            }

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

    private static bool RunCommand(string fileName, string arguments, int timeoutMs)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit(timeoutMs);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Storage

    public async Task SaveMonitoredInstallationAsync(MonitoredInstallation installation)
    {
        var filePath = Path.Combine(_dataFolder, $"{installation.Id}.json");
        var json = JsonSerializer.Serialize(installation, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json);
    }

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
                if (installation != null) installations.Add(installation);
            }
            catch { }
        }

        return installations.OrderByDescending(i => i.StartTime).ToList();
    }

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

    public void DeleteMonitoredInstallation(string id)
    {
        var filePath = Path.Combine(_dataFolder, $"{id}.json");
        if (File.Exists(filePath)) File.Delete(filePath);
    }

    #endregion

    #region P/Invoke

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
        _advancedService.RegistryChangeDetected -= OnAdvancedRegistryChange;
        _advancedService.InstallerProcessDetected -= OnInstallerProcessDetected;
        _advancedService.InstallerProcessExited -= OnInstallerProcessExited;
        _advancedService.Dispose();
        _monitoringCts?.Cancel();
        _monitoringCts?.Dispose();
        _isDisposed = true;

        GC.SuppressFinalize(this);
    }
}

#region Supporting Types

/// <summary>
/// Statistiques de monitoring en temps réel
/// </summary>
public class MonitoringStatistics
{
    public int FilesDetected { get; init; }
    public int FoldersDetected { get; init; }
    public int RegistryChangesDetected { get; init; }
    public int ServicesDetected { get; init; }
    public int ProcessesDetected { get; init; }
    public int TotalChanges { get; init; }
    public List<ProcessInfo> TrackedProcesses { get; init; } = [];
}

#endregion
