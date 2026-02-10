using QuickLauncher.Models;

namespace QuickLauncher.Services.CommandHandlers;

/// <summary>
/// Implémentation de <see cref="ISystemControlExecutor"/>.
/// Gère l'exécution des commandes système quand l'utilisateur valide (Entrée).
/// 
/// Découplé du ViewModel : ne connaît ni la fenêtre, ni le Dispatcher.
/// Retourne un résultat déclaratif que le ViewModel interprète.
/// </summary>
public sealed class SystemControlExecutor : ISystemControlExecutor
{
    private readonly ISettingsProvider _settingsProvider;
    private readonly NoteWidgetService _noteWidgetService;
    private readonly TimerWidgetService _timerWidgetService;
    
    private AppSettings Settings => _settingsProvider.Current;
    
    public SystemControlExecutor(
        ISettingsProvider settingsProvider,
        NoteWidgetService noteWidgetService,
        TimerWidgetService timerWidgetService)
    {
        _settingsProvider = settingsProvider;
        _noteWidgetService = noteWidgetService;
        _timerWidgetService = timerWidgetService;
    }
    
    public SystemControlExecutionResult Execute(string command)
    {
        if (string.IsNullOrEmpty(command))
            return SystemControlExecutionResult.NotHandled;
        
        // === Copier depuis :translate ===
        if (command.StartsWith(":translate:copy:"))
        {
            var text = command[":translate:copy:".Length..];
            return CopyToClipboard(text, "📋 Traduction copiée");
        }
        
        // === Copier depuis :ai ===
        if (command.StartsWith(":ai:copy:"))
        {
            var text = command[":ai:copy:".Length..];
            return CopyToClipboard(text, "📋 Réponse IA copiée");
        }
        
        // === Commandes configurables ===
        var parts = command.TrimStart(':').Split(' ', 2);
        var cmdPrefix = parts[0];
        var arg = parts.Length > 1 ? parts[1] : null;
        
        var matchedCmd = Settings.SystemCommands.FirstOrDefault(c =>
            c.IsEnabled && c.Prefix.Equals(cmdPrefix, StringComparison.OrdinalIgnoreCase));
        
        if (matchedCmd == null)
            return ExecuteNormalizedCommand(command);
        
        return matchedCmd.Type switch
        {
            SystemControlType.Weather or SystemControlType.Translate or SystemControlType.AiChat
                => HandleAsyncCommandExecution(matchedCmd, arg),
            
            SystemControlType.Timer => ExecuteTimer(arg),
            SystemControlType.Note => ExecuteNote(arg),
            SystemControlType.Screenshot => ExecuteScreenshot(arg),
            
            _ => ExecuteNormalizedCommand(command)
        };
    }
    
    /// <summary>
    /// Commandes async (météo, traduction, IA) : redirigent vers le CommandRouter.
    /// Si pas d'argument, autocomplete le préfixe dans la barre de recherche.
    /// </summary>
    private static SystemControlExecutionResult HandleAsyncCommandExecution(SystemControlCommand cmd, string? arg)
    {
        if (!string.IsNullOrEmpty(arg) || cmd.Type == SystemControlType.Weather)
        {
            // Le ViewModel doit re-router via CommandRouter (requête async)
            return SystemControlExecutionResult.NotHandled;
        }
        
        // Autocomplete : remplir la barre de recherche avec le préfixe
        return new SystemControlExecutionResult
        {
            Handled = true,
            AutoCompleteText = $":{cmd.Prefix} "
        };
    }
    
    private SystemControlExecutionResult ExecuteTimer(string? arg)
    {
        if (string.IsNullOrEmpty(arg))
            return SystemControlExecutionResult.NotHandled;
        
        var timerParts = arg.Split(' ', 2);
        var duration = timerParts[0];
        var label = timerParts.Length > 1 ? timerParts[1] : null;
        
        var timerWidget = _timerWidgetService.CreateWidget(duration, label);
        if (timerWidget != null)
        {
            var durationText = TimerWidgetService.FormatDuration(
                TimeSpan.FromSeconds(timerWidget.DurationSeconds));
            return new SystemControlExecutionResult
            {
                Handled = true,
                ShouldHide = true,
                ResultsToShow =
                [
                    new SearchResult
                    {
                        Name = $"⏱️ Minuterie créée: {durationText}",
                        Description = timerWidget.Label,
                        Type = ResultType.SystemControl,
                        DisplayIcon = "ℹ️"
                    }
                ]
            };
        }
        
        return new SystemControlExecutionResult
        {
            Handled = true,
            ResultsToShow =
            [
                new SearchResult
                {
                    Name = "❌ Format invalide",
                    Description = "Utilisez: 5m, 30s, 1h, 1h30m, etc.",
                    Type = ResultType.SystemControl,
                    DisplayIcon = "❌"
                }
            ]
        };
    }
    
