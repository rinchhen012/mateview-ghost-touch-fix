using MateViewGuardian.Core;
using Xunit;

namespace MateViewGuardian.Core.Tests;

public sealed class ProtectionCoordinatorTests
{
    [Fact]
    public async Task EnabledCycleAppliesHidAndWritesVolumeBeforeUnmute()
    {
        var platform = new RecordingPlatform
        {
            Observation = new PlatformObservation(true, true, true, true, 0, 1, true, "ZQE-CAA", null),
        };
        var coordinator = new ProtectionCoordinator(platform, new MemorySettingsStore(GuardianSettings.Default));

        var status = await coordinator.RunCycleAsync();

        Assert.Equal(GuardianState.Protected, status.State);
        Assert.Equal(["hid:block", "observe", "write:62:30", "write:8D:2"], platform.Calls);
        Assert.Equal(500, coordinator.CurrentRetryMilliseconds);
    }

    [Fact]
    public async Task DisabledCycleClearsHidAndDoesNotTouchDdc()
    {
        var settings = GuardianSettings.Default with { ProtectionEnabled = false };
        var platform = new RecordingPlatform();
        var coordinator = new ProtectionCoordinator(platform, new MemorySettingsStore(settings));

        var status = await coordinator.RunCycleAsync();

        Assert.Equal(GuardianState.Disabled, status.State);
        Assert.Equal(["hid:clear"], platform.Calls);
    }

    [Fact]
    public async Task FailedCycleReturnsErrorAndIncreasesRetry()
    {
        var platform = new RecordingPlatform { ObserveException = new IOException("DDC unavailable") };
        var coordinator = new ProtectionCoordinator(platform, new MemorySettingsStore(GuardianSettings.Default));

        var status = await coordinator.RunCycleAsync();

        Assert.Equal(GuardianState.Error, status.State);
        Assert.Contains("DDC unavailable", status.Error);
        Assert.Equal(1000, coordinator.CurrentRetryMilliseconds);
    }

    [Fact]
    public async Task SuccessfulCycleResetsRetryAfterFailure()
    {
        var platform = new RecordingPlatform { ObserveException = new IOException("first") };
        var coordinator = new ProtectionCoordinator(platform, new MemorySettingsStore(GuardianSettings.Default));
        await coordinator.RunCycleAsync();
        platform.ObserveException = null;

        await coordinator.RunCycleAsync();

        Assert.Equal(500, coordinator.CurrentRetryMilliseconds);
    }

    [Fact]
    public async Task ConcurrentCycleRequestsNeverOverlapPlatformAccess()
    {
        var platform = new RecordingPlatform { PauseObservation = true };
        var coordinator = new ProtectionCoordinator(platform, new MemorySettingsStore(GuardianSettings.Default));

        var first = coordinator.RunCycleAsync();
        await platform.ObservationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = coordinator.RunCycleAsync();
        await Task.Delay(50);

        Assert.Equal(1, platform.MaximumConcurrentObservations);
        platform.ReleaseObservation.TrySetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, platform.MaximumConcurrentObservations);
    }

    [Fact]
    public async Task SettingsUpdateIsNormalizedPersistedAndPublished()
    {
        var store = new MemorySettingsStore(GuardianSettings.Default);
        var coordinator = new ProtectionCoordinator(new RecordingPlatform(), store);
        GuardianSettings? published = null;
        coordinator.SettingsChanged += (_, settings) => published = settings;

        await coordinator.UpdateSettingsAsync(settings => settings with { DesiredVolume = 101 });

        Assert.Equal(30, coordinator.Settings.DesiredVolume);
        Assert.Equal(30, store.Value.DesiredVolume);
        Assert.Equal(30, published?.DesiredVolume);
    }

    private sealed class MemorySettingsStore(GuardianSettings value) : ISettingsStore
    {
        public GuardianSettings Value { get; private set; } = value;

        public Task<GuardianSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);

        public Task SaveAsync(GuardianSettings settings, CancellationToken cancellationToken = default)
        {
            Value = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPlatform : IPlatformProtection
    {
        private int concurrentObservations;

        public List<string> Calls { get; } = [];

        public PlatformObservation Observation { get; set; } =
            new(true, true, true, true, 30, 2, true, "ZQE-CAA", null);

        public Exception? ObserveException { get; set; }

        public bool PauseObservation { get; set; }

        public TaskCompletionSource ObservationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseObservation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaximumConcurrentObservations { get; private set; }

        public Task ApplyHidBlockAsync(CancellationToken cancellationToken)
        {
            Calls.Add("hid:block");
            return Task.CompletedTask;
        }

        public Task ClearHidBlockAsync(IReadOnlyList<string> recordedIds, CancellationToken cancellationToken)
        {
            Calls.Add("hid:clear");
            return Task.CompletedTask;
        }

        public async Task<PlatformObservation> ObserveAsync(CancellationToken cancellationToken)
        {
            Calls.Add("observe");
            var concurrent = Interlocked.Increment(ref concurrentObservations);
            MaximumConcurrentObservations = Math.Max(MaximumConcurrentObservations, concurrent);
            try
            {
                ObservationEntered.TrySetResult();
                if (PauseObservation)
                {
                    await ReleaseObservation.Task.WaitAsync(cancellationToken);
                }

                if (ObserveException is not null)
                {
                    throw ObserveException;
                }

                return Observation;
            }
            finally
            {
                Interlocked.Decrement(ref concurrentObservations);
            }
        }

        public Task WriteDdcAsync(DdcCorrection correction, CancellationToken cancellationToken)
        {
            Calls.Add($"write:{correction.Code:X2}:{correction.Value}");
            return Task.CompletedTask;
        }
    }
}
