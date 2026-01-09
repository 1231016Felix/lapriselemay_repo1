using System.Diagnostics;
using System.Security.Principal;
using CleanUninstaller.Models;
using Microsoft.Win32;
using TaskSchedulerLib = Microsoft.Win32.TaskScheduler;

namespace CleanUninstaller.Services;

/// <summary>
/// Service de gestion des programmes au démarrage
/// </summary>
public class StartupManagerService
{
    // Clés de registre pour les programmes au démarrage
    private const string RunKeyCurrentUser = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunKeyLocalMachine = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOnceKeyCurrentUser = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string RunOnceKeyLocalMachine = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string ApprovedRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedRunKeyLM = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";

    /// <summary>
    /// Scanne tous les programmes configurés pour démarrer automatiquement
    /// </summary>
    public async System.Threading.Tasks.Task<List<StartupProgram>> ScanStartupProgramsAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var programs = new List<StartupProgram>();

        await System.Threading.Tasks.Task.Run(() =>
        {
            progress?.Report(new ScanProgress(0, "Scan du registre utilisateur..."));
            
            // Registre - Utilisateur courant
            programs.AddRange(ScanRegistryRun(Registry.CurrentUser, RunKeyCurrentUser, StartupScope.CurrentUser));
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new ScanProgress(15, "Scan du registre machine..."));
            
            // Registre - Machine locale
            programs.AddRange(ScanRegistryRun(Registry.LocalMachine, RunKeyLocalMachine, StartupScope.AllUsers));
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new ScanProgress(30, "Scan des dossiers de démarrage..."));
            
