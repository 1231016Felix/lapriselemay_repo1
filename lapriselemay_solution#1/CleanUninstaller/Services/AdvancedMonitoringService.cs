using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using CleanUninstaller.Models;

namespace CleanUninstaller.Services;

/// <summary>
/// Service de monitoring avancé avec surveillance du registre en temps réel,
/// détection des pilotes, associations de fichiers et objets COM
/// </summary>
public partial class AdvancedMonitoringService : IDisposable
{
    private readonly ConcurrentDictionary<string, SystemChange> _registryChanges = new();
    private readonly ConcurrentDictionary<string, ProcessInfo> _trackedProcesses = new();
    private readonly List<RegistryWatcher> _registryWatchers = [];
    private CancellationTokenSource? _processMonitorCts;
    private bool _isDisposed;

    // Événements
    public event EventHandler<SystemChange>? RegistryChangeDetected;
    public event EventHandler<ProcessInfo>? InstallerProcessDetected;
    public event EventHandler<ProcessInfo>? InstallerProcessExited;

    #region P/Invoke

    [LibraryImport("advapi32.dll", SetLastError = true)]
    private static partial int RegNotifyChangeKeyValue(
        nint hKey,
        [MarshalAs(UnmanagedType.Bool)] bool bWatchSubtree,
        uint dwNotifyFilter,
        nint hEvent,
        [MarshalAs(UnmanagedType.Bool)] bool fAsynchronous);

    private const uint REG_NOTIFY_CHANGE_NAME = 0x00000001;
    private const uint REG_NOTIFY_CHANGE_ATTRIBUTES = 0x00000002;
    private const uint REG_NOTIFY_CHANGE_LAST_SET = 0x00000004;
    private const uint REG_NOTIFY_CHANGE_SECURITY = 0x00000008;
    private const uint REG_LEGAL_CHANGE_FILTER = REG_NOTIFY_CHANGE_NAME |
                                                  REG_NOTIFY_CHANGE_ATTRIBUTES |
                                                  REG_NOTIFY_CHANGE_LAST_SET;

    #endregion

