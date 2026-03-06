using System.IO;
using System.Text.Json;
using CD.Core.Models;

namespace CD.Core.Services;

/// <summary>
/// Fetches champion data from Riot's Data Dragon CDN and caches locally.
/// </summary>
public sealed class ChampionDataService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private const string VersionUrl = "https://ddragon.leagueoflegends.com/api/versions.json";
    private const string ChampionUrl = "https://ddragon.leagueoflegends.com/cdn/{0}/data/en_US/champion.json";
    private const string IconUrl = "https://ddragon.leagueoflegends.com/cdn/{0}/img/champion/{1}";

    private readonly string _cacheDir;
    private readonly string _iconDir;
    private string? _version;

    public List<ChampionInfo> Champions { get; private set; } = new();

    public ChampionDataService(string dataFolder)
    {
        _cacheDir = Path.Combine(dataFolder, "champions");
        _iconDir = Path.Combine(_cacheDir, "icons");
        Directory.CreateDirectory(_iconDir);
    }

    /// <summary>
    /// Loads champion list from cache or fetches from Data Dragon.
    /// </summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var cacheFile = Path.Combine(_cacheDir, "champions.json");

        // Try cache first (valid for 24h)
        if (File.Exists(cacheFile))
        {
            var info = new FileInfo(cacheFile);
            if (info.LastWriteTimeUtc > DateTime.UtcNow.AddHours(-24))
            {
                try
                {
                    var cached = await File.ReadAllTextAsync(cacheFile, ct);
                    Champions = JsonSerializer.Deserialize<List<ChampionInfo>>(cached) ?? new();
                    if (Champions.Count > 0)
                    {
                        // Restore icon paths
                        foreach (var c in Champions)
                        {
                            var iconPath = Path.Combine(_iconDir, c.ImageFile);
                            c.LocalIconPath = File.Exists(iconPath) ? iconPath : null;
                        }
                        return;
                    }
                }
                catch { /* re-fetch */ }
            }
        }

        // Get latest version
        var versionsJson = await Http.GetStringAsync(VersionUrl, ct);
        using var versions = JsonDocument.Parse(versionsJson);
        _version = versions.RootElement[0].GetString() ?? "15.5.1";

        // Fetch champion data
        var url = string.Format(ChampionUrl, _version);
        var champJson = await Http.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(champJson);

        var data = doc.RootElement.GetProperty("data");
        var list = new List<ChampionInfo>();

        foreach (var prop in data.EnumerateObject())
        {
            var champ = prop.Value;
            var info = new ChampionInfo
            {
                Id = prop.Name,
                Name = champ.GetProperty("name").GetString() ?? prop.Name,
                Title = champ.GetProperty("title").GetString() ?? "",
                ImageFile = champ.GetProperty("image").GetProperty("full").GetString() ?? $"{prop.Name}.png",
                Tags = champ.GetProperty("tags").EnumerateArray()
                    .Select(t => t.GetString() ?? "").ToList()
            };
            list.Add(info);
        }

        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        Champions = list;

        // Cache to disk
        await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(list), ct);
    }

    /// <summary>
    /// Downloads a champion icon if not already cached. Returns the local file path.
    /// </summary>
    public async Task<string?> GetIconPathAsync(ChampionInfo champion, CancellationToken ct = default)
    {
        if (champion.LocalIconPath is not null && File.Exists(champion.LocalIconPath))
            return champion.LocalIconPath;

        if (_version is null)
        {
            // Try to determine version
            try
            {
                var versionsJson = await Http.GetStringAsync(VersionUrl, ct);
                using var versions = JsonDocument.Parse(versionsJson);
                _version = versions.RootElement[0].GetString() ?? "15.5.1";
            }
            catch { return null; }
        }

        var localPath = Path.Combine(_iconDir, champion.ImageFile);
        if (File.Exists(localPath))
        {
            champion.LocalIconPath = localPath;
            return localPath;
        }

        try
        {
            var url = string.Format(IconUrl, _version, champion.ImageFile);
            var bytes = await Http.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(localPath, bytes, ct);
            champion.LocalIconPath = localPath;
            return localPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads all icons in the background. Call after LoadAsync().
    /// </summary>
    public async Task PreloadAllIconsAsync(CancellationToken ct = default)
    {
        // Batch in groups of 10 to avoid hammering the CDN
        var batches = Champions.Chunk(10);
        foreach (var batch in batches)
        {
            ct.ThrowIfCancellationRequested();
            await Task.WhenAll(batch.Select(c => GetIconPathAsync(c, ct)));
        }
    }
}
