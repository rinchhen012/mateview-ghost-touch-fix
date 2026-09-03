[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('list', 'once', 'watch', 'status', 'set-volume')]
    [string] $Command = 'status',

    [Alias('volume')]
    [ValidateRange(0, 100)]
    [int] $DesiredVolume = 60,

    [Alias('index')]
    [Nullable[int]] $MonitorIndex
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'MateViewFix.psm1') -Force

switch ($Command) {
    'list' {
        $monitors = @(Get-MateViewMonitors)
        try {
            $monitors |
                Select-Object Index, Description, DeviceString, DisplayName, DeviceId |
                Format-Table -AutoSize
        }
        finally {
            foreach ($monitor in $monitors) {
                if ($monitor -is [System.IDisposable]) {
                    $monitor.Dispose()
                }
            }
        }
    }
    'once' {
        Invoke-MateViewOnce -DesiredVolume $DesiredVolume -Index $MonitorIndex
    }
    'watch' {
        Watch-MateView -DesiredVolume $DesiredVolume -Index $MonitorIndex
    }
    'status' {
        $result = Invoke-MateViewOnce -DesiredVolume $DesiredVolume -Index $MonitorIndex
        if ($result.Writes -eq 0) {
            "active: monitor $($result.Index), volume $DesiredVolume, unmuted"
        }
        else {
            "corrected: monitor $($result.Index), $($result.Writes) setting(s)"
        }
    }
    'set-volume' {
        Invoke-MateViewOnce -DesiredVolume $DesiredVolume -Index $MonitorIndex
    }
}
