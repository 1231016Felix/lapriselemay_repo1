namespace QuickLauncher.Models;

/// <summary>
/// Note rapide de l'utilisateur.
/// </summary>
public sealed class NoteItem
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Preview => Content.Length > 50 ? Content[..47] + "..." : Content;
    public string DateFormatted => CreatedAt.ToString("dd/MM/yyyy HH:mm");
}

/// <summary>
/// État persisté d'un widget de note sur le bureau.
/// </summary>
public sealed class NoteWidgetInfo
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public double Left { get; set; }
    public double Top { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// État persisté d'un widget de minuterie sur le bureau.
/// </summary>
public sealed class TimerWidgetInfo
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
    public int RemainingSeconds { get; set; }
    public double Left { get; set; }
    public double Top { get; set; }
    public DateTime CreatedAt { get; set; }
}