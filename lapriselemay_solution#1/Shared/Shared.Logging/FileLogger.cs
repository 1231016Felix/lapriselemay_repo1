using System.Collections.Concurrent;
using System.Text;

namespace Shared.Logging;

/// <summary>
/// Logger qui écrit les messages dans un fichier dans le dossier AppData local.
/// Thread-safe avec écriture asynchrone en arrière-plan.
/// </summary>
public sealed class FileLogger : ILogger, IDisposable
{
    private readonly string _logFilePath;
    private readonly BlockingCollection<string> _logQueue = new(1000);
    private readonly Task _writerTask;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    /// <summary>
    /// Crée un FileLogger avec un nom de fichier basé sur la date.
    /// </summary>
    /// <param name="appName">Nom de l'application (utilisé pour le dossier)</param>
    public FileLogger(string appName) 
        : this(appName, $"{appName}_{DateTime.Now:yyyy-MM-dd}.log")
    {
    }

    /// <summary>
    /// Crée un FileLogger avec un nom de fichier spécifique.
    /// </summary>
    /// <param name="appName">Nom de l'application (utilisé pour le dossier)</param>
    /// <param name="logFileName">Nom du fichier de log</param>
    public FileLogger(string appName, string logFileName)
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            appName,
            "Logs");
        
        Directory.CreateDirectory(logDir);
        _logFilePath = Path.Combine(logDir, logFileName);
        
        _writerTask = Task.Run(ProcessLogQueueAsync);
    }

    public void Debug(string message) => Log("DEBUG", message);
    public void Info(string message) => Log("INFO", message);
    public void Warning(string message) => Log("WARN", message);
    
    public void Error(string message, Exception? exception = null)
    {
        var sb = new StringBuilder();
        sb.Append(message);
        
        if (exception != null)
        {
            sb.AppendLine();
            sb.Append("  Exception: ").AppendLine(exception.GetType().Name);
            sb.Append("  Message: ").AppendLine(exception.Message);
            if (exception.StackTrace != null)
            {
                sb.Append("  StackTrace: ").Append(exception.StackTrace);
            }
        }
        
        Log("ERROR", sb.ToString());
    }

    private void Log(string level, string message)
    {
        if (_disposed) return;
        
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var threadId = Environment.CurrentManagedThreadId;
        var logLine = $"[{timestamp}] [{level,-5}] [T{threadId:D3}] {message}";
        
        try
        {
            _logQueue.TryAdd(logLine, 100);
        }
        catch (InvalidOperationException)
        {
            // Queue complète ou fermée, on ignore silencieusement
        }
    }

    private async Task ProcessLogQueueAsync()
    {
        try
        {
            await using var writer = new StreamWriter(_logFilePath, append: true, Encoding.UTF8)
            {
                AutoFlush = true
            };
            
            foreach (var logLine in _logQueue.GetConsumingEnumerable(_cts.Token))
            {
                await writer.WriteLineAsync(logLine);
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt normal
        }
        catch
        {
            // Erreur d'écriture, on ignore silencieusement
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _logQueue.CompleteAdding();
        _cts.Cancel();
        
        try
        {
            _writerTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Timeout ou erreur, on continue le dispose
        }
        
        _cts.Dispose();
        _logQueue.Dispose();
    }
}
