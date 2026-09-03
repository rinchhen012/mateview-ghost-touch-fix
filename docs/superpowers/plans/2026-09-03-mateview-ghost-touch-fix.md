# MateView GT Ghost-Touch Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship downloadable macOS and Windows utilities that prevent a faulty HUAWEI MateView GT touch strip from changing host or monitor volume while preserving its built-in speakers.

**Architecture:** macOS applies a device-specific `hidutil` no-op mapping to the MateView USB HID interface and maintains it through a per-user LaunchAgent. Windows uses a PowerShell module backed by `Dxva2.dll` DDC/CI calls to monitor VCP `0x62` and `0x8D`, correcting drift only on a selected `ZQE-CAA` physical monitor.

**Tech Stack:** POSIX shell and native `hidutil` on macOS; Windows PowerShell 5.1-compatible PowerShell/C# P/Invoke; shell tests, Pester tests, and GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-03-mateview-ghost-touch-fix-design.md`

## Global Constraints

- Target USB HID identity is vendor `0x12D1`, product `0x10B6`.
- Target display model is `ZQE-CAA`.
- Default desired monitor volume is `60`, unmuted.
- Active watchdog interval is 500 ms; unavailable-device retry backs off to at most 10 seconds.
- DDC calls must be serialized and must never target an unselected physical monitor.
- Normal operation must never write VCP `0xCA`, input, brightness, contrast, or power state.
- Every install is reversible and includes `status`, `disable`, and `uninstall` behavior.

---

### Task 1: macOS MateView-only HID filter

**Files:**
- Create: `macos/mateview-hid-filter.sh`
- Create: `tests/macos/mateview-hid-filter-test.sh`

**Interfaces:**
- Consumes: `/usr/bin/hidutil`; optional test override `HIDUTIL_BIN`.
- Produces: `mateview-hid-filter.sh apply|clear|status|watch`, with exit code `0` on success, `2` when the MateView is absent, and `1` on command failure.

- [ ] **Step 1: Write the failing macOS command tests**

Create a temporary fake `hidutil` that logs arguments and returns controlled property output. Assert these behaviors with literal expected values:

```sh
assert_contains "$calls" '--matching {"VendorID":0x12d1,"ProductID":0x10b6}'
assert_contains "$calls" 'HIDKeyboardModifierMappingSrc":0xC000000E9'
assert_contains "$calls" 'HIDKeyboardModifierMappingSrc":0xC000000EA'
assert_contains "$calls" 'HIDKeyboardModifierMappingDst":0x700000000'
assert_exit 2 env HIDUTIL_BIN="$fake" "$subject" status
```

The production change caught by these tests is broadening the filter to other keyboards, omitting either volume direction, or treating an absent monitor as active.

- [ ] **Step 2: Run the macOS tests and verify RED**

Run: `sh tests/macos/mateview-hid-filter-test.sh`

Expected: FAIL because `macos/mateview-hid-filter.sh` does not exist.

- [ ] **Step 3: Implement the minimal HID filter**

Implement constants and commands:

```sh
MATCH='{"VendorID":0x12d1,"ProductID":0x10b6}'
MAPPING='{"UserKeyMapping":[
  {"HIDKeyboardModifierMappingSrc":0xC000000E9,"HIDKeyboardModifierMappingDst":0x700000000},
  {"HIDKeyboardModifierMappingSrc":0xC000000EA,"HIDKeyboardModifierMappingDst":0x700000000},
  {"HIDKeyboardModifierMappingSrc":0xC000000CD,"HIDKeyboardModifierMappingDst":0x700000000},
  {"HIDKeyboardModifierMappingSrc":0xC000000B1,"HIDKeyboardModifierMappingDst":0x700000000}
]}'
```

`apply` verifies the matching service exists before setting the property. `clear` sets an empty mapping only on the matching MateView. `status` reports `active`, `inactive`, or `absent`. `watch` rechecks every five seconds and reapplies only when the mapping is missing.

- [ ] **Step 4: Run macOS tests and verify GREEN**

Run: `sh tests/macos/mateview-hid-filter-test.sh`

Expected: all assertions pass with no stderr output.

- [ ] **Step 5: Commit the macOS filter**

```bash
git add macos/mateview-hid-filter.sh tests/macos/mateview-hid-filter-test.sh
git commit -m "feat: add MateView-specific macOS HID filter"
```

### Task 2: macOS installation and recovery

**Files:**
- Create: `macos/install.sh`
- Create: `macos/uninstall.sh`
- Create: `macos/com.mateview-ghost-touch-fix.plist.template`
- Create: `tests/macos/install-test.sh`

**Interfaces:**
- Consumes: `macos/mateview-hid-filter.sh`.
- Produces: files under `$HOME/Library/Application Support/MateViewGhostTouchFix` and `$HOME/Library/LaunchAgents/com.mateview-ghost-touch-fix.plist`.

