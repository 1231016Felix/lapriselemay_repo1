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
    private readonly ISettingsProvider _settingsProvider;
    private readonly FolderFingerprintService _fingerprintService;
    
    private CancellationTokenSource? _indexingCts;
    private bool _disposed;
    
    public event EventHandler? IndexingStarted;
    public event EventHandler? IndexingCompleted;
    public event EventHandler<int>? IndexingProgress;
    
    public bool IsIndexing { get; private set; }
    public int IndexedItemsCount => _cache.Count;

    public IndexingService(ISettingsProvider settingsProvider, FolderFingerprintService fingerprintService, ILogger? logger = null)
    {
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _fingerprintService = fingerprintService ?? throw new ArgumentNullException(nameof(fingerprintService));
        _logger = logger ?? new FileLogger(Constants.AppName, Constants.LogFileName);
        
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Constants.AppName);
        
        Directory.CreateDirectory(appData);
        _dbPath = Path.Combine(appData, Constants.DatabaseFileName);
        // Microsoft.Data.Sqlite utilise une syntaxe de connexion différente
        _connectionString = $"Data Source={_dbPath};Mode=ReadWriteCreate;Cache=Shared";
        
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
            
            var settings = _settingsProvider.Current;
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
                if (settings.IndexBrowserBookmarks)
                {
                    var bookmarks = BookmarkService.GetAllBookmarks();
                    foreach (var bookmark in bookmarks)
                        items.Add(bookmark);
                    _logger.Info($"Favoris navigateurs: {bookmarks.Count} trouvés");
                }
            }, token);
            
            // Ajouter les pages de paramètres Windows
            var windowsSettings = GetWindowsSettingsItems();
            foreach (var ws in windowsSettings)
                items.Add(ws);
            _logger.Info($"Paramètres Windows: {windowsSettings.Count} ajoutés");
            
            // Indexer les dossiers en parallèle
            var folderTasks = settings.IndexedFolders
                .Where(Directory.Exists)
                .Select(folder => Task.Run(() => IndexFolder(folder, items, settings, token), token))
                .ToArray();
            
            await Task.WhenAll([storeTask, bookmarksTask, ..folderTasks]);

            // Ajouter les scripts personnalisés
            foreach (var script in settings.Scripts)
            {
                items.Add(new SearchResult
                {
                    Name = script.Name,
                    Path = script.Command,
                    Description = $"Script: {script.Keyword}",
                    Type = ResultType.Script
                });
            }
            
            // Dédupliquer par (nom + type) pour éviter de masquer des fichiers différents portant le même nom.
            // On regroupe uniquement les items qui ont le même nom ET le même type (ex: 2 raccourcis "Chrome").
            // Les items de types différents avec le même nom sont conservés (ex: "Config" fichier + "Config" dossier).
            var deduplicated = items
                .GroupBy(i => (Name: i.Name.ToLowerInvariant(), i.Type))
                .Select(g => g.OrderByDescending(i => i.Type == ResultType.StoreApp ? 1 : 0)
                              .ThenByDescending(i => i.UseCount)
                              .First())
                .ToList();
            
            _logger.Info($"Total: {deduplicated.Count} éléments (après déduplication)");
            
            await SaveToDatabaseAsync(deduplicated, token);
            LoadCacheFromDatabase();
            
            // Sauvegarder les fingerprints pour le prochain démarrage intelligent
            SaveCurrentFingerprints(settings);
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

    private void IndexFolder(string folderPath, ConcurrentBag<SearchResult> items, AppSettings settings, CancellationToken token)
    {
        var count = 0;
        IndexFolderRecursive(folderPath, items, settings, ref count, 0, token);
        _logger.Info($"Dossier '{Path.GetFileName(folderPath)}': {count} éléments");
    }
    
    private void IndexFolderRecursive(string folderPath, ConcurrentBag<SearchResult> items,
        AppSettings settings, ref int count, int depth, CancellationToken token)
    {
        if (depth > settings.SearchDepth || token.IsCancellationRequested) return;
        
        try
        {
            var dirInfo = new DirectoryInfo(folderPath);
            if (!settings.IndexHiddenFolders && (dirInfo.Attributes & FileAttributes.Hidden) != 0)
                return;
            
            // Indexer les fichiers
            foreach (var file in dirInfo.EnumerateFiles())
            {
                if (token.IsCancellationRequested) return;
                
                var ext = file.Extension.ToLowerInvariant();
                if (!settings.FileExtensions.Contains(ext)) continue;
                if (!settings.IndexHiddenFolders && (file.Attributes & FileAttributes.Hidden) != 0) continue;
                
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
                IndexFolderRecursive(subDir.FullName, items, settings, ref count, depth + 1, token);
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
        var settings = _settingsProvider.Current;
        
        // Recherche web avec préfixe
        foreach (var engine in settings.SearchEngines)
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
            .Take(settings.MaxResults)
            .Select(x =>
            {
                x.Item.Score = x.Score;
                return x.Item;
            })
            .ToList();
    }
    
    private int CalculateScore(string query, SearchResult item)
    {
        var weights = _settingsProvider.Current.ScoringWeights;
        
        // Score principal sur le nom
        var nameScore = SearchAlgorithms.CalculateFuzzyScore(query, item.Name, item.UseCount, item.LastUsed, weights);
        
        // Score additionnel sur le chemin complet (pour les requêtes multi-mots)
        if (weights.EnablePathFuzzyMatch && !string.IsNullOrEmpty(item.Path))
        {
            var pathScore = SearchAlgorithms.CalculatePathFuzzyScore(query, item.Path, weights);
            
            // Si le path score est positif mais le name score est nul,
            // on utilise le path score + les bonus usage/recency
            if (pathScore > 0 && nameScore == 0)
            {
                // Ajouter manuellement les bonus usage et recency
                if (item.UseCount > 0)
                    pathScore += Math.Min(item.UseCount * weights.UsageBonusPerUse, weights.MaxUsageBonus);
                
                if (weights.EnableRecencyBonus && item.LastUsed > DateTime.MinValue)
                {
                    var daysSince = (DateTime.UtcNow - item.LastUsed).TotalDays;
                    pathScore += Math.Max(0, weights.MaxRecencyBonus - (int)(daysSince * weights.RecencyDecayPerDay));
                }
                
                return pathScore;
            }
            
            // Si les deux matchent, prendre le meilleur
            nameScore = Math.Max(nameScore, pathScore);
        }
        
        // Score sur la description (pour les paramètres Windows, bookmarks, etc.)
        // Permet de trouver "DNS", "partition", "variables environnement" via les mots-clés de description.
        if (!string.IsNullOrEmpty(item.Description))
        {
            var descScore = SearchAlgorithms.CalculateDescriptionScore(query, item.Description);
            if (descScore > 0)
            {
                if (nameScore == 0)
                {
                    // Pas de match sur le nom ni le path : utiliser le score description réduit
                    // pour ne pas surclasser les correspondances directes sur le nom.
                    var adjustedScore = (int)(descScore * 0.6);
                    
                    // Ajouter les bonus usage et recency
                    if (item.UseCount > 0)
                        adjustedScore += Math.Min(item.UseCount * weights.UsageBonusPerUse, weights.MaxUsageBonus);
                    
                    if (weights.EnableRecencyBonus && item.LastUsed > DateTime.MinValue)
                    {
                        var daysSince = (DateTime.UtcNow - item.LastUsed).TotalDays;
                        adjustedScore += Math.Max(0, weights.MaxRecencyBonus - (int)(daysSince * weights.RecencyDecayPerDay));
                    }
                    
                    return adjustedScore;
                }
                
                // Le nom match déjà : petit boost si la description confirme la pertinence
                nameScore += (int)(descScore * 0.15);
            }
        }
        
        return nameScore;
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
    
    #region Smart Persistent Index
    
    /// <summary>
    /// Démarrage intelligent : utilise le cache SQLite existant + fingerprints
    /// pour ne réindexer que les dossiers modifiés.
    /// Premier lancement = indexation complète. Lancements suivants = quasi-instantané.
    /// </summary>
    public async Task SmartStartIndexingAsync(CancellationToken cancellationToken = default)
    {
        // Si le cache est vide (premier lancement ou DB supprimée), indexation complète
        if (_cache.Count == 0)
        {
            _logger.Info("[SmartIndex] Cache vide — indexation complète...");
            await StartIndexingAsync(cancellationToken);
            return;
        }
        
        _logger.Info($"[SmartIndex] Cache chargé avec {_cache.Count} éléments. Vérification des changements...");
        
        var settings = _settingsProvider.Current;
        var comparison = _fingerprintService.CompareWithStored(
            settings.IndexedFolders,
            settings.FileExtensions,
            settings.SearchDepth,
            settings.IndexHiddenFolders);
        
        if (!comparison.HasChanges)
        {
            _logger.Info("[SmartIndex] Aucun changement de dossiers — rafraîchissement des sources volatiles uniquement...");
            
            // Toujours rafraîchir les sources volatiles (Store apps, bookmarks, paramètres Windows)
            // même si les dossiers n'ont pas changé, pour garantir qu'elles sont à jour.
            await RefreshVolatileSourcesAsync(settings, cancellationToken);
            
            IndexingCompleted?.Invoke(this, EventArgs.Empty);
            return;
        }
        
        _logger.Info($"[SmartIndex] Changements: {comparison.NewFolders.Count} nouveaux, " +
                     $"{comparison.ModifiedFolders.Count} modifiés, {comparison.DeletedFolders.Count} supprimés");
        
        await _indexLock.WaitAsync(cancellationToken);
        try
        {
            IsIndexing = true;
            _indexingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            IndexingStarted?.Invoke(this, EventArgs.Empty);
            var token = _indexingCts.Token;
            
            // 1. Supprimer les items des dossiers supprimés
            foreach (var deletedFolder in comparison.DeletedFolders)
            {
                RemoveItemsByFolder(deletedFolder);
                _fingerprintService.RemoveFingerprint(deletedFolder);
            }
            
            // 2. Réindexer uniquement les dossiers nouveaux ou modifiés
            var foldersToIndex = comparison.NewFolders
                .Concat(comparison.ModifiedFolders)
                .Where(Directory.Exists)
                .ToList();
            
            if (foldersToIndex.Count > 0)
            {
                var items = new ConcurrentBag<SearchResult>();
                
                // Pour les dossiers modifiés, supprimer les anciens items d'abord
                foreach (var modifiedFolder in comparison.ModifiedFolders)
                {
                    RemoveItemsByFolder(modifiedFolder);
                }
                
                // Réindexer en parallèle
                var folderTasks = foldersToIndex
                    .Select(folder => Task.Run(() => IndexFolder(folder, items, settings, token), token))
                    .ToArray();
                
                await Task.WhenAll(folderTasks);
                
                // Sauvegarder les nouveaux items
                var newItems = items.ToList();
                if (newItems.Count > 0)
                {
                    await SaveToDatabaseAsync(newItems, token);
                    LoadCacheFromDatabase();
                }
                
                _logger.Info($"[SmartIndex] {newItems.Count} éléments réindexés");
            }
            
            // 3. Réindexer les bookmarks et Store apps (rapide, toujours frais)
            await RefreshVolatileSourcesAsync(settings, _indexingCts.Token);
            
            // 4. Sauvegarder les nouveaux fingerprints
            SaveCurrentFingerprints(settings);
            
            _logger.Info($"[SmartIndex] Terminé. Cache: {_cache.Count} éléments");
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
    
    /// <summary>
    /// Supprime tous les items de l'index dont le chemin commence par le dossier spécifié.
    /// </summary>
    private void RemoveItemsByFolder(string folderPath)
    {
        var normalizedFolder = folderPath.TrimEnd('\\', '/');
        var keysToRemove = _cache.Keys
            .Where(k => k.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase))
            .ToList();
        
        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }
        
        if (keysToRemove.Count > 0)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM Items WHERE Path LIKE @prefix";
                cmd.Parameters.AddWithValue("@prefix", normalizedFolder + "%");
                var deleted = cmd.ExecuteNonQuery();
                _logger.Info($"[SmartIndex] Supprimé {deleted} items de '{Path.GetFileName(normalizedFolder)}'");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Erreur RemoveItemsByFolder: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Réindexe les sources volatiles (Store apps, bookmarks, paramètres Windows)
    /// qui doivent toujours être présentes dans l'index.
    /// </summary>
    private async Task RefreshVolatileSourcesAsync(AppSettings settings, CancellationToken token)
    {
        var items = new ConcurrentBag<SearchResult>();
        
        var storeTask = Task.Run(() =>
        {
            var storeApps = StoreAppService.GetAllApps();
            foreach (var app in storeApps) items.Add(app);
        }, token);
        
        var bookmarksTask = Task.Run(() =>
        {
            if (settings.IndexBrowserBookmarks)
            {
                var bookmarks = BookmarkService.GetAllBookmarks();
                foreach (var bm in bookmarks) items.Add(bm);
            }
        }, token);
        
        await Task.WhenAll(storeTask, bookmarksTask);
        
        // Ajouter les pages de paramètres Windows (toujours présentes)
        var windowsSettings = GetWindowsSettingsItems();
        foreach (var ws in windowsSettings)
            items.Add(ws);
        
        // Supprimer les anciens Store apps, bookmarks et SystemControl
        var volatileTypes = new[] { ResultType.StoreApp, ResultType.Bookmark, ResultType.SystemControl };
        var keysToRemove = _cache
            .Where(kv => volatileTypes.Contains(kv.Value.Type))
            .Select(kv => kv.Key)
            .ToList();
        
        foreach (var key in keysToRemove)
            _cache.TryRemove(key, out _);
        
        // Sauvegarder les items frais
        var newItems = items.ToList();
        if (newItems.Count > 0)
        {
            await SaveToDatabaseAsync(newItems, token);
            LoadCacheFromDatabase();
        }
    }
    
    /// <summary>
    /// Sauvegarde les fingerprints pour tous les dossiers indexés actuels.
    /// </summary>
    private void SaveCurrentFingerprints(AppSettings settings)
    {
        var fingerprints = settings.IndexedFolders
            .Where(Directory.Exists)
            .Select(folder => _fingerprintService.ComputeFingerprint(
                folder, settings.FileExtensions, settings.SearchDepth, settings.IndexHiddenFolders))
            .ToList();
        
        _fingerprintService.SaveFingerprints(fingerprints);
    }
    
    #endregion
    
    #region Windows Settings Items
    
    /// <summary>
    /// Retourne une liste de pages de paramètres Windows courantes
    /// pour les rendre accessibles via la recherche primaire (sans préfixe :).
    /// Chaque item utilise un URI ms-settings: ou une commande control panel.
    /// </summary>
    private static List<SearchResult> GetWindowsSettingsItems()
    {
        return
        [
            // === Système ===
            WinSetting("⚙️ Paramètres Windows", "ms-settings:", "Ouvrir les paramètres Windows"),
            WinSetting("🖥️ Affichage", "ms-settings:display", "Résolution, mise à l'échelle, écrans multiples"),
            WinSetting("🔊 Son", "ms-settings:sound", "Volume, périphériques audio, sortie sonore"),
            WinSetting("🔔 Notifications", "ms-settings:notifications", "Notifications et actions rapides"),
            WinSetting("⚡ Alimentation et batterie", "ms-settings:powersleep", "Mode veille, économie d'énergie, alimentation"),
            WinSetting("💾 Stockage", "ms-settings:storagesense", "Espace disque, nettoyage, assistant de stockage"),
            WinSetting("📱 Multitâche", "ms-settings:multitasking", "Bureaux virtuels, ancrage des fenêtres"),
            WinSetting("ℹ️ Informations système", "ms-settings:about", "À propos de votre PC, nom d'ordinateur, spécifications"),
            
            // === Réseau ===
            WinSetting("🌐 Réseau et Internet", "ms-settings:network", "Wi-Fi, Ethernet, VPN, proxy, état du réseau"),
            WinSetting("📶 Wi-Fi", "ms-settings:network-wifi", "Connexions Wi-Fi, réseaux connus"),
            WinSetting("🔒 VPN", "ms-settings:network-vpn", "Connexions VPN"),
            WinSetting("🌐 Proxy", "ms-settings:network-proxy", "Configuration du proxy réseau"),
            
            // === Personnalisation ===
            WinSetting("🎨 Personnalisation", "ms-settings:personalization", "Thème, couleurs, fond d'écran, verrouillage"),
            WinSetting("🖼️ Arrière-plan", "ms-settings:personalization-background", "Fond d'écran, diaporama"),
            WinSetting("🎨 Couleurs", "ms-settings:personalization-colors", "Couleur d'accentuation, mode sombre/clair"),
            WinSetting("🔒 Écran de verrouillage", "ms-settings:lockscreen", "Écran de verrouillage, notifications"),
            WinSetting("📌 Barre des tâches", "ms-settings:taskbar", "Barre des tâches, icônes système"),
            WinSetting("🗔️ Menu Démarrer", "ms-settings:personalization-start", "Disposition du menu Démarrer"),
            
            // === Applications ===
            WinSetting("📦 Applications installées", "ms-settings:appsfeatures", "Désinstaller, déplacer, paramètres d'applications"),
            WinSetting("📦 Applications par défaut", "ms-settings:defaultapps", "Navigateur, lecteur PDF, musique par défaut"),
            WinSetting("🚀 Applications au démarrage", "ms-settings:startupapps", "Gérer les applications qui se lancent au démarrage"),
            
            // === Comptes ===
            WinSetting("👤 Comptes", "ms-settings:yourinfo", "Informations de compte, photo de profil"),
            WinSetting("👥 Famille et autres utilisateurs", "ms-settings:otherusers", "Ajouter des utilisateurs"),
            WinSetting("🔑 Options de connexion", "ms-settings:signinoptions", "Mot de passe, PIN, Windows Hello, empreinte"),
            
            // === Heure et langue ===
            WinSetting("🕒 Date et heure", "ms-settings:dateandtime", "Fuseau horaire, horloge, format de date"),
            WinSetting("🌐 Langue et région", "ms-settings:regionlanguage", "Langue d'affichage, format régional"),
            WinSetting("⌨️ Clavier", "ms-settings:typing", "Saisie, correction automatique, clavier tactile"),
            
            // === Mise à jour et sécurité ===
            WinSetting("🔄 Windows Update", "ms-settings:windowsupdate", "Mises à jour, historique, options avancées"),
            WinSetting("🛡️ Sécurité Windows", "ms-settings:windowsdefender", "Antivirus, pare-feu, protection"),
            WinSetting("💾 Sauvegarde", "ms-settings:backup", "Sauvegarde de fichiers, OneDrive"),
            WinSetting("🔧 Récupération", "ms-settings:recovery", "Réinitialiser le PC, démarrage avancé"),
            
            // === Accessibilité ===
            WinSetting("♿ Accessibilité", "ms-settings:easeofaccess", "Vision, audition, interaction, accessibilité"),
            
            // === Confidentialité ===
            WinSetting("🔒 Confidentialité", "ms-settings:privacy", "Autorisations, diagnostics, historique d'activité"),
            
            // === Périphériques ===
            WinSetting("🖨️ Imprimantes et scanners", "ms-settings:printers", "Ajouter une imprimante, gérer les périphériques d'impression"),
            WinSetting("🖱️ Souris", "ms-settings:mousetouchpad", "Vitesse du curseur, boutons, pavé tactile"),
            WinSetting("📱 Bluetooth", "ms-settings:bluetooth", "Appareils Bluetooth, couplage"),
            
            // === Recherche et indexation (!) ===
            WinSetting("🔍 Options d'indexation", "control|srchadmin.dll", "Indexation Windows, emplacements indexés, reconstruction d'index"),
            WinSetting("🔍 Recherche Windows", "ms-settings:search-permissions", "Autorisations de recherche, indexation, recherche améliorée"),
            WinSetting("🔎 Paramètres de recherche", "ms-settings:cortana-windowssearch", "Recherche Windows, historique de recherche"),
            
            // === Panneau de configuration classique ===
            WinSetting("🛠️ Panneau de configuration", "control|", "Panneau de configuration classique Windows"),
            WinSetting("💻 Gestionnaire de périphériques", "devmgmt.msc", "Pilotes, matériel, périphériques"),
            WinSetting("📀 Gestion des disques", "diskmgmt.msc", "Partitions, volumes, formatage de disques"),
            WinSetting("🔧 Services Windows", "services.msc", "Gérer les services système"),
            WinSetting("📊 Moniteur de performances", "perfmon.msc", "Performances système, compteurs"),
            WinSetting("📃 Événements Windows", "eventvwr.msc", "Observateur d'événements, journaux système"),
            WinSetting("🔥 Pare-feu Windows", "control|firewall.cpl", "Règles de pare-feu, exceptions"),
            WinSetting("🌐 Connexions réseau", "control|ncpa.cpl", "Adaptateurs réseau, IP, DNS"),
            WinSetting("🖥️ Programmes et fonctionnalités", "control|appwiz.cpl", "Désinstaller des programmes, fonctionnalités Windows"),
            WinSetting("👤 Comptes utilisateurs", "control|nusrmgr.cpl", "Gérer les comptes, mots de passe"),
            WinSetting("⚡ Options d'alimentation", "control|powercfg.cpl", "Plans d'alimentation, veille, écran"),
            WinSetting("📡 Centre Réseau et partage", "control|/name Microsoft.NetworkAndSharingCenter", "Partage réseau, groupe résidentiel"),
            WinSetting("📅 Région", "control|intl.cpl", "Format de date, heure, devise, région"),
            WinSetting("⏰ Planificateur de tâches", "taskschd.msc", "Tâches planifiées, automatisation"),
            WinSetting("📦 Fonctionnalités Windows", "control|optionalfeatures", "Activer ou désactiver des fonctionnalités Windows"),
            WinSetting("🎧 Périphériques audio", "control|mmsys.cpl", "Lecture, enregistrement, sons système"),
            WinSetting("🛰️ Connexion Bureau à distance", "mstsc", "Bureau à distance, Remote Desktop"),
            
            // === Paramètres système avancés et outils ===
            WinSetting("⚙️ Paramètres système avancés", "control|sysdm.cpl,,3", "Variables d'environnement, performances, profils utilisateurs, démarrage, mémoire virtuelle"),
            WinSetting("🌐 Propriétés Internet", "control|inetcpl.cpl", "Options Internet, proxy navigateur, sécurité web, cookies, certificats"),
            WinSetting("🔧 Éditeur du registre", "regedit", "Registre Windows, clés, valeurs système, regedit"),
            WinSetting("📊 Informations système détaillées", "msinfo32", "Matériel, composants, BIOS, mémoire RAM, processeur, carte mère"),
            WinSetting("🧹 Nettoyage de disque", "cleanmgr", "Libérer espace disque, fichiers temporaires, cache, corbeille"),
            WinSetting("🔐 Stratégie de sécurité locale", "secpol.msc", "Stratégies de sécurité, audit, droits utilisateurs, mot de passe"),
            WinSetting("📋 Éditeur de stratégie de groupe", "gpedit.msc", "Stratégies de groupe, GPO, configuration Windows, modèles d'administration"),
            WinSetting("🎨 ClearType", "control|cttune", "Réglage ClearType, lissage des polices, texte net"),
            WinSetting("📺 Résolution d'écran", "control|desk.cpl", "Affichage, résolution, orientation, taille du texte"),
            WinSetting("🔊 Mixeur audio", "sndvol", "Volume par application, mixeur de volume, sorties audio"),
            WinSetting("🖨️ Gestion d'impression", "printmanagement.msc", "Imprimantes, files d'attente, serveurs d'impression"),
            WinSetting("💻 Propriétés système", "control|sysdm.cpl", "Nom d'ordinateur, groupe de travail, domaine, matériel, restauration système"),
            WinSetting("🔄 Restauration du système", "rstrui", "Points de restauration, restauration système, récupération"),
            WinSetting("💻 Moniteur de ressources", "resmon", "CPU, mémoire, disque, réseau en temps réel, processus"),
            WinSetting("🔒 Windows Defender Firewall avancé", "wf.msc", "Règles entrantes, sortantes, sécurité connexion, pare-feu avancé"),
            WinSetting("📁 Options des dossiers", "control|folders", "Affichage fichiers cachés, extensions, explorateur de fichiers"),
            WinSetting("⏱️ Diagnostics mémoire", "mdsched", "Test mémoire RAM, diagnostic, erreurs mémoire"),
            WinSetting("📱 Téléphone", "ms-settings:mobile-devices", "Lier téléphone, notifications mobiles, photos"),
            WinSetting("🌍 Paramètres proxy", "ms-settings:network-proxy", "Configuration proxy, proxy automatique, proxy manuel"),
            WinSetting("🔋 Batterie", "ms-settings:batterysaver", "Économie de batterie, utilisation batterie, autonomie"),
        ];
    }
    
    /// <summary>
    /// Crée un SearchResult représentant une page de paramètres Windows.
    /// </summary>
    private static SearchResult WinSetting(string name, string path, string description)
    {
        return new SearchResult
        {
            Name = name,
            Path = path,
            Description = $"⚙️ {description}",
            Type = ResultType.SystemControl,
            Score = 0
        };
    }
    
    #endregion
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        CancelIndexing();
        _indexLock.Dispose();
        _cache.Clear();
        // Note: _fingerprintService est disposé par le conteneur DI
        
        GC.SuppressFinalize(this);
    }
}

// Les interfaces ILogger et FileLogger sont maintenant dans Shared.Logging
