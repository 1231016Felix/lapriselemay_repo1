using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace QuickLauncher.Services.AiProviders;

/// <summary>
/// Fournisseur OpenAI-compatible (ChatGPT, Ollama, Groq, LM Studio, etc.).
/// Utilise le format standard /v1/chat/completions.
/// </summary>
public sealed class OpenAiProvider : IAiProvider
{
    public string ProviderId => "openai";

    public HttpRequestMessage BuildRequest(
        string apiUrl, string apiKey, string model,
        string systemPrompt, string question)
    {
        var body = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = question }
            },
            max_tokens = 500,
            temperature = 0.7,
            stream = false
        };

        var request = new HttpRequestMessage(HttpMethod.Post, apiUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json")
        };

        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

        return request;
    }

    public AiProviderResponse ParseResponse(string responseJson)
    {
        var data = JsonDocument.Parse(responseJson);

        var choices = data.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
            return new AiProviderResponse { Error = "Aucune réponse générée" };

        var answer = choices[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;

        int? tokensUsed = null;
        if (data.RootElement.TryGetProperty("usage", out var usage) &&
            usage.TryGetProperty("total_tokens", out var totalTokens))
        {
            tokensUsed = totalTokens.GetInt32();
        }

        return new AiProviderResponse
        {
            Answer = answer.Trim(),
            TokensUsed = tokensUsed
        };
    }
}
