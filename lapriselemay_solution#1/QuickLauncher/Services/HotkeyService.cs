using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using QuickLauncher.Models;

namespace QuickLauncher.Services;

public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9000;
    
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
    private readonly AppSettings _settings;
    
    public event EventHandler? HotkeyPressed;
    
    public HotkeyService()
    {
        _settings = AppSettings.Load();
    }

    public void Register()
    {
        if (_isRegistered || _disposed) return;
        
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
        var vk = GetVirtualKeyCode(_settings.Hotkey.Key);
        
        _isRegistered = RegisterHotKey(_windowHandle, HOTKEY_ID, modifiers, vk);
        
        Debug.WriteLine(_isRegistered 
            ? $"Hotkey enregistré: {_settings.Hotkey.DisplayText}" 
            : $"Échec enregistrement hotkey: {_settings.Hotkey.DisplayText}");
    }
    
    private uint GetModifiers()
    {
        uint modifiers = MOD_NOREPEAT;
        if (_settings.Hotkey.UseAlt) modifiers |= MOD_ALT;
        if (_settings.Hotkey.UseCtrl) modifiers |= MOD_CONTROL;
        if (_settings.Hotkey.UseShift) modifiers |= MOD_SHIFT;
        if (_settings.Hotkey.UseWin) modifiers |= MOD_WIN;
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
        _ when keyName.Length == 1 && char.IsLetter(keyName[0]) => (uint)char.ToUpper(keyName[0]),
        _ => 0x20
    };
    
    public void Unregister()
    {
        if (!_isRegistered) return;
        
        UnregisterHotKey(_windowHandle, HOTKEY_ID);
        _source?.RemoveHook(WndProc);
        _source?.Dispose();
        _source = null;
        _isRegistered = false;
    }
    
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
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
    }
}
