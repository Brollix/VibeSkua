using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Core.Utils;
using Skua.Shared.Avalonia.ViewModels;

namespace Skua.App.Avalonia.ViewModels;

public partial class JumpViewModel : BotControlViewModelBase
{
    public JumpViewModel(IMapService mapService)
        : base("Jump")
    {
        _mapService = mapService;
        Pads = _mapService.Pads;
    }

    private readonly IMapService _mapService;
    private bool _suppressAutoJump = false;

    [ObservableProperty]
    private string _selectedCell = string.Empty;

    async partial void OnSelectedCellChanged(string value)
    {
        if (!string.IsNullOrEmpty(value) && !_suppressAutoJump)
        {
            if (value == "Enter")
            {
                SelectedPad = "Spawn";
            }
            else if (string.IsNullOrEmpty(SelectedPad))
            {
                SelectedPad = "Left";
            }
            await JumpTo();
        }
    }

    [ObservableProperty]
    private string _selectedPad = string.Empty;

    [ObservableProperty]
    private RangedObservableCollection<string> _cells = new();

    public ImmutableList<string> Pads { get; }

    [RelayCommand]
    private void GetCurrent()
    {
        // Populate the cell list before selecting the player's current cell. Otherwise,
        // the initial selection can be rejected by the bound selector until a cell changes.
        UpdateCells();

        _suppressAutoJump = true;
        try
        {
            (string? cell, string? pad) = _mapService.GetCurrentCell();
            SelectedCell = cell;
            SelectedPad = pad;
        }
        finally
        {
            _suppressAutoJump = false;
        }
    }

    [RelayCommand]
    private async Task JumpTo()
    {
        // Capture the selection before dispatching so rapid arrow-key navigation cannot
        // make a queued jump use a later selection unexpectedly.
        string cell = SelectedCell;
        string pad = SelectedPad;
        if (string.IsNullOrWhiteSpace(cell) || string.IsNullOrWhiteSpace(pad))
            return;

        await Task.Run(() => _mapService.Jump(cell, pad));
    }

    [RelayCommand]
    public void UpdateCells()
    {
        var newCells = _mapService.Cells;
        if (Cells.SequenceEqual(newCells))
            return;

        _suppressAutoJump = true;
        try
        {
            string current = SelectedCell;
            // ReplaceRange also clears stale entries when the map reports no cells.
            Cells.ReplaceRange(newCells);
            if (!string.IsNullOrEmpty(current) && Cells.Contains(current))
                SelectedCell = current;
        }
        finally
        {
            _suppressAutoJump = false;
        }
    }
}