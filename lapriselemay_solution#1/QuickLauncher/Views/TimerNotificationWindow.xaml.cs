using System.Media;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace QuickLauncher.Views;

/// <summary>
/// Fenêtre de notification pour les minuteries terminées.
/// </summary>
public partial class TimerNotificationWindow : Window
{
    private readonly DispatcherTimer _autoCloseTimer;
    private bool _isClosing;
    
    public TimerNotificationWindow()
    {
        InitializeComponent();
        
        // Positionner en bas à droite de l'écran
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 10;
        Top = workArea.Bottom - Height - 10;
        
        // Timer pour fermeture automatique après 10 secondes
        _autoCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        _autoCloseTimer.Tick += (_, _) => CloseWithAnimation();
        
        Loaded += OnLoaded;
    }
    
    /// <summary>
    /// Affiche une notification pour une minuterie terminée.
    /// </summary>
    public static void ShowNotification(string label)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var window = new TimerNotificationWindow();
            window.SetTimerLabel(label);
            window.Show();
            
            // Jouer un son
            SystemSounds.Exclamation.Play();
            
            // Forcer le focus pour attirer l'attention
            window.Activate();
            window.Topmost = true;
        });
    }
    
    public void SetTimerLabel(string label)
    {
        TimerLabelText.Text = label;
    }
    
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Jouer l'animation d'entrée
        var fadeIn = (Storyboard)FindResource("FadeInAnimation");
        fadeIn.Begin(this);
        
        // Démarrer le timer de fermeture automatique
        _autoCloseTimer.Start();
    }
    
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseWithAnimation();
    }
    
    private void CloseWithAnimation()
    {
        if (_isClosing) return;
        _isClosing = true;
        
        _autoCloseTimer.Stop();
        
        var fadeOut = (Storyboard)FindResource("FadeOutAnimation");
        fadeOut.Completed += (_, _) => Close();
        fadeOut.Begin(this);
    }
    
    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        // Permettre de déplacer la fenêtre
        DragMove();
    }
}
