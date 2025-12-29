using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using QuickLauncher.Models;
using QuickLauncher.Services;
using QuickLauncher.ViewModels;

namespace QuickLauncher.Views;

public partial class LauncherWindow : Window
{
    private readonly LauncherViewModel _viewModel;
    private readonly AppSettings _settings;
    
    // Événements pour communiquer avec App.xaml.cs
    public event EventHandler? RequestOpenSettings;
    public event EventHandler? RequestQuit;
    public event EventHandler? RequestReindex;
    
    public LauncherWindow(IndexingService indexingService)
    {
        InitializeComponent();
        
        _settings = AppSettings.Load();
        _viewModel = new LauncherViewModel(indexingService);
        DataContext = _viewModel;
        
        // Événements du ViewModel
        _viewModel.RequestHide += (_, _) => HideWindow();
        _viewModel.RequestOpenSettings += (_, _) => RequestOpenSettings?.Invoke(this, EventArgs.Empty);
        _viewModel.RequestQuit += (_, _) => RequestQuit?.Invoke(this, EventArgs.Empty);
        _viewModel.RequestReindex += (_, _) => RequestReindex?.Invoke(this, EventArgs.Empty);
        
        ResultsList.MouseDoubleClick += (_, _) => _viewModel.ExecuteCommand.Execute(null);
        
        // Appliquer l'opacité de la fenêtre
        Opacity = _settings.WindowOpacity;
        
        // Visibilité du bouton settings
        SettingsButton.Visibility = _settings.ShowSettingsButton ? Visibility.Visible : Visibility.Collapsed;
    }
    
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        
        if (e.OriginalSource is System.Windows.Controls.TextBox || 
            e.OriginalSource is System.Windows.Controls.ListBoxItem)
            return;
            
        DragMove();
    }
    
    public void FocusSearchBox()
    {
        _viewModel.Reset();
        CenterOnScreen();
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.Control)
        {
            HideWindow();
            RequestOpenSettings?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        
        if (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control)
        {
            HideWindow();
            RequestReindex?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        
        if (e.Key == Key.Q && Keyboard.Modifiers == ModifierKeys.Control)
        {
            RequestQuit?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        
        switch (e.Key)
        {
            case Key.Escape:
                HideWindow();
                e.Handled = true;
                break;
                
            case Key.Enter:
                _viewModel.ExecuteCommand.Execute(null);
                e.Handled = true;
                break;
                
            case Key.Down:
                _viewModel.MoveSelection(1);
                e.Handled = true;
                break;
                
            case Key.Up:
                _viewModel.MoveSelection(-1);
                e.Handled = true;
                break;
                
            case Key.Tab:
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                    _viewModel.MoveSelection(-1);
                else
                    _viewModel.MoveSelection(1);
                e.Handled = true;
                break;
        }
    }
    
    private void Window_Deactivated(object sender, EventArgs e)
    {
        HideWindow();
    }
    
    private void HideWindow()
    {
        if (_settings.WindowPosition == "Remember")
        {
            _settings.LastWindowLeft = Left;
            _settings.LastWindowTop = Top;
            _settings.Save();
        }
        
        _viewModel.Reset();
        Hide();
    }
    
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        HideWindow();
        RequestOpenSettings?.Invoke(this, EventArgs.Empty);
    }
    
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
    }
    
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        CenterOnScreen();
    }
    
    private void CenterOnScreen()
    {
        // Utiliser les paramètres WPF qui tiennent compte du DPI
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        double screenHeight = SystemParameters.PrimaryScreenHeight;
        double taskbarHeight = SystemParameters.PrimaryScreenHeight - SystemParameters.WorkArea.Height;
        
        // Largeur de la fenêtre (Width défini dans XAML)
        double windowWidth = this.Width;
        double windowHeight = this.ActualHeight > 0 ? this.ActualHeight : 150;
        
        switch (_settings.WindowPosition)
        {
            case "Remember":
                if (_settings.LastWindowLeft.HasValue && _settings.LastWindowTop.HasValue)
                {
                    double left = _settings.LastWindowLeft.Value;
                    double top = _settings.LastWindowTop.Value;
                    
                    if (left >= 0 && left + windowWidth <= screenWidth &&
                        top >= 0 && top + windowHeight <= screenHeight - taskbarHeight)
                    {
                        Left = left;
                        Top = top;
                        return;
                    }
                }
                goto case "Center";
                
            case "Top":
                Left = (screenWidth - windowWidth) / 2;
                Top = 60;
                break;
                
            case "Center":
            default:
                Left = (screenWidth - windowWidth) / 2;
                Top = (screenHeight - taskbarHeight) / 4;
                break;
        }
    }
}
