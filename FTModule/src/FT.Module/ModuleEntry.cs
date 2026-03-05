using System;
using System.IO;
using System.Windows.Controls;
using GGLauncher.ModuleContracts;
using FT.Core.Services;

namespace FT.Module;

public sealed class ModuleEntry : IGGLauncherModule
{
    private IModuleHost? _host;

    internal TranslationService Translation { get; } = new();
    internal SettingsStore? Settings { get; private set; }

    public string Id => "flame-translator";
    public string DisplayName => "Flame Translator";

    public void Initialize(IModuleHost host)
    {
        _host = host;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dataFolder = Path.Combine(appData, "GGLauncher", "FlameTranslator");

        Settings = new SettingsStore(dataFolder);

        // Restore preferred provider from last session
        var cfg = Settings.Load();
        Translation.SetPreferred(cfg.PreferredProvider);
    }

    public UserControl CreateRootView()
        => new MainPage(this);

    public void Dispose() { }
}
