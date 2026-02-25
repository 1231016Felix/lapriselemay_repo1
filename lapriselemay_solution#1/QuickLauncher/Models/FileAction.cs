namespace QuickLauncher.Models;

/// <summary>
/// Action disponible sur un résultat de recherche.
/// </summary>
public sealed class FileAction
{
    public string Name { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Shortcut { get; init; } = string.Empty;
    public FileActionType ActionType { get; init; }
    public bool RequiresConfirmation { get; init; }
}

/// <summary>
/// Types d'actions disponibles.
/// </summary>
public enum FileActionType
{
    // Actions communes
    Open,
    
    // Actions fichiers
    Delete,
    Rename,
    Properties,
    OpenWith,
    OpenLocation,
    CopyPath,
    CopyName,
    Compress,
    SendByEmail,
    
    // Actions applications
    RunAsAdmin,
    
    // Actions favoris / web
    CopyUrl,
    OpenPrivate,
    
    // Actions dossiers
    OpenInTerminal,
    OpenInExplorer,
    OpenInVSCode,
    
    // Actions scripts
    EditInEditor,
    
    // Actions épingles
    Pin,
    Unpin,
    
    // Actions alias
    CreateAlias,
    DeleteAlias
}
