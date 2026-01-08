using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CleanUninstaller.Models;
using CleanUninstaller.Services;
using System.Collections.ObjectModel;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CleanUninstaller.Views;

/// <summary>
/// Dialogue pour scanner et nettoyer les programmes orphelins
/// </summary>
public sealed partial class OrphanedProgramsDialog : ContentDialog
{
    private readonly OrphanedProgramsService _orphanedService;
    private CancellationTokenSource? _scanCts;
    
    /// <summary>
    /// Liste des entrées orphelines trouvées
    /// </summary>
    public ObservableCollection<OrphanedEntry> OrphanedEntries { get; } = [];
    
    /// <summary>
    /// Indique si un nettoyage a été effectué
    /// </summary>
    public bool CleanupPerformed { get; private set; }
    
    /// <summary>
    /// Nombre d'entrées supprimées
    /// </summary>
    public int CleanedCount { get; private set; }

    public OrphanedProgramsDialog()
    {
        InitializeComponent();
        _orphanedService = new OrphanedProgramsService();
        
        Loaded += OrphanedProgramsDialog_Loaded;
        Closing += OrphanedProgramsDialog_Closing;
    }

    private async void OrphanedProgramsDialog_Loaded(object sender, RoutedEventArgs e)
    {
        await ScanOrphanedEntriesAsync();
    }