    private SystemControlExecutionResult ExecuteNote(string? arg)
    {
        if (string.IsNullOrEmpty(arg))
            return SystemControlExecutionResult.NotHandled;
        
        var widgetInfo = _noteWidgetService.CreateWidget(arg);
        return new SystemControlExecutionResult
        {
            Handled = true,
            ShouldHide = true,
            ResultsToShow =
            [
                new SearchResult
                {
                    Name = "📝 Note créée!",
                    Description = widgetInfo.Content.Length > 50 ? widgetInfo.Content[..47] + "..." : widgetInfo.Content,
                    Type = ResultType.SystemControl,
                    DisplayIcon = "ℹ️"
                }
            ]
        };
    }
    
    private static SystemControlExecutionResult ExecuteScreenshot(string? arg)
    {
        if (arg is "snip" or "region" or "select")
        {
            return new SystemControlExecutionResult
            {
                Handled = true,
                ShouldHide = true,
                ScreenCaptureMode = arg
            };
        }
        
        // Capture plein écran — le ViewModel gère l'aspect async/UI
        return new SystemControlExecutionResult
        {
            Handled = true,
            ShouldHide = true,
            ScreenCaptureMode = "fullscreen"
        };
    }
    
    /// <summary>
    /// Normalise la commande depuis le préfixe personnalisé vers le format standard
    /// attendu par SystemControlService, puis exécute.
    /// </summary>
    private SystemControlExecutionResult ExecuteNormalizedCommand(string command)
    {
        var normalized = NormalizeSystemCommand(command);
        var result = SystemControlService.ExecuteCommand(normalized);
        
        if (result == null)
            return SystemControlExecutionResult.NotHandled;
        
        var results = new List<SearchResult>
        {
            new()
            {
                Name = result.Message,
                Description = result.Success ? "Commande exécutée" : "Erreur",
                Type = ResultType.SystemControl,
                DisplayIcon = result.Success ? "✅" : "❌"
            }
        };
        
        if (result.Success && !string.IsNullOrEmpty(result.FilePath))
        {
            results.Add(new SearchResult
            {
                Name = "Ouvrir la capture",
                Description = result.FilePath,
                Type = ResultType.File,
                Path = result.FilePath,
                DisplayIcon = "📂"
            });
        }
        
        // Certaines commandes ferment la fenêtre après exécution
        var commandLower = normalized.ToLowerInvariant();
        var shouldHide = result.Success && IsAutoHideCommand(commandLower);
        
        return new SystemControlExecutionResult
        {
            Handled = true,
            ShouldHide = shouldHide,
            ResultsToShow = results
        };
    }
    
    /// <summary>
    /// Convertit une commande avec préfixe personnalisé vers le format standard.
    /// </summary>
    internal string NormalizeSystemCommand(string command)
    {
        var parts = command.TrimStart(':').Split(' ', 2);
        var prefix = parts[0];
        var arg = parts.Length > 1 ? parts[1] : null;
        
        var matchedCmd = Settings.SystemCommands.FirstOrDefault(c =>
            c.IsEnabled && c.Prefix.Equals(prefix, StringComparison.OrdinalIgnoreCase));
        
        if (matchedCmd == null)
            return command;
        
        var standardCmd = matchedCmd.Type switch
        {
            SystemControlType.Volume => "volume",
            SystemControlType.Mute => "mute",
            SystemControlType.Brightness => "brightness",
            SystemControlType.Wifi => "wifi",
            SystemControlType.Lock => "lock",
            SystemControlType.Sleep => "sleep",
            SystemControlType.Hibernate => "hibernate",
            SystemControlType.Shutdown => "shutdown",
            SystemControlType.Restart => "restart",
            SystemControlType.Screenshot => "screenshot",
            SystemControlType.Logoff => "logoff",
            SystemControlType.EmptyRecycleBin => "emptybin",
            SystemControlType.OpenTaskManager => "taskmgr",
            SystemControlType.OpenWindowsSettings => "winsettings",
            SystemControlType.OpenControlPanel => "control",
            SystemControlType.EmptyTemp => "emptytemp",
            SystemControlType.OpenCmdAdmin => "cmd",
            SystemControlType.OpenPowerShellAdmin => "powershell",
            SystemControlType.RestartExplorer => "restartexplorer",
            SystemControlType.FlushDns => "flushdns",
            SystemControlType.OpenStartupFolder => "startup",
            SystemControlType.OpenHostsFile => "hosts",
            _ => prefix
        };
        
        return string.IsNullOrEmpty(arg) ? $":{standardCmd}" : $":{standardCmd} {arg}";
    }
    
    private static SystemControlExecutionResult CopyToClipboard(string text, string notification)
    {
        if (string.IsNullOrEmpty(text))
            return SystemControlExecutionResult.NotHandled;
        
        System.Windows.Clipboard.SetText(text);
        return new SystemControlExecutionResult
        {
            Handled = true,
            ShouldHide = true,
            Notification = notification
        };
    }
    
    private static bool IsAutoHideCommand(string normalizedCommand)
    {
        return normalizedCommand.Contains("sleep") || normalizedCommand.Contains("lock") ||
               normalizedCommand.Contains("shutdown") || normalizedCommand.Contains("restart") ||
               normalizedCommand.Contains("hibernate") || normalizedCommand.Contains("logoff") ||
               normalizedCommand.Contains("restartexplorer");
    }
}
