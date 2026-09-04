using MateViewGuardian.Core;
using MateViewGuardian.Platform;
using MateViewGuardian.Platform.Windows;
using Xunit;

namespace MateViewGuardian.Platform.Tests;

public sealed class WindowsHidTests
{
    private const string MateViewOne = "HID\\VID_12D1&PID_10B6&COL01\\7&AAAA&0&0000";
    private const string MateViewTwo = "HID\\VID_12D1&PID_10B6\\8&BBBB&0&0000";

    [Theory]
    [InlineData(MateViewOne, true)]
    [InlineData("hid\\vid_12d1&pid_10b6\\one", true)]
    [InlineData("HID\\VID_12D1&PID_10B60\\one", false)]
    [InlineData("HID\\VID_12D1&PID_9999\\one", false)]
    [InlineData("USB\\VID_12D1&PID_10B6\\one", false)]
    [InlineData("HID\\VID_12D1&PID_10B6evil", false)]
    public void AllowlistMatchesOnlyExactMateViewHidIdentity(string instanceId, bool expected)
    {
        Assert.Equal(expected, WindowsHidIdentity.IsAllowed(instanceId));
    }

    [Fact]
    public async Task DetectFiltersMalformedAndNonMateViewHelperOutput()
    {
        var processes = new QueueProcessRunner(Result(
            "[\"" + JsonEscape(MateViewOne) + "\",\"HID\\\\VID_9999&PID_9999\\\\OTHER\",42,null]"));
        var protection = Create(processes, new RecordingElevationRunner());

        var ids = await protection.DetectAsync(default);

        Assert.Equal([MateViewOne], ids);
    }

    [Fact]
    public async Task DisableUsesOneElevatedBatchAndReturnsExactRecoveryIds()
    {
        var processes = new QueueProcessRunner(Result(DeviceJson(MateViewOne, "Enabled")));
        var elevation = new RecordingElevationRunner();
        var protection = Create(processes, elevation);

        var ids = await protection.DisableAsync([MateViewTwo], default);

        Assert.Equal([MateViewOne, MateViewTwo], ids);
        var call = Assert.Single(elevation.Calls);
        Assert.Equal("pwsh.exe", call.FileName);
        Assert.Contains("-Action", call.Arguments);
        Assert.Contains("Disable", call.Arguments);
        Assert.Equal(1, call.Arguments.Count(argument => argument == "-InstanceId"));
        Assert.Contains(MateViewOne, call.Arguments);
        Assert.DoesNotContain(MateViewTwo, call.Arguments);
    }

    [Fact]
    public async Task AlreadyRecordedDevicesDoNotRequestElevationAgain()
    {
        var processes = new QueueProcessRunner(Result("[\"" + JsonEscape(MateViewOne) + "\"]"));
        var elevation = new RecordingElevationRunner();
        var protection = Create(processes, elevation);

        var ids = await protection.DisableAsync([MateViewOne], default);

        Assert.Equal([MateViewOne], ids);
        Assert.Empty(elevation.Calls);
    }

    [Fact]
    public async Task RecordedButEnabledDeviceIsDisabledAgain()
    {
        var processes = new QueueProcessRunner(Result(DeviceJson(MateViewOne, "Enabled")));
        var elevation = new RecordingElevationRunner();
        var protection = Create(processes, elevation);

        await protection.DisableAsync([MateViewOne], default);

        Assert.Single(elevation.Calls);
    }

    [Fact]
    public async Task ElevationDenialIsNotPromptedAgainUntilTheUserRetries()
    {
        var processes = new QueueProcessRunner(
            Result(DeviceJson(MateViewOne, "Enabled")),
            Result(DeviceJson(MateViewOne, "Enabled")));
        var elevation = new RecordingElevationRunner
        {
            Result = new ElevatedProcessResult(false, true, -1),
        };
        var protection = Create(processes, elevation);

        await Assert.ThrowsAsync<ElevationDeniedException>(() => protection.DisableAsync([], default));
        await Assert.ThrowsAsync<ElevationDeniedException>(() => protection.DisableAsync([], default));

        Assert.Single(elevation.Calls);
    }

    [Fact]
    public async Task FailedElevationIsNotPromptedAgainUntilTheUserRetries()
    {
        var processes = new QueueProcessRunner(
            Result(DeviceJson(MateViewOne, "Enabled")),
            Result(DeviceJson(MateViewOne, "Enabled")),
            Result(DeviceJson(MateViewOne, "Enabled")));
        var elevation = new RecordingElevationRunner
        {
            Result = new ElevatedProcessResult(true, false, 1),
        };
        var protection = Create(processes, elevation);

        await Assert.ThrowsAsync<HidMutationFailedException>(() => protection.DisableAsync([], default));
        await Assert.ThrowsAsync<HidMutationFailedException>(() => protection.DisableAsync([], default));

        Assert.Single(elevation.Calls);

        protection.ResetElevationDenial();
        await Assert.ThrowsAsync<HidMutationFailedException>(() => protection.DisableAsync([], default));

        Assert.Equal(2, elevation.Calls.Count);
    }

