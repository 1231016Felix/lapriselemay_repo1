using System.Diagnostics;
using System.Windows;
using QuickLauncher.Models;
using QuickLauncher.Views;

namespace QuickLauncher.Services;

/// <summary>
/// Service de gestion des widgets de notes sur le bureau.
/// </summary>
public sealed class NoteWidgetService
{
    private static NoteWidgetService? _instance;
    private static readonly object _lock = new();
    
    private readonly Dictionary<int, NoteWidget> _activeWidgets = [];
    private readonly AppSettings _settings;
    private int _nextId;
    
    public static NoteWidgetService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new NoteWidgetService();
                }
            }
            return _instance;
        }
    }
    
    private NoteWidgetService()
    {
        _settings = AppSettings.Load();
        _nextId = _settings.NoteWidgets.Count > 0 
            ? _settings.NoteWidgets.Max(n => n.Id) + 1 
            : 1;
    }
    
    /// <summary>
    /// Crée et affiche un nouveau widget de note sur le bureau.
    /// </summary>
    public NoteWidgetInfo CreateWidget(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Le contenu de la note ne peut pas être vide.");
        
        var noteId = _nextId++;
        
        // Calculer la position (décalage par rapport aux widgets existants)
        var workArea = SystemParameters.WorkArea;
        var offsetX = (_activeWidgets.Count % 5) * 30;
        var offsetY = (_activeWidgets.Count % 5) * 30;
        var left = workArea.Right - 300 - offsetX;
        var top = workArea.Bottom - 150 - offsetY;
        
        // Créer le widget
        var widget = new NoteWidget(noteId, content, OnWidgetClosed);
        widget.SetPosition(left, top);
        
        // Enregistrer dans les settings
        var info = new NoteWidgetInfo
        {
            Id = noteId,
            Content = content,
            Left = left,
            Top = top,
            CreatedAt = DateTime.Now
        };
        
        _settings.NoteWidgets.Add(info);
        _settings.Save();
        
        // Afficher le widget
        _activeWidgets[noteId] = widget;
        widget.Show();
        
        Debug.WriteLine($"[NoteWidget] Créé: ID={noteId}, Content={content}");
        
        return info;
    }
    
    /// <summary>
    /// Sauvegarde la position d'un widget après déplacement.
    /// </summary>
    public void SaveWidgetPosition(int noteId, double left, double top)
    {
        var info = _settings.NoteWidgets.FirstOrDefault(n => n.Id == noteId);
        if (info != null)
        {
            info.Left = left;
            info.Top = top;
            _settings.Save();
            Debug.WriteLine($"[NoteWidget] Position sauvegardée: ID={noteId}, Left={left}, Top={top}");
        }
    }
    
    /// <summary>
    /// Appelé quand un widget est fermé (supprime la note).
    /// </summary>
    private void OnWidgetClosed(int noteId)
    {
        _activeWidgets.Remove(noteId);
        
        var info = _settings.NoteWidgets.FirstOrDefault(n => n.Id == noteId);
        if (info != null)
        {
            _settings.NoteWidgets.Remove(info);
            _settings.Save();
            Debug.WriteLine($"[NoteWidget] Supprimé: ID={noteId}");
        }
    }
    
    /// <summary>
    /// Restaure tous les widgets de notes sauvegardés au démarrage.
    /// </summary>
    public void RestoreWidgets()
    {
        // Recharger les settings pour avoir les dernières données
        var freshSettings = AppSettings.Load();
        
        foreach (var info in freshSettings.NoteWidgets.ToList())
        {
            try
            {
                var widget = new NoteWidget(info.Id, info.Content, OnWidgetClosed);
                widget.SetPosition(info.Left, info.Top);
                _activeWidgets[info.Id] = widget;
                widget.Show();
                Debug.WriteLine($"[NoteWidget] Restauré: ID={info.Id}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NoteWidget] Erreur restauration ID={info.Id}: {ex.Message}");
            }
        }
        
        // Mettre à jour nextId
        if (freshSettings.NoteWidgets.Count > 0)
        {
            _nextId = freshSettings.NoteWidgets.Max(n => n.Id) + 1;
        }
    }
    
    /// <summary>
    /// Ferme tous les widgets actifs.
    /// </summary>
    public void CloseAll()
    {
        foreach (var widget in _activeWidgets.Values.ToList())
        {
            widget.Close();
        }
        _activeWidgets.Clear();
    }
    
    /// <summary>
    /// Retourne le nombre de widgets actifs.
    /// </summary>
    public int ActiveCount => _activeWidgets.Count;
}
