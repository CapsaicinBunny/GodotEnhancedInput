# Modes, settings and accessibility

## Input modes

An `InputMode` is a way of playing — "keyboard and mouse", "gamepad", "touch", "wheel" — with the
bindings that belong to it. Give bindings **groups** (`ActionBinding.Groups`, or
`binding.InGroups("pad")`) and give each mode the groups it uses; a binding without groups works in
every mode.

```csharp
var keyboard = InputMode.Create("keyboard", DeviceKinds.KeyboardMouse, new[] { "kbm" }, switchOn: DeviceKinds.KeyboardMouse);
var gamepad  = InputMode.Create("gamepad",  DeviceKinds.Joypad,        new[] { "pad" }, switchOn: DeviceKinds.Joypad);
player.Modes.Add(keyboard);
player.Modes.Add(gamepad);
```

| `InputMode` field | Meaning |
|---|---|
| `Groups` | binding groups evaluated in this mode |
| `RequiredDevices`, `OptionalDevices` | device kinds the mode needs / can use |
| `RequiredRoles` | roles that must each be a separate device, e.g. `Wheel` and `Pedals`, or `FlightStick` and `Throttle` |
| `RequiredFeatures` | capabilities some usable device must have, e.g. `MotionSensors` for gyro aiming |

`player.RequiredDevicesAvailable(mode)` checks all three against the devices the player can use
right now — connected, and its own or unowned (`player.GetUsableDevices()`); a mode whose
requirements are not met is never chosen automatically.
| `SwitchOn` | using one of these device kinds switches to this mode |
| `SettingDefaults` | setting values for this mode when the profile has none (e.g. a higher `sensitivity` for sticks) |

With `AutoSwitchModes` (on by default) the player switches to the first mode whose `SwitchOn` lists
the device kind just used; `ModeChanged` reports it and prompts follow. `LockMode(mode)` keeps a mode
the player chose in a menu; `UnlockMode()` lets devices switch again, starting from the device used
last. With no `ActiveMode`, every binding is evaluated.

## Player settings

Settings are named numbers and yes/no flags stored in the player's profile, so they are saved and
loaded with the rebinding choices:

```csharp
player.Profile.SetSetting("mouse_sensitivity", 1.4);
player.Profile.SetFlag("invert_look_y", true);
double s = player.GetSetting("mouse_sensitivity", 1.0);   // profile → active mode's default → fallback
```

Built-in pieces that read settings:

| Setting | Read by |
|---|---|
| any name you choose | `SensitivityModifier.Setting`, `InvertXSetting`, `InvertYSetting`; `DeadzoneModifier.LowerSetting` |
| `hold_time_scale` | hold and hold-release durations, the delay before the first repeat |
| `tap_time_scale` | tap and multi-tap windows |
| `repeat_time_scale` | pulse and repeat intervals |
| `vibration_strength` | rumble and handheld vibration (0…1) |
| `toggle:<action name>` | hold-to-toggle for that action |

## Accessibility

- **Hold to toggle**: `player.SetHoldToToggle(aim, true)` turns any held action into press-on,
  press-off — whatever its bindings and triggers. Stored as `toggle:<action name>`.
- **Timing**: the time scales above make taps more forgiving and holds shorter without touching
  the triggers.
- **Vibration**: `vibration_strength` 0 turns all haptics off.
- **Remapping everything**, including composite parts and modifier keys ([rebinding](rebinding.md)).
- **Bigger, movable, fainter touch controls**: [touch layouts](players-and-touch.md#touch-layouts).

Ready-made presets to apply to a profile:

```csharp
player.Profile.Apply(AccessibilityPresets.RelaxedTiming());        // taps ×2, holds ×0.5, repeats slower
player.Profile.Apply(AccessibilityPresets.NoVibration());
player.Profile.Apply(AccessibilityPresets.HoldToToggle(aim, sprint));
```

`Apply(preset, replace: true)` replaces the profile's settings instead of merging them.
