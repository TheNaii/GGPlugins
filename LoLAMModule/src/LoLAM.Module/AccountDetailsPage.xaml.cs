using LoLAM.Core.Models;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LoLAM.Module;

/// <summary>
/// Full account detail editor. Opened when the user clicks "Details" on a list row.
/// Navigates back to MainPage when the user clicks Back (or after saving).
/// </summary>
public partial class AccountDetailsPage : UserControl, INotifyPropertyChanged
{
    private readonly MainPage _mainPage;
    private readonly Account _account;
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

    public AccountDetailsPage(MainPage mainPage, Account account)
    {
        InitializeComponent();
        DataContext = this;

        _mainPage = mainPage;
        _account = account;

        PopulateFields();
    }

    // ──────────────────────────────────────────
    // Populate UI from model
    // ──────────────────────────────────────────

    private void PopulateFields()
    {
        _suppressEvents = true;

        SummonerNameBox.Text = _account.SummonerName ?? "";

        // Riot tag: strip leading # for the text box (the XAML shows a fixed # badge)
        var tag = _account.RiotTag ?? "";
        RiotTagBox.Text = tag.StartsWith('#') ? tag[1..] : tag;

        SelectComboByTag(ServerCombo, _account.Server ?? "");

        UsernameBox.Text = _account.Username ?? "";

        PasswordBox.Password = _account.Password ?? "";
        PasswordRevealBox.Text = _account.Password ?? "";

        // Tier / Division / GamesPlayed — read-only, just display
        SelectComboByTag(TierCombo, _account.Tier ?? "Unranked");
        SelectComboByTag(DivisionCombo, _account.Division ?? "");
        GamesPlayedBox.Text = _account.GamesPlayedThisSplit.ToString();

        XboxLinkedBox.IsChecked = _account.IsXboxLinked;
        CreatedAtBox.Text = _account.CreatedAt == default ? "" : _account.CreatedAt.ToString("yyyy-MM-dd HH:mm");
        LastLoginBox.Text = _account.LastLogin.HasValue ? _account.LastLogin.Value.ToString("yyyy-MM-dd HH:mm") : "";

        _suppressEvents = false;
        IsDirty = false;
        UpdateSaveStatus();
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
        // Fallback to first item
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

        // Re-attach the # prefix when storing
        var tagText = RiotTagBox.Text.Trim();
        _account.RiotTag = tagText.Length > 0 && !tagText.StartsWith('#')
            ? "#" + tagText
            : tagText;

        // Server comes from the dropdown tag
        _account.Server = SelectedTag(ServerCombo);

        _account.Username = UsernameBox.Text.Trim();
        _account.Password = _revealPassword ? PasswordRevealBox.Text : PasswordBox.Password;

        // Tier / Division / GamesPlayed are NOT written here — they are auto-updated via API
        _account.IsXboxLinked = XboxLinkedBox.IsChecked ?? false;
    }

    // ──────────────────────────────────────────
    // Change handlers (only for editable fields)
    // ──────────────────────────────────────────

    private void Field_TextChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        IsDirty = true;
    }

    private void Field_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        IsDirty = true;
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
        ApplyToModel();
        IsDirty = false;

        await _mainPage.OnDetailsPageSavedAsync("Saved account details.");
        _mainPage.RebuildServersPublic();

        NavigateBack();
    }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        PopulateFields();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (IsDirty)
        {
            var result = MessageBox.Show(
                "You have unsaved changes. Discard them and go back?",
                "Unsaved Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;
        }

        NavigateBack();
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
        SaveStatus.Text = IsDirty ? "Unsaved changes" : "";
    }

    // ──────────────────────────────────────────
    // INotifyPropertyChanged
    // ──────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
