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
/// Interface de logging standardisée pour tous les projets
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
/// Interface pour les loggers asynchrones avec support IAsyncDisposable
/// </summary>
public interface IAsyncLogger : ILogger, IAsyncDisposable
{
    /// <summary>
    /// Force l'écriture de tous les logs en attente
    /// </summary>
    Task FlushAsync();
}
