using System.IO;
using System.Collections.Concurrent;
using System.Data.SQLite;
using System.Diagnostics;
using System.Text.RegularExpressions;
using QuickLauncher.Models;

namespace QuickLauncher.Services;

public partial class IndexingService : IDisposable
{
    private readonly string _dbPath;
    private readonly string _logPath;
    private readonly ConcurrentDictionary<string, SearchResult> _cache = new();
    private readonly AppSettings _settings;
    private readonly object _indexLock = new();
    private bool _isIndexing;
    private bool _disposed;
    
    public event EventHandler? IndexingCompleted;
    
    public IndexingService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "QuickLauncher");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "index.db");
        _logPath = Path.Combine(dir, "indexing.log");
        _settings = AppSettings.Load();
        
        try { File.WriteAllText(_logPath, $"=== QuickLauncher Log {DateTime.Now} ==={Environment.NewLine}"); } catch { }
        
        InitializeDatabase();
        LoadCache();
        Log($"Cache chargé: {_cache.Count} éléments");
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Debug.WriteLine(line);
        try { File.AppendAllText(_logPath, line + Environment.NewLine); } catch { }
    }

    private void InitializeDatabase()
    {
        using var conn = new SQLiteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Items (
                Path TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT,
                Type INTEGER NOT NULL,
                LastUsed TEXT,
                UseCount INTEGER DEFAULT 0,
                IndexedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_name ON Items(Name);
            CREATE INDEX IF NOT EXISTS idx_usecount ON Items(UseCount DESC);
            """;
        cmd.ExecuteNonQuery();
    }
    
    public async Task StartIndexingAsync()
    {
        if (_isIndexing) return;
        
        lock (_indexLock)
        {
            if (_isIndexing) return;
            _isIndexing = true;
        }
        
        Log("Démarrage de l'indexation...");
        Log($"Extensions: {string.Join(", ", _settings.FileExtensions)}");
        Log($"Profondeur max: {_settings.SearchDepth}");
        Log($"Dossiers cachés: {(_settings.IndexHiddenFolders ? "Oui" : "Non")}");
        
        try
        {
            await Task.Run(() =>
            {
                var items = new List<SearchResult>();
                
                foreach (var folder in _settings.IndexedFolders)
                {
                    Log($"Indexation: {folder}");
                    if (Directory.Exists(folder))
                    {
                        var count = IndexFolder(folder, items);
                        Log($"  -> {count} éléments trouvés");
                    }
                    else
                    {
                        Log($"  -> DOSSIER INEXISTANT");
                    }
                }
                
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
                
                Log($"TOTAL: {items.Count} éléments");
                SaveToDatabase(items);
                LoadCache();
            });
        }
        finally
        {
            _isIndexing = false;
            IndexingCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private int IndexFolder(string folderPath, List<SearchResult> items)
    {
        var count = 0;
        IndexFolderRecursive(folderPath, items, ref count, 0);
        return count;
    }
    
    private void IndexFolderRecursive(string folderPath, List<SearchResult> items, ref int count, int currentDepth)
    {
        if (currentDepth > _settings.SearchDepth) return;
        
        try
        {
            // Vérifier si c'est un dossier caché
            if (!_settings.IndexHiddenFolders)
            {
                var dirInfo = new DirectoryInfo(folderPath);
                if ((dirInfo.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
                    return;
            }
            
            // Indexer les fichiers
            foreach (var file in Directory.EnumerateFiles(folderPath))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!_settings.FileExtensions.Contains(ext)) continue;
                
                // Vérifier fichier caché
                if (!_settings.IndexHiddenFolders)
                {
                    var fileInfo = new FileInfo(file);
                    if ((fileInfo.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
                        continue;
                }
                
                var result = CreateSearchResult(file);
                if (result != null)
                {
                    items.Add(result);
                    count++;
                }
            }
            
            // Parcourir les sous-dossiers
            foreach (var subDir in Directory.EnumerateDirectories(folderPath))
            {
                IndexFolderRecursive(subDir, items, ref count, currentDepth + 1);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (Exception ex)
        {
            Log($"ERREUR {folderPath}: {ex.Message}");
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
            
            // Résoudre les raccourcis .lnk
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
        catch 
        { 
            return null; 
        }
    }
    
    private void SaveToDatabase(List<SearchResult> items)
    {
        using var conn = new SQLiteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var transaction = conn.BeginTransaction();
        
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO Items (Path, Name, Description, Type, UseCount, IndexedAt)
                VALUES (@path, @name, @desc, @type, 
                    COALESCE((SELECT UseCount FROM Items WHERE Path = @path), 0), 
                    @indexed)
                """;
            
            var now = DateTime.UtcNow.ToString("o");
            
            foreach (var item in items)
            {
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@path", item.Path);
                cmd.Parameters.AddWithValue("@name", item.Name);
                cmd.Parameters.AddWithValue("@desc", item.Description);
                cmd.Parameters.AddWithValue("@type", (int)item.Type);
                cmd.Parameters.AddWithValue("@indexed", now);
                cmd.ExecuteNonQuery();
            }
            
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private void LoadCache()
    {
        _cache.Clear();
        
        using var conn = new SQLiteConnection($"Data Source={_dbPath}");
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
                Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Type = (ResultType)reader.GetInt32(3),
                LastUsed = reader.IsDBNull(4) ? DateTime.MinValue : DateTime.Parse(reader.GetString(4)),
                UseCount = reader.GetInt32(5)
            };
            _cache[item.Path] = item;
        }
    }
    
    public async Task ReindexAsync()
    {
        using var conn = new SQLiteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Items";
        cmd.ExecuteNonQuery();
        
        await StartIndexingAsync();
    }

    public List<SearchResult> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) 
            return [];
        
        query = query.ToLowerInvariant().Trim();
        
        // Recherche web avec préfixe
        foreach (var engine in _settings.SearchEngines)
        {
            if (query.StartsWith($"{engine.Prefix} "))
            {
                var searchQuery = query[(engine.Prefix.Length + 1)..];
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
        if (TryCalculate(query, out var calcResult))
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

        // Recherche normale avec scoring
        return _cache.Values
            .Select(item => (Item: item, Score: CalculateScore(query, item)))
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
        var name = item.Name.ToLowerInvariant();
        var score = 0;
        
        if (name.Equals(query)) 
            score = 150;
        else if (name.StartsWith(query)) 
            score = 100;
        else if (name.Contains(query)) 
            score = 50;
        else if (MatchesInitials(query, name)) 
            score = 30;
        else if (FuzzyMatch(query, name)) 
            score = 20;
        else 
            return 0;
        
        // Bonus par type
        score += item.Type switch
        {
            ResultType.Application => 20,
            ResultType.Script => 15,
            ResultType.Folder => 10,
            _ => 0
        };
        
        // Bonus usage (max 50)
        score += Math.Min(item.UseCount * 5, 50);
        
        return score;
    }

    private static bool MatchesInitials(string query, string name)
    {
        var words = name.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        var initials = string.Concat(words.Select(w => w.FirstOrDefault()));
        return initials.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
    
    private static bool FuzzyMatch(string query, string name)
    {
        var qi = 0;
        foreach (var c in name)
        {
            if (qi < query.Length && char.ToLower(c) == query[qi])
                qi++;
        }
        return qi == query.Length;
    }
    
    [GeneratedRegex(@"^[\d\s\+\-\*\/\(\)\.\,\^]+$")]
    private static partial Regex MathExpressionRegex();
    
    private static bool TryCalculate(string expression, out string result)
    {
        result = string.Empty;
        
        // Vérifier que c'est une expression mathématique valide
        if (!MathExpressionRegex().IsMatch(expression))
            return false;
        
        // Doit contenir au moins un opérateur
        if (!expression.Any(c => "+-*/^".Contains(c)))
            return false;
        
        try
        {
            // Remplacer , par . pour les décimales
            var normalized = expression.Replace(',', '.');
            
            // Évaluation simple avec DataTable
            var table = new System.Data.DataTable();
            var value = table.Compute(normalized, null);
            
            if (value is double d)
            {
                result = d.ToString("G10");
                return true;
            }
            
            result = value?.ToString() ?? string.Empty;
            return !string.IsNullOrEmpty(result);
        }
        catch
        {
            return false;
        }
    }
    
    public void RecordUsage(SearchResult item)
    {
        using var conn = new SQLiteConnection($"Data Source={_dbPath}");
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
    
    public int GetIndexedItemsCount() => _cache.Count;
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cache.Clear();
        GC.SuppressFinalize(this);
    }
}
