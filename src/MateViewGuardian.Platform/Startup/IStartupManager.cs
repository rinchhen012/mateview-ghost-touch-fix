namespace MateViewGuardian.Platform.Startup;

public interface IStartupManager
{
    bool IsEnabled { get; }

    Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}
