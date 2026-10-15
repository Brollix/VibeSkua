using Avalonia;
using Avalonia.Controls;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;

namespace Skua.Shared.Avalonia.Controls;

public class PropertyItemRow
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
}

public partial class PropertyGridControl : UserControl
{
    public static readonly StyledProperty<object?> SelectedObjectProperty =
        AvaloniaProperty.Register<PropertyGridControl, object?>(nameof(SelectedObject));

    public object? SelectedObject
    {
        get => GetValue(SelectedObjectProperty);
        set => SetValue(SelectedObjectProperty, value);
    }

    private readonly ObservableCollection<PropertyItemRow> _properties = new();

    public PropertyGridControl()
    {
        InitializeComponent();
        PropertiesItemsControl.ItemsSource = _properties;
        UpdateVisibility();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectedObjectProperty)
        {
            RebuildProperties();
        }
    }

    private void RebuildProperties()
    {
        _properties.Clear();
        object? obj = SelectedObject;

        if (obj == null)
        {
            UpdateVisibility();
            return;
        }

        Type type = obj.GetType();
        PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (PropertyInfo pi in properties)
        {
            if (!pi.CanRead || pi.GetIndexParameters().Length > 0)
                continue;

            try
            {
                object? val = pi.GetValue(obj);
                string strVal = val switch
                {
                    null => "null",
                    bool b => b ? "True" : "False",
                    _ => val.ToString() ?? string.Empty
                };

                string desc = pi.GetCustomAttribute<DescriptionAttribute>()?.Description ?? pi.Name;
                string category = pi.GetCustomAttribute<CategoryAttribute>()?.Category ?? "General";

                _properties.Add(new PropertyItemRow
                {
                    Name = pi.Name,
                    Value = strVal,
                    Description = desc,
                    Category = category
                });
            }
            catch
            {
                // Ignore properties that throw on read
            }
        }

        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        bool hasProps = _properties.Count > 0;
        EmptyTextBlock.IsVisible = !hasProps;
        ContentScrollViewer.IsVisible = hasProps;
    }
}
