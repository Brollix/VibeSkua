using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Shared.Avalonia.Services;

namespace ButlerV5;

public partial class SettingsWindow : Window
{
    private readonly ButlerV5Plugin _plugin;
    private readonly ObservableCollection<LogLine> _logEntries = new();
    private readonly DispatcherTimer _logTimer;
    private Border? _selectedRow;

    public SettingsWindow() : this(null!)
    {
    }

    public SettingsWindow(ButlerV5Plugin plugin)
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

        var asmVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        string verText = asmVer != null ? $"{asmVer.Major}.{asmVer.Minor}.{asmVer.Build}" : "?";
        MetaText.Text = $"Author: l0newolf12\nVersion: {verText}";

        LogList.ItemsSource = _logEntries;

        if (_plugin != null)
        {
            LogTitle.Text = $"Logs - {_plugin.OwnUsername ?? "this account"}";

            bool firstRow = true;
            foreach (IOption option in _plugin.OptionContainer?.Options ?? new List<IOption>())
            {
                if (!firstRow)
                    SettingsStack.Children.Add(MakeSeparator());
                firstRow = false;
                SettingsStack.Children.Add(BuildSettingRow(option));
            }

            RefreshLog();
            _logTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _logTimer.Tick += (_, _) => RefreshLog();
            _logTimer.Start();
        }
        else
        {
            _logTimer = new DispatcherTimer();
        }

