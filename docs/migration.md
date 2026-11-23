# Migrating from InputForge

Godot Enhanced Input 0.1.0 is a redesign; InputForge's API is gone. The ideas carry over, the names
and behaviour change.

| InputForge | Godot Enhanced Input | Notes |
|---|---|---|
| namespace `InputForge`, `addons/input_forge` | namespace `GodotEnhancedInput`, `addons/godot_enhanced_input` | Disable the old plugin, delete its folder, enable the new one. |
| `EnhancedInputSystem` autoload | `EnhancedInput` autoload + `InputPlayer` | Contexts, bindings and events live on the player: `EnhancedInput.DefaultPlayer`. |
| `InputAction` (equal by name) | `GameAction` (equal by identity) | New: `ValueKind`, `Accumulation`, `ConsumeInput`, `TriggerWhenPaused`, action-level modifiers and triggers. |
| `InputMappingContext` (equal by name) | `BindingContext` (equal by identity) | `Mappings` → `Bindings`. |
| `InputMapping` | `ActionBinding` | `TargetAction` → `Action`, `InputSource` → `Source`, `LocalModifiers`/`LocalTriggers` → `Modifiers`/`Triggers`. |
| `InputKey` | `InputSource` | `InputType`/`DeviceType` → `Kind`. `Boolean` → `Key`/`MouseButton`/`JoyButton`; `Digital` → `KeyAxis1D`/`KeyAxis2D` (Y up is now −Y, Godot's convention); `Analog` → `JoyAxis`/`JoyStick`; `Delta` → `MouseMotion` (sensitivity is now a `ScaleModifier`); `Pointer` → `MousePosition`. Keys match physical positions. |
| `AddContext(ctx)` (last added wins) | `AddContext(ctx, priority, options)` | Explicit priorities; `DuplicateContextBehavior` is gone (re-adding updates the priority). |
| `PreventFallbackContext` | `BindingContext.BlockLowerPriority` | Per context instead of global. |
| context consumes the *event* when it fired | actions consume their *sources* while their context is active | See [concepts](concepts.md#binding-contexts). |
| `BindAction(action, Action<bool/float/Vector2/Vector3>)` | `player.Bind(action, ActionEvents…, e => …)` | One callback shape; read `e.Value.AsBool()/AsFloat()/AsVector2()/AsVector3()`. Returns a disposable handle. |
| `ContextualInputEvent` | `ActionEventArgs` | Carries event, value, phase and timings. |
| `TriggerOnKeyDown` | `PressedTrigger` | |
| `TriggerOnKeyUp` | `ReleasedTrigger` | Now also ongoing while held. |
| `TriggerOnChange` (default for axes) | no trigger | Axes are triggered while non-zero and complete when they return to zero. |
| `TriggerContinuous` | `DownTrigger`; pulse mode → `PulseTrigger` | Pulses now run on ticks, not events. |
| `InvertModifier` | `NegateModifier` | |
| `DeadzoneModifier` (cut-off) | `DeadzoneModifier` (cut-off and rescale, axial or radial) | |
| `NormalizeModifier`, `ScaleModifier`, `SwizzleModifier` | same names | Swizzle gained `XYZ`; settings renamed slightly. |
| `PointerSpaceModifier` | — | Convert positions in game code, or write a custom modifier. |
| `ActiveContextChanged`, `ContextPushed`, `ContextPopped` signals | `ContextAdded`, `ContextRemoved` on the player | |
| `Pushed`, `Popped`, `PriorityChanged` on contexts | — | Contexts are plain data; listen on the player. |

Behaviour that changed on purpose:

- **Ticks, not events.** Evaluation runs once per tick, so held and timed triggers work while
  nothing new arrives.
- **No event is marked handled**, so the GUI and other nodes keep their input.
- **`InputEventAction` and the InputMap** drive actions.
- **Resources hold no runtime state**; one resource can serve many bindings and players.
