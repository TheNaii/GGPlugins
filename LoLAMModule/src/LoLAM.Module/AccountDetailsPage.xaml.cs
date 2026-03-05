using LoLAM.Core.Models;
using LoLAM.Core.Riot;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LoLAM.Module;

/// <summary>
/// Full account detail editor. Supports both editing existing accounts
/// and creating new accounts (isNewAccount mode).
/// </summary>
public partial class AccountDetailsPage : UserControl, INotifyPropertyChanged
{
    private readonly MainPage _mainPage;
    private readonly Account _account;
    private readonly bool _isNewAccount;
    private bool _suppressEvents;
    private bool _isDirty;
    private bool _revealPassword;

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (_isDirty == value) return;
            _isDirty = value;
            OnPropertyChanged();
            UpdateSaveStatus();
        }
    }

    public AccountDetailsPage(MainPage mainPage, Account account, bool isNewAccount = false)
    {
        InitializeComponent();
        DataContext = this;

        _mainPage = mainPage;
        _account = account;
        _isNewAccount = isNewAccount;

        if (_isNewAccount)
        {
            PageTitle.Text = "New Account";
            SaveButton.Content = "Add Account";
            // New accounts start as "dirty" so Save button is always enabled
            IsDirty = true;
        }

        PopulateFields();
    }

    // ──────────────────────────────────────────
    // Populate UI from model
    // ──────────────────────────────────────────

    private void PopulateFields()
    {
        _suppressEvents = true;

        SummonerNameBox.Text = _account.SummonerName ?? "";

        var tag = _account.RiotTag ?? "";
        RiotTagBox.Text = tag.StartsWith('#') ? tag[1..] : tag;

        SelectComboByTag(ServerCombo, _account.Server ?? "");

        UsernameBox.Text = _account.Username ?? "";

        PasswordBox.Password = _account.Password ?? "";
        PasswordRevealBox.Text = _account.Password ?? "";

        SelectComboByTag(TierCombo, _account.Tier ?? "Unranked");
        SelectComboByTag(DivisionCombo, _account.Division ?? "");
        GamesPlayedBox.Text = _account.GamesPlayedThisSplit.ToString();

        XboxLinkedBox.IsChecked = _account.IsXboxLinked;
        CreatedAtBox.Text = _account.CreatedAt == default ? "" : _account.CreatedAt.ToString("yyyy-MM-dd HH:mm");
        LastLoginBox.Text = _account.LastLogin.HasValue ? _account.LastLogin.Value.ToString("yyyy-MM-dd HH:mm") : "";

        _suppressEvents = false;

        if (!_isNewAccount)
        {
            IsDirty = false;
        }

        UpdateSaveStatus();
        HideValidation();
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    private static string SelectedTag(ComboBox combo)
        => (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";

    // ──────────────────────────────────────────
    // Write UI back to model
    // ──────────────────────────────────────────

    private void ApplyToModel()
    {
        _account.SummonerName = SummonerNameBox.Text.Trim();

        var tagText = RiotTagBox.Text.Trim();
        _account.RiotTag = tagText.Length > 0 && !tagText.StartsWith('#')
            ? "#" + tagText
            : tagText;

        _account.Server = SelectedTag(ServerCombo);
        _account.Username = UsernameBox.Text.Trim();
        _account.Password = _revealPassword ? PasswordRevealBox.Text : PasswordBox.Password;
        _account.IsXboxLinked = XboxLinkedBox.IsChecked ?? false;
    }

    // ──────────────────────────────────────────
    // Validation
    // ──────────────────────────────────────────

    /// <summary>
    /// Validates the form. Returns an error message, or null if valid.
    /// </summary>
    private string? Validate()
    {
        var name = SummonerNameBox.Text.Trim();
        var tag = RiotTagBox.Text.Trim();
        var server = SelectedTag(ServerCombo);

        if (string.IsNullOrWhiteSpace(name))
            return "Summoner Name is required.";

        if (string.IsNullOrWhiteSpace(tag))
            return "Riot Tag is required.";

        if (string.IsNullOrWhiteSpace(server))
            return "Please select a server.";

        // Duplicate check: name + tag must be unique
        var excludeForDuplicateCheck = _isNewAccount ? null : _account;
        if (_mainPage.AccountExists(name, tag, excludeForDuplicateCheck))
            return $"An account with the name {name}#{tag} already exists.";

        return null;
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationBanner.Visibility = Visibility.Visible;
    }

    private void HideValidation()
    {
        ValidationText.Text = "";
        ValidationBanner.Visibility = Visibility.Collapsed;
    }

    // ──────────────────────────────────────────
    // Change handlers
    // ──────────────────────────────────────────

    private void Field_TextChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        IsDirty = true;
        HideValidation();
    }

    private void Field_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        IsDirty = true;
        HideValidation();
    }

    private void Field_CheckChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        IsDirty = true;
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        if (!_revealPassword)
            PasswordRevealBox.Text = PasswordBox.Password;
        IsDirty = true;
    }

    private void PasswordReveal_TextChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        if (_revealPassword)
            PasswordBox.Password = PasswordRevealBox.Text;
        IsDirty = true;
    }

    // ──────────────────────────────────────────
    // Button handlers
    // ──────────────────────────────────────────

    private void RevealPassword_Click(object sender, RoutedEventArgs e)
    {
        _revealPassword = !_revealPassword;

        if (_revealPassword)
        {
            PasswordRevealBox.Text = PasswordBox.Password;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordRevealBox.Visibility = Visibility.Visible;
            RevealIcon.Text = "🙈";
        }
        else
        {
            PasswordBox.Password = PasswordRevealBox.Text;
            PasswordRevealBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            RevealIcon.Text = "👁";
        }
    }

    private void CopyUsername_Click(object sender, RoutedEventArgs e)
    {
        var text = UsernameBox.Text;
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
    }

    private void CopyPassword_Click(object sender, RoutedEventArgs e)
    {
        var text = _revealPassword ? PasswordRevealBox.Text : PasswordBox.Password;
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        // Validate form fields first
        var error = Validate();
        if (error is not null)
        {
            ShowValidation(error);
            return;
        }

        // Check Xbox uniqueness per server
        if ((XboxLinkedBox.IsChecked ?? false))
        {
            var server = SelectedTag(ServerCombo);
            var exclude = _isNewAccount ? null : _account;
            if (_mainPage.IsXboxLinkedOnServer(server, exclude))
            {
                ShowValidation($"Another account on {server} already has Xbox Game Pass linked. Only one account per server can have it.");
                return;
            }
        }

        HideValidation();

        // For new accounts, verify the summoner actually exists via Riot API
        if (_isNewAccount && _mainPage.RiotApi is not null)
        {
            var name = SummonerNameBox.Text.Trim();
            var tag = RiotTagBox.Text.Trim().TrimStart('#');
            var server = SelectedTag(ServerCombo);

            SaveButton.IsEnabled = false;
            SaveStatus.Text = "Verifying account...";

            try
            {
                var info = await _mainPage.RiotApi.GetAccountByRiotIdAsync(name, tag, server);
                if (info is null)
                {
                    ShowValidation($"Account {name}#{tag} was not found on {server}. Check the summoner name, tag, and server.");
                    SaveButton.IsEnabled = true;
                    SaveStatus.Text = "";
                    return;
                }

                // Store the resolved PUUID so we don't have to look it up again
                _account.Puuid = info.Puuid;
            }
            catch
            {
                ShowValidation("Could not verify account. Check your internet connection and try again.");
                SaveButton.IsEnabled = true;
                SaveStatus.Text = "";
                return;
            }

            SaveButton.IsEnabled = true;
            SaveStatus.Text = "";
        }

        ApplyToModel();

        if (_isNewAccount)
        {
            _account.CreatedAt = DateTime.Now;
            _mainPage.AddNewAccount(_account);
            _mainPage.RebuildServersPublic();
            await _mainPage.OnDetailsPageSavedAsync("Added new account.");
        }
        else
        {
            await _mainPage.OnDetailsPageSavedAsync("Saved account details.");
            _mainPage.RebuildServersPublic();
        }

        IsDirty = false;
        NavigateBack();
    }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        if (_isNewAccount)
        {
            // Discard on a new account = just go back without adding
            NavigateBack();
        }
        else
        {
            PopulateFields();
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (!IsDirty || (_isNewAccount && !HasUserEnteredAnything()))
        {
            NavigateBack();
            return;
        }

        ConfirmDialog.Show(
            _isNewAccount ? "Discard" : "Unsaved",
            _isNewAccount
                ? "Discard this new account and go back?"
                : "You have unsaved changes. Discard them and go back?",
            () => NavigateBack());
    }

    /// <summary>
    /// Checks whether the user has typed anything meaningful in the new account form.
    /// Used to decide whether to show a confirmation prompt on back.
    /// </summary>
    private bool HasUserEnteredAnything()
    {
        return !string.IsNullOrWhiteSpace(SummonerNameBox.Text)
            || !string.IsNullOrWhiteSpace(RiotTagBox.Text)
            || !string.IsNullOrWhiteSpace(UsernameBox.Text)
            || PasswordBox.Password.Length > 0;
    }

    private void NavigateBack()
    {
        DependencyObject current = this;
        while (true)
        {
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            if (current is null) return;

            if (current is ContentControl cc)
            {
                cc.Content = _mainPage;
                return;
            }
        }
    }

    private void UpdateSaveStatus()
    {
        if (_isNewAccount)
            SaveStatus.Text = "";
        else
            SaveStatus.Text = IsDirty ? "Unsaved changes" : "";
    }

    // ──────────────────────────────────────────
    // INotifyPropertyChanged
    // ──────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
