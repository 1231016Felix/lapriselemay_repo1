using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace QuickLauncher;

/// <summary>
/// ObservableCollection qui supporte le remplacement en lot (ReplaceAll)
/// avec une seule notification CollectionChanged.Reset.
/// 
/// Élimine le flash UI causé par Clear() + N × Add() qui déclenchent N+1
/// notifications individuelles de changement de collection.
/// </summary>
public sealed class BatchObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotifications;

    /// <summary>
    /// Remplace tout le contenu de la collection en une seule opération.
    /// Émet un unique événement <see cref="NotifyCollectionChangedAction.Reset"/>.
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        _suppressNotifications = true;
        try
        {
            Items.Clear();
            foreach (var item in items)
                Items.Add(item);
        }
        finally
        {
            _suppressNotifications = false;
        }

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotifications)
            base.OnCollectionChanged(e);
    }
}
