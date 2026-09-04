using Xunit;

namespace MateViewGuardian.App.Tests;

public sealed class MacMenuCommandServerTests
{
    [Fact]
    public void SocketPathIsPrivateToTheCurrentMacUser()
    {
        var path = MacMenuCommandServer.SocketPath;

        Assert.StartsWith("/tmp/mateview-guardian-", path, StringComparison.Ordinal);
        Assert.EndsWith(".sock", path, StringComparison.Ordinal);
        Assert.DoesNotContain(' ', path);
    }
}
