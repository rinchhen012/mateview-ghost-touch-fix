using MateViewGuardian.Platform.Mac;
using Xunit;

namespace MateViewGuardian.Platform.Tests;

public sealed class MacStatusMenuLauncherTests
{
    [Fact]
    public void CreateStartInfoPassesParentAndBundlePathsAsSeparateArguments()
    {
        var startInfo = MacStatusMenuLauncher.CreateStartInfo(
            "/Applications/MateView Guardian.app/Contents/Resources/MateViewGuardianMenuBar",
            "/Applications/MateView Guardian.app",
            4242);

        Assert.Equal(
            "/Applications/MateView Guardian.app/Contents/Resources/MateViewGuardianMenuBar",
            startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(
            ["--parent-pid", "4242", "--app-path", "/Applications/MateView Guardian.app"],
            startInfo.ArgumentList);
    }
}
