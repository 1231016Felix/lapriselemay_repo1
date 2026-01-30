using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using WallpaperManager.Widgets.Base;

namespace WallpaperManager.Widgets.QuickNotes;

/// <summary>
/// ViewModel pour le widget Quick Notes.
/// Permet de créer, éditer et supprimer des notes rapides.
/// </summary>
public class QuickNotesWidgetViewModel : WidgetViewModelBase
{
    private readonly string _notesFilePath;
    private bool _isEditing;
    private string _currentNoteText = string.Empty;
    private NoteItem? _editingNote;
    
    protected override int RefreshIntervalSeconds => 60; // Pas besoin de rafraîchir souvent
    
    public ObservableCollection<NoteItem> Notes { get; } = [];
    
    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }
    
    public string CurrentNoteText
    {
        get => _currentNoteText;
        set => SetProperty(ref _currentNoteText, value);
    }
    
    public ICommand AddNoteCommand { get; }
    public ICommand EditNoteCommand { get; }
    public ICommand DeleteNoteCommand { get; }
    public ICommand SaveNoteCommand { get; }
    public ICommand CancelEditCommand { get; }
    
    public QuickNotesWidgetViewModel()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WallpaperManager");
        Directory.CreateDirectory(appData);
        _notesFilePath = Path.Combine(appData, "quick_notes.json");
        
        AddNoteCommand = new RelayCommand(StartNewNote);
        EditNoteCommand = new RelayCommand<NoteItem>(StartEditNote);
        DeleteNoteCommand = new RelayCommand<NoteItem>(DeleteNote);
        SaveNoteCommand = new RelayCommand(SaveCurrentNote);
        CancelEditCommand = new RelayCommand(CancelEdit);
        
        LoadNotes();
    }
    
    public override Task RefreshAsync()
    {
        // Pas de rafraîchissement externe nécessaire
        return Task.CompletedTask;
    }
    
    private void LoadNotes()
    {
        try
        {
            if (File.Exists(_notesFilePath))
            {
                var json = File.ReadAllText(_notesFilePath);
                var notes = JsonSerializer.Deserialize<List<NoteItem>>(json);
                
                Notes.Clear();
                if (notes != null)
                {
                    foreach (var note in notes.OrderByDescending(n => n.UpdatedAt))
                    {
                        Notes.Add(note);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur chargement notes: {ex.Message}");
        }
    }
    
    private void SaveNotes()
    {
        try
        {
            var json = JsonSerializer.Serialize(Notes.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_notesFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur sauvegarde notes: {ex.Message}");
        }
    }
    
    private void StartNewNote()
    {
        _editingNote = null;
        CurrentNoteText = string.Empty;
        IsEditing = true;
    }
    
    private void StartEditNote(NoteItem? note)
    {
        if (note == null) return;
        
        _editingNote = note;
        CurrentNoteText = note.Text;
        IsEditing = true;
    }
    
    private void DeleteNote(NoteItem? note)
    {
        if (note == null) return;
        
        Notes.Remove(note);
        SaveNotes();
    }
    
    private void SaveCurrentNote()
    {
        if (string.IsNullOrWhiteSpace(CurrentNoteText))
        {
            CancelEdit();
            return;
        }
        
        if (_editingNote != null)
        {
            // Mise à jour d'une note existante
            _editingNote.Text = CurrentNoteText.Trim();
            _editingNote.UpdatedAt = DateTime.Now;
            
            // Réorganiser pour mettre la note modifiée en haut
            var index = Notes.IndexOf(_editingNote);
            if (index > 0)
            {
                Notes.Move(index, 0);
            }
        }
        else
        {
            // Nouvelle note
            var newNote = new NoteItem
            {
                Text = CurrentNoteText.Trim(),
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            Notes.Insert(0, newNote);
        }
        
        SaveNotes();
        CancelEdit();
    }
    
    private void CancelEdit()
    {
        IsEditing = false;
        CurrentNoteText = string.Empty;
        _editingNote = null;
    }
}

/// <summary>
/// Représente une note individuelle.
/// </summary>
public class NoteItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string Color { get; set; } = "#FEF3C7"; // Jaune post-it par défaut
    
    public string Preview => Text.Length > 50 ? Text[..50] + "..." : Text;
    public string TimeAgo => GetTimeAgo();
    
    private string GetTimeAgo()
    {
        var diff = DateTime.Now - UpdatedAt;
        
        if (diff.TotalMinutes < 1) return "À l'instant";
        if (diff.TotalMinutes < 60) return $"Il y a {(int)diff.TotalMinutes} min";
        if (diff.TotalHours < 24) return $"Il y a {(int)diff.TotalHours}h";
        if (diff.TotalDays < 7) return $"Il y a {(int)diff.TotalDays}j";
        
        return UpdatedAt.ToString("dd/MM");
    }
}
