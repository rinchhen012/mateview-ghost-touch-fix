$ErrorActionPreference = 'Stop'

Describe 'MateView HID privileged helper' {
    BeforeAll {
        $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $script:helperPath = Join-Path $repoRoot 'platform-tools/windows/MateViewHid.ps1'
        $script:helper = Get-Content -LiteralPath $helperPath -Raw
    }

    It 'contains no environment-controlled fixture, elevation bypass, or pnputil path' {
        $helper | Should -Not -Match 'MATEVIEW_HID_FIXTURE_JSON'
        $helper | Should -Not -Match 'MATEVIEW_SKIP_ADMIN_CHECK'
        $helper | Should -Not -Match 'PNPUTIL_BIN'
    }

    It 'uses the system pnputil and Windows PowerShell-compatible platform detection' {
        $helper.Contains("Join-Path `$env:SystemRoot 'System32\pnputil.exe'") | Should -BeTrue
        $helper | Should -Match 'OSVersion\.Platform'
        $helper | Should -Not -Match '\$IsWindows'
    }

    It 'rejects non-MateView IDs before requesting administrator privileges' {
        { & $helperPath -Action Disable -InstanceId @('HID\VID_9999&PID_9999\OTHER') } |
            Should -Throw '*refusing*'
    }
}
