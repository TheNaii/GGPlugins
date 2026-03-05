using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FT.Core.Services;

/// <summary>
/// Base provider for any OpenAI-compatible chat completions API (Mistral, Groq).
/// </summary>
public abstract class OpenAiCompatibleProvider : ITranslationProvider
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public abstract string Name { get; }
    protected abstract string BaseUrl { get; }
    protected abstract string ApiKey { get; }
    protected abstract string Model { get; }

    public async Task<string?> TranslateAsync(string toxicText, string style, CancellationToken ct = default)
    {
        var systemPrompt = PromptBuilder.BuildSystemPrompt(style);

        var payload = new
        {
            model = Model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = toxicText }
            },
            max_tokens = 512,
            temperature = 0.9
        };

        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

        using var response = await Http.SendAsync(request, ct);

        if ((int)response.StatusCode == 429)
            return null; // Rate limited — signal failover

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()?.Trim();
    }
}
