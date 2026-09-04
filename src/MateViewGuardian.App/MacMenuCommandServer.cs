using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace MateViewGuardian.App;

public sealed class MacMenuCommandServer : IDisposable
{
    private const int MaxCommandLength = 64;
    private Socket? listener;
    private CancellationTokenSource? cancellation;

    public static string SocketPath => $"/tmp/mateview-guardian-{GetEffectiveUserId()}.sock";

    public void Start(Func<string, Task> commandHandler)
    {
        if (!OperatingSystem.IsMacOS() || listener is not null)
        {
            return;
        }

        File.Delete(SocketPath);
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Bind(new UnixDomainSocketEndPoint(SocketPath));
        _ = chmod(SocketPath, 0x180);
        socket.Listen(4);
        listener = socket;
        cancellation = new CancellationTokenSource();
        _ = AcceptLoopAsync(socket, commandHandler, cancellation.Token);
    }

    public void Dispose()
    {
        cancellation?.Cancel();
        listener?.Dispose();
        cancellation?.Dispose();
        listener = null;
        cancellation = null;
        if (OperatingSystem.IsMacOS())
        {
            File.Delete(SocketPath);
        }
    }

    private static async Task AcceptLoopAsync(
        Socket socket,
        Func<string, Task> commandHandler,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var client = await socket.AcceptAsync(cancellationToken).ConfigureAwait(false);
                var buffer = new byte[MaxCommandLength];
                var received = await client.ReceiveAsync(buffer, SocketFlags.None, cancellationToken)
                    .ConfigureAwait(false);
                if (received > 0)
                {
                    var command = Encoding.UTF8.GetString(buffer, 0, received).Trim();
                    await commandHandler(command).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
        }
    }

    private static uint GetEffectiveUserId() => OperatingSystem.IsMacOS() ? geteuid() : 0;

    [DllImport("libSystem.B.dylib")]
    private static extern uint geteuid();

    [DllImport("libSystem.B.dylib")]
    private static extern int chmod(string path, int mode);
}
