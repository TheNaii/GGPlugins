using System.Text;
using System.Text.Json;

namespace FT.Core.Services;

public sealed class GeminiProvider : ITranslationProvider
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private const string DefaultKey = "AIzaSyCe6RuJbdrPRxqWXdalMEYEPFLds01IDYY";
    private string _apiKey = DefaultKey;
    private const string Model = "gemini-2.0-flash-lite";

    public string Name => "Gemini";

    public void SetApiKey(string key) => _apiKey = string.IsNullOrWhiteSpace(key) ? DefaultKey : key;

    public async Task<string?> TranslateAsync(string toxicText, string style, CancellationToken ct = default)
    {
        var systemPrompt = PromptBuilder.BuildSystemPrompt(style);

        var payload = new
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
                    parts = new[] { new { text = toxicText } }
                }
            },
            generationConfig = new
            {
                maxOutputTokens = 512,
                temperature = 0.9
            }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent?key={_apiKey}";
        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request, ct);

        if ((int)response.StatusCode == 429)
            return null;

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        return doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString()?.Trim();
    }
}
