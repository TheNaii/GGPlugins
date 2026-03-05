using System.Threading;
using System.Threading.Tasks;

namespace LoLAM.Core.Riot;

public sealed class RiotAccountInfo
{
    public string Puuid { get; init; } = "";
    public string GameName { get; init; } = "";
    public string TagLine { get; init; } = "";
}

public sealed class RiotRankInfo
{
    public string Tier { get; init; } = "Unranked";
    public string Division { get; init; } = "";
    public int LeaguePoints { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
}

public interface IRiotApiService
{
    /// <summary>Look up a Riot account by gameName#tagLine. Returns null if not found.</summary>
    Task<RiotAccountInfo?> GetAccountByRiotIdAsync(string gameName, string tagLine, string server, CancellationToken ct = default);

    /// <summary>Get Solo/Duo ranked info for a summoner by PUUID. Returns null if unranked or unavailable.</summary>
    Task<RiotRankInfo?> GetRankedInfoAsync(string puuid, string server, CancellationToken ct = default);

    /// <summary>Count total ranked games played this split by PUUID.</summary>
    Task<int> GetRankedGamesPlayedAsync(string puuid, string server, CancellationToken ct = default);
}
