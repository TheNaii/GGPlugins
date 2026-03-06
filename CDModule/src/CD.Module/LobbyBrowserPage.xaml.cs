using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CD.Core.Models;

namespace CD.Module;

public partial class LobbyBrowserPage : UserControl
{
    private readonly ModuleEntry _module;

    public LobbyBrowserPage(ModuleEntry module)
    {
        InitializeComponent();
        _module = module;

        PlayerInfo.Text = $"{module.Session?.Username} — {FormatRole(module.Session?.Role ?? "fill")}";
        LobbyNameBox.Text = $"{module.Session?.Username}'s Lobby";

        Loaded += async (_, _) => await RefreshLobbiesAsync();
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var name = LobbyNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusText.Text = "Enter a lobby name.";
            return;
        }

        if (_module.Session is null) return;

        StatusText.Text = "Creating lobby...";

        try
        {
            var lobbyId = await _module.Firestore.CreateLobbyAsync(name, _module.Session);
            _module.CurrentLobbyId = lobbyId;
            NavigateTo(new LobbyPage(_module, lobbyId));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed: {ex.Message}";
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshLobbiesAsync();
    }

    private async Task RefreshLobbiesAsync()
    {
        if (_module.Session is null) return;

        StatusText.Text = "Loading lobbies...";
        LobbyList.Children.Clear();

        try
        {
            var lobbies = await _module.Firestore.ListLobbiesAsync(_module.Session.IdToken);

            // Clean up stale lobbies: remove any with 0 players and lastActivity > 2 min ago
            // Also prune players with stale heartbeats from the count
            var staleThreshold = DateTime.UtcNow.AddSeconds(-60);
            var lobbyDeadThreshold = DateTime.UtcNow.AddMinutes(-2);
            var alive = new List<Lobby>();

            foreach (var lobby in lobbies)
            {
                // Remove stale players
                var staleKeys = lobby.Players
                    .Where(p => DateTime.TryParse(p.Value.LastHeartbeat, out var hb) && hb < staleThreshold)
                    .Select(p => p.Key)
                    .ToList();
                foreach (var key in staleKeys)
                    lobby.Players.Remove(key);

                // If empty and old, delete it from Firestore
                if (lobby.Players.Count == 0)
                {
                    var lastAct = DateTime.TryParse(lobby.LastActivity, out var la) ? la : DateTime.MinValue;
                    if (lastAct < lobbyDeadThreshold)
                    {
                        _ = _module.Firestore.CloseLobbyAsync(lobby.Id, _module.Session.IdToken);
                        continue; // don't show it
                    }
                }

                alive.Add(lobby);
            }

            if (alive.Count == 0)
            {
                LobbyList.Children.Add(new TextBlock
                {
                    Text = "No lobbies found. Create one!",
                    Opacity = 0.5,
                    Margin = new Thickness(0, 20, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }
            else
            {
                foreach (var lobby in alive)
                    LobbyList.Children.Add(CreateLobbyCard(lobby));
            }

            StatusText.Text = $"{alive.Count} lobby(s) found";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private Border CreateLobbyCard(Lobby lobby)
    {
        var playerCount = lobby.Players.Count;
        var hostName = lobby.HostUsername;

        var card = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = Cursors.Hand
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel();
        info.Children.Add(new TextBlock
        {
            Text = lobby.Name,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold
        });
        info.Children.Add(new TextBlock
        {
            Text = $"Host: {hostName} · {playerCount}/6 players",
            FontSize = 11,
            Opacity = 0.55,
            Margin = new Thickness(0, 2, 0, 0)
        });

        var joinBtn = new Button
        {
            Content = "Join",
            Width = 70,
            Height = 30,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand
        };
        joinBtn.Click += async (_, _) => await JoinLobbyAsync(lobby.Id);
        Grid.SetColumn(joinBtn, 1);

        grid.Children.Add(info);
        grid.Children.Add(joinBtn);
        card.Child = grid;

        // Also allow clicking the card itself
        card.MouseLeftButtonUp += async (_, _) => await JoinLobbyAsync(lobby.Id);

        return card;
    }

    private async Task JoinLobbyAsync(string lobbyId)
    {
        if (_module.Session is null) return;

        StatusText.Text = "Joining...";

        try
        {
            await _module.Firestore.JoinLobbyAsync(lobbyId, _module.Session);
            _module.CurrentLobbyId = lobbyId;
            NavigateTo(new LobbyPage(_module, lobbyId));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to join: {ex.Message}";
        }
    }

    private static string FormatRole(string role) => role switch
    {
        "top" => "Top",
        "jungle" => "Jungle",
        "mid" => "Mid",
        "adc" => "ADC",
        "support" => "Support",
        _ => "Fill"
    };

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
