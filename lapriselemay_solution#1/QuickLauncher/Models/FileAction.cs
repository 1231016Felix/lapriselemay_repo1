using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

using Clipboard = System.Windows.Clipboard;

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
    
    /// <summary>
    /// Exécute l'action sur le chemin spécifié.
    /// </summary>
    public bool Execute(string path)
    {
        return FileActionExecutor.Execute(ActionType, path);
    }
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

/// <summary>
/// Abstraction pour le fournisseur d'actions sur les résultats de recherche.
/// Permet l'injection de dépendances et la testabilité (Point #6).
/// </summary>
public interface IFileActionProvider
{
    List<FileAction> GetActionsForResult(SearchResult result);
    List<FileAction> GetActionsForResult(SearchResult result, bool isPinned, bool hasAlias = false);
}

/// <summary>
/// Fournit les actions disponibles selon le type de résultat.
/// </summary>
public class FileActionProvider : IFileActionProvider
{
    /// <summary>
    /// Retourne les actions disponibles pour un type de résultat.
    /// </summary>
    public List<FileAction> GetActionsForResult(SearchResult result)
    {
        return GetActionsForResult(result, isPinned: false, hasAlias: false);
    }
    
    /// <summary>
    /// Retourne les actions disponibles pour un type de résultat avec état d'épinglage.
    /// </summary>
    public List<FileAction> GetActionsForResult(SearchResult result, bool isPinned, bool hasAlias = false)
    {
        var actions = new List<FileAction>();
        
        // Action principale toujours disponible
        actions.Add(new FileAction
        {
            Name = "Ouvrir",
            Icon = "▶️",
            Description = "Ouvrir l'élément",
            Shortcut = "Entrée",
            ActionType = FileActionType.Open
        });
        
        switch (result.Type)
        {
            case ResultType.Application:
                actions.AddRange(GetApplicationActions());
                break;
                
            case ResultType.StoreApp:
                actions.AddRange(GetStoreAppActions());
                break;
                
            case ResultType.File:
                actions.AddRange(GetFileActions());
                break;
                
            case ResultType.Script:
                actions.AddRange(GetScriptActions());
                break;
                
            case ResultType.Folder:
                actions.AddRange(GetFolderActions());
                break;
                
            case ResultType.Bookmark:
                actions.AddRange(GetBookmarkActions());
                break;
                
            case ResultType.WebSearch:
                actions.AddRange(GetWebSearchActions());
                break;
                
            case ResultType.Calculator:
                actions.AddRange(GetCalculatorActions());
                break;
        }
        
        // Actions épingles pour tous les types supportés
        if (result.Type is not (ResultType.WebSearch or ResultType.Calculator 
            or ResultType.SystemCommand or ResultType.SystemControl or ResultType.SearchHistory))
        {
            // Action alias
            actions.AddRange(GetAliasActions(hasAlias));
            
            actions.AddRange(GetPinActions(isPinned));
        }
        
        return actions;
    }
    
    private static IEnumerable<FileAction> GetApplicationActions()
    {
        yield return new FileAction
        {
            Name = "Exécuter en admin",
            Icon = "🛡️",
            Description = "Exécuter avec les droits administrateur",
            Shortcut = "Ctrl+Entrée",
            ActionType = FileActionType.RunAsAdmin
        };
        
        yield return new FileAction
        {
            Name = "Ouvrir l'emplacement",
            Icon = "📂",
            Description = "Ouvrir le dossier contenant le fichier",
            Shortcut = "Ctrl+O",
            ActionType = FileActionType.OpenLocation
        };
        
        yield return new FileAction
        {
            Name = "Copier le chemin",
            Icon = "📋",
            Description = "Copier le chemin complet dans le presse-papiers",
            Shortcut = "Ctrl+Maj+C",
            ActionType = FileActionType.CopyPath
        };
    }
    
    private static IEnumerable<FileAction> GetStoreAppActions()
    {
        return [];
    }
    
