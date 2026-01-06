using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;

namespace CleanUninstaller.Models;

/// <summary>
/// Représente une entrée orpheline dans le système
/// </summary>
public partial class OrphanedEntry : ObservableObject
{
    /// <summary>
    /// Nom d'affichage du programme
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Chemin complet dans le registre
    /// </summary>
    public string RegistryPath { get; set; } = "";

    /// <summary>
    /// Nom de la clé de registre
    /// </summary>
    public string RegistryKeyName { get; set; } = "";

    /// <summary>
    /// Type d'entrée orpheline
    /// </summary>
    public OrphanedEntryType Type { get; set; }

    /// <summary>
    /// Raison pour laquelle l'entrée est considérée orpheline
    /// </summary>
    public string Reason { get; set; } = "";

    /// <summary>
    /// Chemin invalide détecté (désinstalleur, dossier d'installation, etc.)
    /// </summary>
    public string InvalidPath { get; set; } = "";

    /// <summary>
    /// Éditeur du programme
    /// </summary>
    public string Publisher { get; set; } = "";

    /// <summary>
    /// Version du programme
    /// </summary>
    public string Version { get; set; } = "";

    /// <summary>
    /// Date d'installation si disponible
    /// </summary>
    public DateTime? InstallDate { get; set; }

    /// <summary>
    /// Taille estimée en octets
    /// </summary>
    public long EstimatedSize { get; set; }

    /// <summary>
    /// Niveau de confiance pour la suppression
    /// </summary>
    public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.High;

    /// <summary>
    /// Indique si l'entrée est sélectionnée pour suppression
    /// </summary>
    [ObservableProperty]
    private bool _isSelected = true;

    /// <summary>
    /// Description formatée pour l'affichage
    /// </summary>
    public string FormattedDescription => Type switch
    {
        OrphanedEntryType.MissingUninstaller => $"Désinstalleur introuvable: {InvalidPath}",
        OrphanedEntryType.MissingInstallLocation => $"Dossier d'installation inexistant: {InvalidPath}",
        OrphanedEntryType.InvalidRegistryData => $"Données de registre corrompues",
        OrphanedEntryType.EmptyEntry => $"Entrée vide ou incomplète",
        OrphanedEntryType.BrokenShortcut => $"Raccourci cassé: {InvalidPath}",
        OrphanedEntryType.OrphanedComponent => $"Composant orphelin",
        _ => Reason
    };

    /// <summary>
    /// Icône selon le type
    /// </summary>
    public string TypeIcon => Type switch
    {
        OrphanedEntryType.MissingUninstaller => "\uE74D",      // Uninstall
        OrphanedEntryType.MissingInstallLocation => "\uE8B7", // Folder
        OrphanedEntryType.InvalidRegistryData => "\uE7BA",    // Warning
        OrphanedEntryType.EmptyEntry => "\uE946",             // Clear
        OrphanedEntryType.BrokenShortcut => "\uE71B",         // Link
        OrphanedEntryType.OrphanedComponent => "\uE74C",      // Component
        _ => "\uE7BA"
    };

    /// <summary>
    /// Nom du type pour l'affichage
    /// </summary>
    public string TypeName => Type switch
    {
        OrphanedEntryType.MissingUninstaller => "Désinstalleur manquant",
        OrphanedEntryType.MissingInstallLocation => "Dossier inexistant",
        OrphanedEntryType.InvalidRegistryData => "Données corrompues",
        OrphanedEntryType.EmptyEntry => "Entrée vide",
        OrphanedEntryType.BrokenShortcut => "Raccourci cassé",
        OrphanedEntryType.OrphanedComponent => "Composant orphelin",
        _ => "Inconnu"
    };

    /// <summary>
    /// Couleur selon le niveau de confiance
    /// </summary>
    public SolidColorBrush ConfidenceBrush => Confidence switch
    {
        ConfidenceLevel.VeryHigh => new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 16)),  // Vert
        ConfidenceLevel.High => new SolidColorBrush(ColorHelper.FromArgb(255, 73, 130, 5)),      // Vert clair
        ConfidenceLevel.Medium => new SolidColorBrush(ColorHelper.FromArgb(255, 202, 80, 16)),   // Orange
        ConfidenceLevel.Low => new SolidColorBrush(ColorHelper.FromArgb(255, 218, 59, 1)),       // Rouge
        _ => new SolidColorBrush(ColorHelper.FromArgb(255, 128, 128, 128))                        // Gris
    };

    /// <summary>
    /// Taille formatée
    /// </summary>
    public string FormattedSize => FormatSize(EstimatedSize);

    /// <summary>
    /// Source du registre (HKLM ou HKCU)
    /// </summary>
    public string RegistrySource => RegistryPath.StartsWith("HKLM") ? "Système" : "Utilisateur";

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "";
        
        string[] suffixes = ["o", "Ko", "Mo", "Go"];
        int i = 0;
        double size = bytes;
        
        while (size >= 1024 && i < suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }
        
        return $"{size:N1} {suffixes[i]}";
    }
}

/// <summary>
/// Types d'entrées orphelines
/// </summary>
public enum OrphanedEntryType
{
    /// <summary>
    /// Le désinstalleur référencé n'existe plus
    /// </summary>
    MissingUninstaller,

    /// <summary>
    /// Le dossier d'installation n'existe plus
    /// </summary>
    MissingInstallLocation,

    /// <summary>
    /// Les données de registre sont corrompues ou invalides
    /// </summary>
    InvalidRegistryData,

    /// <summary>
    /// L'entrée est vide ou manque d'informations essentielles
    /// </summary>
    EmptyEntry,

    /// <summary>
    /// Raccourci pointant vers un fichier inexistant
    /// </summary>
    BrokenShortcut,

    /// <summary>
    /// Composant orphelin (COM, Shell extension, etc.)
    /// </summary>
    OrphanedComponent
}
