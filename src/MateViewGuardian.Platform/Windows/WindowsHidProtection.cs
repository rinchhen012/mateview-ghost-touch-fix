using System.Text.Json;
using System.Security.Cryptography;
using MateViewGuardian.Core;

namespace MateViewGuardian.Platform.Windows;

public sealed class ElevationDeniedException : HidMutationFailedException
{
    public ElevationDeniedException(IReadOnlyList<string> recoveryIds)
        : base("Administrator approval was cancelled. The MateView touch strip was not changed.", recoveryIds)
    {
    }
}

public interface IWindowsHidProtection
{
    Task<IReadOnlyList<string>> DisableAsync(
        IReadOnlyList<string> recordedIds,
        CancellationToken cancellationToken);

    Task EnableAsync(IReadOnlyList<string> recordedIds, CancellationToken cancellationToken);

    void ResetElevationDenial();
}

public sealed class WindowsHidProtection : IWindowsHidProtection
{
    public const string ReleaseHelperSha256 = "4972fa728072785745fa94e9c6bcae5299a34df3b01b2b2d6c8371838cb5e002";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);
    private readonly IProcessRunner processRunner;
    private readonly IElevatedProcessRunner elevatedRunner;
    private readonly string powershellPath;
    private readonly string helperPath;
    private readonly string? expectedHelperSha256;
    private bool elevationDenied;
    private bool elevationFailed;

    public WindowsHidProtection(
        IProcessRunner processRunner,
        IElevatedProcessRunner elevatedRunner,
        string powershellPath,
        string helperPath,
        string? expectedHelperSha256 = null)
    {
        this.processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        this.elevatedRunner = elevatedRunner ?? throw new ArgumentNullException(nameof(elevatedRunner));
        this.powershellPath = RequirePath(powershellPath, nameof(powershellPath));
        this.helperPath = RequirePath(helperPath, nameof(helperPath));
        this.expectedHelperSha256 = expectedHelperSha256;
    }

    public async Task<IReadOnlyList<string>> DetectAsync(CancellationToken cancellationToken)
    {
        var devices = await DetectDevicesAsync(cancellationToken).ConfigureAwait(false);
        return devices.Select(device => device.InstanceId).ToArray();
    }

    public void ResetElevationDenial()
    {
        elevationDenied = false;
        elevationFailed = false;
    }

    private async Task<IReadOnlyList<HidDevice>> DetectDevicesAsync(CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            powershellPath,
            BaseArguments("Detect"),
            CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, "detect MateView HID devices");
        return ParseAllowedDevices(result.StandardOutput);
    }

    public async Task<IReadOnlyList<string>> DisableAsync(
        IReadOnlyList<string> recordedIds,
        CancellationToken cancellationToken)
    {
        var detected = await DetectDevicesAsync(cancellationToken).ConfigureAwait(false);
        var recorded = NormalizeAllowed(recordedIds).ToArray();
        var recoveryIds = NormalizeAllowed(detected.Select(device => device.InstanceId).Concat(recorded)).ToArray();
        var newIds = detected
            .Where(device => !device.IsDisabled)
            .Select(device => device.InstanceId)
            .ToArray();
        try
        {
            await MutateAsync("Disable", newIds, cancellationToken).ConfigureAwait(false);
        }
        catch (ElevationDeniedException)
        {
            throw new ElevationDeniedException(recoveryIds);
        }
        catch (HidMutationFailedException)
        {
            throw new HidMutationFailedException(
                "Could not change the MateView touch strip. It can still be restored from the app.",
                recoveryIds);
        }
        return recoveryIds;
    }

    public Task EnableAsync(IReadOnlyList<string> recordedIds, CancellationToken cancellationToken) =>
        MutateAsync("Enable", NormalizeAllowed(recordedIds).ToArray(), cancellationToken);

    private async Task MutateAsync(
        string action,
        IReadOnlyList<string> instanceIds,
        CancellationToken cancellationToken)
    {
        if (instanceIds.Count == 0)
        {
            return;
        }

        if (action == "Disable" && elevationDenied)
        {
            throw new ElevationDeniedException(instanceIds);
        }
        if (action == "Disable" && elevationFailed)
        {
            throw new HidMutationFailedException(
                "A previous administrator attempt failed. Retry from the app to try again.",
                instanceIds);
        }

        EnsureHelperIntegrity();

        var arguments = BaseArguments(action).ToList();
        arguments.Add("-InstanceId");
        arguments.AddRange(instanceIds);
        ElevatedProcessResult result;
        try
        {
            result = await elevatedRunner.RunAsync(
                powershellPath,
                arguments,
                CommandTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
            when (action == "Disable" && exception is not OperationCanceledException)
        {
            elevationFailed = true;
            throw;
        }
        if (result.UserDenied)
        {
            elevationDenied = true;
            throw new ElevationDeniedException(instanceIds);
        }

        if (!result.Started || result.ExitCode != 0)
        {
            if (action == "Disable")
            {
                elevationFailed = true;
            }
            throw new HidMutationFailedException(
                $"Could not {action.ToLowerInvariant()} the MateView touch strip (exit {result.ExitCode}).",
                instanceIds);
        }
    }

    private string[] BaseArguments(string action) =>
        ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", helperPath, "-Action", action];

    private void EnsureHelperIntegrity()
    {
        if (string.IsNullOrWhiteSpace(expectedHelperSha256))
        {
            return;
        }

        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(helperPath)));
        if (!string.Equals(actual, expectedHelperSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The bundled MateView HID helper was changed. Reinstall MateView Guardian before approving UAC.");
        }
    }

    private static IReadOnlyList<HidDevice> ParseAllowedDevices(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var values = document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement.EnumerateArray()
                    .Select(ParseDevice)
                    .Where(device => device is not null)
                    .Cast<HidDevice>(),
                JsonValueKind.String => [new HidDevice(document.RootElement.GetString() ?? string.Empty, IsDisabled: true)],
                _ => Array.Empty<HidDevice>(),
            };
            return values
                .Where(device => WindowsHidIdentity.IsAllowed(device.InstanceId))
                .GroupBy(device => device.InstanceId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The MateView HID helper returned invalid JSON.", exception);
        }
    }

    private static HidDevice? ParseDevice(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return new HidDevice(element.GetString() ?? string.Empty, IsDisabled: true);
        }
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("instanceId", out var idElement) ||
            idElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var isDisabled = element.TryGetProperty("status", out var statusElement) &&
            statusElement.ValueKind == JsonValueKind.String &&
            string.Equals(statusElement.GetString(), "Disabled", StringComparison.OrdinalIgnoreCase);
        return new HidDevice(idElement.GetString() ?? string.Empty, isDisabled);
    }

    private sealed record HidDevice(string InstanceId, bool IsDisabled);

    private static IEnumerable<string> NormalizeAllowed(IEnumerable<string> ids) =>
        ids.Where(WindowsHidIdentity.IsAllowed)
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);

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
}