    private static IEnumerable<FileAction> GetFileActions()
    {
        yield return new FileAction
        {
            Name = "Ouvrir avec...",
            Icon = "📎",
            Description = "Choisir l'application pour ouvrir le fichier",
            ActionType = FileActionType.OpenWith
        };
        
        yield return new FileAction
        {
            Name = "Ouvrir l'emplacement",
            Icon = "📂",
            Description = "Ouvrir le dossier contenant le fichier",
            Shortcut = "Ctrl+O",
            ActionType = FileActionType.OpenLocation
        };
        
        yield return new FileAction
        {
            Name = "Copier le chemin",
            Icon = "📋",
            Description = "Copier le chemin complet dans le presse-papiers",
            Shortcut = "Ctrl+Maj+C",
            ActionType = FileActionType.CopyPath
        };
        
        yield return new FileAction
        {
            Name = "Copier le nom",
            Icon = "📝",
            Description = "Copier le nom du fichier",
            ActionType = FileActionType.CopyName
        };
        
        yield return new FileAction
        {
            Name = "Compresser (ZIP)",
            Icon = "🗜️",
            Description = "Créer une archive ZIP du fichier",
            ActionType = FileActionType.Compress
        };
        
        yield return new FileAction
        {
            Name = "Envoyer par email",
            Icon = "📧",
            Description = "Envoyer le fichier en pièce jointe",
            ActionType = FileActionType.SendByEmail
        };
        
        yield return new FileAction
        {
            Name = "Renommer",
            Icon = "✏️",
            Description = "Renommer le fichier",
            Shortcut = "F2",
            ActionType = FileActionType.Rename
        };
        
        yield return new FileAction
        {
            Name = "Supprimer",
            Icon = "🗑️",
            Description = "Envoyer à la corbeille",
            Shortcut = "Suppr",
            ActionType = FileActionType.Delete,
            RequiresConfirmation = true
        };
        
        yield return new FileAction
        {
            Name = "Propriétés",
            Icon = "ℹ️",
            Description = "Afficher les propriétés du fichier",
            ActionType = FileActionType.Properties
        };
    }
    
    private static IEnumerable<FileAction> GetScriptActions()
    {
        yield return new FileAction
        {
            Name = "Exécuter en admin",
            Icon = "🛡️",
            Description = "Exécuter avec les droits administrateur",
            Shortcut = "Ctrl+Entrée",
            ActionType = FileActionType.RunAsAdmin
        };
        
        yield return new FileAction
        {
            Name = "Éditer",
            Icon = "✏️",
            Description = "Ouvrir dans l'éditeur de texte par défaut",
            ActionType = FileActionType.EditInEditor
        };
        
        yield return new FileAction
        {
            Name = "Ouvrir l'emplacement",
            Icon = "📂",
            Description = "Ouvrir le dossier contenant le script",
            Shortcut = "Ctrl+O",
            ActionType = FileActionType.OpenLocation
        };
        
        yield return new FileAction
        {
            Name = "Copier le chemin",
            Icon = "📋",
            Description = "Copier le chemin complet dans le presse-papiers",
            Shortcut = "Ctrl+Maj+C",
            ActionType = FileActionType.CopyPath
        };
    }
    
