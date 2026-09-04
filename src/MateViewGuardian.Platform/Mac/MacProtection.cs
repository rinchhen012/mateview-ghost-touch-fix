using System.Globalization;
using System.Text.RegularExpressions;
using MateViewGuardian.Core;

namespace MateViewGuardian.Platform.Mac;

public sealed partial class MacProtection : IPlatformProtection
{
    private const string Match = "{\"VendorID\":0x12d1,\"ProductID\":0x10b6}";
    private const string Mapping = "{\"UserKeyMapping\":[{\"HIDKeyboardModifierMappingSrc\":0xC000000E9,\"HIDKeyboardModifierMappingDst\":0x700000000},{\"HIDKeyboardModifierMappingSrc\":0xC000000EA,\"HIDKeyboardModifierMappingDst\":0x700000000},{\"HIDKeyboardModifierMappingSrc\":0xC000000CD,\"HIDKeyboardModifierMappingDst\":0x700000000},{\"HIDKeyboardModifierMappingSrc\":0xC000000B1,\"HIDKeyboardModifierMappingDst\":0x700000000}]}";
    private static readonly string[] MappingSources =
        ["51539607785", "51539607786", "51539607757", "51539607729"];
    private const string MappingDestination = "30064771072";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);

    private readonly IProcessRunner runner;
    private readonly string hidutilPath;
    private readonly string ddcPath;
    private string? displayLocation;

    public MacProtection(IProcessRunner runner, string hidutilPath, string ddcPath)
    {
        this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
        this.hidutilPath = RequirePath(hidutilPath, nameof(hidutilPath));
        this.ddcPath = RequirePath(ddcPath, nameof(ddcPath));
    }

    public async Task<IReadOnlyList<string>> ApplyHidBlockAsync(
        IReadOnlyList<string> recordedIds,
        CancellationToken cancellationToken)
    {
        if (!await IsHidPresentAsync(cancellationToken).ConfigureAwait(false))
        {
            return recordedIds;
        }

        if (await IsMappingActiveAsync(cancellationToken).ConfigureAwait(false))
        {
            return recordedIds;
        }

        var result = await runner.RunAsync(
            hidutilPath,
            ["property", "--matching", Match, "--set", Mapping],
            CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, "apply the MateView HID block");
        return recordedIds;
    }

    public async Task ClearHidBlockAsync(
        IReadOnlyList<string> recordedIds,
        CancellationToken cancellationToken)
    {
        if (!await IsHidPresentAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var result = await runner.RunAsync(
            hidutilPath,
            ["property", "--matching", Match, "--set", "{\"UserKeyMapping\":[]}"],
            CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, "clear the MateView HID block");
    }

    public async Task<PlatformObservation> ObserveAsync(CancellationToken cancellationToken)
    {
        displayLocation = await FindDisplayLocationAsync(cancellationToken).ConfigureAwait(false);
        if (displayLocation is null)
        {
            return new PlatformObservation(false, false, false, false, 0, null, false, null, null);
        }

        var volumeResult = await runner.RunAsync(
            ddcPath,
            ["getvcp", "--terse", "--display", displayLocation, "0x62"],
            CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(volumeResult, "read MateView speaker volume");
        var volume = ParseVolume(volumeResult.StandardOutput);
        var hidAvailable = await IsHidPresentAsync(cancellationToken).ConfigureAwait(false);
        var hidBlocked = hidAvailable && await IsMappingActiveAsync(cancellationToken).ConfigureAwait(false);

        return new PlatformObservation(
            true,
            hidAvailable,
            hidBlocked,
            true,
            volume,
            null,
            false,
            displayLocation,
            null);
    }

    public async Task WriteDdcAsync(DdcCorrection correction, CancellationToken cancellationToken)
    {
        if (correction.Code != 0x62 || correction.Value > 100)
        {
            throw new InvalidOperationException("macOS permits only VCP 0x62 volume writes from 0 through 100.");
        }

        displayLocation ??= await FindDisplayLocationAsync(cancellationToken).ConfigureAwait(false);
        if (displayLocation is null)
        {
            throw new InvalidOperationException("No ZQE-CAA display was found.");
        }

        var result = await runner.RunAsync(
            ddcPath,
            ["setvcp", "--display", displayLocation, "0x62", correction.Value.ToString(CultureInfo.InvariantCulture)],
            CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, "write MateView speaker volume");
    }

    private async Task<bool> IsHidPresentAsync(CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(
            hidutilPath,
            ["list"],
            CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, "enumerate HID devices");
        return HidIdentityRegex().IsMatch(result.StandardOutput);
    }

    private async Task<bool> IsMappingActiveAsync(CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(
            hidutilPath,
            ["property", "--matching", Match, "--get", "UserKeyMapping"],
            CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, "read the MateView HID mapping");
        return MappingSources.All(source =>
            result.StandardOutput.Contains(
                $"HIDKeyboardModifierMappingSrc = {source}",
                StringComparison.Ordinal) &&
            result.StandardOutput.Contains(
                $"HIDKeyboardModifierMappingDst = {MappingDestination}",
                StringComparison.Ordinal));
    }

    private async Task<string?> FindDisplayLocationAsync(CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(
            ddcPath,
            ["detect"],
            CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, "detect external displays");

        string? previous = null;
        foreach (var rawLine in result.StandardOutput.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Contains("ZQE-CAA", StringComparison.OrdinalIgnoreCase) &&
                previous is not null &&
                previous.StartsWith("IOService:", StringComparison.Ordinal))
            {
                return previous;
            }

            if (line.Length > 0)
            {
                previous = line;
            }
        }

        return null;
    }

    private static int ParseVolume(string output)
    {
        var match = VcpVolumeRegex().Match(output);
        if (!match.Success)
        {
            throw new InvalidOperationException("The MateView returned an invalid VCP 0x62 response.");
        }

        var high = byte.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var low = byte.Parse(match.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var volume = (high << 8) | low;
        if (volume is < 0 or > 100)
        {
            throw new InvalidOperationException($"The MateView returned unsafe volume {volume}.");
        }

        return volume;
    }

    private static void EnsureSuccess(ProcessResult result, string action)
    {
        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput.Trim()
                : result.StandardError.Trim();
            throw new InvalidOperationException($"Could not {action}: {detail}");
        }
    }

    private static string RequirePath(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("An executable path is required.", parameterName)
            : value;

    [GeneratedRegex(@"(?im)^0x12d1\s+0x10b6(?:\s|$)")]
    private static partial Regex HidIdentityRegex();

    [GeneratedRegex(@"(?im)^VCP\s+62\s+VALUE\s+[0-9A-F]{2}\s+[0-9A-F]{2}\s+([0-9A-F]{2})\s+([0-9A-F]{2})\s*$")]
    private static partial Regex VcpVolumeRegex();
}
