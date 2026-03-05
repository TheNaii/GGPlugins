using System;
using System.Windows;
using System.Windows.Controls;

namespace LoLAM.Module;

/// <summary>
/// Reusable confirm dialog overlay that matches the GGLauncher dialog style.
/// Embed this in a Grid and call Show() to display it.
/// </summary>
public partial class ConfirmOverlay : UserControl
{
    private Action? _onConfirm;

    public ConfirmOverlay()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shows the confirm dialog with the given title, message, and confirm callback.
    /// </summary>
    public void Show(string title, string message, Action onConfirm)
    {
        TitleText.Text = title;
        MessageText.Text = message;
        _onConfirm = onConfirm;
        Visibility = Visibility.Visible;
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        _onConfirm = null;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var callback = _onConfirm;
        Hide();
        callback?.Invoke();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }
}
