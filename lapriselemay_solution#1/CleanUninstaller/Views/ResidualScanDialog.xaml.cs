using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using CleanUninstaller.Models;
using CleanUninstaller.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace CleanUninstaller.Views;

/// <summary>
/// Dialogue d'analyse et suppression des résidus
/// </summary>
public sealed partial class ResidualScanDialog : ContentDialog
{
    private readonly ResidualScannerService _residualScanner;
    private readonly UninstallService _uninstallService;
    private readonly InstalledProgram _program;
    private ObservableCollection<ResidualItem> _residuals = [];
    private bool _isScanning;
    private bool _isDeleting;
    private bool _deletionPerformed;

    /// <summary>
    /// Indique si des résidus ont été supprimés
    /// </summary>
    public bool DeletionPerformed => _deletionPerformed;

    /// <summary>
    /// Liste des résidus trouvés
    /// </summary>
    public ObservableCollection<ResidualItem> Residuals => _residuals;

    public ResidualScanDialog(InstalledProgram program)
    {
        InitializeComponent();
        
        _residualScanner = App.ResidualScanner;
        _uninstallService = App.UninstallService;
        _program = program;

        ProgramNameText.Text = $"Résidus de {program.DisplayName}";
        
        // Démarrer le scan automatiquement
        _ = StartScanAsync();
    }

