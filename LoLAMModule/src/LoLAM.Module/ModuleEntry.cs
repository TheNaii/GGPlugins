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
        // Later: set offline on unload when you track current session.
    }
}
