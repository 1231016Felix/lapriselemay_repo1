using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

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
    OpenLocation,
    CopyPath,
    CopyName,
    
    // Actions fichiers
    Delete,
    Rename,
    Properties,
    
    // Actions applications
    RunAsAdmin,
    CreateShortcut,
    
    // Actions favoris
    CopyUrl,
    OpenPrivate,
    
    // Actions dossiers
    OpenInTerminal,
    OpenInExplorer,
    
    // Actions épingles et alias
    Pin,
    Unpin,
    CreateAlias
}

/// <summary>
/// Fournit les actions disponibles selon le type de résultat.
/// </summary>
public static class FileActionProvider
{
    /// <summary>
    /// Retourne les actions disponibles pour un type de résultat.
    /// </summary>
    public static List<FileAction> GetActionsForResult(SearchResult result)
    {
        return GetActionsForResult(result, isPinned: false);
    }
    
    /// <summary>
    /// Retourne les actions disponibles pour un type de résultat avec état d'épinglage.
    /// </summary>
    public static List<FileAction> GetActionsForResult(SearchResult result, bool isPinned)
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
            case ResultType.StoreApp:
                actions.AddRange(GetApplicationActions());
                break;
                
            case ResultType.File:
            case ResultType.Script:
                actions.AddRange(GetFileActions());
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
        }
        
        // Actions communes à tous les types (sauf WebSearch et Calculator)
        if (result.Type is not (ResultType.WebSearch or ResultType.Calculator or ResultType.SystemCommand or ResultType.SystemControl))
        {
            actions.AddRange(GetCommonActions());
            
            // Actions épingles et alias
            actions.AddRange(GetPinAndAliasActions(result, isPinned));
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
            Description = "Ouvrir le dossier contenant l'application",
            Shortcut = "Ctrl+O",
            ActionType = FileActionType.OpenLocation
        };
        
        yield return new FileAction
        {
            Name = "Créer un raccourci",
            Icon = "🔗",
            Description = "Créer un raccourci sur le bureau",
            ActionType = FileActionType.CreateShortcut
        };
    }
    
    private static IEnumerable<FileAction> GetFileActions()
    {
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
    
    private static IEnumerable<FileAction> GetFolderActions()
    {
        yield return new FileAction
        {
            Name = "Ouvrir dans l'Explorateur",
            Icon = "📁",
            Description = "Ouvrir le dossier dans l'Explorateur",
            ActionType = FileActionType.OpenInExplorer
        };
        
        yield return new FileAction
        {
            Name = "Ouvrir dans le Terminal",
            Icon = "⬛",
            Description = "Ouvrir une invite de commandes ici",
            Shortcut = "Ctrl+T",
            ActionType = FileActionType.OpenInTerminal
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
    
    private static IEnumerable<FileAction> GetCommonActions()
    {
        yield return new FileAction
        {
            Name = "Copier le chemin",
            Icon = "📋",
            Description = "Copier le chemin complet",
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
    }
    
    private static IEnumerable<FileAction> GetPinAndAliasActions(SearchResult result, bool isPinned)
    {
        // Action épingler/désépingler
        if (isPinned)
        {
            yield return new FileAction
            {
                Name = "Désépingler",
                Icon = "📌",
                Description = "Retirer des favoris épinglés",
                Shortcut = "Ctrl+P",
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
                Shortcut = "Ctrl+P",
                ActionType = FileActionType.Pin
            };
        }
        
        // Action créer un alias (seulement pour les apps et fichiers)
        if (result.Type is ResultType.Application or ResultType.StoreApp or ResultType.File or ResultType.Script or ResultType.Folder)
        {
            yield return new FileAction
            {
                Name = "Créer un alias",
                Icon = "⌨️",
                Description = "Créer un raccourci texte",
                ActionType = FileActionType.CreateAlias
            };
        }
    }
}

/// <summary>
/// Exécuteur d'actions sur les fichiers.
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
                FileActionType.OpenLocation => OpenLocation(path),
                FileActionType.CopyPath => CopyToClipboard(path),
                FileActionType.CopyName => CopyToClipboard(Path.GetFileName(path)),
                FileActionType.CopyUrl => CopyToClipboard(path),
                FileActionType.Delete => DeleteFile(path),
                FileActionType.Properties => ShowProperties(path),
                FileActionType.RunAsAdmin => RunAsAdmin(path),
                FileActionType.CreateShortcut => CreateShortcutOnDesktop(path),
                FileActionType.OpenPrivate => OpenInPrivateMode(path),
                FileActionType.OpenInTerminal => OpenInTerminal(path),
                FileActionType.OpenInExplorer => OpenInExplorer(path),
                FileActionType.Rename => false, // Géré par l'UI
                _ => false
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileAction] Erreur: {ex.Message}");
            return false;
        }
    }

    private static bool OpenFile(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
        return true;
    }

    private static bool OpenLocation(string path)
    {
        var folder = File.Exists(path) ? Path.GetDirectoryName(path) : path;
        if (string.IsNullOrEmpty(folder)) return false;
        
        // Ouvrir l'explorateur et sélectionner le fichier
        if (File.Exists(path))
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        else
        {
            Process.Start("explorer.exe", folder);
        }
        return true;
    }

    private static bool CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        
        Clipboard.SetText(text);
        return true;
    }

    private static bool DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            // Envoyer à la corbeille via l'API Shell
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

    private static bool ShowProperties(string path)
    {
        var info = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/e,/select,\"{path}\"",
            UseShellExecute = true
        };
        
        // Utiliser l'API Shell pour afficher les propriétés
        var sei = new NativeMethods.SHELLEXECUTEINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SHELLEXECUTEINFO>(),
            lpVerb = "properties",
            lpFile = path,
            nShow = 1, // SW_SHOWNORMAL
            fMask = 0x0000000C // SEE_MASK_INVOKEIDLIST
        };
        
        return NativeMethods.ShellExecuteEx(ref sei);
    }

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

    private static bool CreateShortcutOnDesktop(string path)
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var shortcutName = Path.GetFileNameWithoutExtension(path) + ".lnk";
        var shortcutPath = Path.Combine(desktopPath, shortcutName);
        
        // Utiliser IShellLink COM pour créer le raccourci
        var link = (IShellLink)new ShellLink();
        link.SetPath(path);
        link.SetWorkingDirectory(Path.GetDirectoryName(path) ?? "");
        
        var file = (IPersistFile)link;
        file.Save(shortcutPath, false);
        
        return true;
    }

    private static bool OpenInPrivateMode(string url)
    {
        // Déterminer le navigateur par défaut et ouvrir en mode privé
        try
        {
            // Essayer avec Edge
            Process.Start(new ProcessStartInfo
            {
                FileName = "msedge.exe",
                Arguments = $"--inprivate \"{url}\"",
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            try
            {
                // Fallback vers Chrome
                Process.Start(new ProcessStartInfo
                {
                    FileName = "chrome.exe",
                    Arguments = $"--incognito \"{url}\"",
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                // Fallback vers Firefox
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "firefox.exe",
                        Arguments = $"-private-window \"{url}\"",
                        UseShellExecute = true
                    });
                    return true;
                }
                catch
                {
                    // Ouvrir normalement si aucun navigateur n'est trouvé
                    return OpenFile(url);
                }
            }
        }
    }

    private static bool OpenInTerminal(string path)
    {
        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder)) return false;
        
        // Essayer Windows Terminal d'abord
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
            // Fallback vers cmd
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                WorkingDirectory = folder,
                UseShellExecute = true
            });
            return true;
        }
    }

    private static bool OpenInExplorer(string path)
    {
        Process.Start("explorer.exe", Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? "");
        return true;
    }
}

/// <summary>
/// Méthodes natives pour les propriétés de fichier.
/// </summary>
internal static class NativeMethods
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
        public string lpVerb;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
        public string lpFile;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
        public string? lpParameters;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    public static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);
}

/// <summary>
/// Interface COM IShellLink pour la création de raccourcis.
/// </summary>
[ComImport]
[Guid("000214F9-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLink
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
    void Resolve(IntPtr hwnd, int fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

/// <summary>
/// Interface COM IPersistFile pour la sauvegarde des raccourcis.
/// </summary>
[ComImport]
[Guid("0000010B-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPersistFile
{
    void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    void IsDirty();
    void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
    void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
}

/// <summary>
/// Classe COM ShellLink pour la création de raccourcis.
/// </summary>
[ComImport]
[Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLink { }
