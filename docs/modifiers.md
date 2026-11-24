# Modifiers

A modifier changes a value. Put modifiers on an `ActionBinding` to change one source's value
before that binding's triggers, or on an `EnhancedInputAction` to change the combined value before the
action's triggers. They run in list order; a disabled modifier (`Enabled` off) passes values
through. Modifiers that remember earlier ticks keep that memory per binding and per player, so
one modifier resource can be shared.

| Modifier | What it does | Settings |
|---|---|---|
| `DeadzoneModifier` | Below `Lower` → 0; between `Lower` and `Upper` → stretched to 0…1; above `Upper` → 1. **Axial** works per component and keeps each sign (good for 1D axes and triggers); **Radial** works on the vector length and keeps the direction (good for sticks). `LowerSetting` lets the player tune `Lower`. | `Shape`, `Lower` (0.2), `Upper` (1), `LowerSetting` |
| `ScaleModifier` | Multiplies per component. Sensitivity, axis weighting. | `Scale` |
| `DeltaTimeScaleModifier` | Multiplies by the tick's delta time: turns a rate into a per-tick amount. Delta sources (mouse motion, drags) are left alone — they already are per tick. | `ScaleDeltaValues` |
| `NegateModifier` | Flips the sign of the chosen components. | `X`, `Y`, `Z` |
| `SwizzleModifier` | Reorders components (`XYZ`, `YXZ`, `XZY`, `ZYX`, `YZX`, `ZXY`). A key feeds X; `YXZ` puts it on Y. | `Order` |
| `SmoothingModifier` | **Average** of the last N ticks, or **Exponential** follow at a rate per second. | `Mode`, `Samples`, `Rate` |
| `ExponentCurveModifier` | `sign(v)·|v|^e` per component: >1 for fine control near the centre, <1 to boost small inputs. | `Exponent` |
| `CurveModifier` | Remaps each component through a Godot `Curve`, sampled at the magnitude with the sign kept (or at the signed value when `Symmetric` is off). | `CurveX/Y/Z`, `Symmetric` |
| `NormalizeModifier` | Scales to unit length; very short vectors become zero. With `OnlyIfLonger`, only shortens vectors longer than 1 (stops fast diagonals without changing analog magnitudes). | `MinLength`, `OnlyIfLonger` |
| `ClampModifier` | Clamps each component to a range. | `Min`, `Max` |
| `WorldAxesModifier` | Places a 2D input in 3D: `GroundXZ` maps (x, y) → (x, 0, y), so up on a stick (−Y) becomes −Z, Godot's forward; `UprightXY` maps to (x, −y, 0). Use on an `Axis3D` action. | `Plane` |
| `ViewRelativeModifier` | Rotates a world direction by the player's view, so forward is "away from the camera". `YawOnly` uses only the heading (stays on the ground). The view is `InputPlayer.ViewCamera`, the viewport's current `Camera3D`, or `ViewBasisOverride`. | `YawOnly` |
| `PointerSpaceModifier` | Converts a pointer position (viewport pixels, from `MousePosition` or a touch position) to 0…1 viewport coordinates, −1…1 centred coordinates, 0…1 within a rect, or 2D world coordinates through the viewport's canvas transform. Uses the player's `PointerViewport` (its own by default) for split screen and SubViewports. | `Space`, `Rect` |
| `FovScaleModifier` | Multiplies by `tan(fov/2) / tan(reference/2)` of the view camera, so aiming feels the same when zoomed. | `ReferenceFov` (75°) |
| `RemapModifier` | Maps each component linearly from an input range to an output range (a pedal resting at −1 onto 0…1, a throttle onto 0…100), optionally clamped. | `InputMin`, `InputMax`, `OutputMin`, `OutputMax`, `Clamp` |
| `CalibrationModifier` | Maps measured min / centre / max readings to −1…0…1 per component, for worn sticks, wheels and pedals. | `Min`, `Center`, `Max` |
| `SensitivityModifier` | Scales by a sensitivity the player chose (a profile setting) and optionally inverts X or Y by yes/no settings. Use one setting per device group. | `Setting`, `Scale`, `InvertXSetting`, `InvertYSetting` |
| `AccelerationModifier` | Pointer acceleration: gain `1 + Acceleration · speed^Exponent`, capped at `MaxGain`; slow movement stays 1:1. | `Acceleration`, `Exponent`, `MaxGain` |
| `AntiDeadzoneModifier` | Lifts small non-zero values to `Minimum` and scales the rest into `Minimum…1`, to cancel a deadzone applied later. | `Shape`, `Minimum` |
| `QuantizeModifier` | Rounds to steps (a throttle in 10 % notches) or a direction to the nearest of N (4- or 8-way sticks). | `Mode`, `Step`, `Directions` |
| `HysteresisModifier` | Holds the output still until the input moves more than `Band` away — removes jitter without smoothing lag. | `Band` |
| `IntegrateModifier` | Adds the value up into a position (mouse motion into an aim offset, a stick into a throttle that stays put), clamped, optionally returning to zero. | `Min`, `Max`, `PerSecond`, `ReturnRate` |
| `DifferentiateModifier` | Outputs the change since the previous tick (a tracked position into movement), optionally per second. | `PerSecond` |
| `DpiScaleModifier` | Multiplies by `ReferenceDpi / screen DPI`, so the same hand movement turns the camera equally on any screen. `InputPlayer.ScreenDpiOverride` wins when set. | `ReferenceDpi` |
| `GyroCalibrationModifier` | Learns a gyroscope's resting bias while the device is still and subtracts it, removing drift. Put it first on a gyroscope binding. | `StillThreshold`, `LearnRate` |

