using MateViewGuardian.App;
using MateViewGuardian.Core;
using MateViewGuardian.Platform.Startup;
using Xunit;

namespace MateViewGuardian.App.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task DisablingProtectionPersistsAndRestoresHidImmediately()
    {
        var platform = new RecordingPlatform();
        var store = new MemorySettingsStore(GuardianSettings.Default);
        var viewModel = new MainWindowViewModel(new ProtectionCoordinator(platform, store));
        await viewModel.InitializeAsync();

        await viewModel.SetProtectionEnabledAsync(false);

        Assert.False(viewModel.ProtectionEnabled);
        Assert.False(store.Value.ProtectionEnabled);
        Assert.Equal(1, platform.ClearCount);
        Assert.Equal("Protection off", viewModel.OverallStatusText);
    }

    [Fact]
    public async Task VolumePresetPersistsAndCorrectsMonitorImmediately()
    {
        var platform = new RecordingPlatform
        {
            Observation = new PlatformObservation(true, true, true, true, 30, 2, true, "ZQE-CAA", null),
        };
        var store = new MemorySettingsStore(GuardianSettings.Default);
        var viewModel = new MainWindowViewModel(new ProtectionCoordinator(platform, store));
        await viewModel.InitializeAsync();

        await viewModel.SetDesiredVolumeAsync(40);

        Assert.Equal(40, viewModel.DesiredVolume);
        Assert.Equal(40, store.Value.DesiredVolume);
        Assert.Contains(new DdcCorrection(0x62, 40), platform.Writes);
    }

    [Fact]
    public async Task VolumePreviewChangesLiveWithoutPersistingOrWritingDdc()
    {
        var platform = new RecordingPlatform();
        var store = new MemorySettingsStore(GuardianSettings.Default);
        var viewModel = new MainWindowViewModel(new ProtectionCoordinator(platform, store));
        await viewModel.InitializeAsync();
        platform.Writes.Clear();

        viewModel.SetVolumePreview(47);

        Assert.Equal(47, viewModel.LiveDesiredVolume);
        Assert.Equal(30, viewModel.DesiredVolume);
        Assert.Equal(30, store.Value.DesiredVolume);
        Assert.Empty(platform.Writes);
    }

    [Fact]
    public async Task StartAtLoginSettingIsPersisted()
    {
        var store = new MemorySettingsStore(GuardianSettings.Default);
        var startup = new RecordingStartupManager();
        var viewModel = new MainWindowViewModel(
            new ProtectionCoordinator(new RecordingPlatform(), store), startup);
        await viewModel.InitializeAsync();

        await viewModel.SetStartAtLoginAsync(false);

        Assert.False(viewModel.StartAtLogin);
        Assert.False(store.Value.StartAtLogin);
        Assert.Equal([true, false], startup.Values);
    }

    [Fact]
    public async Task RestoreInitializationNeverAppliesProtectionBeforeClearingIt()
    {
        var platform = new RecordingPlatform();
        var store = new MemorySettingsStore(GuardianSettings.Default);
        var viewModel = new MainWindowViewModel(new ProtectionCoordinator(platform, store));

        await viewModel.InitializeAsync(applyProtection: false);
        await viewModel.SetProtectionEnabledAsync(false);

        Assert.Equal(0, platform.ApplyCount);
        Assert.Equal(1, platform.ClearCount);
    }

    [Fact]
    public async Task DiagnosticsContainUsefulStateWithoutUserPaths()
    {
        var platform = new RecordingPlatform
        {
            Observation = new PlatformObservation(
                true, true, true, true, 30, 2, true, "ZQE-CAA", "/Users/secret/private"),
        };
        var viewModel = new MainWindowViewModel(
            new ProtectionCoordinator(platform, new MemorySettingsStore(GuardianSettings.Default)));
        await viewModel.InitializeAsync();
        await viewModel.ApplyNowAsync();

        var diagnostics = viewModel.CreateDiagnostics();

        Assert.Contains("Target volume: 30", diagnostics);
        Assert.Contains("ZQE-CAA", diagnostics);
        Assert.DoesNotContain("/Users/secret", diagnostics);
    }

    private sealed class MemorySettingsStore(GuardianSettings value) : ISettingsStore
    {
        public GuardianSettings Value { get; private set; } = value;
        public Task<GuardianSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Value);
        public Task SaveAsync(GuardianSettings settings, CancellationToken cancellationToken = default)
        {
            Value = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPlatform : IPlatformProtection
    {
        public PlatformObservation Observation { get; set; } =
            new(true, true, true, true, 30, 2, true, "ZQE-CAA", null);
        public int ClearCount { get; private set; }
        public int ApplyCount { get; private set; }
        public List<DdcCorrection> Writes { get; } = [];

        public Task<IReadOnlyList<string>> ApplyHidBlockAsync(
            IReadOnlyList<string> recordedIds,
            CancellationToken cancellationToken)
        {
            ApplyCount++;
            return Task.FromResult(recordedIds);
        }

        public Task ClearHidBlockAsync(IReadOnlyList<string> recordedIds, CancellationToken cancellationToken)
        {
            ClearCount++;
            return Task.CompletedTask;
        }

        public Task<PlatformObservation> ObserveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Observation);

        public Task WriteDdcAsync(DdcCorrection correction, CancellationToken cancellationToken)
        {
            Writes.Add(correction);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingStartupManager : IStartupManager
    {
        public bool IsEnabled { get; private set; } = true;
        public List<bool> Values { get; } = [];

        public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            IsEnabled = enabled;
            Values.Add(enabled);
            return Task.CompletedTask;
        }
    }
}
