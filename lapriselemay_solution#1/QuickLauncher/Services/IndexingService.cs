using System.Collections.Concurrent;
using System.IO;
using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Service d'indexation optimisé avec support du parallélisme et annulation.
/// 
/// <b>Point #6 :</b> L'accès SQLite a été extrait dans <see cref="IIndexRepository"/>.
/// IndexingService conserve la responsabilité de l'orchestration (crawling, smart-start,
/// cache mémoire, événements), tandis que le repository gère toute la persistance.
/// </summary>
public sealed class IndexingService : IDisposable
{
    private readonly ConcurrentDictionary<string, IndexedItem> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private readonly ILogger _logger;
    private readonly ISettingsProvider _settingsProvider;
    private readonly IIndexRepository _repository;
    private readonly FolderFingerprintService _fingerprintService;
    private readonly IStoreAppService _storeAppService;
    private readonly IBookmarkService _bookmarkService;
    private readonly IWindowsSettingsProvider _windowsSettingsProvider;
    private readonly IShortcutHelper _shortcutHelper;
    
    private CancellationTokenSource? _indexingCts;
    private bool _disposed;
    
    public event EventHandler? IndexingStarted;
    public event EventHandler? IndexingCompleted;
    public event EventHandler<int>? IndexingProgress;
    
    public bool IsIndexing { get; private set; }
    public int IndexedItemsCount => _cache.Count;
    
    /// <summary>
    /// Accès en lecture au cache pour le SearchService.
    /// Retourne des <see cref="IndexedItem"/> immutables — aucun risque de data race.
    /// </summary>
    public IReadOnlyDictionary<string, IndexedItem> CachedItems => _cache;

    public IndexingService(ISettingsProvider settingsProvider, IIndexRepository repository,
        FolderFingerprintService fingerprintService, IStoreAppService storeAppService,
        IBookmarkService bookmarkService, IWindowsSettingsProvider windowsSettingsProvider,
        IShortcutHelper shortcutHelper, ILogger? logger = null)
    {
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _fingerprintService = fingerprintService ?? throw new ArgumentNullException(nameof(fingerprintService));
        _storeAppService = storeAppService ?? throw new ArgumentNullException(nameof(storeAppService));
        _bookmarkService = bookmarkService ?? throw new ArgumentNullException(nameof(bookmarkService));
        _windowsSettingsProvider = windowsSettingsProvider ?? throw new ArgumentNullException(nameof(windowsSettingsProvider));
        _shortcutHelper = shortcutHelper ?? throw new ArgumentNullException(nameof(shortcutHelper));
        _logger = logger ?? new FileLogger(Constants.AppName, Constants.LogFileName);
        
        LoadCacheFromRepository();
        _logger.Info($"IndexingService initialisé avec {_cache.Count} éléments en cache");
    }

    /// <summary>
    /// Recharge le cache mémoire depuis le repository.
    /// Accepte un CancellationToken pour permettre un arrêt propre si le shutdown
    /// survient pendant le chargement de milliers de lignes (Point #8).
    /// </summary>
    private void LoadCacheFromRepository(CancellationToken token = default)
    {
        _cache.Clear();
        foreach (var item in _repository.LoadAll(token))
            _cache[item.Path] = item;
    }

    public async Task StartIndexingAsync(CancellationToken cancellationToken = default)
    {
        if (IsIndexing) return;
        
        await _indexLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsIndexing) return;
            
            IsIndexing = true;
            _indexingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            IndexingStarted?.Invoke(this, EventArgs.Empty);
            
            var settings = _settingsProvider.Current;
            _logger.Info("Démarrage de l'indexation...");
            
            var items = new ConcurrentBag<IndexedItem>();
            var token = _indexingCts.Token;
            
            // Indexer les apps Store en parallèle
            var storeTask = Task.Run(() =>
            {
                var storeApps = _storeAppService.GetAllApps();
                foreach (var app in storeApps)
                    items.Add(app);
                _logger.Info($"Apps Store: {storeApps.Count} trouvées");
            }, token);
            
            // Indexer les favoris des navigateurs en parallèle
            var bookmarksTask = Task.Run(() =>
            {
                if (settings.Search.IndexBrowserBookmarks)
                {
                    var bookmarks = _bookmarkService.GetAllBookmarks();
                    foreach (var bookmark in bookmarks)
                        items.Add(bookmark);
                    _logger.Info($"Favoris navigateurs: {bookmarks.Count} trouvés");
                }
            }, token);
            
