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
  mode, keeps running while paused, leaves presses to a focused GUI, and never marks events handled
  except a press captured for rebinding.
- `InputEventAction` drives game actions with the same name; InputMap sources follow both
  `InputEventAction` and `Input.ActionPress`.
- Rebinding: `ActionBinding` rebind id, display name, category and former ids; per-player
  `InputProfile` overrides; rebind slots from active and registered contexts; clear and reset;
  conflict detection (same context or all) with allow / reject / swap / clear policies;
  `StartCapture` binds the next key, mouse, joypad button or axis press (the captured press is
  swallowed and marked handled); `InputSource.FromEvent` and `Overlaps`; half-axis joypad sources;
  JSON profiles (format 1) with enums by name, unknown ids kept, former ids migrated, malformed
  entries skipped and newer formats refused.
- Local players: exclusive joypad assignment (`AssignJoypad`, `UnassignJoypad`, `GetJoypadOwner`);
  unowned joypads reach players without a list; join mode on button press with `MaxPlayers` and a
  consumed joining press; `JoypadAssigned`, `PlayerJoined` and `JoypadConnectionChanged` signals;
  assignments survive disconnects; new players listen to no keyboard, mouse, touch or
  `InputEventAction` by default.
- Touch: `Touch` sources (pressed / position / drag, one finger or any) with GUI-aware intake;
  `AddToVirtualControl` for one-tick deltas; on-screen `VirtualJoystick`, `VirtualButton` and
  `TouchZone` controls (multi-touch, optional mouse, touchscreen-only visibility).
- Demo scene with saved actions and contexts, a rebinding panel in its pause menu, joining with more
  joypads, and on-screen touch controls.
- Prompts: `InputPrompts` names for every source kind (physical keys as the layout labels them,
  joypads in generic / Xbox-like / PlayStation-like / Nintendo-like styles detected from the pad's
  name) and stable icon keys; `InputPlayer` tracks the last used device (`InputDeviceChanged`) and
  lists an action's prompts (`GetPrompts`, `GetPromptText`) with rebinds and chords;
  `PromptIconSet` resource with styled keys, fallbacks and BBCode output.
- Debugging: `InputPlayer.DescribeState` / `EnhancedInput.DescribeState` text dumps and the
  `InputDebugOverlay` (F3) with recent events.
- Documentation: design, concepts, modifiers, triggers, rebinding, players and touch, prompts and
  debugging, Godot integration, migration.
- 235 headless tests.

### Removed
- InputForge's `EnhancedInputSystem`, `InputAction`, `InputMappingContext`, `InputMapping`, `InputKey`,
  `ContextualInputEvent`, its triggers and `PointerSpaceModifier`.
