using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CleanUninstaller.Models;
using CleanUninstaller.ViewModels;
using Windows.Storage.Pickers;

namespace CleanUninstaller.Views;

/// <summary>
/// Page de monitoring d'installation
/// </summary>
public sealed partial class InstallationMonitorPage : Page
{
    public InstallationMonitorViewModel ViewModel { get; }

    public InstallationMonitorPage()
    {
        ViewModel = new InstallationMonitorViewModel();
        this.InitializeComponent();
        
        // Charger les installations sauvegardées au démarrage
        Loaded += async (s, e) => await ViewModel.InitializeAsync();
        Unloaded += (s, e) => ViewModel.Dispose();
    }

    private async void BrowseInstaller_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        
        // Initialiser avec la fenêtre
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        picker.SuggestedStartLocation = PickerLocationId.Downloads;
        picker.FileTypeFilter.Add(".exe");
        picker.FileTypeFilter.Add(".msi");
        picker.FileTypeFilter.Add(".msix");
        picker.FileTypeFilter.Add("*");

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            ViewModel.InstallerPath = file.Path;
            
            // Extraire le nom du programme depuis le nom du fichier si pas déjà rempli
            if (string.IsNullOrWhiteSpace(ViewModel.InstallationName))
            {
                var fileName = System.IO.Path.GetFileNameWithoutExtension(file.Name);
                // Nettoyer le nom (enlever setup, install, etc.)
                fileName = System.Text.RegularExpressions.Regex.Replace(
                    fileName, 
                    @"[-_\s]*(setup|install|installer|x64|x86|win|windows|v?\d+[\.\d]*)+[-_\s]*",
                    " ",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                ViewModel.InstallationName = fileName.Trim();
            }
        }
    }

    private void DeleteInstallation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is MonitoredInstallation installation)
        {
            ViewModel.DeleteSavedInstallationCommand.Execute(installation);
        }
    }

    private async void ViewDetails_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedInstallation == null) return;

        var dialog = new ChangesDetailDialog(ViewModel.SelectedInstallation)
        {
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            // Lancer la désinstallation
            await ViewModel.PerfectUninstallCommand.ExecuteAsync(null);
        }
    }

    private void InstallationsList_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.SelectedInstallation != null)
        {
            ViewDetails_Click(sender, e);
        }
    }
}