    private static IEnumerable<FileAction> GetFolderActions()
    {
        yield return new FileAction
        {
            Name = "Ouvrir dans l'Explorateur",
            Icon = "📁",
            Description = "Ouvrir le dossier dans l'Explorateur Windows",
            ActionType = FileActionType.OpenInExplorer
        };
        
        yield return new FileAction
        {
            Name = "Ouvrir dans le Terminal",
            Icon = "⬛",
            Description = "Ouvrir un terminal dans ce dossier",
            Shortcut = "Ctrl+T",
            ActionType = FileActionType.OpenInTerminal
        };
        
        yield return new FileAction
        {
            Name = "Ouvrir dans VS Code",
            Icon = "💻",
            Description = "Ouvrir le dossier dans Visual Studio Code",
            ActionType = FileActionType.OpenInVSCode
        };
        
        yield return new FileAction
        {
            Name = "Copier le chemin",
            Icon = "📋",
            Description = "Copier le chemin complet dans le presse-papiers",
            Shortcut = "Ctrl+Maj+C",
            ActionType = FileActionType.CopyPath
        };
        
        yield return new FileAction
        {
            Name = "Compresser (ZIP)",
            Icon = "🗜️",
            Description = "Créer une archive ZIP du dossier",
            ActionType = FileActionType.Compress
        };
        
        yield return new FileAction
        {
            Name = "Renommer",
            Icon = "✏️",
            Description = "Renommer le dossier",
            Shortcut = "F2",
            ActionType = FileActionType.Rename
        };
        
        yield return new FileAction
        {
            Name = "Propriétés",
            Icon = "ℹ️",
            Description = "Afficher les propriétés du dossier",
            ActionType = FileActionType.Properties
        };
    }
    
    private static IEnumerable<FileAction> GetBookmarkActions()
    {
        yield return new FileAction
        {
            Name = "Ouvrir en privé",
            Icon = "🕶️",
            Description = "Ouvrir en navigation privée",
            Shortcut = "Ctrl+Maj+Entrée",
            ActionType = FileActionType.OpenPrivate
        };
        
        yield return new FileAction
        {
            Name = "Copier l'URL",
            Icon = "🔗",
            Description = "Copier l'adresse dans le presse-papiers",
            Shortcut = "Ctrl+C",
            ActionType = FileActionType.CopyUrl
        };
    }
    
    private static IEnumerable<FileAction> GetWebSearchActions()
    {
        yield return new FileAction
        {
            Name = "Ouvrir en privé",
            Icon = "🕶️",
            Description = "Rechercher en navigation privée",
            ActionType = FileActionType.OpenPrivate
        };
        
        yield return new FileAction
        {
            Name = "Copier l'URL",
            Icon = "🔗",
            Description = "Copier le lien de recherche",
            ActionType = FileActionType.CopyUrl
        };
    }
    
    private static IEnumerable<FileAction> GetCalculatorActions()
    {
        yield return new FileAction
        {
            Name = "Copier le résultat",
            Icon = "📋",
            Description = "Copier le résultat dans le presse-papiers",
            ActionType = FileActionType.CopyUrl // Réutilise la copie dans le clipboard
        };
    }
    
    private static IEnumerable<FileAction> GetAliasActions(bool hasAlias)
    {
        if (hasAlias)
        {
            yield return new FileAction
            {
                Name = "Supprimer l'alias",
                Icon = "⌨️",
                Description = "Retirer le raccourci texte de cet élément",
                ActionType = FileActionType.DeleteAlias
            };
        }
        else
        {
            yield return new FileAction
            {
                Name = "Créer un alias",
                Icon = "⌨️",
                Description = "Assigner un raccourci texte à cet élément",
                ActionType = FileActionType.CreateAlias
            };
        }
    }
    
    private static IEnumerable<FileAction> GetPinActions(bool isPinned)
    {
        if (isPinned)
        {
            yield return new FileAction
            {
                Name = "Désépingler",
                Icon = "📌",
                Description = "Retirer des favoris épinglés",
                ActionType = FileActionType.Unpin
            };
        }
        else
        {
            yield return new FileAction
            {
                Name = "Épingler",
                Icon = "⭐",
                Description = "Ajouter aux favoris épinglés",
                ActionType = FileActionType.Pin
            };
        }
    }
}

