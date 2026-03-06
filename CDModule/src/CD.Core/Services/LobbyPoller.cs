using CD.Core.Models;

namespace CD.Core.Services;

/// <summary>
/// Polls Firestore for lobby state changes at a configurable interval.
/// Fires LobbyUpdated when state changes, and LobbyClosed when the lobby disappears.
/// Also sends heartbeat and cleans stale players.
/// </summary>
public sealed class LobbyPoller : IDisposable
{
    private readonly FirestoreLobbyService _firestore;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;

    private string? _lobbyId;
    private UserSession? _session;
    private int _heartbeatCounter;

    /// <summary>Fires when lobby state changes.</summary>
    public event Action<Lobby>? LobbyUpdated;

    /// <summary>Fires when the lobby no longer exists.</summary>
    public event Action? LobbyClosed;

    public LobbyPoller(FirestoreLobbyService firestore)
    {
        _firestore = firestore;
    }

    public void Start(string lobbyId, UserSession session)
    {
        Stop();
        _lobbyId = lobbyId;
        _session = session;
        _heartbeatCounter = 0;
        _cts = new CancellationTokenSource();
        _pollTask = PollLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _pollTask = null;
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1500, ct);

                if (_lobbyId is null || _session is null) continue;

                var lobby = await _firestore.GetLobbyAsync(_lobbyId, _session.IdToken, ct);

                if (lobby is null)
                {
                    LobbyClosed?.Invoke();
                    return;
                }

                // Clean stale players (no heartbeat for 60s)
                var staleThreshold = DateTime.UtcNow.AddSeconds(-60);
                var staleKeys = lobby.Players
                    .Where(p => DateTime.TryParse(p.Value.LastHeartbeat, out var hb) && hb < staleThreshold)
                    .Select(p => p.Key)
                    .ToList();

                if (staleKeys.Count > 0)
                {
                    foreach (var key in staleKeys)
                        lobby.Players.Remove(key);

                    // If lobby is empty and stale, close it
                    if (lobby.Players.Count == 0)
                    {
                        var lastAct = DateTime.TryParse(lobby.LastActivity, out var la) ? la : DateTime.UtcNow;
                        if (DateTime.UtcNow - lastAct > TimeSpan.FromMinutes(5))
                        {
                            await _firestore.CloseLobbyAsync(_lobbyId, _session.IdToken, ct);
                            LobbyClosed?.Invoke();
                            return;
                        }
                    }
                }

                // Send heartbeat every ~20 polls (30s)
                _heartbeatCounter++;
                if (_heartbeatCounter >= 20)
                {
                    _heartbeatCounter = 0;
                    await _firestore.SendHeartbeatAsync(_lobbyId, _session, ct);
                }

                LobbyUpdated?.Invoke(lobby);
            }
            catch (TaskCanceledException) { return; }
            catch { /* transient error, keep polling */ }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
