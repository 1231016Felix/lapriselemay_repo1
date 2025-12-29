using System.Diagnostics;
using System.IO;
using QuickLauncher.Models;

namespace QuickLauncher.Services;

public static class LaunchService
{
    public static void Launch(SearchResult item)
    {
        try
        {
            switch (item.Type)
            {
                case ResultType.Application:
                case ResultType.File:
                    Process.Start(new ProcessStartInfo { FileName = item.Path, UseShellExecute = true });
                    break;
                case ResultType.Folder:
                    Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = item.Path, UseShellExecute = true });
                    break;
                case ResultType.Script:
                    LaunchScript(item);
                    break;
                case ResultType.WebSearch:
                    Process.Start(new ProcessStartInfo { FileName = item.Path, UseShellExecute = true });
                    break;
                case ResultType.Calculator:
                    System.Windows.Clipboard.SetText(item.Path);
                    break;
                case ResultType.Command:
                    Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/c {item.Path}", UseShellExecute = true });
                    break;
            }
        }
        catch (Exception ex) { Debug.WriteLine($"Erreur lancement: {ex.Message}"); }
    }

    private static void LaunchScript(SearchResult item)
    {
        var ext = Path.GetExtension(item.Path).ToLowerInvariant();
        var psi = new ProcessStartInfo
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(item.Path) ?? ""
        };
        
        if (ext == ".ps1")
        {
            psi.FileName = "powershell.exe";
            psi.Arguments = $"-ExecutionPolicy Bypass -File \"{item.Path}\"";
        }
        else
        {
            psi.FileName = item.Path;
        }
        Process.Start(psi);
    }
    
    public static void OpenContainingFolder(SearchResult item)
    {
        var folder = Path.GetDirectoryName(item.Path);
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{item.Path}\"",
                UseShellExecute = true
            });
        }
    }
    
    public static void RunAsAdmin(SearchResult item)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = item.Path,
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
