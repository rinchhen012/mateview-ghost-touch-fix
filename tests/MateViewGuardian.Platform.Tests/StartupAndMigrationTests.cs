using MateViewGuardian.Core;
using MateViewGuardian.Platform.Startup;
using Xunit;

namespace MateViewGuardian.Platform.Tests;

public sealed class StartupAndMigrationTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "mateview-lifecycle-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MacStartupWritesExactCurrentUserLaunchAgentAndDisablesIdempotently()
    {
        var plist = Path.Combine(root, "LaunchAgents", "com.mateview.guardian.plist");
        var manager = new MacStartupManager(plist, "/Applications/MateView Guardian.app/Contents/MacOS/MateViewGuardian.App");

        await manager.SetEnabledAsync(true);
        await manager.SetEnabledAsync(true);

        var content = await File.ReadAllTextAsync(plist);
        Assert.Contains("com.mateview.guardian", content);
        Assert.Contains("/Applications/MateView Guardian.app/Contents/MacOS/MateViewGuardian.App", content);
        Assert.Contains("--background", content);

        await manager.SetEnabledAsync(false);
        await manager.SetEnabledAsync(false);
        Assert.False(File.Exists(plist));
    }

    [Fact]
    public async Task WindowsStartupTouchesOnlyGuardianLauncher()
    {
        var startup = Path.Combine(root, "Startup");
        Directory.CreateDirectory(startup);
        var neighbor = Path.Combine(startup, "OtherApp.cmd");
        await File.WriteAllTextAsync(neighbor, "keep");
        var launcher = Path.Combine(startup, "MateViewGuardian.cmd");
        var manager = new WindowsStartupManager(launcher, "C:\\Users\\Me\\MateViewGuardian.App.exe");

        await manager.SetEnabledAsync(true);

        var content = await File.ReadAllTextAsync(launcher);
        Assert.Contains("MateViewGuardian.App.exe", content);
        Assert.Contains("--background", content);
        await manager.SetEnabledAsync(false);
        Assert.False(File.Exists(launcher));
        Assert.Equal("keep", await File.ReadAllTextAsync(neighbor));
    }

    [Fact]
    public async Task MigrationImportsValidLegacyWindowsTargetAndIgnoresOtherFields()
    {
        var config = Path.Combine(root, "MateViewGhostTouchFix", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        await File.WriteAllTextAsync(config, "{\"DesiredVolume\":60,\"MonitorIndex\":9}");
        var migration = new LegacyMigration(null, config, null);

        var migrated = await migration.ImportSettingsAsync(GuardianSettings.Default);

        Assert.Equal(60, migrated.DesiredVolume);
        Assert.True(migrated.ProtectionEnabled);
    }

    [Fact]
    public async Task StoppingLegacyRemovesOnlyKnownStartupFiles()
    {
        var launchAgents = Path.Combine(root, "LaunchAgents");
        Directory.CreateDirectory(launchAgents);
        var oldMac = Path.Combine(launchAgents, "com.mateview-ghost-touch-fix.plist");
        var neighbor = Path.Combine(launchAgents, "com.other.app.plist");
        var oldWindows = Path.Combine(root, "Startup", "MateViewGhostTouchFix.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(oldWindows)!);
        await File.WriteAllTextAsync(oldMac, "old");
        await File.WriteAllTextAsync(oldWindows, "old");
        await File.WriteAllTextAsync(neighbor, "keep");
        var migration = new LegacyMigration(oldMac, null, oldWindows);

        await migration.StopLegacyStartupAsync();

        Assert.False(File.Exists(oldMac));
        Assert.False(File.Exists(oldWindows));
        Assert.True(File.Exists(neighbor));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
