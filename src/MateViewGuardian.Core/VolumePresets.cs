using System.Globalization;

namespace MateViewGuardian.Core;

public static class VolumePresets
{
    private const string CommandPrefix = "set-volume-";

    public static IReadOnlyList<int> Values { get; } =
        [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100];

    public static bool TryParseCommand(string command, out int volume)
    {
        volume = 0;
        if (!command.StartsWith(CommandPrefix, StringComparison.Ordinal) ||
            !int.TryParse(
                command.AsSpan(CommandPrefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed is < 0 or > 100 ||
            parsed % 10 != 0)
        {
            return false;
        }

        volume = parsed;
        return true;
    }
}
