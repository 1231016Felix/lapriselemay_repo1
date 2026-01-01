using System.Runtime.InteropServices;

namespace WallpaperManager.Native;

/// <summary>
/// API pour placer une fenêtre derrière les icônes du bureau (pour fonds animés)
/// </summary>
public static partial class DesktopWindowApi
{
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindowW(string? lpClassName, string? lpWindowName);
    
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindowExW(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);
    
    [LibraryImport("user32.dll")]
    private static partial IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
    
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    
    [LibraryImport("user32.dll")]
    private static partial int SetWindowLongW(IntPtr hWnd, int nIndex, int dwNewLong);
    
    [LibraryImport("user32.dll")]
    private static partial int GetWindowLongW(IntPtr hWnd, int nIndex);
    
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    
    [LibraryImport("user32.dll")]
    private static partial IntPtr SendMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    
    private const int GWL_EXSTYLE = -20;
    private const int GWL_STYLE = -16;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_CHILD = 0x40000000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    
    private static readonly IntPtr HWND_BOTTOM = new(1);
    
    private static IntPtr _cachedWorkerW = IntPtr.Zero;
    private static readonly object _lock = new();
    
    /// <summary>
    /// Obtient le handle de la fenêtre WorkerW (derrière les icônes du bureau)
    /// </summary>
    public static IntPtr GetWorkerW()
    {
        lock (_lock)
        {
            // Vérifier si le cache est valide
            if (_cachedWorkerW != IntPtr.Zero && IsWindowValid(_cachedWorkerW))
                return _cachedWorkerW;
            
            // Trouver la fenêtre Progman
            IntPtr progman = FindWindowW("Progman", null);
            if (progman == IntPtr.Zero)
                return IntPtr.Zero;
            
            // Envoyer un message pour créer WorkerW
            SendMessageW(progman, 0x052C, IntPtr.Zero, IntPtr.Zero);
            
            IntPtr workerW = IntPtr.Zero;
            
            // Trouver WorkerW
            EnumWindows((hWnd, _) =>
            {
                IntPtr shell = FindWindowExW(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (shell != IntPtr.Zero)
                {
                    workerW = FindWindowExW(IntPtr.Zero, hWnd, "WorkerW", null);
                }
                return true;
            }, IntPtr.Zero);
            
            _cachedWorkerW = workerW;
            return workerW;
        }
    }
    
    private static bool IsWindowValid(IntPtr hWnd)
    {
        // Simple check - si on peut obtenir le style, la fenêtre existe
        return GetWindowLongW(hWnd, GWL_STYLE) != 0;
    }
    
    /// <summary>
    /// Place une fenêtre derrière les icônes du bureau
    /// </summary>
    public static void SetAsDesktopChild(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            return;
        
        IntPtr workerW = GetWorkerW();
        
        if (workerW != IntPtr.Zero)
        {
            SetParent(windowHandle, workerW);
            
            // Modifier le style de la fenêtre
            int style = GetWindowLongW(windowHandle, GWL_STYLE);
            SetWindowLongW(windowHandle, GWL_STYLE, style | WS_CHILD);
            
            int exStyle = GetWindowLongW(windowHandle, GWL_EXSTYLE);
            SetWindowLongW(windowHandle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
        }
    }
    
    /// <summary>
    /// Envoie la fenêtre à l'arrière-plan
    /// </summary>
    public static void SendToBack(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            return;
        
        SetWindowPos(windowHandle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
    }
    
    /// <summary>
    /// Invalide le cache WorkerW (à appeler si la résolution change par exemple)
    /// </summary>
    public static void InvalidateCache()
    {
        lock (_lock)
        {
            _cachedWorkerW = IntPtr.Zero;
        }
    }
}
