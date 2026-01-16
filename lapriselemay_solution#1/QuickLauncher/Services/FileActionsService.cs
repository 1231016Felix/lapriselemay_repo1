using System.Diagnostics;
using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace QuickLauncher.Services;

/// <summary>
/// Service pour les actions rapides sur les fichiers.
/// </summary>
public static class FileActionsService
{
    #region Clipboard Actions

    /// <summary>
    /// Copie le chemin du fichier dans le presse-papiers.
    /// </summary>
    public static bool CopyPath(string path)
    {
        try
        {
            System.Windows.Clipboard.SetText(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Copie le nom du fichier dans le presse-papiers.
    /// </summary>
    public static bool CopyName(string path)
    {
        try
        {
            var name = Path.GetFileName(path);
            System.Windows.Clipboard.SetText(name);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Copie le dossier parent dans le presse-papiers.
    /// </summary>
    public static bool CopyFolder(string path)
    {
        try
        {
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                System.Windows.Clipboard.SetText(folder);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region File Operations

    /// <summary>
    /// Ouvre le dossier contenant le fichier dans l'Explorateur.
    /// </summary>
    public static bool OpenContainingFolder(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
                return true;
            }
            
            if (Directory.Exists(path))
            {
                Process.Start("explorer.exe", $"\"{path}\"");
                return true;
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ouvre les propriétés du fichier.
    /// </summary>
    public static bool ShowProperties(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return false;

            var info = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            };
            
            Process.Start(info);
            
            // Envoyer Alt+Enter pour ouvrir les propriétés
            System.Threading.Thread.Sleep(500);
            System.Windows.Forms.SendKeys.SendWait("%{ENTER}");
            
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Supprime le fichier vers la corbeille.
    /// </summary>
    public static bool DeleteToRecycleBin(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                return true;
            }
            
            if (Directory.Exists(path))
            {
                FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                return true;
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Supprime définitivement le fichier.
    /// </summary>
    public static bool DeletePermanently(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
            
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
                return true;
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Renomme un fichier ou dossier.
    /// </summary>
    public static bool Rename(string path, string newName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(newName))
                return false;

            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
                return false;

            var newPath = Path.Combine(directory, newName);
            
            if (File.Exists(path))
            {
                File.Move(path, newPath);
                return true;
            }
            
            if (Directory.Exists(path))
            {
                Directory.Move(path, newPath);
                return true;
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Déplace un fichier vers un nouveau dossier.
    /// </summary>
    public static bool MoveTo(string path, string destinationFolder)
    {
        try
        {
            if (string.IsNullOrEmpty(destinationFolder) || !Directory.Exists(destinationFolder))
                return false;

            var fileName = Path.GetFileName(path);
            var newPath = Path.Combine(destinationFolder, fileName);
            
            if (File.Exists(path))
            {
                File.Move(path, newPath, true);
                return true;
            }
            
            if (Directory.Exists(path))
            {
                Directory.Move(path, newPath);
                return true;
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Copie un fichier vers un nouveau dossier.
    /// </summary>
    public static bool CopyTo(string path, string destinationFolder)
    {
        try
        {
            if (string.IsNullOrEmpty(destinationFolder) || !Directory.Exists(destinationFolder))
                return false;

            var fileName = Path.GetFileName(path);
            var newPath = Path.Combine(destinationFolder, fileName);
            
            if (File.Exists(path))
            {
                File.Copy(path, newPath, true);
                return true;
            }
            
            if (Directory.Exists(path))
            {
                CopyDirectory(path, newPath);
                return true;
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Exécute en tant qu'administrateur.
    /// </summary>
    public static bool RunAsAdmin(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "runas"
            };
            
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ouvre avec une application spécifique.
    /// </summary>
    public static bool OpenWith(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "rundll32.exe",
                Arguments = $"shell32.dll,OpenAs_RunDLL \"{path}\"",
                UseShellExecute = false
            };
            
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ouvre une invite de commandes dans le dossier.
    /// </summary>
    public static bool OpenCommandPromptHere(string path)
    {
        try
        {
            var folder = File.Exists(path) ? Path.GetDirectoryName(path) : path;
            if (string.IsNullOrEmpty(folder))
                return false;

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                WorkingDirectory = folder,
                UseShellExecute = true
            };
            
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ouvre PowerShell dans le dossier.
    /// </summary>
    public static bool OpenPowerShellHere(string path)
    {
        try
        {
            var folder = File.Exists(path) ? Path.GetDirectoryName(path) : path;
            if (string.IsNullOrEmpty(folder))
                return false;

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = folder,
                UseShellExecute = true
            };
            
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ouvre Windows Terminal dans le dossier.
    /// </summary>
    public static bool OpenTerminalHere(string path)
    {
        try
        {
            var folder = File.Exists(path) ? Path.GetDirectoryName(path) : path;
            if (string.IsNullOrEmpty(folder))
                return false;

            var psi = new ProcessStartInfo
            {
                FileName = "wt.exe",
                Arguments = $"-d \"{folder}\"",
                UseShellExecute = true
            };
            
            Process.Start(psi);
            return true;
        }
        catch
        {
            // Fallback vers PowerShell si Windows Terminal n'est pas installé
            return OpenPowerShellHere(path);
        }
    }

    #endregion

    #region Helpers

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }
        
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir);
        }
    }

    /// <summary>
    /// Vérifie si le chemin est un fichier exécutable.
    /// </summary>
    public static bool IsExecutable(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".exe" or ".bat" or ".cmd" or ".ps1" or ".msi" or ".com";
    }

    /// <summary>
    /// Obtient des informations sur le fichier.
    /// </summary>
    public static FileInfoResult? GetFileInfo(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                return new FileInfoResult
                {
                    Name = fi.Name,
                    Path = fi.FullName,
                    Size = fi.Length,
                    SizeFormatted = FormatFileSize(fi.Length),
                    Created = fi.CreationTime,
                    Modified = fi.LastWriteTime,
                    IsReadOnly = fi.IsReadOnly,
                    Extension = fi.Extension
                };
            }
            
            if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                return new FileInfoResult
                {
                    Name = di.Name,
                    Path = di.FullName,
                    Size = -1,
                    SizeFormatted = "Dossier",
                    Created = di.CreationTime,
                    Modified = di.LastWriteTime,
                    IsReadOnly = false,
                    Extension = ""
                };
            }
            
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double size = bytes;
        
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        
        return $"{size:0.##} {sizes[order]}";
    }

    #endregion
}

/// <summary>
/// Informations sur un fichier.
/// </summary>
public class FileInfoResult
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public string SizeFormatted { get; set; } = string.Empty;
    public DateTime Created { get; set; }
    public DateTime Modified { get; set; }
    public bool IsReadOnly { get; set; }
    public string Extension { get; set; } = string.Empty;
}