        Closed += (_, _) => _logTimer?.Stop();
    }

    private Border MakeSeparator()
    {
        return new Border
        {
            Height = 1,
            Margin = new Thickness(4, 1, 4, 1),
            Background = Application.Current?.FindResource("SkuaBorderBrush") as IBrush ?? Brush.Parse("#282828")
        };
    }

    private Border BuildSettingRow(IOption option)
    {
        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        TextBlock label = new()
        {
            Text = option.DisplayName,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 10, 0),
            FontSize = 13
        };
        grid.Children.Add(label);

        Control control = BuildControl(option);
        control.VerticalAlignment = VerticalAlignment.Center;
        control.GotFocus += (_, _) => SelectOption(option);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);

        Border row = new()
        {
            Child = grid,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
        };
        row.PointerPressed += (_, _) => SelectOption(option, row);
        return row;
    }

    private Control BuildControl(IOption option)
    {
        IOptionContainer? oc = _plugin.OptionContainer;
        string raw = oc != null ? SafeGetDirect(oc, option) : (option.DefaultValue?.ToString() ?? "");

        if (option.Type == typeof(bool))
        {
            bool value = raw.Equals("True", StringComparison.OrdinalIgnoreCase) || raw == "1";
            CheckBox cb = new() { IsChecked = value };
            bool isReverting = false;

            cb.IsCheckedChanged += (s, _) =>
            {
                if (isReverting)
                    return;

                bool isChecked = cb.IsChecked == true;
                if (!isChecked && option.Name == "enabled")
                {
                    // Master switch: confirm before disabling the whole plugin; revert if declined.
                    var dialogService = Ioc.Default.GetService<IDialogService>();
                    bool? result = dialogService?.ShowMessageBox(
                        "Turning this off stops ButlerV5 from working on ALL accounts - no following, " +
                        "summoning, or broadcasting until you turn it back on.\n\nDisable ButlerV5?",
                        "ButlerV5", true);

                    if (result == true)
                    {
                        _plugin.SaveSetting(option.Name, false);
                    }
                    else
                    {
                        isReverting = true;
                        cb.IsChecked = true;
                        isReverting = false;
                    }
                }
                else
                {
                    _plugin.SaveSetting(option.Name, isChecked);
                }
            };

            return cb;
        }

        if (option.Type.IsEnum)
        {
            ComboBox combo = new() { MinWidth = 120 };
            foreach (object v in Enum.GetValues(option.Type))
                combo.Items.Add(v);

            object? current = TryParseEnum(option.Type, raw) ?? option.DefaultValue;
            combo.SelectedItem = combo.Items.Cast<object>().FirstOrDefault(i => i.Equals(current)) ?? (combo.Items.Count > 0 ? combo.Items[0] : null);
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem != null)
                    _plugin.SaveSetting(option.Name, combo.SelectedItem);
            };
            return combo;
        }

        // int and string both use a text box; int validates on commit.
        bool isInt = option.Type == typeof(int);
        TextBox tb = new() { Text = raw, MinWidth = isInt ? 80 : 150, MaxWidth = 220 };
        tb.LostFocus += (_, _) =>
        {
            if (isInt)
            {
                if (int.TryParse(tb.Text?.Trim(), out int n))
                {
                    _plugin.SaveSetting(option.Name, n);
                }
                else
                {
                    tb.Text = oc != null ? SafeGetDirect(oc, option) : "";
                }
            }
            else
            {
                _plugin.SaveSetting(option.Name, tb.Text ?? "");
            }
        };
        return tb;
    }

    private void SelectOption(IOption option, Border? row = null)
    {
        DescTitle.Text = option.DisplayName;
        DescBody.Text = string.IsNullOrWhiteSpace(option.Description) ? "(no description)" : option.Description;

        if (row != null)
        {
            if (_selectedRow != null)
                _selectedRow.Background = Brushes.Transparent;
            _selectedRow = row;
            _selectedRow.Background = Application.Current?.FindResource("SkuaNavHoverBrush") as IBrush ?? Brush.Parse("#222222");
        }
    }

    private void RefreshLog()
    {
        try
        {
            List<LogLine> snap = DebugLog.Snapshot();

            // Fast path: nothing changed.
            if (snap.Count == _logEntries.Count &&
                (snap.Count == 0 || (snap[^1].Seq == _logEntries[^1].Seq && snap[^1].Text == _logEntries[^1].Text)))
                return;

            int i = 0;
            foreach (LogLine s in snap)
            {
                while (i < _logEntries.Count && _logEntries[i].Seq < s.Seq)
                    _logEntries.RemoveAt(i);

                if (i < _logEntries.Count && _logEntries[i].Seq == s.Seq)
                {
                    if (_logEntries[i].Text != s.Text)
                        _logEntries[i].Text = s.Text;
                    i++;
                }
                else
                {
                    _logEntries.Insert(i, s.Copy());
                    i++;
                }
            }
            while (_logEntries.Count > i)
                _logEntries.RemoveAt(_logEntries.Count - 1);

            if (FollowTailCheck.IsChecked == true && _logEntries.Count > 0)
                LogList.ScrollIntoView(_logEntries[^1]);
        }
        catch
        {
        }
    }

    private void CopyButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            string text = string.Join(Environment.NewLine, _logEntries.Select(en => en.Text));
            if (text.Length > 0)
            {
                var clip = Ioc.Default.GetService<IClipboardService>();
                if (clip != null)
                    clip.SetText(text);
                else
                    _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text);

                DebugLog.Log("UI", $"copied {_logEntries.Count} log lines to clipboard", LogLevel.Success);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Log("UI", $"copy failed: {ex.Message}", LogLevel.Error);
        }
    }

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        string? path = DebugLog.SaveExport(_plugin.OwnUsername);
        if (path != null)
            DebugLog.Log("UI", $"saved log to {Path.GetFileName(path)}", LogLevel.Success);
        else
            DebugLog.Log("UI", "save log failed", LogLevel.Error);
    }

    private void ClearButton_Click(object? sender, RoutedEventArgs e)
    {
        DebugLog.ClearMemory();
        _logEntries.Clear();
    }

    private void OpenFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(DebugLog.LogDir);
            Process.Start(new ProcessStartInfo { FileName = DebugLog.LogDir, UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            DebugLog.Log("UI", $"open folder failed: {ex.Message}", LogLevel.Error);
        }
    }

    private static string SafeGetDirect(IOptionContainer oc, IOption option)
    {
        try { return oc.GetDirect(option) ?? option.DefaultValue?.ToString() ?? ""; }
        catch { return option.DefaultValue?.ToString() ?? ""; }
    }

    private static object? TryParseEnum(Type type, string raw)
    {
        try { return Enum.Parse(type, raw, ignoreCase: true); }
        catch { return null; }
    }
}