Modifiers know the **nature** of what they receive (`context.Nature`: state, delta or absolute; see
[sources](sources-and-devices.md#value-natures)), so the same modifier can treat a held stick and a
mouse movement correctly.

## Rotations

| Modifier | What it does | Settings |
|---|---|---|
| `RotationConvertModifier` | Converts Euler angles (radians) ↔ quaternion, degrees → radians, rotation vector (axis × angle, as gyroscopes give) ↔ quaternion. Lets any rotation source drive any rotation action. | `Conversion` |
| `RotationOffsetModifier` | Turns a quaternion by a fixed offset (a controller's grip angle, a sensor mounted sideways), applied in the rotation's own frame or before it; the offset can come from player settings (`<prefix>_x/_y/_z`, degrees) for a "recenter" option. | `OffsetDegrees`, `Local`, `SettingPrefix` |
| `RotationSmoothingModifier` | Follows a quaternion along the shortest arc at `Rate` per second, frame-rate independent; per-component smoothing would bend the rotation. | `Rate` |

`DeadzoneModifier`, `ScaleModifier`, `NegateModifier` and `NormalizeModifier` understand rotations: on a
`Quaternion` action they work on the rotation angle instead of on the four raw numbers: a deadzone
ignores small rotations (its `Lower` is a fraction of half a turn, so 0.03 is about 5°), a scale of 0.5
halves the turn, negate inverts it, normalize keeps it a unit quaternion.

## Recipes

**Four keys as four bindings** (instead of a `KeyAxis2D` source), with `Sum` accumulation:

| Key | Modifiers |
|---|---|
| D | — |
| A | Negate |
| S | Swizzle `YXZ` |
| W | Swizzle `YXZ`, Negate |

**Camera-relative movement on a 3D action:** stick → `DeadzoneModifier` (radial) on the binding;
`WorldAxesModifier` then `ViewRelativeModifier` on the action.

**Mouse look:** `MouseMotion` → `SensitivityModifier` (`mouse_sensitivity`, `invert_look_y`), optionally
`DpiScaleModifier`, `AccelerationModifier` and `FovScaleModifier`. Stick look: `JoyStick` Right → radial deadzone → `ExponentCurveModifier` →
`DeltaTimeScaleModifier` → `SensitivityModifier` (`stick_sensitivity`, degrees per second as `Scale`).

**Gyro aiming:** `pad:gyroscope` → `GyroCalibrationModifier` → `HysteresisModifier` → `SensitivityModifier`.

**Pedal:** `pad:axis2` → `CalibrationModifier` (or a `DeviceProfile` calibration) → `RemapModifier` −1…1 → 0…1.

## Writing your own

```csharp
[Tool]
public partial class SnapToEightModifier : InputModifier
{
    public override Vector3 Apply(Vector3 value, in ModifierContext context)
    {
        var v = new Vector2(value.X, value.Y);
        if (v == Vector2.Zero) return value;
        float angle = Mathf.Snapped(v.Angle(), Mathf.Pi / 4f);
        return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * v.Length();
    }
}
```

Mark it `[Tool]` and it appears in the Inspector's *Add Modifier…* menu. Leave out `[GlobalClass]`
unless you also want it in *Create New Resource*.

`context` gives the action's value kind, the tick's delta, the player and a `ModifierMemory` for
anything that must survive between ticks (never store it in fields of the resource).
