# Platforms and hardware

GodotEnhancedInput is plain C# on top of Godot's input API: no native code, no platform SDKs. It runs
wherever Godot can run your C# project. What differs between platforms is what Godot reports — which
devices exist, which sensors and lights it can reach, how touch and gestures arrive.

## Platform support

| Platform | Status | Automated tests | Notes |
|---|---|---|---|
| Windows | Supported | CI (headless) | Joypads through SDL; rumble, light and motion sensors where the pad and driver expose them. |
| Linux | Supported | CI (headless) | As Windows. Some pads need udev permissions for motion sensors and lights. |
| macOS | Supported | CI (headless) | Trackpad pinch and two-finger scroll arrive as magnify and pan gestures (`gesture:pinch`, `mouse:wheel`). |
| Android | Expected to work | None | Multi-touch, gestures, safe area, handheld motion sensors (`device:gyroscope`), `VibrateHandheld`. Check on a device. |
| iOS | Expected to work | None | As Android. C# on iOS depends on your Godot version's support for it. |
| Web | Depends on Godot | None | Only if your Godot version can export C# projects to the web. Pads use the browser's gamepad API (no rumble, sensors or GUIDs in most browsers). |
| Consoles | Through a porting partner | None | Nothing platform-specific in the plugin; device identity, rumble and lights depend on the port's input layer. |
| XR | Supported (backend) | Unit tests with simulated trackers | `XRInputBackend` turns tracker poses and inputs into device controls. |

"Automated tests" means the full suite runs on that OS against a real headless Godot (see
`.github/workflows/tests.yml`). Headless runs cannot see real devices; the checklist below covers
what needs a person and the hardware.

## Hardware checklist

Run through the parts that matter for your game before a release, ideally with the demo scene and the
debug overlay (`InputDebugOverlay`) open. Tick a box only after seeing the behaviour on the device.

### Joypads

- [ ] **Xbox-style pad**: sticks with dead zones, triggers as 0…1 axes, every face/shoulder/menu button, D-pad. Prompts show the right style.
- [ ] **PlayStation-style pad**: as above; prompts switch to the PlayStation style; light bar (`SetJoypadLight`); motion sensors (`pad:gyroscope`) if Godot reports them; touchpad click as a button.
- [ ] **Nintendo-style pad / single Joy-Con**: face buttons by *position* (south/east/west/north) vs by label; prompts; sideways Joy-Con layout.
- [ ] **Generic / older pad**: unknown GUID falls back to a sensible layout; raw button and axis indices (`pad:button12`, `pad:axis7+`) bind and prompt by number.
- [ ] **Rumble**: weak/strong motors, duration, stop, and the `vibration_strength` setting scaling it down to zero.
- [ ] **Hot-plug**: unplug and replug during play — the same player gets the pad back (GUID match); a *different* pad on the same index does not inherit the assignment; held buttons release when the pad disappears.
- [ ] **Two or more pads**: join flow, each player hears only its own pad, reassigning a pad mid-hold releases for the old owner.
- [ ] **Steam Input / remappers**: with Steam Input on, the pad may appear as a virtual Xbox pad; check prompts and GUIDs with it on and off.

### Wheels, pedals, flight sticks

- [ ] **Steering wheel**: full-range axis maps to −1…1; centre is centred after `DeviceProfile.AxisCalibration`.
- [ ] **Pedals**: resting pedal reads 0, fully pressed reads 1 (calibration rest/full values); inverted axes handled by `InvertedAxes`.
- [ ] **HOTAS / flight stick**: many axes and buttons by raw index; throttle as a 0…1 axis; hat switch; `DeviceProfile` names show in prompts.
- [ ] **Custom backend devices** (if you wrote an `InputBackend`): attach, detach, controls update, rumble routed.

### Keyboard and mouse

