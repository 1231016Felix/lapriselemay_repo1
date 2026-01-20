using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using QuickLauncher.Models;
using Shared.Logging;

namespace QuickLauncher.Services;

/// <summary>
/// Service d'indexation optimisé avec support du parallélisme et annulation.
/// </summary>
public sealed partial class IndexingService : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly ConcurrentDictionary<string, SearchResult> _cache = new();
    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private readonly ILogger _logger;
    
    private AppSettings _settings;
    private CancellationTokenSource? _indexingCts;
    private bool _disposed;
    
    public event EventHandler? IndexingStarted;
    public event EventHandler? IndexingCompleted;
    public event EventHandler<int>? IndexingProgress;
    
    public bool IsIndexing { get; private set; }
    public int IndexedItemsCount => _cache.Count;

    public IndexingService(ILogger? logger = null)
    {
        _logger = logger ?? new FileLogger(Constants.AppName, Constants.LogFileName);
        
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Constants.AppName);
        
        Directory.CreateDirectory(appData);
        _dbPath = Path.Combine(appData, Constants.DatabaseFileName);
        // Microsoft.Data.Sqlite utilise une syntaxe de connexion différente
        _connectionString = $"Data Source={_dbPath};Mode=ReadWriteCreate;Cache=Shared";
        
        _settings = AppSettings.Load();
        
        InitializeDatabase();
        LoadCacheFromDatabase();
        
        _logger.Info($"IndexingService initialisé avec {_cache.Count} éléments en cache");
    }

    private void InitializeDatabase()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        
        // Activer le mode WAL pour de meilleures performances
        using (var pragmaCmd = conn.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            pragmaCmd.ExecuteNonQuery();
        }
        
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Items (
                Path TEXT PRIMARY KEY,
                Name TEXT NOT NULL COLLATE NOCASE,
                Description TEXT,
                Type INTEGER NOT NULL,
                LastUsed TEXT,
                UseCount INTEGER DEFAULT 0,
                IndexedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_name ON Items(Name COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS idx_usecount ON Items(UseCount DESC);
            CREATE INDEX IF NOT EXISTS idx_type ON Items(Type);
            """;
        cmd.ExecuteNonQuery();
    }
    
    private void LoadCacheFromDatabase()
    {
        _cache.Clear();
        
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Path, Name, Description, Type, LastUsed, UseCount FROM Items";
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var item = new SearchResult
            {
                Path = reader.GetString(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Type = (ResultType)reader.GetInt32(3),
                LastUsed = reader.IsDBNull(4) ? DateTime.MinValue : DateTime.Parse(reader.GetString(4)),
                UseCount = reader.GetInt32(5)
            };
            _cache[item.Path] = item;
        }
    }

    public async Task StartIndexingAsync(CancellationToken cancellationToken = default)
    {
        if (IsIndexing) return;
        
        await _indexLock.WaitAsync(cancellationToken);
        try
        {
            if (IsIndexing) return;
            
            IsIndexing = true;
            _indexingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            IndexingStarted?.Invoke(this, EventArgs.Empty);
            
            _settings = AppSettings.Load();
            _logger.Info("Démarrage de l'indexation...");
            
            var items = new ConcurrentBag<SearchResult>();
            var token = _indexingCts.Token;
            
            // Indexer les apps Store en parallèle
            var storeTask = Task.Run(() =>
            {
                var storeApps = StoreAppService.GetAllApps();
                foreach (var app in storeApps)
                    items.Add(app);
                _logger.Info($"Apps Store: {storeApps.Count} trouvées");
            }, token);
            
            // Indexer les favoris des navigateurs en parallèle
            var bookmarksTask = Task.Run(() =>
            {
                if (_settings.IndexBrowserBookmarks)
                {
                    var bookmarks = BookmarkService.GetAllBookmarks();
                    foreach (var bookmark in bookmarks)
                        items.Add(bookmark);
                    _logger.Info($"Favoris navigateurs: {bookmarks.Count} trouvés");
                }
            }, token);
            
            // Indexer les dossiers en parallèle
            var folderTasks = _settings.IndexedFolders
                .Where(Directory.Exists)
                .Select(folder => Task.Run(() => IndexFolder(folder, items, token), token))
                .ToArray();
            
            await Task.WhenAll([storeTask, bookmarksTask, ..folderTasks]);

            // Ajouter les scripts personnalisés
            foreach (var script in _settings.Scripts)
            {
                items.Add(new SearchResult
                {
                    Name = script.Name,
                    Path = script.Command,
                    Description = $"Script: {script.Keyword}",
                    Type = ResultType.Script
                });
            }
            
            // Dédupliquer par nom (préférer les apps Store)
            var deduplicated = items
                .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(i => i.Type == ResultType.StoreApp ? 1 : 0).First())
                .ToList();
            
            _logger.Info($"Total: {deduplicated.Count} éléments (après déduplication)");
            
            await SaveToDatabaseAsync(deduplicated, token);
            LoadCacheFromDatabase();
        }
        finally
        {
            IsIndexing = false;
            _indexingCts?.Dispose();
            _indexingCts = null;
            _indexLock.Release();
            IndexingCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void IndexFolder(string folderPath, ConcurrentBag<SearchResult> items, CancellationToken token)
    {
        var count = 0;
        IndexFolderRecursive(folderPath, items, ref count, 0, token);
        _logger.Info($"Dossier '{Path.GetFileName(folderPath)}': {count} éléments");
    }
    
    private void IndexFolderRecursive(string folderPath, ConcurrentBag<SearchResult> items, 
        ref int count, int depth, CancellationToken token)
    {
        if (depth > _settings.SearchDepth || token.IsCancellationRequested) return;
        
        try
        {
            var dirInfo = new DirectoryInfo(folderPath);
            if (!_settings.IndexHiddenFolders && (dirInfo.Attributes & FileAttributes.Hidden) != 0)
                return;
            
            // Indexer les fichiers
            foreach (var file in dirInfo.EnumerateFiles())
            {
                if (token.IsCancellationRequested) return;
                
                var ext = file.Extension.ToLowerInvariant();
                if (!_settings.FileExtensions.Contains(ext)) continue;
                if (!_settings.IndexHiddenFolders && (file.Attributes & FileAttributes.Hidden) != 0) continue;
                
                var result = CreateSearchResult(file.FullName);
                if (result != null)
                {
                    items.Add(result);
                    Interlocked.Increment(ref count);
                }
            }
            
            // Parcourir les sous-dossiers
            foreach (var subDir in dirInfo.EnumerateDirectories())
            {
                if (token.IsCancellationRequested) return;
                IndexFolderRecursive(subDir.FullName, items, ref count, depth + 1, token);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (Exception ex)
        {
            _logger.Warning($"Erreur indexation '{folderPath}': {ex.Message}");
        }
    }

    private SearchResult? CreateSearchResult(string filePath)
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(filePath);
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var description = filePath;
            var targetPath = filePath;
            
            if (ext == ".lnk")
            {
                var info = ShortcutHelper.ResolveShortcut(filePath);
                if (info != null)
                {
                    targetPath = info.TargetPath;
                    description = string.IsNullOrEmpty(info.Description) ? targetPath : info.Description;
                }
            }

            var type = ext switch
            {
                ".exe" or ".lnk" or ".msi" => ResultType.Application,
                ".bat" or ".cmd" or ".ps1" => ResultType.Script,
                _ => ResultType.File
            };
            
            if (Directory.Exists(targetPath)) 
                type = ResultType.Folder;
            
            return new SearchResult 
            { 
                Name = name, 
                Path = filePath, 
                Description = description, 
                Type = type 
            };
        }
        catch { return null; }
    }
    
    private async Task SaveToDatabaseAsync(List<SearchResult> items, CancellationToken token)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(token);
        
        await using var transaction = conn.BeginTransaction();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO Items (Path, Name, Description, Type, UseCount, IndexedAt)
                VALUES (@path, @name, @desc, @type, 
                    COALESCE((SELECT UseCount FROM Items WHERE Path = @path), 0), 
                    @indexed)
                """;
            
            var pathParam = cmd.Parameters.Add("@path", SqliteType.Text);
            var nameParam = cmd.Parameters.Add("@name", SqliteType.Text);
            var descParam = cmd.Parameters.Add("@desc", SqliteType.Text);
            var typeParam = cmd.Parameters.Add("@type", SqliteType.Integer);
            var indexedParam = cmd.Parameters.Add("@indexed", SqliteType.Text);
            
            var now = DateTime.UtcNow.ToString("o");
            var progress = 0;
            
            foreach (var item in items)
            {
                if (token.IsCancellationRequested) break;
                
                pathParam.Value = item.Path;
                nameParam.Value = item.Name;
                descParam.Value = item.Description;
                typeParam.Value = (int)item.Type;
                indexedParam.Value = now;
                
                await cmd.ExecuteNonQueryAsync(token);
                
                if (++progress % Constants.IndexingBatchSize == 0)
                    IndexingProgress?.Invoke(this, progress);
            }
            
            await transaction.CommitAsync(token);
        }
        catch
        {
            await transaction.RollbackAsync(token);
            throw;
        }
    }

    public async Task ReindexAsync(CancellationToken cancellationToken = default)
    {
        CancelIndexing();
        
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Items";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        
        _cache.Clear();
        await StartIndexingAsync(cancellationToken);
    }
    
    public void CancelIndexing() => _indexingCts?.Cancel();

    public List<SearchResult> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) 
            return [];
        
        var normalizedQuery = query.Trim().ToLowerInvariant();
        _settings = AppSettings.Load();
        
        // Recherche web avec préfixe
        foreach (var engine in _settings.SearchEngines)
        {
            var prefix = $"{engine.Prefix} ";
            if (normalizedQuery.StartsWith(prefix))
            {
                var searchQuery = normalizedQuery[prefix.Length..];
                return
                [
                    new SearchResult
                    {
                        Name = $"Rechercher '{searchQuery}' sur {engine.Name}",
                        Path = engine.UrlTemplate.Replace("{query}", Uri.EscapeDataString(searchQuery)),
                        Type = ResultType.WebSearch,
                        Score = 100
                    }
                ];
            }
        }
        
        // Calculatrice
        if (TryCalculate(normalizedQuery, out var calcResult))
        {
            return
            [
                new SearchResult
                {
                    Name = calcResult,
                    Description = $"= {calcResult}",
                    Path = calcResult,
                    Type = ResultType.Calculator,
                    Score = 100
                }
            ];
        }

        // Recherche avec scoring - parallélisme conditionnel selon la taille du cache
        const int ParallelThreshold = 500;
        
        var scored = _cache.Count > ParallelThreshold
            ? _cache.Values
                .AsParallel()
                .Select(item => (Item: item, Score: CalculateScore(normalizedQuery, item)))
                .Where(x => x.Score > 0)
            : _cache.Values
                .Select(item => (Item: item, Score: CalculateScore(normalizedQuery, item)))
                .Where(x => x.Score > 0);
        
        return scored
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Item.UseCount)
            .Take(_settings.MaxResults)
            .Select(x =>
            {
                x.Item.Score = x.Score;
                return x.Item;
            })
            .ToList();
    }
    
    private static int CalculateScore(string query, SearchResult item)
    {
        // Utiliser le nouvel algorithme de fuzzy matching avec Levenshtein
        return SearchAlgorithms.CalculateFuzzyScore(query, item.Name, item.UseCount);
    }
    
    [GeneratedRegex(@"^[\d\s\+\-\*\/\(\)\.\,\^]+$")]
    private static partial Regex MathExpressionRegex();
    
    private static bool TryCalculate(string expression, out string result)
    {
        result = string.Empty;
        
        if (!MathExpressionRegex().IsMatch(expression))
            return false;
        
        if (!expression.Any(c => "+-*/^".Contains(c)))
            return false;
        
        try
        {
            var normalized = expression.Replace(',', '.');
            var table = new System.Data.DataTable();
            var value = table.Compute(normalized, null);
            
            result = value switch
            {
                double d => d.ToString("G10"),
                _ => value?.ToString() ?? string.Empty
            };
            
            return !string.IsNullOrEmpty(result);
        }
        catch { return false; }
    }
    
    public void RecordUsage(SearchResult item)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Items SET UseCount = UseCount + 1, LastUsed = @now WHERE Path = @path";
            cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@path", item.Path);
            cmd.ExecuteNonQuery();
            
            if (_cache.TryGetValue(item.Path, out var cached))
            {
                cached.UseCount++;
                cached.LastUsed = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Erreur RecordUsage: {ex.Message}");
        }
    }
    
    #region Incremental Indexing
    
    /// <summary>
    /// Ajoute ou met à jour un fichier dans l'index de manière incrémentale.
    /// </summary>
    public void AddOrUpdateItem(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        
        try
        {
            var result = CreateSearchResult(filePath);
            if (result == null) return;
            
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO Items (Path, Name, Description, Type, UseCount, IndexedAt)
                VALUES (@path, @name, @desc, @type, 
                    COALESCE((SELECT UseCount FROM Items WHERE Path = @path), 0), 
                    @indexed)
                """;
            cmd.Parameters.AddWithValue("@path", result.Path);
            cmd.Parameters.AddWithValue("@name", result.Name);
            cmd.Parameters.AddWithValue("@desc", result.Description);
            cmd.Parameters.AddWithValue("@type", (int)result.Type);
            cmd.Parameters.AddWithValue("@indexed", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
            
            // Mettre à jour le cache
            _cache[result.Path] = result;
            
            _logger.Info($"[Incremental] Ajouté/MàJ: {result.Name}");
        }
        catch (Exception ex)
        {
            _logger.Warning($"Erreur AddOrUpdateItem '{filePath}': {ex.Message}");
        }
    }
    
    /// <summary>
    /// Supprime un fichier de l'index de manière incrémentale.
    /// </summary>
    public void RemoveItem(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Items WHERE Path = @path";
            cmd.Parameters.AddWithValue("@path", filePath);
            var affected = cmd.ExecuteNonQuery();
            
            // Retirer du cache
            _cache.TryRemove(filePath, out _);
            
            // Invalider le cache d'icône
            IconCacheService.Invalidate(filePath);
            
            if (affected > 0)
                _logger.Info($"[Incremental] Supprimé: {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            _logger.Warning($"Erreur RemoveItem '{filePath}': {ex.Message}");
        }
    }
    
    /// <summary>
    /// Traite une liste de changements de fichiers de manière incrémentale.
    /// </summary>
    public void ProcessFileChanges(IEnumerable<FileChangeEvent> changes)
    {
        var changeList = changes.ToList();
        if (changeList.Count == 0) return;
        
        _logger.Info($"[Incremental] Traitement de {changeList.Count} changements...");
        
        foreach (var change in changeList)
        {
            switch (change.Type)
            {
                case FileChangeType.Created:
                    AddOrUpdateItem(change.Path);
                    break;
                    
                case FileChangeType.Deleted:
                    RemoveItem(change.Path);
                    break;
                    
                case FileChangeType.Modified:
                    AddOrUpdateItem(change.Path);
                    break;
            }
        }
        
        _logger.Info($"[Incremental] Terminé. Cache: {_cache.Count} éléments");
    }
    
    #endregion
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        CancelIndexing();
        _indexLock.Dispose();
        _cache.Clear();
        
        GC.SuppressFinalize(this);
    }
}

// Les interfaces ILogger et FileLogger sont maintenant dans Shared.Logging
