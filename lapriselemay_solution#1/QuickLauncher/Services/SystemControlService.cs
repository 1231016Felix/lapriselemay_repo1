using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NAudio.CoreAudioApi;

namespace QuickLauncher.Services;

/// <summary>
/// Service de contrôle système pour les commandes rapides.
/// Gère le volume, la luminosité, le WiFi, la mise en veille, le verrouillage et les captures d'écran.
/// </summary>
public static class SystemControlService
{
    #region Volume Control

    /// <summary>
    /// Définit le volume du système (0-100).
    /// </summary>
    public static bool SetVolume(int level)
    {
        try
        {
            level = Math.Clamp(level, 0, 100);
            
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = level / 100f;
            
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Obtient le volume actuel du système (0-100).
    /// </summary>
    public static int GetVolume()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return (int)(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Active ou désactive le mode muet.
    /// </summary>
    public static bool SetMute(bool mute)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.Mute = mute;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Bascule le mode muet.
    /// </summary>
    public static bool ToggleMute()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.Mute = !device.AudioEndpointVolume.Mute;
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Brightness Control

    /// <summary>
    /// Définit la luminosité de l'écran (0-100).
    /// Fonctionne uniquement sur les laptops ou moniteurs supportant DDC/CI.
    /// </summary>
    public static bool SetBrightness(int level)
    {
        try
        {
            level = Math.Clamp(level, 0, 100);
            
            // Utiliser WMI pour les laptops
            return SetBrightnessViaWmi(level);
        }
        catch
        {
            return false;
        }
    }

    private static bool SetBrightnessViaWmi(int level)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"(Get-WmiObject -Namespace root/WMI -Class WmiMonitorBrightnessMethods).WmiSetBrightness(1, {level})\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.Start();
            process.WaitForExit(2000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Obtient la luminosité actuelle de l'écran.
    /// </summary>
    public static int GetBrightness()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"(Get-WmiObject -Namespace root/WMI -Class WmiMonitorBrightness).CurrentBrightness\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(2000);
            
            return int.TryParse(output, out var brightness) ? brightness : -1;
        }
        catch
        {
            return -1;
        }
    }

    #endregion

    #region WiFi Control

    /// <summary>
    /// Active ou désactive le WiFi.
    /// </summary>
    public static bool SetWifi(bool enabled)
    {
        try
        {
            var action = enabled ? "enable" : "disable";
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"interface set interface \"Wi-Fi\" {action}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas"
                }
            };
            process.Start();
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Bascule l'état du WiFi.
    /// </summary>
    public static bool ToggleWifi()
    {
        var currentState = IsWifiEnabled();
        return SetWifi(!currentState);
    }

    /// <summary>
    /// Vérifie si le WiFi est activé.
    /// </summary>
    public static bool IsWifiEnabled()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "interface show interface \"Wi-Fi\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);
            
            return output.Contains("Connected") || output.Contains("Connecté") || 
                   (output.Contains("Enabled") || output.Contains("Activé"));
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region System Actions

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    [DllImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    [DllImport("shell32.dll")]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);
    
    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    /// <summary>
    /// Verrouille la session Windows.
    /// </summary>
    public static bool Lock()
    {
        try
        {
            return LockWorkStation();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Met l'ordinateur en veille.
    /// </summary>
    public static bool Sleep()
    {
        try
        {
            return SetSuspendState(false, false, false);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Met l'ordinateur en hibernation.
    /// </summary>
    public static bool Hibernate()
    {
        try
        {
            return SetSuspendState(true, false, false);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Arrête l'ordinateur.
    /// </summary>
    public static bool Shutdown(bool force = false)
    {
        try
        {
            var args = force ? "/s /f /t 0" : "/s /t 0";
            Process.Start("shutdown", args);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Redémarre l'ordinateur.
    /// </summary>
    public static bool Restart(bool force = false)
    {
        try
        {
            var args = force ? "/r /f /t 0" : "/r /t 0";
            Process.Start("shutdown", args);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Déconnecte la session utilisateur.
    /// </summary>
    public static bool Logoff(bool force = false)
    {
        try
        {
            var args = force ? "/l /f" : "/l";
            Process.Start("shutdown", args);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Vide la corbeille.
    /// </summary>
    public static bool EmptyRecycleBin()
    {
        try
        {
            var result = SHEmptyRecycleBin(IntPtr.Zero, null, 
                SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
            return result == 0 || result == -2147418113; // S_OK ou corbeille déjà vide
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ouvre le Gestionnaire des tâches.
    /// </summary>
    public static bool OpenTaskManager()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "taskmgr.exe",
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ouvre les Paramètres Windows.
    /// </summary>
    public static bool OpenWindowsSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:",
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ouvre le Panneau de configuration.
    /// </summary>
    public static bool OpenControlPanel()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "control.exe",
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Maintenance Actions

    /// <summary>
    /// Vide le dossier temporaire de l'utilisateur.
    /// Retourne le nombre de fichiers supprimés.
    /// </summary>
    public static (bool Success, int DeletedCount, int ErrorCount) EmptyTempFolder()
    {
        var deletedCount = 0;
        var errorCount = 0;
        
        try
        {
            var tempPath = Path.GetTempPath();
            var dirInfo = new DirectoryInfo(tempPath);
            
            // Supprimer les fichiers
            foreach (var file in dirInfo.GetFiles())
            {
                try
                {
                    file.Delete();
                    deletedCount++;
                }
                catch
                {
                    errorCount++;
                }
            }
            
            // Supprimer les dossiers
            foreach (var dir in dirInfo.GetDirectories())
            {
                try
                {
                    dir.Delete(true);
                    deletedCount++;
                }
                catch
                {
                    errorCount++;
                }
            }
            
            return (true, deletedCount, errorCount);
        }
        catch
        {
            return (false, deletedCount, errorCount);
        }
    }

    /// <summary>
    /// Ouvre l'invite de commandes en mode administrateur.
    /// </summary>
    public static bool OpenCmdAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = true,
                Verb = "runas"
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ouvre PowerShell en mode administrateur.
    /// </summary>
    public static bool OpenPowerShellAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = true,
                Verb = "runas"
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Redémarre l'Explorateur Windows.
    /// </summary>
    public static bool RestartExplorer()
    {
        try
        {
            // Tuer explorer.exe
            foreach (var process in Process.GetProcessesByName("explorer"))
            {
                process.Kill();
                process.WaitForExit(3000);
            }
            
            // Attendre un peu
            Thread.Sleep(500);
            
            // Redémarrer explorer.exe
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true
            });
            
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Vide le cache DNS.
    /// </summary>
    public static bool FlushDns()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ipconfig",
                    Arguments = "/flushdns",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                }
            };
            process.Start();
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Screenshot

    /// <summary>
    /// Prend une capture d'écran et la sauvegarde dans le dossier Images.
    /// </summary>
    public static string? TakeScreenshot()
    {
        try
        {
            var screenshotsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "Screenshots");
            
            Directory.CreateDirectory(screenshotsFolder);
            
            var fileName = $"Screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
            var filePath = Path.Combine(screenshotsFolder, fileName);
            
            // Capturer tous les écrans
            var bounds = Screen.AllScreens
                .Select(s => s.Bounds)
                .Aggregate(Rectangle.Union);
            
            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }
            
            bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
            return filePath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Prend une capture d'écran de l'écran principal uniquement.
    /// </summary>
    public static string? TakeScreenshotPrimary()
    {
        try
        {
            var screenshotsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "Screenshots");
            
            Directory.CreateDirectory(screenshotsFolder);
            
            var fileName = $"Screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
            var filePath = Path.Combine(screenshotsFolder, fileName);
            
            var bounds = Screen.PrimaryScreen!.Bounds;
            
            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }
            
            bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
            return filePath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Ouvre l'outil de capture Windows (Snipping Tool).
    /// </summary>
    public static bool OpenSnippingTool()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-screenclip:",
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            try
            {
                // Fallback vers l'ancien outil
                Process.Start("SnippingTool.exe");
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    #endregion

    #region Command Parser

    /// <summary>
    /// Résultat d'une commande système.
    /// </summary>
    public record SystemCommandResult(bool Success, string Message, string? FilePath = null);

    /// <summary>
    /// Parse et exécute une commande système.
    /// </summary>
    public static SystemCommandResult? ExecuteCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;
        
        var parts = command.Trim().ToLowerInvariant().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return null;
        
        var cmd = parts[0].TrimStart(':');
        var arg = parts.Length > 1 ? parts[1] : null;
        
        return cmd switch
        {
            "volume" or "vol" => HandleVolumeCommand(arg),
            "brightness" or "bright" => HandleBrightnessCommand(arg),
            "wifi" => HandleWifiCommand(arg),
            "sleep" => Sleep() 
                ? new SystemCommandResult(true, "Mise en veille...") 
                : new SystemCommandResult(false, "Échec de la mise en veille"),
            "lock" => Lock() 
                ? new SystemCommandResult(true, "Verrouillage...") 
                : new SystemCommandResult(false, "Échec du verrouillage"),
            "screenshot" or "screen" or "ss" => HandleScreenshotCommand(arg),
            "mute" => ToggleMute() 
                ? new SystemCommandResult(true, "Mode muet basculé") 
                : new SystemCommandResult(false, "Échec du basculement muet"),
            "shutdown" => Shutdown() 
                ? new SystemCommandResult(true, "Arrêt en cours...") 
                : new SystemCommandResult(false, "Échec de l'arrêt"),
            "restart" or "reboot" => Restart() 
                ? new SystemCommandResult(true, "Redémarrage en cours...") 
                : new SystemCommandResult(false, "Échec du redémarrage"),
            "hibernate" => Hibernate() 
                ? new SystemCommandResult(true, "Hibernation...") 
                : new SystemCommandResult(false, "Échec de l'hibernation"),
            "logoff" or "signout" => Logoff() 
                ? new SystemCommandResult(true, "Déconnexion...") 
                : new SystemCommandResult(false, "Échec de la déconnexion"),
            "emptybin" or "emptyrecyclebin" => EmptyRecycleBin() 
                ? new SystemCommandResult(true, "Corbeille vidée") 
                : new SystemCommandResult(false, "Échec du vidage de la corbeille"),
            "taskmgr" or "taskmanager" => OpenTaskManager() 
                ? new SystemCommandResult(true, "Gestionnaire des tâches ouvert") 
                : new SystemCommandResult(false, "Échec de l'ouverture"),
            "winsettings" or "settings" => OpenWindowsSettings() 
                ? new SystemCommandResult(true, "Paramètres Windows ouverts") 
                : new SystemCommandResult(false, "Échec de l'ouverture"),
            "control" or "controlpanel" => OpenControlPanel() 
                ? new SystemCommandResult(true, "Panneau de configuration ouvert") 
                : new SystemCommandResult(false, "Échec de l'ouverture"),
            "emptytemp" or "cleartemp" => HandleEmptyTempCommand(),
            "cmd" => OpenCmdAdmin() 
                ? new SystemCommandResult(true, "Invite de commandes ouvert (admin)") 
                : new SystemCommandResult(false, "Échec de l'ouverture (annulé ou refusé)"),
            "powershell" or "pwsh" => OpenPowerShellAdmin() 
                ? new SystemCommandResult(true, "PowerShell ouvert (admin)") 
                : new SystemCommandResult(false, "Échec de l'ouverture (annulé ou refusé)"),
            "restartexplorer" => RestartExplorer() 
                ? new SystemCommandResult(true, "Explorateur redémarré") 
                : new SystemCommandResult(false, "Échec du redémarrage de l'Explorateur"),
            "flushdns" or "cleardns" => FlushDns() 
                ? new SystemCommandResult(true, "Cache DNS vidé") 
                : new SystemCommandResult(false, "Échec du vidage du cache DNS"),
            _ => null
        };
    }

    private static SystemCommandResult HandleVolumeCommand(string? arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            var currentVol = GetVolume();
            return new SystemCommandResult(true, $"Volume actuel: {currentVol}%");
        }
        
        if (int.TryParse(arg, out var level))
        {
            var success = SetVolume(level);
            return success 
                ? new SystemCommandResult(true, $"Volume réglé à {Math.Clamp(level, 0, 100)}%")
                : new SystemCommandResult(false, "Échec du réglage du volume");
        }
        
        return arg switch
        {
            "up" or "+" => SetVolume(Math.Min(GetVolume() + 10, 100)) 
                ? new SystemCommandResult(true, $"Volume: {GetVolume()}%") 
                : new SystemCommandResult(false, "Échec"),
            "down" or "-" => SetVolume(Math.Max(GetVolume() - 10, 0)) 
                ? new SystemCommandResult(true, $"Volume: {GetVolume()}%") 
                : new SystemCommandResult(false, "Échec"),
            "mute" => SetMute(true) 
                ? new SystemCommandResult(true, "Son coupé") 
                : new SystemCommandResult(false, "Échec"),
            "unmute" => SetMute(false) 
                ? new SystemCommandResult(true, "Son rétabli") 
                : new SystemCommandResult(false, "Échec"),
            _ => new SystemCommandResult(false, "Argument invalide. Utilisez un nombre (0-100), up, down, mute ou unmute")
        };
    }

    private static SystemCommandResult HandleBrightnessCommand(string? arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            var currentBright = GetBrightness();
            return currentBright >= 0 
                ? new SystemCommandResult(true, $"Luminosité actuelle: {currentBright}%")
                : new SystemCommandResult(false, "Impossible de lire la luminosité");
        }
        
        if (int.TryParse(arg, out var level))
        {
            var success = SetBrightness(level);
            return success 
                ? new SystemCommandResult(true, $"Luminosité réglée à {Math.Clamp(level, 0, 100)}%")
                : new SystemCommandResult(false, "Échec du réglage (moniteur non supporté?)");
        }
        
        return new SystemCommandResult(false, "Argument invalide. Utilisez un nombre entre 0 et 100");
    }

    private static SystemCommandResult HandleWifiCommand(string? arg)
    {
        return arg?.ToLowerInvariant() switch
        {
            "on" or "enable" => SetWifi(true) 
                ? new SystemCommandResult(true, "WiFi activé") 
                : new SystemCommandResult(false, "Échec (droits admin requis?)"),
            "off" or "disable" => SetWifi(false) 
                ? new SystemCommandResult(true, "WiFi désactivé") 
                : new SystemCommandResult(false, "Échec (droits admin requis?)"),
            "toggle" or null => ToggleWifi() 
                ? new SystemCommandResult(true, "WiFi basculé") 
                : new SystemCommandResult(false, "Échec (droits admin requis?)"),
            "status" => new SystemCommandResult(true, IsWifiEnabled() ? "WiFi: Activé" : "WiFi: Désactivé"),
            _ => new SystemCommandResult(false, "Argument invalide. Utilisez on, off, toggle ou status")
        };
    }

    private static SystemCommandResult HandleScreenshotCommand(string? arg)
    {
        return arg?.ToLowerInvariant() switch
        {
            "snip" or "region" or "select" => OpenSnippingTool() 
                ? new SystemCommandResult(true, "Outil de capture ouvert") 
                : new SystemCommandResult(false, "Échec de l'ouverture"),
            "primary" or "main" => HandlePrimaryScreenshot(),
            _ => HandleFullScreenshot()
        };
    }

    private static SystemCommandResult HandleFullScreenshot()
    {
        var path = TakeScreenshot();
        return path != null 
            ? new SystemCommandResult(true, $"Capture sauvegardée", path)
            : new SystemCommandResult(false, "Échec de la capture");
    }

    private static SystemCommandResult HandlePrimaryScreenshot()
    {
        var path = TakeScreenshotPrimary();
        return path != null 
            ? new SystemCommandResult(true, $"Capture sauvegardée", path)
            : new SystemCommandResult(false, "Échec de la capture");
    }

    private static SystemCommandResult HandleEmptyTempCommand()
    {
        var (success, deleted, errors) = EmptyTempFolder();
        if (success)
        {
            var message = errors > 0 
                ? $"{deleted} éléments supprimés ({errors} ignorés - en cours d'utilisation)"
                : $"{deleted} éléments supprimés du dossier Temp";
            return new SystemCommandResult(true, message);
        }
        return new SystemCommandResult(false, "Échec du nettoyage du dossier Temp");
    }

    #endregion
}
