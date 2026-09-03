$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$modulePath = Join-Path $repoRoot 'windows/MateViewFix.psm1'
Import-Module $modulePath -Force

Describe 'MateView monitor selection' {
    It 'selects the exact MateView model' {
        $monitors = @(
            [pscustomobject]@{ Index = 0; Description = 'DELL U2720Q' }
            [pscustomobject]@{ Index = 1; Description = 'ZQE-CAA' }
        )

        $selected = Select-MateViewMonitor -Monitors $monitors

        $selected.Index | Should -Be 1
    }

    It 'rejects a non-MateView monitor' {
        { Select-MateViewMonitor -Monitors @([pscustomobject]@{ Index = 0; Description = 'DELL U2720Q' }) } |
            Should -Throw '*ZQE-CAA*'
    }

    It 'requires an index when multiple MateViews are present' {
        $monitors = @(
            [pscustomobject]@{ Index = 0; Description = 'ZQE-CAA' }
            [pscustomobject]@{ Index = 1; Description = 'ZQE-CAA' }
        )

        { Select-MateViewMonitor -Monitors $monitors } | Should -Throw '*multiple*'
        (Select-MateViewMonitor -Monitors $monitors -Index 1).Index | Should -Be 1
    }

    It 'does not allow an index to bypass model verification' {
        $monitors = @([pscustomobject]@{ Index = 4; Description = 'DELL U2720Q' })
        { Select-MateViewMonitor -Monitors $monitors -Index 4 } | Should -Throw '*ZQE-CAA*'
    }
}

Describe 'MateView correction policy' {
    It 'plans no writes when volume and mute already match' {
        Get-MateViewCorrection -CurrentVolume 60 -CurrentMute 2 -DesiredVolume 60 -Unmuted |
            Should -BeNullOrEmpty
    }

    It 'plans serialized volume and unmute writes after drift' {
        $result = @(Get-MateViewCorrection -CurrentVolume 0 -CurrentMute 1 -DesiredVolume 60 -Unmuted)

        $result.Count | Should -Be 2
        $result[0].Code | Should -Be 0x62
        $result[0].Value | Should -Be 60
        $result[1].Code | Should -Be 0x8D
        $result[1].Value | Should -Be 2
    }

    It 'does not write mute when only volume drifted' {
        $result = @(Get-MateViewCorrection -CurrentVolume 10 -CurrentMute 2 -DesiredVolume 60 -Unmuted)

        $result.Count | Should -Be 1
        $result[0].Code | Should -Be 0x62
    }

    It 'rejects volume outside zero through one hundred' {
        { Get-MateViewCorrection -CurrentVolume 60 -CurrentMute 2 -DesiredVolume 101 -Unmuted } |
            Should -Throw '*between 0 and 100*'
    }
}

Describe 'MateView retry policy' {
    It 'doubles failures up to ten seconds and resets after success' {
        (Get-MateViewRetryDelay -CurrentMilliseconds 500 -Succeeded:$false) | Should -Be 1000
        (Get-MateViewRetryDelay -CurrentMilliseconds 8000 -Succeeded:$false) | Should -Be 10000
        (Get-MateViewRetryDelay -CurrentMilliseconds 10000 -Succeeded:$false) | Should -Be 10000
        (Get-MateViewRetryDelay -CurrentMilliseconds 10000 -Succeeded:$true) | Should -Be 500
    }
}

Describe 'MateView correction execution' {
    It 'accepts an empty correction list without writing' {
        $monitor = [pscustomobject]@{}
        $monitor | Add-Member -MemberType ScriptMethod -Name Write -Value { throw 'unexpected write' }

        { Invoke-MateViewCorrection -Monitor $monitor -Corrections @() } | Should -Not -Throw
    }
}
