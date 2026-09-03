namespace MateViewGuardian.Core;

public enum GuardianState
{
    Disabled,
    Disconnected,
    PartiallyProtected,
    Protected,
    Error,
}

public sealed record GuardianStatus(
    GuardianState State,
    bool DisplayConnected,
    bool HidAvailable,
    bool HidBlocked,
    bool DdcHealthy,
    string? Error)
{
    public static GuardianStatus Derive(
        bool enabled,
        bool displayConnected,
        bool hidAvailable,
        bool hidBlocked,
        bool ddcHealthy,
        string? error)
    {
        var state = !string.IsNullOrWhiteSpace(error)
            ? GuardianState.Error
            : !enabled
                ? GuardianState.Disabled
                : !displayConnected
                    ? GuardianState.Disconnected
                    : hidAvailable && hidBlocked && ddcHealthy
                        ? GuardianState.Protected
                        : GuardianState.PartiallyProtected;

        return new GuardianStatus(
            state,
            displayConnected,
            hidAvailable,
            hidBlocked,
            ddcHealthy,
            error);
    }
}
