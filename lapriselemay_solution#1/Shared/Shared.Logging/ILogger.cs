namespace Shared.Logging;

/// <summary>
/// Niveau de log
/// </summary>
public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3
}

/// <summary>
/// Interface de logging standardisée pour tous les projets.
/// Cette interface doit être utilisée partout dans la solution.
/// </summary>
public interface ILogger
{
    /// <summary>
    /// Niveau de log minimum à enregistrer
    /// </summary>
    LogLevel MinimumLevel { get; set; }
    
    /// <summary>
    /// Log un message de debug
    /// </summary>
    void Debug(string message);
    
    /// <summary>
    /// Log un message d'information
    /// </summary>
    void Info(string message);
    
    /// <summary>
    /// Log un avertissement
    /// </summary>
    void Warning(string message);
    
    /// <summary>
    /// Log une erreur avec exception optionnelle
    /// </summary>
    void Error(string message, Exception? exception = null);
    
    /// <summary>
    /// Log un message avec niveau spécifié
    /// </summary>
    void Log(LogLevel level, string message, Exception? exception = null);
}

/// <summary>
/// Interface pour les loggers asynchrones avec support IAsyncDisposable.
/// Utiliser cette interface pour les loggers qui écrivent sur disque ou réseau.
/// </summary>
public interface IAsyncLogger : ILogger, IAsyncDisposable
{
    /// <summary>
    /// Force l'écriture de tous les logs en attente
    /// </summary>
    Task FlushAsync();
}

/// <summary>
/// Alias pour compatibilité avec CleanUninstaller et autres projets.
/// Préférer ILogger pour les nouvelles implémentations.
/// </summary>
public interface ILoggerService : ILogger
{
    // Hérite de toutes les méthodes de ILogger
    // Permet la migration progressive du code existant
}
