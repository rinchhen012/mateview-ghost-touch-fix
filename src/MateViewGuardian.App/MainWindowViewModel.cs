using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using MateViewGuardian.Core;
using MateViewGuardian.Platform.Startup;

namespace MateViewGuardian.App;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ProtectionCoordinator coordinator;
    private readonly IStartupManager? startupManager;
    private readonly SynchronizationContext? synchronizationContext;
    private bool isBusy;
    private int liveDesiredVolume = GuardianSettings.DefaultVolume;

    public MainWindowViewModel(
        ProtectionCoordinator coordinator,
        IStartupManager? startupManager = null)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        this.startupManager = startupManager;
        synchronizationContext = SynchronizationContext.Current;
        coordinator.SettingsChanged += (_, settings) => Dispatch(() => PublishSettings(settings));
        coordinator.StatusChanged += (_, status) => Dispatch(() => PublishStatus(status));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool ProtectionEnabled { get; private set; } = true;

    public int DesiredVolume { get; private set; } = GuardianSettings.DefaultVolume;

    public int LiveDesiredVolume
    {
        get => liveDesiredVolume;
        private set => SetField(ref liveDesiredVolume, value);
    }

    public bool StartAtLogin { get; private set; } = true;

    public bool IsBusy
    {
        get => isBusy;
        private set => SetField(ref isBusy, value);
    }

    public string OverallStatusText { get; private set; } = "Starting…";

    public string DisplayStatusText { get; private set; } = "Checking MateView…";

    public string HidStatusText { get; private set; } = "Checking touch strip…";

    public string DdcStatusText { get; private set; } = "Checking speaker…";

    public string? ErrorText { get; private set; }

    public GuardianState State { get; private set; } = GuardianState.Disconnected;

    public async Task InitializeAsync(
        bool applyProtection = true,
        CancellationToken cancellationToken = default)
    {
        await coordinator.InitializeAsync(cancellationToken).ConfigureAwait(false);
        PublishSettings(coordinator.Settings);
        if (startupManager is not null)
        {
            await startupManager.SetEnabledAsync(coordinator.Settings.StartAtLogin, cancellationToken)
                .ConfigureAwait(false);
        }
        if (applyProtection)
        {
            await ApplyNowAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        coordinator.StartAsync(cancellationToken);

    public Task StopAsync() => coordinator.StopAsync();

    public async Task SetProtectionEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async () =>
        {
            await coordinator.UpdateSettingsAsync(
                settings => settings with { ProtectionEnabled = enabled },
                cancellationToken).ConfigureAwait(false);
            await coordinator.RunCycleAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task SetDesiredVolumeAsync(int volume, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async () =>
        {
            await coordinator.UpdateSettingsAsync(
                settings => settings with { DesiredVolume = volume },
                cancellationToken).ConfigureAwait(false);
            await coordinator.RunCycleAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public void SetVolumePreview(int volume) => LiveDesiredVolume = Math.Clamp(volume, 0, 100);

    public Task SetStartAtLoginAsync(bool enabled, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            if (startupManager is not null)
            {
                await startupManager.SetEnabledAsync(enabled, cancellationToken).ConfigureAwait(false);
            }
            await coordinator.UpdateSettingsAsync(
                settings => settings with { StartAtLogin = enabled },
                cancellationToken).ConfigureAwait(false);
        });

    public Task ApplyNowAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => coordinator.RunUserRequestedCycleAsync(cancellationToken));

    public string CreateDiagnostics()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "development";
        return string.Join(Environment.NewLine,
            $"MateView Guardian: {version}",
            $"OS: {Environment.OSVersion.Platform}",
            "Supported display: ZQE-CAA",
            $"State: {State}",
            $"Display: {DisplayStatusText}",
            $"Touch strip: {HidStatusText}",
            $"Speaker DDC: {DdcStatusText}",
            $"Target volume: {DesiredVolume}");
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        IsBusy = true;
        ErrorText = null;
        OnPropertyChanged(nameof(ErrorText));
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorText = Sanitize(exception.Message);
            OnPropertyChanged(nameof(ErrorText));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void PublishSettings(GuardianSettings settings)
    {
        ProtectionEnabled = settings.ProtectionEnabled;
        DesiredVolume = settings.DesiredVolume;
        LiveDesiredVolume = settings.DesiredVolume;
        StartAtLogin = settings.StartAtLogin;
        OnPropertyChanged(nameof(ProtectionEnabled));
        OnPropertyChanged(nameof(DesiredVolume));
        OnPropertyChanged(nameof(StartAtLogin));
    }

    private void PublishStatus(GuardianStatus status)
    {
        State = status.State;
        OverallStatusText = status.State switch
        {
            GuardianState.Disabled => "Protection off",
            GuardianState.Disconnected => "MateView disconnected",
            GuardianState.PartiallyProtected => "Partially protected",
            GuardianState.Protected => "Protected",
            GuardianState.Error => "Needs attention",
            _ => "Unknown",
        };
        DisplayStatusText = status.DisplayConnected ? "ZQE-CAA connected" : "ZQE-CAA not detected";
        HidStatusText = status.HidBlocked
            ? "Touch strip blocked"
            : status.HidAvailable ? "Touch strip not blocked" : "USB data not detected";
        DdcStatusText = status.DdcHealthy ? "Volume watchdog active" : "Volume watchdog unavailable";
        ErrorText = string.IsNullOrWhiteSpace(status.Error) ? null : Sanitize(status.Error);
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(OverallStatusText));
        OnPropertyChanged(nameof(DisplayStatusText));
        OnPropertyChanged(nameof(HidStatusText));
        OnPropertyChanged(nameof(DdcStatusText));
        OnPropertyChanged(nameof(ErrorText));
    }

    private void Dispatch(Action action)
    {
        if (synchronizationContext is null || SynchronizationContext.Current == synchronizationContext)
        {
            action();
            return;
        }

        synchronizationContext.Post(_ => action(), null);
    }

    private static string Sanitize(string message)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(home)
            ? message
            : message.Replace(home, "<user>", StringComparison.OrdinalIgnoreCase);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
