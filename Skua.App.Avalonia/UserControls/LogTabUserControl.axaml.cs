using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Skua.App.Avalonia.ViewModels.AppLogs;
using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.UserControls;

public partial class LogTabUserControl : UserControl
{
    public LogTabUserControl()
    {
        InitializeComponent();
        this.AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is LogTabViewModel vm)
            {
                vm.Logs.CollectionChanged += Logs_CollectionChanged;
                if (vm.Logs.Count > 0)
                    LogsListBox.ScrollIntoView(vm.Logs.Count - 1);
            }
        };
        this.DetachedFromVisualTree += (_, _) =>
        {
            if (DataContext is LogTabViewModel vm)
                vm.Logs.CollectionChanged -= Logs_CollectionChanged;
        };
        this.DataContextChanged += (_, _) =>
        {
            if (DataContext is LogTabViewModel vm)
            {
                vm.Logs.CollectionChanged -= Logs_CollectionChanged;
                vm.Logs.CollectionChanged += Logs_CollectionChanged;
                if (vm.Logs.Count > 0)
                    LogsListBox.ScrollIntoView(vm.Logs.Count - 1);
            }
        };
    }

    private void Logs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is LogTabViewModel vm && vm.Logs.Count > 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (vm.Logs.Count > 0)
                    LogsListBox.ScrollIntoView(vm.Logs.Count - 1);
            });
        }
    }

    private async void LogsListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.C)
        {
            await CopySelectedLogsAsync();
            e.Handled = true;
        }
    }

    private async void CopyButton_Click(object? sender, RoutedEventArgs e)
    {
        if (LogsListBox.SelectedItems is { Count: > 0 })
            await CopySelectedLogsAsync();
        else if (DataContext is LogTabViewModel vm)
            vm.CopyLogCommand.Execute(null);
    }

    private async void CopySelected_Click(object? sender, RoutedEventArgs e)
    {
        await CopySelectedLogsAsync();
    }

    private async Task CopySelectedLogsAsync()
    {
        if (LogsListBox.SelectedItems is { Count: > 0 } items)
        {
            string text = string.Join(Environment.NewLine, items.Cast<object>().Select(x => x.ToString()));
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(text);
        }
    }
}
