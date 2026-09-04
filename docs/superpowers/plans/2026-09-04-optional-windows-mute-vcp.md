# Optional Windows Mute VCP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep Windows HID blocking and DDC volume correction working when the MateView supports VCP `0x62` but reports VCP `0x8D` as unsupported.

**Architecture:** Keep VCP `0x62` mandatory and treat only Windows error `0xC0262584` (`ERROR_GRAPHICS_DDCCI_VCP_NOT_SUPPORTED_BY_MONITOR`) from the optional `0x8D` read as a capability result. Return the existing `PlatformObservation` with `SupportsMute = false` and `CurrentMute = null`; the existing correction policy will then omit all `0x8D` writes while continuing safe `0x62` correction and independent HID protection.

**Tech Stack:** .NET 10, C#, Dxva2 Windows monitor API, xUnit, PowerShell/Pester, GitHub Actions

**Spec:** Bounded design approved in the 2026-09-04 debugging conversation; no separate design document.

**Execution note:** During Windows smoke testing, the scope was extended with user approval to suppress repeated UAC prompts after any failed elevated HID attempt, allow an explicit **Apply now** retry, enforce one Windows app instance, and prevent clipped Quit/Cancel button text. These additions are covered by regression tests in the platform and app test projects.

## Global Constraints

- USB HID identity `12D1:10B6` and DDC are independent paths; USB-C-to-USB-A is a valid data connection for HID protection.
- VCP `0x62` must remain mandatory, range-checked to `0..100`, and corrected only when it drifts.
- Only `0xC0262584` from reading VCP `0x8D` may be downgraded to “mute unsupported.”
- Transmission, receive, enumeration, cancellation, and all other errors must retain current failure/retry behavior.
- No new VCP writes are allowed; Windows may write only `0x62` values `0..100` and `0x8D = 2` when mute is supported.
- macOS behavior must remain unchanged.

---

### Task 1: Represent unsupported Windows mute as an optional capability

**Files:**
- Modify: `tests/MateViewGuardian.Platform.Tests/WindowsProtectionTests.cs`
- Modify: `src/MateViewGuardian.Platform/Windows/WindowsProtection.cs`

**Interfaces:**
- Consumes: `IWindowsPhysicalMonitor.Read(byte code)` and its `Win32Exception.NativeErrorCode`.
- Produces: `PlatformObservation` with `DdcHealthy = true`, `SupportsMute = false`, and `CurrentMute = null` when only VCP `0x8D` is unsupported.

- [ ] **Step 1: Add failing platform tests for unsupported and genuinely failed mute reads**

Add `using System.ComponentModel;` to `WindowsProtectionTests.cs`. Extend `FakeMonitor` with an optional `Exception? muteReadException = null` constructor parameter and make its `0x8D` branch throw that exception when present.

Add this regression test using the exact error observed on the affected MateView:

```csharp
[Fact]
public async Task ObserveContinuesWhenMuteVcpIsUnsupported()
{
    var unsupported = new Win32Exception(unchecked((int)0xC0262584));
    var mateView = new FakeMonitor(
        "HUAWEI ZQE-CAA", "", MateViewId, 30, 0,
        muteReadException: unsupported);
    var platform = Create(new FakeMonitorApi(mateView), new FakeHidProtection());

    var observation = await platform.ObserveAsync(default);

    Assert.True(observation.DisplayConnected);
    Assert.True(observation.DdcHealthy);
    Assert.Equal(30, observation.CurrentVolume);
    Assert.Null(observation.CurrentMute);
    Assert.False(observation.SupportsMute);
    Assert.Equal([0x62, 0x8D], mateView.Reads);
}
```

Add a safety test proving other DDC errors are not suppressed:

```csharp
[Fact]
public async Task ObservePropagatesOtherMuteReadFailures()
{
    var transmissionFailure = new Win32Exception(unchecked((int)0xC0262582));
    var mateView = new FakeMonitor(
        "HUAWEI ZQE-CAA", "", MateViewId, 30, 0,
        muteReadException: transmissionFailure);
    var platform = Create(new FakeMonitorApi(mateView), new FakeHidProtection());

    var exception = await Assert.ThrowsAsync<Win32Exception>(
        () => platform.ObserveAsync(default));

    Assert.Equal(transmissionFailure.NativeErrorCode, exception.NativeErrorCode);
}
```

