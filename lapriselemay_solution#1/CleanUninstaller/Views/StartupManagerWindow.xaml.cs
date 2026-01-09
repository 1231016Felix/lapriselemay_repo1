using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using WinRT.Interop;

namespace CleanUninstaller.Views;

/// <summary>
/// Fenêtre du gestionnaire de programmes au démarrage
/// </summary>
public sealed partial class StartupManagerWindow : Window
{
    public StartupManagerWindow()
    {
        InitializeComponent();
        ConfigureWindow();
    }

    private void ConfigureWindow()
    {
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        // Taille adaptée pour le gestionnaire
        appWindow.Resize(new Windows.Graphics.SizeInt32(1300, 850));

        // Centrer la fenêtre
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
        if (displayArea != null)
        {
            var centerX = (displayArea.WorkArea.Width - 1300) / 2;
            var centerY = (displayArea.WorkArea.Height - 850) / 2;
            appWindow.Move(new Windows.Graphics.PointInt32(centerX, centerY));
        }

        appWindow.Title = "Programmes au démarrage - Clean Uninstaller";
        
        // Étendre le contenu dans la barre de titre pour le style Mica
        ExtendsContentIntoTitleBar = true;
    }
}
