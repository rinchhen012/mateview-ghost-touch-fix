using System.Text.Json;

namespace MateViewGuardian.Platform.Windows;

public sealed class ElevationDeniedException : InvalidOperationException
{
    public ElevationDeniedException(IReadOnlyList<string> recoveryIds)
        : base("Administrator approval was cancelled. The MateView touch strip was not changed.")
    {
        RecoveryIds = recoveryIds;
    }

    public IReadOnlyList<string> RecoveryIds { get; }
}

public interface IWindowsHidProtection
{
    Task<IReadOnlyList<string>> DisableAsync(
        IReadOnlyList<string> recordedIds,
        CancellationToken cancellationToken);

    Task EnableAsync(IReadOnlyList<string> recordedIds, CancellationToken cancellationToken);
}

public sealed class WindowsHidProtection : IWindowsHidProtection
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);
    private readonly IProcessRunner processRunner;
    private readonly IElevatedProcessRunner elevatedRunner;
    private readonly string powershellPath;
    private readonly string helperPath;

    public WindowsHidProtection(
        IProcessRunner processRunner,
        IElevatedProcessRunner elevatedRunner,
        string powershellPath,
        string helperPath)
    {
        this.processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        this.elevatedRunner = elevatedRunner ?? throw new ArgumentNullException(nameof(elevatedRunner));
        this.powershellPath = RequirePath(powershellPath, nameof(powershellPath));
        this.helperPath = RequirePath(helperPath, nameof(helperPath));
    }

    public async Task<IReadOnlyList<string>> DetectAsync(CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            powershellPath,
            BaseArguments("Detect"),
            CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, "detect MateView HID devices");
        return ParseAllowedIds(result.StandardOutput);
    }

    public async Task<IReadOnlyList<string>> DisableAsync(
        IReadOnlyList<string> recordedIds,
        CancellationToken cancellationToken)
    {
        var detected = await DetectAsync(cancellationToken).ConfigureAwait(false);
        var recoveryIds = NormalizeAllowed(detected.Concat(recordedIds)).ToArray();
        await MutateAsync("Disable", recoveryIds, cancellationToken).ConfigureAwait(false);
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

        var arguments = BaseArguments(action).ToList();
        arguments.Add("-InstanceId");
        arguments.AddRange(instanceIds);
        var result = await elevatedRunner.RunAsync(
            powershellPath,
            arguments,
            CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        if (result.UserDenied)
        {
            throw new ElevationDeniedException(instanceIds);
        }

        if (!result.Started || result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not {action.ToLowerInvariant()} the MateView touch strip (exit {result.ExitCode}).");
        }
    }

    private string[] BaseArguments(string action) =>
        ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", helperPath, "-Action", action];

    private static IReadOnlyList<string> ParseAllowedIds(string json)
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
                    .Where(element => element.ValueKind == JsonValueKind.String)
                    .Select(element => element.GetString() ?? string.Empty),
                JsonValueKind.String => [document.RootElement.GetString() ?? string.Empty],
                _ => [],
            };
            return NormalizeAllowed(values).ToArray();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The MateView HID helper returned invalid JSON.", exception);
        }
    }

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
