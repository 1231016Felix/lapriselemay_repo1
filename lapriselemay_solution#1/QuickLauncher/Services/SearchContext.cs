using QuickLauncher.Services.CommandHandlers;

namespace QuickLauncher.Services;

/// <summary>
/// Composite regroupant les services liés à la recherche et à l'indexation.
/// Réduit le nombre de paramètres du constructeur de <see cref="ViewModels.LauncherViewModel"/>
/// en regroupant les dépendances par domaine fonctionnel.
/// 
/// Enregistré en singleton dans le conteneur DI.
/// </summary>
public sealed class SearchContext
{
    public IndexingService Indexing { get; }
    public SearchService Search { get; }
    public AliasService Aliases { get; }
    public GhostSuggestionService Ghost { get; }
    public CommandRouter Commands { get; }
    public ISystemControlExecutor SystemExecutor { get; }
    public SystemControlSuggestionService SystemSuggestions { get; }
    public IIconLoader Icons { get; }

    public SearchContext(
        IndexingService indexing,
        SearchService search,
        AliasService aliases,
        GhostSuggestionService ghost,
        CommandRouter commands,
        ISystemControlExecutor systemExecutor,
        SystemControlSuggestionService systemSuggestions,
        IIconLoader icons)
    {
        Indexing = indexing ?? throw new ArgumentNullException(nameof(indexing));
        Search = search ?? throw new ArgumentNullException(nameof(search));
        Aliases = aliases ?? throw new ArgumentNullException(nameof(aliases));
        Ghost = ghost ?? throw new ArgumentNullException(nameof(ghost));
        Commands = commands ?? throw new ArgumentNullException(nameof(commands));
        SystemExecutor = systemExecutor ?? throw new ArgumentNullException(nameof(systemExecutor));
        SystemSuggestions = systemSuggestions ?? throw new ArgumentNullException(nameof(systemSuggestions));
        Icons = icons ?? throw new ArgumentNullException(nameof(icons));
    }
}
