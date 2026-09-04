MateView Guardian 0.2.9 for 64-bit Windows 10/11

Recommended wiring:
- DisplayPort from the graphics card for 3440 x 1440 at 165 Hz and audio.
- USB data from the monitor's full-function USB-C port to a PC USB-C port or
  USB-A port using a data-capable cable.

Double-click Install.cmd, then use the notification-area shield. Enabling HID
protection asks for one administrator approval so Windows can disable only the
MateView HID instances matching VID_12D1 and PID_10B6.

Normal Windows, keyboard, application, headphone, and Bluetooth volume controls
keep working. Run Uninstall.cmd to restore the touch strip and remove the app.

If the monitor exposes speaker volume VCP 0x62 but not optional mute VCP 0x8D,
Guardian continues volume correction and USB HID protection without mute restore.

This community build is not Authenticode-signed, so SmartScreen may warn once.
