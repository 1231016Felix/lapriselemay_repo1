using System.IO;
using TempCleaner.Models;

namespace TempCleaner.Services;

public class CleanerService
{
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

    private async Task<(bool Success, string ErrorMessage)> DeleteFileAsync(TempFileInfo file)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (File.Exists(file.FullPath))
                {
                    File.SetAttributes(file.FullPath, FileAttributes.Normal);
                    File.Delete(file.FullPath);
                }
                return (true, string.Empty);
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
