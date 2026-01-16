// Ce fichier est conservé pour rétrocompatibilité mais redirige vers Shared.Logging
// À terme, supprimer ce fichier et utiliser directement Shared.Logging.ILoggerService

// Utiliser: using Shared.Logging; au lieu de CleanUninstaller.Services.Interfaces;

namespace CleanUninstaller.Services.Interfaces;

/// <summary>
/// Interface pour le service de logging.
/// OBSOLÈTE: Utiliser Shared.Logging.ILoggerService à la place.
/// Cette interface est conservée pour rétrocompatibilité et hérite de la version partagée.
/// </summary>
[Obsolete("Utiliser Shared.Logging.ILoggerService à la place")]
public interface ILoggerService : Shared.Logging.ILoggerService
{
    // Hérite de toutes les méthodes de Shared.Logging.ILoggerService
}
