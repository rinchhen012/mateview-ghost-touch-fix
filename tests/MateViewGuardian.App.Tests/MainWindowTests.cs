using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using MateViewGuardian.App;
using Xunit;

namespace MateViewGuardian.App.Tests;

public sealed class MainWindowTests
{
    [Fact]
    public void MessageDialogPlacesItsButtonRowBelowTheMessage()
    {
        var content = MainWindow.CreateMessageDialogContent("Message", "Cancel");
        var grid = content.Grid;

        Assert.Equal(2, grid.RowDefinitions.Count);
        Assert.True(grid.RowDefinitions[1].Height.IsAuto);
        Assert.Equal(1, Grid.GetRow(content.Buttons));
        Assert.Contains(content.Buttons, grid.Children);
    }

    [Fact]
    public void DialogButtonsReserveEnoughSpaceForWindowsTextRendering()
    {
        var button = MainWindow.CreateDialogButton("Cancel");

        Assert.True(button.MinHeight >= 40);
        Assert.Equal(new Thickness(16, 8), button.Padding);
        Assert.Equal(HorizontalAlignment.Center, button.HorizontalContentAlignment);
        Assert.Equal(VerticalAlignment.Center, button.VerticalContentAlignment);
    }
}
