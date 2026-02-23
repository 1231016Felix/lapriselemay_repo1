using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Service de suggestion fantôme (ghost text / autocomplétion).
/// Extrait du ViewModel pour testabilité et responsabilité unique.
/// 
/// Logique : propose une complétion basée sur les commandes système,
/// les préfixes de recherche web, ou le meilleur résultat de recherche.
/// </summary>
public sealed class GhostSuggestionService
{
    private readonly ISettingsProvider _settingsProvider;

    public GhostSuggestionService(ISettingsProvider settingsProvider)
    {
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
    }

    /// <summary>
    /// Calcule la suggestion fantôme pour le texte de recherche courant.
    /// </summary>
    /// <param name="searchText">Texte saisi par l'utilisateur.</param>
    /// <param name="results">Résultats de recherche actuels.</param>
    /// <returns>Le texte de suggestion complète, ou string.Empty si aucune suggestion.</returns>
    public string GetSuggestion(string searchText, IReadOnlyList<SearchResult> results)
    {
        var settings = _settingsProvider.Current;
        
        if (!settings.Appearance.ShowGhostSuggestions 
            || string.IsNullOrWhiteSpace(searchText) 
            || results.Count == 0)
        {
            return string.Empty;
        }

        var query = searchText.Trim();

        // Ne pas suggérer pour les commandes avec arguments (ex: ":note mon texte")
        // ni pour les préfixes de recherche web (ex: "g query")
        if (query.Contains(' '))
            return string.Empty;

        // 1. Commandes système/application (ex: ":w" → ":weather", "::s" → "::settings")
        var cmdSuggestion = GetCommandSuggestion(query, settings);
        if (cmdSuggestion != null)
            return cmdSuggestion;

        // 2. Préfixes de recherche web (ex: "g" → "g ", "yt" → "yt ")
        var webSuggestion = GetWebPrefixSuggestion(query, settings);
        if (webSuggestion != null)
            return webSuggestion;

        // 3. Résultats de recherche (applications, fichiers, historique)
        return GetResultSuggestion(query, results);
    }

    /// <summary>
    /// Suggère une commande système ou application.
    /// Distingue "::" (commandes application) de ":" (commandes système).
    /// </summary>
    private static string? GetCommandSuggestion(string query, AppSettings settings)
    {
        if (!query.StartsWith(':') || query.Length < 2)
            return null;

        if (query.StartsWith("::"))
        {
            var appQuery = query[2..];
            if (appQuery.Length == 0)
                return null;

            var bestAppCmd = settings.SystemCommands
                .Where(c => c.IsEnabled && c.IsAppCommand
                            && c.Prefix.StartsWith(appQuery, StringComparison.OrdinalIgnoreCase)
                            && c.Prefix.Length > appQuery.Length)
                .OrderBy(c => c.Prefix.Length)
                .FirstOrDefault();

            return bestAppCmd != null ? $"::{bestAppCmd.Prefix}" : null;
        }
        else
        {
            var cmdQuery = query[1..];
            var bestCmd = settings.SystemCommands
                .Where(c => c.IsEnabled && !c.IsAppCommand
                            && c.Prefix.StartsWith(cmdQuery, StringComparison.OrdinalIgnoreCase)
                            && c.Prefix.Length > cmdQuery.Length)
                .OrderBy(c => c.Prefix.Length)
                .FirstOrDefault();

            return bestCmd != null ? $":{bestCmd.Prefix}" : null;
        }
    }

    /// <summary>
    /// Suggère un préfixe de recherche web complet (ex: "g" → "g ").
    /// </summary>
    private static string? GetWebPrefixSuggestion(string query, AppSettings settings)
    {
        var matchingEngine = settings.Search.SearchEngines
            .FirstOrDefault(e => e.Prefix.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                                 && e.Prefix.Length >= query.Length);

        if (matchingEngine != null && matchingEngine.Prefix.Length == query.Length)
            return $"{matchingEngine.Prefix} ";

        return null;
    }

    /// <summary>
    /// Suggère le meilleur résultat de recherche (applications, fichiers, historique).
    /// Conserve la casse de l'utilisateur + complétion du résultat.
    /// </summary>
    private static string GetResultSuggestion(string query, IReadOnlyList<SearchResult> results)
    {
        var bestMatch = results
            .Where(r => r.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                        && r.Name.Length > query.Length
                        && r.Type is not (ResultType.Calculator or ResultType.WebSearch
                            or ResultType.SystemControl or ResultType.AppControl or ResultType.SystemCommand))
            .OrderByDescending(r => r.UseCount)
            .ThenByDescending(r => r.Score)
            .FirstOrDefault();

        return bestMatch != null
            ? query + bestMatch.Name[query.Length..]
            : string.Empty;
    }
}