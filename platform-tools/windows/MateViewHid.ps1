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
    if (-not [string]::IsNullOrWhiteSpace($env:MATEVIEW_HID_FIXTURE_JSON)) {
        $devices = @(Get-Content -LiteralPath $env:MATEVIEW_HID_FIXTURE_JSON -Raw | ConvertFrom-Json)
    }
    else {
        if (-not $IsWindows) {
            throw 'MateView HID detection is available only on Windows.'
        }
        $devices = @(Get-PnpDevice -Class HIDClass -ErrorAction Stop)
    }

    return @($devices |
        ForEach-Object { $_.InstanceId } |
        Where-Object { Test-MateViewHidIdentity $_ } |
        Sort-Object -Unique)
}

function Assert-Administrator {
    if ($env:MATEVIEW_SKIP_ADMIN_CHECK -eq '1') {
        return
    }
    if (-not $IsWindows) {
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
$pnputil = if ([string]::IsNullOrWhiteSpace($env:PNPUTIL_BIN)) { 'pnputil.exe' } else { $env:PNPUTIL_BIN }
$verb = if ($Action -eq 'Disable') { '/disable-device' } else { '/enable-device' }
foreach ($id in $validatedIds) {
    & $pnputil $verb $id
    if ($LASTEXITCODE -ne 0) {
        throw "pnputil failed to $($Action.ToLowerInvariant()) '$id' with exit code $LASTEXITCODE."
    }
}
