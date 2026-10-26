using Avalonia.Controls;
using Avalonia.Input;
using Skua.App.Avalonia.ViewModels;

namespace Skua.App.Avalonia.UserControls;

public partial class JumpUserControl : UserControl
{
    public JumpUserControl()
    {
        InitializeComponent();
        Cells.DropDownOpened += Cells_DropDownOpened;
        Cells.KeyDown += Cells_KeyDown;
    }

    private void Cells_DropDownOpened(object? sender, System.EventArgs e)
    {
        if (DataContext is JumpViewModel vm)
            vm.UpdateCellsCommand.Execute(null);
    }

    private void Cells_KeyDown(object? sender, KeyEventArgs e)
    {
        if (Cells.IsDropDownOpen || Cells.ItemCount == 0)
            return;

        int direction = e.Key switch
        {
            Key.Up => -1,
            Key.Down => 1,
            _ => 0
        };

        if (direction == 0)
            return;

        int selectedIndex = Cells.SelectedIndex;
        if (selectedIndex < 0)
            selectedIndex = direction > 0 ? -1 : Cells.ItemCount;

        int nextIndex = System.Math.Clamp(selectedIndex + direction, 0, Cells.ItemCount - 1);
        if (nextIndex != Cells.SelectedIndex)
            Cells.SelectedIndex = nextIndex;

        e.Handled = true;
    }
}