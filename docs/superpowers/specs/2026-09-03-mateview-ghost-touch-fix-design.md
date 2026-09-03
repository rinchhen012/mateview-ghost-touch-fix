# MateView GT Ghost-Touch Software Fix Design

## Goal

Prevent a faulty HUAWEI MateView GT 34-inch Sound Edition touch strip from repeatedly changing or muting host and monitor audio, while preserving video and the built-in speakers on both macOS and Windows.

## Verified hardware behavior

The connected monitor identifies as `ZQE-CAA`.

- USB HID identity: vendor `0x12D1`, product `0x10B6`, product name `MateView GT`.
- Its HID consumer-control collection exposes volume up (`0xE9`), volume down (`0xEA`), play/pause (`0xCD`), and pause (`0xB1`).
- DisplayPort audio is exposed separately as `ZQE-CAA`, so filtering the HID controls does not remove speaker audio.
- DDC/CI reads and writes work when serialized. Verified values were volume `60/100`, brightness `90/100`, and contrast `80/100`.
- VCP `0x62` (speaker volume) and `0x8D` (audio mute) respond.
- VCP `0xCA` reports current value `1` and maximum `2`. The MCCS value `3` button/event-disable request was acknowledged but ignored; readback remained `1`.

## Selected approach

Use two complementary controls rather than a single cross-platform mechanism.

### macOS HID filter

Apply a device-specific `hidutil` mapping only to vendor `0x12D1`, product `0x10B6`. Map the MateView's volume-up, volume-down, play/pause, and pause usages to a no-op destination. Do not modify global mappings or mappings for Apple/internal keyboards.

The mapping is initially applied as a reversible manual test. After validation, a per-user LaunchAgent reapplies it at login and whenever the monitor is reconnected. Removal clears only the MateView-specific mapping and unloads the LaunchAgent.

This layer prevents ghost touches from changing the current macOS output, including Bluetooth headphones. It cannot prevent firmware-local changes inside the monitor.

### DDC volume watchdog

Maintain a configured monitor speaker volume and mute state through DDC/CI:

- Read VCP `0x62` and `0x8D` serially.
- Write only when a value differs from the configured target.
- Never issue concurrent DDC operations.
- Back off when the monitor is absent, asleep, or temporarily rejects a request.
- Stop cleanly without resetting unrelated monitor settings.

On Windows, the watchdog uses the native Low-Level Monitor Configuration API in `Dxva2.dll` over DisplayPort. It selects the physical display whose description/EDID matches `ZQE-CAA` and never writes to another monitor.

The shipped macOS fix uses the HID layer because that is the path that changes the current macOS output, including Bluetooth headphones. The DDC watchdog is shipped for the DisplayPort-only Windows setup.

## Alternatives rejected

### DDC button lock

VCP `0xCA = 3` would be the cleanest standards-based solution, but the monitor ignored the command. Values outside the monitor's reported range will not be retried.

### Generic media-key interception

Global event interception would also block the MacBook keyboard, gaming keyboard, or headset buttons. The fix must match the MateView's vendor and product IDs.

### Constant blind writes

Continuously writing volume without first reading it would create needless DDC traffic and could contend with display-management software. The watchdog writes only after detecting drift.

## User controls

The packages provide platform-appropriate commands for:

- `enable`: activate filtering/watchdog behavior.
- `disable`: stop it without deleting files.
- `status`: show monitor detection, current DDC values, and active components.
- Windows `set-volume <0-100>`: update the desired speaker volume.
- `uninstall`: remove startup registration and restore MateView-specific host mappings.

Defaults are volume `60`, unmuted, a 500 ms active polling interval, and exponential retry up to 10 seconds while unavailable.

## Safety and recovery

- Every DDC write is restricted to `ZQE-CAA`.
- Configuration rejects volume values outside `0-100`.
- macOS removal clears the mapping only on the matching MateView HID device; reconnecting the device also resets transient `hidutil` state.
- DDC failures are logged and retried; they do not trigger monitor resets or writes to other VCP codes.
- The utility never writes VCP `0xCA`, input selection, brightness, contrast, or power state during normal operation.
- Startup jobs run in the user context. Neither installer requires administrator rights.

## Verification

### Automated tests

- Parse and match only the intended monitor identity.
- Validate configuration and volume bounds.
- Confirm watchdog writes only after detected drift.
- Confirm DDC calls are serialized and retry delays are bounded.
- Confirm enable/disable/uninstall operations are idempotent.
- Confirm generated macOS and Windows startup registrations target the correct executable and arguments.

### Hardware tests

1. On macOS, apply the temporary HID filter and verify normal Mac keyboard volume keys still work.
2. Touch or wait for ghost input from the MateView and verify Bluetooth/current-output volume does not change.
3. Select the MateView speakers and verify audio still plays.
4. Enable the DDC watchdog, manually change/mute the monitor, and verify the configured volume/unmuted state returns.
5. Repeat the watchdog test on Windows over DisplayPort.
6. Disable and uninstall on each platform, then verify normal behavior is restored.

## Limitations

The software cannot prevent the touch/LED controller from electrically activating. If Huawei firmware draws its own volume overlay independently of HID and continues drawing it after the host events are filtered, the DDC watchdog can restore audio state but cannot guarantee removal of that firmware-generated overlay.
