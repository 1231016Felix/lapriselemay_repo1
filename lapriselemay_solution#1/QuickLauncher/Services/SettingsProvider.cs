using System.Diagnostics;
using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Implémentation de <see cref="ISettingsProvider"/> avec cache en mémoire.
/// Remplace les appels dispersés à AppSettings.Load() qui lisaient le disque
/// à chaque recherche (hot path critique pour la performance).
/// 
/// Contrat de threading :
/// - <see cref="Current"/> retourne une référence atomique (volatile) et peut être lu depuis n'importe quel thread.
/// - Les mutations de l'objet retourné (via ses propriétés) doivent se faire depuis le UI thread uniquement.
/// - <see cref="Save"/>, <see cref="Reload"/> et <see cref="Update"/> doivent être appelés depuis le UI thread.
/// - Les lectures concurrentes depuis des threads de recherche sont sûres tant qu'elles ne mutent pas l'objet.
/// </summary>
public sealed class SettingsProvider : ISettingsProvider
{
    /// <summary>
    /// Référence volatile : les lectures/écritures de la référence sont atomiques et
    /// visibles immédiatement par tous les threads sans lock.
    /// Plus performant que lock pour le pattern read-mostly (hot path de recherche).
    /// </summary>
    private volatile AppSettings _current;

    public AppSettings Current => _current;

    public event EventHandler<AppSettings>? SettingsChanged;

    public SettingsProvider()
    {
        _current = AppSettings.Load();
        Debug.WriteLine($"[SettingsProvider] Initialisé avec {_current.Search.IndexedFolders.Count} dossiers indexés");
    }

    public void Save()
    {
        _current.Save();
        Debug.WriteLine("[SettingsProvider] Sauvegardé sur disque et notification envoyée");
        SettingsChanged?.Invoke(this, _current);
    }

    public void Reload()
    {
        _current = AppSettings.Load();
        Debug.WriteLine("[SettingsProvider] Rechargé depuis le disque");
        SettingsChanged?.Invoke(this, _current);
    }

    public void Update(Action<AppSettings> updateAction)
    {
        updateAction(_current);
        _current.Save();
        SettingsChanged?.Invoke(this, _current);
    }
}
