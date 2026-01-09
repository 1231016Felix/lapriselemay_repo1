using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using WinRT.Interop;

namespace CleanUninstaller.Views;

/// <summary>
/// Fenêtre du moniteur d'installation
/// </summary>
public sealed partial class MonitorWindow : Window
{
    public MonitorWindow()
    {
        InitializeComponent();
        ConfigureWindow();
    }

    private void ConfigureWindow()
    {
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        // Taille adaptée pour le moniteur
        appWindow.Resize(new Windows.Graphics.SizeInt32(1200, 800));

        // Centrer la fenêtre
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
        if (displayArea != null)
        {
            var centerX = (displayArea.WorkArea.Width - 1200) / 2;
            var centerY = (displayArea.WorkArea.Height - 800) / 2;
            appWindow.Move(new Windows.Graphics.PointInt32(centerX, centerY));
        }

        appWindow.Title = "Moniteur d'installation - Clean Uninstaller";
        
        // Étendre le contenu dans la barre de titre pour le style Mica
        ExtendsContentIntoTitleBar = true;
    }
}
