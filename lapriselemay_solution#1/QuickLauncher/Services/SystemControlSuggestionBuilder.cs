using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Amélioration #1 : Constructeur déclaratif de suggestions pour les commandes système.
/// Remplace le switch monolithique AddExecutableResult du ViewModel
/// par un registre de builders indexés par SystemControlType.
/// 
/// Chaque builder reçoit (commande, argument, requête complète) et retourne
/// une liste de SearchResult à insérer en tête des résultats.
/// </summary>
public sealed class SystemControlSuggestionBuilder
{
    /// <summary>
    /// Delegate pour un builder de suggestion.
    /// </summary>
    /// <param name="cmd">Commande système correspondante.</param>
    /// <param name="arg">Argument après le préfixe (null si absent).</param>
    /// <param name="fullQuery">Requête complète saisie par l'utilisateur.</param>
    /// <returns>Liste de résultats à insérer (peut être vide).</returns>
    public delegate List<SearchResult> SuggestionFactory(SystemControlCommand cmd, string? arg, string fullQuery);
    
    private readonly Dictionary<SystemControlType, SuggestionFactory> _builders = new();

    public SystemControlSuggestionBuilder()
    {
        Register(SystemControlType.Volume, BuildVolumeSuggestions);
        Register(SystemControlType.Brightness, BuildBrightnessSuggestions);
        Register(SystemControlType.Wifi, BuildWifiSuggestions);
        Register(SystemControlType.Screenshot, BuildScreenshotSuggestions);
        Register(SystemControlType.Timer, BuildTimerSuggestions);
        Register(SystemControlType.Note, BuildNoteSuggestions);
        Register(SystemControlType.OpenStartupFolder, BuildSimpleSuggestion("Ouvrir le dossier de démarrage", "Appuyez sur Entrée pour ouvrir"));
        Register(SystemControlType.OpenHostsFile, BuildSimpleSuggestion("Ouvrir le fichier hosts (admin)", "Appuyez sur Entrée pour ouvrir avec privilèges admin"));
    }

    /// <summary>
    /// Enregistre un builder pour un type de commande système.
    /// </summary>
    public void Register(SystemControlType type, SuggestionFactory factory)
    {
        _builders[type] = factory;
    }
    
    /// <summary>
    /// Tente de construire les suggestions pour la commande donnée.
    /// </summary>
    /// <returns>Liste de résultats, ou null si aucun builder n'est enregistré pour ce type.</returns>
    public List<SearchResult>? Build(SystemControlCommand cmd, string? arg, string fullQuery)
    {
        return _builders.TryGetValue(cmd.Type, out var factory) 
            ? factory(cmd, arg, fullQuery) 
            : null;
    }

    // ══════════════════════════════════════════════════════════
    //  BUILDERS PAR TYPE
    // ══════════════════════════════════════════════════════════
    
    private static List<SearchResult> BuildVolumeSuggestions(SystemControlCommand cmd, string? arg, string fullQuery)
    {
        var currentVol = SystemControlService.GetVolume();
        
        if (string.IsNullOrEmpty(arg))
        {
            return [new SearchResult
            {
                Name = $"Volume actuel: {currentVol}%",
                Description = "Appuyez sur Entrée pour voir le volume",
                Type = ResultType.SystemControl,
                DisplayIcon = cmd.Icon,
                Path = fullQuery
            }];
        }
        
        if (int.TryParse(arg, out var volLevel))
        {
            var clamped = Math.Clamp(volLevel, 0, 100);
            return [new SearchResult
            {
                Name = $"Régler le volume à {clamped}%",
                Description = $"Volume actuel: {currentVol}%",
                Type = ResultType.SystemControl,
                DisplayIcon = clamped > 50 ? "🔊" : clamped > 0 ? "🔉" : "🔇",
                Path = fullQuery
            }];
        }
        
        if (arg is "up" or "down" or "+" or "-")
        {
            var direction = arg is "up" or "+" ? "Augmenter" : "Diminuer";
            return [new SearchResult
            {
                Name = $"{direction} le volume de 10%",
                Description = $"Volume actuel: {currentVol}%",
                Type = ResultType.SystemControl,
                DisplayIcon = cmd.Icon,
                Path = fullQuery
            }];
        }
        
        return [];
    }
    
