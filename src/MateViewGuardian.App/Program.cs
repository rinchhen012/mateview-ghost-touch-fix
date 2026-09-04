using Avalonia;

namespace MateViewGuardian.App;

internal static class Program
{
    private const string WindowsInstanceName = "MateViewGuardian";
    private static int activationRequested;

    internal static bool StartHidden { get; private set; }

    internal static bool RestoreAndExit { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        StartHidden = args.Contains("--background", StringComparer.OrdinalIgnoreCase);
        RestoreAndExit = args.Contains("--restore-and-exit", StringComparer.OrdinalIgnoreCase);
        using var singleInstance = AcquireSingleInstance();
        if (OperatingSystem.IsWindows() && !RestoreAndExit && singleInstance is null)
        {
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    internal static bool ConsumeActivationRequest() =>
        Interlocked.Exchange(ref activationRequested, 0) == 1;

    private static SingleInstanceActivation? AcquireSingleInstance()
    {
        if (!OperatingSystem.IsWindows() || RestoreAndExit)
        {
            return null;
        }

        return SingleInstanceActivation.TryAcquire(WindowsInstanceName, RequestActivation);
    }

    private static void RequestActivation()
    {
        Interlocked.Exchange(ref activationRequested, 1);
        if (Application.Current is App app)
        {
            app.ActivateFromSecondInstance();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
