# Sources and devices

An `InputSource` says what a binding reads. Make one in the inspector, in code
(`InputSource.ForKey(Key.Space)`, `InputSource.ForJoyStick(JoyStickSide.Left)`…), or from a short
text path (`InputSourcePath.Parse("pad:left_stick")`).

## Source kinds

| Kind | Reads | Path examples | Value nature |
|---|---|---|---|
| `Key` | one key — physical (default), by key code or by printed label | `key:space`, `keycode:a`, `keylabel:a` | state |
| `KeyAxis1D` / `KeyAxis2D` | two or four keys as one axis or vector | `keys:a/d`, `keys:w/s/a/d` | state |
| `MouseButton` | a mouse button, or a wheel notch as a one-tick press | `mouse:left`, `mouse:wheel_up` | state |
| `MouseMotion` | mouse movement summed over the tick | `mouse:motion` | delta |
| `MousePosition` | pointer position in viewport pixels | `mouse:position` | absolute |
| `MouseWheel` | scrolling summed over the tick (notches and trackpad two-finger pans), down/right positive | `mouse:wheel` | delta |
| `JoyButton` | a joypad button by name or by raw index | `pad:a`, `pad2:south`, `pad:button12` | state |
| `JoyAxis` | an axis, a half axis, or an axis by raw index | `pad:left_x`, `pad:left_y-`, `pad:axis7+` | state |
| `JoyStick` | both axes of a stick, read from one pad together | `pad:left_stick` | state |
| `Motion` | a motion sensor of a pad or of the handheld device: accelerometer, gyroscope, gravity, magnetometer | `pad:gyroscope`, `device:gravity` | state |
| `Touch` | a finger: pressed, position or drag | `touch:pressed`, `touch1:drag` | per channel |
| `Gesture` | pinch, twist, two-finger drag or swipe (see [touch](players-and-touch.md#gestures)) | `gesture:pinch` | delta |
| `Pen` | pen pressure, tilt or eraser | `pen:pressure`, `pen:tilt` | state |
| `InputMapAction` | an action of Godot's InputMap | `inputmap:jump` | state |
| `Virtual` | a named value set from code or an on-screen control | `virtual:touch_move` | state |
| `DeviceControl` | a control of a device from a backend (wheels, XR, custom hardware) | `@wheel:steering`, `@xr1:trigger` | state |
| `Composite` | other sources combined (below) | `key:ctrl + key:s`, `mouse:left \| pad:rt`, `axis1d(pad:lt, pad:rt)` | from its parts |
| `None` | nothing — e.g. a binding that only carries a combo trigger | `none` | — |

Joypad sources with device −1 (`pad:`) read any pad the player owns; `pad2:` reads pad 2 only.
Button and axis names follow Godot's enum names, case- and underscore-insensitive, with position
aliases (`south`, `east`, `west`, `north`) and shoulder aliases (`lb`, `rb`, `lt`, `rt`).

### Value natures

Each source has a nature: **state** (held buttons, stick positions), **delta** (an amount per tick:
mouse motion, scrolling, drags) or **absolute** (positions). Modifiers use it:
`DeltaTimeScaleModifier` leaves deltas alone (they are already per tick), `IntegrateModifier` adds
deltas as they are and treats states as rates, `DifferentiateModifier` turns absolutes into movement.

### Composites

| `CompositeMode` | Parts | Value |
|---|---|---|
| `Axis1D` | negative, positive | positive − negative |
| `Axis2D` | up, down, left, right | a vector (up = −Y) |
| `WithModifiers` | modifiers…, value | the last part, only while every other part is held (`Ctrl + S`, `Shift + click`) |
| `AnyOf` | any | the strongest part |

```csharp
var save = InputSource.WithModifiers(Key.S, Key.Ctrl);
var gas  = InputSource.Composite(CompositeMode.Axis1D, InputSource.ForJoyAxis(JoyAxis.TriggerLeft), InputSource.ForJoyAxis(JoyAxis.TriggerRight));
```

Rebinding can replace a single part (`RebindPart`, see [rebinding](rebinding.md#composite-parts)).

## Devices

`EnhancedInput.Devices` lists every device the system knows about as `InputDeviceInfo`: keyboard and
mouse, each joypad (name, GUID, guessed role), touch, pen, and devices added by backends.
`DeviceConnected` and `DeviceDisconnected` report changes; `GetJoypadDevice(index)` and
`GetDevice(id)` look one up; `AssignDevice(player, device)` and `GetDeviceOwner(device)` work for any
device, not only joypads.

### Device profiles

A `DeviceProfile` (in `EnhancedInput.DeviceProfiles`) matches devices by GUID or by part of their
name and adjusts them before any binding sees their input:

| Field | Use |
|---|---|
| `Guids`, `NameContains` | which devices it applies to |
| `Role` | what the device is (gamepad, wheel, pedals, flight stick, throttle…) |
| `DisplayName` | the name shown in menus |
| `AxisCalibration` | per axis: rest, centre and full readings, mapped to −1…1 or 0…1 |
| `InvertedAxes` | axes that read the wrong way round |
| `ButtonNames`, `AxisNames` | names used by prompts ("Paddle Shift Up" instead of "Button 12") |

### Backends

Hardware Godot does not expose as a joypad can be added with an `InputBackend`: override `Name`,
`Poll(delta)` (and `Rumble` if the device has motors), call `AddDevice(type, index, name, guid)` /
`RemoveDevice(type, index)` when devices come and go and `SetControl(type, index, control, value)`
when they change. Bind its controls with
`DeviceControl` sources (`@wheel:steering`). Register it with `EnhancedInput.AddBackend`.

`XRInputBackend` is included: it follows Godot's XR trackers and exposes each controller's inputs and
its pose (`<pose>/position`, `<pose>/rotation`, `<pose>/linear_velocity`, `<pose>/angular_velocity`)
as device controls of type `xr`.

### Haptics and lights

```csharp
player.Rumble(weak: 0.3f, strong: 0.8f, seconds: 0.25f);   // every pad the player owns
player.StopRumble();
player.SetJoypadLight(Colors.Red);
player.VibrateHandheld(200);                               // phones and tablets
```

Rumble and handheld vibration scale by the player's `vibration_strength` setting (0 turns them
off; see [modes and accessibility](modes-and-accessibility.md)). Backends receive rumble for their devices.
Swap `HapticsOutput` for a test double or another output.