The fake's read branch should be:

```csharp
return code switch
{
    0x62 => volume,
    0x8D when muteReadException is not null => throw muteReadException,
    0x8D => mute,
    _ => throw new InvalidOperationException("Unexpected read."),
};
```

- [ ] **Step 2: Run the focused tests and verify the unsupported-capability test fails**

Run:

```powershell
dotnet test tests/MateViewGuardian.Platform.Tests/MateViewGuardian.Platform.Tests.csproj --filter "FullyQualifiedName~ObserveContinuesWhenMuteVcpIsUnsupported|FullyQualifiedName~ObservePropagatesOtherMuteReadFailures"
```

Expected: `ObserveContinuesWhenMuteVcpIsUnsupported` fails with the `0x8D` `Win32Exception`; `ObservePropagatesOtherMuteReadFailures` passes because the current implementation already propagates it.

- [ ] **Step 3: Implement the narrow capability fallback**

In `WindowsProtection.cs`, add:

```csharp
using System.ComponentModel;
```

Define the named Windows error constant inside `WindowsProtection`:

```csharp
private const int ErrorGraphicsDdcCiVcpNotSupportedByMonitor =
    unchecked((int)0xC0262584);
```

Add a helper that catches only that exact error:

```csharp
private static uint? ReadMute(IWindowsPhysicalMonitor monitor, out bool supportsMute)
{
    try
    {
        var mute = monitor.Read(0x8D);
        supportsMute = true;
        return mute;
    }
    catch (Win32Exception exception)
        when (exception.NativeErrorCode == ErrorGraphicsDdcCiVcpNotSupportedByMonitor)
    {
        supportsMute = false;
        return null;
    }
}
```

Keep `monitor.Read(0x62)` outside this helper. Replace the unconditional mute read with:

```csharp
var volume = monitor.Read(0x62);
var mute = ReadMute(monitor, out var supportsMute);
if (volume > 100 || (mute.HasValue && mute.Value is not (1 or 2)))
{
    throw new InvalidOperationException(
        $"The MateView returned unsafe speaker state (volume {volume}, mute {mute}).");
}
```

Return the observation using:

```csharp
CurrentVolume: (int)volume,
CurrentMute: mute.HasValue ? (int)mute.Value : null,
SupportsMute: supportsMute
```

Do not change `CorrectionPolicy`: it already skips `0x8D` corrections when `SupportsMute` is false.

- [ ] **Step 4: Run the platform and core policy tests**

Run:

```powershell
dotnet test tests/MateViewGuardian.Platform.Tests/MateViewGuardian.Platform.Tests.csproj
dotnet test tests/MateViewGuardian.Core.Tests/MateViewGuardian.Core.Tests.csproj
```

Expected: all tests pass. Existing supported-mute tests must still read `0x62` then `0x8D` and plan volume before unmute.

- [ ] **Step 5: Commit the functional fix**

```powershell
git add src/MateViewGuardian.Platform/Windows/WindowsProtection.cs tests/MateViewGuardian.Platform.Tests/WindowsProtectionTests.cs
git commit -m "Handle unsupported Windows mute VCP"
```

---

### Task 2: Document cable and capability behavior

**Files:**
- Modify: `README.md`
- Modify: `packaging/windows/README.txt`

**Interfaces:**
- Consumes: confirmed hardware behavior: USB HID `12D1:10B6` is present over USB-C-to-USB-A, VCP `0x62` succeeds over DisplayPort, and VCP `0x8D` returns `0xC0262584`.
- Produces: accurate installation and limitation text in the repository and Windows ZIP.

- [ ] **Step 1: Correct the Windows cable instructions**

In `README.md`, replace the USB-C-to-USB-C-only statement with:

```markdown
- USB data from the monitor's full-function USB-C port to either a PC USB-C port or a USB-A port using a data-capable cable. Keep the monitor's selected display input on DisplayPort.
```

Make the equivalent change in `packaging/windows/README.txt`.

- [ ] **Step 2: Document optional mute restoration**

In `README.md`, change the Windows mute bullet to:

```markdown
- On Windows, restores the monitor's unmuted state when the display exposes VCP `0x8D`; volume protection continues when that optional feature is unsupported.
```

