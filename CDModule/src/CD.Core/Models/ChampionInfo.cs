namespace CD.Core.Models;

public sealed class ChampionInfo
{
    public string Id { get; set; } = "";       // e.g. "Jinx"
    public string Name { get; set; } = "";     // e.g. "Jinx"
    public string Title { get; set; } = "";    // e.g. "the Loose Cannon"
    public string ImageFile { get; set; } = ""; // e.g. "Jinx.png"
    public List<string> Tags { get; set; } = new(); // e.g. ["Marksman"]

    /// <summary>Local file path to the cached square icon.</summary>
    public string? LocalIconPath { get; set; }
}
