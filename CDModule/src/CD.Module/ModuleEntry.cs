using System;
using System.IO;
using System.Windows.Controls;
using GGLauncher.ModuleContracts;
using CD.Core.Services;
using CD.Core.Models;

namespace CD.Module;

public sealed class ModuleEntry : IGGLauncherModule
{
    private IModuleHost? _host;

    internal AnonymousAuthService Auth { get; } = new();
    internal FirestoreLobbyService Firestore { get; } = new();
    internal ChampionDataService? Champions { get; private set; }
    internal LobbyPoller? Poller { get; private set; }
    internal UserSession? Session { get; set; }

    /// <summary>The lobby the user is currently in, if any.</summary>
    internal string? CurrentLobbyId { get; set; }

    public string Id => "clash-drafter";
    public string DisplayName => "Clash Drafter";

    public void Initialize(IModuleHost host)
    {
        _host = host;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dataFolder = Path.Combine(appData, "GGLauncher", "ClashDrafter");
        Directory.CreateDirectory(dataFolder);

        Champions = new ChampionDataService(dataFolder);
        Poller = new LobbyPoller(Firestore);
    }

    public UserControl CreateRootView()
        => new JoinPage(this);

    public void Dispose()
    {
        Poller?.Dispose();

        // Fire-and-forget the leave — never block the UI thread
        if (CurrentLobbyId is not null && Session is not null)
        {
            var lobbyId = CurrentLobbyId;
            var session = Session;
            _ = Task.Run(async () =>
            {
                try { await Firestore.LeaveLobbyAsync(lobbyId, session); }
                catch { }
            });
        }
    }
}
