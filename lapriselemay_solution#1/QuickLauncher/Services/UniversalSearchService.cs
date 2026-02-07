using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Service de recherche universel qui fonctionne sans Windows Search.
/// Utilise Everything si disponible, sinon fait une recherche directe.
/// </summary>
public static class UniversalSearchService
{
    private const int MaxResults = 50;
    private static readonly string[] DefaultSearchPaths;
    private static readonly string[] ExcludedFolders = 
    [
        "node_modules", ".git", ".vs", "bin", "obj", "__pycache__",
        "AppData\\Local\\Temp", "Windows\\Temp", "$Recycle.Bin",
        "Windows\\WinSxS", "Windows\\assembly", ".nuget", "packages"
    ];

    // Cache pour les recherches directes
    private static readonly ConcurrentDictionary<string, CachedSearchResult> _searchCache = new();
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromSeconds(60);
    private const int MaxCacheEntries = 20;

    /// <summary>
    /// Profondeur maximale de recherche (configurable).
    /// </summary>
    public static int MaxSearchDepth { get; set; } = 5;

    /// <summary>
    /// Retourne la liste des dossiers par défaut scannés en mode recherche directe.
    /// </summary>
    public static string[] GetDefaultSearchPaths() => DefaultSearchPaths;

    static UniversalSearchService()
    {
        // Dossiers par défaut à rechercher
        DefaultSearchPaths =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            @"C:\Program Files",
            @"C:\Program Files (x86)"
        ];
    }

    /// <summary>
    /// Recherche des fichiers en utilisant la meilleure méthode disponible.
    /// </summary>
    public static async Task<List<SearchResult>> SearchAsync(
        string query,
        string? searchScope = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return [];

        // 1. Essayer Everything d'abord (le plus rapide)
        if (EverythingApi.IsAvailable())
        {
            try
            {
                return await Task.Run(() => SearchWithEverything(query, MaxResults), cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UniversalSearch] Everything failed: {ex.Message}");
            }
        }

        // 2. Essayer Windows Search (recherche dans tout l'index système)
        if (WindowsSearchService.IsAvailable())
        {
            try
            {
                var results = await WindowsSearchService.SearchAsync(query, searchScope, cancellationToken);
                if (results.Count > 0)
                    return results;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UniversalSearch] Windows Search failed: {ex.Message}");
            }
        }

        // 3. Fallback: recherche directe avec cache (limitée aux dossiers par défaut)
        return await SearchDirectWithCacheAsync(query, searchScope, cancellationToken);
    }

    /// <summary>
    /// Vérifie quel moteur de recherche est disponible.
    /// </summary>
    public static SearchEngineStatus GetAvailableEngine()
    {
        if (EverythingApi.IsAvailable())
            return SearchEngineStatus.Everything;
        
        if (WindowsSearchService.IsAvailable())
            return SearchEngineStatus.WindowsSearch;
        
        return SearchEngineStatus.DirectSearch;
    }

    /// <summary>
    /// Retourne des informations détaillées sur le moteur de recherche disponible.
    /// </summary>
    public static SearchEngineInfo GetEngineInfo()
    {
        var status = GetAvailableEngine();
        
        return status switch
        {
            SearchEngineStatus.Everything => new SearchEngineInfo
            {
                Status = status,
                Name = "Everything",
                Description = "Recherche ultra-rapide via Everything",
                IsOptimal = true,
                Icon = "⚡",
                Recommendation = null
            },
            SearchEngineStatus.WindowsSearch => new SearchEngineInfo
            {
                Status = status,
                Name = "Windows Search",
                Description = "Service d'indexation Windows actif",
                IsOptimal = false,
                Icon = "🔍",
                Recommendation = "Installez Everything pour de meilleures performances"
            },
            _ => new SearchEngineInfo
            {
                Status = status,
                Name = "Recherche directe",
                Description = "Scan du système de fichiers (plus lent)",
                IsOptimal = false,
                Icon = "🐢",
                Recommendation = "Installez Everything ou activez le service Windows Search"
            }
        };
    }

    /// <summary>
    /// Vide le cache de recherche.
    /// </summary>
    public static void ClearCache()
    {
        _searchCache.Clear();
        Debug.WriteLine("[UniversalSearch] Cache cleared");
    }

    /// <summary>
    /// Retourne les statistiques du cache.
    /// </summary>
    public static (int EntryCount, int TotalResults) GetCacheStats()
    {
        var totalResults = _searchCache.Values.Sum(c => c.Results.Count);
        return (_searchCache.Count, totalResults);
    }

    #region Everything Search

    private static List<SearchResult> SearchWithEverything(string query, int maxResults)
    {
        var results = new List<SearchResult>();

        EverythingApi.Reset();
        EverythingApi.SetSearch(query);
        EverythingApi.SetMax(maxResults);
        EverythingApi.SetRequestFlags(
            EverythingApi.EVERYTHING_REQUEST_FILE_NAME |
            EverythingApi.EVERYTHING_REQUEST_PATH |
            EverythingApi.EVERYTHING_REQUEST_SIZE |
            EverythingApi.EVERYTHING_REQUEST_DATE_MODIFIED);

        EverythingApi.Query(true);

        var numResults = EverythingApi.GetNumResults();
        for (uint i = 0; i < numResults && results.Count < maxResults; i++)
        {
            var path = EverythingApi.GetResultFullPathName(i);
            if (string.IsNullOrEmpty(path)) continue;

            var name = Path.GetFileName(path);
            var isFolder = EverythingApi.IsFolder(i);
            var size = EverythingApi.GetResultSize(i);
            var modified = EverythingApi.GetResultDateModified(i);

            results.Add(new SearchResult
            {
                Name = isFolder ? name : Path.GetFileNameWithoutExtension(path),
                Path = path,
                Description = BuildDescription(path, isFolder ? null : size, modified),
                Type = DetermineResultType(path, isFolder),
                Score = 500
            });
        }

        return results;
    }

    #endregion

    #region Direct Filesystem Search with Cache

    private static async Task<List<SearchResult>> SearchDirectWithCacheAsync(
        string query,
        string? searchScope,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(query, searchScope);

        // Vérifier le cache
        if (_searchCache.TryGetValue(cacheKey, out var cached))
        {
            if (DateTime.UtcNow - cached.Timestamp < CacheExpiration)
            {
                Debug.WriteLine($"[UniversalSearch] Cache hit for '{query}' ({cached.Results.Count} results)");
                return cached.Results.ToList(); // Retourner une copie
            }
            
            // Cache expiré, le supprimer
            _searchCache.TryRemove(cacheKey, out _);
        }

        // Effectuer la recherche
        var results = await SearchDirectAsync(query, searchScope, cancellationToken);

        // Mettre en cache si la recherche n'a pas été annulée
        if (!cancellationToken.IsCancellationRequested && results.Count > 0)
        {
            // Nettoyer le cache si nécessaire
            CleanupCacheIfNeeded();

            _searchCache[cacheKey] = new CachedSearchResult
            {
                Query = query,
                Results = results,
                Timestamp = DateTime.UtcNow
            };
            
            Debug.WriteLine($"[UniversalSearch] Cached '{query}' ({results.Count} results)");
        }

        return results;
    }

    private static string BuildCacheKey(string query, string? scope)
    {
        var normalizedQuery = query.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(scope) 
            ? normalizedQuery 
            : $"{normalizedQuery}|{scope.ToLowerInvariant()}";
    }

    private static void CleanupCacheIfNeeded()
    {
        if (_searchCache.Count < MaxCacheEntries) return;

        // Supprimer les entrées expirées
        var expiredKeys = _searchCache
            .Where(kv => DateTime.UtcNow - kv.Value.Timestamp > CacheExpiration)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _searchCache.TryRemove(key, out _);
        }

        // Si toujours trop d'entrées, supprimer les plus anciennes
        if (_searchCache.Count >= MaxCacheEntries)
        {
            var oldestKeys = _searchCache
                .OrderBy(kv => kv.Value.Timestamp)
                .Take(_searchCache.Count - MaxCacheEntries + 5)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in oldestKeys)
            {
                _searchCache.TryRemove(key, out _);
            }
        }
    }

    private static async Task<List<SearchResult>> SearchDirectAsync(
        string query,
        string? searchScope,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchResult>();
        var searchPaths = string.IsNullOrEmpty(searchScope)
            ? DefaultSearchPaths.Where(Directory.Exists).ToArray()
            : [searchScope];

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            MaxRecursionDepth = MaxSearchDepth,
            AttributesToSkip = FileAttributes.System
        };

        await Task.Run(() =>
        {
            foreach (var basePath in searchPaths)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (!Directory.Exists(basePath)) continue;

                try
                {
                    // Rechercher les fichiers
                    var files = Directory.EnumerateFiles(basePath, $"*{query}*", options);
                    foreach (var file in files)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        if (results.Count >= MaxResults) break;
                        if (ShouldExclude(file)) continue;

                        try
                        {
                            var info = new FileInfo(file);
                            results.Add(new SearchResult
                            {
                                Name = Path.GetFileNameWithoutExtension(file),
                                Path = file,
                                Description = BuildDescription(file, info.Length, info.LastWriteTime),
                                Type = DetermineResultType(file, false),
                                Score = 400
                            });
                        }
                        catch { /* Ignorer les fichiers inaccessibles */ }
                    }

                    // Rechercher les dossiers
                    var folders = Directory.EnumerateDirectories(basePath, $"*{query}*", options);
                    foreach (var folder in folders)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        if (results.Count >= MaxResults) break;
                        if (ShouldExclude(folder)) continue;

                        try
                        {
                            var info = new DirectoryInfo(folder);
                            results.Add(new SearchResult
                            {
                                Name = info.Name,
                                Path = folder,
                                Description = folder,
                                Type = ResultType.Folder,
                                Score = 400
                            });
                        }
                        catch { /* Ignorer les dossiers inaccessibles */ }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DirectSearch] Error in {basePath}: {ex.Message}");
                }
            }
        }, cancellationToken);

        return results.Take(MaxResults).ToList();
    }

    private static bool ShouldExclude(string path)
    {
        foreach (var excluded in ExcludedFolders)
        {
            if (path.Contains(excluded, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    #endregion

    #region Helpers

    private static ResultType DetermineResultType(string path, bool isFolder)
    {
        if (isFolder) return ResultType.Folder;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".exe" or ".msi" => ResultType.Application,
            ".lnk" => ResultType.Application,
            ".bat" or ".cmd" or ".ps1" or ".vbs" => ResultType.Script,
            _ => ResultType.File
        };
    }

    private static string BuildDescription(string path, long? size, DateTime? modified)
    {
        var parts = new List<string>();

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            parts.Add(dir);

        if (size.HasValue)
            parts.Add(FormatFileSize(size.Value));

        if (modified.HasValue)
            parts.Add(modified.Value.ToString("dd/MM/yyyy HH:mm"));

        return string.Join(" • ", parts);
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        var order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }

    #endregion
}

/// <summary>
/// Statut du moteur de recherche disponible.
/// </summary>
public enum SearchEngineStatus
{
    Everything,
    WindowsSearch,
    DirectSearch
}

/// <summary>
/// Informations détaillées sur le moteur de recherche.
/// </summary>
public class SearchEngineInfo
{
    public SearchEngineStatus Status { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public bool IsOptimal { get; init; }
    public required string Icon { get; init; }
    public string? Recommendation { get; init; }
}

/// <summary>
/// Résultat de recherche mis en cache.
/// </summary>
internal class CachedSearchResult
{
    public required string Query { get; init; }
    public required List<SearchResult> Results { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Wrapper P/Invoke pour Everything SDK.
/// Everything doit être installé et en cours d'exécution.
/// </summary>
internal static class EverythingApi
{
    private const string DllName = "Everything64.dll";

    // Request flags
    public const uint EVERYTHING_REQUEST_FILE_NAME = 0x00000001;
    public const uint EVERYTHING_REQUEST_PATH = 0x00000002;
    public const uint EVERYTHING_REQUEST_SIZE = 0x00000010;
    public const uint EVERYTHING_REQUEST_DATE_MODIFIED = 0x00000040;

    [DllImport(DllName, CharSet = CharSet.Unicode)]
    private static extern uint Everything_SetSearchW(string lpSearchString);

    [DllImport(DllName)]
    private static extern void Everything_SetMax(uint dwMax);

    [DllImport(DllName)]
    private static extern void Everything_SetRequestFlags(uint dwRequestFlags);

    [DllImport(DllName)]
    private static extern bool Everything_QueryW(bool bWait);

    [DllImport(DllName)]
    private static extern uint Everything_GetNumResults();

    [DllImport(DllName, CharSet = CharSet.Unicode)]
    private static extern void Everything_GetResultFullPathNameW(uint nIndex, System.Text.StringBuilder lpString, uint nMaxCount);

    [DllImport(DllName)]
    private static extern bool Everything_IsFolderResult(uint nIndex);

    [DllImport(DllName)]
    private static extern bool Everything_GetResultSize(uint nIndex, out long lpFileSize);

    [DllImport(DllName)]
    private static extern bool Everything_GetResultDateModified(uint nIndex, out long lpFileTime);

    [DllImport(DllName)]
    private static extern uint Everything_GetLastError();

    [DllImport(DllName)]
    private static extern uint Everything_GetMajorVersion();

    [DllImport(DllName)]
    private static extern void Everything_Reset();

    [DllImport(DllName)]
    private static extern bool Everything_IsDBLoaded();

    private static bool? _isAvailable;

    /// <summary>
    /// Vérifie si Everything est installé et en cours d'exécution.
    /// </summary>
    public static bool IsAvailable()
    {
        if (_isAvailable.HasValue)
            return _isAvailable.Value;

        try
        {
            // Vérifier si la DLL est chargeable et si Everything tourne
            var version = Everything_GetMajorVersion();
            _isAvailable = version > 0 && Everything_IsDBLoaded();
        }
        catch
        {
            _isAvailable = false;
        }

        return _isAvailable.Value;
    }

    /// <summary>
    /// Force une nouvelle vérification de disponibilité.
    /// </summary>
    public static void RefreshAvailability()
    {
        _isAvailable = null;
    }

    public static void Reset() => Everything_Reset();
    
    public static void SetSearch(string query) => Everything_SetSearchW(query);
    
    public static void SetMax(int max) => Everything_SetMax((uint)max);
    
    public static void SetRequestFlags(uint flags) => Everything_SetRequestFlags(flags);
    
    public static void Query(bool wait) => Everything_QueryW(wait);
    
    public static int GetNumResults() => (int)Everything_GetNumResults();

    public static string GetResultFullPathName(uint index)
    {
        var sb = new System.Text.StringBuilder(260);
        Everything_GetResultFullPathNameW(index, sb, 260);
        return sb.ToString();
    }

    public static bool IsFolder(uint index) => Everything_IsFolderResult(index);

    public static long? GetResultSize(uint index)
    {
        if (Everything_GetResultSize(index, out var size))
            return size;
        return null;
    }

    public static DateTime? GetResultDateModified(uint index)
    {
        if (Everything_GetResultDateModified(index, out var fileTime))
            return DateTime.FromFileTime(fileTime);
        return null;
    }
}
