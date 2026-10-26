using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Material.Icons;
using Material.Icons.Avalonia;
using System;

namespace Skua.Shared.Avalonia.Controls.Shell;

public partial class WindowTitleBar : UserControl
{
    public static readonly StyledProperty<string> TitleTextProperty =
        AvaloniaProperty.Register<WindowTitleBar, string>(nameof(TitleText), "VibeSkua");

    private static readonly IImage DefaultIconSource = LoadDefaultIconSource();

    public static readonly StyledProperty<IImage?> IconSourceProperty =
        AvaloniaProperty.Register<WindowTitleBar, IImage?>(nameof(IconSource), DefaultIconSource);

    public string TitleText
    {
        get => GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public IImage? IconSource
    {
        get => GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    public WindowTitleBar()
    {
        InitializeComponent();
        PointerPressed += OnTitleBarPointerPressed;
        MinimizeButton.Click += OnMinimizeClicked;
        MaximizeButton.Click += OnMaximizeClicked;
        CloseButton.Click += OnCloseClicked;
        Loaded += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is not Window window)
                return;

            if (!window.CanResize)
                MaximizeButton.IsVisible = false;

            UpdateMaximizeGlyph(window.WindowState);
            window.PropertyChanged += OnWindowPropertyChanged;
        };
        Unloaded += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window window)
                window.PropertyChanged -= OnWindowPropertyChanged;
        };
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty && e.NewValue is WindowState state)
            UpdateMaximizeGlyph(state);
    }

    private void UpdateMaximizeGlyph(WindowState state)
    {
        bool maximized = state == WindowState.Maximized;
        MaximizeButton.Content = new MaterialIcon
        {
            Kind = maximized ? MaterialIconKind.WindowRestore : MaterialIconKind.WindowMaximize,
            Width = 12,
            Height = 12
        };
        ToolTip.SetTip(MaximizeButton, maximized ? "Restore" : "Maximize");
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || TopLevel.GetTopLevel(this) is not Window window)
            return;

        if (e.ClickCount == 2)
        {
            if (window.CanResize && MaximizeButton.IsVisible)
                window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            e.Handled = true;
            return;
        }

        window.BeginMoveDrag(e);
    }

    private void OnMinimizeClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window window)
            window.WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window window)
            return;

        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window window)
            window.Close();
    }

    private static IImage LoadDefaultIconSource()
    {
        // Keep shell icon parity with WPF title bar artwork so badge tint behavior matches.
        using var stream = AssetLoader.Open(new Uri("avares://Skua.Shared.Avalonia/Assets/SkuaShellImage.png"));
        return new Bitmap(stream);
    }
}
