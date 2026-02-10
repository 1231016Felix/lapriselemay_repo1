using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace QuickLauncher.Services.AiProviders;

/// <summary>
/// Fournisseur Google Gemini (API native).
/// Endpoint: https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent
/// Doc: https://ai.google.dev/gemini-api/docs/text-generation
/// </summary>
public sealed class GeminiProvider : IAiProvider
{
    public string ProviderId => "gemini";

    public HttpRequestMessage BuildRequest(
        string apiUrl, string apiKey, string model,
        string systemPrompt, string question)
    {
        // L'URL de base est stockée dans les settings (ex: https://generativelanguage.googleapis.com/v1beta)
        // On construit l'URL complète avec le modèle et la clé API
        var baseUrl = apiUrl.TrimEnd('/');
        var url = $"{baseUrl}/models/{model}:generateContent?key={apiKey}";

        var body = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = question } }
                }
            },
            generationConfig = new
            {
                maxOutputTokens = 500,
                temperature = 0.7
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json")
        };

        return request;
    }

    public AiProviderResponse ParseResponse(string responseJson)
    {
        var data = JsonDocument.Parse(responseJson);

        // Vérifier les erreurs de l'API Gemini
        if (data.RootElement.TryGetProperty("error", out var error))
        {
            var message = error.TryGetProperty("message", out var msg)
                ? msg.GetString() ?? "Erreur inconnue"
                : "Erreur inconnue";
            return new AiProviderResponse { Error = message };
        }

        // Extraire la réponse : candidates[0].content.parts[0].text
        if (!data.RootElement.TryGetProperty("candidates", out var candidates) ||
            candidates.GetArrayLength() == 0)
        {
            return new AiProviderResponse { Error = "Aucune réponse générée par Gemini" };
        }

        var firstCandidate = candidates[0];

        // Vérifier le finishReason (SAFETY, RECITATION, etc.)
        if (firstCandidate.TryGetProperty("finishReason", out var finishReason))
        {
            var reason = finishReason.GetString();
            if (reason is "SAFETY" or "RECITATION" or "BLOCKLIST")
                return new AiProviderResponse { Error = $"Réponse bloquée par Gemini (raison: {reason})" };
        }

        var answer = string.Empty;
        if (firstCandidate.TryGetProperty("content", out var content) &&
            content.TryGetProperty("parts", out var parts) &&
            parts.GetArrayLength() > 0)
        {
            answer = parts[0].TryGetProperty("text", out var textProp)
                ? textProp.GetString() ?? string.Empty
                : string.Empty;
        }

        // Extraire les tokens (usageMetadata)
        int? tokensUsed = null;
        if (data.RootElement.TryGetProperty("usageMetadata", out var usageMeta))
        {
            int prompt = 0, completion = 0;
            if (usageMeta.TryGetProperty("promptTokenCount", out var pt))
                prompt = pt.GetInt32();
            if (usageMeta.TryGetProperty("candidatesTokenCount", out var ct))
                completion = ct.GetInt32();
            tokensUsed = prompt + completion;
        }

        return new AiProviderResponse
        {
            Answer = answer.Trim(),
            TokensUsed = tokensUsed
        };
    }
}
