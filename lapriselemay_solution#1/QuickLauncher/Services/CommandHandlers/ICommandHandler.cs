using QuickLauncher.Models;

namespace QuickLauncher.Services.CommandHandlers;

/// <summary>
/// Résultat unifié retourné par tous les command handlers.
/// Découple la logique métier de la couche UI (ViewModel).
/// </summary>
public sealed class CommandResult
{
    /// <summary>
    /// Résultats à afficher dans la liste.
    /// </summary>
    public List<SearchResult> Results { get; init; } = [];
    
    /// <summary>
    /// Si true, le handler est encore en train de charger (afficher l'indicateur de chargement).
    /// </summary>
    public bool IsLoading { get; init; }
}

/// <summary>
/// Interface pour les handlers de commandes asynchrones du launcher.
/// Chaque handler gère un type de commande spécifique (météo, traduction, IA, recherche système).
/// 
/// Principe: le ViewModel se contente d'orchestrer l'affichage,
/// toute la logique métier vit dans les handlers.
/// </summary>
public interface ICommandHandler
{
    /// <summary>
    /// Détermine si ce handler peut traiter la requête donnée.
    /// </summary>
    /// <param name="query">Requête brute de l'utilisateur (en minuscules, trimmée).</param>
    /// <returns>True si ce handler prend en charge la requête.</returns>
    bool CanHandle(string query);
    
    /// <summary>
    /// Exécute la commande et retourne les résultats à afficher.
    /// </summary>
    /// <param name="query">Requête brute de l'utilisateur.</param>
    /// <param name="token">Token d'annulation pour interrompre la recherche.</param>
    /// <returns>Résultats à afficher dans la liste du launcher.</returns>
    Task<CommandResult> ExecuteAsync(string query, CancellationToken token);
}
