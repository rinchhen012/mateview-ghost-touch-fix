using MateViewGuardian.Core;
using MateViewGuardian.Platform.Windows;
using Xunit;

namespace MateViewGuardian.Platform.Tests;

public sealed class WindowsProtectionTests
{
    private const string MateViewId = "MONITOR\\HWV1234\\ZQE-CAA";

    [Theory]
    [InlineData("ZQE-CAA", "", "", true)]
    [InlineData("Generic PnP Monitor", "HUAWEI ZQE-CAA", "", true)]
    [InlineData("Generic", "", MateViewId, true)]
    [InlineData("ZQE-CAAX", "", "", false)]
    [InlineData("NOTZQE-CAA", "", "", false)]
    [InlineData("Dell", "", "MONITOR\\OTHER", false)]
    public void IdentityRequiresExactModelToken(
        string description,
        string deviceString,
        string deviceId,
        bool expected)
    {
        Assert.Equal(expected, WindowsMonitorIdentity.IsMateView(description, deviceString, deviceId));
    }

    [Fact]
    public async Task ObserveReadsOnlyMateViewVolumeAndMute()
    {
        var other = new FakeMonitor("ZQE-CAAX", "", "", 90, 1);
        var mateView = new FakeMonitor("HUAWEI ZQE-CAA", "", MateViewId, 30, 2);
        var platform = Create(new FakeMonitorApi(other, mateView), new FakeHidProtection());

        var observation = await platform.ObserveAsync(default);

        Assert.True(observation.DisplayConnected);
        Assert.True(observation.DdcHealthy);
        Assert.True(observation.SupportsMute);
        Assert.Equal(30, observation.CurrentVolume);
        Assert.Equal(2, observation.CurrentMute);
        Assert.Equal([0x62, 0x8D], mateView.Reads);
        Assert.Empty(other.Reads);
        Assert.True(other.Disposed);
        Assert.True(mateView.Disposed);
    }

    [Fact]
    public async Task MissingMateViewReturnsDisconnectedWithoutDdcWrites()
    {
        var other = new FakeMonitor("Dell", "", "MONITOR\\DELL", 50, 2);
        var platform = Create(new FakeMonitorApi(other), new FakeHidProtection());

        var observation = await platform.ObserveAsync(default);

        Assert.False(observation.DisplayConnected);
        Assert.False(observation.DdcHealthy);
        Assert.Empty(other.Reads);
    }

    [Theory]
    [InlineData(0x62, 45)]
    [InlineData(0x8D, 2)]
    public async Task WriteAllowsOnlySafeSpeakerCodes(byte code, uint value)
    {
        var mateView = new FakeMonitor("ZQE-CAA", "", MateViewId, 30, 2);
        var platform = Create(new FakeMonitorApi(mateView), new FakeHidProtection());

        await platform.WriteDdcAsync(new DdcCorrection(code, value), default);

        Assert.Equal([(code, value)], mateView.Writes);
    }

    [Theory]
    [InlineData(0x10, 50)]
    [InlineData(0x62, 101)]
    [InlineData(0x8D, 0)]
    [InlineData(0x8D, 3)]
    public async Task WriteRejectsUnsafeCodesAndValues(byte code, uint value)
    {
        var mateView = new FakeMonitor("ZQE-CAA", "", MateViewId, 30, 2);
        var platform = Create(new FakeMonitorApi(mateView), new FakeHidProtection());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            platform.WriteDdcAsync(new DdcCorrection(code, value), default));

        Assert.Empty(mateView.Writes);
    }

    private static WindowsProtection Create(IWindowsMonitorApi monitors, IWindowsHidProtection hid) =>
        new(monitors, hid);

    private sealed class FakeHidProtection : IWindowsHidProtection
    {
        public Task<IReadOnlyList<string>> DisableAsync(
            IReadOnlyList<string> recordedIds,
            CancellationToken cancellationToken) =>
            Task.FromResult(recordedIds);

        public Task EnableAsync(IReadOnlyList<string> recordedIds, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeMonitorApi(params FakeMonitor[] monitors) : IWindowsMonitorApi
    {
        public IReadOnlyList<IWindowsPhysicalMonitor> Enumerate() => monitors;
    }

    private sealed class FakeMonitor(
        string description,
        string deviceString,
        string deviceId,
        uint volume,
        uint mute) : IWindowsPhysicalMonitor
    {
        public string Description { get; } = description;
        public string DeviceString { get; } = deviceString;
        public string DeviceId { get; } = deviceId;
        public string Identity => DeviceId;
        public List<byte> Reads { get; } = [];
        public List<(byte Code, uint Value)> Writes { get; } = [];
        public bool Disposed { get; private set; }

        public uint Read(byte code)
        {
            Reads.Add(code);
            return code switch
            {
                0x62 => volume,
                0x8D => mute,
                _ => throw new InvalidOperationException("Unexpected read."),
            };
        }

        public void Write(byte code, uint value) => Writes.Add((code, value));

        public void Dispose() => Disposed = true;
    }
}
