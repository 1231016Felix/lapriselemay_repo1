using System.Collections.Concurrent;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using QuickLauncher.Models;

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
        _logger = logger ?? new FileLogger();
        
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Constants.AppName);
        
        Directory.CreateDirectory(appData);
        _dbPath = Path.Combine(appData, Constants.DatabaseFileName);
        _connectionString = $"Data Source={_dbPath};Version=3;Journal Mode=WAL;Cache Size=10000";
        
        _settings = AppSettings.Load();
        
        InitializeDatabase();
        LoadCacheFromDatabase();
        
        _logger.Info($"IndexingService initialisé avec {_cache.Count} éléments en cache");
    }

    private void InitializeDatabase()
    {
        using var conn = new SQLiteConnection(_connectionString);
        conn.Open();
        
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
            
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA cache_size=10000;
            """;
        cmd.ExecuteNonQuery();
    }
    
    private void LoadCacheFromDatabase()
    {
        _cache.Clear();
        
        using var conn = new SQLiteConnection(_connectionString);
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
            
            // Indexer les dossiers en parallèle
            var folderTasks = _settings.IndexedFolders
                .Where(Directory.Exists)
                .Select(folder => Task.Run(() => IndexFolder(folder, items, token), token))
                .ToArray();
            
            await Task.WhenAll([storeTask, ..folderTasks]);

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
        await using var conn = new SQLiteConnection(_connectionString);
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
            
            var pathParam = cmd.Parameters.Add("@path", System.Data.DbType.String);
            var nameParam = cmd.Parameters.Add("@name", System.Data.DbType.String);
            var descParam = cmd.Parameters.Add("@desc", System.Data.DbType.String);
            var typeParam = cmd.Parameters.Add("@type", System.Data.DbType.Int32);
            var indexedParam = cmd.Parameters.Add("@indexed", System.Data.DbType.String);
            
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
        
        await using var conn = new SQLiteConnection(_connectionString);
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

        // Recherche avec scoring parallèle
        return _cache.Values
            .AsParallel()
            .Select(item => (Item: item, Score: CalculateScore(normalizedQuery, item)))
            .Where(x => x.Score > 0)
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
            using var conn = new SQLiteConnection(_connectionString);
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

/// <summary>
/// Interface de logging simple.
/// </summary>
public interface ILogger
{
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? ex = null);
}

/// <summary>
/// Logger vers fichier.
/// </summary>
public sealed class FileLogger : ILogger
{
    private readonly string _logPath;
    private readonly object _lock = new();
    
    public FileLogger()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Constants.AppName);
        Directory.CreateDirectory(appData);
        _logPath = Path.Combine(appData, Constants.LogFileName);
    }
    
    private void Log(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        Debug.WriteLine(line);
        
        lock (_lock)
        {
            try { File.AppendAllText(_logPath, line + Environment.NewLine); }
            catch { /* Ignore logging errors */ }
        }
    }
    
    public void Info(string message) => Log("INFO", message);
    public void Warning(string message) => Log("WARN", message);
    public void Error(string message, Exception? ex = null) => 
        Log("ERROR", ex != null ? $"{message}: {ex}" : message);
}
