using System.Text.Json;
using MateViewGuardian.Core;
using Xunit;

namespace MateViewGuardian.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"guardian-settings-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAndLoadRoundTripsNormalizedSettings()
    {
        var path = Path.Combine(directory, "settings.json");
        var store = new JsonSettingsStore(path);
        var input = GuardianSettings.Default with
        {
            DesiredVolume = 45,
            DisabledHidInstanceIds = [" HID\\VID_12D1&PID_10B6\\ONE ", "hid\\vid_12d1&pid_10b6\\one"],
        };

        await store.SaveAsync(input);
        var loaded = await store.LoadAsync();

        Assert.Equal(45, loaded.DesiredVolume);
        Assert.Equal(["HID\\VID_12D1&PID_10B6\\ONE"], loaded.DisabledHidInstanceIds);
        Assert.DoesNotContain(".tmp", Directory.GetFiles(directory).Select(Path.GetFileName));
    }

    [Fact]
    public async Task CorruptSettingsArePreservedAndDefaultsAreReturned()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(path, "{broken json");
        var store = new JsonSettingsStore(path);

        var loaded = await store.LoadAsync();

        Assert.Equal(GuardianSettings.Default, loaded);
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(directory, "settings.json.invalid*"));
    }

    [Fact]
    public async Task UnsafePersistedVolumeNormalizesToThirty()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { DesiredVolume = 500 }));
        var store = new JsonSettingsStore(path);

        var loaded = await store.LoadAsync();

        Assert.Equal(30, loaded.DesiredVolume);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
