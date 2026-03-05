using System.IO;
using System.Text.Json;

namespace FT.Core.Services;

/// <summary>
/// Persists preferred provider and last used style to a JSON file.
/// </summary>
public sealed class SettingsStore
{
    private readonly string _filePath;

    public SettingsStore(string dataFolder)
    {
        Directory.CreateDirectory(dataFolder);
        _filePath = Path.Combine(dataFolder, "settings.json");
    }

    public FTSettings Load()
    {
        if (!File.Exists(_filePath))
            return new FTSettings();

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<FTSettings>(json) ?? new FTSettings();
        }
        catch
        {
            return new FTSettings();
        }
    }

    public void Save(FTSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}

public sealed class FTSettings
{
    public string PreferredProvider { get; set; } = "Mistral";
    public string LastStyle { get; set; } = "Shakespeare";
}
