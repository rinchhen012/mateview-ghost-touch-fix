$ErrorActionPreference = 'Stop'

Describe 'Windows install lifecycle' {
    BeforeAll {
        $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $installer = Join-Path $repoRoot 'windows/Install-MateViewFix.ps1'
    }

    BeforeEach {
        $testRoot = Join-Path $TestDrive 'profile'
        $localRoot = Join-Path $testRoot 'LocalAppData'
        $roamingRoot = Join-Path $testRoot 'AppData'
        $installDir = Join-Path $localRoot 'MateViewGhostTouchFix'
        $startupDir = Join-Path $roamingRoot 'Microsoft/Windows/Start Menu/Programs/Startup'
        $launcher = Join-Path $startupDir 'MateViewGhostTouchFix.cmd'
        New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $testRoot 'keep-me.txt') -Value 'sentinel'
    }

    It 'installs package files and a volume-60 startup watchdog' {
        & $installer Install -DesiredVolume 60 -LocalAppDataRoot $localRoot -RoamingAppDataRoot $roamingRoot

        Join-Path $installDir 'MateViewFix.ps1' | Should -Exist
        Join-Path $installDir 'MateViewFix.psm1' | Should -Exist
        Join-Path $installDir 'config.json' | Should -Exist
        $launcher | Should -Exist

        $launcherText = Get-Content -LiteralPath $launcher -Raw
        $launcherText | Should -Match 'powershell\.exe'
        $launcherText | Should -Match 'MateViewFix\.ps1" watch'
        $launcherText | Should -Match 'DesiredVolume 60'

        $config = Get-Content -LiteralPath (Join-Path $installDir 'config.json') -Raw | ConvertFrom-Json
        $config.DesiredVolume | Should -Be 60
    }

    It 'disables startup without deleting the installed utility' {
        & $installer Install -LocalAppDataRoot $localRoot -RoamingAppDataRoot $roamingRoot
        & $installer Disable -LocalAppDataRoot $localRoot -RoamingAppDataRoot $roamingRoot

        $launcher | Should -Not -Exist
        Join-Path $installDir 'MateViewFix.ps1' | Should -Exist
    }

    It 'uninstalls only its own files and is idempotent' {
        & $installer Install -LocalAppDataRoot $localRoot -RoamingAppDataRoot $roamingRoot
        & $installer Uninstall -LocalAppDataRoot $localRoot -RoamingAppDataRoot $roamingRoot

        $launcher | Should -Not -Exist
        $installDir | Should -Not -Exist
        Join-Path $testRoot 'keep-me.txt' | Should -Exist

        { & $installer Uninstall -LocalAppDataRoot $localRoot -RoamingAppDataRoot $roamingRoot } |
            Should -Not -Throw
    }
}
