using System.Diagnostics;

namespace MateViewGuardian.Platform.Mac;

public sealed class MacStatusMenuLauncher : IDisposable
{
    private Process? process;

    public void Start(string helperPath, string appPath)
    {
        if (!OperatingSystem.IsMacOS() ||
            !File.Exists(helperPath) ||
            (process is { HasExited: false }))
        {
            return;
        }

        process?.Dispose();
        process = Process.Start(CreateStartInfo(helperPath, appPath, Environment.ProcessId));
    }

    public void Dispose()
    {
        process?.Dispose();
        process = null;
    }

    public static ProcessStartInfo CreateStartInfo(string helperPath, string appPath, int parentProcessId)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = helperPath,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(parentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--app-path");
        startInfo.ArgumentList.Add(appPath);
        return startInfo;
    }
}
