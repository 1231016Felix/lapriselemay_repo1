namespace Shared.Logging;

/// <summary>
/// Interface commune pour le logging dans les applications.
/// </summary>
public interface ILogger
{
    /// <summary>
    /// Log un message de niveau Debug.
    /// </summary>
    void Debug(string message);
    
    /// <summary>
    /// Log un message de niveau Info.
    /// </summary>
    void Info(string message);
    
    /// <summary>
    /// Log un message de niveau Warning.
    /// </summary>
    void Warning(string message);
    
    /// <summary>
    /// Log un message de niveau Error.
    /// </summary>
    void Error(string message, Exception? exception = null);
}
