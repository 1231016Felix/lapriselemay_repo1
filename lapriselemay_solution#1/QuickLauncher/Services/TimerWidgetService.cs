using System.Text.RegularExpressions;
using System.Windows;
using QuickLauncher.Models;
using QuickLauncher.Views;

namespace QuickLauncher.Services;

/// <summary>
/// Service de gestion des widgets de minuterie sur le bureau.
/// </summary>
public sealed partial class TimerWidgetService
{
    private static readonly Lazy<TimerWidgetService> _instance = new(() => new TimerWidgetService());
    public static TimerWidgetService Instance => _instance.Value;
    
    private readonly Dictionary<int, TimerWidget> _activeWidgets = [];
    private readonly object _lock = new();
    private int _nextId = 1;
    
    private TimerWidgetService() { }
    
    /// <summary>
    /// Crée un nouveau widget de minuterie.
    /// </summary>
    public TimerWidgetInfo? CreateWidget(string duration, string? label = null)
    {
        var parsedDuration = ParseDuration(duration);
        if (parsedDuration == null) return null;
        
        var settings = AppSettings.Load();
        
        lock (_lock)
        {
            // Trouver le prochain ID disponible
            while (settings.TimerWidgets.Any(w => w.Id == _nextId) || _activeWidgets.ContainsKey(_nextId))
                _nextId++;
            
            var timerInfo = new TimerWidgetInfo
            {
                Id = _nextId++,
                Label = label ?? "Minuterie",
                DurationSeconds = (int)parsedDuration.Value.TotalSeconds,
                RemainingSeconds = (int)parsedDuration.Value.TotalSeconds,
                CreatedAt = DateTime.Now
            };
            
            // Calculer la position (décalage pour plusieurs widgets)
            var workArea = SystemParameters.WorkArea;
            var widgetIndex = _activeWidgets.Count;
            timerInfo.Left = workArea.Right - 180 - (widgetIndex * 35);
            timerInfo.Top = workArea.Bottom - 120 - (widgetIndex * 35);
            
            // Créer et afficher le widget
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var widget = new TimerWidget(
                    timerInfo.Id,
                    timerInfo.Label,
                    parsedDuration.Value,
                    OnWidgetClosed,
                    OnTimerCompleted
                );
                widget.SetPosition(timerInfo.Left, timerInfo.Top);
                widget.Show();
                
                _activeWidgets[timerInfo.Id] = widget;
            });
            
            // Sauvegarder
            settings.TimerWidgets.Add(timerInfo);
            settings.Save();
            
            return timerInfo;
        }
    }
    
    /// <summary>
    /// Callback quand un widget est fermé (annulé).
    /// </summary>
    private void OnWidgetClosed(int timerId)
    {
        lock (_lock)
        {
            _activeWidgets.Remove(timerId);
            
            var settings = AppSettings.Load();
            settings.TimerWidgets.RemoveAll(w => w.Id == timerId);
            settings.Save();
        }
    }
    
    /// <summary>
    /// Callback quand un timer est terminé.
    /// </summary>
    private void OnTimerCompleted(int timerId)
    {
        // Jouer un son de notification
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                System.Media.SystemSounds.Exclamation.Play();
            }
            catch { }
        });
        
        // Supprimer des settings (mais garder le widget visible)
        lock (_lock)
        {
            var settings = AppSettings.Load();
            settings.TimerWidgets.RemoveAll(w => w.Id == timerId);
            settings.Save();
        }
    }
    
    /// <summary>
    /// Sauvegarde la position d'un widget.
    /// </summary>
    public void SaveWidgetPosition(int timerId, double left, double top)
    {
        var settings = AppSettings.Load();
        var timer = settings.TimerWidgets.FirstOrDefault(w => w.Id == timerId);
        if (timer != null)
        {
            timer.Left = left;
            timer.Top = top;
            settings.Save();
        }
    }
    
    /// <summary>
    /// Restaure les widgets au démarrage de l'application.
    /// </summary>
    public void RestoreWidgets()
    {
        var settings = AppSettings.Load();
        var expiredTimers = new List<int>();
        
        foreach (var timerInfo in settings.TimerWidgets.ToList())
        {
            // Calculer le temps restant depuis la création
            var elapsed = DateTime.Now - timerInfo.CreatedAt;
            var remaining = TimeSpan.FromSeconds(timerInfo.DurationSeconds) - elapsed;
            
            if (remaining <= TimeSpan.Zero)
            {
                // Timer expiré pendant que l'app était fermée
                expiredTimers.Add(timerInfo.Id);
                continue;
            }
            
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var widget = new TimerWidget(
                    timerInfo.Id,
                    timerInfo.Label,
                    TimeSpan.FromSeconds(timerInfo.DurationSeconds),
                    OnWidgetClosed,
                    OnTimerCompleted
                );
                widget.SetPosition(timerInfo.Left, timerInfo.Top);
                widget.RestoreWithRemaining(remaining);
                widget.Show();
                
                lock (_lock)
                {
                    _activeWidgets[timerInfo.Id] = widget;
                    if (timerInfo.Id >= _nextId)
                        _nextId = timerInfo.Id + 1;
                }
            });
        }
        
        // Nettoyer les timers expirés
        if (expiredTimers.Count > 0)
        {
            settings.TimerWidgets.RemoveAll(w => expiredTimers.Contains(w.Id));
            settings.Save();
        }
    }
    
    /// <summary>
    /// Ferme tous les widgets.
    /// </summary>
    public void CloseAll()
    {
        lock (_lock)
        {
            foreach (var widget in _activeWidgets.Values.ToList())
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    try { widget.Close(); } catch { }
                });
            }
            _activeWidgets.Clear();
        }
    }
    
    /// <summary>
    /// Parse une durée au format "5m", "30s", "1h", "1h30m", etc.
    /// </summary>
    public static TimeSpan? ParseDuration(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        
        input = input.Trim().ToLowerInvariant();
        
        // Format simple: juste un nombre = minutes
        if (int.TryParse(input, out var justMinutes))
            return TimeSpan.FromMinutes(justMinutes);
        
        // Patterns: 5m, 30s, 1h, 1h30m, 1h30, etc.
        var regex = DurationRegex();
        var match = regex.Match(input);
        
        if (!match.Success) return null;
        
        var hours = 0;
        var minutes = 0;
        var seconds = 0;
        
        if (match.Groups["hours"].Success)
            hours = int.Parse(match.Groups["hours"].Value);
        if (match.Groups["minutes"].Success)
            minutes = int.Parse(match.Groups["minutes"].Value);
        if (match.Groups["seconds"].Success)
            seconds = int.Parse(match.Groups["seconds"].Value);
        
        // Cas spécial: "1h30" sans "m" = 1h30m
        if (match.Groups["hourmin"].Success)
            minutes = int.Parse(match.Groups["hourmin"].Value);
        
        var total = new TimeSpan(hours, minutes, seconds);
        return total > TimeSpan.Zero ? total : null;
    }
    
    /// <summary>
    /// Formate une durée pour l'affichage.
    /// </summary>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h{duration.Minutes:D2}m";
        if (duration.TotalMinutes >= 1)
            return $"{(int)duration.TotalMinutes}m{duration.Seconds:D2}s";
        return $"{duration.Seconds}s";
    }
    
    [GeneratedRegex(@"^(?:(?<hours>\d+)h)?(?:(?<minutes>\d+)m)?(?:(?<seconds>\d+)s)?(?:(?<hourmin>\d+)(?=\s|$))?$")]
    private static partial Regex DurationRegex();
}
