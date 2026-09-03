# MateView GT Ghost Touch Fix

A reversible, software-only workaround for a failing touch volume strip on the HUAWEI MateView GT 34-inch Sound Edition. It is built for the monitor identified as `ZQE-CAA` and keeps the built-in speakers usable.

The project covers this connection setup:

- macOS: MacBook connected directly by USB-C. A device-specific `hidutil` mapping suppresses only the MateView's faulty volume/media events (USB VID `0x12D1`, PID `0x10B6`). MacBook keyboard and headphone controls remain unaffected.
- Windows: gaming PC connected by DisplayPort without USB data. A DDC/CI watchdog restores monitor volume and unmuted state only after it detects drift.

## Download

Download the ZIP for your platform from [the latest release](https://github.com/rinchhen012/mateview-ghost-touch-fix/releases/latest).

## macOS / USB-C

Unzip `mateview-fix-macos.zip`, open Terminal in that folder, and run:

```sh
chmod +x *.sh
./install.sh
```

The per-user LaunchAgent applies the mapping at login and reapplies it after the monitor reconnects. No administrator password is required.

Useful commands:

```sh
./install.sh status
./install.sh disable
./install.sh enable
./uninstall.sh
```

`status` reports `active`, `inactive`, or `absent`. Disabling stops the LaunchAgent and clears only the MateView-specific mapping; enabling restores it.

## Windows / DisplayPort

First enable DDC/CI in the monitor's on-screen menu. Unzip `mateview-fix-windows.zip`, then double-click `Install.cmd`. It installs for the current user, starts the watchdog immediately, and registers it for later sign-ins. No administrator rights or third-party driver are required.

The default is volume `60`, unmuted. To use another persistent level, open PowerShell in the extracted folder and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-MateViewFix.ps1 Install -DesiredVolume 45
```

Other commands:

```powershell
# Inspect detected physical monitors.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\MateViewFix.ps1 list

# Read/correct once and report status.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\MateViewFix.ps1 status

# Stop the running watchdog and prevent startup without deleting files.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-MateViewFix.ps1 Disable

# Remove the utility and startup entry.
.\Uninstall.cmd
```

The watchdog reads VCP `0x62` (speaker volume) and `0x8D` (mute) serially every 500 ms. It writes only after drift, backs off to at most 10 seconds when unavailable, and refuses to target a monitor not identified as `ZQE-CAA`.

## What this cannot fix

Software cannot electrically shut down the touch controller or LED strip. The monitor may therefore keep a faint LED on even when its menu says off. If Huawei firmware draws its own volume overlay internally, this tool may restore the sound state but cannot guarantee that the monitor-generated overlay disappears.

The Windows package is designed for DisplayPort without USB data. If a USB data cable is attached to Windows, touch-strip events may also become Windows media keys; this DDC watchdog does not install a system-wide HID-blocking driver.

## Safety and removal

- The macOS mapping is restricted to Huawei VID/PID `0x12D1:0x10B6`.
- Windows DDC writes are restricted to identified `ZQE-CAA` monitors and only VCP `0x62`/`0x8D`.
- The software never changes brightness, contrast, input, power state, or VCP `0xCA`.
- Both installers are current-user only and include disable/uninstall paths.

This is an independent community workaround, not an official Huawei product. Use it at your own risk.

## Development

```sh
sh tests/macos/mateview-hid-filter-test.sh
sh tests/macos/install-test.sh
sh tests/package-test.sh
pwsh -NoProfile -Command "Invoke-Pester tests/windows -Output Detailed"
```

Technical rationale and the live hardware observations are in [`docs/superpowers/specs/2026-09-03-mateview-ghost-touch-fix-design.md`](docs/superpowers/specs/2026-09-03-mateview-ghost-touch-fix-design.md).
