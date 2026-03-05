using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LoLAM.Core.Cloud;

namespace LoLAM.Module;

public partial class LoginPage : UserControl
{
    private readonly ModuleEntry _module;
    private bool _busy;

    public LoginPage(ModuleEntry module)
    {
        InitializeComponent();
        _module = module;

        Loaded += async (_, _) =>
        {
            EmailBox.Focus();
            await TryAutoLoginAsync();
        };
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
        => await AuthFlowAsync(isRegister: false);

    private async void Register_Click(object sender, RoutedEventArgs e)
        => await AuthFlowAsync(isRegister: true);

    private async void Page_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !_busy)
        {
            e.Handled = true;
            await AuthFlowAsync(isRegister: false);
        }
    }

    private async Task TryAutoLoginAsync()
    {
        if (_busy) return;

        try
        {
            if (_module.Auth is null || _module.Store is null || _module.Presence is null)
            {
                LogDebug("TryAutoLogin: services null");
                return;
            }

            LogDebug("TryAutoLogin: calling TryRestoreSessionAsync...");
            var session = await _module.Auth.TryRestoreSessionAsync();
            if (session is null)
            {
                LogDebug("TryAutoLogin: session null (no saved session or refresh failed)");
                return;
            }

            LogDebug($"TryAutoLogin: restored for {session.Email}");
            SetBusy(true);
            ShowStatus("Restoring session…");

            await _module.Presence.SetOnlineAsync(session);
            _module.ActiveSession = session;
            var accountsJson = await _module.Store.DownloadAccountsJsonAsync(session);

            LogDebug("TryAutoLogin: navigating to main");
            NavigateToMain(session, accountsJson);
        }
        catch (Exception ex)
        {
            LogDebug($"TryAutoLogin FAILED: {ex}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static void LogDebug(string msg)
    {
        try
        {
            var logDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GGLauncherDev", "logs");
            System.IO.Directory.CreateDirectory(logDir);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(logDir, "lolam-auth-debug.log"),
                $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
        }
        catch { }
    }

    private async Task AuthFlowAsync(bool isRegister)
    {
        if (_busy) return;

        try
        {
            SetBusy(true);
            HideStatus();

            var email = (EmailBox.Text ?? "").Trim();
            var pass = PasswordBox.Password ?? "";

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(pass))
            {
                ShowStatus("Please enter email and password.");
                return;
            }

            if (_module.Auth is null || _module.Store is null || _module.Presence is null)
            {
                ShowStatus("Module services not initialized.");
                return;
            }

            AuthSession session = isRegister
                ? await _module.Auth.RegisterAsync(email, pass)
                : await _module.Auth.LoginAsync(email, pass);

            // Handle "Remember Me": if unchecked, clear the persisted session so
            // the user won't be auto-logged-in next time.
            if (RememberMeBox.IsChecked != true)
            {
                _module.AuthConcrete?.ClearSession();
            }

            await _module.Presence.SetOnlineAsync(session);
            _module.ActiveSession = session;

            var accountsJson = await _module.Store.DownloadAccountsJsonAsync(session);

            NavigateToMain(session, accountsJson);
        }
        catch (Exception ex)
        {
            ShowStatus(ToFriendlyAuthError(ex));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ForgotPassword_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        try
        {
            SetBusy(true);
            HideStatus();

            var email = (EmailBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(email))
            {
                ShowStatus("Enter your email first.");
                return;
            }

            if (_module.Auth is null)
            {
                ShowStatus("Module services not initialized.");
                return;
            }

            await _module.Auth.SendPasswordResetAsync(email);
            ShowStatus("If an account exists for that email, a reset link has been sent.");
        }
        catch (Exception ex)
        {
            ShowStatus(ToFriendlyPasswordResetError(ex));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void NavigateToMain(AuthSession session, string accountsJson)
    {
        var next = new MainPage(_module, session, accountsJson);
        var host = FindHostContentControl();
        if (host is null)
        {
            ShowStatus("Could not navigate (host not found).");
            return;
        }
        host.Content = next;
    }

    private ContentControl? FindHostContentControl()
    {
        DependencyObject current = this;
        while (true)
        {
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            if (current is null) return null;
            if (current is ContentControl cc) return cc;
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        EmailBox.IsEnabled = !busy;
        PasswordBox.IsEnabled = !busy;
        LoginButton.IsEnabled = !busy;
        RegisterButton.IsEnabled = !busy;
        ForgotPasswordButton.IsEnabled = !busy;
        RememberMeBox.IsEnabled = !busy;

        if (busy)
            ShowStatus("Working…");
        else if (StatusText.Text == "Working…" || StatusText.Text == "Restoring session…")
            HideStatus();
    }

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusContainer.Visibility = Visibility.Visible;
    }

    private void HideStatus()
    {
        StatusText.Text = "";
        StatusContainer.Visibility = Visibility.Collapsed;
    }

    private static string ToFriendlyAuthError(Exception ex)
    {
        var msg = ex.Message ?? "";

        if (msg.Contains("INVALID_LOGIN_CREDENTIALS", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("INVALID_PASSWORD", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("EMAIL_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
            return "Incorrect email or password.";

        if (msg.Contains("TOO_MANY_ATTEMPTS_TRY_LATER", StringComparison.OrdinalIgnoreCase))
            return "Too many attempts. Try again later.";

        if (msg.Contains("WEAK_PASSWORD", StringComparison.OrdinalIgnoreCase))
            return "Password is too weak.";

        if (msg.Contains("EMAIL_EXISTS", StringComparison.OrdinalIgnoreCase))
            return "An account with this email already exists.";

        if (msg.Contains("INVALID_EMAIL", StringComparison.OrdinalIgnoreCase))
            return "That email address looks invalid.";

        return "Could not sign in right now. Please try again.";
    }

    private static string ToFriendlyPasswordResetError(Exception ex)
    {
        var msg = ex.Message ?? "";

        if (msg.Contains("INVALID_EMAIL", StringComparison.OrdinalIgnoreCase))
            return "That email address looks invalid.";

        if (msg.Contains("TOO_MANY_ATTEMPTS_TRY_LATER", StringComparison.OrdinalIgnoreCase))
            return "Too many attempts. Try again later.";

        return "Could not send reset email right now. Please try again.";
    }
}
