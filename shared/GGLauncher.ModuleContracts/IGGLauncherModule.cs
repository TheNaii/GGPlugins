using System;
using System.Windows.Controls;

namespace GGLauncher.ModuleContracts;

public interface IGGLauncherModule : IDisposable
{
    string Id { get; }
    void Initialize(IModuleHost host);
    UserControl CreateRootView();
    void OnActivated() { }
    void OnDeactivated() { }
}

public interface IModuleHost
{
    IServiceProvider Services { get; }
    string GetAppDataPath(string appId);
}