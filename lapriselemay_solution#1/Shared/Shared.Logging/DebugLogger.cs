using System.Diagnostics;

namespace Shared.Logging;

/// <summary>
/// Logger qui écrit uniquement dans Debug.WriteLine (pour le développement).
/// Implémente ILoggerService pour compatibilité avec tous les projets.
/// </summary>
public sealed class DebugLogger : ILogger, ILoggerService
{
    public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        if (level < MinimumLevel) return;
        
        var levelStr = level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO ",
            LogLevel.Warning => "WARN ",
            LogLevel.Error => "ERROR",
            _ => "???? "
        };
        
        var fullMessage = exception != null
            ? $"[{levelStr}] {message}: {exception}"
            : $"[{levelStr}] {message}";
        
        System.Diagnostics.Debug.WriteLine(fullMessage);
    }

    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warning(string message) => Log(LogLevel.Warning, message);
    public void Error(string message, Exception? exception = null) => Log(LogLevel.Error, message, exception);
}

/// <summary>
/// Logger qui ne fait rien (pattern Null Object).
/// Implémente ILoggerService pour compatibilité avec tous les projets.
/// </summary>
public sealed class NullLogger : ILogger, ILoggerService
{
    public static readonly NullLogger Instance = new();
    
    public LogLevel MinimumLevel { get; set; } = LogLevel.Error;

    public void Log(LogLevel level, string message, Exception? exception = null) { }
    public void Debug(string message) { }
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
}

/// <summary>
/// Logger composite qui écrit dans plusieurs loggers à la fois.
/// Implémente ILoggerService pour compatibilité avec tous les projets.
/// </summary>
public sealed class CompositeLogger : ILogger, ILoggerService
{
    private readonly ILogger[] _loggers;

    public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    public CompositeLogger(params ILogger[] loggers)
    {
        _loggers = loggers;
    }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        if (level < MinimumLevel) return;
        
        foreach (var logger in _loggers)
        {
            logger.Log(level, message, exception);
        }
    }

    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warning(string message) => Log(LogLevel.Warning, message);
    public void Error(string message, Exception? exception = null) => Log(LogLevel.Error, message, exception);
}
