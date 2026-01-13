using CleanUninstaller.Models;

namespace CleanUninstaller.Services.Interfaces;

/// <summary>
/// Service pour afficher des dialogues de manière découplée du ViewModel.
/// Permet de garder les ViewModels testables sans dépendance à l'UI.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Affiche le dialogue de scan des résidus pour un programme.
    /// </summary>
    /// <param name="program">Programme pour lequel scanner les résidus</param>
    /// <returns>Tuple contenant: si une suppression a été effectuée, et la liste des résidus trouvés</returns>
    Task<(bool DeletionPerformed, List<ResidualItem> Residuals)> ShowResidualScanDialogAsync(InstalledProgram program);

    /// <summary>
    /// Affiche une boîte de dialogue de confirmation.
    /// </summary>
    /// <param name="title">Titre du dialogue</param>
    /// <param name="message">Message à afficher</param>
    /// <param name="primaryButton">Texte du bouton principal</param>
    /// <param name="secondaryButton">Texte du bouton secondaire (optionnel)</param>
    /// <returns>True si le bouton principal a été cliqué</returns>
    Task<bool> ShowConfirmationAsync(string title, string message, string primaryButton = "OK", string? secondaryButton = null);

    /// <summary>
    /// Affiche un message d'information.
    /// </summary>
    /// <param name="title">Titre du dialogue</param>
    /// <param name="message">Message à afficher</param>
    Task ShowInfoAsync(string title, string message);

    /// <summary>
    /// Affiche un message d'erreur.
    /// </summary>
    /// <param name="title">Titre du dialogue</param>
    /// <param name="message">Message d'erreur</param>
    Task ShowErrorAsync(string title, string message);
}
