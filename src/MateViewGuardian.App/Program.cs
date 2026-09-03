using Avalonia;

namespace MateViewGuardian.App;

internal static class Program
{
    internal static bool StartHidden { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        StartHidden = args.Contains("--background", StringComparer.OrdinalIgnoreCase);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
