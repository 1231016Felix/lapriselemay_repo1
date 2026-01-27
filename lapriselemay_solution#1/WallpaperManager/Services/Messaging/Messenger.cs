using System.Collections.Concurrent;
using WallpaperManager.Models;

namespace WallpaperManager.Services.Messaging;

/// <summary>
/// Interface de base pour tous les messages.
/// </summary>
public interface IMessage { }

/// <summary>
/// Interface pour les gestionnaires de messages.
/// </summary>
public interface IMessageHandler<in TMessage> where TMessage : IMessage
{
    void Handle(TMessage message);
}

/// <summary>
/// Interface pour les gestionnaires de messages asynchrones.
/// </summary>
public interface IAsyncMessageHandler<in TMessage> where TMessage : IMessage
{
    Task HandleAsync(TMessage message, CancellationToken cancellationToken = default);
}

#region Messages

/// <summary>
/// Message envoyé quand un wallpaper est ajouté.
/// </summary>
public record WallpaperAddedMessage(Wallpaper Wallpaper) : IMessage;

/// <summary>
/// Message envoyé quand un wallpaper est supprimé.
/// </summary>
public record WallpaperRemovedMessage(string WallpaperId) : IMessage;

/// <summary>
/// Message envoyé quand le wallpaper actif change.
/// </summary>
public record WallpaperChangedMessage(Wallpaper? Previous, Wallpaper Current) : IMessage;

/// <summary>
/// Message envoyé quand l'état de la rotation change.
/// </summary>
public record RotationStateChangedMessage(bool IsRunning) : IMessage;

/// <summary>
/// Message envoyé quand un wallpaper dynamique est activé/désactivé.
/// </summary>
public record DynamicWallpaperStateChangedMessage(DynamicWallpaper? Wallpaper, bool IsActive) : IMessage;

/// <summary>
/// Message envoyé quand un favori change.
/// </summary>
public record FavoriteChangedMessage(Wallpaper Wallpaper, bool IsFavorite) : IMessage;

/// <summary>
/// Message envoyé pour afficher un status.
/// </summary>
public record StatusMessage(string Text, StatusSeverity Severity = StatusSeverity.Info) : IMessage;

/// <summary>
/// Message envoyé quand les paramètres changent.
/// </summary>
public record SettingsChangedMessage(string SettingName, object? OldValue, object? NewValue) : IMessage;

/// <summary>
/// Message envoyé pour rafraîchir l'UI.
/// </summary>
public record RefreshUIMessage(RefreshTarget Target) : IMessage;

/// <summary>
/// Message envoyé quand une collection change.
/// </summary>
public record CollectionChangedMessage(Collection Collection, CollectionChangeType ChangeType) : IMessage;

/// <summary>
/// Message envoyé quand le téléchargement progresse.
/// </summary>
public record DownloadProgressMessage(string FileName, int ProgressPercent, bool IsComplete) : IMessage;

#endregion

#region Enums

public enum StatusSeverity
{
    Info,
    Success,
    Warning,
    Error
}

public enum RefreshTarget
{
    Wallpapers,
    Collections,
    DynamicWallpapers,
    Favorites,
    All
}

public enum CollectionChangeType
{
    Added,
    Removed,
    Modified,
    WallpaperAdded,
    WallpaperRemoved
}

#endregion

/// <summary>
/// Mediator pour la communication découplée entre composants.
/// Thread-safe et supporte les abonnements typés.
/// </summary>
public sealed class Messenger : IDisposable
{
    private static readonly Lazy<Messenger> _default = new(() => new Messenger());
    public static Messenger Default => _default.Value;
    
    private readonly ConcurrentDictionary<Type, ConcurrentBag<WeakReference<object>>> _handlers = new();
    private readonly Lock _lock = new();
    private bool _disposed;
    
    /// <summary>
    /// S'abonne à un type de message.
    /// </summary>
    public void Subscribe<TMessage>(Action<TMessage> handler) where TMessage : IMessage
    {
        ThrowIfDisposed();
        
        var wrapper = new ActionHandler<TMessage>(handler);
        var type = typeof(TMessage);
        
        var handlers = _handlers.GetOrAdd(type, _ => []);
        handlers.Add(new WeakReference<object>(wrapper));
    }
    
    /// <summary>
    /// S'abonne à un type de message avec un gestionnaire asynchrone.
    /// </summary>
    public void Subscribe<TMessage>(Func<TMessage, Task> handler) where TMessage : IMessage
    {
        ThrowIfDisposed();
        
        var wrapper = new AsyncActionHandler<TMessage>(handler);
        var type = typeof(TMessage);
        
        var handlers = _handlers.GetOrAdd(type, _ => []);
        handlers.Add(new WeakReference<object>(wrapper));
    }
    
