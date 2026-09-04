[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Detect', 'Disable', 'Enable')]
    [string] $Action,

    [string[]] $InstanceId = @()
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$mateViewPrefix = 'HID\VID_12D1&PID_10B6'
$disabledProblemCode = 22

function Test-WindowsPlatform {
    return [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
}

function Test-MateViewHidIdentity {
    param([AllowNull()][string] $Value)

    if ([string]::IsNullOrWhiteSpace($Value) -or
        -not $Value.StartsWith($mateViewPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    return $Value.Length -eq $mateViewPrefix.Length -or
        $Value[$mateViewPrefix.Length] -eq '&' -or
        $Value[$mateViewPrefix.Length] -eq '\'
}

function Get-MateViewHidDevices {
    if (-not (Test-WindowsPlatform)) {
        throw 'MateView HID detection is available only on Windows.'
    }

    return @(
        Get-PnpDevice -Class HIDClass -ErrorAction Stop |
            Where-Object { Test-MateViewHidIdentity $_.InstanceId } |
            ForEach-Object {
                $problem = Get-PnpDeviceProperty -InstanceId $_.InstanceId `
                    -KeyName 'DEVPKEY_Device_ProblemCode' -ErrorAction Stop
                [pscustomobject]@{
                    instanceId = $_.InstanceId
                    status = if ([int]$problem.Data -eq $disabledProblemCode) { 'Disabled' } else { 'Enabled' }
                }
            } |
            Sort-Object -Property instanceId -Unique
    )
}

function Assert-Administrator {
    if (-not (Test-WindowsPlatform)) {
        throw 'MateView HID changes are available only on Windows.'
    }

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Administrator approval is required to change the MateView HID device.'
    }
}

if ($Action -eq 'Detect') {
    ConvertTo-Json -InputObject @(Get-MateViewHidDevices) -Compress
    return
}

$validatedIds = [Collections.Generic.List[string]]::new()
foreach ($id in $InstanceId) {
    if (-not (Test-MateViewHidIdentity $id)) {
        throw "Safety check failed: refusing non-MateView HID instance ID '$id'."
    }
    if (-not $validatedIds.Contains($id)) {
        $validatedIds.Add($id)
    }
}
if ($validatedIds.Count -eq 0) {
    return
}

Assert-Administrator
$pnputil = Join-Path $env:SystemRoot 'System32\pnputil.exe'
$verb = if ($Action -eq 'Disable') { '/disable-device' } else { '/enable-device' }
$disabledByThisRun = [Collections.Generic.List[string]]::new()
try {
    foreach ($id in $validatedIds) {
        & $pnputil $verb $id
        if ($LASTEXITCODE -ne 0) {
            throw "pnputil failed to $($Action.ToLowerInvariant()) '$id' with exit code $LASTEXITCODE."
        }
        if ($Action -eq 'Disable') {
            $disabledByThisRun.Add($id)
        }
    }
}
catch {
    if ($Action -eq 'Disable') {
        foreach ($id in $disabledByThisRun) {
            & $pnputil '/enable-device' $id
        }
    }
    throw
}