            // Ajouter les pages de paramètres Windows (Amélioration #3 : extrait dans WindowsSettingsProvider)
            var windowsSettings = _windowsSettingsProvider.GetItems();
            foreach (var ws in windowsSettings)
                items.Add(ws);
            _logger.Info($"Paramètres Windows: {windowsSettings.Count} ajoutés");
            
            // Indexer les dossiers en parallèle
            var folderTasks = settings.Search.IndexedFolders
                .Where(Directory.Exists)
                .Select(folder => Task.Run(() => IndexFolder(folder, items, settings, token), token))
                .ToArray();
            
            await Task.WhenAll([storeTask, bookmarksTask, ..folderTasks]).ConfigureAwait(false);

            // Ajouter les scripts personnalisés
            foreach (var script in settings.Search.Scripts)
            {
                items.Add(IndexedItem.Create(
                    path: script.Command,
                    name: script.Name,
                    description: $"Script: {script.Keyword}",
                    type: ResultType.Script));
            }
            
            // Dédupliquer par (nom + catégorie de type) pour éviter de masquer des fichiers différents portant le même nom.
            // Application et StoreApp sont regroupés dans la même catégorie car un même programme
            // peut apparaître via shell:AppsFolder (StoreApp) ET via un raccourci .lnk (Application).
            // Les items de catégories différentes avec le même nom sont conservés (ex: "Config" fichier + "Config" dossier).
            var deduplicated = items
                .GroupBy(i => (Name: i.Name.ToLowerInvariant(), TypeCategory: DeduplicationHelper.GetCategory(i.Type)))
                .Select(g => g.OrderByDescending(i => i.Type == ResultType.StoreApp ? 1 : 0)
                              .ThenByDescending(i => i.UseCount)
                              .First())
                .ToList();
            
            _logger.Info($"Total: {deduplicated.Count} éléments (après déduplication)");
            
            await _repository.SaveBulkAsync(deduplicated, p => IndexingProgress?.Invoke(this, p), token).ConfigureAwait(false);
            LoadCacheFromRepository(token);
            
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

    private void IndexFolder(string folderPath, ConcurrentBag<IndexedItem> items, AppSettings settings, CancellationToken token)
    {
        var count = 0;
        IndexFolderRecursive(folderPath, items, settings, ref count, 0, token);
        _logger.Info($"Dossier '{Path.GetFileName(folderPath)}': {count} éléments");
    }
    
