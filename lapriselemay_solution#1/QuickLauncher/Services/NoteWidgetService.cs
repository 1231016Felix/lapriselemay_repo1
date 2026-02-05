using System.Diagnostics;
using System.Windows;
using QuickLauncher.Models;
using QuickLauncher.Views;

namespace QuickLauncher.Services;

/// <summary>
/// Service de gestion des widgets de notes sur le bureau.
/// Utilise ISettingsProvider pour éviter les lectures disque répétées.
/// </summary>
public sealed class NoteWidgetService
{
    private readonly Dictionary<int, NoteWidget> _activeWidgets = [];
    private readonly ISettingsProvider _settingsProvider;
    private int _nextId;
    
    private AppSettings Settings => _settingsProvider.Current;
    
    public NoteWidgetService(ISettingsProvider settingsProvider)
    {
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _nextId = Settings.NoteWidgets.Count > 0 
            ? Settings.NoteWidgets.Max(n => n.Id) + 1 
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
        var widget = new NoteWidget(noteId, content, OnWidgetClosed, SaveWidgetPosition);
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
        
        _settingsProvider.Update(s => s.NoteWidgets.Add(info));
        
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
        _settingsProvider.Update(s =>
        {
            var info = s.NoteWidgets.FirstOrDefault(n => n.Id == noteId);
            if (info != null)
            {
                info.Left = left;
                info.Top = top;
            }
        });
        Debug.WriteLine($"[NoteWidget] Position sauvegardée: ID={noteId}, Left={left}, Top={top}");
    }
    
    /// <summary>
    /// Appelé quand un widget est fermé (supprime la note).
    /// </summary>
    private void OnWidgetClosed(int noteId)
    {
        _activeWidgets.Remove(noteId);
        
        _settingsProvider.Update(s =>
        {
            var info = s.NoteWidgets.FirstOrDefault(n => n.Id == noteId);
            if (info != null)
                s.NoteWidgets.Remove(info);
        });
        Debug.WriteLine($"[NoteWidget] Supprimé: ID={noteId}");
    }
    
    /// <summary>
    /// Restaure tous les widgets de notes sauvegardés au démarrage.
    /// </summary>
    public void RestoreWidgets()
    {
        foreach (var info in Settings.NoteWidgets.ToList())
        {
            try
            {
                var widget = new NoteWidget(info.Id, info.Content, OnWidgetClosed, SaveWidgetPosition);
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
        if (Settings.NoteWidgets.Count > 0)
        {
            _nextId = Settings.NoteWidgets.Max(n => n.Id) + 1;
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
