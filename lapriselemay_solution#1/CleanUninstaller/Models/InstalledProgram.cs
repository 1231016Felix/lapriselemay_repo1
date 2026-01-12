using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using CleanUninstaller.Helpers;

namespace CleanUninstaller.Models;

/// <summary>
/// Représente un programme installé sur le système
/// </summary>
public partial class InstalledProgram : ObservableObject
{
    /// <summary>
    /// Identifiant unique (clé de registre ou package name)
    /// </summary>
    public string Id { get; init; } = "";

    /// <summary>
    /// Nom d'affichage du programme
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Nom de l'éditeur
    /// </summary>
    public string Publisher { get; init; } = "";

    /// <summary>
    /// Version du programme
    /// </summary>
    public string Version { get; init; } = "";

    /// <summary>
    /// Taille estimée en octets
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedSize))]
    private long _estimatedSize;

    /// <summary>
    /// Indique si la taille est approximative (calculée à partir du dossier)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedSize))]
    private bool _isSizeApproximate;

    /// <summary>
    /// Date d'installation
    /// </summary>
    public DateTime? InstallDate { get; init; }

    /// <summary>
    /// Chemin d'installation
    /// </summary>
    public string InstallLocation { get; init; } = "";

    /// <summary>
    /// Commande de désinstallation
    /// </summary>
    public string UninstallString { get; init; } = "";

    /// <summary>
    /// Commande de désinstallation silencieuse
    /// </summary>
    public string QuietUninstallString { get; init; } = "";

    /// <summary>
    /// Nom de la clé de registre
    /// </summary>
    public string RegistryKeyName { get; init; } = "";

    /// <summary>
    /// Source du registre (HKLM, HKCU, etc.)
    /// </summary>
    public string RegistrySource { get; init; } = "";

    /// <summary>
    /// Chemin complet dans le registre
    /// </summary>
    public string RegistryPath { get; init; } = "";

    /// <summary>
    /// Commande de modification
    /// </summary>
    public string ModifyPath { get; init; } = "";

    /// <summary>
    /// Indique si c'est une application système
    /// </summary>
    public bool IsSystemComponent { get; init; }

    /// <summary>
    /// Indique si c'est une application Windows Store
    /// </summary>
    public bool IsWindowsApp { get; init; }

    /// <summary>
    /// Indique si le programme peut être modifié
    /// </summary>
    public bool CanModify { get; init; } = true;

    /// <summary>
    /// Indique si le programme peut être réparé
    /// </summary>
    public bool CanRepair { get; init; }

    /// <summary>
    /// URL de support
    /// </summary>
    public string HelpLink { get; init; } = "";

    /// <summary>
    /// URL d'information
    /// </summary>
    public string UrlInfoAbout { get; init; } = "";

    /// <summary>
    /// Icône du programme (chargée dynamiquement)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    [NotifyPropertyChangedFor(nameof(IconVisibility))]
    [NotifyPropertyChangedFor(nameof(IconPlaceholderVisibility))]
    private BitmapImage? _icon;

    /// <summary>
    /// Indique si une icône est disponible
    /// </summary>
    public bool HasIcon => Icon != null;

    /// <summary>
    /// Visibilité de l'icône pour le binding XAML
    /// </summary>
    public Microsoft.UI.Xaml.Visibility IconVisibility => 
        Icon != null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>
    /// Visibilité du placeholder d'icône pour le binding XAML
    /// </summary>
    public Microsoft.UI.Xaml.Visibility IconPlaceholderVisibility => 
        Icon == null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>
    /// Indique si le programme est sélectionné
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Statut actuel du programme
    /// </summary>
    [ObservableProperty]
    private ProgramStatus _status = ProgramStatus.Installed;

    /// <summary>
    /// Message de statut détaillé
    /// </summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>
    /// Taille formatée pour l'affichage
    /// </summary>
    public string FormattedSize => IsSizeApproximate 
        ? $"~{CommonHelpers.FormatSize(EstimatedSize)}" 
        : CommonHelpers.FormatSize(EstimatedSize);

    /// <summary>
    /// Date d'installation formatée
    /// </summary>
    public string FormattedInstallDate => InstallDate?.ToString("dd/MM/yyyy") ?? "Inconnue";

    /// <summary>
    /// Nom de recherche (en minuscules pour le filtrage)
    /// </summary>
    public string SearchName => DisplayName.ToLowerInvariant();

    /// <summary>
    /// Indique si la désinstallation silencieuse est disponible
    /// </summary>
    public bool SupportsSilentUninstall => !string.IsNullOrEmpty(QuietUninstallString) ||
                                           UninstallString.Contains("msiexec", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Type d'installeur détecté
    /// </summary>
    public InstallerType InstallerType => DetectInstallerType();

    private InstallerType DetectInstallerType()
    {
        var uninstall = UninstallString.ToLowerInvariant();

        if (uninstall.Contains("msiexec")) return InstallerType.Msi;
        if (uninstall.Contains("unins") || uninstall.Contains("_iu14d2n")) return InstallerType.InnoSetup;
        if (uninstall.Contains("uninst.exe")) return InstallerType.Nsis;
        if (uninstall.Contains("installshield")) return InstallerType.InstallShield;
        if (IsWindowsApp) return InstallerType.Msix;
        
        return InstallerType.Unknown;
    }
}

/// <summary>
/// Statut d'un programme
/// </summary>
public enum ProgramStatus
{
    Installed,
    Scanning,
    Uninstalling,
    Uninstalled,
    Error,
    PartiallyRemoved
}

/// <summary>
/// Type d'installeur
/// </summary>
public enum InstallerType
{
    Unknown,
    Msi,
    InnoSetup,
    Nsis,
    InstallShield,
    Wix,
    Msix,
    ClickOnce,
    Portable
}
