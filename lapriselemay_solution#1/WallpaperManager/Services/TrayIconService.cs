using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace WallpaperManager.Services;

/// <summary>
/// Service gérant l'icône dans le system tray (zone de notification).
/// Extrait de App.xaml.cs pour une meilleure séparation des responsabilités.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private TaskbarIcon? _trayIcon;
    private bool _disposed;
    
    /// <summary>
    /// Événement déclenché quand l'utilisateur double-clique sur l'icône
    /// </summary>
    public event EventHandler? OpenRequested;
    
    /// <summary>
    /// Événement déclenché quand l'utilisateur demande le fond suivant
    /// </summary>
    public event EventHandler? NextWallpaperRequested;
    
    /// <summary>
    /// Événement déclenché quand l'utilisateur demande le fond précédent
    /// </summary>
    public event EventHandler? PreviousWallpaperRequested;
    
    /// <summary>
    /// Événement déclenché quand l'utilisateur demande de quitter
    /// </summary>
    public event EventHandler? ExitRequested;
    
    /// <summary>
    /// Indique si l'icône est visible
    /// </summary>
    public bool IsVisible => _trayIcon?.Visibility == Visibility.Visible;

    /// <summary>
    /// Crée et affiche l'icône dans le system tray
    /// </summary>
    public void Initialize()
    {
        if (_trayIcon != null) return;
        
        try
        {
            var contextMenu = CreateContextMenu();
            
            _trayIcon = new TaskbarIcon
            {
                Icon = LoadIcon(),
                ToolTipText = "Wallpaper Manager - Clic droit pour le menu",
                ContextMenu = contextMenu,
                Visibility = Visibility.Visible
            };
            
            _trayIcon.TrayMouseDoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
            _trayIcon.ForceCreate();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur création tray icon: {ex}");
        }
    }

    private ContextMenu CreateContextMenu()
    {
        var contextMenu = new ContextMenu();
        
        var openItem = new MenuItem { Header = "📂 Ouvrir Wallpaper Manager" };
        openItem.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        contextMenu.Items.Add(openItem);
        
        contextMenu.Items.Add(new Separator());
        
        var nextItem = new MenuItem { Header = "▶ Fond suivant" };
        nextItem.Click += (_, _) => NextWallpaperRequested?.Invoke(this, EventArgs.Empty);
        contextMenu.Items.Add(nextItem);
        
        var prevItem = new MenuItem { Header = "◀ Fond précédent" };
        prevItem.Click += (_, _) => PreviousWallpaperRequested?.Invoke(this, EventArgs.Empty);
        contextMenu.Items.Add(prevItem);
        
        contextMenu.Items.Add(new Separator());
        
        var exitItem = new MenuItem { Header = "❌ Quitter complètement" };
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        contextMenu.Items.Add(exitItem);
        
        return contextMenu;
    }

    private static Icon LoadIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
            if (File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur chargement icône: {ex}");
        }
        
        return SystemIcons.Application;
    }

    /// <summary>
    /// Met à jour le texte de l'infobulle
    /// </summary>
    public void UpdateTooltip(string text)
    {
        if (_trayIcon != null)
        {
            _trayIcon.ToolTipText = text;
        }
    }

    /// <summary>
    /// Affiche une notification ballon
    /// </summary>
    public void ShowNotification(string title, string message, NotificationIcon icon = NotificationIcon.Info)
    {
        _trayIcon?.ShowNotification(title, message, icon);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
