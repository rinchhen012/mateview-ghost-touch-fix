using MateViewGuardian.App;
using Xunit;

namespace MateViewGuardian.App.Tests;

public sealed class SingleInstanceActivationTests
{
    [Fact]
    public async Task SecondAcquisitionSignalsPrimaryInsteadOfStartingAnotherInstance()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var instanceName = $"MateViewGuardian.Tests.{Guid.NewGuid():N}";
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var primary = SingleInstanceActivation.TryAcquire(
            instanceName,
            () => activated.TrySetResult());

        using var secondary = SingleInstanceActivation.TryAcquire(instanceName, () => { });

        Assert.NotNull(primary);
        Assert.Null(secondary);
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
