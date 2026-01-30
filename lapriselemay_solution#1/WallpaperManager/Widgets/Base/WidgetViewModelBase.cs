using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace WallpaperManager.Widgets.Base;

/// <summary>
/// Classe de base pour tous les ViewModels de widgets.
/// </summary>
public abstract class WidgetViewModelBase : INotifyPropertyChanged, IDisposable
{
    private readonly DispatcherTimer _refreshTimer;
    private readonly Dispatcher _dispatcher;
    private bool _disposed;
    
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WallpaperManager", "widget_debug.log");
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected virtual int RefreshIntervalSeconds => 5;
    
    public string WidgetId { get; set; } = string.Empty;
    
    // Dimensions du widget pour adapter le contenu
    private double _widgetWidth = 300;
    public double WidgetWidth
    {
        get => _widgetWidth;
        protected set => SetProperty(ref _widgetWidth, value);
    }
    
    private double _widgetHeight = 200;
    public double WidgetHeight
    {
        get => _widgetHeight;
        protected set => SetProperty(ref _widgetHeight, value);
    }
    
    // Mode compact quand le widget est petit
    private bool _isCompactMode;
    public bool IsCompactMode
    {
        get => _isCompactMode;
        protected set => SetProperty(ref _isCompactMode, value);
    }
    
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        protected set => SetProperty(ref _isLoading, value);
    }
    
    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        protected set => SetProperty(ref _errorMessage, value);
    }
    
    protected WidgetViewModelBase()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(RefreshIntervalSeconds)
        };
        _refreshTimer.Tick += async (s, e) => await RefreshAsync();
    }
    
    protected static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss.fff}] BASE: {message}\n");
        }
        catch { }
    }
    
    public virtual void Start()
    {
        Log($"Start() appelé pour {GetType().Name} (WidgetId={WidgetId})");
        _refreshTimer.Start();
        _ = RefreshAsync();
    }
    
    public virtual void Stop()
    {
        Log($"Stop() appelé pour {GetType().Name}");
        _refreshTimer.Stop();
    }
    
    public abstract Task RefreshAsync();
    
    /// <summary>
    /// Appelé quand la taille du widget change.
    /// </summary>
    public virtual void OnSizeChanged(double width, double height)
    {
        WidgetWidth = width;
        WidgetHeight = height;
        
        // Mode compact si le widget est petit
        IsCompactMode = width < 250 || height < 150;
    }
    
    public void SetRefreshInterval(int seconds)
    {
        _refreshTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, seconds));
    }
    
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        
        field = value;
        
        // Dispatcher vers le UI thread si nécessaire
        if (_dispatcher.CheckAccess())
        {
            OnPropertyChanged(propertyName);
        }
        else
        {
            _dispatcher.BeginInvoke(() => OnPropertyChanged(propertyName));
        }
        
        return true;
    }
    
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        
        if (disposing)
        {
            _refreshTimer.Stop();
        }
        
        _disposed = true;
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
