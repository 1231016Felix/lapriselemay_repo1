using System.Diagnostics;
using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Implémentation de <see cref="ISettingsProvider"/> avec cache en mémoire
/// et pattern clone-swap pour la thread safety.
/// 
/// <b>Point #7 — Debounce des sauvegardes disque :</b>
/// Chaque appel à <see cref="Update"/> mute le snapshot en mémoire immédiatement (clone-swap),
/// mais reporte l'écriture disque via un timer. Si plusieurs mutations arrivent en rafale
/// (ex: SaveWindowPosition à chaque hide, RecordUsage dans l'historique), une seule écriture
/// est effectuée après <see cref="SaveDebounceMs"/> ms d'inactivité.
/// 
/// <see cref="Save"/> et <see cref="Dispose"/> forcent un flush immédiat pour garantir
/// que les données ne sont jamais perdues au shutdown.
/// 
/// <b>Contrat de threading :</b>
/// <list type="bullet">
///   <item><see cref="Current"/> retourne une référence atomique (volatile) lisible depuis
///         n'importe quel thread. Le snapshot est stable et jamais muté après publication.</item>
///   <item><see cref="Update"/> clone → mute → swap atomique. Le timer debounce l'écriture disque.</item>
///   <item><see cref="Save"/>, <see cref="Reload"/> doivent être appelés depuis le UI thread.</item>
/// </list>
/// </summary>
public sealed class SettingsProvider : ISettingsProvider, IDisposable
{
    private volatile AppSettings _current;
    private volatile bool _isDirty;
    private readonly System.Threading.Timer _saveTimer;
    private bool _disposed;
    
    /// <summary>
    /// Délai de debounce en millisecondes avant l'écriture disque.
    /// Chaque nouvelle mutation remet le timer à zéro.
    /// </summary>
    private const int SaveDebounceMs = 5_000;

    public AppSettings Current => _current;

    public event EventHandler<AppSettings>? SettingsChanged;

    public SettingsProvider()
    {
        _current = AppSettings.Load();
        _current.Search.Freeze(); // Point #9 : geler le snapshot initial
        _saveTimer = new System.Threading.Timer(OnSaveTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
        Trace.WriteLine($"[SettingsProvider] Initialisé avec {_current.Search.IndexedFolders.Count} dossiers indexés");
    }

    /// <summary>
    /// Force une sauvegarde immédiate sur disque et notifie les abonnés.
    /// Utilisé avant l'ouverture de la fenêtre de paramètres ou tout contexte
    /// nécessitant que le fichier disque soit synchronisé.
    /// </summary>
    public void Save()
    {
        FlushToDisk();
        Trace.WriteLine("[SettingsProvider] Sauvegardé sur disque (Save explicite)");
        SettingsChanged?.Invoke(this, _current);
    }

    public void Reload()
    {
        _isDirty = false;
        _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _current = AppSettings.Load();
        _current.Search.Freeze(); // Point #9 : geler le snapshot rechargé
        Trace.WriteLine("[SettingsProvider] Rechargé depuis le disque");
        SettingsChanged?.Invoke(this, _current);
    }

    /// <summary>
    /// Applique une modification aux paramètres via le pattern clone-swap :
    /// 1. Clone l'objet courant (copie profonde)
    /// 2. Applique la mutation sur le clone
    /// 3. Swap la référence volatile (publication atomique — immédiat)
    /// 4. Reporte l'écriture disque via timer debounce (5s)
    /// 
    /// Les threads de recherche qui tenaient une référence à l'ancien objet
    /// continuent de le lire sans risque de données incohérentes.
    /// </summary>
    public void Update(Action<AppSettings> updateAction)
    {
        var original = _current;
        var clone = original.Clone();
        
        // En Debug, vérifie que Clone() n'a laissé aucune référence partagée.
        CloneVerifier.AssertDeepClone(original, clone, "AppSettings");
        CloneVerifier.AssertDeepClone(original.Search, clone.Search, "SearchSettings");
        CloneVerifier.AssertDeepClone(original.Integrations, clone.Integrations, "IntegrationSettings");
        
        updateAction(clone);
        clone.Search.Freeze(); // Point #9 : geler avant publication
        _current = clone;
        
        // Marquer dirty et (re)démarrer le timer de sauvegarde.
        // Si un Update() précédent avait déjà armé le timer, il est repoussé.
        _isDirty = true;
        _saveTimer.Change(SaveDebounceMs, Timeout.Infinite);
        
        SettingsChanged?.Invoke(this, clone);
    }
    
    /// <summary>
    /// Callback du timer : écrit sur disque après la période de calme.
    /// Exécuté sur un thread ThreadPool — <see cref="AppSettings.Save"/> est thread-safe
    /// (écriture atomique via .tmp + Move).
    /// </summary>
    private void OnSaveTimerElapsed(object? state)
    {
        FlushToDisk();
    }
    
    /// <summary>
    /// Écrit les paramètres sur disque de manière synchrone si des modifications sont en attente.
    /// Idempotent : peut être appelé plusieurs fois sans effet si déjà flush.
    /// 
    /// Utilise <see cref="AppSettings.SaveSync"/> (bloquant) au lieu de
    /// <see cref="AppSettings.Save"/> (fire-and-forget) pour garantir que
    /// l'écriture est terminée au retour. C'est sûr car FlushToDisk est toujours
    /// appelé depuis un thread non-UI (callback timer ThreadPool ou Dispose).
    /// </summary>
    private void FlushToDisk()
    {
        if (!_isDirty) return;
        _isDirty = false;
        _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _current.SaveSync();
        Trace.WriteLine("[SettingsProvider] Flush disque (debounce)");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        // Flush AVANT de disposer le timer : FlushToDisk() appelle
        // _saveTimer.Change(Infinite) pour désarmer le callback,
        // ce qui lèverait ObjectDisposedException si le timer était déjà mort.
        FlushToDisk();
        _saveTimer.Dispose();
    }
}
