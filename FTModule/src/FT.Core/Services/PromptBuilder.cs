namespace FT.Core.Services;

/// <summary>
/// Builds the system prompt for flame translation.
/// Shared across all providers.
/// </summary>
internal static class PromptBuilder
{
    public static string BuildSystemPrompt(string style)
    {
        return $"""
            You are a Flame Translator. The user will give you toxic, rude, or flaming text from an online game chat.
            Your job is to rewrite it in the style of: {style}.

            Rules:
            - Keep the core meaning and intent of the message, but express it in the chosen style.
            - Be creative and funny. Lean hard into the style.
            - Output ONLY the translated text. No explanations, no quotes, no prefixes.
            - Keep it roughly the same length as the input. Don't write an essay.
            - If the input isn't toxic, still translate it into the chosen style.
            """;
    }
}