- [ ] **Step 1: Write failing installer tests in an isolated fake home**

Run installers with `HOME` set to a temporary directory. Assert that installation copies the filter, substitutes the absolute executable path into the plist, and invokes `launchctl bootstrap`; assert uninstall invokes `clear`, removes only its own files, and is idempotent.

```sh
assert_file "$fake_home/Library/Application Support/MateViewGhostTouchFix/mateview-hid-filter.sh"
assert_contains "$(cat "$plist")" '<string>watch</string>'
assert_contains "$launchctl_calls" 'bootstrap gui/'
assert_not_exists "$fake_home/Library/LaunchAgents/com.mateview-ghost-touch-fix.plist"
```

The production change caught is an incorrect startup path, failure to activate at login, or destructive cleanup outside the utility's files.

- [ ] **Step 2: Run installer tests and verify RED**

Run: `sh tests/macos/install-test.sh`

Expected: FAIL because the installer files do not exist.

- [ ] **Step 3: Implement install and uninstall scripts**

Installation copies the filter, renders the plist with `RunAtLoad=true` and `KeepAlive=true`, bootstraps it into `gui/$UID`, then runs `apply`. Uninstall boots out the known label, runs `clear`, and removes the installed filter directory and plist.

- [ ] **Step 4: Run installer and all macOS tests**

Run: `sh tests/macos/install-test.sh && sh tests/macos/mateview-hid-filter-test.sh`

Expected: both test scripts pass.

- [ ] **Step 5: Commit macOS lifecycle support**

```bash
git add macos tests/macos/install-test.sh
git commit -m "feat: install and remove macOS filter"
```

### Task 3: Windows DDC/CI module and watchdog

**Files:**
- Create: `windows/MateViewFix.psm1`
- Create: `windows/MateViewFix.ps1`
- Create: `tests/windows/MateViewFix.Tests.ps1`

**Interfaces:**
- Consumes: Windows `Dxva2.dll` and `User32.dll` monitor APIs.
- Produces: `Get-MateViewMonitors`, `Get-MateViewCorrection`, `Invoke-MateViewCorrection`, and CLI commands `list|once|watch|status|set-volume`.

- [ ] **Step 1: Write failing Pester tests for identity and correction decisions**

Use literal monitor fixtures and assert real pure-function results:

```powershell
It 'rejects a non-MateView monitor' {
    { Select-MateViewMonitor -Monitors @([pscustomobject]@{ Index=0; Description='DELL U2720Q' }) } |
        Should -Throw '*ZQE-CAA*'
}

It 'plans no writes when volume and mute already match' {
    Get-MateViewCorrection -CurrentVolume 60 -CurrentMute 2 -DesiredVolume 60 -Unmuted |
        Should -BeNullOrEmpty
}

It 'plans serialized volume and unmute writes after drift' {
    $result = @(Get-MateViewCorrection -CurrentVolume 0 -CurrentMute 1 -DesiredVolume 60 -Unmuted)
    $result[0].Code | Should -Be 0x62
    $result[0].Value | Should -Be 60
    $result[1].Code | Should -Be 0x8D
    $result[1].Value | Should -Be 2
}
```

The production changes caught are selecting an unrelated monitor, blind writes when state already matches, or wrong VCP values/order.

- [ ] **Step 2: Run tests on `windows-latest` and verify RED**

Run: `pwsh -NoProfile -Command "Invoke-Pester tests/windows/MateViewFix.Tests.ps1 -Output Detailed"`

Expected: FAIL because `windows/MateViewFix.psm1` does not exist.

- [ ] **Step 3: Implement monitor enumeration and correction policy**

Embed C# P/Invoke definitions for `EnumDisplayMonitors`, `GetNumberOfPhysicalMonitorsFromHMONITOR`, `GetPhysicalMonitorsFromHMONITOR`, `GetVCPFeatureAndVCPFeatureReply`, `SetVCPFeature`, and `DestroyPhysicalMonitors`. Wrap physical handles in `try/finally` and destroy every acquired handle.

`Get-MateViewCorrection` returns ordered value objects and validates desired volume with:

```powershell
if ($DesiredVolume -lt 0 -or $DesiredVolume -gt 100) {
    throw 'Volume must be between 0 and 100.'
}
```

The CLI defaults to a case-insensitive exact model match containing `ZQE-CAA`; an explicit saved index is allowed only after the user runs `list`.

- [ ] **Step 4: Implement the watchdog loop and backoff**

Read VCP `0x62`, then `0x8D`, derive corrections, and apply them in order. Sleep 500 ms after success. On absence/read failure, double the retry delay from 500 ms to a maximum of 10 seconds. Reset delay after the next successful complete read.

- [ ] **Step 5: Run Pester tests and verify GREEN**

