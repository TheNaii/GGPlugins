using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CD.Core.Models;

namespace CD.Core.Services;

/// <summary>
/// Manages lobbies via Firestore REST API.
/// Each lobby is a single document at lobbies/{lobbyId}.
/// Complex fields (players, picks, bans) stored as JSON strings for simplicity.
/// </summary>
public sealed class FirestoreLobbyService
{
    private const string ProjectId = "lolaccountmanager";
    private static readonly string BaseUrl =
        $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ── List lobbies ────────────────────────────────────────────

    public async Task<List<Lobby>> ListLobbiesAsync(string idToken, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/lobbies?key=&pageSize=50";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            return new List<Lobby>();

        using var doc = JsonDocument.Parse(body);
        var lobbies = new List<Lobby>();

        if (!doc.RootElement.TryGetProperty("documents", out var docs))
            return lobbies;

        foreach (var d in docs.EnumerateArray())
        {
            try { lobbies.Add(ParseLobby(d)); }
            catch { /* skip malformed */ }
        }

        return lobbies;
    }

    // ── Get single lobby ────────────────────────────────────────

    public async Task<Lobby?> GetLobbyAsync(string lobbyId, string idToken, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/lobbies/{lobbyId}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

        using var resp = await Http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;

        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        return ParseLobby(doc.RootElement);
    }

    // ── Create lobby ────────────────────────────────────────────

