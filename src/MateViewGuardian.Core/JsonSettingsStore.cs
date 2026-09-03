using System.Text.Json;

namespace MateViewGuardian.Core;

public sealed class JsonSettingsStore(string path) : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public string Path { get; } = string.IsNullOrWhiteSpace(path)
        ? throw new ArgumentException("A settings path is required.", nameof(path))
        : System.IO.Path.GetFullPath(path);

    public async Task<GuardianSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Path))
        {
            return GuardianSettings.Default;
        }

        try
        {
            await using var stream = File.OpenRead(Path);
            var settings = await JsonSerializer.DeserializeAsync<GuardianSettings>(
                stream,
                JsonOptions,
                cancellationToken);
            return (settings ?? GuardianSettings.Default).Normalize();
        }
        catch (JsonException)
        {
            PreserveInvalidFile();
            return GuardianSettings.Default;
        }
    }

    public async Task SaveAsync(GuardianSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{Path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings.Normalize(),
                    JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void PreserveInvalidFile()
    {
        var invalidPath = $"{Path}.invalid";
        if (File.Exists(invalidPath))
        {
            invalidPath = $"{invalidPath}.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        }

        File.Move(Path, invalidPath);
    }
}
