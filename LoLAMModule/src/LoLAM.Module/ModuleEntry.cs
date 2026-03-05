using System;
using System.IO;
using System.Windows.Controls;
using GGLauncher.ModuleContracts;
using LoLAM.Core.Cloud;
using LoLAM.Core.Riot;

namespace LoLAM.Module;

public sealed class ModuleEntry : IGGLauncherModule
{
    private IModuleHost? _host;

    internal IAuthService? Auth { get; private set; }
    internal FirebaseAuthService? AuthConcrete { get; private set; }
    internal ICloudAccountStore? Store { get; private set; }
    internal IPresenceService? Presence { get; private set; }
    internal IAuthSessionStore? SessionStore { get; private set; }
    internal IRiotApiService? RiotApi { get; private set; }

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

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dataFolder = Path.Combine(appData, "GGLauncher", "LoLAM");
        Directory.CreateDirectory(dataFolder);

        var authPath = Path.Combine(dataFolder, "auth.bin");

        SessionStore = new FileAuthSessionStore(authPath);

        var authService = new FirebaseAuthService(opts, SessionStore);
        Auth = authService;
        AuthConcrete = authService;

        Store = new FirestoreCloudAccountStore(opts);
        Presence = new FirestorePresenceService(opts);
        RiotApi = new RiotApiService();
    }

    public UserControl CreateRootView()
        => new LoginPage(this);

    public void Dispose()
    {
        if (ActiveSession is not null && Presence is not null)
        {
            _ = Presence.SetOfflineAsync(ActiveSession);
        }
    }
}
