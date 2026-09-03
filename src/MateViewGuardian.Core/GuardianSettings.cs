namespace MateViewGuardian.Core;

public sealed record GuardianSettings
{
    public const int DefaultVolume = 30;

    public bool ProtectionEnabled { get; init; } = true;

    public int DesiredVolume { get; init; } = DefaultVolume;

    public bool StartAtLogin { get; init; } = true;

    public string? SelectedMonitorIdentity { get; init; }

    public string[] DisabledHidInstanceIds { get; init; } = [];

    public static GuardianSettings Default => new();

    public GuardianSettings Normalize()
    {
        var volume = DesiredVolume is >= 0 and <= 100 ? DesiredVolume : DefaultVolume;
        var ids = (DisabledHidInstanceIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return this with
        {
            DesiredVolume = volume,
            SelectedMonitorIdentity = string.IsNullOrWhiteSpace(SelectedMonitorIdentity)
                ? null
                : SelectedMonitorIdentity.Trim(),
            DisabledHidInstanceIds = ids,
        };
    }
}
