using System.IO;
using Microsoft.Data.Sqlite;
using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Implémentation SQLite de <see cref="IIndexRepository"/> (Point #6).
/// Extrait de IndexingService pour isoler toute la logique d'accès base de données.
/// 
/// <b>Thread safety :</b>
/// - _persistentConnection + _dbLock : CRUD synchrone rapide
/// - Connexions éphémères : opérations bulk async (WAL permet la concurrence)
/// </summary>
public sealed class IndexRepository : IIndexRepository
{
    private readonly string _connectionString;
    private readonly SqliteConnection _persistentConnection;
    private readonly object _dbLock = new();
    private readonly ILogger _logger;
    private bool _disposed;

    public IndexRepository(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Constants.AppName);
        Directory.CreateDirectory(appData);
        
        var dbPath = Path.Combine(appData, Constants.DatabaseFileName);
        _connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared";
        
        _persistentConnection = new SqliteConnection(_connectionString);
        _persistentConnection.Open();
        using (var pragmaCmd = _persistentConnection.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            pragmaCmd.ExecuteNonQuery();
        }
        
        InitializeDatabase();
        _logger.Info("[IndexRepository] Initialisé");
    }

    private void InitializeDatabase()
    {
        using var cmd = _persistentConnection.CreateCommand();
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

    public List<IndexedItem> LoadAll(CancellationToken token = default)
    {
        var items = new List<IndexedItem>();
        
        lock (_dbLock)
        {
            using var cmd = _persistentConnection.CreateCommand();
            cmd.CommandText = "SELECT Path, Name, Description, Type, LastUsed, UseCount FROM Items";
            
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                // Vérification périodique : permet un arrêt propre si shutdown
                // pendant le chargement de milliers de lignes.
                if ((items.Count & 0xFF) == 0)
                    token.ThrowIfCancellationRequested();
                
                var item = IndexedItem.Create(
                    path: reader.GetString(0),
                    name: reader.GetString(1),
                    description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    type: (ResultType)reader.GetInt32(3),
                    useCount: reader.GetInt32(5),
                    lastUsed: reader.IsDBNull(4) ? DateTime.MinValue : DateTime.Parse(reader.GetString(4)));
                items.Add(item);
            }
        }
        
        return items;
    }

    public async Task SaveBulkAsync(List<IndexedItem> items, Action<int>? onProgress, CancellationToken token)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(token).ConfigureAwait(false);
        
        await using var transaction = conn.BeginTransaction();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO Items (Path, Name, Description, Type, UseCount, LastUsed, IndexedAt)
                VALUES (@path, @name, @desc, @type, 
                    COALESCE((SELECT UseCount FROM Items WHERE Path = @path), 0),
                    (SELECT LastUsed FROM Items WHERE Path = @path),
                    @indexed)
                """;
            
            var pathParam = cmd.Parameters.Add("@path", SqliteType.Text);
            var nameParam = cmd.Parameters.Add("@name", SqliteType.Text);
            var descParam = cmd.Parameters.Add("@desc", SqliteType.Text);
            var typeParam = cmd.Parameters.Add("@type", SqliteType.Integer);
            var indexedParam = cmd.Parameters.Add("@indexed", SqliteType.Text);
            cmd.Prepare();
            
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
                
                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                
                if (++progress % Constants.IndexingBatchSize == 0)
                    onProgress?.Invoke(progress);
            }
            
            await transaction.CommitAsync(token).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(token).ConfigureAwait(false);
            throw;
        }
    }

    public void RecordUsage(string path)
    {
        lock (_dbLock)
        {
            using var cmd = _persistentConnection.CreateCommand();
            cmd.CommandText = "UPDATE Items SET UseCount = UseCount + 1, LastUsed = @now WHERE Path = @path";
            cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@path", path);
            cmd.ExecuteNonQuery();
        }
    }

    public void AddOrUpdate(IndexedItem item)
    {
        lock (_dbLock)
        {
            using var cmd = _persistentConnection.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO Items (Path, Name, Description, Type, UseCount, IndexedAt)
                VALUES (@path, @name, @desc, @type, 
                    COALESCE((SELECT UseCount FROM Items WHERE Path = @path), 0), 
                    @indexed)
                """;
            cmd.Parameters.AddWithValue("@path", item.Path);
            cmd.Parameters.AddWithValue("@name", item.Name);
            cmd.Parameters.AddWithValue("@desc", item.Description);
            cmd.Parameters.AddWithValue("@type", (int)item.Type);
            cmd.Parameters.AddWithValue("@indexed", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }
    }

    public bool Remove(string path)
    {
        lock (_dbLock)
        {
            using var cmd = _persistentConnection.CreateCommand();
            cmd.CommandText = "DELETE FROM Items WHERE Path = @path";
            cmd.Parameters.AddWithValue("@path", path);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    public int RemoveByFolder(string folderPath)
    {
        var normalizedFolder = folderPath.TrimEnd('\\', '/');
        
        lock (_dbLock)
        {
            using var cmd = _persistentConnection.CreateCommand();
            cmd.CommandText = "DELETE FROM Items WHERE Path LIKE @prefix";
            cmd.Parameters.AddWithValue("@prefix", normalizedFolder + "%");
            return cmd.ExecuteNonQuery();
        }
    }

    public async Task<int> PurgeStaleAsync(CancellationToken token)
    {
        var threshold = DateTime.UtcNow.AddMinutes(-5).ToString("o");
        
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(token).ConfigureAwait(false);
        
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Items WHERE IndexedAt < @threshold";
        cmd.Parameters.AddWithValue("@threshold", threshold);
        return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    public async Task<int> PurgePathsAsync(IReadOnlyList<string> paths, CancellationToken token)
    {
        if (paths.Count == 0) return 0;
        
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(token).ConfigureAwait(false);
        
        await using var cmd = conn.CreateCommand();
        var paramNames = new List<string>(paths.Count);
        for (var i = 0; i < paths.Count; i++)
        {
            var paramName = $"@p{i}";
            paramNames.Add(paramName);
            cmd.Parameters.AddWithValue(paramName, paths[i]);
        }
        cmd.CommandText = $"DELETE FROM Items WHERE Path IN ({string.Join(", ", paramNames)})";
        return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        try
        {
            _persistentConnection.Close();
            _persistentConnection.Dispose();
        }
        catch { /* Ignore en shutdown */ }
    }
}
