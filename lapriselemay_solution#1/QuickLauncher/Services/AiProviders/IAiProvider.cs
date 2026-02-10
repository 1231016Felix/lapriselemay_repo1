using System.Net.Http;

namespace QuickLauncher.Services.AiProviders;

/// <summary>
/// Interface commune pour les fournisseurs d'IA.
/// Chaque implémentation gère le format de requête/réponse spécifique à son API.
/// </summary>
public interface IAiProvider
{
    /// <summary>
    /// Identifiant du fournisseur (ex: "chatgpt", "ollama", "gemini", "custom").
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Construit le HttpRequestMessage pour l'API du fournisseur.
    /// </summary>
    HttpRequestMessage BuildRequest(
        string apiUrl,
        string apiKey,
        string model,
        string systemPrompt,
        string question);

    /// <summary>
    /// Parse la réponse JSON du fournisseur et extrait le texte + tokens.
    /// </summary>
    AiProviderResponse ParseResponse(string responseJson);
}

/// <summary>
/// Résultat parsé d'une réponse API, indépendant du fournisseur.
/// </summary>
public sealed class AiProviderResponse
{
    public string Answer { get; init; } = string.Empty;
    public int? TokensUsed { get; init; }
    public string? Error { get; init; }
    public bool HasError => !string.IsNullOrEmpty(Error);
}
