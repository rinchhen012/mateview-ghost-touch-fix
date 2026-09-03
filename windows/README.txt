MateView GT Ghost Touch Fix - Windows
=====================================

For a HUAWEI MateView GT 34 Sound Edition connected by DisplayPort without a
USB data connection. Enable DDC/CI in the monitor menu before installing.

Install:
  Double-click Install.cmd

Uninstall:
  Double-click Uninstall.cmd

PowerShell controls from this folder:
  powershell -ExecutionPolicy Bypass -File .\MateViewFix.ps1 list
  powershell -ExecutionPolicy Bypass -File .\MateViewFix.ps1 status
  powershell -ExecutionPolicy Bypass -File .\Install-MateViewFix.ps1 Disable
  powershell -ExecutionPolicy Bypass -File .\Install-MateViewFix.ps1 Install -DesiredVolume 45

The default is monitor volume 60 and unmuted. The watchdog reads DDC values
serially and writes only when they drift. It never changes brightness, input,
contrast, power, or the VCP 0xCA button-control feature.

This cannot electrically disable the touch strip or its LEDs, and it cannot
block Windows media-key events if a separate USB data cable is attached.
