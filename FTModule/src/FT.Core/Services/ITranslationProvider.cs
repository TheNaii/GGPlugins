namespace FT.Core.Services;

/// <summary>
/// Represents a single LLM provider that can translate text.
/// </summary>
public interface ITranslationProvider
{
    string Name { get; }

    /// <summary>
    /// Translates the given toxic text into the specified style.
    /// Returns null if the provider is unavailable or rate-limited.
    /// </summary>
    Task<string?> TranslateAsync(string toxicText, string style, CancellationToken ct = default);
}
