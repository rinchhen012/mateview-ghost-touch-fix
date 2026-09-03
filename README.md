# MateView Guardian

MateView Guardian is a reversible, software-only workaround for a failing touch volume strip on the HUAWEI MateView GT 34-inch Sound Edition. It blocks the monitor's ghost media-key events, keeps the built-in speaker at a chosen hardware volume, and runs from the macOS menu bar or Windows notification area.

It targets only display model `ZQE-CAA` and Huawei USB HID identity `12D1:10B6`.

## Download and install

Download the GUI ZIP for your platform from [the latest release](https://github.com/rinchhen012/mateview-ghost-touch-fix/releases/latest).

### macOS (Apple Silicon)

1. Download and unzip `MateViewGuardian-macOS-arm64.zip`.
2. Control-click `Install.command`, choose **Open**, and approve the copy to Applications if asked.
3. Use the shield in the menu bar. Protection defaults to on and monitor speaker volume defaults to `30`.

The app is ad-hoc signed but not Apple-notarized, so Gatekeeper may show a warning on first launch. The source and SHA-256 checksums are published with every release.

### Windows 10/11 (64-bit)

For the highest refresh rate, use both cables:

- DisplayPort from the graphics card to the monitor for 3440×1440 at 165 Hz and audio.
- USB-C-to-USB-C from the PC to the monitor's full-function USB-C port for touch-strip HID data. Keep the monitor's selected display input on DisplayPort.

Then:

1. Download and unzip `MateViewGuardian-Windows-x64.zip`.
2. Double-click `Install.cmd`.
3. Use the shield in the notification area. Windows asks for administrator approval once when Guardian disables newly detected MateView HID instances.

The Windows build is not Authenticode-signed, so SmartScreen may warn on first launch. Choose **More info → Run anyway** only after checking the release checksum/source.

## What it does

- Blocks the MateView touch strip's volume up, volume down, mute/play-pause, and pause host events.
- Stops those ghost events from changing macOS/Windows volume or repeatedly opening the host volume popup.
- Maintains the monitor's internal speaker volume at your selected target (`0–100`, default `30`).
- On Windows, restores the monitor's unmuted state after ghost drift.
- Reapplies protection after reconnect and starts at login by default.

Normal keyboard buttons, system volume controls, per-app controls, wireless headphones, Bluetooth controls, and other USB devices still work. The target slider controls the monitor speaker's internal hardware volume; normal OS volume remains a separate control.

## Safety model

- macOS HID mapping is restricted to Huawei VID/PID `12D1:10B6` and four known consumer usages.
- Windows disables only exact HID instance IDs beginning `HID\VID_12D1&PID_10B6`, stores those IDs for recovery, and independently revalidates them in the elevated helper.
- DDC requires exact model token `ZQE-CAA`.
- macOS writes only VCP `0x62`; Windows writes only `0x62` and mute code `0x8D` values `1/2`.
- Guardian never changes brightness, contrast, input, power, or VCP `0xCA` and installs no filter driver/global keyboard hook.
- No telemetry or update service is included.

## Restore or uninstall

Turn **Protection** off or choose **Restore touch strip** to stop correction and remove the HID block. Windows may request administrator approval to re-enable the recorded devices.

Run the included `Uninstall.command` on macOS or `Uninstall.cmd` on Windows. The uninstaller restores the touch strip first, removes only Guardian's current-user startup/configuration files, and then removes the app.

## Limits

Guardian cannot electrically disable the touch controller or LED. The LED may remain faintly lit. It also cannot suppress a volume overlay drawn by the monitor's own firmware; it prevents the corresponding host volume change and host OS popup.

Windows DDC protection works over DisplayPort without USB, but blocking the touch strip requires the USB-C data cable. A changed USB port/topology can create a new HID instance and cause one new UAC prompt.

This is an independent community workaround, not an official Huawei product. Use it at your own risk.

## Legacy command-line packages

The v0.1 command-line packages remain available as `mateview-fix-macos.zip` and `mateview-fix-windows.zip`. Guardian imports the old Windows target-volume setting when possible and disables the old startup watcher after the GUI protection loop is ready.

## Development

Requires .NET 10, PowerShell 7 for local cross-platform PowerShell tests, and Xcode/Swift to build the pinned macOS DDC helper.

```sh
dotnet test MateViewGuardian.slnx
pwsh -NoProfile -Command "Invoke-Pester tests/windows -Output Detailed"
sh tests/macos/mateview-hid-filter-test.sh
sh tests/macos/install-test.sh
sh tests/gui-package-test.sh
```

The GUI design and safety constraints are documented in [`docs/superpowers/specs/2026-09-03-v0.2-gui-design.md`](docs/superpowers/specs/2026-09-03-v0.2-gui-design.md).
