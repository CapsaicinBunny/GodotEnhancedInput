# Modifiers

A modifier changes a value. Put modifiers on an `ActionBinding` to change one source's value
before that binding's triggers, or on a `GameAction` to change the combined value before the
action's triggers. They run in list order; a disabled modifier (`Enabled` off) passes values
through. Modifiers that remember earlier ticks keep that memory per binding and per player, so
one modifier resource can be shared.

| Modifier | What it does | Settings |
|---|---|---|
| `DeadzoneModifier` | Below `Lower` → 0; between `Lower` and `Upper` → stretched to 0…1; above `Upper` → 1. **Axial** works per component and keeps each sign (good for 1D axes and triggers); **Radial** works on the vector length and keeps the direction (good for sticks). | `Shape`, `Lower` (0.2), `Upper` (1) |
| `ScaleModifier` | Multiplies per component. Sensitivity, axis weighting. | `Scale` |
| `DeltaTimeScaleModifier` | Multiplies by the tick's delta time: turns a rate into a per-tick amount. | — |
| `NegateModifier` | Flips the sign of the chosen components. | `X`, `Y`, `Z` |
| `SwizzleModifier` | Reorders components (`XYZ`, `YXZ`, `XZY`, `ZYX`, `YZX`, `ZXY`). A key feeds X; `YXZ` puts it on Y. | `Order` |
| `SmoothingModifier` | **Average** of the last N ticks, or **Exponential** follow at a rate per second. | `Mode`, `Samples`, `Rate` |
| `ExponentCurveModifier` | `sign(v)·|v|^e` per component: >1 for fine control near the centre, <1 to boost small inputs. | `Exponent` |
| `CurveModifier` | Remaps each component through a Godot `Curve`, sampled at the magnitude with the sign kept (or at the signed value when `Symmetric` is off). | `CurveX/Y/Z`, `Symmetric` |
| `NormalizeModifier` | Scales to unit length; very short vectors become zero. With `OnlyIfLonger`, only shortens vectors longer than 1 (stops fast diagonals without changing analog magnitudes). | `MinLength`, `OnlyIfLonger` |
| `ClampModifier` | Clamps each component to a range. | `Min`, `Max` |
| `WorldAxesModifier` | Places a 2D input in 3D: `GroundXZ` maps (x, y) → (x, 0, y), so up on a stick (−Y) becomes −Z, Godot's forward; `UprightXY` maps to (x, −y, 0). Use on an `Axis3D` action. | `Plane` |
| `ViewRelativeModifier` | Rotates a world direction by the player's view, so forward is "away from the camera". `YawOnly` uses only the heading (stays on the ground). The view is `InputPlayer.ViewCamera`, the viewport's current `Camera3D`, or `ViewBasisOverride`. | `YawOnly` |
| `FovScaleModifier` | Multiplies by `tan(fov/2) / tan(reference/2)` of the view camera, so aiming feels the same when zoomed. | `ReferenceFov` (75°) |

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

**Mouse look:** `MouseMotion` → `ScaleModifier` (sensitivity), optionally `NegateModifier` (Y) and
`FovScaleModifier`. Stick look: `JoyStick` Right → radial deadzone → `ExponentCurveModifier` →
`DeltaTimeScaleModifier` → `ScaleModifier` (degrees per second).

## Writing your own

```csharp
[Tool, GlobalClass]
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

`context` gives the action's value kind, the tick's delta, the player and a `ModifierMemory` for
anything that must survive between ticks (never store it in fields of the resource).
