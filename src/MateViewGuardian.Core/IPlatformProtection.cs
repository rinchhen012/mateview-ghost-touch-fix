namespace MateViewGuardian.Core;

public sealed record PlatformObservation(
    bool DisplayConnected,
    bool HidAvailable,
    bool HidBlocked,
    bool DdcHealthy,
    int CurrentVolume,
    int? CurrentMute,
    bool SupportsMute,
    string? DisplayIdentity,
    string? Error);

public interface IPlatformProtection
{
    Task<IReadOnlyList<string>> ApplyHidBlockAsync(
        IReadOnlyList<string> recordedIds,
        CancellationToken cancellationToken);

    Task ClearHidBlockAsync(IReadOnlyList<string> recordedIds, CancellationToken cancellationToken);

    Task<PlatformObservation> ObserveAsync(CancellationToken cancellationToken);

    Task WriteDdcAsync(DdcCorrection correction, CancellationToken cancellationToken);
}
