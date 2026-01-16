using Microsoft.Win32;
using QuickLauncher.Models;
using QuickLauncher.Services;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ComboBox = System.Windows.Controls.ComboBox;
using MessageBox = System.Windows.MessageBox;

namespace QuickLauncher.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly IndexingService? _indexingService;
    
    private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "QuickLauncher";

    public SettingsWindow(IndexingService? indexingService = null)
    {
        InitializeComponent();
        _settings = AppSettings.Load();
        _indexingService = indexingService;
        LoadSettings();
        LoadStatistics();
    }

    private void LoadSettings()
    {
        // Général
        StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
        MinimizeOnStartupCheck.IsChecked = _settings.MinimizeOnStartup;
        ShowInTaskbarCheck.IsChecked = _settings.ShowInTaskbar;
        CloseAfterLaunchCheck.IsChecked = _settings.CloseAfterLaunch;
        SingleClickLaunchCheck.IsChecked = _settings.SingleClickLaunch;
        ShowIndexingStatusCheck.IsChecked = _settings.ShowIndexingStatus;
        ShowSettingsButtonCheck.IsChecked = _settings.ShowSettingsButton;
        MaxResultsSlider.Value = _settings.MaxResults;
        MaxResultsValue.Text = _settings.MaxResults.ToString();
        SelectComboByTag(WindowPositionCombo, _settings.WindowPosition);
        
        // Historique
        EnableSearchHistoryCheck.IsChecked = _settings.EnableSearchHistory;
        MaxHistorySlider.Value = _settings.MaxSearchHistory;
        MaxHistoryValue.Text = _settings.MaxSearchHistory.ToString();
        
        // Apparence
        SelectComboByTag(ThemeCombo, _settings.Theme);
        OpacitySlider.Value = _settings.WindowOpacity;
        OpacityValue.Text = $"{(int)(_settings.WindowOpacity * 100)}%";
        EnableAnimationsCheck.IsChecked = _settings.EnableAnimations;
        SelectComboByTag(AccentColorCombo, _settings.AccentColor);
        UpdateColorPreview(_settings.AccentColor);
        
        // Raccourci
        HotkeyAltCheck.IsChecked = _settings.Hotkey.UseAlt;
        HotkeyCtrlCheck.IsChecked = _settings.Hotkey.UseCtrl;
        HotkeyShiftCheck.IsChecked = _settings.Hotkey.UseShift;
        HotkeyWinCheck.IsChecked = _settings.Hotkey.UseWin;
        SelectComboByTag(HotkeyKeyCombo, _settings.Hotkey.Key);
        UpdateHotkeyDisplay();
        
        // Indexation
        IndexedFoldersList.ItemsSource = _settings.IndexedFolders;
        FileExtensionsBox.Text = string.Join(", ", _settings.FileExtensions);
        SearchDepthSlider.Value = _settings.SearchDepth;
        SearchDepthValue.Text = _settings.SearchDepth.ToString();
        IndexHiddenFoldersCheck.IsChecked = _settings.IndexHiddenFolders;
        IndexBrowserBookmarksCheck.IsChecked = _settings.IndexBrowserBookmarks;
        
        // Réindexation automatique
        AutoReindexEnabledCheck.IsChecked = _settings.AutoReindexEnabled;
        ReindexIntervalRadio.IsChecked = _settings.AutoReindexMode == AutoReindexMode.Interval;
        ReindexTimeRadio.IsChecked = _settings.AutoReindexMode == AutoReindexMode.ScheduledTime;
        SelectComboByTag(ReindexIntervalCombo, _settings.AutoReindexIntervalMinutes.ToString());
        LoadScheduledTime(_settings.AutoReindexScheduledTime);
        UpdateAutoReindexOptionsVisibility();
        
        // Recherche Web
        SearchEnginesList.ItemsSource = _settings.SearchEngines;
        
        // Commandes système
        LoadSystemCommands();
        
        // Guide - liste dynamique des commandes
        GuideSystemCommandsList.ItemsSource = _settings.SystemCommands.Where(c => c.IsEnabled).ToList();
        
        // À propos
        DataPathText.Text = AppSettings.GetSettingsPath();
    }
    
    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Tag?.ToString() == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }
    
    private void LoadScheduledTime(string time)
    {
        var parts = time.Split(':');
        if (parts.Length == 2)
        {
            SelectComboByTag(ReindexHourCombo, parts[0]);
            SelectComboByTag(ReindexMinuteCombo, parts[1]);
        }
        else
        {
            ReindexHourCombo.SelectedIndex = 3;
            ReindexMinuteCombo.SelectedIndex = 0;
        }
    }
    
    private void UpdateColorPreview(string color)
    {
        try
        {
            ColorPreview.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(color));
        }
        catch { }
    }
    
    private void UpdateAutoReindexOptionsVisibility()
    {
        var enabled = AutoReindexEnabledCheck.IsChecked == true;
        AutoReindexOptionsPanel.IsEnabled = enabled;
        AutoReindexOptionsPanel.Opacity = enabled ? 1.0 : 0.5;
    }
    
    private void LoadStatistics()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dbPath = Path.Combine(appData, "QuickLauncher", "index.db");
            
            var stats = new List<string>();
            
            if (File.Exists(dbPath))
            {
                var fileInfo = new FileInfo(dbPath);
                stats.Add($"📦 Taille de l'index: {fileInfo.Length / 1024.0:F1} Ko");
                stats.Add($"📅 Dernière indexation: {fileInfo.LastWriteTime:g}");
            }
            
            if (_indexingService != null)
                stats.Add($"🔢 Éléments indexés: {_indexingService.IndexedItemsCount}");
            
            stats.Add($"📂 Dossiers surveillés: {_settings.IndexedFolders.Count}");
            stats.Add($"📄 Extensions indexées: {_settings.FileExtensions.Count}");
            stats.Add($"🔍 Moteurs de recherche: {_settings.SearchEngines.Count}");
            stats.Add($"🕐 Historique: {_settings.SearchHistory.Count} entrées");
            
            StatsText.Text = string.Join("\n", stats);
        }
        catch
        {
            StatsText.Text = "Impossible de charger les statistiques.";
        }
    }

    // === Gestionnaires d'événements - Sliders ===
    
    private void MaxResultsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MaxResultsValue != null)
            MaxResultsValue.Text = ((int)e.NewValue).ToString();
    }
    
    private void MaxHistorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MaxHistoryValue != null)
            MaxHistoryValue.Text = ((int)e.NewValue).ToString();
    }
    
    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityValue != null)
            OpacityValue.Text = $"{(int)(e.NewValue * 100)}%";
    }
    
    private void SearchDepthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SearchDepthValue != null)
            SearchDepthValue.Text = ((int)e.NewValue).ToString();
    }

    // === Gestionnaires d'événements - Apparence ===
    
    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
    
    private void AccentColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccentColorCombo.SelectedItem is ComboBoxItem { Tag: string color })
            UpdateColorPreview(color);
    }

    // === Gestionnaires d'événements - Raccourci ===
    
    private void Hotkey_Changed(object sender, RoutedEventArgs e) => UpdateHotkeyDisplay();
    private void HotkeyKeyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateHotkeyDisplay();
    
    private void UpdateHotkeyDisplay()
    {
        if (CurrentHotkeyDisplay == null) return;
        
        var parts = new List<string>();
        if (HotkeyCtrlCheck?.IsChecked == true) parts.Add("Ctrl");
        if (HotkeyAltCheck?.IsChecked == true) parts.Add("Alt");
        if (HotkeyShiftCheck?.IsChecked == true) parts.Add("Shift");
        if (HotkeyWinCheck?.IsChecked == true) parts.Add("Win");
        
        var key = (HotkeyKeyCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Space";
        parts.Add(key);
        
        CurrentHotkeyDisplay.Text = string.Join(" + ", parts);
    }

    // === Gestionnaires d'événements - Dossiers ===
    
    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Sélectionner un dossier à indexer",
            UseDescriptionForTitle = true
        };
        
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            if (!_settings.IndexedFolders.Contains(dialog.SelectedPath))
            {
                _settings.IndexedFolders.Add(dialog.SelectedPath);
                RefreshFoldersList();
            }
            else
            {
                MessageBox.Show("Ce dossier est déjà dans la liste.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (IndexedFoldersList.SelectedItem is string folder)
        {
            if (_settings.IndexedFolders.Count > 1)
            {
                _settings.IndexedFolders.Remove(folder);
                RefreshFoldersList();
            }
            else
            {
                MessageBox.Show("Vous devez conserver au moins un dossier indexé.", "Attention",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
    
    private void RefreshFoldersList()
    {
        IndexedFoldersList.ItemsSource = null;
        IndexedFoldersList.ItemsSource = _settings.IndexedFolders;
    }

    // === Gestionnaires d'événements - Commandes Système ===
    
    private SystemControlCommand? _selectedSystemCommand;
    
    private void LoadSystemCommands()
    {
        SystemCommandsList.ItemsSource = null;
        SystemCommandsList.ItemsSource = _settings.SystemCommands;
    }
    
    private void SystemCommandsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedSystemCommand = SystemCommandsList.SelectedItem as SystemControlCommand;
        
        if (_selectedSystemCommand != null)
        {
            CommandPrefixBox.Text = _selectedSystemCommand.Prefix;
            CommandIconBox.Text = _selectedSystemCommand.Icon;
            CommandDescriptionBox.Text = _selectedSystemCommand.Description;
        }
    }
    
    private void EditSystemCommand_Click(object sender, RoutedEventArgs e)
    {
        if (SystemCommandsList.SelectedItem is not SystemControlCommand cmd)
        {
            MessageBox.Show("Sélectionnez une commande à modifier.", "Information",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        _selectedSystemCommand = cmd;
        CommandPrefixBox.Text = cmd.Prefix;
        CommandIconBox.Text = cmd.Icon;
        CommandDescriptionBox.Text = cmd.Description;
        CommandEditPanel.Visibility = Visibility.Visible;
    }
    
    private void CancelCommandEdit_Click(object sender, RoutedEventArgs e)
    {
        CommandEditPanel.Visibility = Visibility.Collapsed;
        _selectedSystemCommand = null;
    }
    
    private void ApplyCommandEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSystemCommand == null)
            return;
        
        var newPrefix = CommandPrefixBox.Text.Trim().ToLowerInvariant();
        var newIcon = CommandIconBox.Text.Trim();
        var newDescription = CommandDescriptionBox.Text.Trim();
        
        // Validation
        if (string.IsNullOrWhiteSpace(newPrefix))
        {
            MessageBox.Show("Le préfixe ne peut pas être vide.", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        // Vérifier les doublons
        var duplicate = _settings.SystemCommands.FirstOrDefault(c => 
            c != _selectedSystemCommand && 
            c.Prefix.Equals(newPrefix, StringComparison.OrdinalIgnoreCase));
        
        if (duplicate != null)
        {
            MessageBox.Show($"Le préfixe '{newPrefix}' est déjà utilisé par '{duplicate.Name}'.", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        // Appliquer les modifications
        _selectedSystemCommand.Prefix = newPrefix;
        _selectedSystemCommand.Icon = string.IsNullOrWhiteSpace(newIcon) ? "⚡" : newIcon;
        _selectedSystemCommand.Description = newDescription;
        
        // Rafraîchir la liste
        LoadSystemCommands();
        CommandEditPanel.Visibility = Visibility.Collapsed;
        
        MessageBox.Show("Commande modifiée! N'oubliez pas de sauvegarder.", "Succès",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
    
    private void ResetSystemCommands_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Réinitialiser toutes les commandes système aux valeurs par défaut?", 
            "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _settings.ResetSystemCommands();
            LoadSystemCommands();
            MessageBox.Show("Commandes réinitialisées! N'oubliez pas de sauvegarder.", "Succès",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // === Gestionnaires d'événements - Réindexation auto ===
    
    private void AutoReindexEnabled_Changed(object sender, RoutedEventArgs e) => UpdateAutoReindexOptionsVisibility();
    private void ReindexMode_Changed(object sender, RoutedEventArgs e) { }

    // === Gestionnaires d'événements - Actions ===
    
    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Voulez-vous effacer tout l'historique de recherche?", "Confirmation",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _settings.ClearSearchHistory();
            _settings.Save();
            LoadStatistics();
            MessageBox.Show("Historique effacé!", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
    
    private async void Reindex_Click(object sender, RoutedEventArgs e)
    {
        if (_indexingService == null)
        {
            MessageBox.Show("Service d'indexation non disponible.", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        
        if (MessageBox.Show("Réindexer tous les fichiers maintenant?", "Réindexation",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            try
            {
                await _indexingService.ReindexAsync();
                LoadStatistics();
                MessageBox.Show("Réindexation terminée!", "Succès",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur: {ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    
    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.GetDirectoryName(AppSettings.GetSettingsPath());
        if (folder != null && Directory.Exists(folder))
            Process.Start("explorer.exe", folder);
    }

    private void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("⚠️ Réinitialiser TOUS les paramètres?\n\nCette action est irréversible.",
            "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            AppSettings.Reset();
            MessageBox.Show("Paramètres réinitialisés!\nL'application va redémarrer.", "Succès",
                MessageBoxButton.OK, MessageBoxImage.Information);
            
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                Process.Start(exePath);
                Application.Current.Shutdown();
            }
        }
    }

    // === Boutons principaux ===
    
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // Général
        _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        _settings.MinimizeOnStartup = MinimizeOnStartupCheck.IsChecked == true;
        _settings.ShowInTaskbar = ShowInTaskbarCheck.IsChecked == true;
        _settings.CloseAfterLaunch = CloseAfterLaunchCheck.IsChecked == true;
        _settings.SingleClickLaunch = SingleClickLaunchCheck.IsChecked == true;
        _settings.ShowIndexingStatus = ShowIndexingStatusCheck.IsChecked == true;
        _settings.ShowSettingsButton = ShowSettingsButtonCheck.IsChecked == true;
        _settings.MaxResults = (int)MaxResultsSlider.Value;
        _settings.WindowPosition = GetComboTag(WindowPositionCombo) ?? "Center";
        
        // Historique
        _settings.EnableSearchHistory = EnableSearchHistoryCheck.IsChecked == true;
        _settings.MaxSearchHistory = (int)MaxHistorySlider.Value;
        
        // Apparence
        _settings.Theme = GetComboTag(ThemeCombo) ?? "Dark";
        _settings.WindowOpacity = OpacitySlider.Value;
        _settings.EnableAnimations = EnableAnimationsCheck.IsChecked == true;
        _settings.AccentColor = GetComboTag(AccentColorCombo) ?? "#0078D4";
        
        // Raccourci
        _settings.Hotkey.UseAlt = HotkeyAltCheck.IsChecked == true;
        _settings.Hotkey.UseCtrl = HotkeyCtrlCheck.IsChecked == true;
        _settings.Hotkey.UseShift = HotkeyShiftCheck.IsChecked == true;
        _settings.Hotkey.UseWin = HotkeyWinCheck.IsChecked == true;
        _settings.Hotkey.Key = GetComboTag(HotkeyKeyCombo) ?? "Space";
        
        // Indexation
        _settings.SearchDepth = (int)SearchDepthSlider.Value;
        _settings.IndexHiddenFolders = IndexHiddenFoldersCheck.IsChecked == true;
        _settings.IndexBrowserBookmarks = IndexBrowserBookmarksCheck.IsChecked == true;
        
        // Réindexation automatique
        _settings.AutoReindexEnabled = AutoReindexEnabledCheck.IsChecked == true;
        _settings.AutoReindexMode = ReindexTimeRadio.IsChecked == true 
            ? AutoReindexMode.ScheduledTime 
            : AutoReindexMode.Interval;
        _settings.AutoReindexIntervalMinutes = int.Parse(GetComboTag(ReindexIntervalCombo) ?? "60");
        
        var hour = GetComboTag(ReindexHourCombo) ?? "03";
        var minute = GetComboTag(ReindexMinuteCombo) ?? "00";
        _settings.AutoReindexScheduledTime = $"{hour}:{minute}";
        
        // Extensions
        var extensions = FileExtensionsBox.Text
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim().ToLowerInvariant())
            .Where(e => e.StartsWith('.'))
            .Distinct()
            .ToList();
        
        if (extensions.Count > 0)
            _settings.FileExtensions = extensions;
        
        _settings.Save();
        UpdateStartupRegistry();
        
        // Reconfigurer le timer
        if (Application.Current is App app)
            app.SetupAutoReindex();
        
        MessageBox.Show("✅ Paramètres sauvegardés!\n\nCertains paramètres (raccourci clavier, mode de clic) nécessitent un redémarrage.",
            "QuickLauncher", MessageBoxButton.OK, MessageBoxImage.Information);
        
        Close();
    }
    
    private static string? GetComboTag(ComboBox combo) => 
        (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateStartupRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
            if (key == null) return;

            if (_settings.StartWithWindows)
            {
                var exePath = GetApplicationExecutablePath();
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue(AppName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(AppName, false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Startup] ERREUR: {ex.Message}");
        }
    }

    private static string? GetApplicationExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath) && 
            processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            !processPath.Contains("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        var assemblyLocation = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrEmpty(assemblyLocation))
        {
            var directory = Path.GetDirectoryName(assemblyLocation);
            if (directory != null)
            {
                var exePath = Path.Combine(directory, $"{AppName}.exe");
                if (File.Exists(exePath))
                    return exePath;
            }
        }

        var mainModule = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrEmpty(mainModule) && 
            mainModule.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            !mainModule.Contains("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return mainModule;
        }

        return null;
    }

    public static void SyncStartupRegistry()
    {
        try
        {
            var settings = AppSettings.Load();
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
            if (key == null) return;

            var currentValue = key.GetValue(AppName) as string;
            var expectedPath = GetApplicationExecutablePath();

            if (settings.StartWithWindows)
            {
                if (!string.IsNullOrEmpty(expectedPath))
                {
                    var expectedValue = $"\"{expectedPath}\"";
                    if (currentValue != expectedValue)
                        key.SetValue(AppName, expectedValue);
                }
            }
            else if (currentValue != null)
            {
                key.DeleteValue(AppName, false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Startup] ERREUR sync: {ex.Message}");
        }
    }
}
