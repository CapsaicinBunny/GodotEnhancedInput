# Sources and devices

An `InputSource` says what a binding reads. Make one in the Inspector (type a path in a mapping's
*Input* box, or *New Input Source* and set its fields), in code
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
Backend devices are known by a persistent id — "type:guid" when the backend gives the hardware's
identity, else "type:index" — separate from the runtime index that bindings and channels use
(`GetCustomDevice(type, index)`). Two backends that use the same type and index are kept apart (the
second gets "type:namespace:index" and another runtime index, `InputBackend.Namespace`), and a
device that reconnects through a replacement backend is taken over by it.

`DeviceConnected` and `DeviceDisconnected` (and the `DeviceConnectionChanged` signal) report changes;
`GetJoypadDevice(index)` and `GetDevice(id)` look one up; `AssignDevice(player, device)` and
`GetDeviceOwner(device)` work for any device, not only joypads.

### Device layouts

`device.Layout` lists the controls a device offers as `InputControlDescriptor`s: the source path that
reads each (`CreateSource()`), its value kind, range, nature and unit, and hints for tools —
rebindable, noisy (sticks at rest, sensors), tracked (poses, gaze). Built in: gamepads (with what the
family adds: touchpad, back buttons, motion sensors), the keyboard and mouse, touch and pens.
Backends describe their devices by overriding `InputBackend.DescribeDevice` (the XR and eye-tracker
backends do), so tools and rebinding menus can list controls of hardware they have never heard of.

### Analog sampling

Between ticks, analog channels (axes, pressure, backend and virtual controls) keep their **turning
points**: a trigger squeezed from 0.2 to 0.9 and back to 0.2 within one frame is seen at 0.9, so a 0.5
threshold fires. `player.AnalogSampling` chooses `Extremes` (default), `Latest` (only crossings of zero;
cheapest) or `All` (every sample its own step); `Raw.AnalogBand` (0.02) keeps resting noise from making
steps. Positions, motion sensors and tracked poses always keep their latest value.

### Identity and reconnection

