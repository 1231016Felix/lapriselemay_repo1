using System.Runtime.InteropServices;
using System.Timers;
using System.Windows.Forms;
using Microsoft.Win32;
using Timer = System.Timers.Timer;

namespace WallpaperManager.Services;

/// <summary>
/// Service qui surveille l'état du système (plein écran, batterie) 
/// et notifie les autres services pour qu'ils réagissent.
/// </summary>
public sealed class SystemMonitorService : IDisposable
{
    #region Native APIs
    
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
    
    #endregion
    
    private readonly Timer _pollTimer;
    private readonly Lock _lock = new();
    private volatile bool _disposed;
    
    // Liste statique des processus système à ignorer (évite les allocations)
    private static readonly HashSet<string> SystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer",
        "shellexperiencehost",
        "searchapp",
        "startmenuexperiencehost",
        "applicationframehost",
        "lockapp",
        "wallpapermanager"
    };
    
    private bool _isFullscreenAppRunning;
    private bool _isOnBattery;
    private bool _wasPausedByFullscreen;
    private bool _wasPausedByBattery;
    
    public event EventHandler<bool>? FullscreenStateChanged;
    public event EventHandler<bool>? BatteryStateChanged;
    
    public bool IsFullscreenAppRunning => _isFullscreenAppRunning;
    public bool IsOnBattery => _isOnBattery;
    
    public SystemMonitorService()
    {
        // Timer pour vérifier périodiquement l'état plein écran
        _pollTimer = new Timer(2000); // Toutes les 2 secondes
        _pollTimer.Elapsed += OnPollTimerElapsed;
        _pollTimer.AutoReset = true;
        
        // S'abonner aux changements d'alimentation
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        
        // Vérifier l'état initial de la batterie
        UpdateBatteryState();
    }
    
    public void Start()
    {
        if (_disposed) return;
        _pollTimer.Start();
    }
    
    public void Stop()
    {
        _pollTimer.Stop();
    }
    
    private void OnPollTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (_disposed) return;
        
        try
        {
            var isFullscreen = DetectFullscreenApp();
            
            lock (_lock)
            {
                if (isFullscreen != _isFullscreenAppRunning)
                {
                    _isFullscreenAppRunning = isFullscreen;
                    FullscreenStateChanged?.Invoke(this, isFullscreen);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur détection plein écran: {ex.Message}");
        }
    }
    
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        // StatusChange indique un changement AC/Batterie
        if (e.Mode == PowerModes.StatusChange)
        {
            UpdateBatteryState();
        }
    }
    
    private void UpdateBatteryState()
    {
        try
        {
            var powerStatus = SystemInformation.PowerStatus;
            var isOnBattery = powerStatus.PowerLineStatus == PowerLineStatus.Offline;
            
            lock (_lock)
            {
                if (isOnBattery != _isOnBattery)
                {
                    _isOnBattery = isOnBattery;
                    BatteryStateChanged?.Invoke(this, isOnBattery);
                    
                    System.Diagnostics.Debug.WriteLine(
                        isOnBattery ? "Sur batterie" : "Sur secteur");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur vérification batterie: {ex.Message}");
        }
    }
    
    private bool DetectFullscreenApp()
    {
        try
        {
            var foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
                return false;
            
            // Obtenir les dimensions de la fenêtre active
            if (!GetWindowRect(foregroundWindow, out RECT windowRect))
                return false;
            
            // Obtenir les dimensions de l'écran principal
            var screen = Screen.FromHandle(foregroundWindow);
            var screenBounds = screen.Bounds;
            
            // Vérifier si la fenêtre couvre tout l'écran
            var windowWidth = windowRect.Right - windowRect.Left;
            var windowHeight = windowRect.Bottom - windowRect.Top;
            
            // Tolérance de quelques pixels pour les bordures
            const int tolerance = 10;
            
            var coversScreen = 
                windowWidth >= screenBounds.Width - tolerance &&
                windowHeight >= screenBounds.Height - tolerance;
            
            if (!coversScreen)
                return false;
            
            // Exclure le bureau Windows et la barre des tâches
            GetWindowThreadProcessId(foregroundWindow, out uint processId);
            
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById((int)processId);
                var processName = process.ProcessName;
                
                if (SystemProcesses.Contains(processName))
                    return false;
                
                System.Diagnostics.Debug.WriteLine($"App plein écran détectée: {processName}");
                return true;
            }
            catch (ArgumentException)
            {
                // Processus terminé entre-temps
                return false;
            }
            catch (InvalidOperationException)
            {
                // Accès refusé, considérer comme plein écran par sécurité
                return true;
            }
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Applique les règles de pause basées sur les paramètres utilisateur.
    /// Retourne true si les fonds animés doivent être en pause.
    /// </summary>
    public bool ShouldPauseAnimated()
    {
        var settings = SettingsService.Current;
        
        if (settings.PauseOnFullscreen && _isFullscreenAppRunning)
            return true;
        
        if (settings.PauseOnBattery && _isOnBattery)
            return true;
        
        return false;
    }
    
    /// <summary>
    /// Gère automatiquement la pause/reprise des fonds animés.
    /// </summary>
    public void ManageAnimatedWallpaper(AnimatedWallpaperService animatedService)
    {
        if (animatedService == null) return;
        
        var shouldPause = ShouldPauseAnimated();
        
        if (shouldPause && animatedService.IsPlaying)
        {
            animatedService.Pause();
            
            if (_isFullscreenAppRunning)
                _wasPausedByFullscreen = true;
            if (_isOnBattery)
                _wasPausedByBattery = true;
                
            System.Diagnostics.Debug.WriteLine("Fond animé mis en pause (système)");
        }
        else if (!shouldPause && (_wasPausedByFullscreen || _wasPausedByBattery))
        {
            // Reprendre seulement si on avait mis en pause automatiquement
            animatedService.Resume();
            _wasPausedByFullscreen = false;
            _wasPausedByBattery = false;
            
            System.Diagnostics.Debug.WriteLine("Fond animé repris (système)");
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _pollTimer.Stop();
        _pollTimer.Elapsed -= OnPollTimerElapsed;
        _pollTimer.Dispose();
        
        try
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
        catch (InvalidOperationException)
        {
            // Peut arriver si appelé depuis un thread non-UI
        }
    }
}
