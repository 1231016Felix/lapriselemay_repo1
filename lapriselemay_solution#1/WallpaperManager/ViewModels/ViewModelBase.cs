using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using WallpaperManager.Services.Messaging;

namespace WallpaperManager.ViewModels;

/// <summary>
/// Classe de base pour tous les ViewModels.
/// Fournit l'intégration avec le Messenger et des méthodes utilitaires.
/// </summary>
public abstract class ViewModelBase : ObservableObject, IDisposable
{
    private readonly List<IDisposable> _subscriptions = [];
    private bool _disposed;
    
    /// <summary>
    /// Le Messenger pour la communication inter-composants.
    /// </summary>
    protected Messenger Messenger => Messenger.Default;
    
    /// <summary>
    /// Indique si le ViewModel est occupé (chargement, traitement).
    /// </summary>
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        protected set => SetProperty(ref _isBusy, value);
    }
    
    /// <summary>
    /// Message de status actuel.
    /// </summary>
    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        protected set => SetProperty(ref _statusMessage, value);
    }
    
    protected ViewModelBase()
    {
        // S'abonner aux messages de status
        Subscribe<StatusMessage>(OnStatusMessage);
    }
    
    /// <summary>
    /// S'abonne à un type de message avec gestion automatique du désabonnement.
    /// </summary>
    protected void Subscribe<TMessage>(Action<TMessage> handler) where TMessage : IMessage
    {
        Messenger.Subscribe(handler);
        // Note: WeakReference dans Messenger gère le désabonnement
    }
    
    /// <summary>
    /// Envoie un message via le Messenger.
    /// </summary>
    protected void Send<TMessage>(TMessage message) where TMessage : IMessage
    {
        Messenger.Send(message);
    }
    
    /// <summary>
    /// Envoie un message sur le thread UI.
    /// </summary>
    protected void SendOnUI<TMessage>(TMessage message) where TMessage : IMessage
    {
        Messenger.SendOnUI(message);
    }
    
    /// <summary>
    /// Définit le status avec une sévérité.
    /// </summary>
    protected void SetStatus(string message, StatusSeverity severity = StatusSeverity.Info)
    {
        StatusMessage = message;
        Messenger.SendStatus(message, severity);
    }
    
    /// <summary>
    /// Exécute une action avec gestion du busy state.
    /// </summary>
    protected async Task ExecuteBusyAsync(Func<Task> action, string? busyMessage = null)
    {
        if (IsBusy) return;
        
        try
        {
            IsBusy = true;
            if (!string.IsNullOrEmpty(busyMessage))
                StatusMessage = busyMessage;
            
            await action().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }
    
    /// <summary>
    /// Exécute une action avec gestion du busy state et retour de valeur.
    /// </summary>
    protected async Task<T?> ExecuteBusyAsync<T>(Func<Task<T>> action, string? busyMessage = null)
    {
        if (IsBusy) return default;
        
        try
        {
            IsBusy = true;
            if (!string.IsNullOrEmpty(busyMessage))
                StatusMessage = busyMessage;
            
            return await action().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }
    
    /// <summary>
    /// Handler par défaut pour les messages de status.
    /// </summary>
    protected virtual void OnStatusMessage(StatusMessage message)
    {
        StatusMessage = message.Text;
    }
    
    /// <summary>
    /// Appelé quand le ViewModel est chargé.
    /// </summary>
    public virtual void OnLoaded() { }
    
    /// <summary>
    /// Appelé quand le ViewModel est déchargé.
    /// </summary>
    public virtual void OnUnloaded() { }
    
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        
        if (disposing)
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }
            _subscriptions.Clear();
        }
        
        _disposed = true;
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Interface pour les ViewModels qui peuvent être activés/désactivés.
/// </summary>
public interface IActivatable
{
    bool IsActive { get; }
    void Activate();
    void Deactivate();
}

/// <summary>
/// Interface pour les ViewModels qui supportent la navigation.
/// </summary>
public interface INavigable
{
    void OnNavigatedTo(object? parameter);
    void OnNavigatedFrom();
}
