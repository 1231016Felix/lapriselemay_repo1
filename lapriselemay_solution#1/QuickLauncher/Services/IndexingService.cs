using System.IO;
using System.Collections.Concurrent;
using System.Data.SQLite;
using System.Diagnostics;
using QuickLauncher.Models;

namespace QuickLauncher.Services;

public class IndexingService
{
    private readonly string _dbPath;
    private readonly string _logPath;
    private readonly ConcurrentDictionary<string, SearchResult> _cache = new();
    private readonly AppSettings _settings;
    private bool _isIndexing;
    
    public event EventHandler? IndexingCompleted;
    
    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Debug.WriteLine(line);
        try { File.AppendAllText(_logPath, line + Environment.NewLine); } catch { }
    }
    
    public IndexingService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "QuickLauncher");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "index.db");
        _logPath = Path.Combine(dir, "indexing.log");
        _settings = AppSettings.Load();
        
        // Effacer l'ancien log
        try { File.WriteAllText(_logPath, $"=== QuickLauncher Log {DateTime.Now} ==={Environment.NewLine}"); } catch { }
        
        InitializeDatabase();
        
        // Charger le cache existant immédiatement au démarrage
        LoadCache();
        Log($"Cache chargé: {_cache.Count} éléments");
    }

    private void InitializeDatabase()
    {
        using var conn = new SQLiteConnection($"Data Source={_dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
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
        ";
        cmd.ExecuteNonQuery();
    }
    
    public async Task StartIndexingAsync()
    {
        if (_isIndexing) return;
        _isIndexing = true;
        
        Log("Démarrage de l'indexation...");
        Log($"Extensions recherchées: {string.Join(", ", _settings.FileExtensions)}");
        
        await Task.Run(() =>
        {
            var items = new List<SearchResult>();
            foreach (var folder in _settings.IndexedFolders)
            {
                Log($"Dossier configuré: {folder}");
                if (Directory.Exists(folder))
                {
                    IndexFolder(folder, items);
                    Log($"  -> Total jusqu'ici: {items.Count} éléments");
                }
                else
                {
                    Log($"  -> DOSSIER INEXISTANT!");
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
            
            Log($"TOTAL FINAL: {items.Count} éléments à sauvegarder");
            SaveToDatabase(items);
            LoadCache();
            Log($"Cache rechargé: {_cache.Count} éléments");
        });
        
        _isIndexing = false;
        IndexingCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void IndexFolder(string folderPath, List<SearchResult> items)
    {
        var matchedCount = 0;
        IndexFolderRecursive(folderPath, items, ref matchedCount);
        Log($"  -> {matchedCount} fichiers matchés dans {folderPath}");
    }
    
    private void IndexFolderRecursive(string folderPath, List<SearchResult> items, ref int matchedCount)
    {
        try
        {
            // Indexer les fichiers du dossier courant
            foreach (var file in Directory.EnumerateFiles(folderPath))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!_settings.FileExtensions.Contains(ext)) continue;
                
                var result = CreateSearchResult(file);
                if (result != null)
                {
                    items.Add(result);
                    matchedCount++;
                    if (matchedCount <= 10)
                        Log($"     + {result.Name} ({result.Type})");
                }
            }
            
            // Parcourir les sous-dossiers récursivement
            foreach (var subDir in Directory.EnumerateDirectories(folderPath))
            {
                IndexFolderRecursive(subDir, items, ref matchedCount);
            }
        }
        catch (UnauthorizedAccessException) 
        { 
            // Ignorer silencieusement les dossiers inaccessibles
        }
        catch (Exception ex) 
        { 
            Log($"  ERREUR dans {folderPath}: {ex.Message}"); 
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
            
            if (Directory.Exists(targetPath)) type = ResultType.Folder;
            
            return new SearchResult { Name = name, Path = filePath, Description = description, Type = type };
        }
        catch { return null; }
    }
    
    private void SaveToDatabase(List<SearchResult> items)
    {
        using var conn = new SQLiteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var transaction = conn.BeginTransaction();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT OR REPLACE INTO Items (Path, Name, Description, Type, UseCount, IndexedAt)
            VALUES (@path, @name, @desc, @type, COALESCE((SELECT UseCount FROM Items WHERE Path = @path), 0), @indexed)";
        
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

    private void LoadCache()
    {
        _cache.Clear();
        using var conn = new SQLiteConnection($"Data Source={_dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Items";
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
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Items";
        cmd.ExecuteNonQuery();
        await StartIndexingAsync();
    }

    public List<SearchResult> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<SearchResult>();
        query = query.ToLowerInvariant().Trim();
        
        foreach (var engine in _settings.SearchEngines)
        {
            if (query.StartsWith($"{engine.Prefix} "))
            {
                var searchQuery = query[(engine.Prefix.Length + 1)..];
                return new List<SearchResult>
                {
                    new() { Name = $"Rechercher '{searchQuery}' sur {engine.Name}",
                        Path = engine.UrlTemplate.Replace("{query}", Uri.EscapeDataString(searchQuery)),
                        Type = ResultType.WebSearch, Score = 100 }
                };
            }
        }
        
        if (TryCalculate(query, out var calcResult))
        {
            return new List<SearchResult>
            {
                new() { Name = calcResult, Description = $"= {calcResult}", Path = calcResult,
                    Type = ResultType.Calculator, Score = 100 }
            };
        }

        return _cache.Values
            .Select(item => new { Item = item, Score = CalculateScore(query, item) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Item.UseCount)
            .Take(_settings.MaxResults)
            .Select(x => { x.Item.Score = x.Score; return x.Item; })
            .ToList();
    }
    
    private int CalculateScore(string query, SearchResult item)
    {
        var name = item.Name.ToLowerInvariant();
        var score = 0;
        
        if (name.StartsWith(query)) score += 100;
        else if (name.Contains(query)) score += 50;
        else if (MatchesInitials(query, name)) score += 30;
        else if (FuzzyMatch(query, name)) score += 20;
        else return 0;
        
        score += item.Type switch { ResultType.Application => 20, ResultType.Script => 15, _ => 0 };
        score += Math.Min(item.UseCount * 5, 50);
        return score;
    }

    private bool MatchesInitials(string query, string name)
    {
        var words = name.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
        var initials = string.Concat(words.Select(w => w.FirstOrDefault()));
        return initials.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
    
    private bool FuzzyMatch(string query, string name)
    {
        var qi = 0;
        foreach (var c in name)
            if (qi < query.Length && char.ToLower(c) == query[qi]) qi++;
        return qi == query.Length;
    }
    
    private bool TryCalculate(string expression, out string result)
    {
        result = string.Empty;
        if (!expression.Any(c => "0123456789+-*/().".Contains(c))) return false;
        try
        {
            var table = new System.Data.DataTable();
            var value = table.Compute(expression, null);
            result = value?.ToString() ?? string.Empty;
            return !string.IsNullOrEmpty(result);
        }
        catch { return false; }
    }
    
    public void RecordUsage(SearchResult item)
    {
        using var conn = new SQLiteConnection($"Data Source={_dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
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
}
