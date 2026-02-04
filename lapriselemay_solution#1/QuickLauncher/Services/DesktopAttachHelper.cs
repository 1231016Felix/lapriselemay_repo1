using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace QuickLauncher.Services;

/// <summary>
/// Helper pour attacher des fenêtres WPF au bureau Windows.
/// Les widgets restent visibles sur le bureau et se comportent comme des gadgets.
/// </summary>
public static class DesktopAttachHelper
{
    #region Win32 API
    
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private static readonly IntPtr HWND_BOTTOM = new(1);
    
    private const uint GW_HWNDNEXT = 2;
    
    #endregion

    private static readonly List<WeakReference<Window>> _attachedWindows = [];
    private static DispatcherTimer? _desktopWatcher;
    private static bool _isDesktopVisible;
    private static readonly object _lock = new();

    /// <summary>
    /// Attache une fenêtre WPF au bureau Windows.
    /// La fenêtre sera visible sur le bureau et se comportera comme un gadget.
    /// </summary>
    public static void AttachToDesktop(Window window)
    {
        window.SourceInitialized += (s, e) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            
            // Style tool window + no activate pour ne pas voler le focus
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };
        
        window.Loaded += (s, e) =>
        {
            lock (_lock)
            {
                _attachedWindows.Add(new WeakReference<Window>(window));
                
                // Démarrer le watcher si pas encore démarré
                if (_desktopWatcher == null)
                {
                    StartDesktopWatcher();
                }
            }
            
            // Vérifier immédiatement l'état du bureau
            UpdateWindowState(window, IsDesktopForeground());
        };

        window.Closed += (s, e) =>
        {
            lock (_lock)
            {
                _attachedWindows.RemoveAll(wr => !wr.TryGetTarget(out var w) || w == window);
                
                // Arrêter le watcher si plus de fenêtres
                if (_attachedWindows.Count == 0)
                {
                    StopDesktopWatcher();
                }
            }
        };
    }

    private static void StartDesktopWatcher()
    {
        _desktopWatcher = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _desktopWatcher.Tick += OnDesktopWatcherTick;
        _desktopWatcher.Start();
    }

    private static void StopDesktopWatcher()
    {
        _desktopWatcher?.Stop();
        _desktopWatcher = null;
    }

    private static void OnDesktopWatcherTick(object? sender, EventArgs e)
    {
        var isDesktopNow = IsDesktopForeground();
        
        if (isDesktopNow != _isDesktopVisible)
        {
            _isDesktopVisible = isDesktopNow;
            UpdateAllWindows(isDesktopNow);
        }
    }

    /// <summary>
    /// Vérifie si le bureau est actuellement au premier plan (Show Desktop activé).
    /// </summary>
    private static bool IsDesktopForeground()
    {
        var foreground = GetForegroundWindow();
        
        if (foreground == IntPtr.Zero)
            return true;
        
        // Vérifier si c'est le bureau ou le shell
        var desktop = GetDesktopWindow();
        var shell = GetShellWindow();
        var progman = FindWindow("Progman", "Program Manager");
        
        if (foreground == desktop || foreground == shell || foreground == progman)
            return true;
        
        // Vérifier si c'est WorkerW (utilisé par Windows pour "Show Desktop")
        var className = new System.Text.StringBuilder(256);
        GetClassName(foreground, className, className.Capacity);
        var classNameStr = className.ToString();
        
        if (classNameStr == "WorkerW" || classNameStr == "Progman")
            return true;
        
        // Vérifier si la fenêtre au premier plan contient SHELLDLL_DefView
        var shellView = FindWindowEx(foreground, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (shellView != IntPtr.Zero)
            return true;
        
        return false;
    }

    private static void UpdateAllWindows(bool isDesktopVisible)
    {
        List<Window> windows = [];
        
        lock (_lock)
        {
            foreach (var wr in _attachedWindows.ToList())
            {
                if (wr.TryGetTarget(out var window))
                {
                    windows.Add(window);
                }
            }
            
            // Nettoyer les références mortes
            _attachedWindows.RemoveAll(wr => !wr.TryGetTarget(out _));
        }
        
        foreach (var window in windows)
        {
            window.Dispatcher.Invoke(() => UpdateWindowState(window, isDesktopVisible));
        }
    }

    private static void UpdateWindowState(Window window, bool isDesktopVisible)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        if (isDesktopVisible)
        {
            // Bureau visible: mettre en Topmost pour être au-dessus du bureau
            window.Topmost = true;
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
        else
        {
            // Une application est au premier plan: envoyer tout en bas de la pile Z
            window.Topmost = false;
            SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        }
    }

    /// <summary>
    /// Force l'affichage de tous les widgets attachés.
    /// </summary>
    public static void ShowAllWidgets()
    {
        lock (_lock)
        {
            foreach (var wr in _attachedWindows)
            {
                if (wr.TryGetTarget(out var window))
                {
                    window.Dispatcher.Invoke(() =>
                    {
                        window.Show();
                        window.Topmost = true;
                    });
                }
            }
        }
    }

    /// <summary>
    /// Arrête la surveillance du bureau.
    /// </summary>
    public static void Shutdown()
    {
        StopDesktopWatcher();
        lock (_lock)
        {
            _attachedWindows.Clear();
        }
    }
}
