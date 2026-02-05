using System.Diagnostics;
using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Implémentation de <see cref="ISettingsProvider"/> avec cache en mémoire.
/// Remplace les appels dispersés à AppSettings.Load() qui lisaient le disque
/// à chaque recherche (hot path critique pour la performance).
/// </summary>
public sealed class SettingsProvider : ISettingsProvider
{
    private readonly object _lock = new();
    private AppSettings _current;

    public AppSettings Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public event EventHandler<AppSettings>? SettingsChanged;

    public SettingsProvider()
    {
        _current = AppSettings.Load();
        Debug.WriteLine($"[SettingsProvider] Initialisé avec {_current.IndexedFolders.Count} dossiers indexés");
    }

    public void Save()
    {
        AppSettings snapshot;
        lock (_lock)
        {
            _current.Save();
            snapshot = _current;
        }
        
        Debug.WriteLine("[SettingsProvider] Sauvegardé sur disque et notification envoyée");
        SettingsChanged?.Invoke(this, snapshot);
    }

    public void Reload()
    {
        AppSettings loaded;
        lock (_lock)
        {
            _current = AppSettings.Load();
            loaded = _current;
        }
        
        Debug.WriteLine("[SettingsProvider] Rechargé depuis le disque");
        SettingsChanged?.Invoke(this, loaded);
    }

    public void Update(Action<AppSettings> updateAction)
    {
        AppSettings snapshot;
        lock (_lock)
        {
            updateAction(_current);
            _current.Save();
            snapshot = _current;
        }
        
        SettingsChanged?.Invoke(this, snapshot);
    }
}
