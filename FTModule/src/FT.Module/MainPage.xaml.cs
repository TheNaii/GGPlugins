using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace FT.Module;

public partial class MainPage : UserControl
{
    private readonly ModuleEntry _module;
    private CancellationTokenSource? _cts;

    public MainPage(ModuleEntry module)
    {
        InitializeComponent();
        _module = module;

        // Restore last used style
        var cfg = _module.Settings?.Load();
        if (cfg is not null)
        {
            SelectComboByContent(StyleCombo, cfg.LastStyle);
            SelectComboByTag(ProviderCombo, cfg.PreferredProvider);
        }

        if (StyleCombo.SelectedItem is null && StyleCombo.Items.Count > 0)
            StyleCombo.SelectedIndex = 0;
        if (ProviderCombo.SelectedItem is null && ProviderCombo.Items.Count > 0)
            ProviderCombo.SelectedIndex = 0;
    }

    private async void Translate_Click(object sender, RoutedEventArgs e)
    {
        var input = InputBox.Text.Trim();
        if (string.IsNullOrEmpty(input))
        {
            StatusText.Text = "Enter some text first.";
            return;
        }

        var styleTag = (StyleCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Shakespeare";
        var providerName = (ProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Mistral";

        // Ensure the preferred provider matches the dropdown
        _module.Translation.SetPreferred(providerName);

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        TranslateBtn.IsEnabled = false;
        OutputBox.Text = "";
        StatusText.Text = $"Translating via {providerName}...";

        try
        {
            var result = await _module.Translation.TranslateAsync(input, styleTag, ct);

            if (!ct.IsCancellationRequested)
            {
                OutputBox.Text = result;
                var usedProvider = _module.Translation.LastUsedProvider ?? "?";

                StatusText.Text = usedProvider == providerName
                    ? $"Done — via {usedProvider}"
                    : $"Done — {providerName} was rate-limited, used {usedProvider} instead";
            }
        }
        catch (TaskCanceledException) { }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            TranslateBtn.IsEnabled = true;
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var text = OutputBox.Text;
        if (!string.IsNullOrEmpty(text))
        {
            Clipboard.SetText(text);
            StatusText.Text = "Copied!";
        }
    }

    private void Provider_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_module?.Settings is null) return;
        var tag = (ProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (tag is null) return;

        _module.Translation.SetPreferred(tag);

        var cfg = _module.Settings.Load();
        cfg.PreferredProvider = tag;
        _module.Settings.Save(cfg);
    }

    private void Style_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_module?.Settings is null) return;
        var name = (StyleCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (name is null) return;

        var cfg = _module.Settings.Load();
        cfg.LastStyle = name;
        _module.Settings.Save(cfg);
    }

    // ── Helpers ──

    private static void SelectComboByContent(ComboBox combo, string content)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Content?.ToString(), content, StringComparison.OrdinalIgnoreCase))
            { combo.SelectedItem = item; return; }
        }
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            { combo.SelectedItem = item; return; }
        }
    }
}
