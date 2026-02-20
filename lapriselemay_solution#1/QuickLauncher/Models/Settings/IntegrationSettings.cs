namespace QuickLauncher.Models.Settings;

/// <summary>
/// Paramètres des intégrations externes : météo, traduction, IA, notes, widgets.
/// </summary>
public sealed class IntegrationSettings
{
    // === Météo ===
    public string WeatherCity { get; set; } = "Montreal";
    public string WeatherUnit { get; set; } = "celsius";
    
    // === Traduction ===
    public string TranslateTargetLang { get; set; } = "en";
    public string TranslateSourceLang { get; set; } = "auto";
    
    // === IA ===
    public string AiProvider { get; set; } = "chatgpt";
    public string AiApiUrl { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string AiApiKey { get; set; } = string.Empty;
    public string AiModel { get; set; } = "gpt-4o-mini";
    public string AiSystemPrompt { get; set; } = "Tu es un assistant concis intégré dans un lanceur d'applications. Réponds de manière courte et directe (2-3 phrases max). Pas de markdown. Langue: français.";
    
    /// <summary>
    /// Délai en secondes après la dernière touche tapée avant d'envoyer la requête IA.
    /// Permet à l'utilisateur de finir sa question sans réponse prématurée.
    /// Plage : 1-4 secondes, défaut : 3.
    /// </summary>
    public int AiDebounceSeconds { get; set; } = Constants.AiDebounceSecondsDefault;
    
    // === Widgets de notes ===
    public List<NoteWidgetInfo> NoteWidgets { get; set; } = [];
    
    // === Widgets de minuteries ===
    public List<TimerWidgetInfo> TimerWidgets { get; set; } = [];
    
    // === Notes rapides ===
    public List<NoteItem> Notes { get; set; } = [];
}
