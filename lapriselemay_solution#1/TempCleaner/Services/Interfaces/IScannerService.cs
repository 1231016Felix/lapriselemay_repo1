using TempCleaner.Models;

namespace TempCleaner.Services.Interfaces;

/// <summary>
/// Interface pour le service de scan des fichiers temporaires.
/// </summary>
public interface IScannerService
{
    /// <summary>
    /// Scanne les dossiers selon les profils actifs.
    /// </summary>
    /// <param name="profiles">Profils de nettoyage à utiliser</param>
    /// <param name="progress">Reporter de progression optionnel</param>
    /// <param name="cancellationToken">Token d'annulation</param>
    /// <returns>Résultat du scan avec les fichiers trouvés</returns>
    Task<ScanResult> ScanAsync(
        IEnumerable<CleanerProfile> profiles,
        IProgress<(string message, int percent)>? progress = null,
        CancellationToken cancellationToken = default);
}
