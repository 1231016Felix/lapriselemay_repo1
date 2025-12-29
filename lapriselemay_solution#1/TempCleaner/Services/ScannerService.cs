using System.IO;
using TempCleaner.Models;

namespace TempCleaner.Services;

public class ScannerService
{
    public async Task<ScanResult> ScanAsync(
        IEnumerable<CleanerProfile> profiles,
        IProgress<(string message, int percent)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ScanResult();
        var files = new List<TempFileInfo>();
        var startTime = DateTime.Now;
        var profileList = profiles.Where(p => p.IsEnabled).ToList();
        int profileIndex = 0;

        foreach (var profile in profileList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var percent = (int)((double)profileIndex / profileList.Count * 100);
            progress?.Report(($"Analyse: {profile.Name}...", percent));

            var profileFiles = await ScanProfileAsync(profile, cancellationToken);
            
            profile.FileCount = profileFiles.Count;
            profile.TotalSize = profileFiles.Sum(f => f.Size);

            files.AddRange(profileFiles);

            if (!result.CategoryStats.ContainsKey(profile.Name))
            {
                result.CategoryStats[profile.Name] = new CategoryStats
                {
                    Name = profile.Name,
                    Icon = profile.Icon
                };
            }

            result.CategoryStats[profile.Name].FileCount += profileFiles.Count;
            result.CategoryStats[profile.Name].TotalSize += profileFiles.Sum(f => f.Size);

            profileIndex++;
        }

        result.Files = files;
        result.TotalSize = files.Sum(f => f.Size);
        result.TotalCount = files.Count;
        result.AccessDeniedCount = files.Count(f => !f.IsAccessible);
        result.ScanDuration = DateTime.Now - startTime;

        progress?.Report(("Analyse terminée", 100));

        return result;
    }

    private async Task<List<TempFileInfo>> ScanProfileAsync(
        CleanerProfile profile,
        CancellationToken cancellationToken)
    {
        var files = new List<TempFileInfo>();

        await Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(profile.FolderPath))
                    return;

                var searchOption = profile.IncludeSubdirectories
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                var minDate = profile.MinAgeDays > 0
                    ? DateTime.Now.AddDays(-profile.MinAgeDays)
                    : DateTime.MaxValue;

                var enumOptions = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = profile.IncludeSubdirectories,
                    AttributesToSkip = FileAttributes.System
                };

                foreach (var filePath in Directory.EnumerateFiles(
                    profile.FolderPath, profile.SearchPattern, enumOptions))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var fileInfo = new FileInfo(filePath);

                        if (profile.MinAgeDays > 0 && fileInfo.LastWriteTime > minDate)
                            continue;

                        files.Add(new TempFileInfo
                        {
                            FullPath = filePath,
                            FileName = fileInfo.Name,
                            Size = fileInfo.Length,
                            LastModified = fileInfo.LastWriteTime,
                            Category = profile.Name,
                            IsAccessible = true
                        });
                    }
                    catch (UnauthorizedAccessException)
                    {
                        files.Add(new TempFileInfo
                        {
                            FullPath = filePath,
                            FileName = Path.GetFileName(filePath),
                            Category = profile.Name,
                            IsAccessible = false,
                            ErrorMessage = "Accès refusé"
                        });
                    }
                    catch (IOException ex)
                    {
                        files.Add(new TempFileInfo
                        {
                            FullPath = filePath,
                            FileName = Path.GetFileName(filePath),
                            Category = profile.Name,
                            IsAccessible = false,
                            ErrorMessage = ex.Message
                        });
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Ignorer les dossiers inaccessibles
            }
            catch (DirectoryNotFoundException)
            {
                // Le dossier n'existe pas
            }
        }, cancellationToken);

        return files;
    }
}
