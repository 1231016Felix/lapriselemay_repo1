using System.Diagnostics;
using System.IO;
using System.Security.Principal;

namespace TempCleaner.Services;

public static class AdminService
{
    /// <summary>
    /// Vérifie si l'application s'exécute avec des privilèges administrateur
    /// </summary>
    public static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Relance l'application en tant qu'administrateur
    /// </summary>
    public static bool RestartAsAdmin()
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName,
                UseShellExecute = true,
                Verb = "runas" // Demande l'élévation UAC
            };

            Process.Start(processInfo);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // L'utilisateur a annulé la demande UAC
            return false;
        }
    }

    /// <summary>
    /// Vérifie si un dossier nécessite des droits admin pour être nettoyé
    /// </summary>
    public static bool RequiresAdminAccess(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath))
            return false;

        var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

        // Dossiers qui nécessitent généralement des droits admin
        var adminPaths = new[]
        {
            Path.Combine(windowsPath, "SoftwareDistribution"),
            Path.Combine(windowsPath, "Prefetch"),
            Path.Combine(windowsPath, "Temp"),
            Path.Combine(windowsPath, "Logs"),
            Path.Combine(windowsPath, "Installer"),
            Path.Combine(windowsPath, "ServiceProfiles"),
            Path.Combine(systemDrive, "$Recycle.Bin"),
            Path.Combine(systemDrive, "Windows.old"),
            @"C:\$Recycle.Bin"
        };

        return adminPaths.Any(p => 
            folderPath.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}
