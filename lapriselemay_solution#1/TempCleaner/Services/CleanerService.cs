using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using TempCleaner.Helpers;
using TempCleaner.Models;

namespace TempCleaner.Services;

/// <summary>
/// Service de nettoyage des fichiers temporaires avec circuit breaker intégré.
/// Le circuit breaker évite les tentatives répétées sur des fichiers bloqués.
/// </summary>
public class CleanerService
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool DeleteFile(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint SetFileAttributes(string lpFileName, uint dwFileAttributes);

    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    
    /// <summary>
    /// Nombre maximum de tentatives avant de marquer un fichier comme bloqué
    /// </summary>
    private const int MaxFailedAttempts = 3;
    
    /// <summary>
    /// Cache des fichiers qui ont échoué (circuit breaker)
    /// Clé: chemin du fichier, Valeur: nombre d'échecs
    /// </summary>
    private static readonly ConcurrentDictionary<string, int> _failedAttempts = new(StringComparer.OrdinalIgnoreCase);
    
    /// <summary>
    /// Durée pendant laquelle un fichier bloqué est ignoré
    /// </summary>
    private static readonly TimeSpan _circuitBreakerTimeout = TimeSpan.FromMinutes(30);
    
    /// <summary>
    /// Timestamp de la dernière réinitialisation du circuit breaker
    /// </summary>
    private static DateTime _lastCircuitBreakerReset = DateTime.UtcNow;

    /// <summary>
    /// Réinitialise le circuit breaker (à appeler lors d'un nouveau scan)
    /// </summary>
    public static void ResetCircuitBreaker()
    {
        // Réinitialiser seulement si le timeout est dépassé
        if (DateTime.UtcNow - _lastCircuitBreakerReset > _circuitBreakerTimeout)
        {
            _failedAttempts.Clear();
            _lastCircuitBreakerReset = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Nettoie les fichiers sélectionnés de manière asynchrone.
    /// </summary>
    public async Task<CleanResult> CleanAsync(
        IEnumerable<TempFileInfo> files,
        IProgress<(string message, int percent, long freedBytes)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new CleanResult();
        var fileList = files.Where(f => f.IsSelected && f.IsAccessible).ToList();
        int processed = 0;
        long freedBytes = 0;

        foreach (var file in fileList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var percent = fileList.Count > 0 ? (int)((double)processed / fileList.Count * 100) : 0;
            progress?.Report(($"Suppression: {file.FileName}", percent, freedBytes));

            // Vérifier le circuit breaker avant de tenter la suppression
            if (IsCircuitOpen(file.FullPath))
            {
                result.FailedCount++;
                result.Errors.Add(new CleanError
                {
                    FilePath = file.FullPath,
                    ErrorMessage = "Fichier ignoré (échecs répétés précédents)"
                });
                processed++;
                continue;
            }

            var deleteResult = await DeleteFileAsync(file);

            if (deleteResult.Success)
            {
                result.DeletedCount++;
                result.FreedBytes += file.Size;
                freedBytes += file.Size;
                
                // Succès - retirer du circuit breaker si présent
                _failedAttempts.TryRemove(file.FullPath, out _);
            }
            else
            {
                result.FailedCount++;
                result.Errors.Add(new CleanError
                {
                    FilePath = file.FullPath,
                    ErrorMessage = deleteResult.ErrorMessage
                });
                
                // Enregistrer l'échec dans le circuit breaker
                RecordFailure(file.FullPath);
            }

            processed++;
        }

        progress?.Report(("Nettoyage terminé", 100, freedBytes));
        return result;
    }

    /// <summary>
    /// Vérifie si le circuit breaker est ouvert pour un fichier (trop d'échecs)
    /// </summary>
    private static bool IsCircuitOpen(string filePath)
    {
        return _failedAttempts.TryGetValue(filePath, out var attempts) && attempts >= MaxFailedAttempts;
    }

    /// <summary>
    /// Enregistre un échec pour un fichier
    /// </summary>
    private static void RecordFailure(string filePath)
    {
        _failedAttempts.AddOrUpdate(filePath, 1, (_, count) => count + 1);
    }

    /// <summary>
    /// Supprime un fichier de manière asynchrone avec plusieurs stratégies.
    /// </summary>
    private static async Task<(bool Success, string ErrorMessage)> DeleteFileAsync(TempFileInfo file)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(file.FullPath))
                    return (true, string.Empty);

                // Stratégie 1: Retirer les attributs de protection
                try
                {
                    SetFileAttributes(file.FullPath, FILE_ATTRIBUTE_NORMAL);
                    File.SetAttributes(file.FullPath, FileAttributes.Normal);
                }
                catch { /* Ignorer les erreurs d'attributs */ }

                // Stratégie 2: Suppression standard .NET
                try
                {
                    File.Delete(file.FullPath);
                    return (true, string.Empty);
                }
                catch
                {
                    // Stratégie 3: Fallback avec Win32 API
                    SetFileAttributes(file.FullPath, FILE_ATTRIBUTE_NORMAL);
                    if (DeleteFile(file.FullPath))
                        return (true, string.Empty);
                }

                return (false, "Impossible de supprimer le fichier");
            }
            catch (UnauthorizedAccessException)
            {
                return (false, "Accès refusé");
            }
            catch (IOException ex)
            {
                return (false, $"Fichier en cours d'utilisation: {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        });
    }
}

/// <summary>
/// Résultat d'une opération de nettoyage.
/// </summary>
public class CleanResult
{
    public int DeletedCount { get; set; }
    public int FailedCount { get; set; }
    public long FreedBytes { get; set; }
    public List<CleanError> Errors { get; set; } = [];

    public string FreedBytesFormatted => FileSizeHelper.Format(FreedBytes);
    
    /// <summary>
    /// Nombre de fichiers ignorés par le circuit breaker
    /// </summary>
    public int SkippedByCircuitBreaker => Errors.Count(e => e.ErrorMessage.Contains("échecs répétés"));
}

/// <summary>
/// Erreur lors du nettoyage d'un fichier.
/// </summary>
public class CleanError
{
    public string FilePath { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}
