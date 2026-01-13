using System.Collections.Concurrent;
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
        var startTime = DateTime.Now;
        var profileList = profiles.Where(p => p.IsEnabled).ToList();

        if (profileList.Count == 0)
        {
            progress?.Report(("Aucun profil actif", 100));
            return result;
        }

        progress?.Report(("Démarrage de l'analyse parallèle...", 0));

        // Utiliser ConcurrentBag pour collecter les résultats de manière thread-safe
        var allFiles = new ConcurrentBag<TempFileInfo>();
        var categoryStats = new ConcurrentDictionary<string, CategoryStats>();
        var completedCount = 0;
        var progressLock = new object();

        // Limiter la concurrence pour éviter de surcharger le disque
        using var semaphore = new SemaphoreSlim(Math.Min(Environment.ProcessorCount, 4));

        var tasks = profileList.Select(async profile =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var profileFiles = await ScanProfileAsync(profile, cancellationToken);

                profile.FileCount = profileFiles.Count;
                profile.TotalSize = profileFiles.Sum(f => f.Size);

                // Ajouter les fichiers à la collection thread-safe
                foreach (var file in profileFiles)
                {
                    allFiles.Add(file);
                }

                // Mettre à jour les stats de catégorie
                var stats = categoryStats.GetOrAdd(profile.Name, _ => new CategoryStats 
                { 
                    Name = profile.Name, 
                    Icon = profile.Icon 
                });
                
                lock (stats)
                {
                    stats.FileCount += profileFiles.Count;
                    stats.TotalSize += profileFiles.Sum(f => f.Size);
                }

                // Mise à jour de la progression
                lock (progressLock)
                {
                    completedCount++;
                    var percent = completedCount * 100 / profileList.Count;
                    progress?.Report(($"Analysé: {profile.Name} ({completedCount}/{profileList.Count})", percent));
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        // Construire le résultat final
        var files = allFiles.ToList();
        result.Files = files;
        result.TotalSize = files.Sum(f => f.Size);
        result.TotalCount = files.Count;
        result.AccessDeniedCount = files.Count(f => !f.IsAccessible);
        result.ScanDuration = DateTime.Now - startTime;
        
        foreach (var kvp in categoryStats)
        {
            result.CategoryStats[kvp.Key] = kvp.Value;
        }

        progress?.Report(("Analyse terminée", 100));

        return result;
    }

    private static async Task<List<TempFileInfo>> ScanProfileAsync(
        CleanerProfile profile,
        CancellationToken cancellationToken)
    {
        var files = new List<TempFileInfo>();

        await Task.Run(() =>
        {
            if (!Directory.Exists(profile.FolderPath))
                return;

            var minDate = profile.MinAgeDays > 0
                ? DateTime.Now.AddDays(-profile.MinAgeDays)
                : DateTime.MaxValue;

            var enumOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = profile.IncludeSubdirectories,
                AttributesToSkip = FileAttributes.System
            };

            try
            {
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
                        files.Add(CreateInaccessibleFileInfo(filePath, profile.Name, "Accès refusé"));
                    }
                    catch (IOException ex)
                    {
                        files.Add(CreateInaccessibleFileInfo(filePath, profile.Name, ex.Message));
                    }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (DirectoryNotFoundException) { }

        }, cancellationToken);

        return files;
    }

    private static TempFileInfo CreateInaccessibleFileInfo(string filePath, string category, string error)
    {
        return new TempFileInfo
        {
            FullPath = filePath,
            FileName = Path.GetFileName(filePath),
            Category = category,
            IsAccessible = false,
            ErrorMessage = error
        };
    }
}
