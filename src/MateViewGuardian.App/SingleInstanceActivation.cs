using System.Runtime.Versioning;

namespace MateViewGuardian.App;

[SupportedOSPlatform("windows")]
public sealed class SingleInstanceActivation : IDisposable
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(2);
    private readonly Mutex mutex;
    private readonly EventWaitHandle activationEvent;
    private readonly EventWaitHandle stopEvent = new(false, EventResetMode.ManualReset);
    private readonly Task listener;
    private readonly Action activate;
    private bool disposed;

    private SingleInstanceActivation(
        Mutex mutex,
        EventWaitHandle activationEvent,
        Action activate)
    {
        this.mutex = mutex;
        this.activationEvent = activationEvent;
        this.activate = activate;
        listener = Task.Factory.StartNew(
            Listen,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    public static SingleInstanceActivation? TryAcquire(string instanceName, Action activate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        ArgumentNullException.ThrowIfNull(activate);

        var mutexName = $"Local\\{instanceName}.Mutex";
        var eventName = $"Local\\{instanceName}.Activate";
        var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            SignalExisting(eventName);
            return null;
        }

        try
        {
            var activationEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                eventName);
            return new SingleInstanceActivation(mutex, activationEvent, activate);
        }
        catch
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        stopEvent.Set();
        listener.GetAwaiter().GetResult();
        stopEvent.Dispose();
        activationEvent.Dispose();
        mutex.ReleaseMutex();
        mutex.Dispose();
    }

    private void Listen()
    {
        var handles = new WaitHandle[] { activationEvent, stopEvent };
        while (WaitHandle.WaitAny(handles) == 0)
        {
            activate();
        }
    }

    private static void SignalExisting(string eventName)
    {
        var deadline = DateTime.UtcNow + SignalTimeout;
        do
        {
            if (EventWaitHandle.TryOpenExisting(eventName, out var activationEvent))
            {
                using (activationEvent)
                {
                    activationEvent.Set();
                }
                return;
            }

            Thread.Sleep(20);
        }
        while (DateTime.UtcNow < deadline);
    }
}
