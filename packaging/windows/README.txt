MateView Guardian 0.2.11 for 64-bit Windows 10/11

Recommended wiring:
- DisplayPort from the graphics card for 3440 x 1440 at 165 Hz and audio.
- USB data from the monitor's full-function USB-C port to a PC USB-C port or
  USB-A port using a data-capable cable.

Double-click Install.cmd, then use the notification-area shield. Guardian asks
for administrator approval when it starts so Windows can disable the MateView
HID control instance.

Normal Windows, keyboard, application, headphone, and Bluetooth volume controls
keep working. Run Uninstall.cmd to restore the touch strip and remove the app.

If the monitor exposes speaker volume VCP 0x62 but not optional mute VCP 0x8D,
Guardian continues volume correction and USB HID protection without mute restore.

This community build is not Authenticode-signed, so SmartScreen may warn once.
