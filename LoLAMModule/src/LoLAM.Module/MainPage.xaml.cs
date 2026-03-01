using LoLAM.Core.Cloud;
using LoLAM.Core.Models;
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

    // Backing store
    public ObservableCollection<Account> AllAccounts { get; } = new();

    // Filtered view (by server + sort)
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
            {
                OnPropertyChanged(nameof(HasSelection));
                // Keep details open behavior up to you; I don't force it.
            }
        }
    }

    public bool HasSelection => SelectedAccount is not null;

    private bool _detailsOpen = true;
    public Visibility DetailsVisibility => (_detailsOpen && HasSelection) ? Visibility.Visible : Visibility.Collapsed;

    private bool _revealPassword;
    public bool RevealPassword
    {
        get => _revealPassword;
        set
        {
            if (Set(ref _revealPassword, value))
            {
                OnPropertyChanged(nameof(PasswordBoxVisibility));
                OnPropertyChanged(nameof(PasswordTextVisibility));
            }
        }
    }

    public Visibility PasswordBoxVisibility => RevealPassword ? Visibility.Collapsed : Visibility.Visible;
    public Visibility PasswordTextVisibility => RevealPassword ? Visibility.Visible : Visibility.Collapsed;

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

    // JSON shape handling: we support either:
    //  - array: [ {account...}, ... ]
    //  - wrapper: { "accounts": [ ... ] }  (or Accounts)
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

        // No default server selection; list stays empty until a server tab is clicked.
        SelectedServer = "";
        SelectedAccount = null;

        ApplySort();
        UpdateSaveStatus();
        OnPropertyChanged(nameof(DetailsVisibility));
    }

    private bool IsAccountInSelectedServer(Account a)
    {
        if (string.IsNullOrWhiteSpace(SelectedServer))
            return false;

        var s = (a.Server ?? "").Trim();
        return string.Equals(s, SelectedServer, StringComparison.OrdinalIgnoreCase);
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

        // Don't auto-select a server. List remains empty until the user clicks a tab.
        // If SelectedServer no longer exists, clear it.
        if (!string.IsNullOrWhiteSpace(SelectedServer) && !Servers.Contains(SelectedServer))
            SelectedServer = "";
    }

    private void LoadFromJson(string accountsJson)
    {
        AllAccounts.Clear();

        if (string.IsNullOrWhiteSpace(accountsJson))
            return;

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
                // Most likely wrappers
                if (obj.TryGetValue("accounts", StringComparison.OrdinalIgnoreCase, out var accountsToken) && accountsToken is JArray a1)
                {
                    _jsonWrapperRoot = obj;
                    _jsonShape = obj.Property("accounts") is not null ? JsonShape.WrapperAccounts : JsonShape.WrapperAccountsCapitalized;

                    foreach (var a in a1.ToObject<Account[]>() ?? Array.Empty<Account>())
                        AllAccounts.Add(a);

                    return;
                }

                // Unknown object, try best-effort: find first array
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

            // Fallback: nothing loaded
        }
        catch
        {
            // If parsing fails, keep empty; you can add an error UI if you want
        }
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
            // keep original structure, just update accounts key (case-insensitive)
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

            default:
                // no sort
                break;
        }
    }

    private void ToggleDetails_Click(object sender, RoutedEventArgs e)
    {
        _detailsOpen = !_detailsOpen;
        OnPropertyChanged(nameof(DetailsVisibility));
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var a = new Account
        {
            Server = string.IsNullOrWhiteSpace(SelectedServer) ? "" : SelectedServer,
            SummonerName = "New Account",
            RiotTag = "",
            Tier = "Unranked",
            Division = ""
        };

        AllAccounts.Add(a);
        BuildServers();
        SelectedAccount = a;

        AccountsView.Refresh();

        // Autosave on add (as requested)
        await AutosaveAsync("Saved new account.");
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is null) return;

        var toRemove = SelectedAccount;
        AllAccounts.Remove(toRemove);

        SelectedAccount = AllAccounts.FirstOrDefault(IsAccountInSelectedServer) ?? AllAccounts.FirstOrDefault();
        AccountsView.Refresh();

        // I strongly recommend autosave on remove too.
        await AutosaveAsync("Removed account.");
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await SaveManualAsync();
    }

    private async Task AutosaveAsync(string okStatus)
    {
        // autosave should not mark dirty; it’s a committed change
        IsDirty = false;
        await UploadAsync(okStatus);
    }

    private async Task SaveManualAsync()
    {
        if (!IsDirty) return;
        await UploadAsync("Saved changes.");
        IsDirty = false;
    }

    private async Task UploadAsync(string okStatus)
    {
        if (_saving) return;

        try
        {
            _saving = true;
            OnPropertyChanged(nameof(CanManualSave));
            SaveStatusText = "Saving…";

            var json = SerializeToJson();

            if (_module.Store is null)
            {
                SaveStatusText = "Store not initialized.";
                return;
            }

            // IMPORTANT: rename this call if your store method name differs
            await _module.Store.UploadAccountsJsonAsync(_session, json);

            SaveStatusText = okStatus;
        }
        catch
        {
            SaveStatusText = "Save failed. Try again.";
            // keep dirty if it was a manual save attempt
        }
        finally
        {
            _saving = false;
            OnPropertyChanged(nameof(CanManualSave));
        }
    }

    private void DetailsEdited_TextChanged(object sender, RoutedEventArgs e)
    {
        // Edits do NOT autosave; enable manual save
        IsDirty = true;
    }

    private void CopyUsername_Click(object sender, RoutedEventArgs e)
    {
        var text = SelectedAccount?.Username ?? "";
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
    }

    private void CopyPassword_Click(object sender, RoutedEventArgs e)
    {
        var text = SelectedAccount?.Password ?? "";
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
    }

    private void LaunchClient_Click(object sender, RoutedEventArgs e)
    {
        // Placeholder: wire your Riot launch logic here
        // You can read SelectedAccount and call your service.
    }

    private void ServerTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Content is string server)
            SelectedServer = server;
    }

    private void UpdateSaveStatus()
    {
        if (_saving) return;
        if (IsDirty) SaveStatusText = "Unsaved changes";
        else if (string.IsNullOrWhiteSpace(SaveStatusText)) SaveStatusText = "";
    }

    private void OpenDetails_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is null) return;

        var detailsPage = new AccountDetailsPage(this, SelectedAccount);

        // Navigate: swap this page's host ContentControl content
        DependencyObject current = this;
        while (true)
        {
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            if (current is null) return;

            if (current is ContentControl cc)
            {
                cc.Content = detailsPage;
                return;
            }
        }
    }

    // ──────────────────────────────────────────
    // Called by AccountDetailsPage after a save
    // ──────────────────────────────────────────

    public async Task OnDetailsPageSavedAsync(string okStatus)
    {
        // The model was already mutated by AccountDetailsPage; just upload.
        IsDirty = false;
        await AutosaveAsync(okStatus);
        AccountsView.Refresh();
    }

    public void RebuildServersPublic() => BuildServers();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        if (name == nameof(SelectedAccount)) OnPropertyChanged(nameof(DetailsVisibility));
        if (name == nameof(SelectedAccount)) OnPropertyChanged(nameof(HasSelection));
        return true;
    }
}