Add a sentence in the Limits section stating that some firmware/connection combinations expose volume VCP `0x62` but not mute VCP `0x8D`, and Guardian continues volume and HID protection in that case.

- [ ] **Step 3: Review documentation diff and commit**

Run:

```powershell
git diff --check
git diff -- README.md packaging/windows/README.txt
```

Expected: no whitespace errors; the text accurately distinguishes DisplayPort DDC from USB HID.

Commit:

```powershell
git add README.md packaging/windows/README.txt
git commit -m "Document optional Windows mute support"
```

---

### Task 3: Verify on the affected PC and publish v0.2.10

**Files:**
- Modify: `src/MateViewGuardian.App/MateViewGuardian.App.csproj`
- Modify: `scripts/package-gui.sh`
- Modify: `packaging/windows/README.txt`
- Modify: `packaging/macos/Info.plist`
- Modify: `packaging/macos/README.txt`

**Interfaces:**
- Consumes: Tasks 1 and 2 and the repository's existing tag-triggered `.github/workflows/release.yml`.
- Produces: tested `v0.2.10` Windows and macOS release archives.

- [ ] **Step 1: Run the full automated verification suite**

Run:

```powershell
dotnet test MateViewGuardian.slnx --configuration Release --nologo
pwsh -NoProfile -Command '$result = Invoke-Pester tests/windows -Output Detailed -PassThru; if ($result.Result -ne "Passed") { exit 1 }'
git diff --check
```

Expected: zero .NET test failures, Pester result `Passed`, and no whitespace errors.

- [ ] **Step 2: Build and smoke-test a Windows release binary before tagging**

Run:

```powershell
dotnet publish src/MateViewGuardian.App/MateViewGuardian.App.csproj -c Release -r win-x64 --self-contained true --nologo -p:Version=0.2.10 -p:DebugType=None -p:DebugSymbols=false -o dist/manual-win
```

On the affected PC, launch `dist/manual-win/MateViewGuardian.App.exe` and verify all of the following:

1. The display is detected as `ZQE-CAA`/`MONITOR\HWV6A25`.
2. No “Could not read VCP feature 0x8D” error appears.
3. Changing the target volume causes only VCP `0x62` correction.
4. The USB HID `VID_12D1&PID_10B6` instances are blocked after UAC approval.
5. Restoring protection re-enables the recorded HID instances.

- [ ] **Step 3: Bump all release metadata to 0.2.10**

Apply these exact changes:

- `src/MateViewGuardian.App/MateViewGuardian.App.csproj`: `<Version>0.2.10</Version>`
- `scripts/package-gui.sh`: both `-p:Version=0.2.9` occurrences become `-p:Version=0.2.10`
- `packaging/windows/README.txt`: first line becomes `MateView Guardian 0.2.10 for 64-bit Windows 10/11`
- `packaging/macos/Info.plist`: `CFBundleShortVersionString` becomes `0.2.10` and `CFBundleVersion` becomes `11`
- `packaging/macos/README.txt`: first line becomes `MateView Guardian 0.2.10 for Apple Silicon macOS`

- [ ] **Step 4: Commit the release metadata**

```powershell
git add src/MateViewGuardian.App/MateViewGuardian.App.csproj scripts/package-gui.sh packaging/windows/README.txt packaging/macos/Info.plist packaging/macos/README.txt
git commit -m "Prepare v0.2.10 release"
```

- [ ] **Step 5: Push main, wait for CI, then tag**

```powershell
git push origin main
```

Wait for the `main` CI run to complete successfully. Then run:

```powershell
git tag -a v0.2.10 -m "Release v0.2.10"
git push origin v0.2.10
```

- [ ] **Step 6: Verify the published release**

Wait for the tag-triggered Release workflow to succeed. Confirm that the `v0.2.10` release contains:

```text
MateViewGuardian-macOS-arm64.zip
MateViewGuardian-Windows-x64.zip
SHA256SUMS.txt
mateview-fix-macos.zip
mateview-fix-windows.zip
```

Download `MateViewGuardian-Windows-x64.zip`, verify its checksum from `SHA256SUMS.txt`, reinstall, and repeat the five smoke checks from Step 2.
