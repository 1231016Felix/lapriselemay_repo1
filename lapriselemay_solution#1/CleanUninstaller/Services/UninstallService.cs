using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using CleanUninstaller.Models;

namespace CleanUninstaller.Services;

/// <summary>
/// Service de désinstallation avancé inspiré de BCUninstaller
/// Supporte la désinstallation silencieuse, forcée et par lot
/// </summary>
public partial class UninstallService
{
    private readonly RegistryService _registryService;
    private readonly ResidualScannerService _residualScanner;

    // Patterns pour les arguments silencieux courants
    private static readonly Dictionary<string, string[]> SilentSwitches = new()
    {
        { "msiexec", ["/qn", "/norestart"] },
        { "inno", ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART"] },
        { "nsis", ["/S"] },
        { "installshield", ["/s", "/v\"/qn\""] },
        { "wise", ["/s"] },
        { "wix", ["/quiet", "/norestart"] },
        { "nullsoft", ["/S"] },
        { "qt", ["--silent"] },
        { "advanced", ["/SILENT", "/VERYSILENT"] }
    };

    public UninstallService()
    {
        _registryService = new RegistryService();
        _residualScanner = new ResidualScannerService();
    }

    /// <summary>
    /// Désinstalle un programme de manière standard
    /// </summary>
    public async Task<UninstallResult> UninstallAsync(
        InstalledProgram program,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new UninstallResult { ProgramName = program.DisplayName };

        try
        {
            progress?.Report(new ScanProgress(0, $"Préparation de la désinstallation de {program.DisplayName}..."));

            if (string.IsNullOrEmpty(program.UninstallString))
            {
                result.Success = false;
                result.ErrorMessage = "Aucune commande de désinstallation disponible";
                return result;
            }

            // Parser la commande de désinstallation
            var (executable, arguments) = ParseUninstallString(program.UninstallString);

            progress?.Report(new ScanProgress(10, "Lancement du désinstalleur..."));

            // Exécuter le désinstalleur
            var exitCode = await RunUninstallerAsync(executable, arguments, cancellationToken);

            progress?.Report(new ScanProgress(70, "Vérification de la désinstallation..."));

            // Vérifier si le programme a été désinstallé
            await Task.Delay(1000, cancellationToken); // Attendre que le registre soit mis à jour
            
            var stillExists = await CheckProgramExistsAsync(program);
            
            if (stillExists && exitCode != 0)
            {
                result.Success = false;
                result.ErrorMessage = $"La désinstallation a échoué (code: {exitCode})";
            }
            else
            {
                result.Success = true;
                
                // Scanner les résidus
                progress?.Report(new ScanProgress(80, "Recherche des résidus..."));
                result.Residuals = await _residualScanner.ScanAsync(program, progress, cancellationToken);
                result.ResidualCount = result.Residuals.Count;
                result.ResidualSize = result.Residuals.Sum(r => r.Size);
            }

            progress?.Report(new ScanProgress(100, "Désinstallation terminée"));
        }
        catch (OperationCanceledException)
        {
            // Propager l'annulation pour permettre la gestion en amont
            throw;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Désinstalle un programme en mode silencieux (sans interaction utilisateur)
    /// </summary>
    public async Task<UninstallResult> UninstallSilentAsync(
        InstalledProgram program,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new UninstallResult { ProgramName = program.DisplayName };

        try
        {
            progress?.Report(new ScanProgress(0, $"Désinstallation silencieuse de {program.DisplayName}..."));

            string executable;
            string arguments;

            // Utiliser QuietUninstallString si disponible
            if (!string.IsNullOrEmpty(program.QuietUninstallString))
            {
                (executable, arguments) = ParseUninstallString(program.QuietUninstallString);
            }
            else if (!string.IsNullOrEmpty(program.UninstallString))
            {
                (executable, arguments) = ParseUninstallString(program.UninstallString);
                
                // Ajouter les arguments silencieux appropriés
                arguments = AddSilentArguments(executable, arguments, program.UninstallString);
            }
            else
            {
                result.Success = false;
                result.ErrorMessage = "Aucune commande de désinstallation disponible";
                return result;
            }

            progress?.Report(new ScanProgress(20, "Exécution du désinstalleur silencieux..."));

            var exitCode = await RunUninstallerAsync(executable, arguments, cancellationToken, timeout: TimeSpan.FromMinutes(5));

            progress?.Report(new ScanProgress(80, "Vérification..."));

            await Task.Delay(1000, cancellationToken);
            var stillExists = await CheckProgramExistsAsync(program);

            result.Success = !stillExists || exitCode == 0;
            
            if (result.Success)
            {
                result.Residuals = await _residualScanner.ScanAsync(program, progress, cancellationToken);
                result.ResidualCount = result.Residuals.Count;
                result.ResidualSize = result.Residuals.Sum(r => r.Size);
            }
            else
            {
                result.ErrorMessage = $"Échec de la désinstallation silencieuse (code: {exitCode})";
            }

            progress?.Report(new ScanProgress(100, "Terminé"));
        }
        catch (OperationCanceledException)
        {
            // Propager l'annulation pour permettre la gestion en amont
            throw;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Désinstallation forcée - supprime les fichiers et le registre même si le désinstalleur échoue
    /// </summary>
    public async Task<UninstallResult> ForceUninstallAsync(
        InstalledProgram program,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new UninstallResult { ProgramName = program.DisplayName };
        var errors = new List<string>();

        try
        {
            progress?.Report(new ScanProgress(0, $"Désinstallation forcée de {program.DisplayName}..."));

            // 1. Tenter d'abord une désinstallation normale
            if (!string.IsNullOrEmpty(program.UninstallString))
            {
                progress?.Report(new ScanProgress(5, "Tentative de désinstallation standard..."));
                try
                {
                    var (exe, args) = ParseUninstallString(program.UninstallString);
                    await RunUninstallerAsync(exe, args, cancellationToken, timeout: TimeSpan.FromMinutes(2));
                }
                catch { /* Ignorer les erreurs, on va forcer */ }
            }

            // 2. Tuer les processus liés
            progress?.Report(new ScanProgress(15, "Arrêt des processus..."));
            await KillRelatedProcessesAsync(program);

            // 3. Supprimer le dossier d'installation
            progress?.Report(new ScanProgress(25, "Suppression des fichiers..."));
            if (!string.IsNullOrEmpty(program.InstallLocation) && Directory.Exists(program.InstallLocation))
            {
                try
                {
                    await DeleteDirectoryAsync(program.InstallLocation, cancellationToken);
                }
                catch (Exception ex)
                {
                    errors.Add($"Impossible de supprimer {program.InstallLocation}: {ex.Message}");
                }
            }

            // 4. Scanner et supprimer les résidus
            progress?.Report(new ScanProgress(40, "Recherche des résidus..."));
            var residuals = await _residualScanner.ScanAsync(program, progress, cancellationToken);

            progress?.Report(new ScanProgress(60, "Suppression des résidus..."));
            int cleaned = 0;
            foreach (var residual in residuals.Where(r => r.Confidence >= ConfidenceLevel.High))
            {
                try
                {
                    await DeleteResidualAsync(residual, cancellationToken);
                    residual.IsDeleted = true;
                    cleaned++;
                }
                catch (Exception ex)
                {
                    residual.ErrorMessage = ex.Message;
                    errors.Add($"Résidu non supprimé: {residual.Path}");
                }

                var pct = 60 + (cleaned * 30 / Math.Max(1, residuals.Count));
                progress?.Report(new ScanProgress(pct, $"Nettoyage {cleaned}/{residuals.Count}..."));
            }

            // 5. Supprimer l'entrée du registre
            progress?.Report(new ScanProgress(95, "Nettoyage du registre..."));
            try
            {
                RemoveRegistryEntry(program);
            }
            catch (Exception ex)
            {
                errors.Add($"Registre: {ex.Message}");
            }

            result.Success = true;
            result.Residuals = residuals.Where(r => !r.IsDeleted).ToList();
            result.ResidualCount = result.Residuals.Count;
            result.ResidualSize = result.Residuals.Sum(r => r.Size);

            if (errors.Count > 0)
            {
                result.ErrorMessage = string.Join("; ", errors);
            }

            progress?.Report(new ScanProgress(100, "Désinstallation forcée terminée"));
        }
        catch (OperationCanceledException)
        {
            // Propager l'annulation pour permettre la gestion en amont
            throw;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Désinstalle plusieurs programmes en lot
    /// </summary>
    public async Task<List<UninstallResult>> BatchUninstallAsync(
        IEnumerable<InstalledProgram> programs,
        bool silent = true,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<UninstallResult>();
        var programList = programs.ToList();
        int current = 0;

        foreach (var program in programList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pct = current * 100 / programList.Count;
            progress?.Report(new ScanProgress(pct, $"Désinstallation de {program.DisplayName} ({current + 1}/{programList.Count})..."));

            var result = silent
                ? await UninstallSilentAsync(program, null, cancellationToken)
                : await UninstallAsync(program, null, cancellationToken);

            results.Add(result);
            current++;
        }

        progress?.Report(new ScanProgress(100, $"{results.Count(r => r.Success)}/{programList.Count} programmes désinstallés"));

        return results;
    }

    #region Private Methods

    private static (string executable, string arguments) ParseUninstallString(string uninstallString)
    {
        uninstallString = uninstallString.Trim();

        // Cas MSI
        if (uninstallString.Contains("MsiExec", StringComparison.OrdinalIgnoreCase))
        {
            return ("msiexec.exe", ExtractMsiArguments(uninstallString));
        }

        // Chemin entre guillemets
        if (uninstallString.StartsWith('"'))
        {
            var endQuote = uninstallString.IndexOf('"', 1);
            if (endQuote > 0)
            {
                var exe = uninstallString[1..endQuote];
                var args = uninstallString.Length > endQuote + 1 
                    ? uninstallString[(endQuote + 1)..].Trim() 
                    : "";
                return (exe, args);
            }
        }

        // Chemin sans guillemets - trouver le .exe
        var exeMatch = ExePathRegex().Match(uninstallString);
        if (exeMatch.Success)
        {
            var exe = exeMatch.Value;
            var args = uninstallString[exeMatch.Length..].Trim();
            return (exe, args);
        }

        // Fallback
        var parts = uninstallString.Split(' ', 2);
        return (parts[0], parts.Length > 1 ? parts[1] : "");
    }

    private static string ExtractMsiArguments(string uninstallString)
    {
        // Extraire le GUID du produit
        var guidMatch = GuidRegex().Match(uninstallString);
        if (guidMatch.Success)
        {
            return $"/x{guidMatch.Value}";
        }
        
        // Retourner les arguments après msiexec
        var idx = uninstallString.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            return uninstallString[(idx + 7)..].Trim();
        }

        return uninstallString;
    }

    private static string AddSilentArguments(string executable, string arguments, string originalString)
    {
        var lowerExe = executable.ToLowerInvariant();
        var lowerArgs = arguments.ToLowerInvariant();
        var lowerOriginal = originalString.ToLowerInvariant();

        // Déjà silencieux ?
        if (lowerArgs.Contains("/s") || lowerArgs.Contains("/silent") || 
            lowerArgs.Contains("/quiet") || lowerArgs.Contains("/qn"))
        {
            return arguments;
        }

        // MSI
        if (lowerExe.Contains("msiexec") || lowerOriginal.Contains("msiexec"))
        {
            return arguments + " /qn /norestart";
        }

        // Inno Setup
        if (lowerOriginal.Contains("unins") || lowerOriginal.Contains("inno"))
        {
            return arguments + " /VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
        }

        // NSIS
        if (lowerOriginal.Contains("nsis") || File.Exists(Path.Combine(Path.GetDirectoryName(executable) ?? "", "uninst.exe")))
        {
            return arguments + " /S";
        }

        // InstallShield
        if (lowerOriginal.Contains("installshield") || lowerOriginal.Contains("{") && lowerOriginal.Contains("}"))
        {
            return arguments + " /s";
        }

        // Défaut - essayer les arguments communs
        return arguments + " /S /VERYSILENT /SILENT /quiet /qn";
    }

    private static async Task<int> RunUninstallerAsync(
        string executable, 
        string arguments, 
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromMinutes(10);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas" // Élévation admin
        };

        using var process = new Process { StartInfo = startInfo };
        
        try
        {
            process.Start();
            var mainPid = process.Id;
            
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout.Value);

            // Attendre la fin du processus principal
            await process.WaitForExitAsync(cts.Token);
            
            // Attendre un peu que les processus enfants se terminent aussi
            await Task.Delay(1000, cancellationToken);
            
            // Attendre que les processus liés au désinstalleur soient terminés
            await WaitForRelatedProcessesAsync(executable, mainPid, TimeSpan.FromMinutes(2), cancellationToken);
            
            return process.ExitCode;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout
            try { process.Kill(true); } catch { }
            return -1;
        }
    }

    private static async Task WaitForRelatedProcessesAsync(
        string executable, 
        int parentPid, 
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var exeName = Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
        var startTime = DateTime.UtcNow;
        
        // Noms de processus courants pour les désinstalleurs
        var uninstallerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "unins000", "unins001", "uninstall", "uninst", "setup", 
            "msiexec", "au_", "_iu14d2n", "_au_"
        };
        
        // Ajouter le nom de l'exécutable s'il est spécifique
        if (!string.IsNullOrEmpty(exeName) && exeName.Length > 3)
        {
            uninstallerNames.Add(exeName);
        }

        // Maximum 30 secondes d'attente pour les processus liés
        var maxWait = TimeSpan.FromSeconds(30);
        if (timeout > maxWait) timeout = maxWait;

        while (DateTime.UtcNow - startTime < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var stillRunning = false;
            
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    var name = proc.ProcessName.ToLowerInvariant();
                    // Vérifier si le nom contient un des patterns de désinstalleur
                    if (uninstallerNames.Any(n => name.Contains(n)))
                    {
                        // Ignorer msiexec si c'est le service principal (il tourne toujours)
                        if (name == "msiexec")
                        {
                            try
                            {
                                // Vérifier si c'est une instance avec des arguments (pas le service)
                                var cmdLine = proc.MainModule?.FileName;
                                if (string.IsNullOrEmpty(cmdLine))
                                {
                                    continue;
                                }
                            }
                            catch
                            {
                                continue; // Ignorer si on ne peut pas accéder
                            }
                        }
                        
                        stillRunning = true;
                        break;
                    }
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }
            
            if (!stillRunning)
            {
                break;
            }
            
            await Task.Delay(500, cancellationToken);
        }
    }

    private async Task<bool> CheckProgramExistsAsync(InstalledProgram program)
    {
        return await Task.Run(() =>
        {
            // Vérifier dans le registre
            var paths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var path in paths)
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key?.GetSubKeyNames().Contains(program.RegistryKeyName) == true)
                {
                    return true;
                }
            }

            using var userKey = Registry.CurrentUser.OpenSubKey(paths[0]);
            if (userKey?.GetSubKeyNames().Contains(program.RegistryKeyName) == true)
            {
                return true;
            }

            return false;
        });
    }

    private static async Task KillRelatedProcessesAsync(InstalledProgram program)
    {
        await Task.Run(() =>
        {
            if (string.IsNullOrEmpty(program.InstallLocation)) return;

            var processes = Process.GetProcesses();
            foreach (var proc in processes)
            {
                try
                {
                    var mainModule = proc.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(mainModule) && 
                        mainModule.StartsWith(program.InstallLocation, StringComparison.OrdinalIgnoreCase))
                    {
                        proc.Kill(true);
                    }
                }
                catch { /* Ignorer les erreurs d'accès */ }
                finally
                {
                    proc.Dispose();
                }
            }
        });
    }

    private static async Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            // Supprimer les attributs read-only
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch { }
            }

            // Supprimer les dossiers
            foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                .OrderByDescending(d => d.Length))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    Directory.Delete(dir, false);
                }
                catch { }
            }

            // Supprimer le dossier racine
            try
            {
                Directory.Delete(path, true);
            }
            catch { }
        }, cancellationToken);
    }

    private static async Task DeleteResidualAsync(ResidualItem residual, CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            switch (residual.Type)
            {
                case ResidualType.File:
                    ForceDeleteFile(residual.Path);
                    break;

                case ResidualType.Folder:
                    ForceDeleteDirectory(residual.Path);
                    break;

                case ResidualType.RegistryKey:
                    DeleteRegistryKey(residual.Path);
                    break;

                case ResidualType.RegistryValue:
                    DeleteRegistryValue(residual.Path);
                    break;

                case ResidualType.ScheduledTask:
                    DeleteScheduledTask(residual.Path);
                    break;

                case ResidualType.Service:
                    DeleteService(residual.Path);
                    break;

                case ResidualType.EnvironmentPath:
                    RemoveFromEnvironmentPath(residual.Path);
                    break;

                case ResidualType.EnvironmentVariable:
                    DeleteEnvironmentVariable(residual.Path);
                    break;

                case ResidualType.ComComponent:
                    DeleteComComponent(residual.Path);
                    break;

                case ResidualType.FileAssociation:
                    DeleteFileAssociation(residual.Path);
                    break;

                case ResidualType.StartupEntry:
                    DeleteStartupEntry(residual.Path);
                    break;

                case ResidualType.Firewall:
                    DeleteFirewallRule(residual.Path);
                    break;

                case ResidualType.Certificate:
                    // Les certificats nécessitent des privilèges spéciaux
                    break;
            }
        }, cancellationToken);
    }

    private static void ForceDeleteFile(string path)
    {
        if (!File.Exists(path)) return;

        try
        {
            // Retirer les attributs
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        catch (UnauthorizedAccessException)
        {
            // Essayer avec cmd /c del /f
            RunCommandSilent("cmd.exe", $"/c del /f /q \"{path}\"");
        }
        catch (IOException)
        {
            // Fichier verrouillé - essayer de le déverrouiller ou forcer
            RunCommandSilent("cmd.exe", $"/c del /f /q \"{path}\"");
        }
    }

    private static void ForceDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;

        try
        {
            // D'abord, supprimer les attributs read-only de tous les fichiers
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                catch { }
            }

            Directory.Delete(path, true);
        }
        catch
        {
            // Forcer avec rmdir
            RunCommandSilent("cmd.exe", $"/c rmdir /s /q \"{path}\"");
        }
    }

    private static void RemoveFromEnvironmentPath(string pathToRemove)
    {
        try
        {
            // Variable utilisateur
            var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
            var userPaths = userPath.Split(';').Where(p => !p.Equals(pathToRemove, StringComparison.OrdinalIgnoreCase)).ToArray();
            Environment.SetEnvironmentVariable("PATH", string.Join(";", userPaths), EnvironmentVariableTarget.User);
        }
        catch { }

        try
        {
            // Variable système
            var systemPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "";
            var systemPaths = systemPath.Split(';').Where(p => !p.Equals(pathToRemove, StringComparison.OrdinalIgnoreCase)).ToArray();
            Environment.SetEnvironmentVariable("PATH", string.Join(";", systemPaths), EnvironmentVariableTarget.Machine);
        }
        catch { }
    }

    private static void DeleteEnvironmentVariable(string variableName)
    {
        try
        {
            Environment.SetEnvironmentVariable(variableName, null, EnvironmentVariableTarget.User);
        }
        catch { }

        try
        {
            Environment.SetEnvironmentVariable(variableName, null, EnvironmentVariableTarget.Machine);
        }
        catch { }
    }

    private static void DeleteComComponent(string clsid)
    {
        try
        {
            Registry.ClassesRoot.DeleteSubKeyTree($"CLSID\\{clsid}", false);
        }
        catch { }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes\CLSID", true);
            key?.DeleteSubKeyTree(clsid, false);
        }
        catch { }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Classes\CLSID", true);
            key?.DeleteSubKeyTree(clsid, false);
        }
        catch { }
    }

    private static void DeleteFileAssociation(string extension)
    {
        try
        {
            Registry.ClassesRoot.DeleteSubKeyTree(extension, false);
        }
        catch { }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts", true);
            key?.DeleteSubKeyTree(extension, false);
        }
        catch { }
    }

    private static void DeleteStartupEntry(string path)
    {
        // path peut être une clé de registre ou un fichier dans Startup
        if (path.StartsWith("HKEY", StringComparison.OrdinalIgnoreCase) || 
            path.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase))
        {
            DeleteRegistryValue(path);
        }
        else if (File.Exists(path))
        {
            ForceDeleteFile(path);
        }
    }

    private static void DeleteFirewallRule(string ruleName)
    {
        RunCommandSilent("netsh.exe", $"advfirewall firewall delete rule name=\"{ruleName}\"");
    }

    private static void RunCommandSilent(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        using var proc = Process.Start(psi);
        proc?.WaitForExit(10000);
    }

    private static void DeleteRegistryKey(string path)
    {
        var parts = path.Split('\\', 2);
        if (parts.Length < 2) return;

        var root = parts[0].ToUpperInvariant() switch
        {
            "HKEY_LOCAL_MACHINE" or "HKLM" => Registry.LocalMachine,
            "HKEY_CURRENT_USER" or "HKCU" => Registry.CurrentUser,
            "HKEY_CLASSES_ROOT" or "HKCR" => Registry.ClassesRoot,
            _ => null
        };

        root?.DeleteSubKeyTree(parts[1], false);
    }

    private static void DeleteRegistryValue(string path)
    {
        var lastBackslash = path.LastIndexOf('\\');
        if (lastBackslash < 0) return;

        var keyPath = path[..lastBackslash];
        var valueName = path[(lastBackslash + 1)..];

        var parts = keyPath.Split('\\', 2);
        if (parts.Length < 2) return;

        var root = parts[0].ToUpperInvariant() switch
        {
            "HKEY_LOCAL_MACHINE" or "HKLM" => Registry.LocalMachine,
            "HKEY_CURRENT_USER" or "HKCU" => Registry.CurrentUser,
            _ => null
        };

        using var key = root?.OpenSubKey(parts[1], true);
        key?.DeleteValue(valueName, false);
    }

    private static void DeleteScheduledTask(string taskName)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/Delete /TN \"{taskName}\" /F",
            CreateNoWindow = true,
            UseShellExecute = false
        };
        
        using var proc = Process.Start(psi);
        proc?.WaitForExit(5000);
    }

    private static void DeleteService(string serviceName)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = $"delete \"{serviceName}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        };

        using var proc = Process.Start(psi);
        proc?.WaitForExit(5000);
    }

    private void RemoveRegistryEntry(InstalledProgram program)
    {
        var paths = new[]
        {
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", Registry.LocalMachine),
            (@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", Registry.LocalMachine),
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", Registry.CurrentUser)
        };

        foreach (var (path, root) in paths)
        {
            try
            {
                using var key = root.OpenSubKey(path, true);
                if (key?.GetSubKeyNames().Contains(program.RegistryKeyName) == true)
                {
                    key.DeleteSubKeyTree(program.RegistryKeyName, false);
                }
            }
            catch { }
        }
    }

    [GeneratedRegex(@"[A-Za-z]:\\[^""]+\.exe", RegexOptions.IgnoreCase)]
    private static partial Regex ExePathRegex();

    [GeneratedRegex(@"\{[A-Fa-f0-9\-]+\}")]
    private static partial Regex GuidRegex();

    #endregion

    #region Public API Methods

    /// <summary>
    /// Désinstalle un programme (méthode principale appelée par le ViewModel)
    /// </summary>
    public async Task<UninstallResult> UninstallProgramAsync(
        InstalledProgram program,
        bool silent = true,
        bool scanResiduals = true,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = silent
            ? await UninstallSilentAsync(program, progress, cancellationToken)
            : await UninstallAsync(program, progress, cancellationToken);

        if (result.Success && scanResiduals)
        {
            progress?.Report(new ScanProgress(85, "Scan des résidus..."));
            result.Residuals = await _residualScanner.ScanAsync(program, progress, cancellationToken);
            result.ResidualCount = result.Residuals.Count;
            result.ResidualSize = result.Residuals.Sum(r => r.Size);
        }

        return result;
    }

    /// <summary>
    /// Crée un point de restauration système
    /// </summary>
    public async Task<bool> CreateRestorePointAsync(string description)
    {
        try
        {
            return await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"Checkpoint-Computer -Description '{description}' -RestorePointType 'APPLICATION_UNINSTALL'\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                process?.WaitForExit(60000); // 60 secondes max
                return process?.ExitCode == 0;
            });
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Nettoie les résidus sélectionnés
    /// </summary>
    public async Task<CleanupResult> CleanupResidualsAsync(
        IEnumerable<ResidualItem> residuals,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new CleanupResult();
        var selectedResiduals = residuals.Where(r => r.IsSelected && !r.IsDeleted).ToList();
        var total = selectedResiduals.Count;
        var current = 0;

        foreach (var residual in selectedResiduals)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await DeleteResidualAsync(residual, cancellationToken);
                residual.IsDeleted = true;
                result.DeletedCount++;
                result.SpaceFreed += residual.Size;
            }
            catch (Exception ex)
            {
                residual.ErrorMessage = ex.Message;
                result.FailedCount++;
                result.Errors.Add(new CleanupError
                {
                    Item = residual,
                    Message = ex.Message,
                    Exception = ex
                });
            }

            current++;
            progress?.Report(new ScanProgress(
                current * 100 / Math.Max(1, total),
                $"Nettoyage {current}/{total}..."));
        }

        return result;
    }

    #endregion
}
