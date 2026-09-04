using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MateViewGuardian.Platform.Windows;

public sealed class WindowsMonitorApi : IWindowsMonitorApi
{
    public IReadOnlyList<IWindowsPhysicalMonitor> Enumerate()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Monitor DDC is available only on Windows.");
        }

        var monitors = new List<IWindowsPhysicalMonitor>();
        NativeMethods.MonitorEnumProc callback = (logicalMonitor, _, _, _) =>
        {
            AddPhysicalMonitors(logicalMonitor, monitors);
            return true;
        };
        try
        {
            if (!NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
            {
                throw LastError("enumerate display monitors");
            }

            GC.KeepAlive(callback);
            return monitors;
        }
        catch
        {
            foreach (var monitor in monitors)
            {
                monitor.Dispose();
            }
            throw;
        }
    }

    private static void AddPhysicalMonitors(
        IntPtr logicalMonitor,
        ICollection<IWindowsPhysicalMonitor> result)
    {
        if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(logicalMonitor, out var count))
        {
            throw LastError("count physical monitors");
        }
        if (count == 0)
        {
            return;
        }

        var physical = new PhysicalMonitor[count];
        if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(logicalMonitor, count, physical))
        {
            throw LastError("enumerate physical monitors");
        }

        var (displayName, deviceString, deviceId) = GetDisplayIdentity(logicalMonitor);
        foreach (var item in physical)
        {
            result.Add(new WindowsPhysicalMonitor(
                item.Handle,
                item.Description ?? string.Empty,
                displayName,
                deviceString,
                deviceId));
        }
    }

    private static (string DisplayName, string DeviceString, string DeviceId) GetDisplayIdentity(
        IntPtr logicalMonitor)
    {
        var info = new MonitorInfoEx { Size = (uint)Marshal.SizeOf<MonitorInfoEx>() };
        if (!NativeMethods.GetMonitorInfo(logicalMonitor, ref info))
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        var device = new DisplayDevice { Size = (uint)Marshal.SizeOf<DisplayDevice>() };
        if (!NativeMethods.EnumDisplayDevices(info.DeviceName, 0, ref device, 0))
        {
            return (info.DeviceName ?? string.Empty, string.Empty, string.Empty);
        }

        return (
            info.DeviceName ?? string.Empty,
            device.DeviceString ?? string.Empty,
            device.DeviceId ?? string.Empty);
    }

    private static Win32Exception LastError(string action) =>
        new(Marshal.GetLastWin32Error(), $"Could not {action}.");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public IntPtr Handle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string? Description;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public uint Size;
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int WorkLeft;
        public int WorkTop;
        public int WorkRight;
        public int WorkBottom;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string? DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public uint Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string? DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string? DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string? DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string? DeviceKey;
    }

    private static class NativeMethods
    {
        internal delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumDisplayMonitors(
            IntPtr hdc,
            IntPtr clip,
            MonitorEnumProc callback,
            IntPtr data);

        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

        [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumDisplayDevices(
            string? device,
            uint number,
            ref DisplayDevice displayDevice,
            uint flags);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(
            IntPtr monitor,
            out uint count);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetPhysicalMonitorsFromHMONITOR(
            IntPtr monitor,
            uint count,
            [Out] PhysicalMonitor[] physicalMonitors);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyPhysicalMonitor(IntPtr monitor);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetVCPFeatureAndVCPFeatureReply(
            IntPtr monitor,
            byte code,
            out uint type,
            out uint currentValue,
            out uint maximumValue);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetVCPFeature(IntPtr monitor, byte code, uint value);
    }

    private sealed class WindowsPhysicalMonitor(
        IntPtr handle,
        string description,
        string displayName,
        string deviceString,
        string deviceId) : IWindowsPhysicalMonitor
    {
        private IntPtr handle = handle;

        public string Description { get; } = description;
        public string DeviceString { get; } = deviceString;
        public string DeviceId { get; } = deviceId;
        public string Identity { get; } = string.IsNullOrWhiteSpace(deviceId)
            ? $"{displayName}|{description}"
            : deviceId;

        public uint Read(byte code)
        {
            ThrowIfDisposed();
            if (!NativeMethods.GetVCPFeatureAndVCPFeatureReply(
                    handle, code, out _, out var current, out _))
            {
                throw LastError($"read VCP feature 0x{code:X2}");
            }
            return current;
        }

        public void Write(byte code, uint value)
        {
            ThrowIfDisposed();
            if (!NativeMethods.SetVCPFeature(handle, code, value))
            {
                throw LastError($"write VCP feature 0x{code:X2}");
            }
        }

        public void Dispose()
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.DestroyPhysicalMonitor(handle);
            handle = IntPtr.Zero;
        }

        private void ThrowIfDisposed()
        {
            if (handle == IntPtr.Zero)
            {
                throw new ObjectDisposedException(nameof(WindowsPhysicalMonitor));
            }
        }
    }
}
