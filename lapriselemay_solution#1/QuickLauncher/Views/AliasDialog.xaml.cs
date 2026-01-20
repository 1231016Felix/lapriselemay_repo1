using System.Windows;
using System.Windows.Input;

namespace QuickLauncher.Views;

/// <summary>
/// Dialogue pour créer un alias personnalisé.
/// </summary>
public partial class AliasDialog : Window
{
    /// <summary>
    /// L'alias saisi par l'utilisateur.
    /// </summary>
    public string Alias => AliasTextBox.Text.Trim().ToLowerInvariant();
    
    /// <summary>
    /// Le chemin cible de l'alias.
    /// </summary>
    public string TargetPath { get; private set; } = string.Empty;
    
    public AliasDialog(string suggestedName, string targetPath)
    {
        InitializeComponent();
        
        TargetPath = targetPath;
        TargetTextBox.Text = targetPath;
        
        // Suggérer un alias basé sur le nom (premières lettres ou abréviation)
        var suggested = GenerateSuggestedAlias(suggestedName);
        AliasTextBox.Text = suggested;
        
        Loaded += (_, _) =>
        {
            AliasTextBox.Focus();
            AliasTextBox.SelectAll();
        };
        
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        };
    }
    
    private static string GenerateSuggestedAlias(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;
        
        // Nettoyer le nom
        name = name.Trim();
        
        // Si le nom est court (< 5 caractères), l'utiliser tel quel
        if (name.Length <= 4)
            return name.ToLowerInvariant();
        
        // Essayer de créer des initiales pour les noms composés
        var words = name.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 1)
        {
            // Prendre les initiales de chaque mot
            var initials = string.Concat(words.Where(w => w.Length > 0).Select(w => char.ToLower(w[0])));
            if (initials.Length >= 2)
                return initials;
        }
        
        // Sinon, prendre les 3-4 premiers caractères
        var length = Math.Min(4, name.Length);
        return name[..length].ToLowerInvariant();
    }
    
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
    
    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Alias))
        {
            System.Windows.MessageBox.Show("Veuillez entrer un alias.", "Alias requis", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            AliasTextBox.Focus();
            return;
        }
        
        // Vérifier que l'alias ne contient que des caractères valides
        if (!System.Text.RegularExpressions.Regex.IsMatch(Alias, @"^[a-z0-9]+$"))
        {
            System.Windows.MessageBox.Show("L'alias ne peut contenir que des lettres et des chiffres.", 
                "Alias invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
            AliasTextBox.Focus();
            return;
        }
        
        DialogResult = true;
        Close();
    }
}
