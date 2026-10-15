using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Shared.Avalonia.Services;

namespace ButlerV5;

public partial class RosterWindow : Window
{
    private static readonly IBrush GreenBrush = Brush.Parse("#3FBF6F");
    private static readonly IBrush OrangeBrush = Brush.Parse("#FFA23E");
    private static readonly IBrush OrangeButtonBrush = Brush.Parse("#E8862D");
    private static readonly IBrush RedBrush = Brush.Parse("#E05252");

    private readonly ButlerV5Plugin _plugin;
    private readonly DispatcherTimer _timer;
    private string _lastSnapshot = "";

    public RosterWindow() : this(null!)
    {
    }

    public RosterWindow(ButlerV5Plugin plugin)
    {
        InitializeComponent();
        _plugin = plugin;
        Skua.Shared.Avalonia.Services.WindowPlacementHelper.CenterOnActiveScreen(this);

        Loaded += (_, _) =>
        {
            try
            {
                var themeService = Ioc.Default.GetService<IThemeService>();
                bool isDark = themeService?.IsDarkTheme ?? true;
                ThemeResourceApplicator.ApplyTitleBarTheme(this, isDark);
            }
            catch { }
        };

        if (_plugin != null)
        {
            _plugin.SyncFilesChanged += OnSyncFilesChanged;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += (_, _) => Refresh();
            _timer.Start();
            Refresh(force: true);
        }
        else
        {
            _timer = new DispatcherTimer();
        }

        Closed += (_, _) =>
        {
            _timer?.Stop();
            if (_plugin != null)
                _plugin.SyncFilesChanged -= OnSyncFilesChanged;
        };
    }

    private void OnSyncFilesChanged()
    {
        try { Dispatcher.UIThread.Post(() => Refresh()); }
        catch { }
    }

    public void RefreshRoster() => Refresh(force: true);

    private void Refresh(bool force = false)
    {
        if (_plugin == null)
            return;

        List<SyncData> online = Roster.GetOnline(_plugin.OwnUsername);
        HashSet<string> myFollowers = new(_plugin.CurrentFollowers, StringComparer.OrdinalIgnoreCase);
        string? followingWho = _plugin.FollowingWho;

        MeText.Text = _plugin.OwnUsername ?? "(not logged in)";
        StateText.Text = followingWho != null
            ? $"Following {followingWho} ({(_plugin.FollowingHow == FollowSource.Ordered ? "summoned" : "manual")})"
            : "Not following anyone";
        if (followingWho != null)
            StateText.Foreground = GreenBrush;
        else
            StateText.Foreground = Application.Current?.FindResource("SkuaMutedTextBrush") as IBrush ?? Brush.Parse("#9A9AA5");

        bool pluginOff = !_plugin.PluginEnabled;

        StopButton.IsVisible = followingWho != null;
        ReleaseAllButton.IsVisible = myFollowers.Count > 0;
        ReleaseAllButton.IsEnabled = !pluginOff;

        SummonAllButton.IsEnabled = !pluginOff && followingWho == null;
        ToolTip.SetTip(SummonAllButton, pluginOff
            ? "ButlerV5 is disabled - turn on 'Enable ButlerV5' in Settings first."
            : followingWho == null
                ? "Order every online ButlerV5 account to follow this one."
                : $"You're following {followingWho} - stop that before summoning anyone.");

        TimeSpan interval = TimeSpan.FromSeconds(Math.Clamp(_plugin.RosterRefreshSeconds, 1, 30));
        if (_timer.Interval != interval)
            _timer.Interval = interval;

        FooterText.Text = pluginOff
            ? "ButlerV5 is DISABLED - turn on 'Enable ButlerV5' in Settings"
            : online.Count == 1
                ? "1 plugin account online - refreshes every 2s"
                : $"{online.Count} plugin accounts online - refreshes every 2s";

        string myServer = _plugin.MyServerName;
        string snapshot = string.Join(";", online.Select(d =>
            $"{d.Username}|{d.MapWithRoom}|{(d.Attacking ? 1 : 0)}|{(myFollowers.Contains(d.Username) ? 1 : 0)}" +
            $"|{d.Following}|{(d.ScriptOn ? 1 : 0)}|{d.ScriptName}|{(d.Parking ? 1 : 0)}|{d.Followers.Count}|{d.Server}")) +
            $"@{followingWho}@{myServer}";
        if (!force && snapshot == _lastSnapshot)
            return;
        _lastSnapshot = snapshot;

        RowsPanel.Children.Clear();

        if (online.Count == 0)
        {
            TextBlock empty = new()
            {
                Text = "No other ButlerV5 accounts online.\nAccounts appear here once they log in with the plugin installed.",
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 40, 0, 0),
                Foreground = Application.Current?.FindResource("SkuaMutedTextBrush") as IBrush ?? Brush.Parse("#9A9AA5")
            };
            RowsPanel.Children.Add(empty);
            return;
        }

