namespace MateViewGuardian.Platform.Windows;

public static class WindowsHidIdentity
{
    private const string Prefix = "HID\\VID_12D1&PID_10B6";

    public static bool IsAllowed(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId) ||
            !instanceId.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return instanceId.Length == Prefix.Length ||
            instanceId[Prefix.Length] is '&' or '\\';
    }
}
