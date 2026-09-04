using MateViewGuardian.Core;
using MateViewGuardian.Platform;
using MateViewGuardian.Platform.Mac;
using Xunit;

namespace MateViewGuardian.Platform.Tests;

public sealed class MacProtectionTests
{
    private const string DisplayLocation = "IOService:/AppleARMPE/arm-io/display@1/AppleCLCD2";

    [Fact]
    public async Task ApplyTargetsOnlyMateViewAndMapsAllFourUsages()
    {
        var runner = new RecordingRunner(
            Result("0x12d1 0x10b6 0x110000 1 1 MateView GT\n"),
            Result("HIDKeyboardModifierMappingSrc = 51539607785;\n"),
            Result(string.Empty));
        var protection = new MacProtection(runner, "/usr/bin/hidutil", "/app/ASDDC");

        await protection.ApplyHidBlockAsync([], default);

        Assert.Equal(3, runner.Calls.Count);
        var set = runner.Calls[2];
        Assert.Equal("/usr/bin/hidutil", set.FileName);
        Assert.Equal("property", set.Arguments[0]);
        Assert.Contains("{\"VendorID\":0x12d1,\"ProductID\":0x10b6}", set.Arguments);
        Assert.Contains("HIDKeyboardModifierMappingSrc\":0xC000000E9", set.Arguments[^1]);
        Assert.Contains("HIDKeyboardModifierMappingSrc\":0xC000000EA", set.Arguments[^1]);
        Assert.Contains("HIDKeyboardModifierMappingSrc\":0xC000000CD", set.Arguments[^1]);
        Assert.Contains("HIDKeyboardModifierMappingSrc\":0xC000000B1", set.Arguments[^1]);
    }

