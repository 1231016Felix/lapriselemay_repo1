using CleanUninstaller.Models;
using CleanUninstaller.Services.Interfaces;
using Shared.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace CleanUninstaller.Services;

/// <summary>
/// Service pour gérer les paramètres de l'application
/// </summary>
public class SettingsService : ISettingsService
{
    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CleanUninstaller");
    
    private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");
    private static readonly string BackupsFolder = Path.Combine(SettingsFolder, "Backups");

    private AppSettings _settings = new();
    private readonly Shared.Logging.ILoggerService _logger;

    /// <summary>
    /// Paramètres actuels
    /// </summary>
    public AppSettings Settings => _settings;

    public SettingsService(Shared.Logging.ILoggerService logger)
    {
        _logger = logger;
        Load();
    }

    // Constructeur sans paramètre pour compatibilité
    public SettingsService() : this(ServiceContainer.TryGetService<Shared.Logging.ILoggerService>() ?? new LoggerService())
    { }

    /// <summary>
    /// Charge les paramètres (synchrone) - Implémente ISettingsService
    /// </summary>
    public void Load()
    {
        try
        {
            EnsureDirectoriesExist();
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                _logger.Info("Paramètres chargés avec succès");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Erreur chargement settings: {ex.Message}");
            _settings = new AppSettings();
        }
    }

    /// <summary>
    /// Sauvegarde les paramètres (synchrone) - Implémente ISettingsService
    /// </summary>
    public void Save()
    {
        try
        {
            EnsureDirectoriesExist();
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
            _logger.Info("Paramètres sauvegardés");
        }
        catch (Exception ex)
        {
            _logger.Error($"Erreur sauvegarde settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Charge les paramètres depuis le fichier (asynchrone)
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = await File.ReadAllTextAsync(SettingsFile);
                _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur chargement settings: {ex.Message}");
            _settings = new AppSettings();
        }

        // S'assurer que les dossiers existent
        EnsureDirectoriesExist();
    }

    /// <summary>
    /// Sauvegarde les paramètres dans le fichier
    /// </summary>
    public async Task SaveAsync()
    {
        try
        {
            EnsureDirectoriesExist();

            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            await File.WriteAllTextAsync(SettingsFile, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur sauvegarde settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Réinitialise les paramètres par défaut
    /// </summary>
    public async Task ResetToDefaultsAsync()
    {
        _settings = new AppSettings();
        await SaveAsync();
    }

    /// <summary>
    /// Obtient le chemin du dossier de backups
    /// </summary>
    public string GetBackupsFolder()
    {
        EnsureDirectoriesExist();
        return BackupsFolder;
    }

    /// <summary>
    /// Crée une sauvegarde du registre avant nettoyage
    /// </summary>
    public async Task<string?> CreateRegistryBackupAsync(string programName)
    {
        if (!_settings.CreateRegistryBackup) return null;

        try
        {
            EnsureDirectoriesExist();

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var safeProgName = string.Join("_", programName.Split(Path.GetInvalidFileNameChars()));
            var backupFile = Path.Combine(BackupsFolder, $"registry_{safeProgName}_{timestamp}.reg");

            // Exporter les clés de registre pertinentes
            var keysToBackup = new[]
            {
                @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var key in keysToBackup)
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "reg.exe",
                    Arguments = $"export \"{key}\" \"{backupFile}\" /y",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                await process!.WaitForExitAsync();
            }

            return File.Exists(backupFile) ? backupFile : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur backup registre: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Liste les backups disponibles
    /// </summary>
    public IEnumerable<BackupInfo> GetAvailableBackups()
    {
        if (!Directory.Exists(BackupsFolder))
            return [];

        return Directory.EnumerateFiles(BackupsFolder, "*.reg")
            .Select(file => new BackupInfo
            {
                FilePath = file,
                FileName = Path.GetFileName(file),
                CreatedAt = File.GetCreationTime(file),
                Size = new FileInfo(file).Length
            })
            .OrderByDescending(b => b.CreatedAt);
    }

    /// <summary>
    /// Restaure une sauvegarde de registre
    /// </summary>
    public async Task<bool> RestoreBackupAsync(string backupFile)
    {
        try
        {
            if (!File.Exists(backupFile)) return false;

            var startInfo = new ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = $"import \"{backupFile}\"",
                UseShellExecute = true,
                Verb = "runas"
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur restauration backup: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Supprime les anciens backups (garde les 10 plus récents)
    /// </summary>
    public void CleanupOldBackups(int keepCount = 10)
    {
        try
        {
            var backups = GetAvailableBackups().ToList();
            var toDelete = backups.Skip(keepCount);

            foreach (var backup in toDelete)
            {
                try
                {
                    File.Delete(backup.FilePath);
                }
                catch { /* Ignorer */ }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur nettoyage backups: {ex.Message}");
        }
    }

    private void EnsureDirectoriesExist()
    {
        if (!Directory.Exists(SettingsFolder))
            Directory.CreateDirectory(SettingsFolder);

        if (!Directory.Exists(BackupsFolder))
            Directory.CreateDirectory(BackupsFolder);
    }
}

/// <summary>
/// Information sur une sauvegarde
/// </summary>
public class BackupInfo
{
    public string FilePath { get; init; } = "";
    public string FileName { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public long Size { get; init; }

    public string FormattedSize => Size switch
    {
        < 1024 => $"{Size} o",
        < 1024 * 1024 => $"{Size / 1024.0:N1} Ko",
        _ => $"{Size / (1024.0 * 1024):N1} Mo"
    };
}
