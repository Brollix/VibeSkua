using Avalonia;
using Avalonia.Controls;
using Skua.Manager.Avalonia.ViewModels;
using System;
using System.ComponentModel;

namespace Skua.Manager.Avalonia.UserControls;

public partial class ArmyQueueUserControl : UserControl
{
    private ArmyQueueViewModel? _currentVm;

    public ArmyQueueUserControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_currentVm != null)
        {
            _currentVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _currentVm = DataContext as ArmyQueueViewModel;

        if (_currentVm != null)
        {
            _currentVm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ArmyQueueViewModel.ShowHelpGuide))
        {
            // When guide is closed or opened, smoothly reset/snap scroll offset to top
            ContentScrollViewer?.SetValue(ScrollViewer.OffsetProperty, Vector.Zero);
        }
    }
}