    [Fact]
    public async Task ThrownElevationFailureIsNotPromptedAgainUntilTheUserRetries()
    {
        var processes = new QueueProcessRunner(
            Result(DeviceJson(MateViewOne, "Enabled")),
            Result(DeviceJson(MateViewOne, "Enabled")),
            Result(DeviceJson(MateViewOne, "Enabled")));
        var elevation = new RecordingElevationRunner
        {
            Exception = new TimeoutException("Timed out waiting for the administrator helper."),
        };
        var protection = Create(processes, elevation);

        await Assert.ThrowsAsync<TimeoutException>(() => protection.DisableAsync([], default));
        await Assert.ThrowsAsync<HidMutationFailedException>(() => protection.DisableAsync([], default));

        Assert.Single(elevation.Calls);

        protection.ResetElevationDenial();
        await Assert.ThrowsAsync<TimeoutException>(() => protection.DisableAsync([], default));

        Assert.Equal(2, elevation.Calls.Count);
    }

    [Fact]
    public async Task RefusesToElevateWhenTheBundledHelperWasModified()
    {
        var helper = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(helper, "modified");
            var elevation = new RecordingElevationRunner();
            var protection = new WindowsHidProtection(
                new QueueProcessRunner(Result(DeviceJson(MateViewOne, "Enabled"))),
                elevation,
                "pwsh.exe",
                helper,
                expectedHelperSha256: "00");

            await Assert.ThrowsAsync<InvalidOperationException>(() => protection.DisableAsync([], default));

            Assert.Empty(elevation.Calls);
        }
        finally
        {
            File.Delete(helper);
        }
    }

    [Fact]
    public async Task EnablePassesOnlyAllowlistedRecordedIds()
    {
        var elevation = new RecordingElevationRunner();
        var protection = Create(new QueueProcessRunner(), elevation);

        await protection.EnableAsync(
            [MateViewOne, "HID\\VID_9999&PID_9999\\OTHER", MateViewOne.ToLowerInvariant()],
            default);

        var call = Assert.Single(elevation.Calls);
        Assert.Contains("Enable", call.Arguments);
        Assert.Equal(1, call.Arguments.Count(argument =>
            string.Equals(argument, MateViewOne, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(call.Arguments, argument => argument.Contains("9999", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ElevationDenialIsReportedWithoutLosingRecoveryIds()
    {
        var processes = new QueueProcessRunner(Result(DeviceJson(MateViewOne, "Enabled")));
        var elevation = new RecordingElevationRunner
        {
            Result = new ElevatedProcessResult(false, true, -1),
        };
        var protection = Create(processes, elevation);

        var exception = await Assert.ThrowsAsync<ElevationDeniedException>(() =>
            protection.DisableAsync([MateViewTwo], default));

        Assert.Equal([MateViewOne, MateViewTwo], exception.RecoveryIds);
    }

    private static WindowsHidProtection Create(
        IProcessRunner processes,
        IElevatedProcessRunner elevation) =>
        new(processes, elevation, "pwsh.exe", "C:\\app\\MateViewHid.ps1");

    private static ProcessResult Result(string output, int exitCode = 0) =>
        new(exitCode, output, string.Empty);

    private static string JsonEscape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal);

    private static string DeviceJson(string instanceId, string status) =>
        "[{\"instanceId\":\"" + JsonEscape(instanceId) + "\",\"status\":\"" + status + "\"}]";

    private sealed class QueueProcessRunner(params ProcessResult[] results) : IProcessRunner
    {
        private readonly Queue<ProcessResult> results = new(results);

        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (results.Count == 0)
            {
                throw new InvalidOperationException("Unexpected process call.");
            }

            return Task.FromResult(results.Dequeue());
        }
    }

    private sealed class RecordingElevationRunner : IElevatedProcessRunner
    {
        public List<ElevatedCall> Calls { get; } = [];

        public ElevatedProcessResult Result { get; set; } = new(true, false, 0);

        public Exception? Exception { get; set; }

        public Task<ElevatedProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Calls.Add(new ElevatedCall(fileName, arguments.ToArray()));
            if (Exception is not null)
            {
                throw Exception;
            }
            return Task.FromResult(Result);
        }
    }

    private sealed record ElevatedCall(string FileName, string[] Arguments);
}
