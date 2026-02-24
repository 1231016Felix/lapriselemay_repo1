using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Composite regroupant les services d'actions sur les résultats de recherche.
/// Réduit le nombre de paramètres du constructeur de <see cref="ViewModels.LauncherViewModel"/>.
/// 
/// Enregistré en singleton dans le conteneur DI.
/// </summary>
public sealed class ActionContext
{
    public ResultActionService Actions { get; }
    public PinnedItemsManager Pinned { get; }
    public ILaunchService Launcher { get; }
    public IFileActionProvider FileActions { get; }

    public ActionContext(
        ResultActionService actions,
        PinnedItemsManager pinned,
        ILaunchService launcher,
        IFileActionProvider fileActions)
    {
        Actions = actions ?? throw new ArgumentNullException(nameof(actions));
        Pinned = pinned ?? throw new ArgumentNullException(nameof(pinned));
        Launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        FileActions = fileActions ?? throw new ArgumentNullException(nameof(fileActions));
    }
}