    private static List<SearchResult> BuildBrightnessSuggestions(SystemControlCommand cmd, string? arg, string fullQuery)
    {
        if (!string.IsNullOrEmpty(arg) && int.TryParse(arg, out var brightLevel))
        {
            var clamped = Math.Clamp(brightLevel, 0, 100);
            return [new SearchResult
            {
                Name = $"Régler la luminosité à {clamped}%",
                Description = "Appuyez sur Entrée pour appliquer",
                Type = ResultType.SystemControl,
                DisplayIcon = clamped > 50 ? "☀️" : "🌙",
                Path = fullQuery
            }];
        }
        return [];
    }
    
    private static List<SearchResult> BuildWifiSuggestions(SystemControlCommand cmd, string? arg, string fullQuery)
    {
        return arg switch
        {
            "on" => [new SearchResult
            {
                Name = "Activer le WiFi", Description = "Appuyez sur Entrée pour activer",
                Type = ResultType.SystemControl, DisplayIcon = "📶", Path = fullQuery
            }],
            "off" => [new SearchResult
            {
                Name = "Désactiver le WiFi", Description = "Appuyez sur Entrée pour désactiver",
                Type = ResultType.SystemControl, DisplayIcon = "📵", Path = fullQuery
            }],
            "status" => [new SearchResult
            {
                Name = "Afficher l'état du WiFi", Description = "Appuyez sur Entrée pour voir le statut",
                Type = ResultType.SystemControl, DisplayIcon = cmd.Icon, Path = fullQuery
            }],
            _ => []
        };
    }
    
    private static List<SearchResult> BuildScreenshotSuggestions(SystemControlCommand cmd, string? arg, string fullQuery)
    {
        if (arg is "snip" or "region" or "select")
        {
            return [new SearchResult
            {
                Name = "✂️ Capture de région",
                Description = "Sélectionner une zone à capturer avec annotation",
                Type = ResultType.SystemControl,
                DisplayIcon = "✂️",
                Path = fullQuery
            }];
        }
        
        return [new SearchResult
        {
            Name = "📸 Capture d'écran",
            Description = "Ouvrir l'outil de capture Windows",
            Type = ResultType.SystemControl,
            DisplayIcon = cmd.Icon,
            Path = fullQuery
        }];
    }
    
    private static List<SearchResult> BuildTimerSuggestions(SystemControlCommand cmd, string? arg, string fullQuery)
    {
        if (string.IsNullOrEmpty(arg))
            return [];
        
        var timerParts = arg.Split(' ', 2);
        var duration = timerParts[0];
        var label = timerParts.Length > 1 ? timerParts[1] : null;
        var parsedDuration = TimerWidgetService.ParseDuration(duration);
        
        if (parsedDuration != null)
        {
            var durationText = TimerWidgetService.FormatDuration(parsedDuration.Value);
            return [new SearchResult
            {
                Name = $"⏱️ Créer minuterie: {durationText}",
                Description = string.IsNullOrEmpty(label) ? "Appuyez sur Entrée pour démarrer" : $"Label: {label}",
                Type = ResultType.SystemControl,
                DisplayIcon = cmd.Icon,
                Path = fullQuery
            }];
        }
        
        return [new SearchResult
        {
            Name = "Format invalide",
            Description = "Utilisez: 5m, 30s, 1h, 1h30m, etc.",
            Type = ResultType.SystemControl,
            DisplayIcon = "❌",
            Path = ""
        }];
    }
    
    private static List<SearchResult> BuildNoteSuggestions(SystemControlCommand cmd, string? arg, string fullQuery)
    {
        if (string.IsNullOrEmpty(arg))
            return [];
        
        return [new SearchResult
        {
            Name = "📝 Créer une note",
            Description = arg.Length > 50 ? arg[..47] + "..." : arg,
            Type = ResultType.SystemControl,
            DisplayIcon = cmd.Icon,
            Path = fullQuery
        }];
    }

    /// <summary>
    /// Factory helper pour les commandes sans argument qui affichent un simple résultat.
    /// </summary>
    private static SuggestionFactory BuildSimpleSuggestion(string name, string description)
    {
        return (cmd, _, fullQuery) => [new SearchResult
        {
            Name = name,
            Description = description,
            Type = ResultType.SystemControl,
            DisplayIcon = cmd.Icon,
            Path = fullQuery
        }];
    }
}
