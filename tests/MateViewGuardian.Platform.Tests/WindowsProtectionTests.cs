using System.ComponentModel;
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
    [InlineData("Generic PnP Monitor", "", "MONITOR\\HWV6A25\\{4d36e96e-e325-11ce-bfc1-08002be10318}\\0003", true)]
    [InlineData("Generic PnP Monitor", "", "MONITOR\\HWV6A25X\\{4d36e96e-e325-11ce-bfc1-08002be10318}\\0003", false)]
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
    public async Task ObserveContinuesWhenMuteVcpIsUnsupported()
    {
        var unsupported = new Win32Exception(unchecked((int)0xC0262584));
        var mateView = new FakeMonitor(
            "HUAWEI ZQE-CAA", "", MateViewId, 30, 0,
            muteReadException: unsupported);
        var platform = Create(new FakeMonitorApi(mateView), new FakeHidProtection());

        var observation = await platform.ObserveAsync(default);

        Assert.True(observation.DisplayConnected);
        Assert.True(observation.DdcHealthy);
        Assert.Equal(30, observation.CurrentVolume);
        Assert.Null(observation.CurrentMute);
        Assert.False(observation.SupportsMute);
        Assert.Equal([0x62, 0x8D], mateView.Reads);
    }

    [Fact]
    public async Task ObservePropagatesOtherMuteReadFailures()
    {
        var transmissionFailure = new Win32Exception(unchecked((int)0xC0262582));
        var mateView = new FakeMonitor(
            "HUAWEI ZQE-CAA", "", MateViewId, 30, 0,
            muteReadException: transmissionFailure);
        var platform = Create(new FakeMonitorApi(mateView), new FakeHidProtection());

        var exception = await Assert.ThrowsAsync<Win32Exception>(
            () => platform.ObserveAsync(default));

        Assert.Equal(transmissionFailure.NativeErrorCode, exception.NativeErrorCode);
    }

    [Fact]
    public async Task ObserveRetriesOnceAfterWindowsReturnsDisposedMonitorHandle()
    {
        var stale = new FakeMonitor(
            "ZQE-CAA", "", MateViewId, 30, 2,
            volumeReadException: new ObjectDisposedException("WindowsPhysicalMonitor"));
        var healthy = new FakeMonitor("ZQE-CAA", "", MateViewId, 30, 2);
        var platform = Create(new SequenceMonitorApi([stale], [healthy]), new FakeHidProtection());

        var observation = await platform.ObserveAsync(default);

        Assert.True(observation.DdcHealthy);
        Assert.Equal(30, observation.CurrentVolume);
        Assert.True(stale.Disposed);
        Assert.True(healthy.Disposed);
    }

    [Fact]
    public async Task ObservePropagatesDisposalErrorsFromOtherMonitorTypes()
    {
        var stale = new FakeMonitor(
            "ZQE-CAA", "", MateViewId, 30, 2,
            volumeReadException: new ObjectDisposedException("OtherMonitor"));
        var healthy = new FakeMonitor("ZQE-CAA", "", MateViewId, 30, 2);
        var monitors = new SequenceMonitorApi([stale], [healthy]);
        var platform = Create(monitors, new FakeHidProtection());

        var exception = await Assert.ThrowsAsync<ObjectDisposedException>(
            () => platform.ObserveAsync(default));

        Assert.Equal("OtherMonitor", exception.ObjectName);
        Assert.Equal(1, monitors.EnumerateCount);
        Assert.True(stale.Disposed);
        Assert.False(healthy.Disposed);
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

    [Fact]
    public async Task HidFailureStillReportsDetectedUsbData()
    {
        var hid = new FakeHidProtection
        {
            DisableException = new HidMutationFailedException(
                "pnputil failed", ["HID\\VID_12D1&PID_10B6&COL01\\ONE"]),
        };
        var mateView = new FakeMonitor("ZQE-CAA", "", MateViewId, 30, 2);
        var platform = Create(new FakeMonitorApi(mateView), hid);

        await Assert.ThrowsAsync<HidMutationFailedException>(() => platform.ApplyHidBlockAsync([], default));
        var observation = await platform.ObserveAsync(default);

        Assert.True(observation.HidAvailable);
        Assert.False(observation.HidBlocked);
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
    [InlineData(0x8D, 1)]
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

    [Fact]
    public async Task WriteRetriesOnceAfterWindowsReturnsDisposedMonitorHandle()
    {
        var stale = new FakeMonitor(
            "ZQE-CAA", "", MateViewId, 30, 2,
            writeException: new ObjectDisposedException("WindowsPhysicalMonitor"));
        var healthy = new FakeMonitor("ZQE-CAA", "", MateViewId, 30, 2);
        var platform = Create(new SequenceMonitorApi([stale], [healthy]), new FakeHidProtection());

        await platform.WriteDdcAsync(new DdcCorrection(0x62, 30), default);

        Assert.Equal([(0x62, 30u)], healthy.Writes);
        Assert.True(stale.Disposed);
        Assert.True(healthy.Disposed);
    }

    [Fact]
    public async Task WritePropagatesDisposalErrorsFromOtherMonitorTypes()
    {
        var stale = new FakeMonitor(
            "ZQE-CAA", "", MateViewId, 30, 2,
            writeException: new ObjectDisposedException("OtherMonitor"));
        var healthy = new FakeMonitor("ZQE-CAA", "", MateViewId, 30, 2);
        var monitors = new SequenceMonitorApi([stale], [healthy]);
        var platform = Create(monitors, new FakeHidProtection());

        var exception = await Assert.ThrowsAsync<ObjectDisposedException>(
            () => platform.WriteDdcAsync(new DdcCorrection(0x62, 30), default));

        Assert.Equal("OtherMonitor", exception.ObjectName);
        Assert.Equal(1, monitors.EnumerateCount);
        Assert.True(stale.Disposed);
        Assert.False(healthy.Disposed);
    }

    private static WindowsProtection Create(IWindowsMonitorApi monitors, IWindowsHidProtection hid) =>
        new(monitors, hid);

    private sealed class FakeHidProtection : IWindowsHidProtection
    {
        public Exception? DisableException { get; set; }

        public void ResetElevationDenial()
        {
        }

        public Task<IReadOnlyList<string>> DisableAsync(
            IReadOnlyList<string> recordedIds,
            CancellationToken cancellationToken)
        {
            if (DisableException is not null)
            {
                throw DisableException;
            }
            return Task.FromResult(recordedIds);
        }

        public Task EnableAsync(IReadOnlyList<string> recordedIds, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeMonitorApi(params FakeMonitor[] monitors) : IWindowsMonitorApi
    {
        public IReadOnlyList<IWindowsPhysicalMonitor> Enumerate() => monitors;
    }

    private sealed class SequenceMonitorApi(params FakeMonitor[][] monitorSets) : IWindowsMonitorApi
    {
        private readonly Queue<FakeMonitor[]> monitorSets = new(monitorSets);
        public int EnumerateCount { get; private set; }

        public IReadOnlyList<IWindowsPhysicalMonitor> Enumerate()
        {
            EnumerateCount++;
            return monitorSets.Dequeue();
        }
    }

    private sealed class FakeMonitor(
        string description,
        string deviceString,
        string deviceId,
        uint volume,
        uint mute,
        Exception? muteReadException = null,
        Exception? volumeReadException = null,
        Exception? writeException = null) : IWindowsPhysicalMonitor
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
                0x62 when volumeReadException is not null => throw volumeReadException,
                0x62 => volume,
                0x8D when muteReadException is not null => throw muteReadException,
                0x8D => mute,
                _ => throw new InvalidOperationException("Unexpected read."),
            };
        }

        public void Write(byte code, uint value)
        {
            if (writeException is not null)
            {
                throw writeException;
            }
            Writes.Add((code, value));
        }

        public void Dispose() => Disposed = true;
    }
}
