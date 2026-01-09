using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using CleanUninstaller.ViewModels;
using CleanUninstaller.Models;
using WinRT.Interop;
using Microsoft.UI.Windowing;
using System.Diagnostics;
using Windows.Storage.Pickers;
using System.Text;

namespace CleanUninstaller.Views;

/// <summary>
/// Fenêtre principale de l'application Clean Uninstaller
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private CancellationTokenSource? _currentOperationCts;

    public MainWindow()
    {
        InitializeComponent();
        
        ViewModel = new MainViewModel();
        
        ConfigureWindow();
        ConfigureTitleBar();
        
        Activated += MainWindow_Activated;
    }

    private void ConfigureWindow()
    {
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        appWindow.Resize(new Windows.Graphics.SizeInt32(1450, 950));

        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
        if (displayArea != null)
        {
            var centerX = (displayArea.WorkArea.Width - 1450) / 2;
            var centerY = (displayArea.WorkArea.Height - 950) / 2;
            appWindow.Move(new Windows.Graphics.PointInt32(centerX, centerY));
        }

        appWindow.Title = "Clean Uninstaller";
    }

    private void ConfigureTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }

    private bool _initialized;
    private bool _hasShownApproximateSizesDialog;
    private const string TeachingTipShownKey = "WelcomeTeachingTipShown";

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (!_initialized && args.WindowActivationState != WindowActivationState.Deactivated)
        {
            _initialized = true;
            
            // Initialiser le XamlRoot pour le ViewModel (pour les dialogues)
            ViewModel.XamlRoot = Content.XamlRoot;
            
            await LoadProgramsAsync();
            
            // Afficher le TeachingTip pour les nouveaux utilisateurs
            ShowWelcomeTeachingTipIfNeeded();
        }
    }

    private void ShowWelcomeTeachingTipIfNeeded()
    {
        try
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            if (localSettings.Values.TryGetValue(TeachingTipShownKey, out var shown) && (bool)shown)
            {
                return; // Déjà affiché
            }
            
            // Afficher le TeachingTip après un court délai pour laisser le temps au UI de se charger
            DispatcherQueue.TryEnqueue(async () =>
            {
                await Task.Delay(1500);
                WelcomeTeachingTip.IsOpen = true;
            });
        }
        catch
        {
            // Ignorer les erreurs de settings
        }
    }

    private void WelcomeTeachingTip_ActionButtonClick(TeachingTip sender, object args)
    {
        try
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values[TeachingTipShownKey] = true;
            WelcomeTeachingTip.IsOpen = false;
        }
        catch
        {
            // Ignorer les erreurs de settings
        }
    }

    private async Task LoadProgramsAsync()
    {
        _currentOperationCts?.Cancel();
        _currentOperationCts = new CancellationTokenSource();
        
        ShowLoading(true, "Scan des programmes installés...");
        
        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                LoadingProgressBar.Value = p.Percentage;
                LoadingStatusText.Text = p.StatusMessage;
            });

            await ViewModel.InitializeAsync();
            
            ProgramListView.ItemsSource = ViewModel.Programs;
            UpdateProgramCount();
            
            // Calculer les tailles manquantes pendant le chargement
            var (approximateCount, stillUnknownCount) = await CalculateMissingSizesAsync();
            
            UpdateTotalSize();
            StatusText.Text = $"{ViewModel.TotalProgramCount} programmes trouvés";
            
            ShowInfoBar("Scan terminé", $"{ViewModel.TotalProgramCount} programmes détectés sur votre système.", InfoBarSeverity.Success);
            
            // Afficher un dialogue si des tailles sont approximatives ou inconnues (seulement au premier chargement)
            if ((approximateCount > 0 || stillUnknownCount > 0) && !_hasShownApproximateSizesDialog)
            {
                _hasShownApproximateSizesDialog = true;
                await ShowApproximateSizesDialogAsync(approximateCount, stillUnknownCount);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Scan annulé";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Erreur: {ex.Message}";
            ShowInfoBar("Erreur", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            ShowLoading(false);
        }
    }

    private async Task<(int approximateCount, int stillUnknownCount)> CalculateMissingSizesAsync()
    {
        try
        {
            var registryService = new Services.RegistryService();
            var programsList = ViewModel.Programs.ToList();
            
            var progress = new Progress<ScanProgress>(p =>
            {
                LoadingStatusText.Text = p.StatusMessage;
            });

            var approximateCount = await registryService.CalculateMissingSizesAsync(programsList, progress, CancellationToken.None);
            
            // Compter les programmes dont la taille est toujours inconnue
            var stillUnknownCount = programsList.Count(p => p.EstimatedSize == 0);
            
            return (approximateCount, stillUnknownCount);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur calcul tailles: {ex.Message}");
            return (0, 0);
        }
    }

    private async Task ShowApproximateSizesDialogAsync(int approximateCount, int stillUnknownCount)
    {
        var message = new System.Text.StringBuilder();
        
        if (approximateCount > 0)
        {
            message.AppendLine($"{approximateCount} programme(s) n'avaient pas de taille enregistrée.");
            message.AppendLine();
            message.AppendLine("Leur taille a été calculée à partir de leur dossier d'installation ");
            message.AppendLine("et est marquée avec un \"~\" pour indiquer qu'elle est approximative.");
        }
        
        if (stillUnknownCount > 0)
        {
            if (approximateCount > 0) message.AppendLine();
            message.AppendLine($"{stillUnknownCount} programme(s) ont une taille inconnue.");
            message.AppendLine();
            message.AppendLine("Le dossier d'installation n'a pas pu être localisé ");
            message.AppendLine("(applications Windows Store, programmes partiellement supprimés, etc.).");
        }

        var dialog = new ContentDialog
        {
            Title = "Information sur les tailles",
            Content = message.ToString().Trim(),
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };

        await dialog.ShowAsync();
    }

    private void ShowLoading(bool show, string message = "")
    {
        LoadingOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        LoadingStatusText.Text = message;
        LoadingProgressBar.Value = 0;
        StatusProgressBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowInfoBar(string title, string message, InfoBarSeverity severity, int autoCloseMs = 5000)
    {
        MainInfoBar.Title = title;
        MainInfoBar.Message = message;
        MainInfoBar.Severity = severity;
        MainInfoBar.IsOpen = true;

        if (autoCloseMs > 0)
        {
            _ = Task.Delay(autoCloseMs).ContinueWith(_ =>
            {
                DispatcherQueue.TryEnqueue(() => MainInfoBar.IsOpen = false);
            });
        }
    }

    private void UpdateProgramCount()
    {
        ProgramCountText.Text = $"{ViewModel.FilteredProgramCount} programmes";
        
        var selectedCount = ViewModel.SelectedCount;
        if (selectedCount > 0)
        {
            SelectionInfoText.Text = $"{selectedCount} sélectionné(s) • {ViewModel.SelectedTotalSize}";
            SelectionInfoText.Visibility = Visibility.Visible;
        }
        else
        {
            SelectionInfoText.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateTotalSize()
    {
        var programsWithSize = ViewModel.Programs.Where(p => p.EstimatedSize > 0).ToList();
        var totalSize = programsWithSize.Sum(p => p.EstimatedSize);
        var hasApproximate = ViewModel.Programs.Any(p => p.IsSizeApproximate);
        var unknownCount = ViewModel.Programs.Count() - programsWithSize.Count;
        
        var prefix = hasApproximate ? "~" : "";
        
        if (unknownCount > 0)
        {
            TotalSizeText.Text = $"Total: {prefix}{FormatSize(totalSize)} ({unknownCount} inconnu{(unknownCount > 1 ? "s" : "")})";
            ToolTipService.SetToolTip(TotalSizeText, $"{unknownCount} programme(s) sans taille connue\n{(hasApproximate ? "~ indique une taille approximative" : "")}".Trim());
        }
        else
        {
            TotalSizeText.Text = $"Total: {prefix}{FormatSize(totalSize)}";
            ToolTipService.SetToolTip(TotalSizeText, hasApproximate ? "~ indique une taille approximative" : null);
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 o";
        string[] suffixes = ["o", "Ko", "Mo", "Go", "To"];
        var i = 0;
        var size = (double)bytes;
        while (size >= 1024 && i < suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }
        return $"{size:N1} {suffixes[i]}";
    }

    private void UpdateDetailsPanel()
    {
        var program = ViewModel.SelectedProgram;
        
        if (program != null)
        {
            DetailsPanel.Visibility = Visibility.Visible;
            PlaceholderPanel.Visibility = Visibility.Collapsed;
            
            DetailNameText.Text = program.DisplayName;
            DetailPublisherText.Text = program.Publisher;
            DetailVersionText.Text = program.Version;
            DetailDateText.Text = program.FormattedInstallDate;
            DetailSizeText.Text = program.FormattedSize;
            DetailTypeText.Text = GetInstallerTypeName(program.InstallerType);
            DetailLocationText.Text = string.IsNullOrEmpty(program.InstallLocation) 
                ? "Non spécifié" 
                : program.InstallLocation;

            OpenLocationButton.Visibility = !string.IsNullOrEmpty(program.InstallLocation) && 
                                            Directory.Exists(program.InstallLocation)
                ? Visibility.Visible 
                : Visibility.Collapsed;

            ModifyButton.Visibility = !string.IsNullOrEmpty(program.ModifyPath) 
                ? Visibility.Visible 
                : Visibility.Collapsed;

            // Icône
            if (program.Icon != null)
            {
                DetailIcon.Source = program.Icon;
                DetailIconPlaceholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                DetailIcon.Source = null;
                DetailIconPlaceholder.Visibility = Visibility.Visible;
            }
        }
        else
        {
            DetailsPanel.Visibility = Visibility.Collapsed;
            PlaceholderPanel.Visibility = Visibility.Visible;
        }
    }

    private static string GetInstallerTypeName(InstallerType type) => type switch
    {
        InstallerType.Msi => "Windows Installer (MSI)",
        InstallerType.InnoSetup => "Inno Setup",
        InstallerType.Nsis => "NSIS (Nullsoft)",
        InstallerType.InstallShield => "InstallShield",
        InstallerType.Wix => "WiX Toolset",
        InstallerType.Msix => "MSIX / Windows Store",
        InstallerType.ClickOnce => "ClickOnce",
        InstallerType.Portable => "Portable",
        _ => "Standard"
    };

    private void ShowResiduals(bool show)
    {
        ResidualsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        CleanupActionsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        
        if (show)
        {
            ResidualsListView.ItemsSource = ViewModel.Residuals;
            UpdateResidualsInfo();
        }
    }

    private void UpdateResidualsInfo()
    {
        var selected = ViewModel.Residuals.Count(r => r.IsSelected);
        var total = ViewModel.Residuals.Count;
        ResidualsCountText.Text = $"{selected}/{total} sélectionné(s)";
        ResidualsSizeText.Text = ViewModel.ResidualsTotalSize;
    }

    #region Event Handlers - Toolbar

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadProgramsAsync();
    }

    private async void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        await UninstallSelectedProgramAsync(silent: true);
    }

    private async void UninstallStandard_Click(object sender, RoutedEventArgs e)
    {
        await UninstallSelectedProgramAsync(silent: false);
    }

    private async void UninstallSilent_Click(object sender, RoutedEventArgs e)
    {
        await UninstallSelectedProgramAsync(silent: true);
    }

    private async void UninstallForced_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedProgram == null) return;

        var dialog = new ContentDialog
        {
            Title = "Désinstallation forcée",
            Content = $"La désinstallation forcée va supprimer tous les fichiers et entrées de registre liés à \"{ViewModel.SelectedProgram.DisplayName}\".\n\nCette opération est irréversible. Continuer ?",
            PrimaryButtonText = "Forcer la désinstallation",
            SecondaryButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Secondary,
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await UninstallSelectedProgramAsync(silent: true, force: true);
        }
    }

    private async void UninstallBatch_Click(object sender, RoutedEventArgs e)
    {
        var selectedCount = ViewModel.SelectedCount;
        if (selectedCount == 0)
        {
            ShowInfoBar("Aucune sélection", "Sélectionnez les programmes à désinstaller.", InfoBarSeverity.Warning);
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Désinstallation en lot",
            Content = $"Vous allez désinstaller {selectedCount} programme(s).\n\nUn point de restauration sera créé avant l'opération.",
            PrimaryButtonText = "Désinstaller",
            SecondaryButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Secondary,
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await UninstallBatchAsync();
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        var hWnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hWnd);
        
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = $"programmes_installes_{DateTime.Now:yyyyMMdd}";
        picker.FileTypeChoices.Add("CSV", [".csv"]);
        picker.FileTypeChoices.Add("JSON", [".json"]);
        picker.FileTypeChoices.Add("Texte", [".txt"]);

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
            try
            {
                var content = file.FileType.ToLowerInvariant() switch
                {
                    ".csv" => ExportToCsv(),
                    ".json" => ExportToJson(),
                    _ => ExportToText()
                };

                await File.WriteAllTextAsync(file.Path, content, Encoding.UTF8);
                ShowInfoBar("Export réussi", $"Liste exportée vers {file.Name}", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowInfoBar("Erreur d'export", ex.Message, InfoBarSeverity.Error);
            }
        }
    }

    private string ExportToCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Nom;Éditeur;Version;Taille;Date d'installation;Emplacement;Type");
        
        foreach (var p in ViewModel.Programs)
        {
            sb.AppendLine($"\"{p.DisplayName}\";\"{p.Publisher}\";\"{p.Version}\";\"{p.FormattedSize}\";\"{p.FormattedInstallDate}\";\"{p.InstallLocation}\";\"{p.InstallerType}\"");
        }
        
        return sb.ToString();
    }

    private string ExportToJson()
    {
        var programs = ViewModel.Programs.Select(p => new
        {
            p.DisplayName,
            p.Publisher,
            p.Version,
            p.EstimatedSize,
            p.InstallDate,
            p.InstallLocation,
            InstallerType = p.InstallerType.ToString(),
            p.IsWindowsApp,
            p.IsSystemComponent
        });

        return System.Text.Json.JsonSerializer.Serialize(programs, new System.Text.Json.JsonSerializerOptions 
        { 
            WriteIndented = true 
        });
    }

    private string ExportToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Liste des programmes installés - {DateTime.Now:dd/MM/yyyy HH:mm}");
        sb.AppendLine(new string('=', 80));
        sb.AppendLine();

        foreach (var p in ViewModel.Programs.OrderBy(p => p.DisplayName))
        {
            sb.AppendLine($"• {p.DisplayName}");
            if (!string.IsNullOrEmpty(p.Publisher)) sb.AppendLine($"  Éditeur: {p.Publisher}");
            if (!string.IsNullOrEmpty(p.Version)) sb.AppendLine($"  Version: {p.Version}");
            sb.AppendLine($"  Taille: {p.FormattedSize}");
            sb.AppendLine();
        }

        sb.AppendLine(new string('=', 80));
        sb.AppendLine($"Total: {ViewModel.Programs.Count} programmes");

        return sb.ToString();
    }

    private void MonitorButton_Click(object sender, RoutedEventArgs e)
    {
        var monitorWindow = new MonitorWindow();
        monitorWindow.Activate();
    }

    private void StartupButton_Click(object sender, RoutedEventArgs e)
    {
        var startupWindow = new StartupManagerWindow();
        startupWindow.Activate();
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog { XamlRoot = Content.XamlRoot };
        await dialog.ShowAsync();
    }

    #endregion

    #region Event Handlers - Search & Filters

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.SearchText = sender.Text;
            ProgramListView.ItemsSource = ViewModel.Programs;
            UpdateProgramCount();
        }
    }

    private void FilterChanged_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowSystemApps = ShowSystemAppsToggle.IsChecked;
        ViewModel.ShowWindowsApps = ShowWindowsAppsToggle.IsChecked;
        ProgramListView.ItemsSource = ViewModel.Programs;
        UpdateProgramCount();
    }

    private void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        ShowSystemAppsToggle.IsChecked = false;
        ShowWindowsAppsToggle.IsChecked = true;
        SizeFilterAllItem.IsChecked = true;
        ViewModel.SearchText = "";
        ViewModel.ShowSystemApps = false;
        ViewModel.ShowWindowsApps = true;
        ViewModel.SizeFilter = Models.SizeFilter.All;
        ProgramListView.ItemsSource = ViewModel.Programs;
        UpdateProgramCount();
    }

    private void SizeFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioMenuFlyoutItem item && item.Tag is string tag)
        {
            ViewModel.SizeFilter = tag switch
            {
                "Small" => Models.SizeFilter.Small,
                "Medium" => Models.SizeFilter.Medium,
                "Large" => Models.SizeFilter.Large,
                "VeryLarge" => Models.SizeFilter.VeryLarge,
                "Unknown" => Models.SizeFilter.Unknown,
                _ => Models.SizeFilter.All
            };
            ProgramListView.ItemsSource = ViewModel.Programs;
            UpdateProgramCount();
        }
    }

    private void SelectAllCheckbox_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleSelectAllCommand.Execute(null);
        UpdateProgramCount();
    }

    #endregion

    #region Event Handlers - Sort

    private void SortByName_Click(object sender, RoutedEventArgs e)
    {
        SetSortBy(SortOption.Name);
    }

    private void SortByPublisher_Click(object sender, RoutedEventArgs e)
    {
        SetSortBy(SortOption.Publisher);
    }

    private void SortBySize_Click(object sender, RoutedEventArgs e)
    {
        SetSortBy(SortOption.Size);
    }

    private void SortByDate_Click(object sender, RoutedEventArgs e)
    {
        SetSortBy(SortOption.InstallDate);
    }

    private void SortOrder_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SortDescending = SortDescendingItem.IsChecked;
        ProgramListView.ItemsSource = ViewModel.Programs;
        UpdateSortIcons();
    }

    /// <summary>
    /// Clic sur l'en-tête de colonne "Nom"
    /// </summary>
    private void SortByNameHeader_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SortBy == SortOption.Name)
        {
            // Inverser l'ordre si déjà trié par nom
            ViewModel.SortDescending = !ViewModel.SortDescending;
            SortDescendingItem.IsChecked = ViewModel.SortDescending;
        }
        else
        {
            SetSortBy(SortOption.Name);
        }
        ProgramListView.ItemsSource = ViewModel.Programs;
        UpdateSortIcons();
    }

    /// <summary>
    /// Clic sur l'en-tête de colonne "Éditeur"
    /// </summary>
    private void SortByPublisherHeader_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SortBy == SortOption.Publisher)
        {
            ViewModel.SortDescending = !ViewModel.SortDescending;
            SortDescendingItem.IsChecked = ViewModel.SortDescending;
        }
        else
        {
            SetSortBy(SortOption.Publisher);
        }
        ProgramListView.ItemsSource = ViewModel.Programs;
        UpdateSortIcons();
    }

    /// <summary>
    /// Clic sur l'en-tête de colonne "Date"
    /// </summary>
    private void SortByDateHeader_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SortBy == SortOption.InstallDate)
        {
            ViewModel.SortDescending = !ViewModel.SortDescending;
            SortDescendingItem.IsChecked = ViewModel.SortDescending;
        }
        else
        {
            SetSortBy(SortOption.InstallDate);
        }
        ProgramListView.ItemsSource = ViewModel.Programs;
        UpdateSortIcons();
    }

    /// <summary>
    /// Clic sur l'en-tête de colonne "Taille"
    /// </summary>
    private void SortBySizeHeader_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SortBy == SortOption.Size)
        {
            // Inverser l'ordre si déjà trié par taille
            ViewModel.SortDescending = !ViewModel.SortDescending;
            SortDescendingItem.IsChecked = ViewModel.SortDescending;
        }
        else
        {
            SetSortBy(SortOption.Size);
        }
        ProgramListView.ItemsSource = ViewModel.Programs;
        UpdateSortIcons();
    }

    /// <summary>
    /// Définit l'option de tri et met à jour les menus
    /// </summary>
    private void SetSortBy(SortOption option)
    {
        ViewModel.SortBy = option;
        
        // Mettre à jour les RadioMenuFlyoutItems
        SortByNameItem.IsChecked = option == SortOption.Name;
        SortByPublisherItem.IsChecked = option == SortOption.Publisher;
        SortBySizeItem.IsChecked = option == SortOption.Size;
        SortByDateItem.IsChecked = option == SortOption.InstallDate;
        
        ProgramListView.ItemsSource = ViewModel.Programs;
        UpdateSortIcons();
    }

    /// <summary>
    /// Met à jour les icônes de tri dans les en-têtes de colonnes
    /// </summary>
    private void UpdateSortIcons()
    {
        // Icône pour la colonne Nom
        SortNameIcon.Visibility = ViewModel.SortBy == SortOption.Name 
            ? Visibility.Visible 
            : Visibility.Collapsed;
        SortNameIcon.Glyph = ViewModel.SortDescending ? "\uE70E" : "\uE70D"; // Flèche bas/haut
        
        // Icône pour la colonne Éditeur
        SortPublisherIcon.Visibility = ViewModel.SortBy == SortOption.Publisher 
            ? Visibility.Visible 
            : Visibility.Collapsed;
        SortPublisherIcon.Glyph = ViewModel.SortDescending ? "\uE70E" : "\uE70D";
        
        // Icône pour la colonne Date
        SortDateIcon.Visibility = ViewModel.SortBy == SortOption.InstallDate 
            ? Visibility.Visible 
            : Visibility.Collapsed;
        SortDateIcon.Glyph = ViewModel.SortDescending ? "\uE70E" : "\uE70D";
        
        // Icône pour la colonne Taille
        SortSizeIcon.Visibility = ViewModel.SortBy == SortOption.Size 
            ? Visibility.Visible 
            : Visibility.Collapsed;
        SortSizeIcon.Glyph = ViewModel.SortDescending ? "\uE70E" : "\uE70D";
    }

    #endregion

    #region Event Handlers - Program List

    private void ProgramListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.SelectedProgram = ProgramListView.SelectedItem as InstalledProgram;
        UpdateDetailsPanel();
        ShowResiduals(false);
    }

    private void ProgramListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.SelectedProgram != null)
        {
            _ = UninstallSelectedProgramAsync(silent: true);
        }
    }

    #endregion

    #region Event Handlers - Details Panel

    private void OpenLocationButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedProgram != null && 
            !string.IsNullOrEmpty(ViewModel.SelectedProgram.InstallLocation) &&
            Directory.Exists(ViewModel.SelectedProgram.InstallLocation))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = ViewModel.SelectedProgram.InstallLocation,
                UseShellExecute = true
            });
        }
    }

    private void ModifyButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedProgram != null && !string.IsNullOrEmpty(ViewModel.SelectedProgram.ModifyPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = ViewModel.SelectedProgram.ModifyPath,
                    UseShellExecute = true,
                    Verb = "runas"
                });
            }
            catch (Exception ex)
            {
                ShowInfoBar("Erreur", $"Impossible de lancer la modification: {ex.Message}", InfoBarSeverity.Error);
            }
        }
    }

    private async void ScanResidualsButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedProgram == null) return;

        // Ouvrir le dialogue de scan des résidus (meilleure UX qu'un petit panneau)
        var residualDialog = new ResidualScanDialog(ViewModel.SelectedProgram)
        {
            XamlRoot = Content.XamlRoot
        };
        
        await residualDialog.ShowAsync();

        // Mettre à jour le statut après le dialogue
        if (residualDialog.DeletionPerformed)
        {
            StatusText.Text = "Nettoyage des résidus terminé";
            ShowInfoBar("Nettoyage terminé", "Les résidus sélectionnés ont été supprimés.", InfoBarSeverity.Success);
        }
        else if (residualDialog.Residuals.Count > 0)
        {
            // Des résidus restent - ne pas afficher le panneau, juste mettre à jour le statut
            ShowResiduals(false);
            StatusText.Text = $"{residualDialog.Residuals.Count} résidu(s) non supprimé(s)";
        }
        else
        {
            ShowResiduals(false);
            StatusText.Text = "Aucun résidu trouvé";
            ShowInfoBar("Aucun résidu", "Ce programme ne semble pas avoir laissé de traces.", InfoBarSeverity.Success);
        }
    }

    #endregion

    #region Event Handlers - Residuals

    private void SelectHighConfidence_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectHighConfidenceResidualsCommand.Execute(null);
        ResidualsListView.ItemsSource = null;
        ResidualsListView.ItemsSource = ViewModel.Residuals;
        UpdateResidualsInfo();
    }

    private void ToggleSelectAllResiduals_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleSelectAllResidualsCommand.Execute(null);
        ResidualsListView.ItemsSource = null;
        ResidualsListView.ItemsSource = ViewModel.Residuals;
        UpdateResidualsInfo();
    }

    private async void CleanupResidualsButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedCount = ViewModel.Residuals.Count(r => r.IsSelected);
        if (selectedCount == 0)
        {
            ShowInfoBar("Aucune sélection", "Sélectionnez les résidus à supprimer.", InfoBarSeverity.Warning);
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Confirmer le nettoyage",
            Content = $"Vous allez supprimer {selectedCount} élément(s) résiduel(s).\n\nCette action est irréversible.",
            PrimaryButtonText = "Nettoyer",
            SecondaryButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Secondary,
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        ShowUninstallOverlay(true, "Nettoyage des résidus...");

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                UninstallProgressBar.Value = p.Percentage;
                UninstallDetailText.Text = p.StatusMessage;
            });

            await ViewModel.CleanupResidualsAsync();
            
            // Mettre à jour la liste
            ResidualsListView.ItemsSource = null;
            ResidualsListView.ItemsSource = ViewModel.Residuals;
            
            if (!ViewModel.HasResiduals)
            {
                ShowResiduals(false);
            }
            
            UpdateResidualsInfo();
            StatusText.Text = "Nettoyage terminé";
            ShowInfoBar("Nettoyage terminé", $"Espace libéré: {ViewModel.ResidualsTotalSize}", InfoBarSeverity.Success);
        }
        finally
        {
            ShowUninstallOverlay(false);
        }
    }

    #endregion

    #region Event Handlers - Cancel

    private void CancelLoading_Click(object sender, RoutedEventArgs e)
    {
        _currentOperationCts?.Cancel();
    }

    private void CancelUninstall_Click(object sender, RoutedEventArgs e)
    {
        _currentOperationCts?.Cancel();
    }

    #endregion

    #region Uninstall Operations

    private async Task UninstallSelectedProgramAsync(bool silent, bool force = false)
    {
        if (ViewModel.SelectedProgram == null) return;

        _currentOperationCts?.Cancel();
        _currentOperationCts = new CancellationTokenSource();
        _continueManually = false;

        var program = ViewModel.SelectedProgram;
        var operationType = force ? "forcée" : (silent ? "silencieuse" : "standard");
        
        ShowUninstallOverlay(true, $"Désinstallation {operationType} de {program.DisplayName}...");

        var uninstallSuccess = false;

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                UninstallProgressBar.Value = p.Percentage;
                UninstallDetailText.Text = p.StatusMessage;
            });

            // Créer un backup si configuré
            UninstallDetailText.Text = "Création de la sauvegarde...";
            await App.SettingsService.CreateRegistryBackupAsync(program.DisplayName);

            UninstallResult result;
            if (force)
            {
                result = await App.UninstallService.ForceUninstallAsync(program, progress, _currentOperationCts.Token);
            }
            else
            {
                // Désinstaller sans scanner automatiquement les résidus
                result = await App.UninstallService.UninstallProgramAsync(
                    program, silent, scanResiduals: false, 
                    progress, _currentOperationCts.Token);
            }

            uninstallSuccess = result.Success;

            if (!result.Success && !string.IsNullOrEmpty(result.ErrorMessage))
            {
                program.Status = ProgramStatus.Error;
                StatusText.Text = $"Erreur: {result.ErrorMessage}";
                ShowInfoBar("Échec de la désinstallation", result.ErrorMessage, InfoBarSeverity.Error);
            }
        }
        catch (OperationCanceledException)
        {
            // Si l'utilisateur a cliqué sur "Continuer manuellement", on considère que c'est un succès
            if (_continueManually)
            {
                uninstallSuccess = true;
            }
            else
            {
                StatusText.Text = "Opération annulée";
                ShowInfoBar("Annulé", "L'opération a été annulée.", InfoBarSeverity.Informational);
                ShowUninstallOverlay(false);
                return;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Erreur: {ex.Message}";
            ShowInfoBar("Erreur", ex.Message, InfoBarSeverity.Error);
            ShowUninstallOverlay(false);
            return;
        }

        // Étape suivante: nettoyage et scan des résidus
        if (uninstallSuccess)
        {
            program.Status = ProgramStatus.Uninstalled;
            
            // Actualiser la liste des programmes
            UninstallDetailText.Text = "Actualisation de la liste...";
            await LoadProgramsAsync();

            // Afficher le dialogue d'analyse des résidus si l'option est activée
            if (App.SettingsService.Settings.ThoroughAnalysisEnabled)
            {
                ShowUninstallOverlay(false);
                
                var residualDialog = new ResidualScanDialog(program)
                {
                    XamlRoot = Content.XamlRoot
                };
                
                await residualDialog.ShowAsync();

                // Mettre à jour le statut après le dialogue
                if (residualDialog.DeletionPerformed)
                {
                    StatusText.Text = "Désinstallation et nettoyage terminés";
                    ShowInfoBar("Nettoyage terminé", "Les résidus sélectionnés ont été supprimés.", InfoBarSeverity.Success);
                }
                else if (residualDialog.Residuals.Count > 0)
                {
                    // Des résidus restent - ne pas afficher le panneau, juste mettre à jour le statut
                    ShowResiduals(false);
                    StatusText.Text = $"Désinstallé - {residualDialog.Residuals.Count} résidu(s) restant(s)";
                    ShowInfoBar("Résidus restants", 
                        $"{residualDialog.Residuals.Count} élément(s) non supprimé(s)", 
                        InfoBarSeverity.Warning);
                }
                else
                {
                    StatusText.Text = "Désinstallation complète - Aucun résidu";
                    ShowInfoBar("Désinstallation complète", "Aucun résidu détecté.", InfoBarSeverity.Success);
                }
            }
            else
            {
                ShowUninstallOverlay(false);
                StatusText.Text = "Désinstallation complète";
                ShowInfoBar("Désinstallation complète", "Programme désinstallé avec succès.", InfoBarSeverity.Success);
            }
        }
        else
        {
            ShowUninstallOverlay(false);
        }
    }

    private async Task UninstallBatchAsync()
    {
        _currentOperationCts?.Cancel();
        _currentOperationCts = new CancellationTokenSource();

        ShowUninstallOverlay(true, "Préparation de la désinstallation en lot...");

        try
        {
            // Créer un point de restauration
            if (App.SettingsService.Settings.CreateRestorePoint)
            {
                UninstallDetailText.Text = "Création du point de restauration...";
                await App.UninstallService.CreateRestorePointAsync(
                    $"Avant désinstallation de {ViewModel.SelectedCount} programmes - Clean Uninstaller");
            }

            var progress = new Progress<ScanProgress>(p =>
            {
                UninstallProgressBar.Value = p.Percentage;
                UninstallStatusText.Text = p.StatusMessage;
            });

            await ViewModel.UninstallBatchAsync();

            await LoadProgramsAsync();
            
            if (ViewModel.HasResiduals)
            {
                ShowResiduals(true);
            }

            ShowInfoBar("Désinstallation terminée", 
                $"Opération terminée. Vérifiez les résidus si nécessaire.", 
                InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            ShowInfoBar("Annulé", "L'opération a été annulée.", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            ShowInfoBar("Erreur", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            ShowUninstallOverlay(false);
        }
    }

    private void ShowUninstallOverlay(bool show, string message = "")
    {
        UninstallOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        UninstallStatusText.Text = message;
        UninstallProgressBar.Value = 0;
        UninstallDetailText.Text = "";
        ContinueUninstallButton.Visibility = Visibility.Collapsed;
        
        // Annuler le timer du bouton "Continuer" précédent
        _continueButtonCts?.Cancel();
        _continueButtonCts?.Dispose();
        _continueButtonCts = null;
        
        if (show)
        {
            // Afficher le bouton "Continuer" après 30 secondes
            _continueButtonCts = new CancellationTokenSource();
            _ = ShowContinueButtonAfterDelayAsync(_continueButtonCts.Token);
        }
    }

    private CancellationTokenSource? _continueButtonCts;

    private async Task ShowContinueButtonAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(30000, cancellationToken); // 30 secondes
            
            // Vérifier si l'overlay est toujours visible et si on n'a pas été annulé
            if (!cancellationToken.IsCancellationRequested && UninstallOverlay.Visibility == Visibility.Visible)
            {
                ContinueUninstallButton.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException)
        {
            // Le timer a été annulé car la désinstallation s'est terminée
        }
    }

    private void ContinueUninstall_Click(object sender, RoutedEventArgs e)
    {
        // IMPORTANT: Définir le flag AVANT d'annuler pour éviter la condition de course
        // Le flag doit être true quand l'exception OperationCanceledException est attrapée
        _continueManually = true;
        ContinueUninstallButton.Visibility = Visibility.Collapsed;
        UninstallDetailText.Text = "Passage à l'étape suivante...";
        
        // Annuler l'attente en cours - cela va déclencher le passage à l'étape suivante
        _currentOperationCts?.Cancel();
    }

    private bool _continueManually;

    #endregion

    #region Column Resizing

    private double _lastNameWidth;
    private double _lastPublisherWidth;
    private double _lastDateWidth;
    private double _lastSizeWidth;
    private Microsoft.UI.Xaml.DispatcherTimer? _columnSyncTimer;

    private void ProgramListView_Loaded(object sender, RoutedEventArgs e)
    {
        // S'abonner aux changements de taille de la grille d'en-tête
        HeaderGrid.SizeChanged += HeaderGrid_SizeChanged;
        HeaderGrid.LayoutUpdated += HeaderGrid_LayoutUpdated;
        
        // Créer le timer pour la synchronisation pendant le drag
        _columnSyncTimer = new Microsoft.UI.Xaml.DispatcherTimer();
        _columnSyncTimer.Interval = TimeSpan.FromMilliseconds(16); // ~60fps
        _columnSyncTimer.Tick += (s, e) => ForceUpdateListViewColumnWidths();
        
        // Synchronisation initiale avec délai pour laisser le temps au layout
        DispatcherQueue.TryEnqueue(() => UpdateListViewColumnWidths());
    }

    private void HeaderGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateListViewColumnWidths();
    }

    private void HeaderGrid_LayoutUpdated(object? sender, object e)
    {
        UpdateListViewColumnWidths();
    }

    private void ProgramListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        // Mettre à jour les colonnes quand un élément est préparé (virtualization)
        if (!args.InRecycleQueue && args.ItemContainer is ListViewItem container)
        {
            // Utiliser le dispatcher pour s'assurer que le layout est terminé
            DispatcherQueue.TryEnqueue(() =>
            {
                var nameWidth = NameColumn.ActualWidth;
                var publisherWidth = PublisherColumn.ActualWidth;
                var dateWidth = DateColumn.ActualWidth;
                var sizeWidth = SizeColumn.ActualWidth;
                
                if (nameWidth > 0)
                {
                    UpdateItemGridColumns(container, nameWidth, publisherWidth, dateWidth, sizeWidth);
                }
            });
        }
    }

    private void GridSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Démarrer le timer pour synchroniser les colonnes pendant le drag
        _columnSyncTimer?.Start();
    }

    private void GridSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        // Arrêter le timer et faire une dernière mise à jour
        _columnSyncTimer?.Stop();
        ForceUpdateListViewColumnWidths();
    }

    private void ForceUpdateListViewColumnWidths()
    {
        // Réinitialiser le cache pour forcer la mise à jour
        _lastNameWidth = 0;
        _lastPublisherWidth = 0;
        _lastDateWidth = 0;
        _lastSizeWidth = 0;
        UpdateListViewColumnWidths();
    }

    private void UpdateListViewColumnWidths()
    {
        // Récupérer les largeurs actuelles des colonnes de l'en-tête
        var nameWidth = NameColumn.ActualWidth;
        var publisherWidth = PublisherColumn.ActualWidth;
        var dateWidth = DateColumn.ActualWidth;
        var sizeWidth = SizeColumn.ActualWidth;

        if (nameWidth <= 0) return; // Layout pas encore prêt

        // Vérifier si les largeurs ont changé
        if (Math.Abs(nameWidth - _lastNameWidth) < 1 &&
            Math.Abs(publisherWidth - _lastPublisherWidth) < 1 &&
            Math.Abs(dateWidth - _lastDateWidth) < 1 &&
            Math.Abs(sizeWidth - _lastSizeWidth) < 1)
        {
            return; // Pas de changement significatif
        }

        _lastNameWidth = nameWidth;
        _lastPublisherWidth = publisherWidth;
        _lastDateWidth = dateWidth;
        _lastSizeWidth = sizeWidth;

        // Parcourir tous les éléments visibles du ListView
        if (ProgramListView.ItemsSource is not System.Collections.IEnumerable items) return;
        
        foreach (var item in items)
        {
            if (ProgramListView.ContainerFromItem(item) is ListViewItem container)
            {
                UpdateItemGridColumns(container, nameWidth, publisherWidth, dateWidth, sizeWidth);
            }
        }
    }

    private static void UpdateItemGridColumns(ListViewItem container, double nameWidth, double publisherWidth, double dateWidth, double sizeWidth)
    {
        var itemGrid = FindChildGrid(container);
        if (itemGrid != null && itemGrid.ColumnDefinitions.Count >= 9)
        {
            // Synchroniser les largeurs (colonnes 2, 4, 6, 8 correspondent aux données)
            itemGrid.ColumnDefinitions[2].Width = new GridLength(nameWidth);
            itemGrid.ColumnDefinitions[4].Width = new GridLength(publisherWidth);
            itemGrid.ColumnDefinitions[6].Width = new GridLength(dateWidth);
            itemGrid.ColumnDefinitions[8].Width = new GridLength(sizeWidth);
        }
    }

    private static Grid? FindChildGrid(DependencyObject parent)
    {
        var childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            
            if (child is Grid grid && grid.ColumnDefinitions.Count >= 9)
            {
                return grid;
            }
            
            var result = FindChildGrid(child);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    #endregion
}
