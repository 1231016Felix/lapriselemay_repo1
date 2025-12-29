using System.Windows;
using System.Windows.Controls;
using TempCleaner.ViewModels;

namespace TempCleaner.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        // Force la fenêtre à avoir une position visible avec la barre de titre
        Loaded += (s, e) =>
        {
            // S'assurer que la fenêtre est dans les limites de l'écran
            var workArea = SystemParameters.WorkArea;
            
            // Centrer la fenêtre dans la zone de travail
            Left = (workArea.Width - Width) / 2 + workArea.Left;
            Top = (workArea.Height - Height) / 2 + workArea.Top;
            
            // S'assurer que Top est au moins à 0 (barre de titre visible)
            if (Top < 0) Top = 10;
        };
    }

    private void CheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.UpdateSelectedStats();
        }
    }
}
