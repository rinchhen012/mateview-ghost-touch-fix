namespace MateViewGuardian.Core;

public sealed class ProtectionCoordinator : IAsyncDisposable
{
    private readonly IPlatformProtection platform;
    private readonly ISettingsStore settingsStore;
    private readonly SemaphoreSlim cycleGate = new(1, 1);
    private CancellationTokenSource? loopCancellation;
    private Task? loopTask;
    private bool settingsLoaded;

    public ProtectionCoordinator(IPlatformProtection platform, ISettingsStore settingsStore)
    {
        this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    public event EventHandler<GuardianStatus>? StatusChanged;

    public event EventHandler<GuardianSettings>? SettingsChanged;

    public GuardianSettings Settings { get; private set; } = GuardianSettings.Default;

    public GuardianStatus Status { get; private set; } = GuardianStatus.Derive(
        enabled: true,
        displayConnected: false,
        hidAvailable: false,
        hidBlocked: false,
        ddcHealthy: false,
        error: null);

    public int CurrentRetryMilliseconds { get; private set; } = RetryPolicy.ActiveMilliseconds;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await cycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureSettingsLoadedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            cycleGate.Release();
        }
    }

    public async Task<GuardianStatus> RunCycleAsync(CancellationToken cancellationToken = default)
    {
        await cycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureSettingsLoadedAsync(cancellationToken).ConfigureAwait(false);
            if (!Settings.ProtectionEnabled)
            {
                await platform.ClearHidBlockAsync(Settings.DisabledHidInstanceIds, cancellationToken)
                    .ConfigureAwait(false);
                CurrentRetryMilliseconds = RetryPolicy.Next(CurrentRetryMilliseconds, succeeded: true);
                return PublishStatus(GuardianStatus.Derive(
                    enabled: false,
                    displayConnected: false,
                    hidAvailable: false,
                    hidBlocked: false,
                    ddcHealthy: false,
                    error: null));
            }

            await platform.ApplyHidBlockAsync(cancellationToken).ConfigureAwait(false);
            var observation = await platform.ObserveAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(observation.Error))
            {
                throw new InvalidOperationException(observation.Error);
            }

            var corrections = CorrectionPolicy.GetCorrections(
                observation.CurrentVolume,
                observation.CurrentMute,
                Settings,
                observation.SupportsMute);
            foreach (var correction in corrections)
            {
                await platform.WriteDdcAsync(correction, cancellationToken).ConfigureAwait(false);
            }

            CurrentRetryMilliseconds = RetryPolicy.Next(CurrentRetryMilliseconds, succeeded: true);
            return PublishStatus(GuardianStatus.Derive(
                enabled: true,
                observation.DisplayConnected,
                observation.HidAvailable,
                observation.HidBlocked,
                observation.DdcHealthy,
                error: null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            CurrentRetryMilliseconds = RetryPolicy.Next(CurrentRetryMilliseconds, succeeded: false);
            return PublishStatus(GuardianStatus.Derive(
                enabled: Settings.ProtectionEnabled,
                displayConnected: false,
                hidAvailable: false,
                hidBlocked: false,
                ddcHealthy: false,
                error: exception.Message));
        }
        finally
        {
            cycleGate.Release();
        }
    }

    public async Task UpdateSettingsAsync(
        Func<GuardianSettings, GuardianSettings> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        await cycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureSettingsLoadedAsync(cancellationToken).ConfigureAwait(false);
            Settings = update(Settings).Normalize();
            await settingsStore.SaveAsync(Settings, cancellationToken).ConfigureAwait(false);
            SettingsChanged?.Invoke(this, Settings);
        }
        finally
        {
            cycleGate.Release();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (loopTask is not null)
        {
            return;
        }

        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        loopCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        loopTask = RunLoopAsync(loopCancellation.Token);
    }

    public async Task StopAsync()
    {
        if (loopTask is null)
        {
            return;
        }

        loopCancellation?.Cancel();
        try
        {
            await loopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            loopCancellation?.Dispose();
            loopCancellation = null;
            loopTask = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        cycleGate.Dispose();
    }

    private async Task EnsureSettingsLoadedAsync(CancellationToken cancellationToken)
    {
        if (settingsLoaded)
        {
            return;
        }

        Settings = (await settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false)).Normalize();
        settingsLoaded = true;
        SettingsChanged?.Invoke(this, Settings);
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await RunCycleAsync(cancellationToken).ConfigureAwait(false);
            await Task.Delay(CurrentRetryMilliseconds, cancellationToken).ConfigureAwait(false);
        }
    }

    private GuardianStatus PublishStatus(GuardianStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
        return status;
    }
}
