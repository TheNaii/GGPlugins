using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace LoLAM.Module;

public partial class LaunchWarningOverlay : UserControl
{
    private Action? _onProceed;

    private static readonly string DismissFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GGLauncher", "LoLAM", "launch-warning-dismissed");

    public LaunchWarningOverlay()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shows the warning overlay. If the user previously chose "Don't show again",
    /// immediately invokes onProceed without showing the dialog.
    /// </summary>
    public void Show(Action onProceed)
    {
        _onProceed = onProceed;

        if (IsDismissed())
        {
            _onProceed?.Invoke();
            return;
        }

        // Reset state each time
        UnderstandCheckBox.IsChecked = false;
        GotItButton.IsEnabled = false;
        DontShowButton.IsEnabled = false;

        Visibility = Visibility.Visible;
    }

    private void Hide()
    {
        Visibility = Visibility.Collapsed;
        _onProceed = null;
    }

    private void UnderstandCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        var isChecked = UnderstandCheckBox.IsChecked == true;
        GotItButton.IsEnabled = isChecked;
        DontShowButton.IsEnabled = isChecked;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void GotIt_Click(object sender, RoutedEventArgs e)
    {
        var action = _onProceed;
        Hide();
        action?.Invoke();
    }

    private void DontShowAgain_Click(object sender, RoutedEventArgs e)
    {
        SaveDismissed();
        var action = _onProceed;
        Hide();
        action?.Invoke();
    }

    private static bool IsDismissed()
    {
        try { return File.Exists(DismissFilePath); }
        catch { return false; }
    }

    private static void SaveDismissed()
    {
        try
        {
            var dir = Path.GetDirectoryName(DismissFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(DismissFilePath, "1");
        }
        catch { }
    }
}
