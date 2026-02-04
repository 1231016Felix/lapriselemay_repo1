using System.Diagnostics;
using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Service de gestion des notes rapides.
/// </summary>
public sealed class NotesService
{
    private static NotesService? _instance;
    private static readonly object _lock = new();
    
    private readonly AppSettings _settings;
    
    public static NotesService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new NotesService();
                }
            }
            return _instance;
        }
    }
    
    public event EventHandler? NotesChanged;
    
    private NotesService()
    {
        _settings = AppSettings.Load();
    }
    
    /// <summary>
    /// Ajoute une nouvelle note.
    /// </summary>
    public NoteItem AddNote(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Le contenu de la note ne peut pas être vide.");
        
        var note = new NoteItem
        {
            Id = _settings.Notes.Count > 0 ? _settings.Notes.Max(n => n.Id) + 1 : 1,
            Content = content.Trim(),
            CreatedAt = DateTime.Now
        };
        
        _settings.Notes.Insert(0, note); // Ajouter au début
        _settings.Save();
        
        NotesChanged?.Invoke(this, EventArgs.Empty);
        Debug.WriteLine($"[Notes] Ajoutée: {note.Content}");
        
        return note;
    }
    
    /// <summary>
    /// Supprime une note par son ID.
    /// </summary>
    public bool DeleteNote(int id)
    {
        var note = _settings.Notes.FirstOrDefault(n => n.Id == id);
        if (note != null)
        {
            _settings.Notes.Remove(note);
            _settings.Save();
            NotesChanged?.Invoke(this, EventArgs.Empty);
            Debug.WriteLine($"[Notes] Supprimée: {note.Content}");
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Supprime toutes les notes.
    /// </summary>
    public void ClearAllNotes()
    {
        _settings.Notes.Clear();
        _settings.Save();
        NotesChanged?.Invoke(this, EventArgs.Empty);
        Debug.WriteLine("[Notes] Toutes les notes supprimées");
    }
    
    /// <summary>
    /// Retourne toutes les notes.
    /// </summary>
    public IReadOnlyList<NoteItem> GetAllNotes() => _settings.Notes.AsReadOnly();
    
    /// <summary>
    /// Recherche dans les notes.
    /// </summary>
    public IEnumerable<NoteItem> SearchNotes(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _settings.Notes;
        
        var queryLower = query.ToLowerInvariant();
        return _settings.Notes.Where(n => 
            n.Content.Contains(query, StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// Recharge les notes depuis les paramètres.
    /// </summary>
    public void Reload()
    {
        var freshSettings = AppSettings.Load();
        _settings.Notes.Clear();
        foreach (var note in freshSettings.Notes)
            _settings.Notes.Add(note);
    }
}
