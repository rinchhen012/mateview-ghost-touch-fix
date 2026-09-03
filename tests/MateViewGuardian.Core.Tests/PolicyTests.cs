using MateViewGuardian.Core;
using Xunit;

namespace MateViewGuardian.Core.Tests;

public sealed class PolicyTests
{
    [Fact]
    public void DefaultsEnableProtectionAtVolumeThirty()
    {
        var settings = GuardianSettings.Default;

        Assert.True(settings.ProtectionEnabled);
        Assert.Equal(30, settings.DesiredVolume);
        Assert.True(settings.StartAtLogin);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void NormalizeReplacesUnsafeVolumeWithDefault(int unsafeVolume)
    {
        var settings = GuardianSettings.Default with { DesiredVolume = unsafeVolume };

        Assert.Equal(30, settings.Normalize().DesiredVolume);
    }

    [Theory]
    [InlineData(false, true, true, true, true, null, GuardianState.Disabled)]
    [InlineData(true, false, false, false, false, null, GuardianState.Disconnected)]
    [InlineData(true, true, false, false, true, null, GuardianState.PartiallyProtected)]
    [InlineData(true, true, true, true, true, null, GuardianState.Protected)]
    [InlineData(true, true, true, true, true, "DDC failed", GuardianState.Error)]
    public void StatusDerivesUserVisibleState(
        bool enabled,
        bool displayConnected,
        bool hidAvailable,
        bool hidBlocked,
        bool ddcHealthy,
        string? error,
        GuardianState expected)
    {
        var status = GuardianStatus.Derive(
            enabled,
            displayConnected,
            hidAvailable,
            hidBlocked,
            ddcHealthy,
            error);

        Assert.Equal(expected, status.State);
    }

    [Fact]
    public void NoCorrectionsArePlannedAtTarget()
    {
        var result = CorrectionPolicy.GetCorrections(
            currentVolume: 30,
            currentMute: 2,
            GuardianSettings.Default,
            supportsMute: true);

        Assert.Empty(result);
    }

    [Fact]
    public void WindowsCorrectionsAreVolumeThenUnmute()
    {
        var result = CorrectionPolicy.GetCorrections(
            currentVolume: 0,
            currentMute: 1,
            GuardianSettings.Default,
            supportsMute: true);

        Assert.Equal(
            [new DdcCorrection(0x62, 30), new DdcCorrection(0x8D, 2)],
            result);
    }

    [Fact]
    public void MacNeverPlansAMuteWrite()
    {
        var result = CorrectionPolicy.GetCorrections(
            currentVolume: 30,
            currentMute: 1,
            GuardianSettings.Default,
            supportsMute: false);

        Assert.Empty(result);
    }

    [Fact]
    public void DisabledProtectionPlansNoWrites()
    {
        var settings = GuardianSettings.Default with { ProtectionEnabled = false };

        var result = CorrectionPolicy.GetCorrections(0, 1, settings, supportsMute: true);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(500, false, 1000)]
    [InlineData(8000, false, 10000)]
    [InlineData(10000, false, 10000)]
    [InlineData(10000, true, 500)]
    public void RetryDelayDoublesToTenSecondsAndResets(int current, bool succeeded, int expected)
    {
        Assert.Equal(expected, RetryPolicy.Next(current, succeeded));
    }
}
