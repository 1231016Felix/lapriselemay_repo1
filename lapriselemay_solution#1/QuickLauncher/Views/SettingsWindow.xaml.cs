using Microsoft.Win32;
using QuickLauncher.Models;
using QuickLauncher.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

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
    
    // Valeurs initiales du raccourci pour détecter les changements
    private bool _initialHotkeyAlt;
    private bool _initialHotkeyCtrl;
    private bool _initialHotkeyShift;
    private bool _initialHotkeyWin;
    private string _initialHotkeyKey = "Space";
    
    // Flag pour éviter les sauvegardes pendant le chargement initial
    private bool _isLoading = true;
    
    // Timer pour le feedback de sauvegarde
    private readonly DispatcherTimer _saveIndicatorTimer;

    public SettingsWindow(IndexingService? indexingService = null)
    {
        InitializeComponent();
        _settings = AppSettings.Load();
        _indexingService = indexingService;
        
        // Initialiser le timer pour le feedback de sauvegarde
        _saveIndicatorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _saveIndicatorTimer.Tick += SaveIndicatorTimer_Tick;
        
        // Sauvegarder les valeurs initiales du raccourci
        _initialHotkeyAlt = _settings.Hotkey.UseAlt;
        _initialHotkeyCtrl = _settings.Hotkey.UseCtrl;
        _initialHotkeyShift = _settings.Hotkey.UseShift;
        _initialHotkeyWin = _settings.Hotkey.UseWin;
        _initialHotkeyKey = _settings.Hotkey.Key;
        
        LoadSettings();
        LoadStatistics();
        
        _isLoading = false;
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
        SingleClickLaunchCheck.IsChecked = _settings.SingleClickLaunch;
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
        
        // Recherche système
        SystemSearchDepthSlider.Value = _settings.SystemSearchDepth;
        SystemSearchDepthValue.Text = _settings.SystemSearchDepth.ToString();
        LoadSearchEngineInfo();
        
        // Charger la liste des navigateurs
        LoadBrowsersList();
        
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
        
        if (!_isLoading)
        {
            _settings.MaxResults = (int)e.NewValue;
            AutoSave();
        }
    }
    
    private void MaxHistorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MaxHistoryValue != null)
            MaxHistoryValue.Text = ((int)e.NewValue).ToString();
        
        if (!_isLoading)
        {
            _settings.MaxSearchHistory = (int)e.NewValue;
            AutoSave();
        }
    }
    
    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityValue != null)
            OpacityValue.Text = $"{(int)(e.NewValue * 100)}%";
        
        if (!_isLoading)
        {
            _settings.WindowOpacity = e.NewValue;
            AutoSave();
        }
    }
    
    private void SearchDepthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SearchDepthValue != null)
            SearchDepthValue.Text = ((int)e.NewValue).ToString();
        
        if (!_isLoading)
        {
            _settings.SearchDepth = (int)e.NewValue;
            AutoSave();
        }
    }
    
    private void SystemSearchDepthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SystemSearchDepthValue != null)
            SystemSearchDepthValue.Text = ((int)e.NewValue).ToString();
        
        if (!_isLoading)
        {
            _settings.SystemSearchDepth = (int)e.NewValue;
            AutoSave();
        }
    }
    
    private void LoadSearchEngineInfo()
    {
        var info = UniversalSearchService.GetEngineInfo();
        
        SearchEngineIcon.Text = info.Icon;
        SearchEngineName.Text = info.Name;
        SearchEngineDescription.Text = info.Description;
        
        // Style selon le statut
        if (info.IsOptimal)
        {
            SearchEngineIndicator.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x1E));
            SearchEngineStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10));
            SearchEngineStatusText.Text = "✓ Optimal";
            SearchEngineStatusText.Foreground = new SolidColorBrush(Colors.White);
        }
        else
        {
            SearchEngineIndicator.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x2A, 0x1E));
            SearchEngineStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00));
            SearchEngineStatusText.Text = "⚠ Non optimal";
            SearchEngineStatusText.Foreground = new SolidColorBrush(Colors.Black);
        }
        
        // Recommandation
        if (!string.IsNullOrEmpty(info.Recommendation))
        {
            SearchEngineRecommendation.Visibility = Visibility.Visible;
            SearchEngineRecommendationText.Text = info.Recommendation;
        }
        else
        {
            SearchEngineRecommendation.Visibility = Visibility.Collapsed;
        }
        
        // Stats du cache
        UpdateSearchCacheStats();
    }
    
    private void UpdateSearchCacheStats()
    {
        var (entryCount, totalResults) = UniversalSearchService.GetCacheStats();
        if (entryCount > 0)
            SearchCacheStats.Text = $"Cache: {entryCount} recherches, {totalResults} résultats";
        else
            SearchCacheStats.Text = "Cache vide";
    }
    
    private void ClearSearchCache_Click(object sender, RoutedEventArgs e)
    {
        UniversalSearchService.ClearCache();
        UpdateSearchCacheStats();
        MessageBox.Show("Cache de recherche vidé!", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // === Gestionnaires d'événements - Apparence ===
    
    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoading && ThemeCombo.SelectedItem is ComboBoxItem { Tag: string theme })
        {
            _settings.Theme = theme;
            AutoSave();
        }
    }
    
    private void AccentColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccentColorCombo.SelectedItem is ComboBoxItem { Tag: string color })
        {
            UpdateColorPreview(color);
            if (!_isLoading)
            {
                _settings.AccentColor = color;
                AutoSave();
            }
        }
    }

    // === Gestionnaires d'événements - Raccourci ===
    
    private void Hotkey_Changed(object sender, RoutedEventArgs e)
    {
        UpdateHotkeyDisplay();
        if (!_isLoading)
        {
            SaveHotkeySettings();
            CheckHotkeyChanged();
        }
    }
    
    private void HotkeyKeyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateHotkeyDisplay();
        if (!_isLoading)
        {
            SaveHotkeySettings();
            CheckHotkeyChanged();
        }
    }
    
    private void SaveHotkeySettings()
    {
        _settings.Hotkey.UseAlt = HotkeyAltCheck.IsChecked == true;
        _settings.Hotkey.UseCtrl = HotkeyCtrlCheck.IsChecked == true;
        _settings.Hotkey.UseShift = HotkeyShiftCheck.IsChecked == true;
        _settings.Hotkey.UseWin = HotkeyWinCheck.IsChecked == true;
        _settings.Hotkey.Key = GetComboTag(HotkeyKeyCombo) ?? "Space";
        AutoSave();
    }
    
    private void CheckHotkeyChanged()
    {
        bool hasChanged = 
            _settings.Hotkey.UseAlt != _initialHotkeyAlt ||
            _settings.Hotkey.UseCtrl != _initialHotkeyCtrl ||
            _settings.Hotkey.UseShift != _initialHotkeyShift ||
            _settings.Hotkey.UseWin != _initialHotkeyWin ||
            _settings.Hotkey.Key != _initialHotkeyKey;
        
        RestartWarningPanel.Visibility = hasChanged ? Visibility.Visible : Visibility.Collapsed;
    }
    
    private void RestartApp_Click(object sender, RoutedEventArgs e)
    {
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            Process.Start(exePath);
            Application.Current.Shutdown();
        }
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
                AutoSave();
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
                AutoSave();
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
    
    // === Sauvegarde automatique ===
    
    private void AutoSave()
    {
        if (_isLoading) return;
        
        _settings.Save();
        Debug.WriteLine("[Settings] Auto-sauvegardé");
        
        // Afficher le feedback visuel
        ShowSaveIndicator();
    }
    
    private void ShowSaveIndicator()
    {
        AutoSaveIndicator.Text = "✓ Sauvegardé";
        AutoSaveIndicator.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10));
        
        _saveIndicatorTimer.Stop();
        _saveIndicatorTimer.Start();
    }
    
    private void SaveIndicatorTimer_Tick(object? sender, EventArgs e)
    {
        _saveIndicatorTimer.Stop();
        AutoSaveIndicator.Text = "💾 Sauvegarde automatique";
        AutoSaveIndicator.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
    }
    
    // === Gestionnaires génériques pour CheckBox ===
    
    private void CheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        
        // Synchroniser toutes les valeurs de checkboxes
        _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        _settings.MinimizeOnStartup = MinimizeOnStartupCheck.IsChecked == true;
        _settings.ShowInTaskbar = ShowInTaskbarCheck.IsChecked == true;
        _settings.CloseAfterLaunch = CloseAfterLaunchCheck.IsChecked == true;
        _settings.ShowIndexingStatus = ShowIndexingStatusCheck.IsChecked == true;
        _settings.ShowSettingsButton = ShowSettingsButtonCheck.IsChecked == true;
        _settings.SingleClickLaunch = SingleClickLaunchCheck.IsChecked == true;
        _settings.EnableSearchHistory = EnableSearchHistoryCheck.IsChecked == true;
        _settings.EnableAnimations = EnableAnimationsCheck.IsChecked == true;
        _settings.IndexHiddenFolders = IndexHiddenFoldersCheck.IsChecked == true;
        
        UpdateStartupRegistry();
        AutoSave();
    }
    
    private void WindowPositionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoading && WindowPositionCombo.SelectedItem is ComboBoxItem { Tag: string pos })
        {
            _settings.WindowPosition = pos;
            AutoSave();
        }
    }

    // === Gestionnaires d'événements - Navigateurs ===
    
    private void LoadBrowsersList()
    {
        BrowsersList.ItemsSource = BookmarkService.GetSupportedBrowsers();
    }
    
    private async void ImportBrowserBookmarks_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string browserName }) return;
        
        try
        {
            var bookmarks = BookmarkService.GetBookmarksForBrowser(browserName);
            
            if (bookmarks.Count == 0)
            {
                MessageBox.Show($"Aucun favori trouvé pour {browserName}.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            if (_indexingService == null)
            {
                MessageBox.Show("Service d'indexation non disponible.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            
            // Réindexer pour inclure les nouveaux favoris
            await _indexingService.ReindexAsync();
            LoadBrowsersList(); // Rafraîchir les compteurs
            LoadStatistics();
            
            MessageBox.Show($"✅ {bookmarks.Count} favoris de {browserName} importés avec succès!", 
                "Import réussi", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de l'import: {ex.Message}", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private async void ImportAllBrowserBookmarks_Click(object sender, RoutedEventArgs e)
    {
        if (_indexingService == null)
        {
            MessageBox.Show("Service d'indexation non disponible.", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        
        try
        {
            var bookmarks = BookmarkService.GetAllBookmarks();
            
            if (bookmarks.Count == 0)
            {
                MessageBox.Show("Aucun favori trouvé dans les navigateurs installés.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            // S'assurer que l'option est activée
            _settings.IndexBrowserBookmarks = true;
            _settings.Save();
            
            // Réindexer
            await _indexingService.ReindexAsync();
            LoadBrowsersList(); // Rafraîchir les compteurs
            LoadStatistics();
            
            MessageBox.Show($"✅ {bookmarks.Count} favoris importés depuis tous les navigateurs!", 
                "Import réussi", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de l'import: {ex.Message}", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // === Gestionnaires d'événements - Commandes Système ===
    
    private SystemControlCommand? _selectedSystemCommand;
    
    private void LoadSystemCommands()
    {
        try
        {
            SystemCommandsList.ItemsSource = null;
            
            if (_settings.SystemCommands != null && _settings.SystemCommands.Count > 0)
            {
                // Trier par catégorie puis par nom (sans groupement WPF)
                var sortedCommands = _settings.SystemCommands
                    .OrderBy(c => c.Category)
                    .ThenBy(c => c.Name)
                    .ToList();
                SystemCommandsList.ItemsSource = sortedCommands;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Settings] ERREUR LoadSystemCommands: {ex.Message}");
        }
    }
    
    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != MainTabControl) return;
        
        try
        {
            var selectedIndex = MainTabControl.SelectedIndex;
            Debug.WriteLine($"[Settings] Onglet sélectionné: {selectedIndex}");
            
            // Recharger les données de l'onglet Commandes si sélectionné
            if (selectedIndex == 2 && !_isLoading)
            {
                LoadSystemCommands();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Settings] ERREUR changement onglet: {ex.Message}");
            MessageBox.Show($"Erreur lors du changement d'onglet: {ex.Message}", "Erreur", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void SystemCommandItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SystemControlCommand cmd })
        {
            _selectedSystemCommand = cmd;
            CommandPrefixBox.Text = cmd.Prefix;
            CommandIconBox.Text = cmd.Icon;
            CommandEditPanel.Visibility = Visibility.Visible;
        }
    }
    
    private void ToggleSwitch_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SystemControlCommand cmd })
        {
            cmd.IsEnabled = !cmd.IsEnabled;
            LoadSystemCommands();
            AutoSave();
            e.Handled = true; // Empêcher la propagation vers le parent
        }
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
        
        // Rafraîchir la liste et sauvegarder
        LoadSystemCommands();
        AutoSave();
        CommandEditPanel.Visibility = Visibility.Collapsed;
        _selectedSystemCommand = null;
    }
    
    private void ResetSystemCommands_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Réinitialiser toutes les commandes système aux valeurs par défaut?", 
            "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _settings.ResetSystemCommands();
            LoadSystemCommands();
            AutoSave();
            CommandEditPanel.Visibility = Visibility.Collapsed;
        }
    }

    // === Gestionnaires d'événements - Réindexation auto ===
    
    private void AutoReindexEnabled_Changed(object sender, RoutedEventArgs e)
    {
        UpdateAutoReindexOptionsVisibility();
        if (!_isLoading)
        {
            _settings.AutoReindexEnabled = AutoReindexEnabledCheck.IsChecked == true;
            AutoSave();
            
            // Reconfigurer le timer
            if (Application.Current is App app)
                app.SetupAutoReindex();
        }
    }
    
    private void ReindexMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoading)
        {
            _settings.AutoReindexMode = ReindexTimeRadio.IsChecked == true 
                ? AutoReindexMode.ScheduledTime 
                : AutoReindexMode.Interval;
            AutoSave();
            
            // Reconfigurer le timer
            if (Application.Current is App app)
                app.SetupAutoReindex();
        }
    }
    
    private void ReindexIntervalCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoading && ReindexIntervalCombo.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            _settings.AutoReindexIntervalMinutes = int.Parse(tag);
            AutoSave();
            
            if (Application.Current is App app)
                app.SetupAutoReindex();
        }
    }
    
    private void ReindexTimeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoading)
        {
            var hour = GetComboTag(ReindexHourCombo) ?? "03";
            var minute = GetComboTag(ReindexMinuteCombo) ?? "00";
            _settings.AutoReindexScheduledTime = $"{hour}:{minute}";
            AutoSave();
            
            if (Application.Current is App app)
                app.SetupAutoReindex();
        }
    }

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

    // === Extensions (sauvegarde sur perte de focus) ===
    
    private void FileExtensionsBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        
        var extensions = FileExtensionsBox.Text
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(ext => ext.Trim().ToLowerInvariant())
            .Where(ext => ext.StartsWith('.'))
            .Distinct()
            .ToList();
        
        if (extensions.Count > 0)
        {
            _settings.FileExtensions = extensions;
            AutoSave();
        }
    }
    
    private static string? GetComboTag(ComboBox combo) => 
        (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

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
