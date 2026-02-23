using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace QuickLauncher.Services;

/// <summary>
/// Helper pour attacher des fenêtres WPF au bureau Windows.
/// Les widgets restent visibles sur le bureau et se comportent comme des gadgets.
/// 
/// Utilise SetWinEventHook (EVENT_SYSTEM_FOREGROUND) au lieu d'un timer polling
/// pour détecter les changements de fenêtre active → zéro CPU au repos.
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

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    // ── SetWinEventHook ──
    private delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    private const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_BOTTOM = new(1);

    #endregion

    private static readonly List<WeakReference<Window>> _attachedWindows = [];
    private static readonly object _lock = new();
    private static bool _isDesktopVisible;

    // ── Hook state ──
    private static IntPtr _foregroundHook;
    private static IntPtr _minimizeStartHook;
    private static IntPtr _minimizeEndHook;
    // IMPORTANT : garder une référence forte au delegate pour empêcher le GC de le collecter
    // tant que le hook Win32 est actif (sinon → CallbackOnCollectedDelegate crash).
    private static WinEventDelegate? _winEventProc;
    private static Dispatcher? _dispatcher;

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

                // Démarrer le hook si pas encore actif
                if (_foregroundHook == IntPtr.Zero)
                    StartHook(window.Dispatcher);
            }

            // Vérifier immédiatement l'état du bureau
            UpdateWindowState(window, IsDesktopForeground());
        };

        window.Closed += (s, e) =>
        {
            lock (_lock)
            {
                _attachedWindows.RemoveAll(wr => !wr.TryGetTarget(out var w) || w == window);

                // Arrêter le hook si plus de fenêtres
                if (_attachedWindows.Count == 0)
                    StopHook();
            }
        };
    }

    /// <summary>
    /// Installe les hooks Win32 événementiels (remplace le timer 250ms).
    /// EVENT_SYSTEM_FOREGROUND : déclenché quand une fenêtre passe au premier plan.
    /// EVENT_SYSTEM_MINIMIZESTART/END : déclenché lors de minimize/restore (Show Desktop).
    /// </summary>
    private static void StartHook(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _winEventProc = OnWinEvent;

        _foregroundHook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        _minimizeStartHook = SetWinEventHook(
            EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZESTART,
            IntPtr.Zero, _winEventProc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        _minimizeEndHook = SetWinEventHook(
            EVENT_SYSTEM_MINIMIZEEND, EVENT_SYSTEM_MINIMIZEEND,
            IntPtr.Zero, _winEventProc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    /// <summary>
    /// Désinstalle tous les hooks Win32.
    /// </summary>
    private static void StopHook()
    {
        if (_foregroundHook != IntPtr.Zero)
        {
            UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }
        if (_minimizeStartHook != IntPtr.Zero)
        {
            UnhookWinEvent(_minimizeStartHook);
            _minimizeStartHook = IntPtr.Zero;
        }
        if (_minimizeEndHook != IntPtr.Zero)
        {
            UnhookWinEvent(_minimizeEndHook);
            _minimizeEndHook = IntPtr.Zero;
        }
        _winEventProc = null;
        _dispatcher = null;
    }

    /// <summary>
    /// Callback appelé par Windows quand une fenêtre change de Z-order.
    /// Exécuté sur le thread du message pump Win32, on dispatch vers le thread WPF.
    /// </summary>
    private static void OnWinEvent(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Dispatcher vers le thread UI pour accéder aux fenêtres WPF
        _dispatcher?.BeginInvoke(EvaluateDesktopState);
    }

    /// <summary>
    /// Évalue l'état du bureau et met à jour toutes les fenêtres si changement.
    /// Équivalent de l'ancien OnDesktopWatcherTick mais appelé uniquement sur événement.
    /// </summary>
    private static void EvaluateDesktopState()
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

        if (classNameStr is "WorkerW" or "Progman")
            return true;

        // Les fenêtres système du tray et de la barre des tâches sont « transparentes » :
        // on conserve l'état actuel au lieu de forcer une transition.
        if (IsSystemTrayWindow(classNameStr))
            return _isDesktopVisible;

        // Vérifier si la fenêtre au premier plan contient SHELLDLL_DefView
        var shellView = FindWindowEx(foreground, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (shellView != IntPtr.Zero)
            return true;

        return false;
    }

    /// <summary>
    /// Vérifie si la fenêtre est une fenêtre système du tray ou de la barre des tâches.
    /// </summary>
    private static bool IsSystemTrayWindow(string className)
    {
        return className switch
        {
            "Shell_TrayWnd" => true,
            "NotifyIconOverflowWindow" => true,
            "Shell_SecondaryTrayWnd" => true,
            "Windows.UI.Core.CoreWindow" => true,
            "XamlExplorerHostIslandWindow" => true,
            "TopLevelWindowForOverflowXamlIsland" => true,
            "ToolbarWindow32" => true,
            "SysPager" => true,
            "TrayNotifyWnd" => true,
            "ReBarWindow32" => true,
            "MSTaskSwWClass" => true,
            "MSTaskListWClass" => true,
            _ => false
        };
    }

    private static void UpdateAllWindows(bool isDesktopVisible)
    {
        List<Window> windows = [];

        lock (_lock)
        {
            foreach (var wr in _attachedWindows.ToList())
            {
                if (wr.TryGetTarget(out var window))
                    windows.Add(window);
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
    /// Arrête la surveillance du bureau et libère les hooks Win32.
    /// </summary>
    public static void Shutdown()
    {
        StopHook();
        lock (_lock)
        {
            _attachedWindows.Clear();
        }
    }
}