using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;

namespace Skua.App.Avalonia.Converters;

public sealed class GrabberItemDisplayConverter : IValueConverter
{
    public static readonly GrabberItemDisplayConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ItemBase item)
        {
            string baseText = item.ToString();

            IJunkService? junkService = Ioc.Default.GetService<IJunkService>();
            if (junkService != null && junkService.IsJunk(item.ID))
            {
                return baseText + " [Junk]";
            }

            return baseText;
        }

        return value?.ToString() ?? string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
