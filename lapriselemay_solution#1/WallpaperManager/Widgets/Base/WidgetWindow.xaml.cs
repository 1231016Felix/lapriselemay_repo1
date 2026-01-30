using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using WpfUserControl = System.Windows.Controls.UserControl;
using WallpaperManager.Models;

namespace WallpaperManager.Widgets.Base;

/// <summary>
/// Fenêtre transparente pour afficher un widget sur le bureau.
/// Supporte le blur Windows 10/11, le redimensionnement et le Z-order dynamique.
/// </summary>
public partial class WidgetWindow : Window
{
    #region Native APIs
    
    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
    
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
    
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }
    
    private enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }
    
    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        ACCENT_INVALID_STATE = 5
    }
    
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    
    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTBOTTOMRIGHT = 17;
    
    #endregion
    
    private WidgetViewModelBase? _viewModel;
    private WidgetConfig? _config;
    private IntPtr _hwnd;
    private bool _allowHide;
    private DispatcherTimer? _focusWatcher;
    private bool _isDesktopMode;
    
    public string WidgetId => _config?.Id ?? string.Empty;
    
    /// <summary>
    /// Permet de désactiver temporairement la protection contre le masquage.
    /// </summary>
    public bool AllowHide
    {
        get => _allowHide;
        set
        {
            _allowHide = value;
            if (value)
                _focusWatcher?.Stop();
            else
                _focusWatcher?.Start();
        }
    }
    
    public WidgetWindow()
    {
        InitializeComponent();
    }
    
    /// <summary>
    /// Configure le widget avec son contenu et sa configuration.
    /// </summary>
    public void SetWidget(WpfUserControl widgetControl, WidgetViewModelBase viewModel, WidgetConfig config)
    {
        _viewModel = viewModel;
        _config = config;
        
        // Appliquer la configuration
        Left = config.Left;
        Top = config.Top;
        Width = config.Width;
        Height = config.Height;
        
        // Appliquer l'opacité du fond
        if (BackgroundBrush != null)
        {
            BackgroundBrush.Opacity = config.BackgroundOpacity;
        }
        
        // Définir le contenu
        widgetControl.DataContext = viewModel;
        WidgetContent.Content = widgetControl;
        
        viewModel.WidgetId = config.Id;
        
        // Notifier la taille initiale au ViewModel
        viewModel.OnSizeChanged(config.Width, config.Height);
    }
    
    /// <summary>
    /// Met à jour l'opacité du fond.
    /// </summary>
    public void SetBackgroundOpacity(double opacity)
    {
        if (BackgroundBrush != null)
        {
            BackgroundBrush.Opacity = Math.Clamp(opacity, 0.0, 1.0);
        }
    }
    
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        
        // Empêcher la fenêtre de prendre le focus et de s'afficher dans la taskbar
        var exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        
        // Activer l'effet blur/acrylic
        EnableBlur(_hwnd);
        
        // Timer pour surveiller le focus et ajuster le Z-order
        _focusWatcher = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _focusWatcher.Tick += OnFocusWatcherTick;
        _focusWatcher.Start();
        
        // Démarrer le ViewModel
        _viewModel?.Start();
    }
    
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Sauvegarder les nouvelles dimensions
        if (_config != null)
        {
            _config.Width = ActualWidth;
            _config.Height = ActualHeight;
        }
        
        // Notifier le ViewModel du changement de taille
        _viewModel?.OnSizeChanged(ActualWidth, ActualHeight);
    }
    
    private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_hwnd != IntPtr.Zero)
        {
            ReleaseCapture();
            SendMessage(_hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTBOTTOMRIGHT, IntPtr.Zero);
        }
    }
    
    /// <summary>
    /// Ajuste le Z-order selon la fenêtre active.
    /// </summary>
    private void OnFocusWatcherTick(object? sender, EventArgs e)
    {
        if (_allowHide || _hwnd == IntPtr.Zero) return;
        
        try
        {
            bool desktopActive = IsDesktopOrTaskbarForeground();
            
            if (desktopActive && !_isDesktopMode)
            {
                _isDesktopMode = true;
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, 
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            else if (!desktopActive && _isDesktopMode)
            {
                _isDesktopMode = false;
                
                var foreground = GetForegroundWindow();
                SetWindowPos(_hwnd, foreground, 0, 0, 0, 0, 
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                
                SetWindowPos(_hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, 
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }
        catch
        {
            // Ignorer les erreurs
        }
    }
    
    private static bool IsDesktopOrTaskbarForeground()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return true;
        
        var className = new System.Text.StringBuilder(256);
        GetClassName(foreground, className, className.Capacity);
        var name = className.ToString();
        
        // Seulement le bureau réel, pas la barre de tâches
        // Shell_TrayWnd et Shell_SecondaryTrayWnd exclus pour éviter
        // que les widgets apparaissent au clic sur la taskbar
        return name == "WorkerW" || name == "Progman";
    }
    
    private void EnableBlur(IntPtr hwnd)
    {
        try
        {
            var accent = new AccentPolicy
            {
                AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                GradientColor = unchecked((int)0xCC2E1A1A)
            };
            
            var accentSize = Marshal.SizeOf(accent);
            var accentPtr = Marshal.AllocHGlobal(accentSize);
            
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);
                
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    SizeOfData = accentSize,
                    Data = accentPtr
                };
                
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Impossible d'activer le blur: {ex.Message}");
        }
    }
    
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            
            if (_config != null)
            {
                _config.Left = Left;
                _config.Top = Top;
            }
        }
    }
    
    protected override void OnClosed(EventArgs e)
    {
        _focusWatcher?.Stop();
        _viewModel?.Stop();
        _viewModel?.Dispose();
        base.OnClosed(e);
    }
}