A device's `Id` stays the same when it reconnects, even under another joypad index. A returning pad
is recognised by its **serial number** where the platform reports one (Godot's `serial_number`;
PlayStation and Switch pads usually have one, Xbox pads often do not), and otherwise by its **model**
(`ModelKey`: the USB vendor and product ids, read from Godot or from the GUID, grouped by
[family](#controller-families)). The model ignores how the pad is connected, so a pad that switches
from Bluetooth to a cable — which changes its GUID, and for some Xbox pads its product id — is still
the same pad. Identical pads without serials get ids ending in `#2`, `#3`…

Ownership is kept by identity: each record's `Owner` is the player it belongs to, connected or missing, and `player.GetOwnedDevices()` / `OwnedDeviceIds` list a player's devices by persistent id; `JoypadDevices` holds the indices its connected pads' input is routed by. Ownership follows the pad, not the index: a pad that comes back returns to the player that had it
when it disconnected, under whatever index it gets (`PreviousIndex` shows where it was), and
`PlayerDeviceRegained` fires. A pad of another model that receives a freed index never inherits the
assignment (`JoypadUnassigned` tells the old owner its claim on that index is gone), but the missing
pad still goes home when it reconnects. `UnassignJoypad` on a missing pad stops it coming back.

**Pads that cannot be told apart.** Without serial numbers, two pads of one model look the same.
When such a pad connects and only one player is missing a pad of that model, it goes to that player.
When several are, `ReconnectWithoutSerial` decides:

| `ReconnectPolicy` | What happens |
|---|---|
| `Automatic` (default) | the pad goes to the player who lost theirs first; `JoypadReconnectAmbiguous(device, candidates, chosen)` fires so the game can ask and correct it |
| `Ask` | no guessing, even with only one candidate: the pad stays unassigned, its input reaches nobody (not even players that take unowned pads), and `JoypadReconnectAmbiguous` fires with `chosen` null |

Either way, `ResolveJoypadReconnect(device, player)` settles it — for example after "Player 2,
press a button on your controller": the pad takes that player's identity and goes to it, and a player
it had been given to by mistake is missing a pad again (`PlayerDeviceLost`).
`GetReconnectCandidates(device)` and `IsJoypadReconnectPending(device)` tell where things stand.
Assigning the pad to one of its candidates with `AssignJoypad` resolves it too.

Every few seconds (`DeviceCheckSeconds`) the registry is compared with Godot's list of pads, so a
missed connection signal fixes itself; being told twice that a pad connected is not a reconnection.

### What the player is using

`player.CurrentDevice` is the device the player last gave deliberate input with — a pad, the keyboard
and mouse, touch, or a backend device (a button or axis pushed past halfway; poses and sensors do not
count). `DeviceSwitched` reports changes; prompts and [input modes](modes-and-accessibility.md)
follow it. When the device in use disconnects, the player falls back to another one it can use
(another pad, then keyboard and mouse, then touch). When a player loses its only pad,
`EnhancedInput.PlayerDeviceLost(player, deviceId)` fires — pause and show "reconnect your
controller" — and `PlayerDeviceRegained` when it is back.

### Controller families

Each pad gets a `Family` from its name and USB vendor and product ids (`ControllerDetection`):

| Family | Recognised as |
|---|---|
| `Xbox360`, `XboxOne`, `XboxSeries`, `XboxElite` | Xbox pads; Elite with back paddles |
| `PlayStation3`, `PlayStation4`, `PlayStation5`, `PlayStation5Edge` | DualShock 3/4, DualSense, DualSense Edge |
| `SwitchPro`, `JoyConLeft`, `JoyConRight`, `JoyConPair`, `NintendoOther` | Nintendo pads |
| `SteamDeck`, `SteamController`, `SteamVirtual` | Valve hardware and Steam Input's virtual pad |
| `WebStandard` | a pad the browser maps to the standard layout (web exports) |
| `Generic`, `Unknown` | anything else / not yet known |

`device.DisplayName` ("DualSense", "Steam Deck"), `device.Style` (the prompt style: Steam Deck pads
show L1/R1, L4/L5/R4/R5 for the back buttons), `device.HardwareFeatures` (what the hardware has:
`Rumble`, `TriggerRumble`, `AdaptiveTriggers`, `Light`, `MotionSensors`, `Touchpad`, `BackButtons`,
`Trackpads`, `Microphone`, `ShareButton`, `HDHaptics`) and `device.Features` (what Godot can use on this
platform now) follow from it. Modes can require features and roles (`InputMode.RequiredFeatures`,
`RequiredRoles` — e.g. a wheel **and** pedals as separate devices).

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

### XR

`XRInputBackend` follows Godot's XR trackers (VR, AR and mixed reality alike) and makes each a device of
type `xr`:

| Tracker | Controls |
|---|---|
| Controllers and the head | every button, trigger and stick by its XR name (`trigger`, `grip`, `primary`, `ax_button`…); each pose (`default`, `aim`, `grip`, `palm`, `eye_gaze_pose`) as `<pose>/position`, `<pose>/orientation` (quaternion), `<pose>/rotation` (Euler), `<pose>/linear_velocity`, `<pose>/angular_velocity` |
| Hands (hand tracking) | `hand/tracked`, `hand/<joint>/position` and `/orientation` for wrist, palm and fingertips, `hand/pinch` and `hand/grab` (0…1; ranges in `PinchRange`, `GrabRange`) |
| Face | `eyes/gaze` (−1…1, right and down positive), `eyes/closed_left`, `eyes/closed_right`, `eyes/wide`, `jaw_open` |

```csharp
EnhancedInput.Instance.AddBackend(new XRInputBackend());
// aim: a Quaternion action bound to @xr:aim/orientation
var rotation = player.GetValue(aim).AsQuaternion();
```

Haptics played on an XR controller go to that hand only, at the effect's `Frequency`.

### Eye trackers

Godot has no desktop eye-tracker API, so a few lines of adapter around the vendor SDK (Tobii and
similar) implement `IEyeTracker` (`IsAvailable`, `TryGetGaze`, `IsUserPresent`, `TryGetHead`), and
`EyeTrackerBackend` does the rest: a device of type `eye` with `gaze` (0…1 screen),
`gaze_pixels`, `gaze_centered` (−1…1), `gaze_valid`, `present`, `head/position`,
`head/orientation` and `head/rotation`. The device disconnects while the tracker is unavailable.
`MouseGazeSimulator` follows the mouse, for building gaze features without the hardware. Pair
`@eye:gaze` with a [`DwellTrigger`](triggers.md) for look-to-select.

### Tracked values

Poses, tracked positions and gaze are **tracked** controls: while they have a value they are input,
even at the origin or the identity rotation, and when tracking is lost they have no value at all
rather than "zero". Backends set them with `SetTrackedControl` and remove them with `ClearControl`
(the XR and eye-tracker backends do both). An action reading one is triggered while it is tracked
(`ActionState.IsTracked`), ends canceled with `InputEndReason.TrackingLost` when tracking is lost,
and `player.GetSample(action)` gives its value with `IsTracked`, `IsValid` and when it last changed:

```csharp
var sample = player.GetSample(aim);
if (sample.IsValid) hand.Quaternion = sample.Value.AsQuaternion();   // identity is a real reading
```

### Haptics and lights

```csharp
player.Rumble(weak: 0.3f, strong: 0.8f, seconds: 0.25f);   // every pad the player owns
player.StopRumble();
player.SetJoypadLight(Colors.Red);
player.VibrateHandheld(200);                               // phones and tablets

var engine = player.Haptics.Play(HapticEffect.Engine(0.4f), loop: true);
player.Haptics.Play(HapticEffect.Explosion());             // mixes with the engine
engine.Strength = 0.8f;                                    // follow the revs
player.Haptics.Stop(engine);
player.BindHaptic(fire, ActionEvents.Triggered, HapticEffect.Click());
```

A `HapticEffect` resource describes a pattern: `Weak` and `Strong` motor strengths, `Duration`,
`Attack` and `Release`, an optional `Envelope` curve, `Repeats` with a `Gap`, `Loop`, a `Frequency`
(XR controllers), `TriggerLeft` / `TriggerRight` (trigger motors, for outputs that support them) and
whether it also buzzes the handheld device. Presets: `Tap`, `Click`, `Bump`, `Thud`, `Heartbeat`,
`Explosion`, `Engine`, `Constant`. Effects play per device and end on their own; several on one device
mix per motor by their `Blend` — `Max` (default: the strongest wins), `Add`, or `Override`
(silences lower `Priority` effects while it plays). `PlayOn(deviceId, …)` targets one device;
effects only ever reach devices the player owns or may use.

Everything scales by the player's `vibration_strength` setting, running effects included (0 turns
haptics off; see [modes and accessibility](modes-and-accessibility.md)). Backends receive rumble for
their devices. Swap `HapticsOutput` for a test double or another output.

Haptics follow ownership: an effect aimed at a device stops when the device goes to another player,
and the old owner stops its motors at the moment of the transfer (before the new owner commands
them) and never commands them again. `StopRumble(device)` stops what is aimed at one pad and leaves
it out of effects playing on all the player's pads; `StopRumbleOn(deviceId)` does the same for a
backend device. Motor commands are repeated every `HapticRefreshSeconds`, each lasting a little
longer, so motors stop on their own if the game stalls; small changes are sent once they add up.
Lights only go to connected pads that have one. Handheld (phone) vibration is a one-off buzz per
effect of finite length, not part of the per-motor mix.

**Trigger rumble and adaptive triggers are different things.** `HapticEffect.TriggerLeft/Right` drive
trigger motors (Xbox impulse triggers) and go only to pads whose hardware has
`ControllerFeatures.TriggerRumble`. `player.SetTriggerResistance(side, TriggerResistance.Weapon(…))`
sets adaptive trigger resistance (DualSense: steady, weapon-click or vibrating) on pads with
`ControllerFeatures.AdaptiveTriggers`, through an output that implements `IAdaptiveTriggerOutput`
(Godot has no API for either; plug in the platform's).

### Force feedback

For wheels: `player.SetForceFeedback(new ForceFeedback { Spring = 0.5f, Damper = 0.2f, Constant = slip })`
each physics step (or `ForceFeedback.Centering()`, `ForceFeedback.None` to release). It reaches the
player's backend devices (`InputBackend.SetForceFeedback`) and pads whose `HapticsOutput` implements
`IForceFeedbackOutput`, scaled by the `force_feedback_strength` setting.
