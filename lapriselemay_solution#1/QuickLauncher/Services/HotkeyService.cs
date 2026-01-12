using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Service de gestion du raccourci clavier global avec meilleure gestion des ressources.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;
    
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    
    private HwndSource? _source;
    private IntPtr _windowHandle;
    private bool _isRegistered;
    private bool _disposed;
    private readonly HotkeySettings _hotkeySettings;
    
    public event EventHandler? HotkeyPressed;
    public bool IsRegistered => _isRegistered;
    
    public HotkeyService() : this(AppSettings.Load().Hotkey) { }
    
    public HotkeyService(HotkeySettings settings)
    {
        _hotkeySettings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public bool Register()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isRegistered) return true;
        
        try
        {
            var parameters = new HwndSourceParameters("HotkeyWindow")
            {
                Width = 0,
                Height = 0,
                PositionX = -100,
                PositionY = -100,
                WindowStyle = 0
            };
            
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
            _windowHandle = _source.Handle;
            
            var modifiers = GetModifiers();
            var vk = GetVirtualKeyCode(_hotkeySettings.Key);
            
            _isRegistered = RegisterHotKey(_windowHandle, Constants.HotkeyId, modifiers, vk);
            
            Debug.WriteLine(_isRegistered 
                ? $"Hotkey enregistré: {_hotkeySettings.DisplayText}" 
                : $"Échec enregistrement hotkey: {_hotkeySettings.DisplayText}");
            
            return _isRegistered;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur Register: {ex.Message}");
            return false;
        }
    }
    
    private uint GetModifiers()
    {
        uint modifiers = MOD_NOREPEAT;
        if (_hotkeySettings.UseAlt) modifiers |= MOD_ALT;
        if (_hotkeySettings.UseCtrl) modifiers |= MOD_CONTROL;
        if (_hotkeySettings.UseShift) modifiers |= MOD_SHIFT;
        if (_hotkeySettings.UseWin) modifiers |= MOD_WIN;
        return modifiers;
    }
    
    private static uint GetVirtualKeyCode(string keyName) => keyName.ToUpperInvariant() switch
    {
        "SPACE" => 0x20,
        "ENTER" or "RETURN" => 0x0D,
        "TAB" => 0x09,
        "ESCAPE" or "ESC" => 0x1B,
        "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73,
        "F5" => 0x74, "F6" => 0x75, "F7" => 0x76, "F8" => 0x77,
        "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
        _ when keyName.Length == 1 && char.IsAsciiLetter(keyName[0]) 
            => (uint)char.ToUpperInvariant(keyName[0]),
        _ => 0x20 // Default to Space
    };
    
    public void Unregister()
    {
        if (!_isRegistered) return;
        
        UnregisterHotKey(_windowHandle, Constants.HotkeyId);
        _source?.RemoveHook(WndProc);
        _source?.Dispose();
        _source = null;
        _windowHandle = IntPtr.Zero;
        _isRegistered = false;
    }
    
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Constants.WM_HOTKEY && wParam.ToInt32() == Constants.HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        Unregister();
        GC.SuppressFinalize(this);
    }
}