    /// <summary>
    /// Clés de registre importantes à surveiller en temps réel
    /// </summary>
    private static readonly (RegistryKey Root, string Path)[] CriticalRegistryPaths =
    [
        // Programmes installés
        (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        
        // Services
        (Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services"),
        
        // Démarrage automatique
        (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
        
        // Shell Extensions
        (Registry.ClassesRoot, @"*\shellex\ContextMenuHandlers"),
        (Registry.ClassesRoot, @"Directory\shellex\ContextMenuHandlers"),
        (Registry.ClassesRoot, @"Folder\shellex\ContextMenuHandlers"),
        
        // Associations de fichiers
        (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FileExts"),
        (Registry.ClassesRoot, @".exe"),
        (Registry.ClassesRoot, @".txt"),
        (Registry.ClassesRoot, @".pdf"),
        
        // COM Objects
        (Registry.ClassesRoot, @"CLSID"),
        (Registry.LocalMachine, @"SOFTWARE\Classes\CLSID"),
        
        // Pilotes
        (Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Class"),
        
        // Variables d'environnement
        (Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"),
        (Registry.CurrentUser, @"Environment"),
    ];

    /// <summary>
    /// Démarre la surveillance avancée du registre
    /// </summary>
    public void StartRegistryWatching()
    {
        StopRegistryWatching();
        _registryChanges.Clear();

        foreach (var (root, path) in CriticalRegistryPaths)
        {
            try
            {
                var watcher = new RegistryWatcher(root, path);
                watcher.Changed += OnRegistryKeyChanged;
                watcher.Start();
                _registryWatchers.Add(watcher);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Impossible de surveiller {GetRootName(root)}\\{path}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Arrête la surveillance du registre
    /// </summary>
    public void StopRegistryWatching()
    {
        foreach (var watcher in _registryWatchers)
        {
            watcher.Changed -= OnRegistryKeyChanged;
            watcher.Dispose();
        }
        _registryWatchers.Clear();
    }

    /// <summary>
    /// Démarre la surveillance des processus d'installation
    /// </summary>
    public void StartProcessMonitoring()
    {
        StopProcessMonitoring();
        _trackedProcesses.Clear();
        _processMonitorCts = new CancellationTokenSource();

        _ = MonitorProcessesAsync(_processMonitorCts.Token);
    }

    /// <summary>
    /// Arrête la surveillance des processus
    /// </summary>
    public void StopProcessMonitoring()
    {
        _processMonitorCts?.Cancel();
        _processMonitorCts?.Dispose();
        _processMonitorCts = null;
    }

    /// <summary>
    /// Obtient les changements de registre détectés
    /// </summary>
    public IReadOnlyCollection<SystemChange> GetRegistryChanges() => 
        _registryChanges.Values.ToList();

    /// <summary>
    /// Obtient les processus d'installation suivis
    /// </summary>
    public IReadOnlyCollection<ProcessInfo> GetTrackedProcesses() =>
        _trackedProcesses.Values.ToList();

    #region Registry Watching

    private void OnRegistryKeyChanged(object? sender, RegistryChangeEventArgs e)
    {
        var change = new SystemChange
        {
            ChangeType = ChangeType.Modified,
            Category = SystemChangeCategory.RegistryKey,
            Path = e.FullPath,
            Description = $"Clé de registre modifiée: {e.SubPath}",
            Timestamp = DateTime.Now
        };

        _registryChanges[e.FullPath] = change;
        RegistryChangeDetected?.Invoke(this, change);

        // Analyser les changements spécifiques
        AnalyzeRegistryChange(e.FullPath, e.Root, e.SubPath);
    }

    private void AnalyzeRegistryChange(string fullPath, RegistryKey root, string subPath)
    {
        // Détecter les nouveaux programmes installés
        if (subPath.Contains("Uninstall", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var key = root.OpenSubKey(subPath);
                if (key != null)
                {
                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        using var programKey = key.OpenSubKey(subKeyName);
                        var displayName = programKey?.GetValue("DisplayName")?.ToString();
                        if (!string.IsNullOrEmpty(displayName))
                        {
                            var programChange = new SystemChange
                            {
                                ChangeType = ChangeType.Created,
                                Category = SystemChangeCategory.RegistryKey,
                                Path = $"{fullPath}\\{subKeyName}",
                                NewValue = displayName,
                                Description = $"Programme détecté: {displayName}"
                            };
                            _registryChanges[$"Program:{subKeyName}"] = programChange;
                            RegistryChangeDetected?.Invoke(this, programChange);
                        }
                    }
                }
            }
            catch { }
        }

        // Détecter les nouveaux services
        if (subPath.Contains("Services", StringComparison.OrdinalIgnoreCase))
        {
            DetectNewServices(root, subPath);
        }

        // Détecter les shell extensions
        if (subPath.Contains("shellex", StringComparison.OrdinalIgnoreCase))
        {
            DetectShellExtensions(root, subPath);
        }
    }

    private void DetectNewServices(RegistryKey root, string subPath)
    {
        try
        {
            using var key = root.OpenSubKey(subPath);
            if (key == null) return;

            foreach (var serviceName in key.GetSubKeyNames())
            {
                using var serviceKey = key.OpenSubKey(serviceName);
                var imagePath = serviceKey?.GetValue("ImagePath")?.ToString();
                var displayName = serviceKey?.GetValue("DisplayName")?.ToString();

                if (!string.IsNullOrEmpty(imagePath))
                {
                    var serviceChange = new SystemChange
                    {
                        ChangeType = ChangeType.Created,
                        Category = SystemChangeCategory.Service,
                        Path = serviceName,
                        NewValue = imagePath,
                        Description = $"Service: {displayName ?? serviceName}"
                    };
                    _registryChanges[$"Service:{serviceName}"] = serviceChange;
                }
            }
        }
        catch { }
    }

    private void DetectShellExtensions(RegistryKey root, string subPath)
    {
        try
        {
            using var key = root.OpenSubKey(subPath);
            if (key == null) return;

            foreach (var extName in key.GetSubKeyNames())
            {
                using var extKey = key.OpenSubKey(extName);
                var clsid = extKey?.GetValue("")?.ToString();

                if (!string.IsNullOrEmpty(clsid))
                {
                    var extChange = new SystemChange
                    {
                        ChangeType = ChangeType.Created,
                        Category = SystemChangeCategory.RegistryKey,
                        Path = $"{subPath}\\{extName}",
                        NewValue = clsid,
                        Description = $"Shell Extension: {extName}"
                    };
                    _registryChanges[$"ShellExt:{extName}"] = extChange;
                    RegistryChangeDetected?.Invoke(this, extChange);
                }
            }
        }
        catch { }
    }

    #endregion

    #region Process Monitoring

    private async Task MonitorProcessesAsync(CancellationToken cancellationToken)
    {
        var installerKeywords = new[]
        {
            "setup", "install", "uninst", "msiexec", "msi",
            "update", "patch", "deploy", "wizard"
        };

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var currentProcesses = Process.GetProcesses();

                foreach (var proc in currentProcesses)
                {
                    try
                    {
                        var name = proc.ProcessName.ToLowerInvariant();
                        var isInstaller = installerKeywords.Any(k => name.Contains(k));

                        if (isInstaller && !_trackedProcesses.ContainsKey(proc.Id.ToString()))
                        {
                            string? path = null;
                            try
                            {
                                path = proc.MainModule?.FileName;
                            }
                            catch { }

                            var info = new ProcessInfo
                            {
                                ProcessId = proc.Id,
                                ProcessName = proc.ProcessName,
                                FilePath = path,
                                StartTime = proc.StartTime,
                                CommandLine = GetCommandLine(proc.Id)
                            };

                            _trackedProcesses[proc.Id.ToString()] = info;
                            InstallerProcessDetected?.Invoke(this, info);
                        }
                    }
                    catch { }
                }

                // Vérifier les processus terminés
                var toRemove = new List<string>();
                foreach (var (id, info) in _trackedProcesses)
                {
                    try
                    {
                        var proc = Process.GetProcessById(info.ProcessId);
                        if (proc.HasExited)
                        {
                            info.ExitTime = proc.ExitTime;
                            info.ExitCode = proc.ExitCode;
                            toRemove.Add(id);
                            InstallerProcessExited?.Invoke(this, info);
                        }
                    }
                    catch
                    {
                        info.ExitTime = DateTime.Now;
                        toRemove.Add(id);
                        InstallerProcessExited?.Invoke(this, info);
                    }
                }

                foreach (var id in toRemove)
                {
                    _trackedProcesses.TryRemove(id, out _);
                }
            }
            catch { }

            await Task.Delay(500, cancellationToken);
        }
    }

    private static string? GetCommandLine(int processId)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}");
            
            foreach (var obj in searcher.Get())
            {
                return obj["CommandLine"]?.ToString();
            }
        }
        catch { }
        return null;
    }

    #endregion

    #region Driver Detection

    /// <summary>
    /// Capture les pilotes actuellement installés
    /// </summary>
    public HashSet<DriverSnapshot> CaptureInstalledDrivers()
    {
        var drivers = new HashSet<DriverSnapshot>();

        try
        {
            // Pilotes via le registre
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (key != null)
            {
                foreach (var serviceName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var serviceKey = key.OpenSubKey(serviceName);
                        var type = (int)(serviceKey?.GetValue("Type") ?? 0);
                        
                        // Types de pilotes: 1=Kernel, 2=FileSystem, 8=Recognizer
                        if (type is 1 or 2 or 8)
                        {
                            var imagePath = serviceKey?.GetValue("ImagePath")?.ToString();
                            var description = serviceKey?.GetValue("Description")?.ToString();

                            drivers.Add(new DriverSnapshot
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
        }
        catch { }

        return drivers;
    }

    /// <summary>
    /// Compare les pilotes et détecte les nouveaux
    /// </summary>
    public List<SystemChange> CompareDrivers(
        HashSet<DriverSnapshot> before, 
        HashSet<DriverSnapshot> after)
    {
        var changes = new List<SystemChange>();

        foreach (var driver in after.Except(before))
        {
            changes.Add(new SystemChange
            {
                ChangeType = ChangeType.Created,
                Category = SystemChangeCategory.Service, // Utiliser Service pour les pilotes
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
                Category = SystemChangeCategory.Service,
                Path = driver.Name,
                OldValue = driver.ImagePath,
                Description = $"Pilote supprimé: {driver.Description ?? driver.Name}"
            });
        }

        return changes;
    }

    #endregion

    #region COM Objects Detection

    /// <summary>
    /// Capture les objets COM enregistrés
    /// </summary>
    public HashSet<ComObjectSnapshot> CaptureCOMObjects()
    {
        var comObjects = new HashSet<ComObjectSnapshot>();

        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey("CLSID");
            if (key == null) return comObjects;

            foreach (var clsid in key.GetSubKeyNames())
            {
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

                    if (!string.IsNullOrEmpty(serverPath))
                    {
                        comObjects.Add(new ComObjectSnapshot
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

        return comObjects;
    }

    /// <summary>
    /// Compare les objets COM et détecte les nouveaux
    /// </summary>
    public List<SystemChange> CompareCOMObjects(
        HashSet<ComObjectSnapshot> before,
        HashSet<ComObjectSnapshot> after)
    {
        var changes = new List<SystemChange>();

        foreach (var com in after.Except(before))
        {
            changes.Add(new SystemChange
            {
                ChangeType = ChangeType.Created,
                Category = SystemChangeCategory.RegistryKey,
                Path = $"CLSID\\{com.CLSID}",
                NewValue = com.ServerPath,
                Description = $"Objet COM: {com.Name ?? com.CLSID}"
            });
        }

        return changes;
    }

    #endregion

    #region File Associations Detection

    /// <summary>
    /// Capture les associations de fichiers
    /// </summary>
    public Dictionary<string, FileAssociationSnapshot> CaptureFileAssociations()
    {
        var associations = new Dictionary<string, FileAssociationSnapshot>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FileExts");
            
            if (key == null) return associations;

            foreach (var ext in key.GetSubKeyNames())
            {
                try
                {
                    using var extKey = key.OpenSubKey(ext);
                    using var choiceKey = extKey?.OpenSubKey("UserChoice");
                    
                    var progId = choiceKey?.GetValue("ProgId")?.ToString();
                    if (!string.IsNullOrEmpty(progId))
                    {
                        associations[ext] = new FileAssociationSnapshot
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

        return associations;
    }

    /// <summary>
    /// Compare les associations de fichiers
    /// </summary>
    public List<SystemChange> CompareFileAssociations(
        Dictionary<string, FileAssociationSnapshot> before,
        Dictionary<string, FileAssociationSnapshot> after)
    {
        var changes = new List<SystemChange>();

        // Nouvelles associations
        foreach (var (ext, assoc) in after)
        {
            if (!before.ContainsKey(ext))
            {
                changes.Add(new SystemChange
                {
                    ChangeType = ChangeType.Created,
                    Category = SystemChangeCategory.RegistryValue,
                    Path = $"FileExt\\{ext}",
                    NewValue = assoc.ProgId,
                    Description = $"Association créée: {ext} → {assoc.ProgId}"
                });
            }
            else if (before[ext].ProgId != assoc.ProgId)
            {
                changes.Add(new SystemChange
                {
                    ChangeType = ChangeType.Modified,
                    Category = SystemChangeCategory.RegistryValue,
                    Path = $"FileExt\\{ext}",
                    OldValue = before[ext].ProgId,
                    NewValue = assoc.ProgId,
                    Description = $"Association modifiée: {ext}"
                });
            }
        }

        return changes;
    }

    #endregion

    #region Helpers

    private static string GetRootName(RegistryKey root)
    {
        if (root == Registry.LocalMachine) return "HKLM";
        if (root == Registry.CurrentUser) return "HKCU";
        if (root == Registry.ClassesRoot) return "HKCR";
        return "HKEY";
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        StopRegistryWatching();
        StopProcessMonitoring();
        _isDisposed = true;

        GC.SuppressFinalize(this);
    }

    #endregion
}

#region Supporting Types

/// <summary>
/// Information sur un processus d'installation
/// </summary>
public class ProcessInfo
{
    public int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public string? FilePath { get; init; }
    public string? CommandLine { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime? ExitTime { get; set; }
    public int? ExitCode { get; set; }
    
    public TimeSpan Duration => (ExitTime ?? DateTime.Now) - StartTime;
}

/// <summary>
/// Arguments pour les changements de registre
/// </summary>
public class RegistryChangeEventArgs : EventArgs
{
    public required RegistryKey Root { get; init; }
    public required string SubPath { get; init; }
    public string FullPath => $"{GetRootName(Root)}\\{SubPath}";

    private static string GetRootName(RegistryKey root)
    {
        if (root == Registry.LocalMachine) return "HKLM";
        if (root == Registry.CurrentUser) return "HKCU";
        if (root == Registry.ClassesRoot) return "HKCR";
        return "HKEY";
    }
}

#endregion
