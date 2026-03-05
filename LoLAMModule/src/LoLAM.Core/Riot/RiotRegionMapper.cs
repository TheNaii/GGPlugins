using System;
using System.Collections.Generic;

namespace LoLAM.Core.Riot;

/// <summary>
/// Maps LoLAM server codes (EUW, NA, etc.) to Riot API platform and regional routing values.
/// </summary>
public static class RiotRegionMapper
{
    /// <summary>Platform routing host used for summoner-v4, league-v4, etc.</summary>
    public static string ToPlatformHost(string server) => server.ToUpperInvariant() switch
    {
        "EUW"  => "euw1.api.riotgames.com",
        "EUNE" => "eun1.api.riotgames.com",
        "NA"   => "na1.api.riotgames.com",
        "KR"   => "kr.api.riotgames.com",
        "JP"   => "jp1.api.riotgames.com",
        "BR"   => "br1.api.riotgames.com",
        "LAN"  => "la1.api.riotgames.com",
        "LAS"  => "la2.api.riotgames.com",
        "OCE"  => "oc1.api.riotgames.com",
        "TR"   => "tr1.api.riotgames.com",
        "RU"   => "ru.api.riotgames.com",
        "PH"   => "ph2.api.riotgames.com",
        "SG"   => "sg2.api.riotgames.com",
        "TH"   => "th2.api.riotgames.com",
        "TW"   => "tw2.api.riotgames.com",
        "VN"   => "vn2.api.riotgames.com",
        "ME"   => "me1.api.riotgames.com",
        _      => throw new ArgumentException($"Unknown server: {server}")
    };

    /// <summary>Regional routing host used for account-v1, match-v5, etc.</summary>
    public static string ToRegionalHost(string server) => server.ToUpperInvariant() switch
    {
        "NA" or "BR" or "LAN" or "LAS" or "OCE" => "americas.api.riotgames.com",
        "EUW" or "EUNE" or "TR" or "RU" or "ME" => "europe.api.riotgames.com",
        "KR" or "JP"                              => "asia.api.riotgames.com",
        "PH" or "SG" or "TH" or "TW" or "VN"    => "sea.api.riotgames.com",
        _                                         => "europe.api.riotgames.com"
    };
}
