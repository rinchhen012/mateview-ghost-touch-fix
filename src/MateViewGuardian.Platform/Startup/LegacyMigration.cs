using System.Text.Json;
using MateViewGuardian.Core;

namespace MateViewGuardian.Platform.Startup;

public sealed class LegacyMigration
{
    private readonly string? macLaunchAgentPath;
    private readonly string? windowsConfigPath;
    private readonly string? windowsStartupPath;
    private readonly Func<CancellationToken, Task>? stopLegacyProcess;

    public LegacyMigration(
        string? macLaunchAgentPath,
        string? windowsConfigPath,
        string? windowsStartupPath,
        Func<CancellationToken, Task>? stopLegacyProcess = null)
    {
        this.macLaunchAgentPath = macLaunchAgentPath;
        this.windowsConfigPath = windowsConfigPath;
        this.windowsStartupPath = windowsStartupPath;
        this.stopLegacyProcess = stopLegacyProcess;
    }

    public bool IsPresent => Exists(macLaunchAgentPath) || Exists(windowsStartupPath) || Exists(windowsConfigPath);

    public async Task<GuardianSettings> ImportSettingsAsync(
        GuardianSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (!Exists(windowsConfigPath))
        {
            return settings;
        }

        try
        {
            await using var stream = File.OpenRead(windowsConfigPath!);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (document.RootElement.TryGetProperty("DesiredVolume", out var desiredVolume) &&
                desiredVolume.TryGetInt32(out var volume) && volume is >= 0 and <= 100)
            {
                return (settings with { DesiredVolume = volume }).Normalize();
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        return settings;
    }

    public async Task StopLegacyStartupAsync(CancellationToken cancellationToken = default)
    {
        var hasLegacyStartup = Exists(macLaunchAgentPath) || Exists(windowsStartupPath);
        if (stopLegacyProcess is not null && hasLegacyStartup)
        {
            await stopLegacyProcess(cancellationToken).ConfigureAwait(false);
        }

        DeleteKnownFile(macLaunchAgentPath);
        DeleteKnownFile(windowsStartupPath);
    }

    private static bool Exists(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private static void DeleteKnownFile(string? path)
    {
        if (Exists(path))
        {
            File.Delete(path!);
        }
    }
}
