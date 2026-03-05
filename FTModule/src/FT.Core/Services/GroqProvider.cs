namespace FT.Core.Services;

public sealed class GroqProvider : OpenAiCompatibleProvider
{
    private const string DefaultKey = "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";
    private string _apiKey = DefaultKey;

    public override string Name => "Groq";
    protected override string BaseUrl => "https://api.groq.com/openai/v1";
    protected override string ApiKey => _apiKey;
    protected override string Model => "llama-3.3-70b-versatile";

    public void SetApiKey(string key) => _apiKey = string.IsNullOrWhiteSpace(key) ? DefaultKey : key;
}
