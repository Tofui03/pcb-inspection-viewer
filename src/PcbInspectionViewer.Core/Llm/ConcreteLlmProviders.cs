using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PcbInspectionViewer.Core.Llm;

public sealed class NullLlmProvider : ILlmProvider
{
    public string ProviderName => "None";

    public Task<string> GenerateTextAsync(string prompt, CancellationToken ct = default)
    {
        return Task.FromResult(string.Empty);
    }
}

public sealed class GeminiLlmProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _modelName;

    public string ProviderName => "Gemini";

    public GeminiLlmProvider(HttpClient httpClient, string apiKey, string modelName = "gemini-2.5-flash")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKey = apiKey ?? string.Empty;
        _modelName = string.IsNullOrWhiteSpace(modelName) ? "gemini-2.5-flash" : modelName.Trim();
    }

    public async Task<string> GenerateTextAsync(string prompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("Gemini API key is not configured.");
        }

        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{_modelName}:generateContent?key={_apiKey}";

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.2,
                maxOutputTokens = 1024
            }
        };

        using var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        HttpResponseMessage response = await _httpClient.PostAsync(url, content, cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        using JsonDocument doc = JsonDocument.Parse(json);

        // candidates[0].content.parts[0].text
        if (doc.RootElement.TryGetProperty("candidates", out JsonElement candidates) &&
            candidates.GetArrayLength() > 0 &&
            candidates[0].TryGetProperty("content", out JsonElement candContent) &&
            candContent.TryGetProperty("parts", out JsonElement parts) &&
            parts.GetArrayLength() > 0 &&
            parts[0].TryGetProperty("text", out JsonElement textElem))
        {
            return textElem.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}

public sealed class OpenAiLlmProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _modelName;

    public string ProviderName => "OpenAI";

    public OpenAiLlmProvider(HttpClient httpClient, string apiKey, string modelName = "gpt-4o-mini")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKey = apiKey ?? string.Empty;
        _modelName = string.IsNullOrWhiteSpace(modelName) ? "gpt-4o-mini" : modelName.Trim();
    }

    public async Task<string> GenerateTextAsync(string prompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("OpenAI API key is not configured.");
        }

        string url = "https://api.openai.com/v1/chat/completions";

        var requestBody = new
        {
            model = _modelName,
            temperature = 0.2,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        HttpResponseMessage response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        using JsonDocument doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("choices", out JsonElement choices) &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out JsonElement msg) &&
            msg.TryGetProperty("content", out JsonElement textElem))
        {
            return textElem.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}
