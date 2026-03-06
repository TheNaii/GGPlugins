namespace CD.Core.Models;

public sealed class Lobby
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string HostId { get; set; } = "";
    public string HostUsername { get; set; } = "";
    public string Mode { get; set; } = "planning"; // "planning" | "pickban"
    public string Notes { get; set; } = "";
    public int BansPerSide { get; set; } = 5;
    public string CreatedAt { get; set; } = "";
    public string LastActivity { get; set; } = "";
    public Dictionary<string, Player> Players { get; set; } = new();
    public Dictionary<string, DraftPick> Picks { get; set; } = new();
    public List<Ban> Bans { get; set; } = new();
    public PickBanState? PickBanPhase { get; set; }
}

public sealed class Player
{
    public string Username { get; set; } = "";
    public string Role { get; set; } = "fill"; // top, jungle, mid, adc, support, fill
    public string LastHeartbeat { get; set; } = "";
}

public sealed class DraftPick
{
    public string ChampionId { get; set; } = "";
    public string AssignedBy { get; set; } = "";
}

public sealed class Ban
{
    public string ChampionId { get; set; } = "";
    public string Side { get; set; } = "blue"; // "blue" | "red"
    public int Index { get; set; }
}

public sealed class PickBanState
{
    public string Phase { get; set; } = "ban"; // "ban" | "pick" | "done"
    public string CurrentTeam { get; set; } = "blue";
    public int CurrentIndex { get; set; }
}
