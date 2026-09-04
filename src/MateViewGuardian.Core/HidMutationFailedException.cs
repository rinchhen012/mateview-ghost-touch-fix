namespace MateViewGuardian.Core;

/// <summary>
/// Reports the exact MateView HID instance IDs that may need restoring after a
/// privileged HID operation did not complete.
/// </summary>
public class HidMutationFailedException : InvalidOperationException
{
    public HidMutationFailedException(string message, IReadOnlyList<string> recoveryIds, Exception? innerException = null)
        : base(message, innerException)
    {
        RecoveryIds = recoveryIds;
    }

    public IReadOnlyList<string> RecoveryIds { get; }
}
