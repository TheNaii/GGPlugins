using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CD.Core.Models;

namespace CD.Module;

public partial class LobbyPage : UserControl
{
    private readonly ModuleEntry _module;
    private readonly string _lobbyId;
    private Lobby? _currentLobby;
    private bool _suppressEvents;
    private string? _selectedRole; // role slot to assign a champion to
    private string _searchText = "";
    private string _roleFilter = "all";
    private System.Timers.Timer? _notesDebounce;
    private bool _notesUserEditing; // true while user is typing in notes
    private DateTime _notesLastKeystroke;

    private static readonly string[] Roles = { "top", "jungle", "mid", "adc", "support" };
    private static readonly Dictionary<string, string> RoleEmoji = new()
    {
        ["top"] = "⚔", ["jungle"] = "🌲", ["mid"] = "🔮", ["adc"] = "🏹", ["support"] = "🛡", ["fill"] = "🔄"
    };
    private static readonly Dictionary<string, string> RoleLabel = new()
    {
        ["top"] = "TOP", ["jungle"] = "JNG", ["mid"] = "MID", ["adc"] = "ADC", ["support"] = "SUP", ["fill"] = "FILL"
    };

    private readonly Dictionary<string, ToggleButton> _filterButtons;

    public LobbyPage(ModuleEntry module, string lobbyId)
    {
        InitializeComponent();
        _module = module;
        _lobbyId = lobbyId;

        _filterButtons = new()
        {
            ["all"] = FilterAll,
            ["top"] = FilterTop,
            ["jungle"] = FilterJungle,
            ["mid"] = FilterMid,
            ["adc"] = FilterAdc,
            ["support"] = FilterSupport
        };

        // Auto-select the user's own role so they can pick immediately
        var userRole = module.Session?.Role ?? "top";
        _selectedRole = Roles.Contains(userRole) ? userRole : "top";

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_module.Poller is null) return;

        _module.Poller.LobbyUpdated += OnLobbyUpdated;
        _module.Poller.LobbyClosed += OnLobbyClosed;
        _module.Poller.Start(_lobbyId, _module.Session!);

