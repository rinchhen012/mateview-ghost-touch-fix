using MateViewGuardian.Core;
using Xunit;

namespace MateViewGuardian.Core.Tests;

public sealed class VolumePresetsTests
{
    [Fact]
    public void ValuesCoverZeroThroughOneHundredInStepsOfTen()
    {
        Assert.Equal([0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100], VolumePresets.Values);
    }

    [Theory]
    [InlineData("set-volume-0", 0)]
    [InlineData("set-volume-50", 50)]
    [InlineData("set-volume-100", 100)]
    public void TryParseCommandAcceptsMenuPresetCommands(string command, int expectedVolume)
    {
        Assert.True(VolumePresets.TryParseCommand(command, out var volume));
        Assert.Equal(expectedVolume, volume);
    }

    [Theory]
    [InlineData("set-volume--10")]
    [InlineData("set-volume-55")]
    [InlineData("set-volume-110")]
    [InlineData("set-volume-any")]
    public void TryParseCommandRejectsValuesOutsideTheMenuPresets(string command)
    {
        Assert.False(VolumePresets.TryParseCommand(command, out _));
    }
}
