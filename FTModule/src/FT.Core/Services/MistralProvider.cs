namespace FT.Core.Services;

public sealed class MistralProvider : OpenAiCompatibleProvider
{
    private const string DefaultKey = "iY1QXogb5SrlvHN2DBNiqPjuYvf7vyP4";
    private string _apiKey = DefaultKey;

    public override string Name => "Mistral";
    protected override string BaseUrl => "https://api.mistral.ai/v1";
    protected override string ApiKey => _apiKey;
    protected override string Model => "mistral-small-latest";

    public void SetApiKey(string key) => _apiKey = string.IsNullOrWhiteSpace(key) ? DefaultKey : key;
}
