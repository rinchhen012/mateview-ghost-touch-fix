Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

if (-not ('MateViewFixNative.MonitorApi' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MateViewFixNative
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PhysicalMonitor
    {
        public IntPtr Handle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfoEx
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
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayDevice
    {
        public int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    internal static class NativeMethods
    {
        internal delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumDisplayDevices(string device, uint number, ref DisplayDevice displayDevice, uint flags);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

        [DllImport("dxva2.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitor[] physicalMonitors);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyPhysicalMonitor(IntPtr monitor);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte code, out uint type, out uint currentValue, out uint maximumValue);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetVCPFeature(IntPtr monitor, byte code, uint value);
    }

    public sealed class MonitorHandle : IDisposable
    {
        private IntPtr handle;

        internal MonitorHandle(IntPtr handle, string description, string displayName, string deviceString, string deviceId, int index)
        {
            this.handle = handle;
            Description = description ?? String.Empty;
            DisplayName = displayName ?? String.Empty;
            DeviceString = deviceString ?? String.Empty;
            DeviceId = deviceId ?? String.Empty;
            Index = index;
        }

        public int Index { get; private set; }
        public string Description { get; private set; }
        public string DisplayName { get; private set; }
        public string DeviceString { get; private set; }
        public string DeviceId { get; private set; }

        public uint Read(byte code)
        {
            ThrowIfDisposed();
            uint type;
            uint current;
            uint maximum;
            if (!NativeMethods.GetVCPFeatureAndVCPFeatureReply(handle, code, out type, out current, out maximum))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read VCP feature 0x" + code.ToString("X2") + ".");
            return current;
        }

        public void Write(byte code, uint value)
        {
            ThrowIfDisposed();
            if (!NativeMethods.SetVCPFeature(handle, code, value))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not write VCP feature 0x" + code.ToString("X2") + ".");
        }

        private void ThrowIfDisposed()
        {
            if (handle == IntPtr.Zero)
                throw new ObjectDisposedException("MonitorHandle");
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero)
            {
                NativeMethods.DestroyPhysicalMonitor(handle);
                handle = IntPtr.Zero;
            }
        }
    }

    public static class MonitorApi
    {
        public static MonitorHandle[] Enumerate()
        {
            List<MonitorHandle> result = new List<MonitorHandle>();
            NativeMethods.MonitorEnumProc callback = delegate(IntPtr logicalMonitor, IntPtr hdc, IntPtr rect, IntPtr data)
            {
                AddPhysicalMonitors(logicalMonitor, result);
                return true;
            };

            if (!NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate display monitors.");

            return result.ToArray();
        }

        private static void AddPhysicalMonitors(IntPtr logicalMonitor, List<MonitorHandle> result)
        {
            uint count;
            if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(logicalMonitor, out count))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not count physical monitors.");
            if (count == 0)
                return;

            PhysicalMonitor[] physical = new PhysicalMonitor[count];
            if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(logicalMonitor, count, physical))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate physical monitors.");

            string displayName = String.Empty;
            string deviceString = String.Empty;
            string deviceId = String.Empty;
            MonitorInfoEx info = new MonitorInfoEx();
            info.Size = (uint)Marshal.SizeOf(typeof(MonitorInfoEx));
            if (NativeMethods.GetMonitorInfo(logicalMonitor, ref info))
            {
                displayName = info.DeviceName;
                DisplayDevice display = new DisplayDevice();
                display.Size = Marshal.SizeOf(typeof(DisplayDevice));
                if (NativeMethods.EnumDisplayDevices(displayName, 0, ref display, 0))
                {
                    deviceString = display.DeviceString;
                    deviceId = display.DeviceId;
                }
            }

            for (int i = 0; i < physical.Length; i++)
            {
                result.Add(new MonitorHandle(
                    physical[i].Handle,
                    physical[i].Description,
                    displayName,
                    deviceString,
                    deviceId,
                    result.Count));
            }
        }
    }
}
'@
}

function Test-MateViewIdentity {
    param([Parameter(Mandatory = $true)] $Monitor)

    $identityParts = @($Monitor.Description)
    if ($Monitor.PSObject.Properties.Name -contains 'DeviceString') {
        $identityParts += $Monitor.DeviceString
    }
    if ($Monitor.PSObject.Properties.Name -contains 'DeviceId') {
        $identityParts += $Monitor.DeviceId
    }
    $identity = ($identityParts -join ' ')
    return $identity -match '(?i)(^|[^A-Z0-9])ZQE-CAA([^A-Z0-9]|$)'
}

function Get-MateViewMonitors {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        throw 'Monitor enumeration is available only on Windows.'
    }
    return @([MateViewFixNative.MonitorApi]::Enumerate())
}

function Select-MateViewMonitor {
    param(
        [Parameter(Mandatory = $true)] [object[]] $Monitors,
        [int] $Index
    )

    $matching = @($Monitors | Where-Object { Test-MateViewIdentity $_ })
    if ($PSBoundParameters.ContainsKey('Index')) {
        $selected = @($matching | Where-Object { $_.Index -eq $Index })
        if ($selected.Count -ne 1) {
            throw "Monitor index $Index is not an identified ZQE-CAA. Run 'list' again."
        }
        return $selected[0]
    }

    if ($matching.Count -eq 0) {
        throw "No ZQE-CAA monitor was found. Run 'list' to inspect detected monitors."
    }
    if ($matching.Count -gt 1) {
        throw "Found multiple ZQE-CAA monitors. Select one with --index after running 'list'."
    }
    return $matching[0]
}

function Get-MateViewCorrection {
    param(
        [Parameter(Mandatory = $true)] [int] $CurrentVolume,
        [Parameter(Mandatory = $true)] [int] $CurrentMute,
        [Parameter(Mandatory = $true)] [int] $DesiredVolume,
        [switch] $Unmuted
    )

    if ($DesiredVolume -lt 0 -or $DesiredVolume -gt 100) {
        throw 'Volume must be between 0 and 100.'
    }

    if ($CurrentVolume -ne $DesiredVolume) {
        [pscustomobject]@{ Code = 0x62; Value = $DesiredVolume; Name = 'volume' }
    }
    if ($Unmuted -and $CurrentMute -ne 2) {
        [pscustomobject]@{ Code = 0x8D; Value = 2; Name = 'mute' }
    }
}

function Get-MateViewRetryDelay {
    param(
        [Parameter(Mandatory = $true)] [int] $CurrentMilliseconds,
        [Parameter(Mandatory = $true)] [bool] $Succeeded
    )

    if ($Succeeded) {
        return 500
    }
    return [Math]::Min(10000, [Math]::Max(500, $CurrentMilliseconds * 2))
}

function Invoke-MateViewCorrection {
    param(
        [Parameter(Mandatory = $true)] $Monitor,
        [Parameter(Mandatory = $true)] [AllowEmptyCollection()] [object[]] $Corrections
    )

    foreach ($correction in $Corrections) {
        $Monitor.Write([byte] $correction.Code, [uint32] $correction.Value)
    }
}

function Invoke-MateViewOnce {
    param(
        [int] $DesiredVolume = 60,
        [Nullable[int]] $Index
    )

    if ($DesiredVolume -lt 0 -or $DesiredVolume -gt 100) {
        throw 'Volume must be between 0 and 100.'
    }

    $monitors = @(Get-MateViewMonitors)
    try {
        if ($null -eq $Index) {
            $monitor = Select-MateViewMonitor -Monitors $monitors
        }
        else {
            $monitor = Select-MateViewMonitor -Monitors $monitors -Index ([int] $Index)
        }
        # These reads and every possible write are deliberately serialized.
        $volume = [int] $monitor.Read([byte] 0x62)
        $mute = [int] $monitor.Read([byte] 0x8D)
        $corrections = @(Get-MateViewCorrection -CurrentVolume $volume -CurrentMute $mute -DesiredVolume $DesiredVolume -Unmuted)
        Invoke-MateViewCorrection -Monitor $monitor -Corrections $corrections
        return [pscustomobject]@{
            Index = $monitor.Index
            Description = $monitor.Description
            PreviousVolume = $volume
            PreviousMute = $mute
            DesiredVolume = $DesiredVolume
            Writes = $corrections.Count
        }
    }
    finally {
        foreach ($item in $monitors) {
            if ($null -ne $item -and $item -is [System.IDisposable]) {
                $item.Dispose()
            }
        }
    }
}

function Watch-MateView {
    param(
        [int] $DesiredVolume = 60,
        [Nullable[int]] $Index
    )

    $retryMilliseconds = 500
    while ($true) {
        try {
            Invoke-MateViewOnce -DesiredVolume $DesiredVolume -Index $Index | Out-Null
            $retryMilliseconds = Get-MateViewRetryDelay -CurrentMilliseconds $retryMilliseconds -Succeeded $true
            Start-Sleep -Milliseconds 500
        }
        catch {
            Write-Warning $_.Exception.Message
            $retryMilliseconds = Get-MateViewRetryDelay -CurrentMilliseconds $retryMilliseconds -Succeeded $false
            Start-Sleep -Milliseconds $retryMilliseconds
        }
    }
}

Export-ModuleMember -Function @(
    'Get-MateViewMonitors',
    'Select-MateViewMonitor',
    'Get-MateViewCorrection',
    'Get-MateViewRetryDelay',
    'Invoke-MateViewCorrection',
    'Invoke-MateViewOnce',
    'Watch-MateView'
)
