namespace CleanUninstaller.Services.Interfaces;

/// <summary>
/// Interface pour le service de logging
/// </summary>
public interface ILoggerService
{
    void Debug(string message);
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
}