- [ ] **Non-QWERTY layouts** (AZERTY, QWERTZ, Dvorak): physical bindings stay in place; label bindings follow the printed key; prompts show the printed key.
- [ ] **Modifier chords** (Ctrl/Shift/Alt + key) and key repeat (echo events ignored).
- [ ] **Mouse**: extra buttons, wheel notches and smooth scrolling, high-DPI mice with `DpiScale` / sensitivity settings, raw motion vs pointer position.
- [ ] **Focus loss**: alt-tab while holding a key — the action is canceled (a release-to-throw does not throw), nothing stays stuck.
- [ ] **Stuck keys**: hold a key, click another window, release, come back — `ReleaseStuckInput` lets go of it within a frame.
- [ ] **Low frame rate**: at 5–10 fps, quick taps, double taps and chords pressed in order still register in the frame they happened.

### Pen and touch

- [ ] **Pen tablet**: pressure 0…1, tilt, eraser end; note whether the pen tip also arrives as a left mouse click on that platform and bind accordingly.
- [ ] **Phone/tablet touch**: several fingers at once on virtual sticks and buttons; touches that start on a control do not reach gameplay.
- [ ] **Gestures**: pinch, twist, two-finger drag and swipe feel right at the device's screen density (`SwipeMinDistance`, `SwipeMinSpeed`).
- [ ] **Safe area**: controls stay clear of notches, rounded corners and system bars in portrait and landscape, after rotation.
- [ ] **Touch layouts**: edit mode drag, save, reload, scale and opacity; layout survives a resolution change.
- [ ] **App suspend/resume**: fingers held during a suspend are released.
- [ ] **Handheld sensors**: `device:gyroscope` / accelerometer / gravity read sensible values and respect the player's `AcceptTouch`.

### Trackpad (laptops)

- [ ] Pinch arrives as `gesture:pinch`; two-finger scroll as `mouse:wheel`; tap-to-click as a mouse button.

### XR

- [ ] Controller poses (`position`, `orientation` quaternion, `rotation` Euler, velocities) and inputs map through `XRInputBackend`; trackers appearing late or disappearing are handled.
- [ ] **Hand tracking**: `hand/pinch` and `hand/grab` reach 1 for a real pinch and fist and fall back to 0; `PinchRange` / `GrabRange` suit adult and child hands.
- [ ] **Face and eye tracking** (headsets that have it): `eyes/gaze` follows the eyes; blinks reach `eyes/closed_*`.
- [ ] **Per-hand haptics**: an effect played on the left controller buzzes only the left one, at the effect's frequency.

### Eye trackers (desktop)

- [ ] An `IEyeTracker` adapter for the vendor SDK (Tobii and similar): `gaze` follows where you look across the whole screen and on multiple monitors; `gaze_valid` drops while blinking or looking away; `present` follows sitting down and leaving.
- [ ] Dwell selection (`DwellTrigger`) feels right; `Radius` suits the tracker's accuracy at the screen's size.
- [ ] Unplugging the tracker disconnects the device and prompts fall back to the other devices.

### Device identity and switching

- [ ] Unplug a player's pad and plug it into another port (or reconnect over Bluetooth): it comes back to the same player, `PlayerDeviceLost` / `PlayerDeviceRegained` fire.
- [ ] Two identical pads keep their players when one reconnects.
- [ ] Moving between pad, keyboard and mouse and a backend device switches prompts and modes; losing the pad in use falls back to the keyboard.
- [ ] Families and prompts: Xbox, Elite (back paddles), DualShock 4, DualSense (Edge), Switch Pro, Joy-Con, Steam Deck and Steam Input pads show their own names and button labels.

### Force feedback

- [ ] A wheel backend applies `SetForceFeedback` (centring spring, damper, constant push, jolts); `force_feedback_strength` scales it and 0 turns it off.

### Accessibility and comfort

- [ ] Hold-to-toggle per action; relaxed timing preset (longer taps, shorter holds); vibration off; sensitivity and inversion settings saved in the profile and restored on load.

## Known limits

- Godot exposes no device events for keyboards and mice beyond "keyboard and mouse", so several
  keyboards or mice act as one device.
- Motion sensors, lights and rumble exist only where Godot's joypad layer reports them for that pad
  and platform.
- Sensor fusion (combining gyroscope and accelerometer into an orientation) is not included; the
  gyroscope calibration modifier removes drift, and fusion can be added as a custom modifier.
