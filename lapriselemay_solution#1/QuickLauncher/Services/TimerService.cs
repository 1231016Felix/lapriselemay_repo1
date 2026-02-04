using System.Diagnostics;
using System.Media;
using System.Windows;
using System.Windows.Threading;
using QuickLauncher.Views;

namespace QuickLauncher.Services;

/// <summary>
/// Service de gestion des minuteries avec notifications Windows.
/// </summary>
public sealed class TimerService : IDisposable
{
    private static TimerService? _instance;
    private static readonly object _lock = new();
    
    private readonly List<TimerItem> _activeTimers = [];
    private readonly DispatcherTimer _tickTimer;
    private int _nextId = 1;
    
    public static TimerService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new TimerService();
                }
            }
            return _instance;
        }
    }
    
    public event EventHandler<TimerCompletedEventArgs>? TimerCompleted;
    public event EventHandler? TimersChanged;
    
    private TimerService()
    {
        _tickTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _tickTimer.Tick += OnTick;
        _tickTimer.Start();
    }
    
    /// <summary>
    /// Crée une nouvelle minuterie.
    /// </summary>
    public TimerItem? CreateTimer(string duration, string? label = null)
    {
        var timeSpan = ParseDuration(duration);
        if (timeSpan == null || timeSpan.Value.TotalSeconds < 1)
            return null;
        
        var timer = new TimerItem
        {
            Id = _nextId++,
            Label = string.IsNullOrWhiteSpace(label) ? $"Minuterie {_nextId - 1}" : label.Trim(),
            Duration = timeSpan.Value,
            RemainingTime = timeSpan.Value,
            StartedAt = DateTime.Now,
            EndsAt = DateTime.Now.Add(timeSpan.Value)
        };
        
        _activeTimers.Add(timer);
        TimersChanged?.Invoke(this, EventArgs.Empty);
        
        Debug.WriteLine($"[Timer] Créé: {timer.Label} - {timer.Duration}");
        return timer;
    }
    
    /// <summary>
    /// Annule une minuterie.
    /// </summary>
    public bool CancelTimer(int id)
    {
        var timer = _activeTimers.FirstOrDefault(t => t.Id == id);
        if (timer != null)
        {
            _activeTimers.Remove(timer);
            TimersChanged?.Invoke(this, EventArgs.Empty);
            Debug.WriteLine($"[Timer] Annulé: {timer.Label}");
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Annule toutes les minuteries.
    /// </summary>
    public void CancelAll()
    {
        _activeTimers.Clear();
        TimersChanged?.Invoke(this, EventArgs.Empty);
        Debug.WriteLine("[Timer] Toutes les minuteries annulées");
    }
    
    /// <summary>
    /// Retourne la liste des minuteries actives.
    /// </summary>
    public IReadOnlyList<TimerItem> GetActiveTimers() => _activeTimers.AsReadOnly();
    
    /// <summary>
    /// Parse une durée au format "5m", "30s", "1h", "1h30m", "90" (secondes par défaut).
    /// </summary>
    public static TimeSpan? ParseDuration(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        
        input = input.Trim().ToLowerInvariant();
        
        // Format simple: nombre seul = secondes
        if (int.TryParse(input, out var seconds))
            return TimeSpan.FromSeconds(seconds);
        
        var totalSeconds = 0.0;
        var currentNumber = "";
        
        foreach (var c in input)
        {
            if (char.IsDigit(c) || c == '.' || c == ',')
            {
                currentNumber += c == ',' ? '.' : c;
            }
            else if (!string.IsNullOrEmpty(currentNumber))
            {
                if (double.TryParse(currentNumber, System.Globalization.NumberStyles.Any, 
                    System.Globalization.CultureInfo.InvariantCulture, out var num))
                {
                    totalSeconds += c switch
                    {
                        'h' => num * 3600,
                        'm' => num * 60,
                        's' => num,
                        _ => 0
                    };
                }
                currentNumber = "";
            }
        }
        
        // Si reste un nombre sans unité à la fin, considérer comme secondes
        if (!string.IsNullOrEmpty(currentNumber) && 
            double.TryParse(currentNumber, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var remaining))
        {
            totalSeconds += remaining;
        }
        
        return totalSeconds > 0 ? TimeSpan.FromSeconds(totalSeconds) : null;
    }
    
    /// <summary>
    /// Formate une durée pour l'affichage.
    /// </summary>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes:D2}m {duration.Seconds:D2}s";
        if (duration.TotalMinutes >= 1)
            return $"{(int)duration.TotalMinutes}m {duration.Seconds:D2}s";
        return $"{(int)duration.TotalSeconds}s";
    }
    
    private void OnTick(object? sender, EventArgs e)
    {
        var completedTimers = new List<TimerItem>();
        
        foreach (var timer in _activeTimers)
        {
            timer.RemainingTime = timer.EndsAt - DateTime.Now;
            
            if (timer.RemainingTime <= TimeSpan.Zero)
            {
                completedTimers.Add(timer);
            }
        }
        
        foreach (var timer in completedTimers)
        {
            _activeTimers.Remove(timer);
            OnTimerCompleted(timer);
        }
        
        if (completedTimers.Count > 0)
            TimersChanged?.Invoke(this, EventArgs.Empty);
    }
    
    private void OnTimerCompleted(TimerItem timer)
    {
        Debug.WriteLine($"[Timer] Terminé: {timer.Label}");
        
        // Afficher la fenêtre de notification personnalisée
        try
        {
            TimerNotificationWindow.ShowNotification(timer.Label);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Timer] Erreur notification: {ex.Message}");
            // Fallback: son système
            SystemSounds.Exclamation.Play();
        }
        
        TimerCompleted?.Invoke(this, new TimerCompletedEventArgs(timer));
    }
    
    public void Dispose()
    {
        _tickTimer.Stop();
        _activeTimers.Clear();
    }
}

/// <summary>
/// Représente une minuterie active.
/// </summary>
public class TimerItem
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public TimeSpan RemainingTime { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime EndsAt { get; set; }
    
    public string RemainingFormatted => TimerService.FormatDuration(RemainingTime > TimeSpan.Zero ? RemainingTime : TimeSpan.Zero);
    public string DurationFormatted => TimerService.FormatDuration(Duration);
    public double ProgressPercent => Duration.TotalSeconds > 0 
        ? Math.Max(0, Math.Min(100, (1 - RemainingTime.TotalSeconds / Duration.TotalSeconds) * 100))
        : 0;
}

/// <summary>
/// Arguments de l'événement de fin de minuterie.
/// </summary>
public class TimerCompletedEventArgs : EventArgs
{
    public TimerItem Timer { get; }
    public TimerCompletedEventArgs(TimerItem timer) => Timer = timer;
}
