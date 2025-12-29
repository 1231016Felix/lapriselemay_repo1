using Microsoft.Win32;
using QuickLauncher.Models;
using QuickLauncher.Services;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuickLauncher.Views;

public partial class SettingsWindow : System.Windows.Window
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
        ShowIndexingStatusCheck.IsChecked = _settings.ShowIndexingStatus;
        ShowSettingsButtonCheck.IsChecked = _settings.ShowSettingsButton;
        MaxResultsSlider.Value = _settings.MaxResults;
        MaxResultsValue.Text = _settings.MaxResults.ToString();
        
        // Position fenêtre
        SelectWindowPosition(_settings.WindowPosition);
        
        // Historique
        EnableSearchHistoryCheck.IsChecked = _settings.EnableSearchHistory;
        MaxHistorySlider.Value = _settings.MaxSearchHistory;
        MaxHistoryValue.Text = _settings.MaxSearchHistory.ToString();
        
        // Apparence
        SelectTheme(_settings.Theme);
        OpacitySlider.Value = _settings.WindowOpacity;
        OpacityValue.Text = $"{(int)(_settings.WindowOpacity * 100)}%";
        EnableAnimationsCheck.IsChecked = _settings.EnableAnimations;
        SelectAccentColor(_settings.AccentColor);
        
        // Raccourci
        HotkeyAltCheck.IsChecked = _settings.Hotkey.UseAlt;
        HotkeyCtrlCheck.IsChecked = _settings.Hotkey.UseCtrl;
        HotkeyShiftCheck.IsChecked = _settings.Hotkey.UseShift;
        HotkeyWinCheck.IsChecked = _settings.Hotkey.UseWin;
        SelectHotkeyKey(_settings.Hotkey.Key);
        UpdateHotkeyDisplay();
        
        // Indexation
        IndexedFoldersList.ItemsSource = _settings.IndexedFolders;
        FileExtensionsBox.Text = string.Join(", ", _settings.FileExtensions);
        SearchDepthSlider.Value = _settings.SearchDepth;
        SearchDepthValue.Text = _settings.SearchDepth.ToString();
        IndexHiddenFoldersCheck.IsChecked = _settings.IndexHiddenFolders;
        
        // Recherche Web
        SearchEnginesList.ItemsSource = _settings.SearchEngines;
        
        // À propos
        DataPathText.Text = AppSettings.GetSettingsPath();
    }
    
    private void SelectWindowPosition(string position)
    {
        foreach (ComboBoxItem item in WindowPositionCombo.Items)
        {
            if (item.Tag?.ToString() == position)
            {
                WindowPositionCombo.SelectedItem = item;
                return;
            }
        }
        WindowPositionCombo.SelectedIndex = 0;
    }
    
    private void SelectTheme(string theme)
    {
        foreach (ComboBoxItem item in ThemeCombo.Items)
        {
            if (item.Tag?.ToString() == theme)
            {
                ThemeCombo.SelectedItem = item;
                return;
            }
        }
        ThemeCombo.SelectedIndex = 0;
    }

    private void SelectAccentColor(string color)
    {
        foreach (ComboBoxItem item in AccentColorCombo.Items)
        {
            if (item.Tag?.ToString() == color)
            {
                AccentColorCombo.SelectedItem = item;
                break;
            }
        }
        if (AccentColorCombo.SelectedItem == null)
            AccentColorCombo.SelectedIndex = 0;
        
        try
        {
            ColorPreview.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
        }
        catch { }
    }
    
    private void SelectHotkeyKey(string key)
    {
        foreach (ComboBoxItem item in HotkeyKeyCombo.Items)
        {
            if (item.Tag?.ToString() == key)
            {
                HotkeyKeyCombo.SelectedItem = item;
                return;
            }
        }
        HotkeyKeyCombo.SelectedIndex = 0;
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
            
            stats.Add($"📂 Dossiers surveillés: {_settings.IndexedFolders.Count}");
            stats.Add($"📄 Extensions indexées: {_settings.FileExtensions.Count}");
            stats.Add($"🔍 Moteurs de recherche: {_settings.SearchEngines.Count}");
            stats.Add($"🕐 Historique de recherche: {_settings.SearchHistory.Count} entrées");
            
            StatsText.Text = string.Join("\n", stats);
        }
        catch
        {
            StatsText.Text = "Impossible de charger les statistiques.";
        }
    }

    // ═══════════════ Gestionnaires d'événements - Sliders ═══════════════
    
    private void MaxResultsSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (MaxResultsValue != null)
            MaxResultsValue.Text = ((int)e.NewValue).ToString();
    }
    
    private void MaxHistorySlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (MaxHistoryValue != null)
            MaxHistoryValue.Text = ((int)e.NewValue).ToString();
    }
    
    private void OpacitySlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityValue != null)
            OpacityValue.Text = $"{(int)(e.NewValue * 100)}%";
    }
    
    private void SearchDepthSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (SearchDepthValue != null)
            SearchDepthValue.Text = ((int)e.NewValue).ToString();
    }

    // ═══════════════ Gestionnaires d'événements - Apparence ═══════════════
    
    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Future implementation pour le thème clair
    }
    
    private void AccentColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccentColorCombo.SelectedItem is ComboBoxItem item && item.Tag is string color)
        {
            try
            {
                ColorPreview.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
            }
            catch { }
        }
    }

    // ═══════════════ Gestionnaires d'événements - Raccourci ═══════════════
    
    private void Hotkey_Changed(object sender, System.Windows.RoutedEventArgs e)
    {
        UpdateHotkeyDisplay();
    }
    
    private void HotkeyKeyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateHotkeyDisplay();
    }
    
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

    // ═══════════════ Gestionnaires d'événements - Dossiers ═══════════════
    
    private void AddFolder_Click(object sender, System.Windows.RoutedEventArgs e)
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
                System.Windows.MessageBox.Show("Ce dossier est déjà dans la liste.", "Information",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
        }
    }

    private void RemoveFolder_Click(object sender, System.Windows.RoutedEventArgs e)
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
                System.Windows.MessageBox.Show("Vous devez conserver au moins un dossier indexé.", "Attention",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }
    }
    
    private void RefreshFoldersList()
    {
        IndexedFoldersList.ItemsSource = null;
        IndexedFoldersList.ItemsSource = _settings.IndexedFolders;
    }

    // ═══════════════ Gestionnaires d'événements - Actions ═══════════════
    
    private void ClearHistory_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            "Voulez-vous effacer tout l'historique de recherche?",
            "Confirmation",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
            
        if (result == System.Windows.MessageBoxResult.Yes)
        {
            _settings.ClearSearchHistory();
            _settings.Save();
            LoadStatistics();
            System.Windows.MessageBox.Show("Historique effacé!", "Succès",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
    }
    
    private async void Reindex_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_indexingService == null)
        {
            System.Windows.MessageBox.Show("Service d'indexation non disponible. Veuillez redémarrer l'application.",
                "Erreur", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }
        
        var result = System.Windows.MessageBox.Show(
            "Voulez-vous réindexer tous les fichiers maintenant?\nCela peut prendre quelques instants.",
            "Réindexation",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
            
        if (result == System.Windows.MessageBoxResult.Yes)
        {
            try
            {
                await _indexingService.ReindexAsync();
                LoadStatistics();
                System.Windows.MessageBox.Show("Réindexation terminée!", "Succès",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Erreur lors de la réindexation:\n{ex.Message}", "Erreur",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }
    
    private void OpenDataFolder_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var folder = Path.GetDirectoryName(AppSettings.GetSettingsPath());
        if (folder != null && Directory.Exists(folder))
        {
            Process.Start("explorer.exe", folder);
        }
    }

    private void ResetSettings_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            "⚠️ Êtes-vous sûr de vouloir réinitialiser TOUS les paramètres?\n\nCette action est irréversible.",
            "Confirmation",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
            
        if (result == System.Windows.MessageBoxResult.Yes)
        {
            AppSettings.Reset();
            System.Windows.MessageBox.Show(
                "Paramètres réinitialisés!\nL'application va redémarrer.",
                "Succès",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            
            // Redémarrer l'application
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath))
            {
                Process.Start(exePath);
                System.Windows.Application.Current.Shutdown();
            }
        }
    }

    // ═══════════════ Boutons principaux ═══════════════
    
    private void SaveButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // Général
        _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        _settings.MinimizeOnStartup = MinimizeOnStartupCheck.IsChecked == true;
        _settings.ShowInTaskbar = ShowInTaskbarCheck.IsChecked == true;
        _settings.CloseAfterLaunch = CloseAfterLaunchCheck.IsChecked == true;
        _settings.ShowIndexingStatus = ShowIndexingStatusCheck.IsChecked == true;
        _settings.ShowSettingsButton = ShowSettingsButtonCheck.IsChecked == true;
        _settings.MaxResults = (int)MaxResultsSlider.Value;
        
        // Position fenêtre
        if (WindowPositionCombo.SelectedItem is ComboBoxItem posItem)
            _settings.WindowPosition = posItem.Tag?.ToString() ?? "Center";
        
        // Historique
        _settings.EnableSearchHistory = EnableSearchHistoryCheck.IsChecked == true;
        _settings.MaxSearchHistory = (int)MaxHistorySlider.Value;
        
        // Apparence
        if (ThemeCombo.SelectedItem is ComboBoxItem themeItem)
            _settings.Theme = themeItem.Tag?.ToString() ?? "Dark";
        _settings.WindowOpacity = OpacitySlider.Value;
        _settings.EnableAnimations = EnableAnimationsCheck.IsChecked == true;
        if (AccentColorCombo.SelectedItem is ComboBoxItem colorItem)
            _settings.AccentColor = colorItem.Tag?.ToString() ?? "#0078D4";
        
        // Raccourci
        _settings.Hotkey.UseAlt = HotkeyAltCheck.IsChecked == true;
        _settings.Hotkey.UseCtrl = HotkeyCtrlCheck.IsChecked == true;
        _settings.Hotkey.UseShift = HotkeyShiftCheck.IsChecked == true;
        _settings.Hotkey.UseWin = HotkeyWinCheck.IsChecked == true;
        if (HotkeyKeyCombo.SelectedItem is ComboBoxItem keyItem)
            _settings.Hotkey.Key = keyItem.Tag?.ToString() ?? "Space";
        
        // Indexation
        _settings.SearchDepth = (int)SearchDepthSlider.Value;
        _settings.IndexHiddenFolders = IndexHiddenFoldersCheck.IsChecked == true;
        
        // Extensions
        var extensions = FileExtensionsBox.Text
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim().ToLowerInvariant())
            .Where(e => e.StartsWith('.'))
            .Distinct()
            .ToList();
        if (extensions.Count > 0)
            _settings.FileExtensions = extensions;
        
        _settings.Save();
        UpdateStartupRegistry();
        
        System.Windows.MessageBox.Show(
            "✅ Paramètres sauvegardés!\n\nCertains changements (comme le raccourci clavier) nécessitent un redémarrage.",
            "QuickLauncher",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
        Close();
    }

    private void CancelButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Close();
    }

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
                {
                    key.SetValue(AppName, $"\"{exePath}\"");
                    Debug.WriteLine($"[Startup] Entrée registre créée: {exePath}");
                }
                else
                {
                    Debug.WriteLine("[Startup] ERREUR: Impossible de déterminer le chemin de l'exécutable");
                }
            }
            else
            {
                key.DeleteValue(AppName, false);
                Debug.WriteLine("[Startup] Entrée registre supprimée");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Startup] ERREUR registre: {ex.Message}");
        }
    }

    /// <summary>
    /// Obtient le chemin correct de l'exécutable de l'application.
    /// Fonctionne pour les applications .NET publiées (single-file, framework-dependent, self-contained).
    /// </summary>
    private static string? GetApplicationExecutablePath()
    {
        // Méthode 1: Environment.ProcessPath (recommandé pour .NET 6+)
        // Retourne le chemin du processus actuel, même pour les apps single-file
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath) && 
            processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            !processPath.Contains("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        // Méthode 2: Chercher l'exe dans le répertoire de l'assembly
        var assemblyLocation = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrEmpty(assemblyLocation))
        {
            var directory = Path.GetDirectoryName(assemblyLocation);
            if (directory != null)
            {
                // Chercher QuickLauncher.exe dans le même répertoire
                var exePath = Path.Combine(directory, $"{AppName}.exe");
                if (File.Exists(exePath))
                {
                    return exePath;
                }
            }
        }

        // Méthode 3: Fallback via Process.MainModule
        var mainModule = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrEmpty(mainModule) && 
            mainModule.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            !mainModule.Contains("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return mainModule;
        }

        return null;
    }

    /// <summary>
    /// Synchronise l'entrée de registre avec les paramètres actuels.
    /// Appelé au démarrage de l'application pour s'assurer que le chemin est à jour.
    /// </summary>
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
                    
                    // Mettre à jour si différent ou manquant
                    if (currentValue != expectedValue)
                    {
                        key.SetValue(AppName, expectedValue);
                        Debug.WriteLine($"[Startup] Registre synchronisé: {expectedPath}");
                    }
                }
            }
            else if (currentValue != null)
            {
                // L'option est désactivée mais l'entrée existe, la supprimer
                key.DeleteValue(AppName, false);
                Debug.WriteLine("[Startup] Entrée registre orpheline supprimée");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Startup] ERREUR sync registre: {ex.Message}");
        }
    }
}