    private async Task StartScanAsync()
    {
        _isScanning = true;
        ScanningPanel.Visibility = Visibility.Visible;
        ResidualsListView.Visibility = Visibility.Collapsed;
        IsPrimaryButtonEnabled = false;
        ScanStatusText.Text = "Analyse en cours...";

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                ScanningText.Text = p.StatusMessage;
            });

            var residuals = await _residualScanner.ScanResidualsAsync(_program, progress);
            _residuals = new ObservableCollection<ResidualItem>(residuals);

            // Pré-sélectionner uniquement les éléments sûrs (High et VeryHigh)
            foreach (var item in _residuals)
            {
                item.IsSelected = item.Confidence >= ConfidenceLevel.High;
                item.PropertyChanged += Item_PropertyChanged;
            }

            ResidualsListView.ItemsSource = _residuals;
            UpdateSummary();

            if (_residuals.Count > 0)
            {
                ScanStatusText.Text = $"{_residuals.Count} élément(s) résiduel(s) trouvé(s)";
                IsPrimaryButtonEnabled = true;
            }
            else
            {
                ScanStatusText.Text = "Aucun résidu trouvé - Désinstallation propre!";
            }
        }
        catch (Exception ex)
        {
            ScanStatusText.Text = $"Erreur lors de l'analyse: {ex.Message}";
        }
        finally
        {
            _isScanning = false;
            ScanningPanel.Visibility = Visibility.Collapsed;
            ResidualsListView.Visibility = Visibility.Visible;
        }
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResidualItem.IsSelected))
        {
            UpdateSummary();
        }
    }

    private void UpdateSummary()
    {
        var selected = _residuals.Where(r => r.IsSelected && !r.IsDeleted).ToList();
        var totalSize = selected.Sum(r => r.Size);

        SelectedCountText.Text = selected.Count.ToString();
        TotalSizeText.Text = FormatSize(totalSize);

        IsPrimaryButtonEnabled = selected.Count > 0 && !_isScanning && !_isDeleting;
    }

    private async void DeleteButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Empêcher la fermeture automatique
        args.Cancel = true;

        if (_isDeleting) return;

        var selected = _residuals.Where(r => r.IsSelected && !r.IsDeleted).ToList();
        if (selected.Count == 0) return;

        // Vérifier s'il y a des éléments risqués sélectionnés
        var riskyItems = selected.Where(r => r.IsRisky).ToList();
        
        if (riskyItems.Count > 0)
        {
            // Afficher le panneau de confirmation intégré (on ne peut pas ouvrir un ContentDialog dans un ContentDialog)
            WarningMessageText.Text = $"Vous avez sélectionné {riskyItems.Count} élément(s) avec une confiance faible (jaune ou rouge).\n\n" +
                                      "Ces fichiers pourraient être utilisés par d'autres programmes ou le système.\n\n" +
                                      "Voulez-vous vraiment les supprimer?";
            WarningConfirmPanel.Visibility = Visibility.Visible;
            IsPrimaryButtonEnabled = false;
            IsSecondaryButtonEnabled = false;
            return;
        }

        // Pas d'éléments risqués, procéder directement
        await PerformDeletionAsync(selected);
    }

    private void CancelWarning_Click(object sender, RoutedEventArgs e)
    {
        WarningConfirmPanel.Visibility = Visibility.Collapsed;
        IsPrimaryButtonEnabled = true;
        IsSecondaryButtonEnabled = true;
    }

    private async void ConfirmDelete_Click(object sender, RoutedEventArgs e)
    {
        WarningConfirmPanel.Visibility = Visibility.Collapsed;
        var selected = _residuals.Where(r => r.IsSelected && !r.IsDeleted).ToList();
        await PerformDeletionAsync(selected);
    }

    private async Task PerformDeletionAsync(List<ResidualItem> selected)
    {
        // Procéder à la suppression
        _isDeleting = true;
        IsPrimaryButtonEnabled = false;
        IsSecondaryButtonEnabled = false;
        ScanStatusText.Text = "Suppression en cours...";

        try
        {
            var deletedCount = 0;
            var failedCount = 0;
            long spaceFreed = 0;
            var total = selected.Count;

            foreach (var item in selected)
            {
                ScanStatusText.Text = $"Suppression {deletedCount + 1}/{total}: {item.DisplayPath}...";
                
                try
                {
                    var progress = new Progress<ScanProgress>(p =>
                    {
                        ScanStatusText.Text = p.StatusMessage;
                    });

                    // Supprimer via le service
                    var itemsToDelete = new[] { item };
                    var result = await _uninstallService.CleanupResidualsAsync(itemsToDelete, progress);

                    if (item.IsDeleted || result.DeletedCount > 0)
                    {
                        item.IsDeleted = true;
                        deletedCount++;
                        spaceFreed += item.Size;
                    }
                    else
                    {
                        failedCount++;
                        if (string.IsNullOrEmpty(item.ErrorMessage))
                        {
                            item.ErrorMessage = "Échec de la suppression";
                        }
                    }
                }
                catch (Exception ex)
                {
                    failedCount++;
                    item.ErrorMessage = ex.Message;
                }
            }

            // Retirer les éléments supprimés de la liste
            var deleted = _residuals.Where(r => r.IsDeleted).ToList();
            foreach (var item in deleted)
            {
                item.PropertyChanged -= Item_PropertyChanged;
                _residuals.Remove(item);
            }

            _deletionPerformed = deletedCount > 0;

            if (failedCount > 0)
            {
                ScanStatusText.Text = $"Terminé: {deletedCount} supprimé(s), {failedCount} échec(s), {FormatSize(spaceFreed)} libéré(s)";
                
                // Afficher les détails des erreurs dans l'InfoBar
                var failedItems = _residuals.Where(r => !string.IsNullOrEmpty(r.ErrorMessage)).ToList();
                var errorDetails = new System.Text.StringBuilder();
                errorDetails.AppendLine($"{failedCount} élément(s) n'ont pas pu être supprimés:");
                
                foreach (var item in failedItems.Take(5)) // Limiter à 5 pour ne pas surcharger
                {
                    var reason = item.ErrorMessage ?? "Raison inconnue";
                    // Simplifier les messages d'erreur courants
                    if (reason.Contains("Access", StringComparison.OrdinalIgnoreCase) || 
                        reason.Contains("Accès", StringComparison.OrdinalIgnoreCase))
                        reason = "Accès refusé (fichier en cours d'utilisation ou protégé)";
                    else if (reason.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                             reason.Contains("introuvable", StringComparison.OrdinalIgnoreCase))
                        reason = "Élément introuvable";
                    
                    errorDetails.AppendLine($"• {item.DisplayPath}: {reason}");
                }
                
                if (failedItems.Count > 5)
                {
                    errorDetails.AppendLine($"... et {failedItems.Count - 5} autre(s)");
                }
                
                ErrorInfoBar.Title = "Certains éléments n'ont pas pu être supprimés";
                ErrorInfoBar.Message = errorDetails.ToString();
                ErrorInfoBar.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning;
                ErrorInfoBar.IsOpen = true;
            }
            else
            {
                ScanStatusText.Text = $"Terminé: {deletedCount} élément(s) supprimé(s), {FormatSize(spaceFreed)} libéré(s)";
                ErrorInfoBar.IsOpen = false;
            }

            UpdateSummary();

            if (_residuals.Count == 0)
            {
                // Tous les résidus ont été supprimés, fermer le dialogue
                await Task.Delay(1500);
                Hide();
            }
        }
        catch (Exception ex)
        {
            ScanStatusText.Text = $"Erreur: {ex.Message}";
        }
        finally
        {
            _isDeleting = false;
            IsSecondaryButtonEnabled = true;
            UpdateSummary();
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _residuals)
        {
            if (!item.IsDeleted)
            {
                item.IsSelected = true;
            }
        }
    }

    private void SelectSafeOnly_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _residuals)
        {
            if (!item.IsDeleted)
            {
                item.IsSelected = item.Confidence >= ConfidenceLevel.High;
            }
        }
    }

    private void ContentDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        // Empêcher la fermeture pendant la suppression
        if (_isDeleting)
        {
            args.Cancel = true;
            return;
        }

        // Nettoyer les handlers d'événements
        foreach (var item in _residuals)
        {
            item.PropertyChanged -= Item_PropertyChanged;
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 o";
        
        string[] suffixes = ["o", "Ko", "Mo", "Go"];
        var counter = 0;
        var size = (decimal)bytes;
        
        while (size >= 1024 && counter < suffixes.Length - 1)
        {
            size /= 1024;
            counter++;
        }
        
        return $"{size:N1} {suffixes[counter]}";
    }
}
