using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace LoLAM.Core.Riot;

public sealed class RiotApiService : IRiotApiService
{
    private const string ApiKey = "RGAPI-33ea94a9-1025-401b-913c-0b4f408c9833";

    private readonly HttpClient _http;
    private readonly string? _logFile;

    public RiotApiService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GGLauncherDev", "logs");
            Directory.CreateDirectory(logDir);
            _logFile = Path.Combine(logDir, "riot-api-debug.log");
        }
        catch { }
    }

    private void Log(string msg)
    {
        if (_logFile is null) return;
        try { File.AppendAllText(_logFile, $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}"); }
        catch { }
    }

    // ─── Account lookup by Riot ID ──────────────────────────────

    public async Task<RiotAccountInfo?> GetAccountByRiotIdAsync(
        string gameName, string tagLine, string server, CancellationToken ct = default)
    {
        tagLine = tagLine.TrimStart('#');

        var host = RiotRegionMapper.ToRegionalHost(server);
        var url = $"https://{host}/riot/account/v1/accounts/by-riot-id/{Uri.EscapeDataString(gameName)}/{Uri.EscapeDataString(tagLine)}";

        Log($"GetAccountByRiotId: {gameName}#{tagLine} server={server} url={url}");

        var json = await GetAsync(url, ct);
        if (json is null)
        {
            Log($"  -> returned null");
            return null;
        }

        var info = new RiotAccountInfo
        {
            Puuid = json["puuid"]?.ToString() ?? "",
            GameName = json["gameName"]?.ToString() ?? gameName,
            TagLine = json["tagLine"]?.ToString() ?? tagLine
        };
        Log($"  -> puuid={info.Puuid}");
        return info;
    }

    // ─── Ranked info ────────────────────────────────────────────

    public async Task<RiotRankInfo?> GetRankedInfoAsync(
        string puuid, string server, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(puuid)) return null;

        var platformHost = RiotRegionMapper.ToPlatformHost(server);

        // league-v4 now supports direct PUUID lookup (summoner ID no longer returned by summoner-v4)
        var leagueUrl = $"https://{platformHost}/lol/league/v4/entries/by-puuid/{Uri.EscapeDataString(puuid)}";
        Log($"GetRankedInfo: leagueUrl={leagueUrl}");

        var leagueJson = await GetArrayAsync(leagueUrl, ct);
        if (leagueJson is null)
        {
            Log($"  -> league entries returned null");
            return null;
        }

        Log($"  -> league entries count={leagueJson.Count}");
        foreach (var entry in leagueJson)
            Log($"     queueType={entry["queueType"]} tier={entry["tier"]} rank={entry["rank"]}");

        var soloEntry = leagueJson.FirstOrDefault(e =>
            string.Equals(e["queueType"]?.ToString(), "RANKED_SOLO_5x5", StringComparison.OrdinalIgnoreCase));

        if (soloEntry is null)
        {
            Log($"  -> no RANKED_SOLO_5x5 entry found");
            return null;
        }

        return new RiotRankInfo
        {
            Tier = CapitalizeFirst(soloEntry["tier"]?.ToString() ?? "Unranked"),
            Division = soloEntry["rank"]?.ToString() ?? "",
            LeaguePoints = soloEntry["leaguePoints"]?.Value<int>() ?? 0,
            Wins = soloEntry["wins"]?.Value<int>() ?? 0,
            Losses = soloEntry["losses"]?.Value<int>() ?? 0
        };
    }

    // ─── Games played (from ranked wins + losses) ───────────────

    public async Task<int> GetRankedGamesPlayedAsync(
        string puuid, string server, CancellationToken ct = default)
    {
        var rank = await GetRankedInfoAsync(puuid, server, ct);
        return rank is not null ? rank.Wins + rank.Losses : 0;
    }

    // ─── HTTP helpers ───────────────────────────────────────────

    private async Task<JObject?> GetAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Riot-Token", ApiKey);

            using var response = await _http.SendAsync(request, ct);

            Log($"  HTTP {(int)response.StatusCode} {response.StatusCode} for {url}");

            if (response.StatusCode is HttpStatusCode.NotFound
                or HttpStatusCode.Forbidden
                or HttpStatusCode.Unauthorized
                or (HttpStatusCode)429)
                return null;

            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            return JObject.Parse(body);
        }
        catch (Exception ex)
        {
            Log($"  EXCEPTION: {ex.Message}");
            return null;
        }
    }

    private async Task<JArray?> GetArrayAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Riot-Token", ApiKey);

            using var response = await _http.SendAsync(request, ct);

            Log($"  HTTP {(int)response.StatusCode} {response.StatusCode} for {url}");

            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            return JArray.Parse(body);
        }
        catch (Exception ex)
        {
            Log($"  EXCEPTION: {ex.Message}");
            return null;
        }
    }

    private static string CapitalizeFirst(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpper(s[0]) + s[1..].ToLower();
    }
}
