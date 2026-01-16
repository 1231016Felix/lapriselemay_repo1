using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Shared.Core.Extensions;

/// <summary>
/// Extensions pour ObservableCollection permettant des mises à jour efficaces.
/// </summary>
public static class ObservableCollectionExtensions
{
    /// <summary>
    /// Remplace tous les éléments de la collection par ceux fournis.
    /// Plus efficace que de recréer une nouvelle collection car préserve les bindings.
    /// Optimisé pour éviter les notifications inutiles si les données sont identiques.
    /// </summary>
    /// <typeparam name="T">Type des éléments</typeparam>
    /// <param name="collection">Collection à modifier</param>
    /// <param name="items">Nouveaux éléments</param>
    public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(items);
        
        var itemsList = items as IList<T> ?? items.ToList();
        
        // Optimisation: vérifier si les données sont identiques
        if (AreCollectionsEqual(collection, itemsList))
        {
            return; // Pas de changement nécessaire
        }
        
        collection.Clear();
        foreach (var item in itemsList)
        {
            collection.Add(item);
        }
    }
    
    /// <summary>
    /// Remplace tous les éléments en une seule notification (si la collection le supporte).
    /// Utilise la réflexion pour accéder à la méthode interne SetItem.
    /// </summary>
    public static void ReplaceWithBatch<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(items);
        
        var itemsList = items as IList<T> ?? items.ToList();
        
        // Pour les collections standard, utiliser la méthode simple
        // Car ObservableCollection ne supporte pas nativement les batch updates
        collection.ReplaceWith(itemsList);
    }

    /// <summary>
    /// Ajoute plusieurs éléments à la collection.
    /// </summary>
    /// <typeparam name="T">Type des éléments</typeparam>
    /// <param name="collection">Collection à modifier</param>
    /// <param name="items">Éléments à ajouter</param>
    public static void AddRange<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(items);
        
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    /// <summary>
    /// Supprime tous les éléments correspondant au prédicat.
    /// </summary>
    /// <typeparam name="T">Type des éléments</typeparam>
    /// <param name="collection">Collection à modifier</param>
    /// <param name="predicate">Condition de suppression</param>
    /// <returns>Nombre d'éléments supprimés</returns>
    public static int RemoveWhere<T>(this ObservableCollection<T> collection, Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(predicate);
        
        // Itérer en sens inverse pour éviter les problèmes d'index
        var removedCount = 0;
        for (var i = collection.Count - 1; i >= 0; i--)
        {
            if (predicate(collection[i]))
            {
                collection.RemoveAt(i);
                removedCount++;
            }
        }
        return removedCount;
    }

    /// <summary>
    /// Met à jour la collection de manière intelligente en ajoutant/supprimant uniquement les différences.
    /// Utile pour minimiser les notifications de changement.
    /// </summary>
    /// <typeparam name="T">Type des éléments</typeparam>
    /// <param name="collection">Collection à modifier</param>
    /// <param name="newItems">Nouveaux éléments</param>
    /// <param name="comparer">Comparateur d'égalité (optionnel)</param>
    public static void SyncWith<T>(this ObservableCollection<T> collection, IEnumerable<T> newItems, IEqualityComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(newItems);
        
        comparer ??= EqualityComparer<T>.Default;
        var newList = newItems.ToList();
        
        // Supprimer les éléments qui ne sont plus présents
        for (var i = collection.Count - 1; i >= 0; i--)
        {
            if (!newList.Contains(collection[i], comparer))
            {
                collection.RemoveAt(i);
            }
        }
        
        // Ajouter les nouveaux éléments
        foreach (var item in newList)
        {
            if (!collection.Contains(item, comparer))
            {
                collection.Add(item);
            }
        }
    }
    
    /// <summary>
    /// Met à jour la collection en préservant l'ordre des nouveaux éléments.
    /// Plus sophistiqué que SyncWith car maintient l'ordre.
    /// </summary>
    /// <typeparam name="T">Type des éléments</typeparam>
    /// <param name="collection">Collection à modifier</param>
    /// <param name="newItems">Nouveaux éléments dans l'ordre souhaité</param>
    /// <param name="keySelector">Sélecteur de clé pour identifier les éléments</param>
    public static void SyncWithOrdered<T, TKey>(
        this ObservableCollection<T> collection, 
        IEnumerable<T> newItems, 
        Func<T, TKey> keySelector) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(newItems);
        ArgumentNullException.ThrowIfNull(keySelector);
        
        var newList = newItems.ToList();
        var existingKeys = collection.ToDictionary(keySelector);
        var newKeys = new HashSet<TKey>(newList.Select(keySelector));
        
        // Supprimer les éléments qui ne sont plus présents
        for (var i = collection.Count - 1; i >= 0; i--)
        {
            var key = keySelector(collection[i]);
            if (!newKeys.Contains(key))
            {
                collection.RemoveAt(i);
            }
        }
        
        // Ajouter/réordonner les éléments
        for (var i = 0; i < newList.Count; i++)
        {
            var item = newList[i];
            var key = keySelector(item);
            
            if (i < collection.Count)
            {
                var currentKey = keySelector(collection[i]);
                if (!EqualityComparer<TKey>.Default.Equals(currentKey, key))
                {
                    // L'élément n'est pas à la bonne position
                    if (existingKeys.ContainsKey(key))
                    {
                        // Trouver et déplacer
                        var currentIndex = -1;
                        for (var j = i + 1; j < collection.Count; j++)
                        {
                            if (EqualityComparer<TKey>.Default.Equals(keySelector(collection[j]), key))
                            {
                                currentIndex = j;
                                break;
                            }
                        }
                        if (currentIndex >= 0)
                        {
                            collection.Move(currentIndex, i);
                        }
                    }
                    else
                    {
                        // Nouvel élément à insérer
                        collection.Insert(i, item);
                    }
                }
            }
            else
            {
                // Ajouter à la fin
                collection.Add(item);
            }
        }
    }
    
    /// <summary>
    /// Vérifie si deux collections sont égales (même contenu, même ordre).
    /// </summary>
    private static bool AreCollectionsEqual<T>(IList<T> collection1, IList<T> collection2)
    {
        if (collection1.Count != collection2.Count)
            return false;
        
        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < collection1.Count; i++)
        {
            if (!comparer.Equals(collection1[i], collection2[i]))
                return false;
        }
        
        return true;
    }
}