    private void IndexFolderRecursive(string folderPath, ConcurrentBag<IndexedItem> items,
        AppSettings settings, ref int count, int depth, CancellationToken token)
    {
        if (depth > settings.Search.SearchDepth || token.IsCancellationRequested) return;
        
        try
        {
            var dirInfo = new DirectoryInfo(folderPath);
            if (!settings.Search.IndexHiddenFolders && (dirInfo.Attributes & FileAttributes.Hidden) != 0)
                return;
            
            // Indexer les fichiers
            foreach (var file in dirInfo.EnumerateFiles())
            {
                if (token.IsCancellationRequested) return;
                
                var ext = file.Extension.ToLowerInvariant();
                if (!settings.Search.FileExtensions.Contains(ext)) continue;
                if (!settings.Search.IndexHiddenFolders && (file.Attributes & FileAttributes.Hidden) != 0) continue;
                
                var item = CreateIndexedItem(file.FullName);
                if (item != null)
                {
                    items.Add(item);
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

    private IndexedItem? CreateIndexedItem(string filePath)
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(filePath);
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var description = filePath;
            var targetPath = filePath;
            
            if (ext == ".lnk")
            {
                var info = _shortcutHelper.ResolveShortcut(filePath);
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
            
            return IndexedItem.Create(
                path: filePath,
                name: name,
                description: description,
                type: type);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Erreur CreateIndexedItem '{filePath}': {ex.Message}");
            return null;
        }
    }
    

    public async Task ReindexAsync(CancellationToken cancellationToken = default)
    {
        CancelIndexing();
        
        // Ne PAS vider la table avant la réindexation.
        // SaveBulkAsync utilise INSERT OR REPLACE + COALESCE pour préserver UseCount/LastUsed.
        // Après l'indexation, on purge les items stale (disparus) via leur IndexedAt.
        _cache.Clear();
        
        // Purger le cache de distances fuzzy pour éviter l'accumulation d'entrées périmées
        SearchAlgorithms.ClearCache();
        
        await StartIndexingAsync(cancellationToken).ConfigureAwait(false);
        
        // Supprimer les items qui n'ont pas été touchés par cette indexation
        // (fichiers supprimés, déplacés ou renommés depuis la dernière réindexation).
        await PurgeStaleItemsAsync(cancellationToken).ConfigureAwait(false);
    }
    
    /// <summary>
    /// Supprime de la DB les items dont IndexedAt est antérieur au dernier run.
    /// Appelé après une réindexation complète pour nettoyer les entrées obsolètes
    /// sans perdre les UseCount/LastUsed des items toujours présents.
    /// </summary>
    private async Task PurgeStaleItemsAsync(CancellationToken token)
    {
        try
        {
            var purged = await _repository.PurgeStaleAsync(token).ConfigureAwait(false);
            if (purged > 0)
            {
                _logger.Info($"[Reindex] Purgé {purged} items obsolètes");
                LoadCacheFromRepository(token);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Erreur PurgeStaleItemsAsync: {ex.Message}");
        }
    }
    
    public void CancelIndexing() => _indexingCts?.Cancel();

    // Search(), CalculateScore() et TryCalculate() ont été déplacés
    // vers SearchService et CalculatorService (Amélioration #3 et #7).
    // Utiliser SearchService.Search() à la place.
    
    // Déduplication centralisée dans DeduplicationHelper.
    
    /// <summary>
    /// Enregistre un usage pour un item donné par son chemin.
    /// Met à jour la DB et effectue un swap atomique dans le cache
    /// via <see cref="IndexedItem.WithUsageRecorded"/> (immutable, aucun data race).
    /// </summary>
    public void RecordUsage(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        
        try
        {
            _repository.RecordUsage(path);
            
            // Swap atomique : crée un nouvel IndexedItem immutable avec UseCount+1.
            // Les threads de recherche en cours continuent de lire l'ancien objet sans risque.
            if (_cache.TryGetValue(path, out var cached))
            {
                _cache.TryUpdate(path, cached.WithUsageRecorded(), cached);
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
            var item = CreateIndexedItem(filePath);
            if (item == null) return;
            
            _repository.AddOrUpdate(item);
            _cache[item.Path] = item;
            _logger.Info($"[Incremental] Ajouté/MàJ: {item.Name}");
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
            var removed = _repository.Remove(filePath);
            
            _cache.TryRemove(filePath, out _);
            IconCacheService.Invalidate(filePath);
            
            if (removed)
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
            await StartIndexingAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        
        _logger.Info($"[SmartIndex] Cache chargé avec {_cache.Count} éléments. Vérification des changements...");
        
        var settings = _settingsProvider.Current;
        var comparison = _fingerprintService.CompareWithStored(
            settings.Search.IndexedFolders,
            settings.Search.FileExtensions,
            settings.Search.SearchDepth,
            settings.Search.IndexHiddenFolders);
        
        if (!comparison.HasChanges)
        {
            _logger.Info("[SmartIndex] Aucun changement de dossiers — rafraîchissement des sources volatiles uniquement...");
            
            // Toujours rafraîchir les sources volatiles (Store apps, bookmarks, paramètres Windows)
            // même si les dossiers n'ont pas changé, pour garantir qu'elles sont à jour.
            await RefreshVolatileSourcesAsync(settings, cancellationToken).ConfigureAwait(false);
            
            IndexingCompleted?.Invoke(this, EventArgs.Empty);
            return;
        }
        
        _logger.Info($"[SmartIndex] Changements: {comparison.NewFolders.Count} nouveaux, " +
                     $"{comparison.ModifiedFolders.Count} modifiés, {comparison.DeletedFolders.Count} supprimés");
        
        await _indexLock.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                var items = new ConcurrentBag<IndexedItem>();
                
                // Pour les dossiers modifiés, supprimer les anciens items d'abord
                foreach (var modifiedFolder in comparison.ModifiedFolders)
                {
                    RemoveItemsByFolder(modifiedFolder);
                }
                
                // Réindexer en parallèle
                var folderTasks = foldersToIndex
                    .Select(folder => Task.Run(() => IndexFolder(folder, items, settings, token), token))
                    .ToArray();
                
                await Task.WhenAll(folderTasks).ConfigureAwait(false);
                
                // Sauvegarder les nouveaux items
                var newItems = items.ToList();
                if (newItems.Count > 0)
                {
                    await _repository.SaveBulkAsync(newItems, null, token).ConfigureAwait(false);
                    LoadCacheFromRepository(token);
                }
                
                _logger.Info($"[SmartIndex] {newItems.Count} éléments réindexés");
            }
            
            // 3. Réindexer les bookmarks et Store apps (rapide, toujours frais)
            await RefreshVolatileSourcesAsync(settings, _indexingCts.Token).ConfigureAwait(false);
            
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
                var deleted = _repository.RemoveByFolder(folderPath);
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
        var items = new ConcurrentBag<IndexedItem>();
        
        var storeTask = Task.Run(() =>
        {
            var storeApps = _storeAppService.GetAllApps();
            foreach (var app in storeApps) items.Add(app);
        }, token);
        
        var bookmarksTask = Task.Run(() =>
        {
            if (settings.Search.IndexBrowserBookmarks)
            {
                var bookmarks = _bookmarkService.GetAllBookmarks();
                foreach (var bm in bookmarks) items.Add(bm);
            }
        }, token);
        
        await Task.WhenAll(storeTask, bookmarksTask).ConfigureAwait(false);
        
        // Ajouter les pages de paramètres Windows (toujours présentes)
        var windowsSettings = _windowsSettingsProvider.GetItems();
        foreach (var ws in windowsSettings)
            items.Add(ws);
        
        // Supprimer les anciens Store apps, bookmarks et SystemControl du cache ET de la DB
        var volatileTypes = new[] { ResultType.StoreApp, ResultType.Bookmark, ResultType.SystemControl };
        var keysToRemove = _cache
            .Where(kv => volatileTypes.Contains(kv.Value.Type))
            .Select(kv => kv.Key)
            .ToList();
        
        foreach (var key in keysToRemove)
            _cache.TryRemove(key, out _);
        
        // Purger les entrées volatiles PÉRIMÉES de la base de données.
        // On ne supprime que celles dont le Path n'existe plus dans le nouveau set,
        // pour éviter que des entrées fantômes (ex: app réinstallée avec un nouveau AppUserModelId)
        // ne réapparaissent au prochain LoadCacheFromRepository() tout en préservant le UseCount
        // des entrées toujours valides.
        try
        {
            var freshPaths = new HashSet<string>(items.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
            var stalePaths = keysToRemove.Where(k => !freshPaths.Contains(k)).ToList();
            
            if (stalePaths.Count > 0)
            {
                var purged = await _repository.PurgePathsAsync(stalePaths, token).ConfigureAwait(false);
                _logger.Info($"[Volatile] Purgé {purged} entrées volatiles périmées de la DB");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Erreur purge volatiles: {ex.Message}");
        }
        
        // Sauvegarder les items frais
        var newItems = items.ToList();
        if (newItems.Count > 0)
        {
            await _repository.SaveBulkAsync(newItems, null, token).ConfigureAwait(false);
            LoadCacheFromRepository(token);
        }
    }
    
    /// <summary>
    /// Sauvegarde les fingerprints pour tous les dossiers indexés actuels.
    /// </summary>
    private void SaveCurrentFingerprints(AppSettings settings)
    {
        var fingerprints = settings.Search.IndexedFolders
            .Where(Directory.Exists)
            .Select(folder => _fingerprintService.ComputeFingerprint(
                folder, settings.Search.FileExtensions, settings.Search.SearchDepth, settings.Search.IndexHiddenFolders))
            .ToList();
        
        _fingerprintService.SaveFingerprints(fingerprints);
    }
    
    #endregion
    
    // Région Windows Settings Items supprimée — déplacée dans WindowsSettingsProvider (Amélioration #3).
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        CancelIndexing();
        _indexLock.Dispose();
        _cache.Clear();
        
        // Le repository (IIndexRepository) gère sa propre connexion SQLite
        // et est disposé par le conteneur DI.
        // _fingerprintService idem.
        GC.SuppressFinalize(this);
    }
}