Run: `pwsh -NoProfile -Command "Invoke-Pester tests/windows/MateViewFix.Tests.ps1 -Output Detailed"`

Expected: all tests pass on Windows PowerShell 5.1 and PowerShell 7.

- [ ] **Step 6: Commit the Windows watchdog**

```bash
git add windows/MateViewFix.psm1 windows/MateViewFix.ps1 tests/windows/MateViewFix.Tests.ps1
git commit -m "feat: add Windows MateView DDC watchdog"
```

### Task 4: Windows install, disable, and uninstall lifecycle

**Files:**
- Create: `windows/Install.cmd`
- Create: `windows/Uninstall.cmd`
- Create: `windows/Install-MateViewFix.ps1`
- Create: `tests/windows/Install.Tests.ps1`

**Interfaces:**
- Consumes: `windows/MateViewFix.ps1` and `windows/MateViewFix.psm1`.
- Produces: `%LOCALAPPDATA%\MateViewGhostTouchFix` and a current-user Startup-folder launcher.

- [ ] **Step 1: Write failing lifecycle tests**

Use temporary `LOCALAPPDATA` and `APPDATA` roots. Assert install copies only the package files, writes a startup command invoking `watch`, and records volume `60`; assert disable removes the startup launcher; assert uninstall removes only the utility directory and launcher.

- [ ] **Step 2: Run lifecycle tests and verify RED**

Run: `pwsh -NoProfile -Command "Invoke-Pester tests/windows/Install.Tests.ps1 -Output Detailed"`

Expected: FAIL because Windows lifecycle scripts do not exist.

- [ ] **Step 3: Implement lifecycle scripts**

`Install.cmd` invokes the PowerShell installer with `-ExecutionPolicy Bypass`. The installer creates a Startup-folder `.cmd` that calls:

```cmd
powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "%LOCALAPPDATA%\MateViewGhostTouchFix\MateViewFix.ps1" watch --volume 60
```

No administrator rights, driver, service, or registry modification is required.

- [ ] **Step 4: Run all Windows tests and verify GREEN**

Run: `pwsh -NoProfile -Command "Invoke-Pester tests/windows -Output Detailed"`

Expected: all Windows tests pass.

- [ ] **Step 5: Commit Windows lifecycle support**

```bash
git add windows tests/windows/Install.Tests.ps1
git commit -m "feat: add Windows install and recovery scripts"
```

### Task 5: Documentation, CI, packaging, and GitHub release

**Files:**
- Create: `README.md`
- Create: `LICENSE`
- Create: `.github/workflows/ci.yml`
- Create: `.github/workflows/release.yml`
- Create: `scripts/package.sh`
- Create: `tests/package-test.sh`

**Interfaces:**
- Consumes: completed platform directories and tests.
- Produces: `mateview-fix-macos.zip`, `mateview-fix-windows.zip`, passing CI, and a tagged GitHub release.

- [ ] **Step 1: Write the failing package behavior test**

Run `scripts/package.sh` into a temporary output directory, inspect each zip with `unzip -Z1`, and assert literal required entries. The test fails if either package contains repository metadata, docs-only files, or omits install/uninstall commands.

- [ ] **Step 2: Run the package test and verify RED**

Run: `sh tests/package-test.sh`

Expected: FAIL because the package script does not exist.

- [ ] **Step 3: Implement packaging and user documentation**

Document the verified connection split: USB-C HID filtering on macOS and DDC/CI over DisplayPort on Windows. Include installation, status, volume selection, disable, uninstall, limitations, and recovery commands. Package only runtime files plus a short platform README.

- [ ] **Step 4: Add CI and release workflows**

CI runs macOS shell tests on `macos-latest`, Pester tests on `windows-latest` under both Windows PowerShell and PowerShell 7, and the package test. Release runs packaging on a version tag and uploads both zip files using GitHub's release action.

- [ ] **Step 5: Run all locally available tests**

Run: `sh tests/macos/mateview-hid-filter-test.sh && sh tests/macos/install-test.sh && sh tests/package-test.sh`

Expected: all local tests pass with no warnings.

- [ ] **Step 6: Perform the connected-Mac hardware test**

Apply the filter temporarily, read it back with device-specific `hidutil status`, verify the MacBook's own volume keys still work, and verify MateView touch activity no longer changes the selected macOS output. Clear and reapply once to prove recovery.

- [ ] **Step 7: Commit release preparation**

```bash
git add README.md LICENSE .github scripts tests/package-test.sh
git commit -m "docs: add packaging and release workflow"
```

- [ ] **Step 8: Create private GitHub repository and push**

```bash
gh repo create rinchhen012/mateview-ghost-touch-fix --private --source=. --remote=origin --push
```

- [ ] **Step 9: Verify remote CI, tag, and release**

Wait for CI to pass, tag `v0.1.0`, push the tag, wait for the release workflow, and verify both downloadable zip assets are present.
