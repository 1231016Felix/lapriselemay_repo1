using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace QuickLauncher.Services;

/// <summary>
/// Service d'intégration IA pour la commande :ai.
/// Supporte l'API OpenAI-compatible (ChatGPT, Ollama, Groq, LM Studio, etc.).
/// Ollama expose nativement cette API sur http://localhost:11434/v1/chat/completions.
/// </summary>
public sealed class AiChatService : IDisposable
{
    private readonly HttpClient _httpClient;
    private bool _disposed;

    // Cache simple pour éviter les appels répétés identiques
    private (string Key, AiChatResult Result, DateTime CachedAt)? _cache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public AiChatService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "QuickLauncher/1.0");
    }

    /// <summary>
    /// Envoie une question à l'API et retourne la réponse.
    /// </summary>
    public async Task<AiChatResult?> AskAsync(
        string question,
        string apiUrl,
        string apiKey,
        string model,
        string systemPrompt,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            return null;

        // Vérifier le cache
        var cacheKey = $"{question.ToLowerInvariant()}_{model}";
        if (_cache.HasValue &&
            _cache.Value.Key == cacheKey &&
            DateTime.Now - _cache.Value.CachedAt < CacheDuration)
        {
            return _cache.Value.Result;
        }

        try
        {
            // Construire la requête au format OpenAI-compatible
            var requestBody = new
            {
                model = model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = question }
                },
                max_tokens = 500,
                temperature = 0.7,
                stream = false
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Ajouter la clé API si fournie (pas nécessaire pour Ollama local)
            using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            request.Content = content;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
            }

            var response = await _httpClient.SendAsync(request, token);
            var responseJson = await response.Content.ReadAsStringAsync(token);

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[AI] Erreur HTTP {response.StatusCode}: {responseJson}");
                return new AiChatResult
                {
                    Error = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                        ? "Clé API invalide ou manquante"
                        : response.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? $"Modèle '{model}' introuvable. Vérifiez le nom du modèle."
                        : $"Erreur serveur ({(int)response.StatusCode})"
                };
            }

            var data = JsonDocument.Parse(responseJson);
            var choices = data.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
            {
                return new AiChatResult { Error = "Aucune réponse générée" };
            }

            var message = choices[0].GetProperty("message");
            var answer = message.GetProperty("content").GetString() ?? string.Empty;

            // Extraire les tokens utilisés (si disponible)
            int? tokensUsed = null;
            if (data.RootElement.TryGetProperty("usage", out var usage) &&
                usage.TryGetProperty("total_tokens", out var totalTokens))
            {
                tokensUsed = totalTokens.GetInt32();
            }

            var result = new AiChatResult
            {
                Question = question,
                Answer = answer.Trim(),
                Model = model,
                TokensUsed = tokensUsed
            };

            // Mettre en cache
            _cache = (cacheKey, result, DateTime.Now);
            return result;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
        {
            Debug.WriteLine($"[AI] Connexion refusée: {ex.Message}");
            return new AiChatResult
            {
                Error = "Impossible de se connecter au serveur IA. Vérifiez qu'Ollama est lancé ou que l'URL est correcte."
            };
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"[AI] Erreur HTTP: {ex.Message}");
            return new AiChatResult { Error = "Erreur de connexion. Vérifiez votre réseau." };
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"[AI] Erreur JSON: {ex.Message}");
            return new AiChatResult { Error = "Réponse invalide du serveur IA." };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AI] Erreur: {ex.Message}");
            return new AiChatResult { Error = $"Erreur: {ex.Message}" };
        }
    }

    /// <summary>
    /// Teste la connectivité avec le serveur IA.
    /// </summary>
    public async Task<(bool Success, string Message)> TestConnectionAsync(
        string apiUrl, string apiKey, string model)
    {
        try
        {
            var result = await AskAsync("Dis simplement 'OK'.", apiUrl, apiKey, model,
                "Réponds uniquement 'OK'.", CancellationToken.None);

            if (result == null)
                return (false, "Aucune réponse");
            if (result.HasError)
                return (false, result.Error!);
            return (true, $"✅ Connecté — Modèle: {model}");
        }
        catch (Exception ex)
        {
            return (false, $"Erreur: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }
}

/// <summary>
/// Résultat d'une requête IA.
/// </summary>
public sealed class AiChatResult
{
    public string Question { get; init; } = string.Empty;
    public string Answer { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public int? TokensUsed { get; init; }
    public string? Error { get; init; }

    public bool HasError => !string.IsNullOrEmpty(Error);
}
