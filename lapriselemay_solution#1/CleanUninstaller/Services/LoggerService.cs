using System.Collections.Concurrent;
using Shared.Logging;

namespace CleanUninstaller.Services;

/// <summary>
/// Service de logging thread-safe avec rotation des fichiers.
/// Implémente ILoggerService de Shared.Logging pour compatibilité cross-projet.
/// </summary>
public sealed class LoggerService : ILoggerService, IDisposable
{
    private readonly string _logDirectory;
    private readonly string _logFilePath;
    private readonly ConcurrentQueue<LogEntry> _logQueue = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Timer _flushTimer;
    private bool _disposed;

    private const int MaxLogFileSizeMB = 10;
    private const int MaxLogFiles = 5;
    private const int FlushIntervalMs = 1000;

    /// <summary>
    /// Niveau de log minimum (par défaut: Info)
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    public LoggerService(string logLevel = "Info")
    {
        MinimumLevel = Enum.TryParse<LogLevel>(logLevel, true, out var level) ? level : LogLevel.Info;
        
        _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanUninstaller",
            "Logs");
        
        Directory.CreateDirectory(_logDirectory);
        _logFilePath = Path.Combine(_logDirectory, $"CleanUninstaller_{DateTime.Now:yyyyMMdd}.log");
        
        // Timer pour flush périodique
        _flushTimer = new Timer(_ => FlushAsync().ConfigureAwait(false), null, FlushIntervalMs, FlushIntervalMs);
        
        Info("LoggerService initialisé");
    }

    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warning(string message) => Log(LogLevel.Warning, message);
    public void Error(string message, Exception? exception = null) => 
        Log(LogLevel.Error, message, exception);

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        if (level < MinimumLevel) return;

        var fullMessage = exception != null ? $"{message}\n{exception}" : message;
        
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = fullMessage,
            ThreadId = Environment.CurrentManagedThreadId
        };

        _logQueue.Enqueue(entry);
        
        // Écriture immédiate en debug
        System.Diagnostics.Debug.WriteLine($"[{entry.Level}] {entry.Message}");
    }

    private async Task FlushAsync()
    {
        if (_disposed || _logQueue.IsEmpty) return;

        await _writeLock.WaitAsync();
        try
        {
            await RotateLogIfNeededAsync();
            
            var entries = new List<string>();
            while (_logQueue.TryDequeue(out var entry))
            {
                entries.Add(entry.ToString());
            }

            if (entries.Count > 0)
            {
                await File.AppendAllLinesAsync(_logFilePath, entries);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur flush log: {ex.Message}");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task RotateLogIfNeededAsync()
    {
        try
        {
            var fileInfo = new FileInfo(_logFilePath);
            if (!fileInfo.Exists || fileInfo.Length < MaxLogFileSizeMB * 1024 * 1024)
                return;

            // Rotation des fichiers
            for (int i = MaxLogFiles - 1; i >= 1; i--)
            {
                var oldPath = Path.Combine(_logDirectory, $"CleanUninstaller_{DateTime.Now:yyyyMMdd}.{i}.log");
                var newPath = Path.Combine(_logDirectory, $"CleanUninstaller_{DateTime.Now:yyyyMMdd}.{i + 1}.log");
                
                if (File.Exists(oldPath))
                {
                    if (i == MaxLogFiles - 1)
                        File.Delete(oldPath);
                    else
                        File.Move(oldPath, newPath, true);
                }
            }

            var firstRotated = Path.Combine(_logDirectory, $"CleanUninstaller_{DateTime.Now:yyyyMMdd}.1.log");
            File.Move(_logFilePath, firstRotated, true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur rotation log: {ex.Message}");
        }
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Nettoie les vieux fichiers de log
    /// </summary>
    public void CleanOldLogs(int daysToKeep = 30)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-daysToKeep);
            foreach (var file in Directory.GetFiles(_logDirectory, "*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur nettoyage logs: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _flushTimer.Dispose();
        FlushAsync().GetAwaiter().GetResult();
        _writeLock.Dispose();
    }

    private class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; } = string.Empty;
        public int ThreadId { get; set; }

        public override string ToString()
        {
            var levelStr = Level switch
            {
                LogLevel.Debug => "DEBUG  ",
                LogLevel.Info => "INFO   ",
                LogLevel.Warning => "WARNING",
                LogLevel.Error => "ERROR  ",
                _ => "????   "
            };
            return $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{levelStr}] [T{ThreadId:D3}] {Message}";
        }
    }
}
