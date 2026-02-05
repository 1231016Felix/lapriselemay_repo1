namespace QuickLauncher.Services;

/// <summary>
/// Cache LRU (Least Recently Used) thread-safe avec capacité fixe.
/// Remplace le pattern ConcurrentDictionary + Clear() brutal utilisé précédemment
/// dans SearchAlgorithms, qui perdait tout le cache d'un coup à 10 000 entrées.
/// 
/// Ici, les entrées les moins récemment utilisées sont évincées une par une
/// quand la capacité est atteinte, préservant les résultats fréquents.
/// </summary>
/// <typeparam name="TKey">Type de la clé.</typeparam>
/// <typeparam name="TValue">Type de la valeur.</typeparam>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<CacheEntry>> _map;
    private readonly LinkedList<CacheEntry> _list = new();
    private readonly object _lock = new();

    /// <summary>
    /// Nombre d'entrées actuellement dans le cache.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _map.Count;
            }
        }
    }

    /// <summary>
    /// Crée un cache LRU avec la capacité spécifiée.
    /// </summary>
    /// <param name="capacity">Nombre maximum d'entrées.</param>
    /// <param name="comparer">Comparateur optionnel pour les clés.</param>
    public LruCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<CacheEntry>>(capacity, comparer);
    }

    /// <summary>
    /// Tente de récupérer une valeur du cache.
    /// Si trouvée, l'entrée est promue en tête (Most Recently Used).
    /// </summary>
    public bool TryGet(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                // Promouvoir en tête (MRU)
                _list.Remove(node);
                _list.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }

        value = default!;
        return false;
    }

    /// <summary>
    /// Ajoute ou met à jour une entrée dans le cache.
    /// Si la capacité est atteinte, l'entrée la moins récemment utilisée est évincée.
    /// </summary>
    public void Set(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existingNode))
            {
                // Mise à jour : modifier la valeur et promouvoir en tête
                existingNode.Value = new CacheEntry(key, value);
                _list.Remove(existingNode);
                _list.AddFirst(existingNode);
                return;
            }

            // Éviction si capacité atteinte
            if (_map.Count >= _capacity)
            {
                var lru = _list.Last!;
                _map.Remove(lru.Value.Key);
                _list.RemoveLast();
            }

            // Ajouter la nouvelle entrée en tête
            var entry = new CacheEntry(key, value);
            var newNode = _list.AddFirst(entry);
            _map[key] = newNode;
        }
    }

    /// <summary>
    /// Vide le cache.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _map.Clear();
            _list.Clear();
        }
    }

    /// <summary>
    /// Entrée interne du cache associant clé et valeur.
    /// </summary>
    private record struct CacheEntry(TKey Key, TValue Value);
}
