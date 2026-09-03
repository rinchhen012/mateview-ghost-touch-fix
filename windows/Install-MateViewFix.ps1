[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('Install', 'Disable', 'Uninstall', 'Status')]
    [string] $Action = 'Install',

    [ValidateRange(0, 100)]
    [int] $DesiredVolume = 60,

    [Nullable[int]] $MonitorIndex,

    [switch] $SkipStart,

    [string] $LocalAppDataRoot = $env:LOCALAPPDATA,

    [string] $RoamingAppDataRoot = $env:APPDATA
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($LocalAppDataRoot)) {
    throw 'LOCALAPPDATA is unavailable.'
}
if ([string]::IsNullOrWhiteSpace($RoamingAppDataRoot)) {
    throw 'APPDATA is unavailable.'
}

$installDirectory = Join-Path $LocalAppDataRoot 'MateViewGhostTouchFix'
$startupDirectory = Join-Path $RoamingAppDataRoot 'Microsoft/Windows/Start Menu/Programs/Startup'
$launcherPath = Join-Path $startupDirectory 'MateViewGhostTouchFix.cmd'
$installedScript = Join-Path $installDirectory 'MateViewFix.ps1'
$pidPath = Join-Path $installDirectory 'watchdog.pid'

function Stop-MateViewWatchdog {
    if (-not (Test-Path -LiteralPath $pidPath)) {
        return
    }

    $watchdogPid = 0
    [void] [int]::TryParse((Get-Content -LiteralPath $pidPath -Raw).Trim(), [ref] $watchdogPid)
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT -and $watchdogPid -gt 0 -and $watchdogPid -ne $PID) {
        $process = Get-CimInstance Win32_Process -Filter "ProcessId = $watchdogPid" -ErrorAction SilentlyContinue
        if ($null -ne $process -and
            -not [string]::IsNullOrWhiteSpace($process.CommandLine) -and
            $process.CommandLine.IndexOf($installedScript, [StringComparison]::OrdinalIgnoreCase) -ge 0 -and
            $process.CommandLine -match '(?i)\bwatch\b') {
            Stop-Process -Id $watchdogPid -Force -ErrorAction SilentlyContinue
        }
    }
    Remove-Item -LiteralPath $pidPath -Force -ErrorAction SilentlyContinue
}

function Disable-MateViewStartup {
    Stop-MateViewWatchdog
    if (Test-Path -LiteralPath $launcherPath) {
        Remove-Item -LiteralPath $launcherPath -Force
    }
}

function Install-MateViewFix {
    Disable-MateViewStartup
    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $startupDirectory -Force | Out-Null

    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MateViewFix.ps1') -Destination $installedScript -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MateViewFix.psm1') -Destination (Join-Path $installDirectory 'MateViewFix.psm1') -Force

    $config = [ordered]@{
        DesiredVolume = $DesiredVolume
        MonitorIndex = if ($null -eq $MonitorIndex) { $null } else { [int] $MonitorIndex }
    }
    $config | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $installDirectory 'config.json') -Encoding UTF8

    $indexArgument = if ($null -eq $MonitorIndex) { '' } else { " -MonitorIndex $([int] $MonitorIndex)" }
    $launcher = "@echo off`r`n@powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$installedScript`" watch -DesiredVolume $DesiredVolume$indexArgument`r`n"
    [System.IO.File]::WriteAllText($launcherPath, $launcher, [System.Text.Encoding]::ASCII)

    if (-not $SkipStart -and [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        $arguments = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$installedScript`" watch -DesiredVolume $DesiredVolume$indexArgument"
        Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -WindowStyle Hidden
    }

    Write-Output "Installed MateView Ghost Touch Fix at $installDirectory"
}

function Uninstall-MateViewFix {
    Disable-MateViewStartup
    if (Test-Path -LiteralPath $installDirectory) {
        Remove-Item -LiteralPath $installDirectory -Recurse -Force
    }
    Write-Output 'Removed MateView Ghost Touch Fix.'
}

switch ($Action) {
    'Install' {
        Install-MateViewFix
    }
    'Disable' {
        Disable-MateViewStartup
        Write-Output 'Disabled MateView Ghost Touch Fix startup.'
    }
    'Uninstall' {
        Uninstall-MateViewFix
    }
    'Status' {
        [pscustomobject]@{
            Installed = Test-Path -LiteralPath $installedScript
            StartupEnabled = Test-Path -LiteralPath $launcherPath
            InstallDirectory = $installDirectory
        }
    }
}