            // Dossiers de démarrage
            programs.AddRange(ScanStartupFolder(StartupScope.CurrentUser));
            programs.AddRange(ScanStartupFolder(StartupScope.AllUsers));
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new ScanProgress(50, "Scan des tâches planifiées..."));
            
            // Tâches planifiées (au démarrage/logon)
            programs.AddRange(ScanScheduledTasks());
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new ScanProgress(70, "Lecture des états d'activation..."));
            
            // Lire les états d'activation depuis StartupApproved
            ApplyApprovedStates(programs);
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new ScanProgress(85, "Estimation de l'impact..."));
            
            // Estimer l'impact sur le démarrage
            EstimateImpact(programs);

            progress?.Report(new ScanProgress(100, "Scan terminé"));

        }, cancellationToken);

        return programs.OrderByDescending(p => p.Impact).ThenBy(p => p.Name).ToList();
    }

    /// <summary>
    /// Scanne une clé de registre Run
    /// </summary>
    private List<StartupProgram> ScanRegistryRun(RegistryKey root, string keyPath, StartupScope scope)
    {
        var programs = new List<StartupProgram>();

        try
        {
            using var key = root.OpenSubKey(keyPath);
            if (key == null) return programs;

            foreach (var valueName in key.GetValueNames())
            {
                try
                {
                    var value = key.GetValue(valueName)?.ToString();
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    var (command, arguments) = ParseCommand(value);

                    var program = new StartupProgram
                    {
                        Name = valueName,
                        Command = command,
                        Arguments = arguments,
                        Location = $"{root.Name}\\{keyPath}",
                        Type = Models.StartupType.Registry,
                        Scope = scope,
                        IsEnabled = true, // Par défaut, sera mis à jour par ApplyApprovedStates
                        RegistryKey = $"{root.Name}\\{keyPath}",
                        RegistryValueName = valueName,
                        FileExists = File.Exists(command)
                    };

                    // Récupérer les infos du fichier
                    if (program.FileExists)
                    {
                        try
                        {
                            var fileInfo = FileVersionInfo.GetVersionInfo(command);
                            program.Publisher = fileInfo.CompanyName ?? string.Empty;
                            program.Description = fileInfo.FileDescription ?? string.Empty;
                        }
                        catch { }
                    }

                    programs.Add(program);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erreur lecture valeur registre {valueName}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur lecture clé registre {keyPath}: {ex.Message}");
        }

        return programs;
    }

    /// <summary>
    /// Scanne le dossier de démarrage
    /// </summary>
    private List<StartupProgram> ScanStartupFolder(StartupScope scope)
    {
        var programs = new List<StartupProgram>();

        try
        {
            var folderPath = scope == StartupScope.CurrentUser
                ? Environment.GetFolderPath(Environment.SpecialFolder.Startup)
                : Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);

            if (!Directory.Exists(folderPath)) return programs;

            foreach (var file in Directory.GetFiles(folderPath))
            {
                try
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    string command, arguments = string.Empty;

                    if (ext == ".lnk")
                    {
                        // Résoudre le raccourci
                        var (targetPath, targetArgs) = ResolveShortcut(file);
                        command = targetPath;
                        arguments = targetArgs;
                    }
                    else
                    {
                        command = file;
                    }

                    var program = new StartupProgram
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        Command = command,
                        Arguments = arguments,
                        Location = folderPath,
                        Type = Models.StartupType.StartupFolder,
                        Scope = scope,
                        IsEnabled = true,
                        RegistryKey = file, // Utiliser le chemin du fichier comme identifiant
                        FileExists = File.Exists(command)
                    };

                    if (program.FileExists && !string.IsNullOrEmpty(command))
                    {
                        try
                        {
                            var fileInfo = FileVersionInfo.GetVersionInfo(command);
                            program.Publisher = fileInfo.CompanyName ?? string.Empty;
                            program.Description = fileInfo.FileDescription ?? string.Empty;
                        }
                        catch { }
                    }

                    programs.Add(program);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erreur lecture fichier startup {file}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur lecture dossier startup: {ex.Message}");
        }

        return programs;
    }

    /// <summary>
    /// Scanne les tâches planifiées qui s'exécutent au démarrage/logon
    /// </summary>
    private List<StartupProgram> ScanScheduledTasks()
    {
        var programs = new List<StartupProgram>();

        try
        {
            using var taskService = new TaskSchedulerLib.TaskService();
            ScanTaskFolder(taskService.RootFolder, programs);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur lecture tâches planifiées: {ex.Message}");
        }

        return programs;
    }

    private void ScanTaskFolder(TaskSchedulerLib.TaskFolder folder, List<StartupProgram> programs)
    {
        try
        {
            foreach (var task in folder.Tasks)
            {
                try
                {
                    // Vérifier que la définition existe
                    if (task?.Definition?.Triggers == null) continue;
                    
                    // Vérifier si la tâche a un déclencheur au démarrage ou à l'ouverture de session
                    var hasStartupTrigger = task.Definition.Triggers.Any(t =>
                        t.TriggerType == TaskSchedulerLib.TaskTriggerType.Boot ||
                        t.TriggerType == TaskSchedulerLib.TaskTriggerType.Logon);

                    if (!hasStartupTrigger) continue;

                    // Vérifier que les actions existent
                    if (task.Definition.Actions == null) continue;
                    
                    var action = task.Definition.Actions
                        .OfType<TaskSchedulerLib.ExecAction>()
                        .FirstOrDefault();

                    if (action == null) continue;

                    var command = action.Path?.Trim('"') ?? string.Empty;
                    
                    // Récupérer les infos de manière sécurisée
                    var scope = StartupScope.CurrentUser;
                    try
                    {
                        if (task.Definition.Principal?.RunLevel == TaskSchedulerLib.TaskRunLevel.Highest)
                            scope = StartupScope.System;
                    }
                    catch { }

                    string description = string.Empty;
                    string author = string.Empty;
                    try
                    {
                        description = task.Definition.RegistrationInfo?.Description ?? string.Empty;
                        author = task.Definition.RegistrationInfo?.Author ?? string.Empty;
                    }
                    catch { }

                    var program = new StartupProgram
                    {
                        Name = task.Name ?? "Tâche inconnue",
                        Command = command,
                        Arguments = action.Arguments ?? string.Empty,
                        Location = task.Path ?? string.Empty,
                        Type = Models.StartupType.ScheduledTask,
                        Scope = scope,
                        IsEnabled = task.Enabled,
                        RegistryKey = task.Path ?? string.Empty,
                        Description = description,
                        Publisher = author,
                        FileExists = !string.IsNullOrEmpty(command) && File.Exists(command)
                    };

                    programs.Add(program);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erreur lecture tâche: {ex.Message}");
                }
            }

            // Scanner les sous-dossiers
            try
            {
                foreach (var subFolder in folder.SubFolders)
                {
                    ScanTaskFolder(subFolder, programs);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur accès sous-dossiers: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur scan dossier tâches {folder.Path}: {ex.Message}");
        }
    }

    /// <summary>
    /// Applique les états d'activation depuis StartupApproved
    /// </summary>
    private void ApplyApprovedStates(List<StartupProgram> programs)
    {
        try
        {
            // HKCU
            using var approvedKey = Registry.CurrentUser.OpenSubKey(ApprovedRunKey);
            if (approvedKey != null)
            {
                foreach (var program in programs.Where(p => p.Scope == StartupScope.CurrentUser && p.Type == Models.StartupType.Registry))
                {
                    var value = approvedKey.GetValue(program.RegistryValueName) as byte[];
                    if (value != null && value.Length >= 12)
                    {
                        // Les 4 premiers bytes indiquent l'état: 02 = désactivé, 00/06 = activé
                        program.IsEnabled = value[0] != 0x03;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur lecture StartupApproved: {ex.Message}");
        }
    }

    /// <summary>
    /// Estime l'impact sur le démarrage basé sur les données système
    /// </summary>
    private void EstimateImpact(List<StartupProgram> programs)
    {
        foreach (var program in programs)
        {
            // Essayer de lire les données de performance depuis le registre
            var impact = GetStartupImpactFromSystem(program);
            
            if (impact == StartupImpact.NotMeasured)
            {
                // Estimer basé sur des heuristiques
                impact = EstimateImpactHeuristic(program);
            }

            program.Impact = impact;
            program.EstimatedImpactMs = GetEstimatedMs(impact);
        }
    }

    private StartupImpact GetStartupImpactFromSystem(StartupProgram program)
    {
        try
        {
            // Windows stocke les données de performance dans le registre
            var keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Diagnostics\DiagTrack\StartupInfo";
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            
            // Cette clé n'est pas toujours accessible, donc on retourne NotMeasured
            return StartupImpact.NotMeasured;
        }
        catch
        {
            return StartupImpact.NotMeasured;
        }
    }

    private StartupImpact EstimateImpactHeuristic(StartupProgram program)
    {
        if (!program.FileExists)
            return StartupImpact.None;

        try
        {
            var fileInfo = new FileInfo(program.Command);
            var sizeMb = fileInfo.Length / (1024.0 * 1024.0);

            // Heuristiques basées sur la taille et le type
            var publisherLower = program.Publisher.ToLowerInvariant();
            var nameLower = program.Name.ToLowerInvariant();

            // Programmes connus pour être lourds
            if (nameLower.Contains("onedrive") || nameLower.Contains("teams") ||
                nameLower.Contains("spotify") || nameLower.Contains("discord") ||
                nameLower.Contains("steam") || nameLower.Contains("epic"))
                return StartupImpact.High;

            // Programmes de sécurité
            if (nameLower.Contains("antivirus") || nameLower.Contains("security") ||
                nameLower.Contains("defender") || nameLower.Contains("norton") ||
                nameLower.Contains("mcafee") || nameLower.Contains("kaspersky"))
                return StartupImpact.High;

            // Services système légers
            if (publisherLower.Contains("microsoft") && program.Type == Models.StartupType.ScheduledTask)
                return StartupImpact.Low;

            // Basé sur la taille
            if (sizeMb > 100) return StartupImpact.High;
            if (sizeMb > 50) return StartupImpact.Medium;
            if (sizeMb > 10) return StartupImpact.Low;

            return StartupImpact.Low;
        }
        catch
        {
            return StartupImpact.NotMeasured;
        }
    }

    private int GetEstimatedMs(StartupImpact impact) => impact switch
    {
        StartupImpact.None => 0,
        StartupImpact.Low => 500,
        StartupImpact.Medium => 1500,
        StartupImpact.High => 3000,
        StartupImpact.Critical => 5000,
        _ => 0
    };

    /// <summary>
    /// Active ou désactive un programme au démarrage
    /// </summary>
    public async System.Threading.Tasks.Task<bool> SetStartupEnabledAsync(StartupProgram program, bool enabled)
    {
        return await System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                switch (program.Type)
                {
                    case Models.StartupType.Registry:
                        return SetRegistryStartupEnabled(program, enabled);

                    case Models.StartupType.StartupFolder:
                        return SetStartupFolderEnabled(program, enabled);

                    case Models.StartupType.ScheduledTask:
                        return SetScheduledTaskEnabled(program, enabled);

                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur modification startup {program.Name}: {ex.Message}");
                return false;
            }
        });
    }

    private bool SetRegistryStartupEnabled(StartupProgram program, bool enabled)
    {
        try
        {
            var rootKey = program.Scope == StartupScope.CurrentUser
                ? Registry.CurrentUser
                : Registry.LocalMachine;

            var approvedKeyPath = program.Scope == StartupScope.CurrentUser
                ? ApprovedRunKey
                : ApprovedRunKeyLM;

            using var approvedKey = rootKey.OpenSubKey(approvedKeyPath, writable: true);
            if (approvedKey == null)
            {
                // Créer la clé si elle n'existe pas
                using var newKey = rootKey.CreateSubKey(approvedKeyPath);
                if (newKey == null) return false;
            }

            using var key = rootKey.OpenSubKey(approvedKeyPath, writable: true);
            if (key == null) return false;

            // Format du byte array pour StartupApproved:
            // Activé: 02 00 00 00 00 00 00 00 00 00 00 00
            // Désactivé: 03 00 00 00 XX XX XX XX XX XX XX XX (timestamp)
            byte[] value;
            if (enabled)
            {
                value = new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            }
            else
            {
                var timestamp = DateTime.Now.ToFileTime();
                var timestampBytes = BitConverter.GetBytes(timestamp);
                value = new byte[12];
                value[0] = 0x03;
                Array.Copy(timestampBytes, 0, value, 4, 8);
            }

            key.SetValue(program.RegistryValueName, value, RegistryValueKind.Binary);
            program.IsEnabled = enabled;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur SetRegistryStartupEnabled: {ex.Message}");
            return false;
        }
    }

    private bool SetStartupFolderEnabled(StartupProgram program, bool enabled)
    {
        try
        {
            var filePath = program.RegistryKey; // Chemin du fichier/raccourci

            if (enabled)
            {
                // Renommer .disabled -> original
                var disabledPath = filePath + ".disabled";
                if (File.Exists(disabledPath))
                {
                    File.Move(disabledPath, filePath);
                }
            }
            else
            {
                // Renommer original -> .disabled
                if (File.Exists(filePath))
                {
                    File.Move(filePath, filePath + ".disabled");
                    program.RegistryKey = filePath + ".disabled";
                }
            }

            program.IsEnabled = enabled;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur SetStartupFolderEnabled: {ex.Message}");
            return false;
        }
    }

    private bool SetScheduledTaskEnabled(StartupProgram program, bool enabled)
    {
        try
        {
            using var taskService = new TaskSchedulerLib.TaskService();
            var task = taskService.GetTask(program.RegistryKey);
            if (task == null) return false;

            task.Enabled = enabled;
            program.IsEnabled = enabled;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur SetScheduledTaskEnabled: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Supprime définitivement un programme du démarrage
    /// </summary>
    public async System.Threading.Tasks.Task<bool> RemoveFromStartupAsync(StartupProgram program)
    {
        return await System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                switch (program.Type)
                {
                    case Models.StartupType.Registry:
                        return RemoveRegistryStartup(program);

                    case Models.StartupType.StartupFolder:
                        return RemoveStartupFolderEntry(program);

                    case Models.StartupType.ScheduledTask:
                        return RemoveScheduledTask(program);

                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur suppression startup {program.Name}: {ex.Message}");
                return false;
            }
        });
    }

    private bool RemoveRegistryStartup(StartupProgram program)
    {
        try
        {
            var rootKey = program.Scope == StartupScope.CurrentUser
                ? Registry.CurrentUser
                : Registry.LocalMachine;

            var keyPath = program.Scope == StartupScope.CurrentUser
                ? RunKeyCurrentUser
                : RunKeyLocalMachine;

            using var key = rootKey.OpenSubKey(keyPath, writable: true);
            key?.DeleteValue(program.RegistryValueName, throwOnMissingValue: false);

            // Supprimer aussi de StartupApproved
            var approvedKeyPath = program.Scope == StartupScope.CurrentUser
                ? ApprovedRunKey
                : ApprovedRunKeyLM;

            using var approvedKey = rootKey.OpenSubKey(approvedKeyPath, writable: true);
            approvedKey?.DeleteValue(program.RegistryValueName, throwOnMissingValue: false);

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur RemoveRegistryStartup: {ex.Message}");
            return false;
        }
    }

    private bool RemoveStartupFolderEntry(StartupProgram program)
    {
        try
        {
            var filePath = program.RegistryKey;
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            // Vérifier aussi la version .disabled
            if (File.Exists(filePath + ".disabled"))
            {
                File.Delete(filePath + ".disabled");
            }
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur RemoveStartupFolderEntry: {ex.Message}");
            return false;
        }
    }

    private bool RemoveScheduledTask(StartupProgram program)
    {
        try
        {
            using var taskService = new TaskSchedulerLib.TaskService();
            taskService.RootFolder.DeleteTask(program.RegistryKey, exceptionOnNotExists: false);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur RemoveScheduledTask: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Parse une commande pour séparer l'exécutable des arguments
    /// </summary>
    private (string command, string arguments) ParseCommand(string fullCommand)
    {
        if (string.IsNullOrWhiteSpace(fullCommand))
            return (string.Empty, string.Empty);

        fullCommand = fullCommand.Trim();

        // Si la commande commence par des guillemets
        if (fullCommand.StartsWith('"'))
        {
            var endQuote = fullCommand.IndexOf('"', 1);
            if (endQuote > 0)
            {
                var command = fullCommand[1..endQuote];
                var arguments = fullCommand.Length > endQuote + 1
                    ? fullCommand[(endQuote + 1)..].Trim()
                    : string.Empty;
                return (command, arguments);
            }
        }

        // Chercher le premier espace qui sépare commande et arguments
        var spaceIndex = fullCommand.IndexOf(' ');
        if (spaceIndex > 0)
        {
            // Vérifier si c'est un chemin avec espaces
            var potentialPath = fullCommand;
            while (!File.Exists(potentialPath) && spaceIndex > 0)
            {
                potentialPath = fullCommand[..spaceIndex];
                spaceIndex = fullCommand.IndexOf(' ', spaceIndex + 1);
            }

            if (File.Exists(potentialPath))
            {
                var arguments = fullCommand.Length > potentialPath.Length
                    ? fullCommand[potentialPath.Length..].Trim()
                    : string.Empty;
                return (potentialPath, arguments);
            }
        }

        return (fullCommand, string.Empty);
    }

    /// <summary>
    /// Résout un raccourci .lnk pour obtenir sa cible
    /// </summary>
    private (string targetPath, string arguments) ResolveShortcut(string shortcutPath)
    {
        try
        {
            var shell = new IWshRuntimeLibrary.WshShell();
            var shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(shortcutPath);
            return (shortcut.TargetPath, shortcut.Arguments);
        }
        catch
        {
            return (shortcutPath, string.Empty);
        }
    }

    /// <summary>
    /// Calcule le temps de boot total estimé
    /// </summary>
    public int CalculateTotalBootImpact(IEnumerable<StartupProgram> programs)
    {
        return programs
            .Where(p => p.IsEnabled)
            .Sum(p => p.EstimatedImpactMs);
    }

    /// <summary>
    /// Vérifie si l'application s'exécute en tant qu'administrateur
    /// </summary>
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