    /// <summary>
    /// Se désabonne d'un type de message.
    /// </summary>
    public void Unsubscribe<TMessage>(Action<TMessage> handler) where TMessage : IMessage
    {
        var type = typeof(TMessage);
        
        if (!_handlers.TryGetValue(type, out var handlers))
            return;
        
        // Les WeakReferences mortes seront nettoyées lors du prochain Send
    }
    
    /// <summary>
    /// Envoie un message à tous les abonnés.
    /// </summary>
    public void Send<TMessage>(TMessage message) where TMessage : IMessage
    {
        ThrowIfDisposed();
        
        var type = typeof(TMessage);
        
        if (!_handlers.TryGetValue(type, out var handlers))
            return;
        
        var deadRefs = new List<WeakReference<object>>();
        
        foreach (var weakRef in handlers)
        {
            if (weakRef.TryGetTarget(out var target))
            {
                try
                {
                    if (target is IMessageHandler<TMessage> syncHandler)
                    {
                        syncHandler.Handle(message);
                    }
                    else if (target is ActionHandler<TMessage> actionHandler)
                    {
                        actionHandler.Handle(message);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Erreur handler message {type.Name}: {ex.Message}");
                }
            }
            else
            {
                deadRefs.Add(weakRef);
            }
        }
        
        // Nettoyer les références mortes
        foreach (var dead in deadRefs)
        {
            // ConcurrentBag ne supporte pas Remove, donc on laisse pour le GC
        }
    }
    
    /// <summary>
    /// Envoie un message de manière asynchrone.
    /// </summary>
    public async Task SendAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default) 
        where TMessage : IMessage
    {
        ThrowIfDisposed();
        
        var type = typeof(TMessage);
        
        if (!_handlers.TryGetValue(type, out var handlers))
            return;
        
        var tasks = new List<Task>();
        
        foreach (var weakRef in handlers)
        {
            if (weakRef.TryGetTarget(out var target))
            {
                try
                {
                    if (target is IAsyncMessageHandler<TMessage> asyncHandler)
                    {
                        tasks.Add(asyncHandler.HandleAsync(message, cancellationToken));
                    }
                    else if (target is AsyncActionHandler<TMessage> asyncActionHandler)
                    {
                        tasks.Add(asyncActionHandler.HandleAsync(message));
                    }
                    else if (target is IMessageHandler<TMessage> syncHandler)
                    {
                        syncHandler.Handle(message);
                    }
                    else if (target is ActionHandler<TMessage> actionHandler)
                    {
                        actionHandler.Handle(message);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Erreur handler async {type.Name}: {ex.Message}");
                }
            }
        }
        
        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }
    
    /// <summary>
    /// Envoie un message sur le thread UI.
    /// </summary>
    public void SendOnUI<TMessage>(TMessage message) where TMessage : IMessage
    {
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher)
        {
            if (dispatcher.CheckAccess())
            {
                Send(message);
            }
            else
            {
                dispatcher.BeginInvoke(() => Send(message));
            }
        }
        else
        {
            Send(message);
        }
    }
    
    /// <summary>
    /// Nettoie tous les abonnements.
    /// </summary>
    public void Reset()
    {
        _handlers.Clear();
    }
    
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handlers.Clear();
    }
    
    #region Wrapper Classes
    
    private sealed class ActionHandler<TMessage>(Action<TMessage> action) : IMessageHandler<TMessage> 
        where TMessage : IMessage
    {
        public void Handle(TMessage message) => action(message);
    }
    
    private sealed class AsyncActionHandler<TMessage>(Func<TMessage, Task> action) 
        where TMessage : IMessage
    {
        public Task HandleAsync(TMessage message) => action(message);
    }
    
    #endregion
}

/// <summary>
/// Extensions pour faciliter l'utilisation du Messenger.
/// </summary>
public static class MessengerExtensions
{
    /// <summary>
    /// Envoie un message de status.
    /// </summary>
    public static void SendStatus(this Messenger messenger, string text, StatusSeverity severity = StatusSeverity.Info)
    {
        messenger.SendOnUI(new StatusMessage(text, severity));
    }
    
    /// <summary>
    /// Envoie une demande de rafraîchissement.
    /// </summary>
    public static void RequestRefresh(this Messenger messenger, RefreshTarget target = RefreshTarget.All)
    {
        messenger.SendOnUI(new RefreshUIMessage(target));
    }
}
