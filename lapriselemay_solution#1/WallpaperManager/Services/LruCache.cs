using System.Collections.Concurrent;

namespace WallpaperManager.Services;

/// <summary>
/// Cache LRU (Least Recently Used) thread-safe avec limite de taille en bytes.
/// Optimisé pour le stockage de BitmapSource et autres objets volumineux.
/// </summary>
/// <typeparam name="TKey">Type de la clé</typeparam>
/// <typeparam name="TValue">Type de la valeur</typeparam>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, CacheEntry> _cache = new();
    private readonly LinkedList<TKey> _lruList = new();
    private readonly Lock _lruLock = new();
    private readonly long _maxSizeBytes;
    private readonly Func<TValue, long> _sizeEstimator;
    private long _currentSizeBytes;
    
    /// <summary>
    /// Nombre d'éléments dans le cache.
    /// </summary>
    public int Count => _cache.Count;
    
    /// <summary>
    /// Taille actuelle du cache en bytes.
    /// </summary>
    public long CurrentSizeBytes => Interlocked.Read(ref _currentSizeBytes);
    
    /// <summary>
    /// Taille maximale du cache en bytes.
    /// </summary>
    public long MaxSizeBytes => _maxSizeBytes;
    
    /// <summary>
    /// Pourcentage d'utilisation du cache (0-100).
    /// </summary>
    public double UsagePercent => _maxSizeBytes > 0 
        ? (CurrentSizeBytes * 100.0) / _maxSizeBytes 
        : 0;
    
    /// <summary>
    /// Crée un nouveau cache LRU.
    /// </summary>
    /// <param name="maxSizeBytes">Taille maximale en bytes</param>
    /// <param name="sizeEstimator">Fonction pour estimer la taille d'une valeur en bytes</param>
    public LruCache(long maxSizeBytes, Func<TValue, long> sizeEstimator)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSizeBytes);
        ArgumentNullException.ThrowIfNull(sizeEstimator);
        
        _maxSizeBytes = maxSizeBytes;
        _sizeEstimator = sizeEstimator;
    }
    
    /// <summary>
    /// Tente de récupérer une valeur du cache.
    /// Met à jour la position LRU si trouvé.
    /// </summary>
    public bool TryGet(TKey key, out TValue? value)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            // Mettre à jour le timestamp d'accès
            entry.LastAccess = DateTime.UtcNow;
            
            // Déplacer en tête de la liste LRU
            lock (_lruLock)
            {
                if (entry.Node != null)
                {
                    _lruList.Remove(entry.Node);
                    _lruList.AddFirst(entry.Node);
                }
            }
            
            value = entry.Value;
            return true;
        }
        
        value = default;
        return false;
    }
    
    /// <summary>
    /// Ajoute ou met à jour une valeur dans le cache.
    /// Évince automatiquement les éléments les moins récemment utilisés si nécessaire.
    /// </summary>
    public void Set(TKey key, TValue value)
    {
        var size = _sizeEstimator(value);
        
        // Si la valeur seule dépasse la limite, ne pas l'ajouter
        if (size > _maxSizeBytes)
        {
            System.Diagnostics.Debug.WriteLine($"LruCache: Valeur trop grande ({size:N0} bytes > {_maxSizeBytes:N0} bytes max)");
            return;
        }
        
        // Supprimer l'ancienne entrée si elle existe
        if (_cache.TryRemove(key, out var oldEntry))
        {
            Interlocked.Add(ref _currentSizeBytes, -oldEntry.Size);
            lock (_lruLock)
            {
                if (oldEntry.Node != null)
                    _lruList.Remove(oldEntry.Node);
            }
        }
        
        // Faire de la place si nécessaire
        EnsureCapacity(size);
        
        // Créer la nouvelle entrée
        var newEntry = new CacheEntry
        {
            Value = value,
            Size = size,
            LastAccess = DateTime.UtcNow
        };
        
        lock (_lruLock)
        {
            newEntry.Node = _lruList.AddFirst(key);
        }
        
        if (_cache.TryAdd(key, newEntry))
        {
            Interlocked.Add(ref _currentSizeBytes, size);
        }
        else
        {
            // Clé ajoutée par un autre thread entre-temps
            lock (_lruLock)
            {
                if (newEntry.Node != null)
                    _lruList.Remove(newEntry.Node);
            }
        }
    }
    
    /// <summary>
    /// Supprime une entrée du cache.
    /// </summary>
    public bool Remove(TKey key)
    {
        if (_cache.TryRemove(key, out var entry))
        {
            Interlocked.Add(ref _currentSizeBytes, -entry.Size);
            lock (_lruLock)
            {
                if (entry.Node != null)
                    _lruList.Remove(entry.Node);
            }
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Vide complètement le cache.
    /// </summary>
    public void Clear()
    {
        lock (_lruLock)
        {
            _cache.Clear();
            _lruList.Clear();
            Interlocked.Exchange(ref _currentSizeBytes, 0);
        }
    }
    
    /// <summary>
    /// Évince les entrées non accédées depuis plus longtemps que la durée spécifiée.
    /// </summary>
    /// <param name="maxAge">Durée maximale depuis le dernier accès</param>
    /// <returns>Nombre d'entrées évincées</returns>
    public int EvictOlderThan(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var evicted = 0;
        var keysToRemove = new List<TKey>();
        
        foreach (var kvp in _cache)
        {
            if (kvp.Value.LastAccess < cutoff)
            {
                keysToRemove.Add(kvp.Key);
            }
        }
        
        foreach (var key in keysToRemove)
        {
            if (Remove(key))
                evicted++;
        }
        
        if (evicted > 0)
        {
            System.Diagnostics.Debug.WriteLine($"LruCache: {evicted} entrée(s) évincée(s) (âge > {maxAge.TotalMinutes:F0} min)");
        }
        
        return evicted;
    }
    
    /// <summary>
    /// Réduit le cache à un pourcentage de sa taille maximale.
    /// </summary>
    /// <param name="targetPercent">Pourcentage cible (0-100)</param>
    /// <returns>Nombre d'entrées évincées</returns>
    public int TrimToPercent(int targetPercent)
    {
        targetPercent = Math.Clamp(targetPercent, 0, 100);
        var targetSize = (_maxSizeBytes * targetPercent) / 100;
        return TrimToSize(targetSize);
    }
    
    /// <summary>
    /// Réduit le cache à une taille cible.
    /// </summary>
    /// <param name="targetSizeBytes">Taille cible en bytes</param>
    /// <returns>Nombre d'entrées évincées</returns>
    public int TrimToSize(long targetSizeBytes)
    {
        var evicted = 0;
        
        while (CurrentSizeBytes > targetSizeBytes)
        {
            TKey? keyToRemove = default;
            
            lock (_lruLock)
            {
                if (_lruList.Last != null)
                {
                    keyToRemove = _lruList.Last.Value;
                }
            }
            
            if (keyToRemove == null)
                break;
            
            if (Remove(keyToRemove))
                evicted++;
            else
                break; // Éviter boucle infinie
        }
        
        return evicted;
    }
    
    /// <summary>
    /// Retourne les statistiques du cache.
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        return new CacheStatistics
        {
            Count = Count,
            CurrentSizeBytes = CurrentSizeBytes,
            MaxSizeBytes = MaxSizeBytes,
            UsagePercent = UsagePercent
        };
    }
    
    private void EnsureCapacity(long requiredSize)
    {
        var targetSize = _maxSizeBytes - requiredSize;
        
        while (CurrentSizeBytes > targetSize)
        {
            TKey? keyToRemove = default;
            
            lock (_lruLock)
            {
                if (_lruList.Last != null)
                {
                    keyToRemove = _lruList.Last.Value;
                }
            }
            
            if (keyToRemove == null)
                break;
            
            if (!Remove(keyToRemove))
                break; // Éviter boucle infinie
        }
    }
    
    private sealed class CacheEntry
    {
        public TValue Value { get; init; } = default!;
        public long Size { get; init; }
        public DateTime LastAccess { get; set; }
        public LinkedListNode<TKey>? Node { get; set; }
    }
}

/// <summary>
/// Statistiques du cache LRU.
/// </summary>
public readonly struct CacheStatistics
{
    public int Count { get; init; }
    public long CurrentSizeBytes { get; init; }
    public long MaxSizeBytes { get; init; }
    public double UsagePercent { get; init; }
    
    public string FormattedSize => FormatBytes(CurrentSizeBytes);
    public string FormattedMaxSize => FormatBytes(MaxSizeBytes);
    
    private static string FormatBytes(long bytes)
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