    public async Task<string> CreateLobbyAsync(string name, UserSession session, CancellationToken ct = default)
    {
        var lobbyId = Guid.NewGuid().ToString("N")[..12];
        var now = DateTime.UtcNow.ToString("o");

        var players = new Dictionary<string, Player>
        {
            [session.UserId] = new Player
            {
                Username = session.Username,
                Role = session.Role,
                LastHeartbeat = now
            }
        };

        var fields = BuildFields(new Dictionary<string, object>
        {
            ["name"] = name,
            ["hostId"] = session.UserId,
            ["hostUsername"] = session.Username,
            ["mode"] = "planning",
            ["notes"] = "",
            ["bansPerSide"] = 5,
            ["createdAt"] = now,
            ["lastActivity"] = now,
            ["playersJson"] = JsonSerializer.Serialize(players, Json),
            ["picksJson"] = "{}",
            ["bansJson"] = "[]",
            ["pickBanStateJson"] = ""
        });

        var payload = JsonSerializer.Serialize(new { fields });
        var url = $"{BaseUrl}/lobbies?documentId={lobbyId}";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.IdToken);
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        return lobbyId;
    }

    // ── Join lobby ──────────────────────────────────────────────

    public async Task JoinLobbyAsync(string lobbyId, UserSession session, CancellationToken ct = default)
    {
        var lobby = await GetLobbyAsync(lobbyId, session.IdToken, ct);
        if (lobby is null) throw new InvalidOperationException("Lobby not found.");

        lobby.Players[session.UserId] = new Player
        {
            Username = session.Username,
            Role = session.Role,
            LastHeartbeat = DateTime.UtcNow.ToString("o")
        };

        await PatchFieldsAsync(lobbyId, session.IdToken, new Dictionary<string, object>
        {
            ["playersJson"] = JsonSerializer.Serialize(lobby.Players, Json),
            ["lastActivity"] = DateTime.UtcNow.ToString("o")
        }, ct);
    }

    // ── Leave lobby ─────────────────────────────────────────────

    public async Task LeaveLobbyAsync(string lobbyId, UserSession session, CancellationToken ct = default)
    {
        var lobby = await GetLobbyAsync(lobbyId, session.IdToken, ct);
        if (lobby is null) return;

        lobby.Players.Remove(session.UserId);

        await PatchFieldsAsync(lobbyId, session.IdToken, new Dictionary<string, object>
        {
            ["playersJson"] = JsonSerializer.Serialize(lobby.Players, Json),
            ["lastActivity"] = DateTime.UtcNow.ToString("o")
        }, ct);
    }

    // ── Update player role ──────────────────────────────────────

    public async Task UpdatePlayerRoleAsync(string lobbyId, UserSession session, string newRole, CancellationToken ct = default)
    {
        var lobby = await GetLobbyAsync(lobbyId, session.IdToken, ct);
        if (lobby is null) return;

        if (lobby.Players.TryGetValue(session.UserId, out var player))
        {
            player.Role = newRole;
            await PatchFieldsAsync(lobbyId, session.IdToken, new Dictionary<string, object>
            {
                ["playersJson"] = JsonSerializer.Serialize(lobby.Players, Json)
            }, ct);
        }
    }

    // ── Heartbeat ───────────────────────────────────────────────

    public async Task SendHeartbeatAsync(string lobbyId, UserSession session, CancellationToken ct = default)
    {
        var lobby = await GetLobbyAsync(lobbyId, session.IdToken, ct);
        if (lobby is null) return;

        if (lobby.Players.TryGetValue(session.UserId, out var player))
        {
            player.LastHeartbeat = DateTime.UtcNow.ToString("o");
            await PatchFieldsAsync(lobbyId, session.IdToken, new Dictionary<string, object>
            {
                ["playersJson"] = JsonSerializer.Serialize(lobby.Players, Json),
                ["lastActivity"] = DateTime.UtcNow.ToString("o")
            }, ct);
        }
    }

    // ── Draft picks ─────────────────────────────────────────────

    /// <summary>
    /// Sets a pick directly from the provided full picks dictionary. No read required.
    /// </summary>
    public async Task SetPicksDirectAsync(string lobbyId, Dictionary<string, DraftPick> picks, string idToken, CancellationToken ct = default)
    {
        await PatchFieldsAsync(lobbyId, idToken, new Dictionary<string, object>
        {
            ["picksJson"] = JsonSerializer.Serialize(picks, Json),
            ["lastActivity"] = DateTime.UtcNow.ToString("o")
        }, ct);
    }

    /// <summary>
    /// Sets bans directly from the provided full bans list. No read required.
    /// </summary>
    public async Task SetBansDirectAsync(string lobbyId, List<Ban> bans, string idToken, CancellationToken ct = default)
    {
        await PatchFieldsAsync(lobbyId, idToken, new Dictionary<string, object>
        {
            ["bansJson"] = JsonSerializer.Serialize(bans, Json),
            ["lastActivity"] = DateTime.UtcNow.ToString("o")
        }, ct);
    }

    // ── Notes ───────────────────────────────────────────────────

    public async Task UpdateNotesAsync(string lobbyId, string notes, UserSession session, CancellationToken ct = default)
    {
        await PatchFieldsAsync(lobbyId, session.IdToken, new Dictionary<string, object>
        {
            ["notes"] = notes,
            ["lastActivity"] = DateTime.UtcNow.ToString("o")
        }, ct);
    }

    // ── Mode / settings ─────────────────────────────────────────

    public async Task UpdateModeAsync(string lobbyId, string mode, UserSession session, CancellationToken ct = default)
    {
        await PatchFieldsAsync(lobbyId, session.IdToken, new Dictionary<string, object>
        {
            ["mode"] = mode,
            ["lastActivity"] = DateTime.UtcNow.ToString("o")
        }, ct);
    }

    public async Task UpdateBansPerSideAsync(string lobbyId, int count, UserSession session, CancellationToken ct = default)
    {
        await PatchFieldsAsync(lobbyId, session.IdToken, new Dictionary<string, object>
        {
            ["bansPerSide"] = count,
            ["lastActivity"] = DateTime.UtcNow.ToString("o")
        }, ct);
    }

    public async Task UpdatePickBanStateAsync(string lobbyId, PickBanState? state, UserSession session, CancellationToken ct = default)
    {
        await PatchFieldsAsync(lobbyId, session.IdToken, new Dictionary<string, object>
        {
            ["pickBanStateJson"] = state is not null ? JsonSerializer.Serialize(state, Json) : "",
            ["lastActivity"] = DateTime.UtcNow.ToString("o")
        }, ct);
    }

    // ── Close lobby ─────────────────────────────────────────────

    public async Task CloseLobbyAsync(string lobbyId, string idToken, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/lobbies/{lobbyId}";
        using var req = new HttpRequestMessage(HttpMethod.Delete, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

        using var resp = await Http.SendAsync(req, ct);
        // Ignore 404 — already deleted
        if (resp.StatusCode != HttpStatusCode.NotFound)
            resp.EnsureSuccessStatusCode();
    }

    // ── Firestore REST helpers ──────────────────────────────────

    private async Task PatchFieldsAsync(string lobbyId, string idToken,
        Dictionary<string, object> fieldsToUpdate, CancellationToken ct)
    {
        var fields = BuildFields(fieldsToUpdate);
        var payload = JsonSerializer.Serialize(new { fields });

        var masks = string.Join("&", fieldsToUpdate.Keys.Select(k => $"updateMask.fieldPaths={k}"));
        var url = $"{BaseUrl}/lobbies/{lobbyId}?{masks}";

        using var req = new HttpRequestMessage(HttpMethod.Patch, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private static Dictionary<string, object> BuildFields(Dictionary<string, object> data)
    {
        var fields = new Dictionary<string, object>();
        foreach (var (key, value) in data)
        {
            fields[key] = value switch
            {
                int i => new { integerValue = i.ToString() },
                long l => new { integerValue = l.ToString() },
                string s => new { stringValue = s },
                _ => new { stringValue = value?.ToString() ?? "" }
            };
        }
        return fields;
    }

    private static Lobby ParseLobby(JsonElement doc)
    {
        var fields = doc.GetProperty("fields");
        var name = doc.GetProperty("name").GetString() ?? "";
        // Extract document ID from the full path: projects/.../documents/lobbies/{id}
        var docId = name.Split('/').Last();

        var lobby = new Lobby
        {
            Id = docId,
            Name = GetString(fields, "name"),
            HostId = GetString(fields, "hostId"),
            HostUsername = GetString(fields, "hostUsername"),
            Mode = GetString(fields, "mode", "planning"),
            Notes = GetString(fields, "notes"),
            BansPerSide = GetInt(fields, "bansPerSide", 5),
            CreatedAt = GetString(fields, "createdAt"),
            LastActivity = GetString(fields, "lastActivity")
        };

        var playersJson = GetString(fields, "playersJson", "{}");
        lobby.Players = JsonSerializer.Deserialize<Dictionary<string, Player>>(playersJson, Json) ?? new();

        var picksJson = GetString(fields, "picksJson", "{}");
        lobby.Picks = JsonSerializer.Deserialize<Dictionary<string, DraftPick>>(picksJson, Json) ?? new();

        var bansJson = GetString(fields, "bansJson", "[]");
        lobby.Bans = JsonSerializer.Deserialize<List<Ban>>(bansJson, Json) ?? new();

        var pbJson = GetString(fields, "pickBanStateJson");
        if (!string.IsNullOrWhiteSpace(pbJson))
            lobby.PickBanPhase = JsonSerializer.Deserialize<PickBanState>(pbJson, Json);

        return lobby;
    }

    private static string GetString(JsonElement fields, string key, string fallback = "")
    {
        if (fields.TryGetProperty(key, out var prop) && prop.TryGetProperty("stringValue", out var val))
            return val.GetString() ?? fallback;
        return fallback;
    }

    private static int GetInt(JsonElement fields, string key, int fallback = 0)
    {
        if (fields.TryGetProperty(key, out var prop) && prop.TryGetProperty("integerValue", out var val))
            return int.TryParse(val.GetString(), out var i) ? i : fallback;
        return fallback;
    }
}
