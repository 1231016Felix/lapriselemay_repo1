using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using WallpaperManager.Models;

namespace WallpaperManager.Services;

public class DuplicateGroup
{
    public required string Hash { get; init; }
    public required ObservableCollection<Wallpaper> Wallpapers { get; init; }
    public long FileSize => Wallpapers.FirstOrDefault()?.FileSize ?? 0;
    public string Resolution => Wallpapers.FirstOrDefault()?.Resolution ?? "Inconnu";
}

public static class DuplicateDetectionService
{
    /// <summary>
    /// Détecte les doublons en utilisant une approche hybride :
    /// 1. Groupe par taille + dimensions (rapide)
    /// 2. Calcule le hash MD5 uniquement sur les candidats potentiels
    /// </summary>
    public static async Task<List<DuplicateGroup>> FindDuplicatesAsync(
        IEnumerable<Wallpaper> wallpapers,
        IProgress<(int current, int total, string status)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var wallpaperList = wallpapers.Where(w => w.Exists).ToList();
        var duplicateGroups = new List<DuplicateGroup>();
        
        if (wallpaperList.Count < 2)
            return duplicateGroups;
        
        // Étape 1: Grouper par taille + dimensions (pré-filtrage rapide)
        progress?.Report((0, wallpaperList.Count, "Analyse des métadonnées..."));
        
        var candidates = wallpaperList
            .GroupBy(w => (w.FileSize, w.Width, w.Height))
            .Where(g => g.Count() > 1)
            .SelectMany(g => g)
            .ToList();
        
        if (candidates.Count < 2)
        {
            progress?.Report((wallpaperList.Count, wallpaperList.Count, "Aucun doublon potentiel"));
            return duplicateGroups;
        }
        
        // Étape 2: Calculer les hash MD5 uniquement sur les candidats
        progress?.Report((0, candidates.Count, $"Vérification de {candidates.Count} fichiers..."));
        
        var hashDict = new Dictionary<string, List<Wallpaper>>();
        var processed = 0;
        
        foreach (var wallpaper in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                var hash = wallpaper.FileHash ?? await ComputeFileHashAsync(wallpaper.FilePath, cancellationToken);
                
                // Sauvegarder le hash pour éviter de recalculer
                if (wallpaper.FileHash == null)
                {
                    wallpaper.FileHash = hash;
                }
                
                if (!hashDict.TryGetValue(hash, out var list))
                {
                    list = [];
                    hashDict[hash] = list;
                }
                list.Add(wallpaper);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur hash {wallpaper.FilePath}: {ex.Message}");
            }
            
            processed++;
            progress?.Report((processed, candidates.Count, $"Analyse: {processed}/{candidates.Count}"));
        }
        
        // Étape 3: Retourner uniquement les vrais doublons (même hash)
        duplicateGroups = hashDict
            .Where(kvp => kvp.Value.Count > 1)
            .Select(kvp => new DuplicateGroup
            {
                Hash = kvp.Key,
                Wallpapers = new ObservableCollection<Wallpaper>(kvp.Value.OrderBy(w => w.AddedDate))
            })
            .OrderByDescending(g => g.FileSize)
            .ToList();
        
        var totalDuplicates = duplicateGroups.Sum(g => g.Wallpapers.Count - 1);
        progress?.Report((candidates.Count, candidates.Count, 
            $"Terminé: {duplicateGroups.Count} groupe(s), {totalDuplicates} doublon(s)"));
        
        return duplicateGroups;
    }
    
    /// <summary>
    /// Calcule le hash MD5 d'un fichier de manière asynchrone
    /// </summary>
    private static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath, 
            FileMode.Open, 
            FileAccess.Read, 
            FileShare.Read, 
            bufferSize: 81920, 
            useAsync: true);
        
        var hashBytes = await MD5.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes);
    }
    
    /// <summary>
    /// Calcule l'espace disque récupérable en supprimant les doublons
    /// </summary>
    public static long CalculateRecoverableSpace(IEnumerable<DuplicateGroup> groups)
    {
        return groups.Sum(g => g.FileSize * (g.Wallpapers.Count - 1));
    }
    
    /// <summary>
    /// Formate la taille en bytes vers une chaîne lisible
    /// </summary>
    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        
        ReadOnlySpan<string> sizes = ["B", "KB", "MB", "GB"];
        var order = 0;
        var size = (double)bytes;
        
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        
        return $"{size:0.##} {sizes[order]}";
    }
}
