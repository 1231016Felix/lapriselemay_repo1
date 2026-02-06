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
    private readonly ISettingsProvider _settingsProvider;
    private readonly NotesService _notesService;
    
    /// <summary>
    /// Accès rapide aux paramètres actuels (toujours à jour via ISettingsProvider).
    /// </summary>
    private AppSettings _settings => _settingsProvider.Current;
    
    public event EventHandler? RequestOpenSettings;
    public event EventHandler? RequestQuit;
    public event EventHandler? RequestReindex;
    
    public LauncherWindow(IndexingService indexingService, ISettingsProvider settingsProvider,
        AliasService aliasService, NoteWidgetService noteWidgetService, TimerWidgetService timerWidgetService,
        NotesService notesService, WebIntegrationService webIntegrationService, FileWatcherService? fileWatcherService = null)
    {
        InitializeComponent();
        
        _settingsProvider = settingsProvider;
        _notesService = notesService;
        _viewModel = new LauncherViewModel(indexingService, settingsProvider, aliasService, noteWidgetService, timerWidgetService, notesService, webIntegrationService, fileWatcherService);
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
        _viewModel.RequestCaretAtEnd += (_, _) => Dispatcher.BeginInvoke(() => SearchBox.CaretIndex = SearchBox.Text.Length);
        _viewModel.RequestScreenCapture += OnRequestScreenCapture;
        
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

    private void OnRequestScreenCapture(object? sender, string? mode)
    {
        try
        {
            var captureMode = mode?.ToLowerInvariant();
            
            System.Drawing.Bitmap? bitmap = null;
            
            if (captureMode is "snip" or "region" or "select")
            {
                // Capture de région avec overlay
                var overlay = new ScreenshotOverlayWindow();
                if (overlay.ShowDialog() == true && overlay.CapturedRegion != null)
                    bitmap = overlay.CapturedRegion;
            }
            else if (captureMode is "primary" or "main")
            {
                bitmap = CaptureScreen(primaryOnly: true);
            }
            else
            {
                bitmap = CaptureScreen(primaryOnly: false);
            }

            if (bitmap != null)
            {
                var annotationWindow = new AnnotationWindow(bitmap);
                annotationWindow.ShowDialog();

                if (!string.IsNullOrEmpty(annotationWindow.SavedFilePath))
                {
                    // Ouvrir le dossier avec le fichier sélectionné
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{annotationWindow.SavedFilePath}\"");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ScreenCapture ERROR] {ex}");
            MessageBox.Show($"Erreur lors de la capture :\n{ex.GetType().Name}: {ex.Message}\n\nStack:\n{ex.StackTrace}", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static System.Drawing.Bitmap? CaptureScreen(bool primaryOnly)
    {
        try
        {
            var bounds = primaryOnly
                ? System.Windows.Forms.Screen.PrimaryScreen!.Bounds
                : System.Windows.Forms.Screen.AllScreens
                    .Select(s => s.Bounds)
                    .Aggregate(System.Drawing.Rectangle.Union);

            var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
            return bitmap;
        }
        catch
        {
            return null;
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
        _settingsProvider.Reload();
        ApplySettings();
        _viewModel.ReloadSettings();
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
                    else if (item.Type == ResultType.Note && item.Path.StartsWith(":note:id:"))
                    {
                        // Supprimer la note
                        if (int.TryParse(item.Path[9..], out var noteId))
                        {
                            _notesService.DeleteNote(noteId);
                            OnShowNotification(this, "🗑️ Note supprimée");
                            _viewModel.ForceRefresh();
                        }
                    }
                }
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
            _settingsProvider.Update(s =>
            {
                s.LastWindowLeft = Left;
                s.LastWindowTop = Top;
            });
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
    
    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!_settings.SingleClickLaunch)
        {
            _viewModel.ExecuteCommand.Execute(null);
        }
    }
    
    private void ResultsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Lancement en clic simple si activé
        if (_settings.SingleClickLaunch && ResultsList.SelectedItem != null)
        {
            // Vérifier qu'on a bien cliqué sur un item (pas sur le scrollbar)
            var item = ItemsControl.ContainerFromElement(ResultsList, (DependencyObject)e.OriginalSource) as ListBoxItem;
            if (item != null)
            {
                _viewModel.ExecuteCommand.Execute(null);
            }
        }
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

    /// <summary>
    /// Génère dynamiquement le menu contextuel en fonction du résultat sélectionné.
    /// </summary>
    private void ResultContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu contextMenu)
            return;
        
        contextMenu.Items.Clear();
        
        // Récupérer le SearchResult depuis le ListBoxItem
        if (contextMenu.PlacementTarget is not ListBoxItem listBoxItem ||
            listBoxItem.DataContext is not SearchResult result)
            return;
        
        // Obtenir les actions disponibles pour ce résultat
        var isPinned = _settings.IsPinned(result.Path);
        var actions = FileActionProvider.GetActionsForResult(result, isPinned);
        
        // Créer le style pour les items
        var menuItemStyle = (Style)FindResource("DarkMenuItemStyle");
        
        FileActionType? lastCategory = null;
        
        foreach (var action in actions)
        {
            // Ajouter un séparateur entre les catégories
            var currentCategory = GetActionCategory(action.ActionType);
            if (lastCategory.HasValue && currentCategory != lastCategory.Value)
            {
                contextMenu.Items.Add(new Separator { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3E, 0x3E, 0x3E)) });
            }
            lastCategory = currentCategory;
            
            var menuItem = new MenuItem
            {
                Header = $"{action.Icon} {action.Name}",
                Style = menuItemStyle,
                InputGestureText = action.Shortcut,
                Tag = action
            };
            
            // Couleur spéciale pour Supprimer
            if (action.ActionType == FileActionType.Delete)
            {
                menuItem.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x6B));
            }
            
            menuItem.Click += (s, args) =>
            {
                if (s is MenuItem mi && mi.Tag is FileAction fileAction)
                {
                    ExecuteContextAction(fileAction, result);
                }
            };
            
            contextMenu.Items.Add(menuItem);
        }
        
        // Ajouter "Ouvrir avec..." pour les fichiers
        if (result.Type is ResultType.File or ResultType.Script)
        {
            contextMenu.Items.Add(new Separator { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3E, 0x3E, 0x3E)) });
            var openWithItem = new MenuItem
            {
                Header = "📎 Ouvrir avec...",
                Style = menuItemStyle
            };
            openWithItem.Click += (s, args) =>
            {
                FileActionsService.OpenWith(result.Path);
                HideWindow();
            };
            contextMenu.Items.Add(openWithItem);
        }
    }
    
    /// <summary>
    /// Détermine la catégorie d'une action pour le regroupement dans le menu.
    /// </summary>
    private static FileActionType GetActionCategory(FileActionType actionType)
    {
        return actionType switch
        {
            FileActionType.Open or FileActionType.RunAsAdmin or FileActionType.OpenPrivate => FileActionType.Open,
            FileActionType.OpenInExplorer or FileActionType.OpenInTerminal => FileActionType.OpenInExplorer,
            FileActionType.CopyUrl => FileActionType.CopyUrl,
            FileActionType.Rename or FileActionType.Delete or FileActionType.Properties => FileActionType.Rename,
            FileActionType.Pin or FileActionType.Unpin => FileActionType.Pin,
            _ => actionType
        };
    }
    
    /// <summary>
    /// Exécute une action du menu contextuel.
    /// </summary>
    private void ExecuteContextAction(FileAction action, SearchResult result)
    {
        // Cas spécial pour Rename
        if (action.ActionType == FileActionType.Rename)
        {
            OnRequestRename(this, result.Path);
            return;
        }
        
        // Cas spécial pour Delete avec confirmation
        if (action.ActionType == FileActionType.Delete)
        {
            ConfirmAndDelete(result);
            return;
        }
        
        // Cas spécial pour Pin
        if (action.ActionType == FileActionType.Pin)
        {
            _settings.PinItem(result.Name, result.Path, result.Type, result.DisplayIcon);
            _settingsProvider.Save();
            OnShowNotification(this, "⭐ Épinglé");
            return;
        }
        
        // Cas spécial pour Unpin
        if (action.ActionType == FileActionType.Unpin)
        {
            _settings.UnpinItem(result.Path);
            _settingsProvider.Save();
            OnShowNotification(this, "📌 Désépinglé");
            // Rafraîchir si on était dans la vue des épingles
            if (string.IsNullOrWhiteSpace(_viewModel.SearchText))
            {
                _viewModel.Reset();
            }
            return;
        }
        
        // Exécuter l'action
        var success = action.Execute(result.Path);
        
        if (success)
        {
            // Notification de succès
            var message = action.ActionType switch
            {
                FileActionType.CopyUrl => "URL copiée",
                _ => null
            };
            
            if (message != null)
                OnShowNotification(this, message);
            
            // Fermer après certaines actions
            if (action.ActionType is FileActionType.Open 
                or FileActionType.RunAsAdmin 
                or FileActionType.OpenPrivate
                or FileActionType.OpenInTerminal)
            {
                HideWindow();
            }
        }
    }

    #endregion
}
