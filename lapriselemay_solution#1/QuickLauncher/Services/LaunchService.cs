using System;
using System.Diagnostics;
using System.IO;
using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Abstraction pour le lancement de fichiers, applications et commandes système.
/// Permet l'injection de dépendances et la testabilité (Point #6).
/// </summary>
public interface ILaunchService
{
    void Launch(SearchResult item);
    void OpenContainingFolder(SearchResult item);
    void RunAsAdmin(SearchResult item);
}

public class LaunchService : ILaunchService
{
    private readonly IStoreAppService _storeAppService;
    private readonly IShortcutHelper _shortcutHelper;

    // Injection des services par le constructeur
    public LaunchService(IStoreAppService storeAppService, IShortcutHelper shortcutHelper)
    {
        _storeAppService = storeAppService ?? throw new ArgumentNullException(nameof(storeAppService));
        _shortcutHelper = shortcutHelper ?? throw new ArgumentNullException(nameof(shortcutHelper));
    }

    public void Launch(SearchResult item)
    {
        try
        {
            switch (item.Type)
            {
                case ResultType.Application:
                case ResultType.File:
                    // Les apps issues de shell:AppsFolder sans chemin fichier
                    // (ex: AppUserModelId comme "Microsoft.VisualStudio.Installer")
                    // doivent être lancées via shell:AppsFolder
                    if (item.Type == ResultType.Application && !IsFileSystemPath(item.Path))
                    {
                        if (!_storeAppService.LaunchApp(item.Path))
                            LaunchApplication(item.Path);
                    }
                    else
                    {
                        LaunchApplication(item.Path);
                    }
                    break;

                case ResultType.StoreApp:
                    // Utiliser shell:AppsFolder pour toutes les apps de AppsFolder
                    if (!_storeAppService.LaunchApp(item.Path))
                    {
                        // Fallback: essayer de lancer directement
                        LaunchApplication(item.Path);
                    }
                    break;

                case ResultType.Folder:
                    StartProcess("explorer.exe", $"\"{item.Path}\"");
                    break;

                case ResultType.Script:
                    LaunchScript(item);
                    break;

                case ResultType.WebSearch:
                case ResultType.Bookmark:
                    StartProcess(item.Path);
                    break;

                case ResultType.SystemControl:
                case ResultType.AppControl:
                    LaunchSystemControl(item.Path);
                    break;

                case ResultType.Calculator:
                    System.Windows.Clipboard.SetText(item.Path);
                    break;

                case ResultType.Command:
                    StartProcess("cmd.exe", $"/c {item.Path}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur lancement: {ex.Message}");
        }
    }

    private void LaunchApplication(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();

        // Pour les fichiers .lnk, résoudre le raccourci si nécessaire
        if (ext == ".lnk")
        {
            var info = _shortcutHelper.ResolveShortcut(path);
            if (info != null && !string.IsNullOrEmpty(info.TargetPath))
            {
                // Vérifier si la cible existe
                if (File.Exists(info.TargetPath) || Directory.Exists(info.TargetPath))
                {
                    var workingDir = !string.IsNullOrEmpty(info.WorkingDirectory)
                        ? info.WorkingDirectory
                        : Path.GetDirectoryName(info.TargetPath);

                    if (!string.IsNullOrEmpty(info.Arguments))
                        StartProcess(info.TargetPath, info.Arguments, workingDir);
                    else
                        StartProcess(info.TargetPath, workingDirectory: workingDir);
                    return;
                }

                // La cible n'existe pas mais c'est peut-être une URL ou un protocole
                if (info.TargetPath.Contains("://") || info.TargetPath.StartsWith("steam:"))
                {
                    StartProcess(info.TargetPath);
                    return;
                }
            }
        }

        // Lancement direct (fonctionne pour .exe, .lnk avec UseShellExecute, URLs, etc.)
        StartProcess(path);
    }

    private void LaunchScript(SearchResult item)
    {
        var ext = Path.GetExtension(item.Path).ToLowerInvariant();
        var workingDir = Path.GetDirectoryName(item.Path) ?? "";

        if (ext == ".ps1")
            StartProcess("powershell.exe", $"-ExecutionPolicy Bypass -File \"{item.Path}\"", workingDir);
        else
            StartProcess(item.Path, workingDirectory: workingDir);
    }

    public void OpenContainingFolder(SearchResult item)
    {
        var folder = Path.GetDirectoryName(item.Path);
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            StartProcess("explorer.exe", $"/select,\"{item.Path}\"");
    }

    public void RunAsAdmin(SearchResult item)
    {
        try
        {
            var path = item.Path;
            var arguments = string.Empty;
            var workingDirectory = string.Empty;

            // 1. Gérer les applications du Windows Store (ex: PowerShell 7, Windows Terminal)
            // Elles n'ont pas de chemin fichier classique (AppUserModelId)
            if (!IsFileSystemPath(path) && (item.Type == ResultType.Application || item.Type == ResultType.StoreApp))
            {
                // L'ajout de "shell:AppsFolder\" permet à Windows d'accepter le verbe "runas" sur une App UWP
                path = $"shell:AppsFolder\\{path}";
            }
            // 2. Gérer les raccourcis classiques (.lnk)
            else if (Path.GetExtension(path).ToLowerInvariant() == ".lnk")
            {
                var info = _shortcutHelper.ResolveShortcut(path);
                if (info != null && !string.IsNullOrEmpty(info.TargetPath))
                {
                    if (File.Exists(info.TargetPath) || Directory.Exists(info.TargetPath))
                    {
                        path = info.TargetPath;
                        arguments = info.Arguments ?? string.Empty;
                        workingDirectory = !string.IsNullOrEmpty(info.WorkingDirectory)
                            ? info.WorkingDirectory
                            : Path.GetDirectoryName(info.TargetPath) ?? string.Empty;
                    }
                }
            }

            // 3. Configuration du processus d'élévation
            var psi = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "runas" // C'est ici que l'élévation UAC (Admin) est demandée
            };

            if (!string.IsNullOrEmpty(arguments))
                psi.Arguments = arguments;

            if (!string.IsNullOrEmpty(workingDirectory))
                psi.WorkingDirectory = workingDirectory;

            Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // Code 1223 = L'utilisateur a annulé l'élévation UAC (fenêtre "Voulez-vous autoriser..."), on ignore silencieusement
            Debug.WriteLine("Élévation UAC annulée par l'utilisateur.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur RunAsAdmin: {ex.Message}");
        }
    }

    /// <summary>
    /// Lance un item de paramètres Windows.
    /// Gère les URIs ms-settings:, les commandes control| et les .msc.
    /// </summary>
    private void LaunchSystemControl(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith(":weather:") || path.StartsWith(":timer:"))
            return;

        // Format "control|args" pour les applets du panneau de configuration
        if (path.StartsWith("control|"))
        {
            var args = path["control|".Length..];
            StartProcess("control.exe", string.IsNullOrEmpty(args) ? null : args);
            return;
        }

        // ms-settings: URIs, .msc, mstsc, etc. → lancement direct
        StartProcess(path);
    }

    /// <summary>
    /// Vérifie si un chemin ressemble à un chemin fichier Windows (ex: C:\...)
    /// </summary>
    private static bool IsFileSystemPath(string path)
        => path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');

    private static void StartProcess(string fileName, string? arguments = null, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = true
        };

        if (!string.IsNullOrEmpty(arguments))
            psi.Arguments = arguments;

        if (!string.IsNullOrEmpty(workingDirectory))
            psi.WorkingDirectory = workingDirectory;

        Process.Start(psi);
    }
}