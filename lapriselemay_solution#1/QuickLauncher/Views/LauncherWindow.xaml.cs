using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickLauncher.Models;
using QuickLauncher.Services;
using QuickLauncher.ViewModels;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;

namespace QuickLauncher.Views;

public partial class LauncherWindow : Window
{
    private readonly LauncherViewModel _viewModel;
    private AppSettings _settings;
    
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
        _viewModel.RequestRename += OnRequestRename;
        _viewModel.ShowNotification += OnShowNotification;
        
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_viewModel.SearchText))
            {
                ClearButton.Visibility = string.IsNullOrEmpty(_viewModel.SearchText) 
                    ? Visibility.Collapsed 
                    : Visibility.Visible;
            }
        };
    }
    
    private void OnRequestRename(object? sender, string path)
    {
        var name = System.IO.Path.GetFileName(path);
        var dialog = new RenameDialog(name) { Owner = this };
        
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.NewName))
        {
            var success = FileActionsService.Rename(path, dialog.NewName);
            if (success)
            {
                RequestReindex?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                MessageBox.Show("Impossible de renommer le fichier.", "Erreur", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    
    private void OnShowNotification(object? sender, string message)
    {
        // Pour l'instant, on peut utiliser une notification simple
        // TODO: Implémenter un toast notification
        System.Diagnostics.Debug.WriteLine($"[Notification] {message}");
    }
    
    private void ApplySettings()
    {
        _settings = AppSettings.Load();
        Opacity = _settings.WindowOpacity;
        SettingsButton.Visibility = _settings.ShowSettingsButton ? Visibility.Visible : Visibility.Collapsed;
        _viewModel.ShowPreviewPanel = _settings.ShowPreviewPanel;
        
        // Ajuster la largeur selon si le panneau de prévisualisation est visible
        Width = _settings.ShowPreviewPanel ? 900 : 680;
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
        _settings = AppSettings.Load();
        ApplySettings();
        _viewModel.ReloadSettings(); // Synchroniser les settings du ViewModel (épingles, etc.)
        _viewModel.Reset();
        CenterOnScreen();
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Si le panneau d'actions est ouvert, gérer ses raccourcis
        if (_viewModel.ShowActionsPanel)
        {
            HandleActionsKeyDown(e);
            return;
        }
        
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
                    
                case Key.P:
                    _viewModel.TogglePreviewCommand.Execute(null);
                    Width = _viewModel.ShowPreviewPanel ? 900 : 680;
                    CenterOnScreen();
                    e.Handled = true;
                    return;
                    
                case Key.O:
                    _viewModel.OpenLocationCommand.Execute(null);
                    e.Handled = true;
                    return;
                    
                case Key.T:
                    // Ouvrir terminal
                    if (_viewModel.SelectedIndex >= 0 && _viewModel.SelectedIndex < _viewModel.Results.Count)
                    {
                        var item = _viewModel.Results[_viewModel.SelectedIndex];
                        FileActionExecutor.Execute(FileActionType.OpenInTerminal, item.Path);
                    }
                    e.Handled = true;
                    return;
            }
        }
        
        // Ctrl+Shift+C: Copier le chemin
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.C)
        {
            _viewModel.CopyPathCommand.Execute(null);
            e.Handled = true;
            return;
        }
        
        // Ctrl+Enter: Exécuter en admin
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter)
        {
            _viewModel.ExecuteAsAdminCommand.Execute(null);
            e.Handled = true;
            return;
        }
        
        // Ctrl+Shift+Enter: Ouvrir en navigation privée (pour les bookmarks/URLs)
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Enter)
        {
            if (_viewModel.SelectedIndex >= 0 && _viewModel.SelectedIndex < _viewModel.Results.Count)
            {
                var item = _viewModel.Results[_viewModel.SelectedIndex];
                if (item.Type is ResultType.Bookmark or ResultType.WebSearch)
                {
                    FileActionExecutor.Execute(FileActionType.OpenPrivate, item.Path);
                    HideWindow();
                }
            }
            e.Handled = true;
            return;
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
                // Tab ouvre le panneau d'actions
                if (_viewModel.HasResults && _viewModel.SelectedIndex >= 0)
                {
                    _viewModel.ToggleActionsCommand.Execute(null);
                }
                e.Handled = true;
                break;
                
            case Key.F2:
                // Renommer
                if (_viewModel.SelectedIndex >= 0 && _viewModel.SelectedIndex < _viewModel.Results.Count)
                {
                    var item = _viewModel.Results[_viewModel.SelectedIndex];
                    if (item.Type is ResultType.File or ResultType.Folder or ResultType.Application)
                    {
                        OnRequestRename(this, item.Path);
                    }
                }
                e.Handled = true;
                break;
                
            case Key.Delete:
                // Supprimer
                if (_viewModel.SelectedIndex >= 0 && _viewModel.SelectedIndex < _viewModel.Results.Count)
                {
                    var item = _viewModel.Results[_viewModel.SelectedIndex];
                    if (item.Type is ResultType.File or ResultType.Folder)
                    {
                        ConfirmAndDelete(item);
                    }
                }
                e.Handled = true;
                break;
        }
    }
    
    private void HandleActionsKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _viewModel.ShowActionsPanel = false;
                SearchBox.Focus();
                e.Handled = true;
                break;
                
            case Key.Enter:
                _viewModel.ExecuteActionCommand.Execute(null);
                e.Handled = true;
                break;
                
            case Key.Down:
                _viewModel.MoveActionSelection(1);
                e.Handled = true;
                break;
                
            case Key.Up:
                _viewModel.MoveActionSelection(-1);
                e.Handled = true;
                break;
                
            case Key.Tab:
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                    _viewModel.MoveActionSelection(-1);
                else
                    _viewModel.MoveActionSelection(1);
                e.Handled = true;
                break;
        }
    }
    
    private void ConfirmAndDelete(SearchResult item)
    {
        var result = MessageBox.Show(
            $"Voulez-vous vraiment supprimer '{item.Name}' ?\n\nLe fichier sera envoyé à la corbeille.",
            "Confirmer la suppression",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        
        if (result == MessageBoxResult.Yes)
        {
            var success = FileActionsService.DeleteToRecycleBin(item.Path);
            if (success)
            {
                RequestReindex?.Invoke(this, EventArgs.Empty);
                HideWindow();
            }
            else
            {
                MessageBox.Show("Impossible de supprimer le fichier.", "Erreur", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    
    private void Window_Deactivated(object sender, EventArgs e) => HideWindow();
    
    private void HideWindow()
    {
        if (_settings.WindowPosition == "Remember")
        {
            // Recharger les settings depuis le fichier pour ne pas écraser les changements
            // du ViewModel (comme les épingles) avec une ancienne version
            _settings = AppSettings.Load();
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
    
    private void PreviewToggle_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.TogglePreviewCommand.Execute(null);
        Width = _viewModel.ShowPreviewPanel ? 900 : 680;
        CenterOnScreen();
    }
    
    private void PreviewPath_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_viewModel.CurrentPreview != null)
        {
            Clipboard.SetText(_viewModel.CurrentPreview.FullPath);
            OnShowNotification(this, "Chemin copié");
        }
    }
    
    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _viewModel.ExecuteCommand.Execute(null);
    }
    
    private void ActionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _viewModel.ExecuteActionCommand.Execute(null);
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

    #region Context Menu Handlers

    private SearchResult? GetContextItem(object sender)
    {
        if (sender is MenuItem menuItem && 
            menuItem.Parent is ContextMenu contextMenu &&
            contextMenu.PlacementTarget is ListBoxItem listBoxItem)
        {
            return listBoxItem.DataContext as SearchResult;
        }
        return null;
    }

    private void ContextMenu_OpenLocation(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null)
        {
            FileActionExecutor.Execute(FileActionType.OpenLocation, item.Path);
        }
    }

    private void ContextMenu_CopyPath(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null)
        {
            FileActionExecutor.Execute(FileActionType.CopyPath, item.Path);
            OnShowNotification(this, "Chemin copié");
        }
    }

    private void ContextMenu_CopyName(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null)
        {
            FileActionExecutor.Execute(FileActionType.CopyName, item.Path);
            OnShowNotification(this, "Nom copié");
        }
    }

    private void ContextMenu_RunAsAdmin(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null)
        {
            FileActionExecutor.Execute(FileActionType.RunAsAdmin, item.Path);
            HideWindow();
        }
    }

    private void ContextMenu_OpenWith(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null)
        {
            FileActionsService.OpenWith(item.Path);
            HideWindow();
        }
    }
    
    private void ContextMenu_OpenPrivate(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null)
        {
            FileActionExecutor.Execute(FileActionType.OpenPrivate, item.Path);
            HideWindow();
        }
    }

    private void ContextMenu_OpenTerminal(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null)
        {
            FileActionExecutor.Execute(FileActionType.OpenInTerminal, item.Path);
            HideWindow();
        }
    }

    private void ContextMenu_Rename(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null)
        {
            OnRequestRename(this, item.Path);
        }
    }

    private void ContextMenu_Delete(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null)
        {
            ConfirmAndDelete(item);
        }
    }

    #endregion
}
