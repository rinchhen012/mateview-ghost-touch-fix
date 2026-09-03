using System.Text;

namespace MateViewGuardian.Platform.Startup;

public sealed class WindowsStartupManager : IStartupManager
{
    private readonly string launcherPath;
    private readonly string executablePath;

    public WindowsStartupManager(string launcherPath, string executablePath)
    {
        this.launcherPath = RequireSafePath(launcherPath, nameof(launcherPath));
        this.executablePath = RequireSafePath(executablePath, nameof(executablePath));
    }

    public bool IsEnabled => File.Exists(launcherPath);

    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (!enabled)
        {
            if (File.Exists(launcherPath))
            {
                File.Delete(launcherPath);
            }
            return;
        }

        var escaped = executablePath.Replace("%", "%%", StringComparison.Ordinal);
        var launcher = $"@echo off\r\n@start \"\" \"{escaped}\" --background\r\n";
        await AtomicTextFile.WriteAsync(
            launcherPath,
            launcher,
            Encoding.ASCII,
            cancellationToken).ConfigureAwait(false);
    }

    private static string RequireSafePath(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Contains('"', StringComparison.Ordinal) ||
            value.Contains('\r', StringComparison.Ordinal) ||
            value.Contains('\n', StringComparison.Ordinal))
        {
            throw new ArgumentException("A safe path is required.", name);
        }
        return value;
    }
}
