using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LoLAM.Core.Models;

public class Account : INotifyPropertyChanged
{
    private string? _summonerName;
    private string? _riotTag;
    private string? _puuid;
    private string? _username;
    private string? _password;
    private string? _server;
    private string? _tier = "Unranked";
    private string? _division = "";
    private DateTime? _lastLogin;
    private int _gamesPlayedThisSplit;
    private bool _isXboxLinked;
    private DateTime _createdAt = DateTime.Now;

    public string? SummonerName
    {
        get => _summonerName;
        set { if (Set(ref _summonerName, value)) { OnPropertyChanged(nameof(Rank)); OnPropertyChanged(nameof(DisplayLabel)); } }
    }

    public string? RiotTag
    {
        get => _riotTag;
        set { if (Set(ref _riotTag, value)) OnPropertyChanged(nameof(DisplayLabel)); }
    }

    public string? Puuid
    {
        get => _puuid;
        set => Set(ref _puuid, value);
    }

    public string? Username
    {
        get => _username;
        set => Set(ref _username, value);
    }

    public string? Password
    {
        get => _password;
        set => Set(ref _password, value);
    }

    public string? Server
    {
        get => _server;
        set => Set(ref _server, value);
    }

    public string? Tier
    {
        get => _tier;
        set { if (Set(ref _tier, value)) { OnPropertyChanged(nameof(Rank)); OnPropertyChanged(nameof(DisplayLabel)); } }
    }

    public string? Division
    {
        get => _division;
        set { if (Set(ref _division, value)) { OnPropertyChanged(nameof(Rank)); OnPropertyChanged(nameof(DisplayLabel)); } }
    }

    public DateTime? LastLogin
    {
        get => _lastLogin;
        set => Set(ref _lastLogin, value);
    }

    public int GamesPlayedThisSplit
    {
        get => _gamesPlayedThisSplit;
        set => Set(ref _gamesPlayedThisSplit, value);
    }

    public bool IsXboxLinked
    {
        get => _isXboxLinked;
        set => Set(ref _isXboxLinked, value);
    }

    public DateTime CreatedAt
    {
        get => _createdAt;
        set => Set(ref _createdAt, value);
    }

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

    // ── INotifyPropertyChanged ──────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
