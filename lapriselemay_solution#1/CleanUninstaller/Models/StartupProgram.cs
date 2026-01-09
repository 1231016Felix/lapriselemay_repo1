using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;

namespace CleanUninstaller.Models;

/// <summary>
/// Représente un programme configuré pour se lancer au démarrage
/// </summary>
public class StartupProgram : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isEnabled;
    private bool _isSelected;
    
    // Brushes en cache pour éviter la création d'objets à chaque accès (cause StackOverflow dans WinUI binding)
    // Utilisation de Lazy<T> pour s'assurer que les Brushes sont créés sur le thread UI
    private static readonly Lazy<SolidColorBrush> GrayBrush = new(() => new SolidColorBrush(Colors.Gray));
    private static readonly Lazy<SolidColorBrush> GreenBrush = new(() => new SolidColorBrush(Colors.Green));
    private static readonly Lazy<SolidColorBrush> OrangeBrush = new(() => new SolidColorBrush(Colors.Orange));
    private static readonly Lazy<SolidColorBrush> OrangeRedBrush = new(() => new SolidColorBrush(Colors.OrangeRed));
    private static readonly Lazy<SolidColorBrush> RedBrush = new(() => new SolidColorBrush(Colors.Red));

    /// <summary>
    /// Nom affiché du programme
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Chemin complet de l'exécutable
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Arguments de ligne de commande
    /// </summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>
    /// Emplacement de la configuration (registre, dossier, tâche planifiée)
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Type de démarrage
    /// </summary>
    public StartupType Type { get; set; }

    /// <summary>
    /// Portée (utilisateur courant ou tous les utilisateurs)
    /// </summary>
    public StartupScope Scope { get; set; }

    /// <summary>
    /// Le programme est-il activé au démarrage
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusBrush)));
            }
        }
    }

    /// <summary>
    /// Sélectionné pour action groupée
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    /// <summary>
    /// Éditeur du programme
    /// </summary>
    public string Publisher { get; set; } = string.Empty;

    /// <summary>
    /// Description du programme
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Impact estimé sur le démarrage (en millisecondes)
    /// </summary>
    public int EstimatedImpactMs { get; set; }

    /// <summary>
    /// Catégorie d'impact
    /// </summary>
    public StartupImpact Impact { get; set; }

    /// <summary>
    /// Date d'ajout au démarrage (si disponible)
    /// </summary>
    public DateTime? AddedDate { get; set; }

    /// <summary>
    /// Le fichier exécutable existe-t-il
    /// </summary>
    public bool FileExists { get; set; }

    /// <summary>
    /// Clé de registre ou chemin complet pour identification unique
    /// </summary>
    public string RegistryKey { get; set; } = string.Empty;

    /// <summary>
    /// Nom de la valeur dans le registre
    /// </summary>
    public string RegistryValueName { get; set; } = string.Empty;

    /// <summary>
    /// Programme associé dans la liste des programmes installés (si trouvé)
    /// </summary>
    public InstalledProgram? AssociatedProgram { get; set; }

    // Propriétés calculées pour l'affichage

    public string TypeName => Type switch
    {
        StartupType.Registry => "Registre",
        StartupType.StartupFolder => "Dossier Démarrage",
        StartupType.ScheduledTask => "Tâche planifiée",
        StartupType.Service => "Service Windows",
        _ => "Inconnu"
    };

    public string TypeIcon => Type switch
    {
        StartupType.Registry => "\uE8F1",        // Settings
        StartupType.StartupFolder => "\uE8B7",   // Folder
        StartupType.ScheduledTask => "\uE823",   // Clock
        StartupType.Service => "\uE912",         // Processing
        _ => "\uE9CE"
    };

    public string ScopeName => Scope switch
    {
        StartupScope.CurrentUser => "Utilisateur",
        StartupScope.AllUsers => "Tous les utilisateurs",
        StartupScope.System => "Système",
        _ => "Inconnu"
    };

    public string ScopeIcon => Scope switch
    {
        StartupScope.CurrentUser => "\uE77B",    // Contact
        StartupScope.AllUsers => "\uE716",       // People
        StartupScope.System => "\uE7EF",         // Shield
        _ => "\uE9CE"
    };

    public string ImpactName => Impact switch
    {
        StartupImpact.None => "Aucun",
        StartupImpact.Low => "Faible",
        StartupImpact.Medium => "Moyen",
        StartupImpact.High => "Élevé",
        StartupImpact.Critical => "Critique",
        _ => "Non mesuré"
    };

    public string ImpactIcon => Impact switch
    {
        StartupImpact.None => "\uE73E",          // Checkmark
        StartupImpact.Low => "\uE74B",           // Down arrow
        StartupImpact.Medium => "\uE738",        // Dash
        StartupImpact.High => "\uE74A",          // Up arrow
        StartupImpact.Critical => "\uE7BA",      // Warning
        _ => "\uE9CE"
    };

    public Brush ImpactBrush => Impact switch
    {
        StartupImpact.None => GrayBrush.Value,
        StartupImpact.Low => GreenBrush.Value,
        StartupImpact.Medium => OrangeBrush.Value,
        StartupImpact.High => OrangeRedBrush.Value,
        StartupImpact.Critical => RedBrush.Value,
        _ => GrayBrush.Value
    };

    public string StatusText => IsEnabled ? "Activé" : "Désactivé";

    public Brush StatusBrush => IsEnabled ? GreenBrush.Value : GrayBrush.Value;

    public string FormattedImpact => EstimatedImpactMs > 0
        ? $"{EstimatedImpactMs:N0} ms"
        : "Non mesuré";

    public string DisplayCommand => string.IsNullOrEmpty(Arguments)
        ? Command ?? string.Empty
        : $"{Command ?? string.Empty} {Arguments}";

    public Visibility FileExistsWarningVisibility => FileExists
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility AssociatedProgramVisibility => AssociatedProgram != null
        ? Visibility.Visible
        : Visibility.Collapsed;
}

/// <summary>
/// Type de configuration de démarrage
/// </summary>
public enum StartupType
{
    Registry,
    StartupFolder,
    ScheduledTask,
    Service
}

/// <summary>
/// Portée du démarrage
/// </summary>
public enum StartupScope
{
    CurrentUser,
    AllUsers,
    System
}

/// <summary>
/// Impact estimé sur le temps de démarrage
/// </summary>
public enum StartupImpact
{
    NotMeasured,
    None,
    Low,
    Medium,
    High,
    Critical
}