    private void OrphanedProgramsDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        _scanCts?.Cancel();
    }

    /// <summary>
    /// Lance le scan des entrées orphelines
    /// </summary>
    private async Task ScanOrphanedEntriesAsync()
    {
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();

        // Masquer les résultats précédents
        OrphanedEntriesListView.Visibility = Visibility.Collapsed;
        NoResultsPanel.Visibility = Visibility.Collapsed;
        ToolbarPanel.Visibility = Visibility.Collapsed;
        LegendPanel.Visibility = Visibility.Collapsed;
        SummaryPanel.Visibility = Visibility.Collapsed;
        
        // Afficher la progression
        ScanningPanel.Visibility = Visibility.Visible;
        IsPrimaryButtonEnabled = false;
        IsSecondaryButtonEnabled = false;

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                ScanProgressBar.Value = p.Percentage;
                ScanStatusText.Text = p.StatusMessage;
            });

            var entries = await _orphanedService.ScanOrphanedEntriesAsync(progress, _scanCts.Token);
            
            OrphanedEntries.Clear();
            foreach (var entry in entries)
            {
                OrphanedEntries.Add(entry);
            }

            // Afficher les résultats
            ScanningPanel.Visibility = Visibility.Collapsed;
            
            if (OrphanedEntries.Count > 0)
            {
                OrphanedEntriesListView.ItemsSource = OrphanedEntries;
                OrphanedEntriesListView.Visibility = Visibility.Visible;
                ToolbarPanel.Visibility = Visibility.Visible;
                LegendPanel.Visibility = Visibility.Visible;
                
                IsPrimaryButtonEnabled = true;
                IsSecondaryButtonEnabled = true;
                
                // Sélectionner par défaut les entrées sûres (haute confiance)
                SelectHighConfidenceEntries();
                
                UpdateSelectionInfo();
                
                DescriptionInfoBar.Severity = InfoBarSeverity.Warning;
                DescriptionInfoBar.Title = $"{OrphanedEntries.Count} entrées orphelines détectées";
                DescriptionInfoBar.Message = "Ces entrées de registre pointent vers des fichiers ou dossiers qui n'existent plus. Sélectionnez celles à supprimer.";
            }
            else
            {
                NoResultsPanel.Visibility = Visibility.Visible;
                IsSecondaryButtonEnabled = false;
                
                // Masquer l'InfoBar pour éviter la redondance avec NoResultsPanel
                DescriptionInfoBar.IsOpen = false;
            }
        }
        catch (OperationCanceledException)
        {
            ScanningPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ScanningPanel.Visibility = Visibility.Collapsed;
            
            DescriptionInfoBar.Severity = InfoBarSeverity.Error;
            DescriptionInfoBar.Title = "Erreur de scan";
            DescriptionInfoBar.Message = ex.Message;
        }
    }

    /// <summary>
    /// Sélectionne les entrées avec confiance élevée
    /// </summary>
    private void SelectHighConfidenceEntries()
    {
        foreach (var entry in OrphanedEntries)
        {
            entry.IsSelected = entry.Confidence >= ConfidenceLevel.High;
        }
    }

    /// <summary>
    /// Met à jour les informations de sélection
    /// </summary>
    private void UpdateSelectionInfo()
    {
        var selected = OrphanedEntries.Count(e => e.IsSelected);
        var total = OrphanedEntries.Count;
        
        SelectionCountText.Text = $"{selected} sélectionné(s)";
        TotalCountText.Text = $"sur {total} entrée(s)";
        
        SelectAllCheckBox.IsChecked = selected == total && total > 0;
        
        IsPrimaryButtonEnabled = selected > 0;
    }

    #region Event Handlers

    private void SelectAllCheckBox_Click(object sender, RoutedEventArgs e)
    {
        var isChecked = SelectAllCheckBox.IsChecked == true;
        foreach (var entry in OrphanedEntries)
        {
            entry.IsSelected = isChecked;
        }
        UpdateSelectionInfo();
        
        // Rafraîchir la liste
        OrphanedEntriesListView.ItemsSource = null;
        OrphanedEntriesListView.ItemsSource = OrphanedEntries;
    }

    private void SelectHighConfidence_Click(object sender, RoutedEventArgs e)
    {
        SelectHighConfidenceEntries();
        UpdateSelectionInfo();
        
        // Rafraîchir la liste
        OrphanedEntriesListView.ItemsSource = null;
        OrphanedEntriesListView.ItemsSource = OrphanedEntries;
    }

    private async void ShowRegistryPath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string registryPath)
        {
            var dialog = new ContentDialog
            {
                Title = "Chemin du registre",
                Content = new TextBox
                {
                    Text = registryPath,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true
                },
                CloseButtonText = "Fermer",
                XamlRoot = XamlRoot
            };
            
            await dialog.ShowAsync();
        }
    }

    private async void RescanButton_Click(object sender, RoutedEventArgs e)
    {
        SummaryPanel.Visibility = Visibility.Collapsed;
        await ScanOrphanedEntriesAsync();
    }

    private async void PrimaryButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Empêcher la fermeture immédiate
        args.Cancel = true;

        var selectedEntries = OrphanedEntries.Where(e => e.IsSelected).ToList();
        if (selectedEntries.Count == 0)
        {
            return;
        }

        // Demander confirmation
        var confirmDialog = new ContentDialog
        {
            Title = "Confirmer le nettoyage",
            Content = $"Vous allez supprimer {selectedEntries.Count} entrée(s) de registre.\n\n" +
                     "Un fichier de sauvegarde (.reg) sera créé pour permettre la restauration si nécessaire.\n\n" +
                     "Voulez-vous continuer ?",
            PrimaryButtonText = "Supprimer",
            SecondaryButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Secondary,
            XamlRoot = XamlRoot
        };

        if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        // Effectuer le nettoyage
        await CleanupSelectedEntriesAsync(selectedEntries);
    }

    private async void SecondaryButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Empêcher la fermeture
        args.Cancel = true;
        
        await ExportEntriesAsync();
    }

    #endregion

    #region Cleanup

    /// <summary>
    /// Nettoie les entrées sélectionnées
    /// </summary>
    private async Task CleanupSelectedEntriesAsync(List<OrphanedEntry> entries)
    {
        // Afficher la progression
        ScanningPanel.Visibility = Visibility.Visible;
        ScanStatusText.Text = "Création de la sauvegarde...";
        ScanProgressBar.Value = 0;
        IsPrimaryButtonEnabled = false;
        IsSecondaryButtonEnabled = false;

        try
        {
            // Créer un backup
            var backupFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CleanUninstaller", "Backups", "OrphanedEntries");
            Directory.CreateDirectory(backupFolder);
            
            var backupPath = Path.Combine(backupFolder, $"orphaned_backup_{DateTime.Now:yyyyMMdd_HHmmss}.reg");
            await _orphanedService.ExportEntriesAsync(entries, backupPath);

            // Nettoyer
            var progress = new Progress<ScanProgress>(p =>
            {
                ScanProgressBar.Value = p.Percentage;
                ScanStatusText.Text = p.StatusMessage;
            });

            var result = await _orphanedService.CleanupOrphanedEntriesAsync(entries, progress);

            CleanupPerformed = true;
            CleanedCount = result.TotalCleaned;

            // Retirer les entrées supprimées de la liste
            foreach (var deleted in result.DeletedEntries)
            {
                OrphanedEntries.Remove(deleted);
            }

            // Mettre à jour l'affichage
            ScanningPanel.Visibility = Visibility.Collapsed;
            
            if (OrphanedEntries.Count == 0)
            {
                OrphanedEntriesListView.Visibility = Visibility.Collapsed;
                ToolbarPanel.Visibility = Visibility.Collapsed;
                LegendPanel.Visibility = Visibility.Collapsed;
                NoResultsPanel.Visibility = Visibility.Visible;
                IsPrimaryButtonEnabled = false;
            }
            else
            {
                OrphanedEntriesListView.ItemsSource = null;
                OrphanedEntriesListView.ItemsSource = OrphanedEntries;
                IsPrimaryButtonEnabled = true;
                UpdateSelectionInfo();
            }

            // Afficher le résumé
            SummaryPanel.Visibility = Visibility.Visible;
            
            if (result.HasErrors)
            {
                SummaryTitle.Text = $"{result.TotalCleaned} entrée(s) supprimée(s) avec {result.Errors.Count} erreur(s)";
                SummaryDetails.Text = $"Sauvegarde créée : {backupPath}";
                
                DescriptionInfoBar.Severity = InfoBarSeverity.Warning;
                DescriptionInfoBar.Title = "Nettoyage partiel";
                DescriptionInfoBar.Message = string.Join("\n", result.Errors.Take(3));
            }
            else
            {
                SummaryTitle.Text = $"{result.TotalCleaned} entrée(s) supprimée(s) avec succès";
                SummaryDetails.Text = $"Sauvegarde créée : {Path.GetFileName(backupPath)}";
                
                DescriptionInfoBar.Severity = InfoBarSeverity.Success;
                DescriptionInfoBar.Title = "Nettoyage réussi";
                DescriptionInfoBar.Message = "Les entrées orphelines ont été supprimées. Vous pouvez rescanner pour vérifier.";
            }

            IsSecondaryButtonEnabled = OrphanedEntries.Count > 0;
        }
        catch (Exception ex)
        {
            ScanningPanel.Visibility = Visibility.Collapsed;
            IsPrimaryButtonEnabled = true;
            IsSecondaryButtonEnabled = true;

            DescriptionInfoBar.Severity = InfoBarSeverity.Error;
            DescriptionInfoBar.Title = "Erreur de nettoyage";
            DescriptionInfoBar.Message = ex.Message;
        }
    }

    #endregion

    #region Export

    /// <summary>
    /// Exporte les entrées vers un fichier
    /// </summary>
    private async Task ExportEntriesAsync()
    {
        var picker = new FileSavePicker();
        var hWnd = GetWindowHandle();
        InitializeWithWindow.Initialize(picker, hWnd);
        
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = $"orphaned_entries_{DateTime.Now:yyyyMMdd}";
        picker.FileTypeChoices.Add("Fichier registre", [".reg"]);
        picker.FileTypeChoices.Add("CSV", [".csv"]);

        var file = await picker.PickSaveFileAsync();
        if (file == null) return;

        try
        {
            if (file.FileType.Equals(".reg", StringComparison.OrdinalIgnoreCase))
            {
                await _orphanedService.ExportEntriesAsync(OrphanedEntries, file.Path);
            }
            else
            {
                await ExportToCsvAsync(file.Path);
            }

            DescriptionInfoBar.Severity = InfoBarSeverity.Success;
            DescriptionInfoBar.Title = "Export réussi";
            DescriptionInfoBar.Message = $"Exporté vers {file.Name}";
        }
        catch (Exception ex)
        {
            DescriptionInfoBar.Severity = InfoBarSeverity.Error;
            DescriptionInfoBar.Title = "Erreur d'export";
            DescriptionInfoBar.Message = ex.Message;
        }
    }

    private async Task ExportToCsvAsync(string path)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Nom;Type;Raison;Chemin registre;Chemin invalide;Éditeur;Version;Confiance");
        
        foreach (var entry in OrphanedEntries)
        {
            sb.AppendLine($"\"{entry.DisplayName}\";\"{entry.TypeName}\";\"{entry.Reason}\";\"{entry.RegistryPath}\";\"{entry.InvalidPath}\";\"{entry.Publisher}\";\"{entry.Version}\";\"{entry.Confidence}\"");
        }
        
        await File.WriteAllTextAsync(path, sb.ToString(), System.Text.Encoding.UTF8);
    }

    private IntPtr GetWindowHandle()
    {
        // Utiliser directement la fenêtre principale de l'application
        var window = App.MainWindow;
        if (window != null)
        {
            return WindowNative.GetWindowHandle(window);
        }
        return IntPtr.Zero;
    }

    #endregion
}
