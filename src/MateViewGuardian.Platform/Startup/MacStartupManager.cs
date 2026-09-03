using System.Security;
using System.Text;

namespace MateViewGuardian.Platform.Startup;

public sealed class MacStartupManager : IStartupManager
{
    private readonly string launchAgentPath;
    private readonly string executablePath;

    public MacStartupManager(string launchAgentPath, string executablePath)
    {
        this.launchAgentPath = RequirePath(launchAgentPath, nameof(launchAgentPath));
        this.executablePath = RequirePath(executablePath, nameof(executablePath));
    }

    public bool IsEnabled => File.Exists(launchAgentPath);

    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (!enabled)
        {
            if (File.Exists(launchAgentPath))
            {
                File.Delete(launchAgentPath);
            }
            return;
        }

        var escapedExecutable = SecurityElement.Escape(executablePath) ?? executablePath;
        var plist = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key><string>com.mateview.guardian</string>
              <key>ProgramArguments</key>
              <array><string>{escapedExecutable}</string><string>--background</string></array>
              <key>RunAtLoad</key><true/>
            </dict>
            </plist>
            """;
        await AtomicTextFile.WriteAsync(
            launchAgentPath,
            plist + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken).ConfigureAwait(false);
    }

    private static string RequirePath(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A path is required.", name) : value;
}
