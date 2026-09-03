MateView GT Ghost Touch Fix - macOS
===================================

For a HUAWEI MateView GT 34 Sound Edition connected directly by USB-C.
The filter targets only USB vendor 0x12D1, product 0x10B6.

Install:
  chmod +x *.sh
  ./install.sh

Controls:
  ./install.sh status
  ./install.sh disable
  ./install.sh enable
  ./uninstall.sh

No administrator password is required. The built-in monitor speakers remain
available because this disables only the MateView media-control HID usages.

This cannot electrically disable the touch strip or its LEDs. If the monitor
firmware itself draws an overlay, host software may not be able to hide it.
