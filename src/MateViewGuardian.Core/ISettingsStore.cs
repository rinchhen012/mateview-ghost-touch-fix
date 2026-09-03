namespace MateViewGuardian.Core;

public interface ISettingsStore
{
    Task<GuardianSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(GuardianSettings settings, CancellationToken cancellationToken = default);
}
