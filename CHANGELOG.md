# Changelog

All notable changes to Godot Enhanced Input are documented here. Versions follow
[Semantic Versioning](https://semver.org/); 0.x releases may change the API.

## [0.1.0] — unreleased

First version of Godot Enhanced Input, a redesign of [InputForge](https://github.com/pandoraoyun/InputForge)
0.2.3 (MIT). InputForge's API is replaced; see [docs/migration.md](docs/migration.md). InputForge's own
history up to 0.2.3 is in its repository.

### Added
- `GameAction` resources with typed values (digital, 1D, 2D, 3D), value accumulation (strongest or
  sum), *consume input*, *trigger when paused*, and action-level modifiers and triggers.
- `InputSource`: keys (physical by default, or logical / label), two- and four-key composites, mouse
  buttons and wheel, mouse motion and position, joypad buttons, axes and sticks (any device or one),
  Godot InputMap actions, named virtual controls.
- `BindingContext` resources, added per player with an integer priority; per-source consumption by
  higher contexts, *block lower priority*, *ignore held sources until released*, identity comparison.
- Modifiers: deadzone (axial / radial, with rescaling), scale, delta-time scale, negate, swizzle,
  smoothing (average / exponential), exponent curve, `Curve` remap, normalize, clamp, world axes,
  view-relative rotation, field-of-view scaling.
- Triggers: down, pressed, released, hold (optionally one-shot), hold-and-release, tap, pulse, chord,
  chord blocker, combo; explicit / implicit / blocker roles; per-trigger actuation threshold.
- `InputPlayer`: tick-driven evaluation; phase state machine raising started, ongoing, triggered,
  completed and canceled with elapsed and triggered time; C# callbacks with disposable handles,
  polling, a stream of all events, and Godot signals; injected input (one tick or continuous).
- `EnhancedInput` autoload: routes events to players by device, ticks in process, physics or manual
  mode, keeps running while paused, never marks events handled, and leaves presses to a focused GUI.
- `InputEventAction` drives game actions with the same name; InputMap sources follow both
  `InputEventAction` and `Input.ActionPress`.
- Demo scene with saved actions and contexts; design, concepts, modifier, trigger, integration and
  migration documentation; 141 headless tests.

### Removed
- InputForge's `EnhancedInputSystem`, `InputAction`, `InputMappingContext`, `InputMapping`, `InputKey`,
  `ContextualInputEvent`, its triggers and `PointerSpaceModifier`.
