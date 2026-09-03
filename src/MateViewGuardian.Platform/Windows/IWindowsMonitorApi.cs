namespace MateViewGuardian.Platform.Windows;

public interface IWindowsPhysicalMonitor : IDisposable
{
    string Description { get; }

    string DeviceString { get; }

    string DeviceId { get; }

    string Identity { get; }

    uint Read(byte code);

    void Write(byte code, uint value);
}

public interface IWindowsMonitorApi
{
    IReadOnlyList<IWindowsPhysicalMonitor> Enumerate();
}