    [Fact]
    public async Task CompleteMappingDoesNotWriteAgain()
    {
        var mapping = string.Join('\n', new[]
        {
            "HIDKeyboardModifierMappingSrc = 51539607785; HIDKeyboardModifierMappingDst = 30064771072;",
            "HIDKeyboardModifierMappingSrc = 51539607786; HIDKeyboardModifierMappingDst = 30064771072;",
            "HIDKeyboardModifierMappingSrc = 51539607757; HIDKeyboardModifierMappingDst = 30064771072;",
            "HIDKeyboardModifierMappingSrc = 51539607729; HIDKeyboardModifierMappingDst = 30064771072;",
        });
        var runner = new RecordingRunner(
            Result("0x12d1 0x10b6 0x110000 1 1 MateView GT\n"),
            Result(mapping));
        var protection = new MacProtection(runner, "hidutil", "ASDDC");

        await protection.ApplyHidBlockAsync([], default);

        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task MappingWithTheWrongDestinationIsReplaced()
    {
        var mapping = string.Join('\n', new[]
        {
            "HIDKeyboardModifierMappingSrc = 51539607785; HIDKeyboardModifierMappingDst = 1;",
            "HIDKeyboardModifierMappingSrc = 51539607786; HIDKeyboardModifierMappingDst = 1;",
            "HIDKeyboardModifierMappingSrc = 51539607757; HIDKeyboardModifierMappingDst = 1;",
            "HIDKeyboardModifierMappingSrc = 51539607729; HIDKeyboardModifierMappingDst = 1;",
        });
        var runner = new RecordingRunner(
            Result("0x12d1 0x10b6 0x110000 1 1 MateView GT\n"),
            Result(mapping),
            Result(string.Empty));
        var protection = new MacProtection(runner, "hidutil", "ASDDC");

        await protection.ApplyHidBlockAsync([], default);

        Assert.Equal(3, runner.Calls.Count);
    }

    [Fact]
    public async Task ObserveSelectsZqeCaaLocationAndParsesVolume()
    {
        var detect = $"IOService:/wrong\n  product name: (ABC) OTHER\n{DisplayLocation}\n  product name:  (HWV) ZQE-CAA\n";
        var mapping = string.Join('\n', new[]
        {
            "HIDKeyboardModifierMappingSrc = 51539607785; HIDKeyboardModifierMappingDst = 30064771072;",
            "HIDKeyboardModifierMappingSrc = 51539607786; HIDKeyboardModifierMappingDst = 30064771072;",
            "HIDKeyboardModifierMappingSrc = 51539607757; HIDKeyboardModifierMappingDst = 30064771072;",
            "HIDKeyboardModifierMappingSrc = 51539607729; HIDKeyboardModifierMappingDst = 30064771072;",
        });
        var runner = new RecordingRunner(
            Result(detect),
            Result("VCP 62 VALUE 00 64 00 1E\n"),
            Result("0x12d1 0x10b6 MateView GT\n"),
            Result(mapping));
        var protection = new MacProtection(runner, "hidutil", "ASDDC");

        var observation = await protection.ObserveAsync(default);

        Assert.True(observation.DisplayConnected);
        Assert.True(observation.HidAvailable);
        Assert.True(observation.HidBlocked);
        Assert.True(observation.DdcHealthy);
        Assert.Equal(30, observation.CurrentVolume);
        Assert.False(observation.SupportsMute);
        Assert.Equal(DisplayLocation, observation.DisplayIdentity);
        Assert.Equal(
            ["getvcp", "--terse", "--display", DisplayLocation, "0x62"],
            runner.Calls[1].Arguments);
    }

    [Fact]
    public async Task ObserveNeverFallsBackToAnUnmatchedDisplay()
    {
        var runner = new RecordingRunner(Result("IOService:/wrong\n  product name: (ABC) OTHER\n"));
        var protection = new MacProtection(runner, "hidutil", "ASDDC");

        var observation = await protection.ObserveAsync(default);

        Assert.False(observation.DisplayConnected);
        Assert.False(observation.DdcHealthy);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task WritePassesExactDisplayAndRejectsEveryOtherVcpCode()
    {
        var runner = new RecordingRunner(
            Result($"{DisplayLocation}\n  product name: (HWV) ZQE-CAA\n"),
            Result("Write OK\n"));
        var protection = new MacProtection(runner, "hidutil", "ASDDC");

        await protection.WriteDdcAsync(new DdcCorrection(0x62, 45), default);

        Assert.Equal(
            ["setvcp", "--display", DisplayLocation, "0x62", "45"],
            runner.Calls[1].Arguments);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            protection.WriteDdcAsync(new DdcCorrection(0x8D, 2), default));
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task ProcessRunnerPreservesArgumentsWithoutShellEvaluation()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var runner = new ProcessRunner();

        var result = await runner.RunAsync(
            "/usr/bin/printf",
            ["[%s]", "a b;$(touch /tmp/guardian-should-not-exist)"],
            TimeSpan.FromSeconds(2),
            default);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("[a b;$(touch /tmp/guardian-should-not-exist)]", result.StandardOutput);
        Assert.False(File.Exists("/tmp/guardian-should-not-exist"));
    }

    [Fact]
    public async Task ProcessRunnerTerminatesCommandsAfterTimeout()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var runner = new ProcessRunner();

        await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(
            "/bin/sh",
            ["-c", "sleep 2"],
            TimeSpan.FromMilliseconds(50),
            default));
    }

    private static ProcessResult Result(string output, int exitCode = 0) => new(exitCode, output, string.Empty);

    private sealed class RecordingRunner(params ProcessResult[] results) : IProcessRunner
    {
        private readonly Queue<ProcessResult> results = new(results);

        public List<ProcessCall> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Calls.Add(new ProcessCall(fileName, arguments.ToArray()));
            if (results.Count == 0)
            {
                throw new InvalidOperationException("Unexpected process call.");
            }

            return Task.FromResult(results.Dequeue());
        }
    }

    private sealed record ProcessCall(string FileName, string[] Arguments);
}
