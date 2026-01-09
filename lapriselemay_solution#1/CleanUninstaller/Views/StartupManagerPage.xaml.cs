using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CleanUninstaller.ViewModels;
using CleanUninstaller.Models;
using Microsoft.UI;

namespace CleanUninstaller.Views;

/// <summary>
/// Page de gestion des programmes au démarrage
/// </summary>
public sealed partial class StartupManagerPage : Page
{
    public StartupManagerViewModel ViewModel { get; }
    
    // Guard contre la réentrance lors des mises à jour
    private bool _isUpdatingDetails;

    public StartupManagerPage()
    {
        ViewModel = new StartupManagerViewModel();
        this.InitializeComponent();

        Loaded += async (s, e) => await ViewModel.ScanAsync();
        
        // Utiliser l'événement SelectionChanged du ListView plutôt que PropertyChanged
        // pour éviter les boucles de binding TwoWay
        StartupListView.SelectionChanged += StartupListView_SelectionChanged;
    }

    private void StartupListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Guard contre la réentrance
        if (_isUpdatingDetails) return;
        
        try
        {
            _isUpdatingDetails = true;
            
            // Mettre à jour le ViewModel sans déclencher de boucle
            if (StartupListView.SelectedItem is StartupProgram program)
            {
                ViewModel.SelectedProgram = program;
            }
            else
            {
                ViewModel.SelectedProgram = null;
            }
            
            UpdateDetailsPanel();
        }
        finally
        {
            _isUpdatingDetails = false;
        }
    }

    private void UpdateDetailsPanel()
    {
        var program = ViewModel.SelectedProgram;
        
        if (program == null)
        {
            // Réinitialiser les champs
            DetailNameText.Text = string.Empty;
            DetailPublisherText.Text = string.Empty;
            DetailCommandText.Text = string.Empty;
            DetailTypeText.Text = string.Empty;
            DetailTypeIcon.Glyph = string.Empty;
            DetailScopeText.Text = string.Empty;
            DetailScopeIcon.Glyph = string.Empty;
            DetailImpactText.Text = string.Empty;
            DetailImpactIcon.Glyph = string.Empty;
            DetailImpactMs.Text = string.Empty;
            DetailLocationText.Text = string.Empty;
            FileNotFoundInfoBar.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            return;
        }

        try
        {
            DetailNameText.Text = program.Name ?? "Inconnu";
            DetailPublisherText.Text = string.IsNullOrEmpty(program.Publisher) ? "Non spécifié" : program.Publisher;
            DetailCommandText.Text = program.DisplayCommand ?? program.Command ?? "Non spécifié";
            
            DetailTypeText.Text = program.TypeName ?? "Inconnu";
            DetailTypeIcon.Glyph = program.TypeIcon ?? "\uE9CE";
            
            DetailScopeText.Text = program.ScopeName ?? "Inconnu";
            DetailScopeIcon.Glyph = program.ScopeIcon ?? "\uE9CE";
            
            DetailImpactText.Text = program.ImpactName ?? "Non mesuré";
            DetailImpactIcon.Glyph = program.ImpactIcon ?? "\uE9CE";
            DetailImpactIcon.Foreground = program.ImpactBrush ?? new SolidColorBrush(Colors.Gray);
            DetailImpactText.Foreground = program.ImpactBrush ?? new SolidColorBrush(Colors.Gray);
            DetailImpactMs.Text = program.FormattedImpact ?? string.Empty;
            
            DetailLocationText.Text = string.IsNullOrEmpty(program.Location) ? "Non spécifié" : program.Location;
            
            FileNotFoundInfoBar.Visibility = program.FileExists 
                ? Microsoft.UI.Xaml.Visibility.Collapsed 
                : Microsoft.UI.Xaml.Visibility.Visible;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur mise à jour panneau détails: {ex.Message}");
        }
    }
}
