using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.ViewModels;
using Skua.Core.Options;
using System;
using System.Collections.Generic;

namespace Skua.Shared.Avalonia.ViewModels.Options;

public partial class OptionContainerItemViewModel : ObservableObject, IOptionContainerItemViewModel
{
    public OptionContainerItemViewModel(IOptionContainer container, IOption option)
    {
        Container = container;
        Option = option;
        Type = option.Type;
        Category = string.IsNullOrWhiteSpace(option.Category) ? "Options" : option.Category;

        string currentRawVal = container.GetDirect(option);

        if (Type.IsEnum)
        {
            string[] enumNames = Enum.GetNames(Type);
            EnumValues = new List<string>(enumNames.Length);
            foreach (string name in enumNames)
                EnumValues.Add(name.Replace('_', ' '));

            string displayStr = currentRawVal.Replace('_', ' ');
            SelectedValue = EnumValues.Contains(displayStr)
                ? displayStr
                : (EnumValues.Count > 0 ? EnumValues[0] : string.Empty);
            _value = SelectedValue;
            return;
        }

        if (Type == typeof(bool))
        {
            if (bool.TryParse(currentRawVal, out bool parsedBool))
                _value = parsedBool;
            else if (option.DefaultValue is bool b)
                _value = b;
            else
                _value = false;
        }
        else
        {
            _value = !string.IsNullOrEmpty(currentRawVal) ? currentRawVal : (option.DefaultValue?.ToString() ?? string.Empty);
        }
    }

    [ObservableProperty]
    private object _value;

    [ObservableProperty]
    private List<string>? _enumValues;

    [ObservableProperty]
    private string? _selectedValue;

    public IOptionContainer Container { get; }
    public IOption Option { get; }
    public Type Type { get; }
    public string Category { get; }

    public bool IsEnum => Type.IsEnum;
    public bool IsBool => Type == typeof(bool);
    public bool IsText => !IsEnum && !IsBool;

    public void ResetToDefault()
    {
        if (Type.IsEnum)
        {
            string defaultStr = Option.DefaultValue?.ToString()?.Replace('_', ' ') ?? (EnumValues?.Count > 0 ? EnumValues[0] : string.Empty);
            SelectedValue = EnumValues != null && EnumValues.Contains(defaultStr) ? defaultStr : (EnumValues?.Count > 0 ? EnumValues[0] : string.Empty);
            Value = SelectedValue;
        }
        else if (Type == typeof(bool))
        {
            if (Option.DefaultValue is bool b)
                Value = b;
            else if (bool.TryParse(Option.DefaultValue?.ToString(), out bool parsedBool))
                Value = parsedBool;
            else
                Value = false;
        }
        else
        {
            Value = Option.DefaultValue?.ToString() ?? string.Empty;
        }
    }
}
