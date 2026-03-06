using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace CD.Module;

public partial class JoinPage : UserControl
{
    private readonly ModuleEntry _module;
    private string _selectedRole = "fill";

    private readonly Dictionary<string, ToggleButton> _roleButtons;

    public JoinPage(ModuleEntry module)
    {
        InitializeComponent();
        _module = module;

        _roleButtons = new Dictionary<string, ToggleButton>
        {
            ["top"] = RoleTop,
            ["jungle"] = RoleJungle,
            ["mid"] = RoleMid,
            ["adc"] = RoleAdc,
            ["support"] = RoleSupport
        };
    }

    private void Role_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked) return;

        // Uncheck all others
        foreach (var btn in _roleButtons.Values)
        {
            if (btn != clicked)
                btn.IsChecked = false;
        }

        // Find which role was selected
        _selectedRole = "fill";
        foreach (var (role, btn) in _roleButtons)
        {
            if (btn.IsChecked == true)
            {
                _selectedRole = role;
                break;
            }
        }
    }

    private async void Continue_Click(object sender, RoutedEventArgs e)
    {
        var username = UsernameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            ShowError("Enter a username.");
            return;
        }

        if (username.Length > 16)
        {
            ShowError("Username must be 16 characters or less.");
            return;
        }

        HideError();
        ContinueBtn.IsEnabled = false;
        StatusText.Text = "Connecting...";

        try
        {
            // Anonymous auth
            var session = await _module.Auth.SignInAnonymouslyAsync();
            session.Username = username;
            session.Role = _selectedRole;
            _module.Session = session;

            // Preload champion data in background
            if (_module.Champions is not null)
            {
                StatusText.Text = "Loading champions...";
                await _module.Champions.LoadAsync();
                _ = _module.Champions.PreloadAllIconsAsync(); // fire and forget
            }

            // Navigate to lobby browser
            NavigateTo(new LobbyBrowserPage(_module));
        }
        catch (Exception ex)
        {
            ShowError($"Connection failed: {ex.Message}");
            ContinueBtn.IsEnabled = true;
            StatusText.Text = "";
        }
    }

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void NavigateTo(UserControl page)
    {
        DependencyObject current = this;
        while (true)
        {
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            if (current is null) return;
            if (current is ContentControl cc)
            {
                cc.Content = page;
                return;
            }
        }
    }
}
