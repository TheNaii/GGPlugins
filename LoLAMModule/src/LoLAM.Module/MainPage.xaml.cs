using LoLAM.Core.Cloud;
using LoLAM.Core.Models;
using LoLAM.Core.Riot;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace LoLAM.Module;

public partial class MainPage : UserControl, INotifyPropertyChanged
{
    private readonly ModuleEntry _module;
    private readonly AuthSession _session;

    public ObservableCollection<Account> AllAccounts { get; } = new();
    public ICollectionView AccountsView { get; }
    public ObservableCollection<string> Servers { get; } = new();

    private string _selectedServer = "";
    public string SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (Set(ref _selectedServer, value))
                AccountsView.Refresh();
        }
    }

    private Account? _selectedAccount;
    public Account? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (Set(ref _selectedAccount, value))
                OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => SelectedAccount is not null;

    private bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (Set(ref _isDirty, value))
            {
                OnPropertyChanged(nameof(CanManualSave));
                UpdateSaveStatus();
            }
        }
    }

    public bool CanManualSave => IsDirty && !_saving;

    private bool _saving;
    private string _saveStatusText = "";
    public string SaveStatusText
    {
        get => _saveStatusText;
        private set => Set(ref _saveStatusText, value);
    }

    private string _refreshStatusText = "";
    public string RefreshStatusText
    {
        get => _refreshStatusText;
        private set => Set(ref _refreshStatusText, value);
    }

    private bool _refreshing;
    public bool CanRefresh => !_refreshing;

    public string SignedInText { get; }

    private string _selectedSort = "None";
    public string SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (Set(ref _selectedSort, value))
                ApplySort();
        }
    }

    private enum JsonShape { Array, WrapperAccounts, WrapperAccountsCapitalized, UnknownObject }
    private JsonShape _jsonShape = JsonShape.Array;
    private JObject? _jsonWrapperRoot;

    public MainPage(ModuleEntry module, AuthSession session, string accountsJson)
    {
        InitializeComponent();

        _module = module;
        _session = session;

        SignedInText = $"Signed in as {session.Email}";
        DataContext = this;

        AccountsView = CollectionViewSource.GetDefaultView(AllAccounts);
        AccountsView.Filter = o => o is Account a && IsAccountInSelectedServer(a);

        LoadFromJson(accountsJson);
        BuildServers();

        SelectedServer = "";
        SelectedAccount = null;

        ApplySort();
        UpdateSaveStatus();

        // Auto-refresh ranks on load
        Loaded += async (_, _) => await TryAutoRefreshAsync();
    }

    // ──────────────────────────────────────────
    // Server / JSON helpers
    // ──────────────────────────────────────────

    private bool IsAccountInSelectedServer(Account a)
    {
        if (string.IsNullOrWhiteSpace(SelectedServer)) return false;
        return string.Equals((a.Server ?? "").Trim(), SelectedServer, StringComparison.OrdinalIgnoreCase);
    }

    private void BuildServers()
    {
        Servers.Clear();

        var servers = AllAccounts
            .Select(a => (a.Server ?? "").Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s);

        foreach (var s in servers)
            Servers.Add(s);

        if (!string.IsNullOrWhiteSpace(SelectedServer) && !Servers.Contains(SelectedServer))
            SelectedServer = "";
    }

    private void LoadFromJson(string accountsJson)
    {
        AllAccounts.Clear();

        if (string.IsNullOrWhiteSpace(accountsJson)) return;

        try
        {
            var token = JToken.Parse(accountsJson);

            if (token is JArray arr)
            {
                _jsonShape = JsonShape.Array;
                foreach (var a in arr.ToObject<Account[]>() ?? Array.Empty<Account>())
                    AllAccounts.Add(a);
                return;
            }

            if (token is JObject obj)
            {
                if (obj.TryGetValue("accounts", StringComparison.OrdinalIgnoreCase, out var accountsToken) && accountsToken is JArray a1)
                {
                    _jsonWrapperRoot = obj;
                    _jsonShape = obj.Property("accounts") is not null ? JsonShape.WrapperAccounts : JsonShape.WrapperAccountsCapitalized;

                    foreach (var a in a1.ToObject<Account[]>() ?? Array.Empty<Account>())
                        AllAccounts.Add(a);
                    return;
                }

                var firstArrayProp = obj.Properties().FirstOrDefault(p => p.Value is JArray);
                if (firstArrayProp?.Value is JArray anyArr)
                {
                    _jsonWrapperRoot = obj;
                    _jsonShape = JsonShape.UnknownObject;

                    foreach (var a in anyArr.ToObject<Account[]>() ?? Array.Empty<Account>())
                        AllAccounts.Add(a);
                    return;
                }
            }
        }
        catch { }
    }

    private string SerializeToJson()
    {
        var accountsArray = JArray.FromObject(AllAccounts);

        return _jsonShape switch
        {
            JsonShape.Array => accountsArray.ToString(Formatting.None),
            JsonShape.WrapperAccounts or JsonShape.WrapperAccountsCapitalized when _jsonWrapperRoot is not null
                => WithWrapper(_jsonWrapperRoot, "accounts", accountsArray).ToString(Formatting.None),
            JsonShape.UnknownObject when _jsonWrapperRoot is not null
                => ReplaceFirstArray(_jsonWrapperRoot, accountsArray).ToString(Formatting.None),
            _ => accountsArray.ToString(Formatting.None),
        };

        static JObject WithWrapper(JObject root, string key, JArray accounts)
        {
            var prop = root.Properties().FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));
            if (prop is not null) prop.Value = accounts;
            else root[key] = accounts;
            return root;
        }

        static JObject ReplaceFirstArray(JObject root, JArray accounts)
        {
            var firstArrayProp = root.Properties().FirstOrDefault(p => p.Value is JArray);
            if (firstArrayProp is not null) firstArrayProp.Value = accounts;
            else root["accounts"] = accounts;
            return root;
        }
    }

    private void ApplySort()
    {
        if (AccountsView is null) return;

        AccountsView.SortDescriptions.Clear();

        switch (SelectedSort)
        {
            case "NameAsc":
                AccountsView.SortDescriptions.Add(new SortDescription(nameof(Account.SummonerName), ListSortDirection.Ascending));
                break;
            case "LastLoginDesc":
                AccountsView.SortDescriptions.Add(new SortDescription(nameof(Account.LastLogin), ListSortDirection.Descending));
                break;
            case "GamesPlayedDesc":
                AccountsView.SortDescriptions.Add(new SortDescription(nameof(Account.GamesPlayedThisSplit), ListSortDirection.Descending));
                break;
            case "CreatedDesc":
                AccountsView.SortDescriptions.Add(new SortDescription(nameof(Account.CreatedAt), ListSortDirection.Descending));
                break;
        }
    }

    // ──────────────────────────────────────────
    // Button handlers
    // ──────────────────────────────────────────

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var newAccount = new Account
        {
            Server = string.IsNullOrWhiteSpace(SelectedServer) ? "" : SelectedServer,
            SummonerName = "",
            RiotTag = "",
            Tier = "Unranked",
            Division = ""
        };

        var detailsPage = new AccountDetailsPage(this, newAccount, isNewAccount: true);
        NavigateToPage(detailsPage);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is null) return;

        var account = SelectedAccount;
        var name = $"{account.SummonerName}{account.RiotTag}";

        ConfirmDialog.Show(
            "Delete Account",
            $"Are you sure you want to delete {name}? This cannot be undone.",
            async () =>
            {
                AllAccounts.Remove(account);
                SelectedAccount = AllAccounts.FirstOrDefault(IsAccountInSelectedServer) ?? AllAccounts.FirstOrDefault();
                AccountsView.Refresh();
                await AutosaveAsync("Removed account.");
            });
    }

    private void OpenDetails_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is null) return;

        var detailsPage = new AccountDetailsPage(this, SelectedAccount, isNewAccount: false);
        NavigateToPage(detailsPage);
    }

    private void LaunchClient_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is null) return;

        var account = SelectedAccount;

        LaunchWarning.Show(async () =>
        {
            SaveStatusText = "Launching Riot Client\u2026";

            bool launched = await Task.Run(() =>
                RiotClientLauncher.LaunchAsync(account.Username, account.Password));

            if (launched)
            {
                account.LastLogin = DateTime.Now;
                SaveStatusText = "Client launched.";
                await AutosaveAsync("Updated last login.");
            }
            else
            {
                SaveStatusText = "Riot Client not found. Check installation.";
            }
        });
    }

    private void ServerTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Content is string server)
            SelectedServer = server;
    }

    // ──────────────────────────────────────────
    // Riot API: Refresh ranks & games played
    // ──────────────────────────────────────────

    private async void RefreshRanks_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAllAccountDataAsync();
    }

    private async Task TryAutoRefreshAsync()
    {
        if (_module.RiotApi is null) return;
        await RefreshAllAccountDataAsync();
    }

    private async Task RefreshAllAccountDataAsync()
    {
        if (_module.RiotApi is null)
        {
            RefreshStatusText = "Riot API not available.";
            return;
        }

        if (_refreshing) return;

        try
        {
            _refreshing = true;
            OnPropertyChanged(nameof(CanRefresh));

            var accounts = AllAccounts.ToArray();
            int total = accounts.Length;
            int done = 0;
            bool anyUpdated = false;

            foreach (var account in accounts)
            {
                done++;
                RefreshStatusText = $"Refreshing {done}/{total}\u2026";

                if (string.IsNullOrWhiteSpace(account.SummonerName) || string.IsNullOrWhiteSpace(account.Server))
                    continue;

                try
                {
                    var tag = (account.RiotTag ?? "").TrimStart('#');
                    if (string.IsNullOrWhiteSpace(tag)) continue;

                    // Step 1: Resolve PUUID if we don't have it
                    if (string.IsNullOrWhiteSpace(account.Puuid))
                    {
                        var info = await _module.RiotApi.GetAccountByRiotIdAsync(
                            account.SummonerName, tag, account.Server);

                        if (info is not null)
                        {
                            account.Puuid = info.Puuid;
                            anyUpdated = true;
                        }
                        else
                        {
                            continue;
                        }
                    }

                    // Step 2: Get ranked data
                    var rank = await _module.RiotApi.GetRankedInfoAsync(account.Puuid, account.Server);
                    if (rank is not null)
                    {
                        account.Tier = rank.Tier;
                        account.Division = rank.Division;
                        account.GamesPlayedThisSplit = rank.Wins + rank.Losses;
                        anyUpdated = true;
                    }
                    else
                    {
                        if (account.Tier != "Unranked")
                        {
                            account.Tier = "Unranked";
                            account.Division = "";
                            account.GamesPlayedThisSplit = 0;
                            anyUpdated = true;
                        }
                    }

                    // Small delay to stay under rate limits
                    await Task.Delay(250);
                }
                catch
                {
                    // Skip individual account failures
                }
            }

            AccountsView.Refresh();
            RefreshStatusText = $"Refreshed {total} accounts.";

            if (anyUpdated)
                await AutosaveAsync("Saved updated ranks.");
        }
        catch
        {
            RefreshStatusText = "Refresh failed.";
        }
        finally
        {
            _refreshing = false;
            OnPropertyChanged(nameof(CanRefresh));
        }
    }

    // ──────────────────────────────────────────
    // Duplicate check (used by AccountDetailsPage)
    // ──────────────────────────────────────────

    public bool AccountExists(string summonerName, string riotTag, Account? excludeAccount = null)
    {
        var normalizedTag = (riotTag ?? "").Trim().TrimStart('#');
        var normalizedName = (summonerName ?? "").Trim();

        return AllAccounts.Any(a =>
            a != excludeAccount &&
            string.Equals((a.SummonerName ?? "").Trim(), normalizedName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals((a.RiotTag ?? "").Trim().TrimStart('#'), normalizedTag, StringComparison.OrdinalIgnoreCase));
    }

    // ──────────────────────────────────────────
    // Called by AccountDetailsPage
    // ──────────────────────────────────────────

    public void AddNewAccount(Account account)
    {
        AllAccounts.Add(account);
        BuildServers();
        AccountsView.Refresh();
    }

    public async Task OnDetailsPageSavedAsync(string okStatus)
    {
        IsDirty = false;
        await AutosaveAsync(okStatus);
        AccountsView.Refresh();
    }

    public void RebuildServersPublic() => BuildServers();

    /// <summary>Exposed for AccountDetailsPage to validate accounts via Riot API.</summary>
    public IRiotApiService? RiotApi => _module.RiotApi;

    /// <summary>Check if another account on the same server already has Xbox linked.</summary>
    public bool IsXboxLinkedOnServer(string server, Account? excludeAccount = null)
    {
        return AllAccounts.Any(a =>
            a != excludeAccount &&
            a.IsXboxLinked &&
            string.Equals((a.Server ?? "").Trim(), (server ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
    }

    // ──────────────────────────────────────────
    // Save / Upload
    // ──────────────────────────────────────────

    private async Task AutosaveAsync(string okStatus)
    {
        IsDirty = false;
        await UploadAsync(okStatus);
    }

    private async Task UploadAsync(string okStatus)
    {
        if (_saving) return;

        try
        {
            _saving = true;
            OnPropertyChanged(nameof(CanManualSave));
            SaveStatusText = "Saving\u2026";

            var json = SerializeToJson();

            if (_module.Store is null)
            {
                SaveStatusText = "Store not initialized.";
                return;
            }

            await _module.Store.UploadAccountsJsonAsync(_session, json);
            SaveStatusText = okStatus;
        }
        catch
        {
            SaveStatusText = "Save failed. Try again.";
        }
        finally
        {
            _saving = false;
            OnPropertyChanged(nameof(CanManualSave));
        }
    }

    private void UpdateSaveStatus()
    {
        if (_saving) return;
        if (IsDirty) SaveStatusText = "Unsaved changes";
        else if (string.IsNullOrWhiteSpace(SaveStatusText)) SaveStatusText = "";
    }

    // ──────────────────────────────────────────
    // Sign Out
    // ──────────────────────────────────────────

    private async void SignOut_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Save any pending changes before signing out
            if (IsDirty) await UploadAsync("Saved.");

            // Set presence to offline
            if (_module.Presence is not null && _module.ActiveSession is not null)
                await _module.Presence.SetOfflineAsync(_module.ActiveSession);
        }
        catch { }

        // Clear saved session so Remember Me won't auto-login
        _module.AuthConcrete?.ClearSession();
        _module.ActiveSession = null;

        // Navigate back to login
        NavigateToPage(new LoginPage(_module));
    }

    // ──────────────────────────────────────────
    // Navigation helper
    // ──────────────────────────────────────────

    private void NavigateToPage(UserControl page)
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

    // ──────────────────────────────────────────
    // INotifyPropertyChanged
    // ──────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        if (name == nameof(SelectedAccount))
        {
            OnPropertyChanged(nameof(HasSelection));
        }
        return true;
    }
}
