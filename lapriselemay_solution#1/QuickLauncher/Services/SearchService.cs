using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Service de recherche dédié, extrait de IndexingService (Amélioration #3).
/// Responsabilité unique : scoring et filtrage des résultats sur le cache existant.
/// 
/// IndexingService conserve la responsabilité de l'indexation, du cache et de la base de données.
/// SearchService se charge de la recherche, de la calculatrice et des résultats web.
/// </summary>
public sealed class SearchService
{
    private readonly IndexingService _indexingService;
    private readonly ISettingsProvider _settingsProvider;

    public SearchService(IndexingService indexingService, ISettingsProvider settingsProvider)
    {
        _indexingService = indexingService ?? throw new ArgumentNullException(nameof(indexingService));
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
    }

    /// <summary>
    /// Recherche des résultats dans le cache indexé.
    /// </summary>
    public List<SearchResult> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var normalizedQuery = query.Trim().ToLowerInvariant();
        var settings = _settingsProvider.Current;

        // Recherche web avec préfixe
        foreach (var engine in settings.Search.SearchEngines)
        {
            var prefix = $"{engine.Prefix} ";
            if (normalizedQuery.StartsWith(prefix))
            {
                var searchQuery = normalizedQuery[prefix.Length..];
                return
                [
                    new SearchResult
                    {
                        Name = $"Rechercher '{searchQuery}' sur {engine.Name}",
                        Path = engine.UrlTemplate.Replace("{query}", Uri.EscapeDataString(searchQuery)),
                        Type = ResultType.WebSearch,
                        Score = 100
                    }
                ];
            }
        }

        // Calculatrice (Amélioration #7 : utilise le nouveau parser)
        if (CalculatorService.TryCalculate(normalizedQuery, out var calcResult))
        {
            return
            [
                new SearchResult
                {
                    Name = calcResult,
                    Description = $"= {calcResult}",
                    Path = calcResult,
                    Type = ResultType.Calculator,
                    Score = 100
                }
            ];
        }

        // Recherche avec scoring — parallélisme conditionnel selon la taille du cache
        const int ParallelThreshold = 500;
        var cachedItems = _indexingService.CachedItems;

        var scored = cachedItems.Count > ParallelThreshold
            ? cachedItems.Values
                .AsParallel()
                .Select(item => (Item: item, Score: CalculateScore(normalizedQuery, item)))
                .Where(x => x.Score > 0)
            : cachedItems.Values
                .Select(item => (Item: item, Score: CalculateScore(normalizedQuery, item)))
                .Where(x => x.Score > 0);

        return scored
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Item.UseCount)
            // Déduplication : garder le meilleur résultat par (nom normalisé, catégorie de type).
            // Évite les doublons Application/.lnk + StoreApp + SystemControl pour le même programme.
            // Les noms sont normalisés pour ignorer les emojis en tête (ex: "🧹 Nettoyage" = "Nettoyage").
            // Note : on réordonne dans chaque groupe car PLINQ ne garantit pas l'ordre intra-groupe.
            .GroupBy(x => (Name: NormalizeNameForDedup(x.Item.Name), Category: DeduplicationHelper.GetCategory(x.Item.Type)))
            .Select(g => g
                .OrderByDescending(x => x.Item.Type is ResultType.SystemControl or ResultType.AppControl ? 1 : 0)
                .ThenByDescending(x => x.Score)
                .ThenByDescending(x => x.Item.UseCount)
                .First())
            .Take(settings.Search.MaxResults)
            .Select(x =>
            {
                // Conversion IndexedItem → SearchResult pour la couche UI.
                // L'IndexedItem immutable reste intact dans le cache.
                var result = x.Item.ToSearchResult();
                result.Score = x.Score;
                return result;
            })
            .ToList();
    }

    // Déduplication centralisée dans DeduplicationHelper.

    /// <summary>
    /// Normalise un nom pour la déduplication en supprimant les emojis/symboles en tête.
    /// Permet de regrouper "🧹 Nettoyage de disque" (SystemControl) et "Nettoyage de disque" (StoreApp).
    /// </summary>
    private static string NormalizeNameForDedup(string name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        var start = 0;
        while (start < name.Length && !char.IsLetterOrDigit(name[start]))
            start++;

        return name[start..].ToLowerInvariant();
    }

    private int CalculateScore(string query, IndexedItem item)
    {
        var searchSettings = _settingsProvider.Current.Search;
        var weights = searchSettings.ScoringWeights;
        var userAbbreviations = searchSettings.UserAbbreviations;

        // Score principal sur le nom — utilise les champs pré-normalisés (Point #5)
        // pour éviter ~4000 allocations ToLowerInvariant()/StripEmojis() par frappe.
        var nameScore = SearchAlgorithms.CalculateFuzzyScoreNormalized(
            query, item.NameNormalized, item.UseCount, item.LastUsed, weights,
            userAbbreviations.Count > 0 ? userAbbreviations : null);

        // Score additionnel sur le chemin complet (pour les requêtes multi-mots)
        // Utilise PathLower pré-calculé (Point #5).
        if (weights.EnablePathFuzzyMatch && !string.IsNullOrEmpty(item.PathLower))
        {
            var pathScore = SearchAlgorithms.CalculatePathFuzzyScoreNormalized(query, item.PathLower, weights);

            if (pathScore > 0 && nameScore == 0)
            {
                if (item.UseCount > 0)
                    pathScore += Math.Min(item.UseCount * weights.UsageBonusPerUse, weights.MaxUsageBonus);

                if (weights.EnableRecencyBonus && item.LastUsed > DateTime.MinValue)
                {
                    var daysSince = (DateTime.UtcNow - item.LastUsed).TotalDays;
                    pathScore += Math.Max(0, weights.MaxRecencyBonus - (int)(daysSince * weights.RecencyDecayPerDay));
                }

                return pathScore;
            }

            nameScore = Math.Max(nameScore, pathScore);
        }

        // Score sur la description (pour les paramètres Windows, bookmarks, etc.)
        // Utilise DescriptionLower pré-calculé (Point #5).
        if (!string.IsNullOrEmpty(item.DescriptionLower))
        {
            var descScore = SearchAlgorithms.CalculateDescriptionScoreNormalized(query, item.DescriptionLower, weights);
            if (descScore > 0)
            {
                if (nameScore == 0)
                {
                    var adjustedScore = (int)(descScore * weights.DescriptionOnlyMultiplier);

                    if (item.UseCount > 0)
                        adjustedScore += Math.Min(item.UseCount * weights.UsageBonusPerUse, weights.MaxUsageBonus);

                    if (weights.EnableRecencyBonus && item.LastUsed > DateTime.MinValue)
                    {
                        var daysSince = (DateTime.UtcNow - item.LastUsed).TotalDays;
                        adjustedScore += Math.Max(0, weights.MaxRecencyBonus - (int)(daysSince * weights.RecencyDecayPerDay));
                    }

                    return adjustedScore;
                }

                nameScore += (int)(descScore * weights.DescriptionAdditiveMultiplier);
            }
        }

        return nameScore;
    }
}
