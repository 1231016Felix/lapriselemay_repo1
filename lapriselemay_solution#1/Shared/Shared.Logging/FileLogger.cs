using System.Threading.Channels;

namespace Shared.Logging;

/// <summary>
/// Logger asynchrone vers fichier utilisant Channel&lt;T&gt; pour des performances optimales.
/// Les logs sont mis en queue et écrits par batch de manière asynchrone pour minimiser les I/O disque.
/// Implémente ILoggerService pour compatibilité avec tous les projets.
/// </summary>
public sealed class FileLogger : IAsyncLogger, ILoggerService, IDisposable
{
    private readonly string _logPath;
    private readonly Channel<LogEntry> _logChannel;
    private readonly Task _writerTask;
    private readonly CancellationTokenSource _cts = new();
    private readonly int _batchSize;
    private readonly TimeSpan _flushInterval;
    private bool _disposed;

    /// <summary>
    /// Niveau de log minimum (par défaut: Info)
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    /// <summary>
    /// Crée un FileLogger avec le chemin spécifié
    /// </summary>
    /// <param name="logPath">Chemin complet du fichier de log</param>
    /// <param name="bufferSize">Taille du buffer (nombre de logs en attente max)</param>
    /// <param name="batchSize">Nombre de logs à accumuler avant écriture</param>
    /// <param name="flushInterval">Intervalle max entre les écritures</param>
    public FileLogger(string logPath, int bufferSize = 1000, int batchSize = 50, TimeSpan? flushInterval = null)
    {
        _logPath = logPath;
        _batchSize = batchSize;
        _flushInterval = flushInterval ?? TimeSpan.FromSeconds(1);
        
        // Créer le dossier si nécessaire
        var directory = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        // Channel borné pour éviter une consommation mémoire excessive
        _logChannel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(bufferSize)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        
        // Démarrer le writer en background
        _writerTask = Task.Run(ProcessLogsAsync);
    }

    /// <summary>
    /// Crée un FileLogger dans le dossier AppData de l'application
    /// </summary>
    /// <param name="appName">Nom de l'application</param>
    /// <param name="fileName">Nom du fichier de log (défaut: app.log)</param>
    public FileLogger(string appName, string fileName = "app.log", int bufferSize = 1000)
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            appName,
            fileName), bufferSize)
    { }

    /// <summary>
    /// Traite les logs par batch pour minimiser les écritures disque
    /// </summary>
    private async Task ProcessLogsAsync()
    {
        var buffer = new List<string>(_batchSize);
        
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                // Collecter les logs jusqu'à atteindre batchSize ou timeout
                var deadline = DateTime.UtcNow.Add(_flushInterval);
                
                while (buffer.Count < _batchSize)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                        break;
                    
                    try
                    {
                        // Attendre un log avec timeout
                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                        timeoutCts.CancelAfter(remaining);
                        
                        if (await _logChannel.Reader.WaitToReadAsync(timeoutCts.Token).ConfigureAwait(false))
                        {
                            while (buffer.Count < _batchSize && _logChannel.Reader.TryRead(out var entry))
                            {
                                buffer.Add(FormatLogEntry(entry));
                            }
                        }
                    }
                    catch (OperationCanceledException) when (!_cts.Token.IsCancellationRequested)
                    {
                        // Timeout atteint, on flush ce qu'on a
                        break;
                    }
                }
                
                // Écrire le batch si non vide
                if (buffer.Count > 0)
                {
                    try
                    {
                        await File.AppendAllLinesAsync(_logPath, buffer, _cts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Ignorer les erreurs d'écriture silencieusement
                        System.Diagnostics.Debug.WriteLine($"FileLogger write error: {ex.Message}");
                    }
                    buffer.Clear();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal lors de la fermeture
        }
        finally
        {
            // Flush final - écrire tout ce qui reste
            while (_logChannel.Reader.TryRead(out var entry))
            {
                buffer.Add(FormatLogEntry(entry));
            }
            
            if (buffer.Count > 0)
            {
                try
                {
                    File.AppendAllLines(_logPath, buffer);
                }
                catch
                {
                    // Ignorer les erreurs lors du shutdown
                }
            }
        }
    }

    private static string FormatLogEntry(LogEntry entry)
    {
        var levelStr = entry.Level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO ",
            LogLevel.Warning => "WARN ",
            LogLevel.Error => "ERROR",
            _ => "???? "
        };
        
        var message = entry.Exception != null
            ? $"{entry.Message}: {entry.Exception}"
            : entry.Message;
        
        return $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{levelStr}] {message}";
    }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        if (level < MinimumLevel) return;
        
        var entry = new LogEntry(DateTime.Now, level, message, exception);
        
        // Écrire aussi dans Debug output
        System.Diagnostics.Debug.WriteLine(FormatLogEntry(entry));
        
        // Envoyer au channel (non-bloquant)
        _logChannel.Writer.TryWrite(entry);
    }

    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warning(string message) => Log(LogLevel.Warning, message);
    public void Error(string message, Exception? exception = null) => Log(LogLevel.Error, message, exception);

    public async Task FlushAsync()
    {
        // Attendre que tous les logs soient traités
        _logChannel.Writer.Complete();
        await _writerTask.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _logChannel.Writer.Complete();
        
        try
        {
            // Attendre un peu que les logs restants soient écrits
            await _writerTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Forcer l'arrêt si trop long
            await _cts.CancelAsync().ConfigureAwait(false);
        }
        
        _cts.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _logChannel.Writer.Complete();
        _cts.Cancel();
        
        try
        {
            _writerTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch { }
        
        _cts.Dispose();
    }

    private readonly record struct LogEntry(
        DateTime Timestamp,
        LogLevel Level,
        string Message,
        Exception? Exception);
}
