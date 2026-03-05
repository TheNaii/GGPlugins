namespace FT.Core.Services;

/// <summary>
/// Orchestrates translation across multiple LLM providers with automatic failover.
/// Tries the preferred provider first, then falls back to others on 429 / failure.
/// </summary>
public sealed class TranslationService
{
    public MistralProvider Mistral { get; } = new();
    public GroqProvider Groq { get; } = new();
    public GeminiProvider Gemini { get; } = new();

    private ITranslationProvider[] _order = [];

    /// <summary>Currently preferred provider name.</summary>
    public string PreferredProvider { get; private set; } = "Mistral";

    /// <summary>Which provider actually handled the last translation.</summary>
    public string? LastUsedProvider { get; private set; }

    public void SetPreferred(string providerName)
    {
        PreferredProvider = providerName;
        RebuildOrder();
    }

    /// <summary>
    /// Translates using the preferred provider, falling back to others on failure.
    /// </summary>
    public async Task<string> TranslateAsync(string toxicText, string style, CancellationToken ct = default)
    {
        if (_order.Length == 0)
            RebuildOrder();

        foreach (var provider in _order)
        {
            try
            {
                var result = await provider.TranslateAsync(toxicText, style, ct);
                if (result is not null)
                {
                    LastUsedProvider = provider.Name;
                    return result;
                }
                // null = rate limited, try next
            }
            catch (TaskCanceledException) { throw; }
            catch
            {
                // Provider error, try next
            }
        }

        throw new InvalidOperationException(
            "All translation providers are unavailable. Check your API keys and rate limits.");
    }

    private void RebuildOrder()
    {
        var all = new ITranslationProvider[] { Mistral, Groq, Gemini };

        // Put preferred first, keep the rest as fallbacks
        var preferred = all.FirstOrDefault(p =>
            p.Name.Equals(PreferredProvider, StringComparison.OrdinalIgnoreCase));

        if (preferred is not null)
            _order = new[] { preferred }.Concat(all.Where(p => p != preferred)).ToArray();
        else
            _order = all;
    }
}
