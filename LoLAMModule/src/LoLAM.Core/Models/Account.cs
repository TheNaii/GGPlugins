using System;

namespace LoLAM.Core.Models;

public class Account
{
    public string? SummonerName { get; set; }
    public string? RiotTag { get; set; } // e.g., "#EUW1"
    public string? Puuid { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; } // encrypted later, reveal supported
    public string? Server { get; set; }

    public string? Tier { get; set; } = "Unranked";
    public string? Division { get; set; } = "";
    public DateTime? LastLogin { get; set; }
    public int GamesPlayedThisSplit { get; set; }
    public bool IsXboxLinked { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string Rank => $"{Tier} {Division}".Trim();

    public string DisplayLabel
    {
        get
        {
            string rankDisplay = Tier switch
            {
                "Unavailable" => "Rank data unavailable",
                "Unranked" => "Unranked",
                _ => $"{Tier} {Division}"
            };

            return $"{SummonerName}{RiotTag} — {rankDisplay}";
        }
    }
}