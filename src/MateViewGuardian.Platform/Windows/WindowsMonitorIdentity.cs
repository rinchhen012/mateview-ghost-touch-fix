using System.Text.RegularExpressions;

namespace MateViewGuardian.Platform.Windows;

public static partial class WindowsMonitorIdentity
{
    public static bool IsMateView(string? description, string? deviceString, string? deviceId) =>
        new[] { description, deviceString, deviceId }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Any(value => ModelRegex().IsMatch(value!));

    [GeneratedRegex(@"(?i)(^|[^A-Z0-9])ZQE-CAA([^A-Z0-9]|$)")]
    private static partial Regex ModelRegex();
}
