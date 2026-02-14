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
            .Take(settings.Search.MaxResults)
            .Select(x =>
            {
                x.Item.Score = x.Score;
                return x.Item;
            })
            .ToList();
    }

    private int CalculateScore(string query, SearchResult item)
    {
        var weights = _settingsProvider.Current.Search.ScoringWeights;

        // Score principal sur le nom
        var nameScore = SearchAlgorithms.CalculateFuzzyScore(query, item.Name, item.UseCount, item.LastUsed, weights);

        // Score additionnel sur le chemin complet (pour les requêtes multi-mots)
        if (weights.EnablePathFuzzyMatch && !string.IsNullOrEmpty(item.Path))
        {
            var pathScore = SearchAlgorithms.CalculatePathFuzzyScore(query, item.Path, weights);

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
        if (!string.IsNullOrEmpty(item.Description))
        {
            var descScore = SearchAlgorithms.CalculateDescriptionScore(query, item.Description);
            if (descScore > 0)
            {
                if (nameScore == 0)
                {
                    var adjustedScore = (int)(descScore * 0.6);

                    if (item.UseCount > 0)
                        adjustedScore += Math.Min(item.UseCount * weights.UsageBonusPerUse, weights.MaxUsageBonus);

                    if (weights.EnableRecencyBonus && item.LastUsed > DateTime.MinValue)
                    {
                        var daysSince = (DateTime.UtcNow - item.LastUsed).TotalDays;
                        adjustedScore += Math.Max(0, weights.MaxRecencyBonus - (int)(daysSince * weights.RecencyDecayPerDay));
                    }

                    return adjustedScore;
                }

                nameScore += (int)(descScore * 0.15);
            }
        }

        return nameScore;
    }
}
