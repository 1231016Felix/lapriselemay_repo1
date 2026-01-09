using System.IO.Compression;
using System.Text.Json;
using CleanUninstaller.Models;
using Microsoft.Win32;

namespace CleanUninstaller.Services;

/// <summary>
/// Service de sauvegarde pour permettre la restauration après une désinstallation
/// </summary>
public class BackupService
{
    private readonly string _backupFolder;

    public BackupService()
    {
        _backupFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanUninstaller",
            "Backups");
        
        Directory.CreateDirectory(_backupFolder);
    }

    /// <summary>
    /// Crée une sauvegarde complète avant désinstallation
    /// </summary>
    public async Task<UninstallBackup> CreateBackupAsync(
        MonitoredInstallation installation,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var backup = new UninstallBackup
        {
            InstallationId = installation.Id,
            InstallationName = installation.Name,
            BackupFolder = Path.Combine(_backupFolder, $"{installation.Id}_{DateTime.Now:yyyyMMdd_HHmmss}")
        };

        Directory.CreateDirectory(backup.BackupFolder);

        var changes = installation.Changes
            .Where(c => c.IsSelected && c.ChangeType == ChangeType.Created)
            .ToList();

        var total = changes.Count;
        var processed = 0;

        foreach (var change in changes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                progress?.Report(new ScanProgress(
                    (processed * 100) / total,
                    $"Sauvegarde de {change.DisplayPath}..."));

                var backupItem = await BackupItemAsync(change, backup.BackupFolder);
                if (backupItem != null)
                {
                    backup.BackedUpItems.Add(backupItem);
                }
            }
            catch (Exception ex)
            {
                backup.Errors.Add($"{change.Path}: {ex.Message}");
            }

            processed++;
        }

        // Sauvegarder les métadonnées
        var metadataPath = Path.Combine(backup.BackupFolder, "backup_metadata.json");
        var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, json, cancellationToken);

        progress?.Report(new ScanProgress(100, 
            $"Sauvegarde terminée: {backup.BackedUpItems.Count} éléments"));

        return backup;
    }

    private async Task<BackupItem?> BackupItemAsync(SystemChange change, string backupFolder)
    {
        var item = new BackupItem
        {
            OriginalPath = change.Path,
            Category = change.Category
        };

        switch (change.Category)
        {
            case SystemChangeCategory.File:
                if (File.Exists(change.Path))
                {
                    var relativePath = GetSafeRelativePath(change.Path);
                    item.BackupPath = Path.Combine(backupFolder, "Files", relativePath);
                    
                    Directory.CreateDirectory(Path.GetDirectoryName(item.BackupPath)!);
                    File.Copy(change.Path, item.BackupPath, overwrite: true);
                    item.Success = true;
                }
                break;

            case SystemChangeCategory.Folder:
                // Ne pas sauvegarder les dossiers vides, ils seront recréés au besoin
                item.Success = true;
                break;

            case SystemChangeCategory.RegistryKey:
            case SystemChangeCategory.RegistryValue:
                item.BackupPath = Path.Combine(backupFolder, "Registry", 
                    $"{Guid.NewGuid()}.reg");
                Directory.CreateDirectory(Path.GetDirectoryName(item.BackupPath)!);
                item.Success = await ExportRegistryKeyAsync(change.Path, item.BackupPath);
                break;

            case SystemChangeCategory.Service:
                item.BackupPath = Path.Combine(backupFolder, "Services", 
                    $"{change.Path}.json");
                Directory.CreateDirectory(Path.GetDirectoryName(item.BackupPath)!);
                item.Success = await ExportServiceConfigAsync(change.Path, item.BackupPath);
                break;

            case SystemChangeCategory.ScheduledTask:
                item.BackupPath = Path.Combine(backupFolder, "Tasks", 
                    $"{GetSafeFileName(change.Path)}.xml");
                Directory.CreateDirectory(Path.GetDirectoryName(item.BackupPath)!);
                item.Success = await ExportScheduledTaskAsync(change.Path, item.BackupPath);
                break;

            default:
                // Pour les autres types, stocker juste les métadonnées
                item.Metadata = JsonSerializer.Serialize(new
                {
                    change.Path,
                    change.NewValue,
                    change.OldValue,
                    change.Description
                });
                item.Success = true;
                break;
        }

        return item.Success ? item : null;
    }

    private static string GetSafeRelativePath(string fullPath)
    {
        // Convertir C:\Program Files\App\file.exe en Program Files\App\file.exe
        var root = Path.GetPathRoot(fullPath);
        if (!string.IsNullOrEmpty(root))
        {
            var driveLetter = root.TrimEnd('\\', ':');
            var relativePart = fullPath[root.Length..];
            return Path.Combine(driveLetter, relativePart);
        }
        return fullPath;
    }

    private static string GetSafeFileName(string path)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", path.Split(invalid));
    }

    private static async Task<bool> ExportRegistryKeyAsync(string keyPath, string outputPath)
    {
        try
        {
            var (root, subPath) = ParseRegistryPath(keyPath);
            if (root == null) return false;

            using var key = root.OpenSubKey(subPath);
            if (key == null) return false;

            var lines = new List<string>
            {
                "Windows Registry Editor Version 5.00",
                "",
                $"[{keyPath}]"
            };

            // Exporter les valeurs
            foreach (var valueName in key.GetValueNames())
            {
                var value = key.GetValue(valueName);
                var kind = key.GetValueKind(valueName);
                var formattedValue = FormatRegistryValue(value, kind);
                
                var name = string.IsNullOrEmpty(valueName) ? "@" : $"\"{valueName}\"";
                lines.Add($"{name}={formattedValue}");
            }

            await File.WriteAllLinesAsync(outputPath, lines);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatRegistryValue(object? value, RegistryValueKind kind)
    {
        return kind switch
        {
            RegistryValueKind.String => $"\"{value}\"",
            RegistryValueKind.DWord => $"dword:{(int)(value ?? 0):x8}",
            RegistryValueKind.QWord => $"qword:{(long)(value ?? 0):x16}",
            RegistryValueKind.Binary when value is byte[] bytes => 
                $"hex:{string.Join(",", bytes.Select(b => b.ToString("x2")))}",
            RegistryValueKind.MultiString when value is string[] strings =>
                $"hex(7):{string.Join(",", strings.SelectMany(s => 
                    System.Text.Encoding.Unicode.GetBytes(s + "\0")).Select(b => b.ToString("x2")))}",
            RegistryValueKind.ExpandString => $"hex(2):{string.Join(",", 
                System.Text.Encoding.Unicode.GetBytes(value?.ToString() ?? "").Select(b => b.ToString("x2")))}",
            _ => $"\"{value}\""
        };
    }

    private static async Task<bool> ExportServiceConfigAsync(string serviceName, string outputPath)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            
            if (key == null) return false;

            var config = new Dictionary<string, object?>
            {
                ["Name"] = serviceName,
                ["DisplayName"] = key.GetValue("DisplayName"),
                ["Description"] = key.GetValue("Description"),
                ["ImagePath"] = key.GetValue("ImagePath"),
                ["Start"] = key.GetValue("Start"),
                ["Type"] = key.GetValue("Type"),
                ["ObjectName"] = key.GetValue("ObjectName"),
                ["ErrorControl"] = key.GetValue("ErrorControl")
            };

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(outputPath, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> ExportScheduledTaskAsync(string taskPath, string outputPath)
    {
        try
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Query /TN \"{taskPath}\" /XML",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var xml = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(xml))
            {
                await File.WriteAllTextAsync(outputPath, xml);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Restaure une sauvegarde
    /// </summary>
    public async Task<RestoreResult> RestoreBackupAsync(
        UninstallBackup backup,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new RestoreResult();
        var total = backup.BackedUpItems.Count;
        var processed = 0;

        foreach (var item in backup.BackedUpItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                progress?.Report(new ScanProgress(
                    (processed * 100) / total,
                    $"Restauration de {Path.GetFileName(item.OriginalPath)}..."));

                var success = await RestoreItemAsync(item);
                if (success)
                {
                    result.RestoredCount++;
                }
                else
                {
                    result.FailedCount++;
                }
            }
            catch (Exception ex)
            {
                result.FailedCount++;
                result.Errors.Add($"{item.OriginalPath}: {ex.Message}");
            }

            processed++;
        }

        progress?.Report(new ScanProgress(100,
            $"Restauration terminée: {result.RestoredCount} restaurés, {result.FailedCount} échecs"));

        return result;
    }

    private static async Task<bool> RestoreItemAsync(BackupItem item)
    {
        if (string.IsNullOrEmpty(item.BackupPath)) return false;

        switch (item.Category)
        {
            case SystemChangeCategory.File:
                if (File.Exists(item.BackupPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(item.OriginalPath)!);
                    File.Copy(item.BackupPath, item.OriginalPath, overwrite: true);
                    return true;
                }
                break;

            case SystemChangeCategory.RegistryKey:
            case SystemChangeCategory.RegistryValue:
                if (File.Exists(item.BackupPath))
                {
                    // Importer le fichier .reg
                    var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "reg.exe",
                        Arguments = $"import \"{item.BackupPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    await process!.WaitForExitAsync();
                    return process.ExitCode == 0;
                }
                break;

            case SystemChangeCategory.ScheduledTask:
                if (File.Exists(item.BackupPath))
                {
                    var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/Create /XML \"{item.BackupPath}\" /TN \"{item.OriginalPath}\" /F",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    await process!.WaitForExitAsync();
                    return process.ExitCode == 0;
                }
                break;
        }

        return false;
    }

    /// <summary>
    /// Liste les sauvegardes disponibles
    /// </summary>
    public async Task<List<UninstallBackup>> GetBackupsAsync()
    {
        var backups = new List<UninstallBackup>();

        if (!Directory.Exists(_backupFolder)) return backups;

        foreach (var dir in Directory.GetDirectories(_backupFolder))
        {
            var metadataPath = Path.Combine(dir, "backup_metadata.json");
            if (File.Exists(metadataPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(metadataPath);
                    var backup = JsonSerializer.Deserialize<UninstallBackup>(json);
                    if (backup != null)
                    {
                        backups.Add(backup);
                    }
                }
                catch { }
            }
        }

        return backups.OrderByDescending(b => b.CreatedAt).ToList();
    }

    /// <summary>
    /// Supprime une sauvegarde
    /// </summary>
    public void DeleteBackup(UninstallBackup backup)
    {
        if (Directory.Exists(backup.BackupFolder))
        {
            Directory.Delete(backup.BackupFolder, recursive: true);
        }
    }

    /// <summary>
    /// Calcule la taille totale d'une sauvegarde
    /// </summary>
    public long GetBackupSize(UninstallBackup backup)
    {
        if (!Directory.Exists(backup.BackupFolder)) return 0;

        return Directory.GetFiles(backup.BackupFolder, "*", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);
    }

    private static (RegistryKey? Root, string SubPath) ParseRegistryPath(string path)
    {
        if (path.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase))
            return (Registry.LocalMachine, path[5..]);
        if (path.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
            return (Registry.CurrentUser, path[5..]);
        if (path.StartsWith("HKCR\\", StringComparison.OrdinalIgnoreCase))
            return (Registry.ClassesRoot, path[5..]);
        return (null, "");
    }
}

#region Models

/// <summary>
/// Représente une sauvegarde avant désinstallation
/// </summary>
public class UninstallBackup
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public required string InstallationId { get; init; }
    public required string InstallationName { get; init; }
    public required string BackupFolder { get; set; }
    public List<BackupItem> BackedUpItems { get; init; } = [];
    public List<string> Errors { get; init; } = [];
}

/// <summary>
/// Élément sauvegardé
/// </summary>
public class BackupItem
{
    public required string OriginalPath { get; init; }
    public string? BackupPath { get; set; }
    public SystemChangeCategory Category { get; init; }
    public string? Metadata { get; set; }
    public bool Success { get; set; }
}

/// <summary>
/// Résultat d'une restauration
/// </summary>
public class RestoreResult
{
    public int RestoredCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> Errors { get; init; } = [];
}

#endregion
