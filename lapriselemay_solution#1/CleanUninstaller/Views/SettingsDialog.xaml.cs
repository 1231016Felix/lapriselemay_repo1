using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CleanUninstaller.Models;
using CleanUninstaller.Services;
using CleanUninstaller.Services.Interfaces;
using System.Diagnostics;

namespace CleanUninstaller.Views;

/// <summary>
/// Dialogue de paramètres de l'application
/// </summary>
public sealed partial class SettingsDialog : ContentDialog
{
    private readonly ISettingsService _settingsService;
    private readonly AppSettings _settings;

    public SettingsDialog()
    {
        InitializeComponent();
        
        _settingsService = App.SettingsService;
        _settings = _settingsService.Settings;
        
        LoadSettings();
    }

    private void LoadSettings()
    {
        CreateRestorePointToggle.IsOn = _settings.CreateRestorePoint;
        CreateRegistryBackupToggle.IsOn = _settings.CreateRegistryBackup;
        PreferQuietUninstallToggle.IsOn = _settings.PreferQuietUninstall;
        ThoroughAnalysisToggle.IsOn = _settings.ThoroughAnalysisEnabled;
        ThemeComboBox.SelectedIndex = _settings.Theme;
    }

    private async void SaveButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Sauvegarder les paramètres
        _settings.CreateRestorePoint = CreateRestorePointToggle.IsOn;
        _settings.CreateRegistryBackup = CreateRegistryBackupToggle.IsOn;
        _settings.PreferQuietUninstall = PreferQuietUninstallToggle.IsOn;
        _settings.ThoroughAnalysisEnabled = ThoroughAnalysisToggle.IsOn;
        _settings.Theme = ThemeComboBox.SelectedIndex;

        await _settingsService.SaveAsync();
    }

    private void OpenBackupsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var backupsFolder = _settingsService.GetBackupsFolder();
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = backupsFolder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur ouverture dossier: {ex.Message}");
        }
    }

    private void CleanupBackups_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.CleanupOldBackups(10);
    }

    private async void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Confirmation",
            Content = "Voulez-vous vraiment réinitialiser tous les paramètres ?",
            PrimaryButtonText = "Oui",
            SecondaryButtonText = "Non",
            DefaultButton = ContentDialogButton.Secondary,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await _settingsService.ResetToDefaultsAsync();
            LoadSettings();
        }
    }
}
