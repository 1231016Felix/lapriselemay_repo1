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
    private static partial IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    
    [LibraryImport("user32.dll")]
    private static partial IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);
    
    [LibraryImport("user32.dll")]
    private static partial IntPtr SendMessageTimeoutW(
        IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
    
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);
    
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateWindow(IntPtr hWnd);
    
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(IntPtr hWnd);
    
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    
    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int nIndex);

    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    
    private const int GWL_EXSTYLE = -20;
    private const int GWL_STYLE = -16;
    
    private const long WS_CHILD = 0x40000000L;
    private const long WS_POPUP = 0x80000000L;
    private const long WS_VISIBLE = 0x10000000L;
    private const long WS_CAPTION = 0x00C00000L;
    private const long WS_THICKFRAME = 0x00040000L;
    private const long WS_MINIMIZEBOX = 0x00020000L;
    private const long WS_MAXIMIZEBOX = 0x00010000L;
    private const long WS_SYSMENU = 0x00080000L;
    private const long WS_BORDER = 0x00800000L;
    private const long WS_DLGFRAME = 0x00400000L;
    
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_NOACTIVATE = 0x08000000L;
    private const long WS_EX_LAYERED = 0x00080000L;
    private const long WS_EX_TOPMOST = 0x00000008L;
    
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;
    
    private const int SW_SHOWNA = 8;
    private const uint SMTO_NORMAL = 0x0000;
    
    private static IntPtr _progman = IntPtr.Zero;
    private static IntPtr _defView = IntPtr.Zero;
    private static readonly object _lock = new();

    private static void Log(string message)
    {
        try
        {
            var logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WallpaperManager", "desktop_api.log");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }

    /// <summary>
    /// Trouve Progman et SHELLDLL_DefView
    /// </summary>
    private static bool FindDesktopWindows()
    {
        Log("=== FindDesktopWindows START ===");
        
        // Trouver Progman
        _progman = FindWindowW("Progman", null);
        if (_progman == IntPtr.Zero)
        {
            Log("Progman non trouvé!");
            return false;
        }
        Log($"Progman: 0x{_progman:X}");
        
        // Envoyer le message 0x052C (peut aider sur certains systèmes)
        SendMessageTimeoutW(_progman, 0x052C, IntPtr.Zero, IntPtr.Zero, SMTO_NORMAL, 1000, out _);
        System.Threading.Thread.Sleep(100);
        
        // Trouver SHELLDLL_DefView dans Progman
        _defView = FindWindowExW(_progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (_defView == IntPtr.Zero)
        {
            Log("SHELLDLL_DefView non trouvé dans Progman!");
            return false;
        }
        Log($"SHELLDLL_DefView: 0x{_defView:X}");
        
        return true;
    }

    /// <summary>
    /// Place une fenêtre derrière les icônes du bureau
    /// </summary>
    public static bool SetAsDesktopChild(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            return false;
        
        try
        {
            Log($"=== SetAsDesktopChild START, window: 0x{windowHandle:X} ===");
            
            lock (_lock)
            {
                if (!FindDesktopWindows())
                {
                    Log("ÉCHEC: Impossible de trouver les fenêtres du bureau");
                    return false;
                }
            }
            
            // Obtenir les dimensions de Progman
            GetWindowRect(_progman, out RECT parentRect);
            int width = parentRect.Right - parentRect.Left;
            int height = parentRect.Bottom - parentRect.Top;
            Log($"Progman rect: Left={parentRect.Left}, Top={parentRect.Top}, Right={parentRect.Right}, Bottom={parentRect.Bottom}");
            Log($"Progman dimensions: {width}x{height}");
            
            if (width == 0 || height == 0)
            {
                width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
                height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
                Log($"Fallback écran virtuel: {width}x{height}");
            }
            
            // Modifier les styles pour en faire une fenêtre enfant sans bordure
            long style = (long)GetWindowLongPtrW(windowHandle, GWL_STYLE);
            style &= ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | 
                       WS_MAXIMIZEBOX | WS_SYSMENU | WS_BORDER | WS_DLGFRAME);
            style |= WS_CHILD | WS_VISIBLE;
            SetWindowLongPtrW(windowHandle, GWL_STYLE, (IntPtr)style);
            
            long exStyle = (long)GetWindowLongPtrW(windowHandle, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            exStyle &= ~(WS_EX_LAYERED | WS_EX_TOPMOST);
            SetWindowLongPtrW(windowHandle, GWL_EXSTYLE, (IntPtr)exStyle);
            
            // Définir comme enfant de Progman
            var result = SetParent(windowHandle, _progman);
            Log($"SetParent(Progman) result: 0x{result:X}");
            
            if (result == IntPtr.Zero)
            {
                Log("ÉCHEC: SetParent a retourné null!");
                return false;
            }
            
            // Positionner DERRIÈRE SHELLDLL_DefView (les icônes)
            // En utilisant _defView comme hwndInsertAfter, notre fenêtre sera placée
            // juste derrière les icônes dans l'ordre Z
            bool posResult = SetWindowPos(windowHandle, _defView, 0, 0, width, height,
                SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOACTIVATE);
            Log($"SetWindowPos(0, 0, {width}, {height}) result: {posResult}");
            
            // Vérifier la position finale
            GetWindowRect(windowHandle, out RECT videoRect);
            Log($"Video rect après: Left={videoRect.Left}, Top={videoRect.Top}, Right={videoRect.Right}, Bottom={videoRect.Bottom}");
            
            ShowWindow(windowHandle, SW_SHOWNA);
            UpdateWindow(windowHandle);
            
            Log("=== SetAsDesktopChild SUCCESS ===");
            return true;
        }
        catch (Exception ex)
        {
            Log($"EXCEPTION: {ex.Message}");
            return false;
        }
    }
    
    public static IntPtr GetWorkerW()
    {
        lock (_lock)
        {
            FindDesktopWindows();
            return _progman;
        }
    }
    
    public static void InvalidateCache()
    {
        lock (_lock)
        {
            _progman = IntPtr.Zero;
            _defView = IntPtr.Zero;
        }
    }
    
    public static bool IsWorkerWValid()
    {
        lock (_lock)
        {
            return _progman != IntPtr.Zero && IsWindow(_progman);
        }
    }
    
    public static void SendToBack(IntPtr windowHandle)
    {
        if (windowHandle != IntPtr.Zero && _defView != IntPtr.Zero)
            SetWindowPos(windowHandle, _defView, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
    }
}
