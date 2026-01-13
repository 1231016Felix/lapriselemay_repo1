using System.Collections.ObjectModel;

namespace Shared.Core.Extensions;

/// <summary>
/// Extensions pour ObservableCollection permettant des mises à jour efficaces.
/// </summary>
public static class ObservableCollectionExtensions
{
    /// <summary>
    /// Remplace tous les éléments de la collection par ceux fournis.
    /// Plus efficace que de recréer une nouvelle collection car préserve les bindings.
    /// </summary>
    /// <typeparam name="T">Type des éléments</typeparam>
    /// <param name="collection">Collection à modifier</param>
    /// <param name="items">Nouveaux éléments</param>
    public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(items);
        
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
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
        
        var toRemove = collection.Where(predicate).ToList();
        foreach (var item in toRemove)
        {
            collection.Remove(item);
        }
        return toRemove.Count;
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
}
