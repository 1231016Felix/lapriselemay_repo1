using System.Collections.Concurrent;
using System.IO;
using QuickLauncher.Models;
using Shared.Logging;

using Timer = System.Threading.Timer;

namespace QuickLauncher.Services;

/// <summary>
/// Service de surveillance des fichiers pour l'indexation incrémentale.
/// Détecte les créations, modifications et suppressions de fichiers.
/// </summary>
public sealed class FileWatcherService : IDisposable
{
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentQueue<FileChangeEvent> _changeQueue = new();
    private readonly ILogger _logger;
    private readonly Timer _processTimer;
    private readonly object _processLock = new();
    
    private readonly ISettingsProvider _settingsProvider;
    private bool _disposed;
    private bool _isProcessing;

    /// <summary>
    /// Événement déclenché quand des fichiers ont changé.
    /// </summary>
    public event EventHandler<FileChangesEventArgs>? FilesChanged;

    /// <summary>
    /// Nombre de changements en attente de traitement.
    /// </summary>
    public int PendingChangesCount => _changeQueue.Count;

    public FileWatcherService(ISettingsProvider settingsProvider, ILogger? logger = null)
    {
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _logger = logger ?? new FileLogger(Constants.AppName, Constants.LogFileName);
        
        // Timer pour traiter les changements par lots (évite le spam)
        _processTimer = new Timer(ProcessChanges, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Démarre la surveillance des dossiers indexés.
    /// </summary>
    public void Start()
    {
        var settings = _settingsProvider.Current;
        
        foreach (var folder in settings.IndexedFolders.Where(Directory.Exists))
        {
            StartWatching(folder);
        }
        
        _logger.Info($"FileWatcher démarré - {_watchers.Count} dossiers surveillés");
    }

    /// <summary>
    /// Arrête toute surveillance.
    /// </summary>
    public void Stop()
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
        
        _logger.Info("FileWatcher arrêté");
    }

    /// <summary>
    /// Ajoute un dossier à surveiller.
    /// </summary>
    public void AddFolder(string folderPath)
    {
        if (Directory.Exists(folderPath) && !_watchers.ContainsKey(folderPath))
        {
            StartWatching(folderPath);
        }
    }

    /// <summary>
    /// Retire un dossier de la surveillance.
    /// </summary>
    public void RemoveFolder(string folderPath)
    {
        if (_watchers.TryRemove(folderPath, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
    }

    private void StartWatching(string folderPath)
    {
        try
        {
            var watcher = new FileSystemWatcher(folderPath)
            {
                NotifyFilter = NotifyFilters.FileName 
                    | NotifyFilters.DirectoryName 
                    | NotifyFilters.LastWrite
                    | NotifyFilters.CreationTime,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            // Configurer les filtres d'extensions si possible
            // Note: FileSystemWatcher ne supporte qu'un seul filtre, on utilise donc "*"
            watcher.Filter = "*.*";

            watcher.Created += OnFileCreated;
            watcher.Deleted += OnFileDeleted;
            watcher.Renamed += OnFileRenamed;
            watcher.Changed += OnFileChanged;
            watcher.Error += OnWatcherError;

            _watchers[folderPath] = watcher;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Impossible de surveiller '{folderPath}': {ex.Message}");
        }
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        if (ShouldProcess(e.FullPath))
        {
            EnqueueChange(FileChangeType.Created, e.FullPath);
        }
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (ShouldProcess(e.FullPath))
        {
            EnqueueChange(FileChangeType.Deleted, e.FullPath);
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (ShouldProcess(e.OldFullPath))
        {
            EnqueueChange(FileChangeType.Deleted, e.OldFullPath);
        }
        if (ShouldProcess(e.FullPath))
        {
            EnqueueChange(FileChangeType.Created, e.FullPath);
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // On ignore les modifications de contenu pour l'indexation
        // (seules les créations/suppressions nous intéressent)
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.Warning($"Erreur FileWatcher: {e.GetException().Message}");
        
        // Tenter de redémarrer le watcher
        if (sender is FileSystemWatcher watcher)
        {
            var path = watcher.Path;
            RemoveFolder(path);
            
            Task.Delay(5000).ContinueWith(_ =>
            {
                if (Directory.Exists(path))
                    AddFolder(path);
            });
        }
    }

    private bool ShouldProcess(string path)
    {
        // Vérifier si c'est un fichier avec une extension indexée
        var ext = Path.GetExtension(path).ToLowerInvariant();
        
        // Ignorer les fichiers système et temporaires
        if (string.IsNullOrEmpty(ext))
            return Directory.Exists(path); // C'est un dossier
        
        if (ext.StartsWith(".tmp") || ext == ".temp" || path.Contains("~$"))
            return false;
        
        // Vérifier les extensions autorisées
        return _settingsProvider.Current.FileExtensions.Contains(ext);
    }

    private void EnqueueChange(FileChangeType type, string path)
    {
        _changeQueue.Enqueue(new FileChangeEvent(type, path, DateTime.UtcNow));
        
        // Démarrer le timer pour traiter par lots (500ms de délai)
        _processTimer.Change(500, Timeout.Infinite);
    }

    private void ProcessChanges(object? state)
    {
        if (_isProcessing) return;
        
        lock (_processLock)
        {
            if (_isProcessing) return;
            _isProcessing = true;
        }

        try
        {
            var changes = new List<FileChangeEvent>();
            var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Récupérer tous les changements en attente
            while (_changeQueue.TryDequeue(out var change))
            {
                // Dédupliquer (garder le dernier changement pour chaque chemin)
                if (!processedPaths.Contains(change.Path))
                {
                    processedPaths.Add(change.Path);
                    changes.Add(change);
                }
            }

            if (changes.Count > 0)
            {
                _logger.Info($"FileWatcher: {changes.Count} changements détectés");
                
                // Notifier les abonnés
                FilesChanged?.Invoke(this, new FileChangesEventArgs(changes));
            }
        }
        finally
        {
            _isProcessing = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _processTimer.Dispose();
        Stop();
        
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Type de changement de fichier.
/// </summary>
public enum FileChangeType
{
    Created,
    Deleted,
    Modified
}

/// <summary>
/// Événement de changement de fichier.
/// </summary>
public readonly record struct FileChangeEvent(
    FileChangeType Type,
    string Path,
    DateTime Timestamp);

/// <summary>
/// Arguments pour l'événement FilesChanged.
/// </summary>
public sealed class FileChangesEventArgs : EventArgs
{
    public IReadOnlyList<FileChangeEvent> Changes { get; }

    public FileChangesEventArgs(IReadOnlyList<FileChangeEvent> changes)
    {
        Changes = changes;
    }

    public IEnumerable<FileChangeEvent> CreatedFiles => 
        Changes.Where(c => c.Type == FileChangeType.Created);
    
    public IEnumerable<FileChangeEvent> DeletedFiles => 
        Changes.Where(c => c.Type == FileChangeType.Deleted);
}
