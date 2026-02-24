namespace QuickLauncher.Services;

/// <summary>
/// Composite regroupant les services de widgets (notes, minuteries).
/// Réduit le nombre de paramètres du constructeur de <see cref="ViewModels.LauncherViewModel"/>.
/// 
/// Enregistré en singleton dans le conteneur DI.
/// </summary>
public sealed class WidgetContext
{
    public NoteWidgetService Notes { get; }
    public TimerWidgetService Timers { get; }
    public NotesService NotesData { get; }

    public WidgetContext(
        NoteWidgetService notes,
        TimerWidgetService timers,
        NotesService notesData)
    {
        Notes = notes ?? throw new ArgumentNullException(nameof(notes));
        Timers = timers ?? throw new ArgumentNullException(nameof(timers));
        NotesData = notesData ?? throw new ArgumentNullException(nameof(notesData));
    }
}
