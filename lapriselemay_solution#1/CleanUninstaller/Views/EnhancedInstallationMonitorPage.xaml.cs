using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CleanUninstaller.Models;
using CleanUninstaller.Services;
using CleanUninstaller.ViewModels;
using Windows.Storage.Pickers;

namespace CleanUninstaller.Views;

/// <summary>
/// Page améliorée de monitoring d'installation avec support complet
/// </summary>
public sealed partial class EnhancedInstallationMonitorPage : Page
{
    public EnhancedInstallationMonitorViewModel ViewModel { get; }

    public EnhancedInstallationMonitorPage()
    {
        ViewModel = new EnhancedInstallationMonitorViewModel();
        this.InitializeComponent();

        Loaded += async (s, e) => await ViewModel.InitializeAsync();
        Unloaded += (s, e) => ViewModel.Dispose();
    }

    private async void BrowseInstaller_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        picker.SuggestedStartLocation = PickerLocationId.Downloads;
        picker.FileTypeFilter.Add(".exe");
        picker.FileTypeFilter.Add(".msi");
        picker.FileTypeFilter.Add(".msix");
        picker.FileTypeFilter.Add(".appx");
        picker.FileTypeFilter.Add("*");

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            ViewModel.InstallerPath = file.Path;

            // Extraire le nom du programme depuis le nom du fichier si pas déjà rempli
            if (string.IsNullOrWhiteSpace(ViewModel.InstallationName))
            {
                var fileName = System.IO.Path.GetFileNameWithoutExtension(file.Name);
                // Nettoyer le nom
                fileName = System.Text.RegularExpressions.Regex.Replace(
                    fileName,
                    @"[-_\s]*(setup|install|installer|x64|x86|win|windows|v?\d+[\.\d]*|amd64|arm64)+[-_\s]*",
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

    private void DeleteBackup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedBackup != null)
        {
            ViewModel.DeleteBackupCommand.Execute(ViewModel.SelectedBackup);
        }
    }
}
