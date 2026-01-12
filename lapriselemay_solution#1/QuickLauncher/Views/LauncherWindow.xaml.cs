using System.Windows;
using System.Windows.Input;
using QuickLauncher.Models;
using QuickLauncher.Services;
using QuickLauncher.ViewModels;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace QuickLauncher.Views;

public partial class LauncherWindow : Window
{
    private readonly LauncherViewModel _viewModel;
    private readonly AppSettings _settings;
    
    public event EventHandler? RequestOpenSettings;
    public event EventHandler? RequestQuit;
    public event EventHandler? RequestReindex;
    
    public LauncherWindow(IndexingService indexingService)
    {
        InitializeComponent();
        
        _settings = AppSettings.Load();
        _viewModel = new LauncherViewModel(indexingService);
        DataContext = _viewModel;
        
        SetupEventHandlers();
        ApplySettings();
    }
    
    private void SetupEventHandlers()
    {
        _viewModel.RequestHide += (_, _) => HideWindow();
        _viewModel.RequestOpenSettings += (_, _) => RequestOpenSettings?.Invoke(this, EventArgs.Empty);
        _viewModel.RequestQuit += (_, _) => RequestQuit?.Invoke(this, EventArgs.Empty);
        _viewModel.RequestReindex += (_, _) => RequestReindex?.Invoke(this, EventArgs.Empty);
        
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_viewModel.SearchText))
            {
                ClearButton.Visibility = string.IsNullOrEmpty(_viewModel.SearchText) 
                    ? Visibility.Collapsed 
                    : Visibility.Visible;
            }
        };
        
        // Lancement en simple clic ou double-clic selon le paramètre
        if (_settings.SingleClickLaunch)
        {
            ResultsList.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (ResultsList.SelectedItem != null)
                {
                    _viewModel.ExecuteCommand.Execute(null);
                    e.Handled = true;
                }
            };
        }
        else
        {
            ResultsList.MouseDoubleClick += (_, _) => _viewModel.ExecuteCommand.Execute(null);
        }
    }
    
    private void ApplySettings()
    {
        Opacity = _settings.WindowOpacity;
        SettingsButton.Visibility = _settings.ShowSettingsButton ? Visibility.Visible : Visibility.Collapsed;
    }
    
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        
        if (e.OriginalSource is System.Windows.Controls.TextBox or System.Windows.Controls.ListBoxItem)
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

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Raccourcis avec Ctrl
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.OemComma:
                    HideWindow();
                    RequestOpenSettings?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    return;
                    
                case Key.R:
                    HideWindow();
                    RequestReindex?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    return;
                    
                case Key.Q:
                    RequestQuit?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    return;
            }
        }
        
        // Raccourcis sans modificateurs
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
                _viewModel.MoveSelection(Keyboard.Modifiers == ModifierKeys.Shift ? -1 : 1);
                e.Handled = true;
                break;
        }
    }
    
    private void Window_Deactivated(object sender, EventArgs e) => HideWindow();
    
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
    
    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SearchText = string.Empty;
        SearchBox.Focus();
    }
    
    private void Window_Loaded(object sender, RoutedEventArgs e) => CenterOnScreen();
    
    private void CenterOnScreen()
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        var taskbarHeight = screenHeight - SystemParameters.WorkArea.Height;
        var windowWidth = Width;
        var windowHeight = ActualHeight > 0 ? ActualHeight : 150;
        
        switch (_settings.WindowPosition)
        {
            case "Remember" when _settings.LastWindowLeft.HasValue && _settings.LastWindowTop.HasValue:
                var left = _settings.LastWindowLeft.Value;
                var top = _settings.LastWindowTop.Value;
                
                if (left >= 0 && left + windowWidth <= screenWidth &&
                    top >= 0 && top + windowHeight <= screenHeight - taskbarHeight)
                {
                    Left = left;
                    Top = top;
                    return;
                }
                goto default;
                
            case "Top":
                Left = (screenWidth - windowWidth) / 2;
                Top = 60;
                break;
                
            default:
                Left = (screenWidth - windowWidth) / 2;
                Top = (screenHeight - taskbarHeight) / 4;
                break;
        }
    }
}
