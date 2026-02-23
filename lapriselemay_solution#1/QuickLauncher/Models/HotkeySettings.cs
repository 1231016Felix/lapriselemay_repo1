using System.Text.Json.Serialization;

namespace QuickLauncher.Models;

/// <summary>Configuration du raccourci clavier global.</summary>
public sealed class HotkeySettings
{
    public bool UseAlt { get; set; } = true;
    public bool UseCtrl { get; set; }
    public bool UseShift { get; set; }
    public bool UseWin { get; set; }
    public string Key { get; set; } = "Space";
    
    [JsonIgnore]
    public string DisplayText => string.Join("+", GetModifiers().Append(Key));
    
    private IEnumerable<string> GetModifiers()
    {
        if (UseCtrl) yield return "Ctrl";
        if (UseAlt) yield return "Alt";
        if (UseShift) yield return "Shift";
        if (UseWin) yield return "Win";
    }
}