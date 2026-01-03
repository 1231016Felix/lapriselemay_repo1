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
                    StartProcess(item.Path);
                    break;
                    
                case ResultType.Folder:
                    StartProcess("explorer.exe", item.Path);
                    break;
                    
                case ResultType.Script:
                    LaunchScript(item);
                    break;
                    
                case ResultType.WebSearch:
                    StartProcess(item.Path);
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

    private static void LaunchScript(SearchResult item)
    {
        var ext = Path.GetExtension(item.Path).ToLowerInvariant();
        var workingDir = Path.GetDirectoryName(item.Path) ?? "";
        
        if (ext == ".ps1")
            StartProcess("powershell.exe", $"-ExecutionPolicy Bypass -File \"{item.Path}\"", workingDir);
        else
            StartProcess(item.Path, workingDirectory: workingDir);
    }
    
    public static void OpenContainingFolder(SearchResult item)
    {
        var folder = Path.GetDirectoryName(item.Path);
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            StartProcess("explorer.exe", $"/select,\"{item.Path}\"");
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
        catch (System.ComponentModel.Win32Exception)
        {
            // L'utilisateur a annulé l'élévation UAC
        }
    }
    
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
