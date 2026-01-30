using System.Windows.Forms;
using WallpaperManager.Widgets.Base;

namespace WallpaperManager.Widgets.Battery;

public class BatteryWidgetViewModel : WidgetViewModelBase
{
    protected override int RefreshIntervalSeconds => 30;
    
    private int _batteryPercent;
    public int BatteryPercent
    {
        get => _batteryPercent;
        set => SetProperty(ref _batteryPercent, value);
    }
    
    private bool _isCharging;
    public bool IsCharging
    {
        get => _isCharging;
        set => SetProperty(ref _isCharging, value);
    }
    
    private bool _isPluggedIn;
    public bool IsPluggedIn
    {
        get => _isPluggedIn;
        set => SetProperty(ref _isPluggedIn, value);
    }
    
    private string _timeRemaining = "";
    public string TimeRemaining
    {
        get => _timeRemaining;
        set => SetProperty(ref _timeRemaining, value);
    }
    
    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }
    
    private bool _hasBattery = true;
    public bool HasBattery
    {
        get => _hasBattery;
        set => SetProperty(ref _hasBattery, value);
    }
    
    public string BatteryIcon => IsCharging ? "🔌" : BatteryPercent switch
    {
        >= 80 => "🔋",
        >= 50 => "🔋",
        >= 20 => "🪫",
        _ => "🪫"
    };
    
    public string BatteryColor => BatteryPercent switch
    {
        >= 50 => "#10B981", // Vert
        >= 20 => "#F59E0B", // Orange
        _ => "#EF4444"      // Rouge
    };
    
    public double BatteryBarWidth => BatteryPercent; // Pour la barre de progression (0-100)
    
    public override Task RefreshAsync()
    {
        try
        {
            var powerStatus = SystemInformation.PowerStatus;
            
            // Vérifier si une batterie est présente
            if (powerStatus.BatteryChargeStatus == BatteryChargeStatus.NoSystemBattery)
            {
                HasBattery = false;
                StatusText = "Aucune batterie détectée";
                return Task.CompletedTask;
            }
            
            HasBattery = true;
            
            // Pourcentage
            BatteryPercent = (int)(powerStatus.BatteryLifePercent * 100);
            
            // État de charge
            IsPluggedIn = powerStatus.PowerLineStatus == PowerLineStatus.Online;
            IsCharging = powerStatus.BatteryChargeStatus.HasFlag(BatteryChargeStatus.Charging);
            
            // Temps restant
            var secondsRemaining = powerStatus.BatteryLifeRemaining;
            if (secondsRemaining > 0 && !IsCharging)
            {
                var time = TimeSpan.FromSeconds(secondsRemaining);
                if (time.TotalHours >= 1)
                    TimeRemaining = $"{(int)time.TotalHours}h {time.Minutes}min";
                else
                    TimeRemaining = $"{time.Minutes} min";
            }
            else if (IsCharging)
            {
                TimeRemaining = "En charge...";
            }
            else if (IsPluggedIn && BatteryPercent >= 100)
            {
                TimeRemaining = "Complètement chargé";
            }
            else
            {
                TimeRemaining = "Calcul...";
            }
            
            // Texte de statut
            if (IsCharging)
                StatusText = "En charge";
            else if (IsPluggedIn)
                StatusText = "Branché";
            else
                StatusText = "Sur batterie";
            
            // Notifier les propriétés calculées
            OnPropertyChanged(nameof(BatteryIcon));
            OnPropertyChanged(nameof(BatteryColor));
            OnPropertyChanged(nameof(BatteryBarWidth));
            
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            HasBattery = false;
        }
        
        return Task.CompletedTask;
    }
}