/// <summary>
/// Exécuteur d'actions sur les fichiers.
/// Chaque action est implémentée de manière fonctionnelle et robuste.
/// </summary>
public static class FileActionExecutor
{
    /// <summary>
    /// Exécute une action sur un chemin.
    /// </summary>
    public static bool Execute(FileActionType actionType, string path)
    {
        try
        {
            return actionType switch
            {
                FileActionType.Open => OpenFile(path),
                FileActionType.OpenWith => OpenWith(path),
                FileActionType.OpenLocation => OpenLocation(path),
                FileActionType.CopyPath => CopyToClipboard(path),
                FileActionType.CopyName => CopyNameToClipboard(path),
                FileActionType.CopyUrl => CopyToClipboard(path),
                FileActionType.Compress => CompressToZip(path),
                FileActionType.SendByEmail => SendByEmail(path),
                FileActionType.Delete => DeleteFile(path),
                FileActionType.Properties => ShowProperties(path),
                FileActionType.RunAsAdmin => RunAsAdmin(path),
                FileActionType.OpenPrivate => OpenInPrivateMode(path),
                FileActionType.OpenInTerminal => OpenInTerminal(path),
                FileActionType.OpenInExplorer => OpenInExplorer(path),
                FileActionType.OpenInVSCode => OpenInVSCode(path),
                FileActionType.EditInEditor => EditInEditor(path),
                FileActionType.Rename => false, // Géré par l'UI
                FileActionType.Pin => false,    // Géré par le ViewModel
                FileActionType.Unpin => false,  // Géré par le ViewModel
                FileActionType.CreateAlias => false,  // Géré par l'UI
                FileActionType.DeleteAlias => false,  // Géré par le ViewModel
                _ => false
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileAction] Erreur {actionType}: {ex.Message}");
            return false;
        }
    }

    #region Ouverture

    private static bool OpenFile(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
        return true;
    }
    
    /// <summary>
    /// Ouvre le dialogue "Ouvrir avec..." de Windows.
    /// </summary>
    private static bool OpenWith(string path)
    {
        if (!File.Exists(path)) return false;
        
        Process.Start(new ProcessStartInfo
        {
            FileName = "rundll32.exe",
            Arguments = $"shell32.dll,OpenAs_RunDLL \"{path}\"",
            UseShellExecute = false
        });
        return true;
    }
    
    /// <summary>
    /// Ouvre l'explorateur Windows avec le fichier sélectionné.
    /// </summary>
    private static bool OpenLocation(string path)
    {
        if (File.Exists(path))
        {
            // Sélectionner le fichier dans l'explorateur
            Process.Start("explorer.exe", $"/select,\"{path}\"");
            return true;
        }
        
        if (Directory.Exists(path))
        {
            // Ouvrir le dossier parent et sélectionner le dossier
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
                return true;
            }
            // Fallback: ouvrir le dossier lui-même
            Process.Start("explorer.exe", $"\"{path}\"");
            return true;
        }
        
