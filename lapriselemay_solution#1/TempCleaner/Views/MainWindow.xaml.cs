using System.Windows;
using System.Windows.Controls;
using TempCleaner.Models;
using TempCleaner.ViewModels;

namespace TempCleaner.Views;

public partial class MainWindow : Window
{
    private bool _suppressWarning = false;
    
    public MainWindow()
    {
        InitializeComponent();
        
        Loaded += (s, e) =>
        {
            var workArea = SystemParameters.WorkArea;
            Left = (workArea.Width - Width) / 2 + workArea.Left;
            Top = (workArea.Height - Height) / 2 + workArea.Top;
            if (Top < 0) Top = 10;
        };
        
        // Sauvegarder les préférences à la fermeture
        Closing += (s, e) =>
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.SaveSettings();
            }
        };
    }

    private void CheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.UpdateSelectedStats();
        }
    }
    
    private void ProfileCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressWarning) return;
        
        if (sender is CheckBox checkBox && checkBox.Tag is CleanerProfile profile)
        {
            if (DataContext is MainViewModel viewModel)
            {
                bool confirmed = viewModel.ShowProfileWarning(profile);
                
                if (!confirmed)
                {
                    _suppressWarning = true;
                    checkBox.IsChecked = false;
                    profile.IsEnabled = false;
                    _suppressWarning = false;
                }
            }
        }
    }
}
