using System;
using System.IO;
using System.Windows.Controls;
using GGLauncher.ModuleContracts;
using LoLAM.Core.Cloud;

namespace LoLAM.Module;

public sealed class ModuleEntry : IGGLauncherModule
{
    private IModuleHost? _host;

    internal IAuthService? Auth { get; private set; }
    internal ICloudAccountStore? Store { get; private set; }
    internal IPresenceService? Presence { get; private set; }
    internal IAuthSessionStore? SessionStore { get; private set; }

    /// <summary>Set by LoginPage / MainPage after a successful auth so Dispose can set offline.</summary>
    internal AuthSession? ActiveSession { get; set; }

    public string Id => "lol-account-manager";
    public string DisplayName => "LoL Account Manager";

    public void Initialize(IModuleHost host)
    {
        _host = host;

        var opts = new FirebaseOptions(
            ApiKey: "AIzaSyDeAnFmCEZSejefChCer1V42TsMBSzFJPI",
            ProjectId: "lolaccountmanager"
        );

        // Persisted login session file (refresh token).
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(appData, "GGLauncher", "LoLAM", "auth.bin");

        SessionStore = new FileAuthSessionStore(path);

        Auth = new FirebaseAuthService(opts, SessionStore);
        Store = new FirestoreCloudAccountStore(opts);
        Presence = new FirestorePresenceService(opts);
    }

    public UserControl CreateRootView()
        => new LoginPage(this);

    public void Dispose()
    {
        // Best-effort: mark user offline when the module is unloaded.
        if (ActiveSession is not null && Presence is not null)
        {
            // Fire-and-forget; we can't await in Dispose.
            _ = Presence.SetOfflineAsync(ActiveSession);
        }
    }
}
