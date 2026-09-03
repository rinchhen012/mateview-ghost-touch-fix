$ErrorActionPreference = 'Stop'

Describe 'MateView HID privileged helper' {
    BeforeAll {
        $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $script:helperPath = Join-Path $repoRoot 'platform-tools/windows/MateViewHid.ps1'
        $script:mateViewOne = 'HID\VID_12D1&PID_10B6&COL01\7&AAAA&0&0000'
        $script:mateViewTwo = 'HID\VID_12D1&PID_10B6\8&BBBB&0&0000'
    }

    BeforeEach {
        $script:testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("mateview-hid-test-" + [Guid]::NewGuid())
        New-Item -ItemType Directory -Path $script:testRoot | Out-Null
        $script:fixturePath = Join-Path $script:testRoot 'devices.json'
        $script:logPath = Join-Path $script:testRoot 'pnputil.log'
        $fakePnP = Join-Path $script:testRoot 'pnputil.ps1'
        @'
param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Remaining)
Add-Content -LiteralPath $env:MATEVIEW_PNPUTIL_LOG -Value ($Remaining -join '|')
exit 0
'@ | Set-Content -LiteralPath $fakePnP
        @(
            [pscustomobject]@{ InstanceId = $mateViewOne; Class = 'HIDClass' }
            [pscustomobject]@{ InstanceId = 'HID\VID_9999&PID_9999\OTHER'; Class = 'HIDClass' }
            [pscustomobject]@{ InstanceId = $mateViewTwo; Class = 'HIDClass' }
        ) | ConvertTo-Json | Set-Content -LiteralPath $fixturePath
        $env:MATEVIEW_HID_FIXTURE_JSON = $fixturePath
        $env:PNPUTIL_BIN = $fakePnP
        $env:MATEVIEW_PNPUTIL_LOG = $logPath
        $env:MATEVIEW_SKIP_ADMIN_CHECK = '1'
    }

    AfterEach {
        Remove-Item Env:MATEVIEW_HID_FIXTURE_JSON -ErrorAction SilentlyContinue
        Remove-Item Env:PNPUTIL_BIN -ErrorAction SilentlyContinue
        Remove-Item Env:MATEVIEW_PNPUTIL_LOG -ErrorAction SilentlyContinue
        Remove-Item Env:MATEVIEW_SKIP_ADMIN_CHECK -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $script:testRoot -Recurse -Force
    }

    It 'detects only exact MateView HID instance IDs as a JSON array' {
        $json = & $helperPath -Action Detect
        $ids = @($json | ConvertFrom-Json)

        $ids | Should -HaveCount 2
        $ids | Should -Contain $mateViewOne
        $ids | Should -Contain $mateViewTwo
    }

    It 'passes each exact ID to pnputil under one elevated helper run' {
        & $helperPath -Action Disable -InstanceId @($mateViewOne, $mateViewTwo)

        $calls = @(Get-Content -LiteralPath $logPath)
        $calls | Should -HaveCount 2
        $calls[0] | Should -Be "/disable-device|$mateViewOne"
        $calls[1] | Should -Be "/disable-device|$mateViewTwo"
    }

    It 'rejects every non-allowlisted ID before invoking pnputil' {
        { & $helperPath -Action Disable -InstanceId @($mateViewOne, 'HID\VID_9999&PID_9999\OTHER') } |
            Should -Throw '*refusing*'
        Test-Path -LiteralPath $logPath | Should -BeFalse
    }
}