        BuildChampionGrid();
        _ = InitialLoadAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_module.Poller is null) return;
        _module.Poller.LobbyUpdated -= OnLobbyUpdated;
        _module.Poller.LobbyClosed -= OnLobbyClosed;
        _module.Poller.Stop();
    }

    private async Task InitialLoadAsync()
    {
        if (_module.Session is null) return;

        try
        {
            var lobby = await _module.Firestore.GetLobbyAsync(_lobbyId, _module.Session.IdToken);
            if (lobby is not null)
                Dispatcher.Invoke(() => ApplyLobbyState(lobby));
        }
        catch { }
    }

    // ── Polling callbacks ────────────────────────────────────────

    private void OnLobbyUpdated(Lobby lobby)
    {
        Dispatcher.Invoke(() => ApplyLobbyState(lobby));
    }

    private void OnLobbyClosed()
    {
        Dispatcher.Invoke(() =>
        {
            _module.CurrentLobbyId = null;
            NavigateTo(new LobbyBrowserPage(_module));
        });
    }

    // ── Render lobby state ──────────────────────────────────────

    private void ApplyLobbyState(Lobby lobby)
    {
        _suppressEvents = true;
        _currentLobby = lobby;

        LobbyTitle.Text = lobby.Name;

        // Bans combo
        foreach (ComboBoxItem item in BansCombo.Items)
        {
            if (item.Tag?.ToString() == lobby.BansPerSide.ToString())
            { BansCombo.SelectedItem = item; break; }
        }

        // Notes: skip remote updates while user is actively typing (within last 2s)
        if (!_notesUserEditing || (DateTime.UtcNow - _notesLastKeystroke).TotalSeconds > 2)
        {
            if (NotesBox.Text != lobby.Notes)
                NotesBox.Text = lobby.Notes;
            _notesUserEditing = false;
        }

        ModeHint.Text = "Left-click a champion to assign to selected role. Right-click to ban.";

        RenderTeamPanel(lobby);
        RenderBans(lobby);
        HighlightPickedChampions(lobby);

        _suppressEvents = false;
    }

    private void RenderTeamPanel(Lobby lobby)
    {
        TeamPanel.Children.Clear();

        TeamPanel.Children.Add(new TextBlock
        {
            Text = "TEAM",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Opacity = 0.5,
            Margin = new Thickness(0, 0, 0, 8)
        });

        foreach (var role in Roles)
        {
            var pick = lobby.Picks.TryGetValue(role, out var p) ? p : null;
            var player = lobby.Players.Values.FirstOrDefault(pl =>
                string.Equals(pl.Role, role, StringComparison.OrdinalIgnoreCase));

            var isSelected = _selectedRole == role;

            var slot = new Border
            {
                Background = new SolidColorBrush(CC(isSelected ? "#2D2059" : "#1E293B")),
                BorderBrush = new SolidColorBrush(CC(isSelected ? "#7C3AED" : "#334155")),
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand
            };

            var roleName = role;
            slot.MouseLeftButtonUp += (_, _) =>
            {
                _selectedRole = _selectedRole == roleName ? null : roleName;
                if (_currentLobby is not null)
                    RenderTeamPanel(_currentLobby);
            };

            // Right-click to clear the pick for this role (own role or host only)
            slot.MouseRightButtonUp += (_, _) =>
            {
                if (_currentLobby is null || _module.Session is null) return;
                var isHost = _currentLobby.HostId == _module.Session.UserId;
                var myRole = _module.Session.Role;
                if (!isHost && !string.Equals(roleName, myRole, StringComparison.OrdinalIgnoreCase)) return;

                if (_currentLobby.Picks.ContainsKey(roleName))
                {
                    _currentLobby.Picks.Remove(roleName);
                    RenderTeamPanel(_currentLobby);
                    HighlightPickedChampions(_currentLobby);
                    _ = _module.Firestore.SetPicksDirectAsync(_lobbyId, _currentLobby.Picks, _module.Session.IdToken);
                }
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Champion icon (if picked)
            if (pick is not null)
            {
                var champ = _module.Champions?.Champions.FirstOrDefault(c => c.Id == pick.ChampionId);
                if (champ?.LocalIconPath is not null && File.Exists(champ.LocalIconPath))
                {
                    var img = new Image
                    {
                        Width = 32,
                        Height = 32,
                        Margin = new Thickness(0, 0, 8, 0),
                        Source = LoadImage(champ.LocalIconPath)
                    };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    Grid.SetColumn(img, 0);
                    grid.Children.Add(img);
                }
            }

            // Role info + player name
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new TextBlock
            {
                Text = $"{RoleEmoji.GetValueOrDefault(role, "?")} {RoleLabel.GetValueOrDefault(role, role.ToUpper())}",
                FontWeight = FontWeights.SemiBold,
                FontSize = 12
            });

            if (player is not null)
            {
                header.Children.Add(new TextBlock
                {
                    Text = $" — {player.Username}",
                    Opacity = 0.6,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            info.Children.Add(header);

            if (pick is not null)
            {
                info.Children.Add(new TextBlock
                {
                    Text = pick.ChampionId,
                    FontSize = 10,
                    Opacity = 0.7,
                    Foreground = new SolidColorBrush(CC("#A78BFA")),
                    Margin = new Thickness(0, 1, 0, 0)
                });
            }

            Grid.SetColumn(info, 1);
            grid.Children.Add(info);

            slot.Child = grid;
            TeamPanel.Children.Add(slot);
        }

        // Unassigned (fill) players
        var unassigned = lobby.Players.Values
            .Where(p => !Roles.Contains(p.Role, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (unassigned.Count > 0)
        {
            TeamPanel.Children.Add(new TextBlock
            {
                Text = "FILL",
                FontSize = 10,
                Opacity = 0.4,
                Margin = new Thickness(0, 8, 0, 4)
            });

            foreach (var p in unassigned)
            {
                TeamPanel.Children.Add(new TextBlock
                {
                    Text = $"🔄 {p.Username}",
                    FontSize = 11,
                    Opacity = 0.6,
                    Margin = new Thickness(4, 1, 0, 1)
                });
            }
        }
    }

    private void RenderBans(Lobby lobby)
    {
        BansPanel.Children.Clear();

        if (lobby.Bans.Count == 0)
        {
            BansPanel.Children.Add(new TextBlock
            {
                Text = "No bans yet. Right-click a champion to ban.",
                FontSize = 10,
                Opacity = 0.35
            });
            return;
        }

        foreach (var ban in lobby.Bans)
        {
            var champ = _module.Champions?.Champions.FirstOrDefault(c => c.Id == ban.ChampionId);

            var badge = new Border
            {
                Background = new SolidColorBrush(CC("#7F1D1D")),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 3, 6, 3),
                Margin = new Thickness(0, 0, 4, 4),
                Cursor = Cursors.Hand,
                ToolTip = "Right-click to remove ban"
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal };

            // Mini icon
            if (champ?.LocalIconPath is not null && File.Exists(champ.LocalIconPath))
            {
                var img = new Image { Width = 18, Height = 18, Margin = new Thickness(0, 0, 4, 0) };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                img.Source = LoadImage(champ.LocalIconPath);
                stack.Children.Add(img);
            }

            stack.Children.Add(new TextBlock
            {
                Text = ban.ChampionId,
                FontSize = 11,
                Foreground = new SolidColorBrush(CC("#FCA5A5")),
                VerticalAlignment = VerticalAlignment.Center
            });

            badge.Child = stack;

            // Right-click to remove ban
            var champId = ban.ChampionId;
            badge.MouseRightButtonUp += async (_, _) =>
            {
                if (_currentLobby is null || _module.Session is null) return;
                _currentLobby.Bans.RemoveAll(b => b.ChampionId == champId);
                RenderBans(_currentLobby);
                HighlightPickedChampions(_currentLobby);
                _ = _module.Firestore.SetBansDirectAsync(_lobbyId, _currentLobby.Bans, _module.Session.IdToken);
            };

            BansPanel.Children.Add(badge);
        }
    }

    // ── Champion grid ────────────────────────────────────────────

    private void BuildChampionGrid()
    {
        ChampionGrid.Children.Clear();

        if (_module.Champions is null) return;

        foreach (var champ in _module.Champions.Champions)
        {
            if (!MatchesFilter(champ)) continue;
            ChampionGrid.Children.Add(CreateChampionTile(champ));
        }
    }

    private Border CreateChampionTile(ChampionInfo champ)
    {
        var tile = new Border
        {
            Width = 56,
            Height = 70,
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(CC("#1E293B")),
            Cursor = Cursors.Hand,
            ToolTip = $"{champ.Name} — {champ.Title}\nLeft-click: assign to role\nRight-click: ban",
            Tag = champ.Id
        };

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        var img = new Image
        {
            Width = 40,
            Height = 40,
            Margin = new Thickness(0, 4, 0, 2)
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

        if (champ.LocalIconPath is not null && File.Exists(champ.LocalIconPath))
            img.Source = LoadImage(champ.LocalIconPath);
        else
            _ = LoadIconAsync(champ, img);

        stack.Children.Add(img);

        stack.Children.Add(new TextBlock
        {
            Text = champ.Name.Length > 7 ? champ.Name[..6] + "…" : champ.Name,
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.8
        });

        tile.Child = stack;

        // Left-click = assign to selected role
        tile.MouseLeftButtonUp += (_, _) => OnChampionLeftClick(champ);

        // Right-click = ban
        tile.MouseRightButtonUp += (_, _) => OnChampionRightClick(champ);

        return tile;
    }

    private void OnChampionLeftClick(ChampionInfo champ)
    {
        if (_currentLobby is null || _module.Session is null) return;

        if (_selectedRole is null)
        {
            ModeHint.Text = "Select a role slot on the left first, then click a champion.";
            return;
        }

        // Only allow picking for your own role, unless you're the host
        var isHost = _currentLobby.HostId == _module.Session.UserId;
        var myRole = _module.Session.Role;
        if (!isHost && !string.Equals(_selectedRole, myRole, StringComparison.OrdinalIgnoreCase))
        {
            ModeHint.Text = $"You can only pick for your role ({RoleLabel.GetValueOrDefault(myRole, myRole.ToUpper())}). The host can pick for anyone.";
            return;
        }

        // Optimistic: update local state immediately
        _currentLobby.Picks[_selectedRole] = new DraftPick
        {
            ChampionId = champ.Id,
            AssignedBy = _module.Session.Username
        };

        // Re-render instantly
        RenderTeamPanel(_currentLobby);
        HighlightPickedChampions(_currentLobby);

        // Fire-and-forget the write
        _ = _module.Firestore.SetPicksDirectAsync(_lobbyId, _currentLobby.Picks, _module.Session.IdToken);
    }

    private void OnChampionRightClick(ChampionInfo champ)
    {
        if (_currentLobby is null || _module.Session is null) return;

        // Check if already banned
        if (_currentLobby.Bans.Any(b => b.ChampionId == champ.Id))
        {
            // Remove the ban
            _currentLobby.Bans.RemoveAll(b => b.ChampionId == champ.Id);
        }
        else
        {
            // Add ban (respect bans-per-side limit)
            var maxBans = _currentLobby.BansPerSide * 2;
            if (_currentLobby.Bans.Count >= maxBans)
            {
                ModeHint.Text = $"Max {maxBans} bans reached ({_currentLobby.BansPerSide} per side).";
                return;
            }

            _currentLobby.Bans.Add(new Ban
            {
                ChampionId = champ.Id,
                Side = _currentLobby.Bans.Count < _currentLobby.BansPerSide ? "blue" : "red",
                Index = _currentLobby.Bans.Count
            });
        }

        // Optimistic render
        RenderBans(_currentLobby);
        HighlightPickedChampions(_currentLobby);

        // Fire-and-forget
        _ = _module.Firestore.SetBansDirectAsync(_lobbyId, _currentLobby.Bans, _module.Session.IdToken);
    }

    private async Task LoadIconAsync(ChampionInfo champ, Image img)
    {
        if (_module.Champions is null) return;
        var path = await _module.Champions.GetIconPathAsync(champ);
        if (path is not null)
            Dispatcher.Invoke(() => img.Source = LoadImage(path));
    }

    private static BitmapImage? LoadImage(string path)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = new MemoryStream(File.ReadAllBytes(path));
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    private bool MatchesFilter(ChampionInfo champ)
    {
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            if (!champ.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (_roleFilter != "all")
        {
            var tags = _roleFilter switch
            {
                "top" => new[] { "Fighter", "Tank" },
                "jungle" => new[] { "Tank", "Fighter", "Assassin" },
                "mid" => new[] { "Mage", "Assassin" },
                "adc" => new[] { "Marksman" },
                "support" => new[] { "Support" },
                _ => Array.Empty<string>()
            };

            if (tags.Length > 0 && !champ.Tags.Any(t => tags.Contains(t)))
                return false;
        }

        return true;
    }

    private void HighlightPickedChampions(Lobby lobby)
    {
        var pickedIds = lobby.Picks.Values.Select(p => p.ChampionId).ToHashSet();
        var bannedIds = lobby.Bans.Select(b => b.ChampionId).ToHashSet();

        foreach (var child in ChampionGrid.Children)
        {
            if (child is Border tile && tile.Tag is string champId)
            {
                if (bannedIds.Contains(champId))
                {
                    tile.Opacity = 0.25;
                    tile.Background = new SolidColorBrush(CC("#7F1D1D"));
                }
                else if (pickedIds.Contains(champId))
                {
                    tile.Opacity = 0.5;
                    tile.Background = new SolidColorBrush(CC("#2D2059"));
                }
                else
                {
                    tile.Opacity = 1.0;
                    tile.Background = new SolidColorBrush(CC("#1E293B"));
                }
            }
        }
    }

    // ── Search and filter ────────────────────────────────────────

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        _searchText = SearchBox.Text.Trim();
        BuildChampionGrid();
        if (_currentLobby is not null)
            HighlightPickedChampions(_currentLobby);
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked) return;

        foreach (var (role, btn) in _filterButtons)
        {
            if (btn == clicked)
            {
                _roleFilter = role;
                btn.IsChecked = true;
            }
            else
            {
                btn.IsChecked = false;
            }
        }

        BuildChampionGrid();
        if (_currentLobby is not null)
            HighlightPickedChampions(_currentLobby);
    }

    // ── Top bar controls ────────────────────────────────────────

    private async void Bans_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || _module.Session is null) return;
        var countStr = (BansCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "5";
        if (int.TryParse(countStr, out var count))
            await _module.Firestore.UpdateBansPerSideAsync(_lobbyId, count, _module.Session);
    }

    // ── Notes ────────────────────────────────────────────────────

    private void Notes_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents) return;

        _notesUserEditing = true;
        _notesLastKeystroke = DateTime.UtcNow;

        _notesDebounce?.Stop();
        _notesDebounce?.Dispose();
        _notesDebounce = new System.Timers.Timer(800) { AutoReset = false };
        _notesDebounce.Elapsed += async (_, _) =>
        {
            var text = "";
            Dispatcher.Invoke(() => text = NotesBox.Text);
            if (_module.Session is not null)
            {
                try { await _module.Firestore.UpdateNotesAsync(_lobbyId, text, _module.Session); }
                catch { }
            }
        };
        _notesDebounce.Start();
    }

    // ── Leave ────────────────────────────────────────────────────

    private void Leave_Click(object sender, RoutedEventArgs e)
    {
        _module.Poller?.Stop();

        // Fire-and-forget the leave so UI never blocks
        if (_module.Session is not null)
        {
            var session = _module.Session;
            var lobbyId = _lobbyId;
            var isHostAlone = _currentLobby is not null &&
                _currentLobby.HostId == session.UserId &&
                _currentLobby.Players.Count <= 1;

            _ = Task.Run(async () =>
            {
                try
                {
                    if (isHostAlone)
                        await _module.Firestore.CloseLobbyAsync(lobbyId, session.IdToken);
                    else
                        await _module.Firestore.LeaveLobbyAsync(lobbyId, session);
                }
                catch { }
            });
        }

        _module.CurrentLobbyId = null;
        NavigateTo(new LobbyBrowserPage(_module));
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static Color CC(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private void NavigateTo(UserControl page)
    {
        DependencyObject current = this;
        while (true)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current is null) return;
            if (current is ContentControl cc)
            {
                cc.Content = page;
                return;
            }
        }
    }
}