        bool firstRow = true;
        foreach (SyncData d in online)
        {
            if (!firstRow)
                RowsPanel.Children.Add(MakeSeparator());
            firstRow = false;
            RowsPanel.Children.Add(BuildRow(d, myFollowers.Contains(d.Username), d.Username.Equals(followingWho, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private static Border MakeSeparator()
    {
        return new Border
        {
            Height = 1,
            Margin = new Thickness(4, 0, 4, 0),
            Background = Application.Current?.FindResource("SkuaBorderBrush") as IBrush ?? Brush.Parse("#3A3A44")
        };
    }

    private Border BuildRow(SyncData d, bool followsMe, bool amFollowing)
    {
        bool iAmButler = _plugin.FollowingWho != null;

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        StackPanel info = new() { VerticalAlignment = VerticalAlignment.Center };

        WrapPanel nameLine = new() { Orientation = Orientation.Horizontal };
        TextBlock name = new()
        {
            Text = d.Username,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = Application.Current?.FindResource("SkuaTextBrush") as IBrush ?? Brush.Parse("#F0F0F3"),
            VerticalAlignment = VerticalAlignment.Center
        };
        nameLine.Children.Add(name);

        if (d.Attacking)
        {
            nameLine.Children.Add(new TextBlock
            {
                Text = "  ⚔ fighting",
                FontSize = 11,
                Foreground = OrangeBrush,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        bool followingMe = d.Following.Equals(_plugin.OwnUsername ?? "", StringComparison.OrdinalIgnoreCase) &&
                           !string.IsNullOrEmpty(d.Following);
        bool followingOther = !string.IsNullOrEmpty(d.Following) && !followingMe;

        var accentBrush = Application.Current?.FindResource("SkuaAccentBrush") as IBrush ?? Brush.Parse("#4F8CFF");
        var mutedBrush = Application.Current?.FindResource("SkuaMutedTextBrush") as IBrush ?? Brush.Parse("#9A9AA5");

        if (followingMe)
            nameLine.Children.Add(MakeChip("following me", GreenBrush));
        else if (followingOther)
            nameLine.Children.Add(MakeChip($"following {d.Following}", accentBrush));
        else if (followsMe)
            nameLine.Children.Add(MakeChip("summoned", mutedBrush));

        if (d.ScriptOn)
            nameLine.Children.Add(MakeChip($"busy: {(string.IsNullOrEmpty(d.ScriptName) ? "script" : d.ScriptName)}", OrangeBrush));

        if (d.Parking)
            nameLine.Children.Add(MakeChip("parking", mutedBrush));

        string myServer = _plugin.MyServerName;
        bool differentServer = !string.IsNullOrEmpty(d.Server) && !string.IsNullOrEmpty(myServer) &&
                               !d.Server.Equals(myServer, StringComparison.OrdinalIgnoreCase);
        if (differentServer)
            nameLine.Children.Add(MakeChip("on a different server", RedBrush));

        info.Children.Add(nameLine);

        TextBlock location = new()
        {
            Text = string.IsNullOrEmpty(d.Server) ? d.MapWithRoom : $"{d.MapWithRoom}   ·   {d.Server}",
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = Application.Current?.FindResource("SkuaMutedTextBrush") as IBrush ?? Brush.Parse("#9A9AA5")
        };
        info.Children.Add(location);
        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        Button followButton = new()
        {
            Content = amFollowing ? "Stop" : "Follow",
            Classes = { "skua-button" },
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            Padding = new Thickness(10, 4)
        };

        if (amFollowing)
        {
            followButton.Background = RedBrush;
            followButton.Foreground = Brushes.White;
            followButton.Click += (_, _) => { _plugin.StopFollow(); Refresh(force: true); };
            ToolTip.SetTip(followButton, "Stop following this account.");
        }
        else
        {
            followButton.Click += (_, _) =>
            {
                if (_plugin.IsScriptRunning)
                {
                    try
                    {
                        var dialogService = Ioc.Default.GetService<IDialogService>();
                        dialogService?.ShowMessageBox($"A script is running on this account ({_plugin.RunningScriptName}).\nStop it before following someone.", "Butler V5");
                    }
                    catch { }
                    return;
                }
                _plugin.StartFollow(d.Username, FollowSource.Manual);
                Refresh(force: true);
            };

            if (followsMe || followingMe || followingOther || iAmButler || differentServer)
            {
                followButton.IsEnabled = false;
                ToolTip.SetTip(followButton,
                    iAmButler ? $"You're following {_plugin.FollowingWho} - stop that first."
                    : followsMe ? $"You summoned {d.Username} - release them before you can follow them."
                    : followingMe ? $"{d.Username} is following you - can't follow them back."
                    : followingOther ? $"{d.Username} is following {d.Following} - can't follow a butler (would make a conga line)."
                    : $"{d.Username} is on a different server ({d.Server}) - can't follow across servers.");
            }
            else
            {
                ToolTip.SetTip(followButton, $"Make this account ({_plugin.OwnUsername ?? "me"}) follow {d.Username}.");
            }
        }

        Grid.SetColumn(followButton, 1);
        grid.Children.Add(followButton);

        Button summonButton = new()
        {
            Content = followsMe ? "Release" : "Summon",
            Classes = { "skua-button" },
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            Padding = new Thickness(10, 4)
        };

        if (followsMe)
        {
            summonButton.Background = OrangeButtonBrush;
            summonButton.Foreground = Brushes.White;
            summonButton.Click += (_, _) => { _plugin.ToggleFollower(d.Username); Refresh(force: true); };
            ToolTip.SetTip(summonButton, $"Stop summoning {d.Username}; they park in their house.");
        }
        else
        {
            summonButton.Classes.Add("skua-primary");
            summonButton.Click += (_, _) => { _plugin.ToggleFollower(d.Username); Refresh(force: true); };

            if (iAmButler)
            {
                summonButton.IsEnabled = false;
                ToolTip.SetTip(summonButton, $"You're following {_plugin.FollowingWho} - stop that before summoning anyone.");
            }
            else if (amFollowing)
            {
                summonButton.IsEnabled = false;
                ToolTip.SetTip(summonButton, $"You're following {d.Username} - stop first before summoning them.");
            }
            else if (followingMe)
            {
                summonButton.IsEnabled = false;
                ToolTip.SetTip(summonButton, $"{d.Username} is already following you manually - can't also summon them.");
            }
            else if (followingOther)
            {
                summonButton.IsEnabled = false;
                ToolTip.SetTip(summonButton, $"{d.Username} is already following {d.Following} - they must be released from that first.");
            }
            else if (differentServer)
            {
                summonButton.IsEnabled = false;
                ToolTip.SetTip(summonButton, $"{d.Username} is on a different server ({d.Server}) - can't summon across servers.");
            }
            else if (d.ScriptOn)
            {
                summonButton.IsEnabled = false;
                ToolTip.SetTip(summonButton, $"{d.Username} is running a script - can't be summoned.");
            }
            else if (d.Followers.Count > 0)
            {
                summonButton.IsEnabled = false;
                ToolTip.SetTip(summonButton, $"{d.Username} has its own summoned butlers - it must release them before it can be summoned.");
            }
            else if (d.Parking)
            {
                summonButton.IsEnabled = false;
                ToolTip.SetTip(summonButton, $"{d.Username} is parking (walking home) - summonable once it settles.");
            }
            else
            {
                ToolTip.SetTip(summonButton, $"Order {d.Username} to follow me (their plugin obeys automatically).");
            }
        }

        Grid.SetColumn(summonButton, 2);
        grid.Children.Add(summonButton);

        return new Border
        {
            Padding = new Thickness(8, 9, 8, 9),
            Child = grid
        };
    }

    private static Border MakeChip(string text, IBrush color)
    {
        return new Border
        {
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(6, 1, 6, 1),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = color,
            Background = new SolidColorBrush(color is ISolidColorBrush scb ? scb.Color : Colors.Gray, 0.15),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = FontWeight.Medium,
                Foreground = color,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        _plugin.StopFollow();
        Refresh(force: true);
    }

    private void ReleaseAllButton_Click(object? sender, RoutedEventArgs e)
    {
        _plugin.ReleaseFollowers();
        Refresh(force: true);
    }

    private void SummonAllButton_Click(object? sender, RoutedEventArgs e)
    {
        _plugin.SummonAll();
        Refresh(force: true);
    }

    private void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        _plugin.OpenSettings();
    }
}
