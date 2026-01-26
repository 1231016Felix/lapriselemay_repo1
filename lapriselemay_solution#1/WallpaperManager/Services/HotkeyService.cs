using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WallpaperManager.Services;

/// <summary>
/// Service de gestion des raccourcis clavier globaux Windows.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    
    // IDs des hotkeys
    private const int HOTKEY_NEXT = 1;
    private const int HOTKEY_PREVIOUS = 2;
    private const int HOTKEY_FAVORITE = 3;
    private const int HOTKEY_PAUSE = 4;
    
    private IntPtr _windowHandle;
    private HwndSource? _source;
    private bool _disposed;
    private bool _registered;
    
    public event EventHandler? NextWallpaperRequested;
    public event EventHandler? PreviousWallpaperRequested;
    public event EventHandler? ToggleFavoriteRequested;
    public event EventHandler? TogglePauseRequested;
    
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    
    // Modificateurs
    [Flags]
    private enum KeyModifiers : uint
    {
        None = 0,
        Alt = 1,
        Control = 2,
        Shift = 4,
        Win = 8
    }
    
    // Virtual Key Codes
    private const uint VK_LEFT = 0x25;
    private const uint VK_RIGHT = 0x27;
    private const uint VK_F = 0x46;
    private const uint VK_SPACE = 0x20;
    
    public void Initialize(Window window)
    {
        if (_disposed) return;
        
        var helper = new WindowInteropHelper(window);
        _windowHandle = helper.Handle;
        
        if (_windowHandle == IntPtr.Zero)
        {
            // La fenêtre n'est pas encore créée, attendre
            window.SourceInitialized += (s, e) =>
            {
                _windowHandle = new WindowInteropHelper(window).Handle;
                SetupHwndSource();
                RegisterHotkeys();
            };
        }
        else
        {
            SetupHwndSource();
            RegisterHotkeys();
        }
    }
    
    private void SetupHwndSource()
    {
        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(HwndHook);
    }
    
    public void RegisterHotkeys()
    {
        if (_disposed || _windowHandle == IntPtr.Zero || _registered) return;
        
        var settings = SettingsService.Current;
        
        // Win+Alt+Right - Suivant
        if (settings.HotkeysEnabled)
        {
            var (nextMod, nextKey) = ParseHotkey(settings.HotkeyNextWallpaper);
            if (nextKey != 0)
                RegisterHotKey(_windowHandle, HOTKEY_NEXT, (uint)nextMod, nextKey);
            
            // Win+Alt+Left - Précédent
            var (prevMod, prevKey) = ParseHotkey(settings.HotkeyPreviousWallpaper);
            if (prevKey != 0)
                RegisterHotKey(_windowHandle, HOTKEY_PREVIOUS, (uint)prevMod, prevKey);
            
            // Win+Alt+F - Favoris
            var (favMod, favKey) = ParseHotkey(settings.HotkeyToggleFavorite);
            if (favKey != 0)
                RegisterHotKey(_windowHandle, HOTKEY_FAVORITE, (uint)favMod, favKey);
            
            // Win+Alt+Space - Pause
            var (pauseMod, pauseKey) = ParseHotkey(settings.HotkeyPauseRotation);
            if (pauseKey != 0)
                RegisterHotKey(_windowHandle, HOTKEY_PAUSE, (uint)pauseMod, pauseKey);
        }
        
        _registered = true;
        System.Diagnostics.Debug.WriteLine("Raccourcis clavier globaux enregistrés");
    }
    
    public void UnregisterHotkeys()
    {
        if (_windowHandle == IntPtr.Zero || !_registered) return;
        
        UnregisterHotKey(_windowHandle, HOTKEY_NEXT);
        UnregisterHotKey(_windowHandle, HOTKEY_PREVIOUS);
        UnregisterHotKey(_windowHandle, HOTKEY_FAVORITE);
        UnregisterHotKey(_windowHandle, HOTKEY_PAUSE);
        
        _registered = false;
        System.Diagnostics.Debug.WriteLine("Raccourcis clavier globaux désenregistrés");
    }
    
    public void ReloadHotkeys()
    {
        UnregisterHotkeys();
        RegisterHotkeys();
    }
    
    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            
            switch (id)
            {
                case HOTKEY_NEXT:
                    System.Diagnostics.Debug.WriteLine("Hotkey: Suivant");
                    NextWallpaperRequested?.Invoke(this, EventArgs.Empty);
                    handled = true;
                    break;
                    
                case HOTKEY_PREVIOUS:
                    System.Diagnostics.Debug.WriteLine("Hotkey: Précédent");
                    PreviousWallpaperRequested?.Invoke(this, EventArgs.Empty);
                    handled = true;
                    break;
                    
                case HOTKEY_FAVORITE:
                    System.Diagnostics.Debug.WriteLine("Hotkey: Favoris");
                    ToggleFavoriteRequested?.Invoke(this, EventArgs.Empty);
                    handled = true;
                    break;
                    
                case HOTKEY_PAUSE:
                    System.Diagnostics.Debug.WriteLine("Hotkey: Pause");
                    TogglePauseRequested?.Invoke(this, EventArgs.Empty);
                    handled = true;
                    break;
            }
        }
        
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Parse une chaîne de raccourci comme "Win+Alt+Right" en modificateurs et code de touche.
    /// </summary>
    private static (KeyModifiers modifiers, uint virtualKey) ParseHotkey(string? hotkeyString)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString))
            return (KeyModifiers.None, 0);
        
        var modifiers = KeyModifiers.None;
        uint virtualKey = 0;
        
        var parts = hotkeyString.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        
        foreach (var part in parts)
        {
            var upper = part.ToUpperInvariant();
            
            switch (upper)
            {
                case "WIN":
                case "WINDOWS":
                    modifiers |= KeyModifiers.Win;
                    break;
                case "ALT":
                    modifiers |= KeyModifiers.Alt;
                    break;
                case "CTRL":
                case "CONTROL":
                    modifiers |= KeyModifiers.Control;
                    break;
                case "SHIFT":
                    modifiers |= KeyModifiers.Shift;
                    break;
                case "LEFT":
                    virtualKey = VK_LEFT;
                    break;
                case "RIGHT":
                    virtualKey = VK_RIGHT;
                    break;
                case "SPACE":
                    virtualKey = VK_SPACE;
                    break;
                default:
                    // Lettre simple (A-Z)
                    if (upper.Length == 1 && upper[0] >= 'A' && upper[0] <= 'Z')
                    {
                        virtualKey = (uint)upper[0];
                    }
                    // Chiffre (0-9)
                    else if (upper.Length == 1 && upper[0] >= '0' && upper[0] <= '9')
                    {
                        virtualKey = (uint)upper[0];
                    }
                    // Touches de fonction (F1-F12)
                    else if (upper.StartsWith("F") && upper.Length <= 3 && 
                             int.TryParse(upper[1..], out var fNum) && fNum >= 1 && fNum <= 12)
                    {
                        virtualKey = (uint)(0x70 + fNum - 1); // VK_F1 = 0x70
                    }
                    break;
            }
        }
        
        return (modifiers, virtualKey);
    }
    
    /// <summary>
    /// Formate un raccourci pour l'affichage.
    /// </summary>
    public static string FormatHotkey(string? hotkeyString)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString))
            return "Non défini";
        
        return hotkeyString
            .Replace("Win", "⊞")
            .Replace("Alt", "Alt")
            .Replace("Ctrl", "Ctrl")
            .Replace("Shift", "⇧")
            .Replace("Right", "→")
            .Replace("Left", "←")
            .Replace("Space", "␣");
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        UnregisterHotkeys();
        
        _source?.RemoveHook(HwndHook);
        _source = null;
    }
}
