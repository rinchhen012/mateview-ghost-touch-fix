using Avalonia;
using Avalonia.Layout;
using MateViewGuardian.App;
using Xunit;

namespace MateViewGuardian.App.Tests;

public sealed class MainWindowTests
{
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