        // Pour les raccourcis .lnk, essayer d'ouvrir le dossier contenant le .lnk
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
            return true;
        }
        
        return false;
    }

    #endregion

    #region Presse-papiers

    private static bool CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        Clipboard.SetText(text);
        return true;
    }
    
    private static bool CopyNameToClipboard(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        
        var name = File.Exists(path) || Directory.Exists(path)
            ? Path.GetFileName(path)
            : path; // Pour les StoreApps ou autres, copier tel quel
        
        if (string.IsNullOrEmpty(name)) return false;
        Clipboard.SetText(name);
        return true;
    }

    #endregion

    #region Opérations fichier

    /// <summary>
    /// Compresse un fichier ou dossier en archive ZIP dans le même répertoire.
    /// </summary>
    private static bool CompressToZip(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return false;
        
        var baseName = Path.GetFileNameWithoutExtension(path);
        var parentDir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parentDir)) return false;
        
        // Générer un nom de fichier ZIP unique
        var zipPath = Path.Combine(parentDir, $"{baseName}.zip");
        var counter = 1;
        while (File.Exists(zipPath))
        {
            zipPath = Path.Combine(parentDir, $"{baseName} ({counter}).zip");
            counter++;
        }
        
        if (File.Exists(path))
        {
            // Compresser un fichier unique
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            archive.CreateEntryFromFile(path, Path.GetFileName(path), CompressionLevel.Optimal);
        }
        else if (Directory.Exists(path))
        {
            // Compresser un dossier entier
            ZipFile.CreateFromDirectory(path, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);
        }
        
        // Ouvrir l'explorateur sur le ZIP créé
        Process.Start("explorer.exe", $"/select,\"{zipPath}\"");
        return true;
    }
    
    /// <summary>
    /// Envoie un fichier par email via le client mail par défaut (mailto: avec pièce jointe via Shell).
    /// </summary>
    private static bool SendByEmail(string path)
    {
        if (!File.Exists(path)) return false;
        
        // Utiliser le verbe Shell "sendto" qui utilise le client mail par défaut
        // C'est la méthode la plus fiable sous Windows
        try
        {
            // Méthode 1: MAPI via rundll32 (fonctionne avec Outlook, Thunderbird, etc.)
            var psi = new ProcessStartInfo
            {
                FileName = "rundll32.exe",
                Arguments = $"shell32.dll,ShellExec_RunDLL ?subject=&body=&attach=\"{path}\"",
                UseShellExecute = false
            };
            Process.Start(psi);
            return true;
        }
        catch
        {
            try
            {
                // Méthode 2: Fallback vers mailto (sans pièce jointe mais ouvre le client)
                var fileName = Path.GetFileName(path);
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"mailto:?subject={Uri.EscapeDataString(fileName)}&body={Uri.EscapeDataString($"Voir pièce jointe: {fileName}")}",
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
    
    private static bool DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            return true;
        }
        
        if (Directory.Exists(path))
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// Affiche les propriétés du fichier via l'API Shell native.
    /// </summary>
    private static bool ShowProperties(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return false;
        
        var sei = new NativeMethods.SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.SHELLEXECUTEINFO>(),
            lpVerb = "properties",
            lpFile = path,
            nShow = 1, // SW_SHOWNORMAL
            fMask = 0x0000000C // SEE_MASK_INVOKEIDLIST
        };
        
        return NativeMethods.ShellExecuteEx(ref sei);
    }

    #endregion

    #region Exécution

    private static bool RunAsAdmin(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
            Verb = "runas"
        });
        return true;
    }

    /// <summary>
    /// Ouvre une URL en mode navigation privée.
    /// Détecte le navigateur par défaut via le registre Windows.
    /// </summary>
    private static bool OpenInPrivateMode(string url)
    {
        var defaultBrowser = GetDefaultBrowserExecutable();
        
        if (!string.IsNullOrEmpty(defaultBrowser))
        {
            var browserName = Path.GetFileNameWithoutExtension(defaultBrowser).ToLowerInvariant();
            var privateArg = browserName switch
            {
                "chrome" or "chromium" => "--incognito",
                "msedge" => "--inprivate",
                "firefox" => "-private-window",
                "brave" => "--incognito",
                "vivaldi" => "--incognito",
                "opera" => "--private",
                _ => "--inprivate" // Fallback Edge-like
            };
            
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = defaultBrowser,
                    Arguments = $"{privateArg} \"{url}\"",
                    UseShellExecute = true
                });
                return true;
            }
            catch { /* Fallback ci-dessous */ }
        }
        
        // Fallback: essayer Edge > Chrome > Firefox dans l'ordre
        string[] browsers = ["msedge.exe", "chrome.exe", "firefox.exe"];
        string[] args = ["--inprivate", "--incognito", "-private-window"];
        
        for (int i = 0; i < browsers.Length; i++)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = browsers[i],
                    Arguments = $"{args[i]} \"{url}\"",
                    UseShellExecute = true
                });
                return true;
            }
            catch { continue; }
        }
        
        // Dernier recours: ouvrir normalement
        return OpenFile(url);
    }
    
    /// <summary>
    /// Détecte l'exécutable du navigateur par défaut via le registre Windows.
    /// </summary>
    private static string? GetDefaultBrowserExecutable()
    {
        try
        {
            // Windows 10/11: UserChoice dans le registre
            using var userChoice = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice");
            
            var progId = userChoice?.GetValue("ProgId") as string;
            if (string.IsNullOrEmpty(progId)) return null;
            
            using var command = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
            var commandLine = command?.GetValue(null) as string;
            if (string.IsNullOrEmpty(commandLine)) return null;
            
            // Extraire le chemin de l'exécutable
            if (commandLine.StartsWith('"'))
            {
                var endQuote = commandLine.IndexOf('"', 1);
                if (endQuote > 0)
                    return commandLine[1..endQuote];
            }
            
            return commandLine.Split(' ')[0];
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Terminal / Éditeurs

    /// <summary>
    /// Ouvre un terminal dans le dossier spécifié.
    /// Essaie Windows Terminal, puis PowerShell, puis cmd.
    /// </summary>
    private static bool OpenInTerminal(string path)
    {
        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;
        
        // Essayer Windows Terminal
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "wt.exe",
                Arguments = $"-d \"{folder}\"",
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            // Fallback vers PowerShell
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    WorkingDirectory = folder,
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                // Dernier recours: cmd
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    WorkingDirectory = folder,
                    UseShellExecute = true
                });
                return true;
            }
        }
    }

    /// <summary>
    /// Ouvre le dossier dans l'Explorateur Windows.
    /// </summary>
    private static bool OpenInExplorer(string path)
    {
        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder)) return false;
        
        Process.Start("explorer.exe", $"\"{folder}\"");
        return true;
    }
    
    /// <summary>
    /// Ouvre le dossier dans Visual Studio Code.
    /// Essaie 'code' (PATH), puis les chemins d'installation courants.
    /// </summary>
    private static bool OpenInVSCode(string path)
    {
        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder)) return false;
        
        // Essayer via le PATH (la méthode la plus courante)
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "code",
                Arguments = $"\"{folder}\"",
                UseShellExecute = true
            });
            return true;
        }
        catch { /* Pas dans le PATH */ }
        
        // Essayer les chemins d'installation classiques
        string[] possiblePaths =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Microsoft VS Code", "Code.exe"),
            @"C:\Program Files\Microsoft VS Code\Code.exe",
            @"C:\Program Files (x86)\Microsoft VS Code\Code.exe"
        ];
        
        foreach (var codePath in possiblePaths)
        {
            if (!File.Exists(codePath)) continue;
            
            Process.Start(new ProcessStartInfo
            {
                FileName = codePath,
                Arguments = $"\"{folder}\"",
                UseShellExecute = true
            });
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Ouvre un fichier dans l'éditeur de texte par défaut.
    /// Essaie VS Code, puis Notepad++, puis Notepad.
    /// </summary>
    private static bool EditInEditor(string path)
    {
        if (!File.Exists(path)) return false;
        
        // Essayer VS Code
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "code",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
            return true;
        }
        catch { /* Pas dans le PATH */ }
        
        // Essayer Notepad++
        string[] notepadPlusPaths =
        [
            @"C:\Program Files\Notepad++\notepad++.exe",
            @"C:\Program Files (x86)\Notepad++\notepad++.exe"
        ];
        
        foreach (var nppPath in notepadPlusPaths)
        {
            if (!File.Exists(nppPath)) continue;
            
            Process.Start(new ProcessStartInfo
            {
                FileName = nppPath,
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
            return true;
        }
        
        // Fallback: Notepad (toujours disponible)
        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
        return true;
    }

    #endregion
}

/// <summary>
/// Méthodes natives pour les propriétés de fichier.
/// </summary>
internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpFile;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);
}
