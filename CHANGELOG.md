# Changelog

All notable changes to Godot Enhanced Input are documented here. Versions follow
[Semantic Versioning](https://semver.org/); 0.x releases may change the API.

## [0.1.0] — unreleased

First version of Godot Enhanced Input, a redesign of [InputForge](https://github.com/pandoraoyun/InputForge)
0.2.3 (MIT). InputForge's API is replaced; see [docs/migration.md](docs/migration.md). InputForge's own
history up to 0.2.3 is in its repository.

### Added
- Three assets in *Create New Resource*, and only these: `EnhancedInputAction`,
  `EnhancedInputMappingContext` and `EnhancedInputConfig`. Mappings, input sources, modifiers, triggers,
  control schemes, haptic effects and touch layouts are created inside them from Inspector *Add …*
  menus (undoable), and a mapping's input can be typed as a source path.
- Left and right Shift, Ctrl, Alt and Meta: `InputSource.KeySide` (`key:left_shift`, `key:right_ctrl`;
  `key:shift` is either). Listening and capture record the side pressed; prompts say "Left Shift".
- Editor dock: lists every action file (not only mapped ones) and offers them in the action picker;
  drag mappings to reorder them or move them between mapping contexts; drop action files on a
  mapping or a mapping context; *+ Action* button; config filter; double-clicking an action, mapping
  context or config file shows it in the dock; Inspector edits show up at once.
- `EnhancedInputAction` resources with typed values (digital, 1D, 2D, 3D), value accumulation (strongest or
  sum), *consume input*, *trigger when paused*, and action-level modifiers and triggers.
- `InputSource`: keys (physical by default, or logical / label), two- and four-key composites, mouse
  buttons and wheel, mouse motion and position, joypad buttons, axes and sticks (any device or one),
  Godot InputMap actions, named virtual controls.
- `EnhancedInputMappingContext` resources, added per player with an integer priority; per-source consumption by
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
- Robustness (from review): no press is lost between ticks (ordered per-channel change queue);
  suppressed bindings (blocked, paused, consumed, ignored) are reset instead of fed zeros; only
  firing bindings add value (`ActionState.RawValue` keeps the unfiltered value) and action-level
  release / tap / hold-release triggers work; consumption and rebind conflicts keep device and
  finger identity, and logical/label keys consume as their physical key; sticks read one pad;
  only player 0 polls the InputMap or takes unowned pads by default; pad identity checked on
  reconnect (`JoypadUnassigned`); stable `ProfileId`; focus loss cuts off input and on-screen
  controls; a stick reversal releases before the GUI and presses only if the GUI lets it through.
- `ChangedTrigger` and `PointerSpaceModifier` (back from InputForge, reworked).
- Sources: mouse wheel axis (with trackpad pans), pen pressure / tilt / eraser, pad and handheld
  motion sensors, joypad buttons and axes by raw index, backend device controls, gestures (pinch,
  twist, two-finger drag, swipe, trackpad pinch), composites (axes of any sources, held modifiers
  then a value, any-of); value natures; `InputSourcePath` text form for every kind.
- Devices: registry with connect / disconnect events and guessed roles, `AssignDevice` for any
  device, `DeviceProfile` calibration, inversion and names, `InputBackend` for other hardware,
  `XRInputBackend`; rumble, joypad lights and handheld vibration scaled by `vibration_strength`.
- Triggers: multi-tap, toggle, repeat, threshold, hysteresis, flick, distance, predicate; timing
  scale settings. Modifiers: remap, calibration, sensitivity, acceleration, anti-deadzone,
  quantize, hysteresis, integrate, differentiate, DPI scale, gyro calibration; player-tunable
  deadzone.
- Input modes with binding groups, device-driven switching and locking; player settings in the
  profile with mode defaults; hold-to-toggle; accessibility presets.
- Rebinding: composite parts (`RebindPart`, part capture), capture exclusions, filters, chords,
  timeouts and wait-for-release; named profiles, preset import / export, safe (atomic) writes.
- Touch: safe-area placement, `TouchLayout` / `TouchLayoutRoot` with edit mode, scale and opacity.
- Recording and deterministic replay (`StartRecording`, `StartReplay`, `InputRecording` JSON).
- Performance: compiled bindings (`RefreshBindings`, `EnhancedInputMappingContext.NotifyBindingsChanged`),
  allocation-free steady-state ticks, `EmitActionSignals`; nested `Tick` calls are ignored.
- Editor dock and `BindingValidator`; the dock edits bindings in place (action or a new one, input
  path checked as typed or by listening, groups, rebind id, modifier and trigger stacks, with undo),
  lists actions and problems, and inspects connected pads live with an input log.
- CI on Windows, Linux and macOS; platform table, hardware checklist and a supported-features matrix
  (`docs/features.md`).
- Values: `Axis4D`, `Quaternion` and `Euler` actions (rotation-aware accumulation, deadzone, scale,
  negate, normalize); `ActionState.InputValue`; `RotationConvertModifier`, `RotationOffsetModifier`,
  `RotationSmoothingModifier`.
- Haptics: `HapticEffect` resources (envelope, repeats, loop, frequency, trigger motors, priority,
  blend) mixed per device, effect handles, `BindHaptic`; adaptive trigger resistance
  (`SetTriggerResistance`, `IAdaptiveTriggerOutput`) kept apart from trigger rumble; wheel force
  feedback (`SetForceFeedback`).
- Devices: controller families and features (`ControllerDetection`); persistent identity across
  reconnects (serial number, else model — cable and Bluetooth count as the same pad); ownership kept
  by identity (`InputDeviceInfo.Owner`, `GetOwnedDevices`, `OwnedDeviceIds`); doubtful returns of
  identical serial-less pads (`ReconnectPolicy`, `JoypadReconnectAmbiguous`,
  `ResolveJoypadReconnect`); `CurrentDevice`, `DeviceSwitched`, fallback and
  `PlayerDeviceLost` / `PlayerDeviceRegained`; modes requiring roles and features; backend devices
  with persistent ids, namespaces and replacement.
- XR hands (joints, pinch, grab), face (eye gaze, blinks, jaw) and pose orientation as quaternions;
  desktop eye trackers (`EyeTrackerBackend`, `IEyeTracker`, `MouseGazeSimulator`); `DwellTrigger`.
- Tracked values: tracked controls are input while present, even at identity or origin;
  `ActionState.IsTracked`, `InputSample` / `GetSample`; `InputEndReason.TrackingLost`.
- `TickMode.Auto` (default): one tick per frame, before physics and process; `ReleaseStuckInput`.
- Public API: shortcuts on actions (`WasPressed`, `IsTriggered`, `ReadVector2`, `OnTriggered`…) and
  short factories (`EnhancedInputAction.Button`, `Axis2D`…); contexts bound from text paths
  (`Bind(jump, "key:space", "pad:a")`, `wasd`, `arrows`) with binding helpers (`Pressed`, `Hold`, `Tap`,
  `Deadzone`, `Scale`, `Negate`); stable `Id` and `FormerIds` on actions, contexts, bindings, modes and
  configurations; `EnhancedInputConfig` with `LoadConfiguration`, `ActivateContext` and
  `EnhancedInput.Configuration`; event provenance (`Binding`, `Source`, `Device`, `Timestamp`,
  `EndReason`); `ActionEvaluation` policies; `PushContext` handles, `SwitchContext`, `InputScope`,
  `SetActionEnabled`; `InputDeviceLayout` and `InputControlDescriptor` (backends describe theirs).
- `AnalogSampling` and `AnalogBand`: analog turning points within a frame.
- 466 headless tests, including a worst-case benchmark (many events per frame).

### Changed (during review)
- Evaluation: a tick evaluates every input event since the last one in arrival order, letting time
  pass up to each event for the actions it touches; changes are placed by arrival time when ticking
  in real time (`UseEventTimes`, `Clock`). Held actions raise steady events once per frame; one-shots
  stay triggered to the frame's end. (Previously each tick observed one change per channel.)
- Input that is cut off — focus loss, suspension, a lost or reassigned device, a removed context, a
  mode or binding change, pausing, lost tracking, a system-canceled touch, cleared injections — ends
  actions as `Canceled` with `ActionState.EndReason`, never as a release.
- A key held while its binding was hidden counts as held when it comes back: no fresh press.
- Recordings store cut-offs, sensor samples in the frame they belong to, tracked device controls
  and each input's moment within its tick; replays ignore live cut-offs and release what they held
  when they end.
- Haptics follow device ownership; a transfer stops the old owner's motors first, and releases
  trigger resistance and force feedback; so do player removal, focus loss and suspension.
- Tracking gained and lost are queued changes (also at the origin); a backend device assigned to a
  player is cut off for everyone else; input from removed backend devices is ignored.

### Removed
- InputForge's `EnhancedInputSystem`, `InputAction`, `InputMappingContext`, `InputMapping`, `InputKey`,
  `ContextualInputEvent` and its triggers.
