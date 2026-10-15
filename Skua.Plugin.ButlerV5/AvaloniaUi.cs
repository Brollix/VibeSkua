using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ButlerV5;

internal static class AvaloniaUi
{
    public static readonly IBrush Background = Brush("#1B1B1F");
    public static readonly IBrush Panel = Brush("#28282E");
    public static readonly IBrush PanelRaised = Brush("#303038");
    public static readonly IBrush Text = Brush("#F0F0F3");
    public static readonly IBrush SubText = Brush("#A7A7B2");
    public static readonly IBrush Divider = Brush("#41414B");
    public static readonly IBrush Accent = Brush("#4F8CFF");
    public static readonly IBrush Green = Brush("#3FBF6F");
    public static readonly IBrush Orange = Brush("#FFA23E");
    public static readonly IBrush OrangeButton = Brush("#E8862D");
    public static readonly IBrush Red = Brush("#E05252");
    public static readonly IBrush White = Brushes.White;

    public static SolidColorBrush Brush(string color) => new(Color.Parse(color));

    public static void PrepareWindow(Window window)
    {
        window.Background = Background;
        window.Classes.Add("skua");
    }

    public static TextBlock TextBlock(double size, IBrush? foreground = null) => new()
    {
        FontSize = size,
        Foreground = foreground ?? Text,
    };

    public static Button Button(string text, Action onClick, IBrush? color = null)
    {
        Button button = new()
        {
            Content = text,
            FontSize = 12,
            Padding = new Thickness(12, 5),
            MinHeight = 30,
        };
        button.Classes.Add("skua-button");
        if (color != null)
        {
            button.Background = color;
            button.BorderBrush = color;
            button.Foreground = White;
        }
        button.Click += (_, _) => onClick();
        return button;
    }

    public static void SetToolTip(Control control, string text) => ToolTip.SetTip(control, text);
}
