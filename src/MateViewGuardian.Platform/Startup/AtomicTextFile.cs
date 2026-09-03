using System.Text;

namespace MateViewGuardian.Platform.Startup;

internal static class AtomicTextFile
{
    public static async Task WriteAsync(
        string path,
        string content,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path) ??
            throw new ArgumentException("A parent directory is required.", nameof(path));
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, content, encoding, cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
