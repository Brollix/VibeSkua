using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;

namespace Skua.Shared.Avalonia.Controls.Shell;

public partial class WindowTitleBar : UserControl
{
    public static readonly StyledProperty<string> TitleTextProperty =
        AvaloniaProperty.Register<WindowTitleBar, string>(nameof(TitleText), "VibeSkua");

    private static readonly IImage DefaultIconSource = LoadDefaultIconSource();
    private static string? _artworkPath;
    private Bitmap? _artworkBitmap;

    public static event Action<string?>? ArtworkPathChanged;

    public static void SetArtworkPath(string? path)
    {
        _artworkPath = string.IsNullOrWhiteSpace(path) ? null : path;
        ArtworkPathChanged?.Invoke(_artworkPath);
    }

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
        ArtworkPathChanged += ApplyArtworkPath;
        Loaded += (_, _) => ApplyArtworkPath(_artworkPath);
        Unloaded += (_, _) => ArtworkPathChanged -= ApplyArtworkPath;
        PointerPressed += OnTitleBarPointerPressed;
        MinimizeButton.Click += OnMinimizeClicked;
        MaximizeButton.Click += OnMaximizeClicked;
        CloseButton.Click += OnCloseClicked;
        Loaded += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window window && !window.CanResize)
                MaximizeButton.IsVisible = false;
        };
    }

    private void ApplyArtworkPath(string? path)
    {
        _artworkBitmap?.Dispose();
        _artworkBitmap = null;

        if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
        {
            try
            {
                _artworkBitmap = new Bitmap(path);
            }
            catch (Exception)
            {
                // Keep the normal themed title bar if the selected image cannot be loaded.
            }
        }

        ArtworkImage.Source = _artworkBitmap;
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || TopLevel.GetTopLevel(this) is not Window window)
            return;

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
