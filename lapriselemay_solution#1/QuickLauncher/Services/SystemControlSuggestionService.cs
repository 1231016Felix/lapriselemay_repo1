using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Service de construction des suggestions de commandes système et applicatives.
/// Responsabilité unique : transformer une requête ":xxx" en liste de SearchResult.
/// Extrait de LauncherViewModel (Point #5).
/// </summary>
public sealed class SystemControlSuggestionService
{
    private readonly ISettingsProvider _settingsProvider;
    private readonly SystemControlSuggestionBuilder _suggestionBuilder = new();

    private AppSettings Settings => _settingsProvider.Current;

    public SystemControlSuggestionService(ISettingsProvider settingsProvider)
    {
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
    }

    /// <summary>
    /// Vérifie si la requête correspond à une commande de contrôle système.
    /// Distingue le préfixe ":" (commandes système) de "::" (commandes application).
    /// </summary>
    public bool IsSystemControlCommand(string query)
    {
        if (!query.StartsWith(':'))
            return false;

        var enabledCommands = Settings.SystemCommands.Where(c => c.IsEnabled).ToList();

        var isDoubleColon = query.StartsWith("::");
        var isSingleColonOnly = query.Length >= 2 && query[1] != ':';

        foreach (var cmd in enabledCommands)
        {
            if (isSingleColonOnly && cmd.IsAppCommand) continue;
            if (isDoubleColon && !cmd.IsAppCommand) continue;

            var prefix = cmd.FullPrefix;
            if (query.StartsWith(prefix) || prefix.StartsWith(query))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Construit les suggestions de commandes de contrôle système basées sur la requête.
    /// Filtre selon la portée du préfixe : ":" (système) vs "::" (application).
    /// </summary>
    /// <returns>Liste de SearchResult à afficher.</returns>
    public List<SearchResult> BuildSuggestions(string query)
    {
        var results = new List<SearchResult>();
        var enabledCommands = Settings.SystemCommands.Where(c => c.IsEnabled).ToList();

        // Filtrer selon la portée :: vs :
        var isDoubleColon = query.StartsWith("::");
        var isSingleColonOnly = query.Length >= 2 && query[1] != ':';

        if (isSingleColonOnly)
            enabledCommands = enabledCommands.Where(c => !c.IsAppCommand).ToList();
        else if (isDoubleColon)
            enabledCommands = enabledCommands.Where(c => c.IsAppCommand).ToList();

        // Déterminer si la requête contient un argument (ex: ":sc snip" → arg = "snip")
        var parts = query.Split(' ', 2);
        var cmdPrefix = parts[0].TrimStart(':');
        var arg = parts.Length > 1 ? parts[1] : null;

        // Trouver la commande exacte correspondant au préfixe
        var matchedCmd = enabledCommands.FirstOrDefault(c =>
            c.Prefix.Equals(cmdPrefix, StringComparison.OrdinalIgnoreCase));

        // Si on a une commande exacte avec argument, ne générer QUE le résultat exécutable
        if (matchedCmd != null && arg != null)
        {
            AddExecutableResult(results, matchedCmd, arg, query);
            return results;
        }

        // Ensemble des types déjà ajoutés via le builder, pour éviter les doublons
        var typesFromBuilder = new HashSet<SystemControlType>();

        // Si on a une commande exacte SANS argument, ajouter le résultat exécutable en premier
        if (matchedCmd != null)
        {
            var suggestions = _suggestionBuilder.Build(matchedCmd, null, query);
            if (suggestions is { Count: > 0 })
            {
                results.AddRange(suggestions);
                typesFromBuilder.Add(matchedCmd.Type);
            }

            // Ajouter les sous-commandes pour screenshot (snip) même quand le préfixe est exact
            if (matchedCmd.Type == SystemControlType.Screenshot)
            {
                var snipName = $":{matchedCmd.Prefix} snip";
                results.Add(new SearchResult
                {
                    Name = snipName,
                    Description = "Sélectionner une zone à capturer (Outil Capture d'écran)",
                    Type = ResultType.SystemControl,
                    DisplayIcon = "✂️",
                    Path = snipName
                });
            }
        }

        // Ajouter les suggestions de préfixe partiel
        foreach (var cmd in enabledCommands)
        {
            if (typesFromBuilder.Contains(cmd.Type))
                continue;

            var prefix = cmd.FullPrefix;
            var displayName = cmd.RequiresArgument
                ? $"{cmd.FullPrefix} {cmd.ArgumentHint}"
                : cmd.FullPrefix;

            if (prefix.StartsWith(query) || displayName.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new SearchResult
                {
                    Name = displayName,
                    Description = cmd.Description,
                    Type = cmd.IsAppCommand ? ResultType.AppControl : ResultType.SystemControl,
                    DisplayIcon = cmd.Icon,
                    Path = prefix
                });

                // Ajouter les sous-commandes pour screenshot
                if (cmd.Type == SystemControlType.Screenshot && !query.Contains(' '))
                {
                    var snipName = $"{cmd.FullPrefix} snip";
                    results.Add(new SearchResult
                    {
                        Name = snipName,
                        Description = "Sélectionner une zone à capturer (Outil Capture d'écran)",
                        Type = ResultType.SystemControl,
                        DisplayIcon = "✂️",
                        Path = snipName
                    });
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Construit la liste d'aide montrant toutes les commandes et raccourcis disponibles.
    /// </summary>
    public List<SearchResult> BuildHelpResults()
    {
        var results = new List<SearchResult>();

        // Toutes les commandes système (inclut les commandes applicatives)
        foreach (var cmd in Settings.SystemCommands.Where(c => c.IsEnabled))
        {
            var displayName = cmd.RequiresArgument
                ? $"{cmd.FullPrefix} {cmd.ArgumentHint}"
                : cmd.FullPrefix;

            results.Add(new SearchResult
            {
                Name = displayName,
                Description = cmd.Description,
                Type = cmd.IsAppCommand ? ResultType.AppControl : ResultType.SystemControl,
                DisplayIcon = cmd.Icon,
                Path = cmd.FullPrefix
            });
        }

        // Recherche web
        foreach (var engine in Settings.Search.SearchEngines.Take(4))
        {
            results.Add(new SearchResult
            {
                Name = $"{engine.Prefix} [recherche]",
                Description = $"Recherche {engine.Name}",
                Type = ResultType.SystemCommand,
                DisplayIcon = "🌐"
            });
        }

        // Raccourcis clavier
        results.Add(new SearchResult
        {
            Name = "Raccourcis clavier",
            Description = "Ctrl+Entrée: Admin • Ctrl+O: Emplacement • Ctrl+Maj+C: Copier chemin",
            Type = ResultType.SystemCommand,
            DisplayIcon = "⌨️"
        });

        return results;
    }

    /// <summary>
    /// Ajoute un résultat exécutable pour une commande avec argument.
    /// Délègue au SystemControlSuggestionBuilder déclaratif.
    /// </summary>
    private void AddExecutableResult(List<SearchResult> results, SystemControlCommand cmd, string? arg, string fullQuery)
    {
        var suggestions = _suggestionBuilder.Build(cmd, arg, fullQuery);
        if (suggestions == null || suggestions.Count == 0)
            return;

        // Insérer en tête des résultats
        for (var i = suggestions.Count - 1; i >= 0; i--)
            results.Insert(0, suggestions[i]);
    }
}
