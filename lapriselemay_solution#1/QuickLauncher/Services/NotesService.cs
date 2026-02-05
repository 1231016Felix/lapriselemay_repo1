using System.Diagnostics;
using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Service de gestion des notes rapides.
/// Utilise ISettingsProvider pour éviter les lectures disque répétées.
/// </summary>
public sealed class NotesService
{
    private readonly ISettingsProvider _settingsProvider;
    
    public event EventHandler? NotesChanged;
    
    public NotesService(ISettingsProvider settingsProvider)
    {
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
    }
    
    private AppSettings Settings => _settingsProvider.Current;
    
    /// <summary>
    /// Ajoute une nouvelle note.
    /// </summary>
    public NoteItem AddNote(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Le contenu de la note ne peut pas être vide.");
        
        var note = new NoteItem
        {
            Id = Settings.Notes.Count > 0 ? Settings.Notes.Max(n => n.Id) + 1 : 1,
            Content = content.Trim(),
            CreatedAt = DateTime.Now
        };
        
        _settingsProvider.Update(s => s.Notes.Insert(0, note));
        
        NotesChanged?.Invoke(this, EventArgs.Empty);
        Debug.WriteLine($"[Notes] Ajoutée: {note.Content}");
        
        return note;
    }
    
    /// <summary>
    /// Supprime une note par son ID.
    /// </summary>
    public bool DeleteNote(int id)
    {
        var note = Settings.Notes.FirstOrDefault(n => n.Id == id);
        if (note != null)
        {
            _settingsProvider.Update(s => s.Notes.Remove(note));
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
        _settingsProvider.Update(s => s.Notes.Clear());
        NotesChanged?.Invoke(this, EventArgs.Empty);
        Debug.WriteLine("[Notes] Toutes les notes supprimées");
    }
    
    /// <summary>
    /// Retourne toutes les notes.
    /// </summary>
    public IReadOnlyList<NoteItem> GetAllNotes() => Settings.Notes.AsReadOnly();
    
    /// <summary>
    /// Recherche dans les notes.
    /// </summary>
    public IEnumerable<NoteItem> SearchNotes(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Settings.Notes;
        
        return Settings.Notes.Where(n => 
            n.Content.Contains(query, StringComparison.OrdinalIgnoreCase));
    }
}
