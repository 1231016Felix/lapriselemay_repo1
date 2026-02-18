namespace QuickLauncher.Services;

/// <summary>
/// Interface commune pour le logging dans l'application.
/// </summary>
public interface ILogger
{
    void Debug(string message);
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
}
