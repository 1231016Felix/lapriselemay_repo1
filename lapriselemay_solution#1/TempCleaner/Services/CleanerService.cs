using System.IO;
using System.Runtime.InteropServices;
using TempCleaner.Models;

namespace TempCleaner.Services;

public class CleanerService
{
    // Win32 API pour suppression forcée (du C++)
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool DeleteFile(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint SetFileAttributes(string lpFileName, uint dwFileAttributes);

    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

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

            var percent = (int)((double)processed / fileList.Count * 100);
            progress?.Report(($"Suppression: {file.FileName}", percent, freedBytes));

            var deleteResult = await DeleteFileAsync(file);

            if (deleteResult.Success)
            {
                result.DeletedCount++;
                result.FreedBytes += file.Size;
                freedBytes += file.Size;
            }
            else
            {
                result.FailedCount++;
                result.Errors.Add(new CleanError
                {
                    FilePath = file.FullPath,
                    ErrorMessage = deleteResult.ErrorMessage
                });
            }

            processed++;
        }

        progress?.Report(("Nettoyage terminé", 100, freedBytes));
        return result;
    }

    /// <summary>
    /// Suppression améliorée avec fallback Win32 API (équivalent du C++)
    /// </summary>
    private async Task<(bool Success, string ErrorMessage)> DeleteFileAsync(TempFileInfo file)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(file.FullPath))
                    return (true, string.Empty);

                // Étape 1: Retirer les attributs de protection (du C++)
                try
                {
                    SetFileAttributes(file.FullPath, FILE_ATTRIBUTE_NORMAL);
                    File.SetAttributes(file.FullPath, FileAttributes.Normal);
                }
                catch { /* Ignorer les erreurs d'attributs */ }

                // Étape 2: Tentative de suppression standard
                try
                {
                    File.Delete(file.FullPath);
                    return (true, string.Empty);
                }
                catch
                {
                    // Étape 3: Fallback avec Win32 API (du C++)
                    SetFileAttributes(file.FullPath, FILE_ATTRIBUTE_NORMAL);
                    if (DeleteFile(file.FullPath))
                    {
                        return (true, string.Empty);
                    }
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

public class CleanResult
{
    public int DeletedCount { get; set; }
    public int FailedCount { get; set; }
    public long FreedBytes { get; set; }
    public List<CleanError> Errors { get; set; } = [];

    public string FreedBytesFormatted => FormatSize(FreedBytes);

    private static string FormatSize(long bytes)
    {
        if (bytes == 0) return "0 B";
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int suffixIndex = 0;
        double size = bytes;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return $"{size:N2} {suffixes[suffixIndex]}";
    }
}

public class CleanError
{
    public string FilePath { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}
