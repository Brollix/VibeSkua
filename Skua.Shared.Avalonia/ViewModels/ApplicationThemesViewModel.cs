using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Shared.Avalonia.Services;
using System.Collections.Generic;

namespace Skua.Shared.Avalonia.ViewModels;

public class ApplicationThemesViewModel : BotControlViewModelBase
{
    private string _selectedTab = "Palette & Accents";

    public ApplicationThemesViewModel(IThemeService themeService, ThemeSettingsViewModel themeSettings, ColorSchemeEditorViewModel colorSchemeEditor, BackgroundThemeViewModel backgroundTheme)
        : base("Application Themes", 620, 680)
    {
        ThemeService = themeService;
        ThemeSettings = themeSettings;
        ColorSchemeEditor = colorSchemeEditor;
        BackgroundTheme = backgroundTheme;
        SetCurrentThemeCommand = new RelayCommand<object>(t => ThemeService.SetCurrentTheme(t));
        RemoveThemeCommand = new RelayCommand<object>(t => ThemeService.RemoveTheme(t));
    }

    public IThemeService ThemeService { get; }

    /// <summary>
    /// Controls whether the main VibeSkua title bar uses the active theme's accent color.
    /// </summary>
    public bool UseAccentOnMainTitleBar
    {
        get => ThemeService is AvaloniaThemeService avaloniaTheme && avaloniaTheme.UseAccentOnMainTitleBar;
        set
        {
            if (ThemeService is not AvaloniaThemeService avaloniaTheme || avaloniaTheme.UseAccentOnMainTitleBar == value)
                return;

            avaloniaTheme.UseAccentOnMainTitleBar = value;
            OnPropertyChanged();
        }
    }

    public ThemeSettingsViewModel ThemeSettings { get; }
    public ColorSchemeEditorViewModel ColorSchemeEditor { get; }
    public BackgroundThemeViewModel BackgroundTheme { get; }
    public IRelayCommand SetCurrentThemeCommand { get; }
    public IRelayCommand RemoveThemeCommand { get; }

    public List<string> Tabs { get; } = new() { "Palette & Accents", "Theme Library", "Custom Backgrounds" };

    public string SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }
}