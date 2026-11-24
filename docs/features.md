# Supported features

What Godot Enhanced Input 0.1.0 does, how far each part is checked, and what still needs a person with
the hardware. Every "tested" entry has headless tests that run in CI on Windows, Linux and macOS (the
requirement IDs link the [design](design.md) to those tests).

| Mark | Meaning |
|---|---|
| ✅ | Implemented and covered by headless tests |
| 🔌 | Implemented; needs a check on real hardware (see the [checklist](platforms-and-hardware.md#hardware-checklist)) |
| ⚙️ | Implemented as an interface; needs a platform or vendor plug-in (Godot has no API for it) |
| — | Not included |

## Actions and evaluation

| Feature | Status | Notes |
|---|---|---|
| Typed actions: digital, 1D, 2D, 3D, 4D, quaternion, Euler | ✅ | ACT-1, VAL-1…3 |
| Accumulation of several bindings (strongest, sum; rotations composed) | ✅ | |
| Binding contexts: priority, per-channel consumption, blocking, ignore-held-until-released | ✅ | CTX-* |
| A key held while hidden counts as held when it returns (no fresh press) | ✅ | EVT-4 |
| 28 modifiers incl. rotation conversion, offset and smoothing | ✅ | [Modifiers](modifiers.md) |
| 20 triggers incl. chords, combos, tap/hold, multi-tap, toggle, dwell | ✅ | [Triggers](triggers.md) |
| Phases and events (started, ongoing, triggered, completed, canceled) with timings | ✅ | |
| Every input event evaluated in order within its frame; time split at each change | ✅ | EVT-1, EVT-3 |
| Held actions fire once per frame; one-shots stay triggered to the frame's end | ✅ | EVT-2 |
| Evaluation policies: continuous, on change, event-driven, pass-through, tracked | ✅ | API-5 |
| Analog turning points within a frame (thresholds crossed and recrossed) | ✅ | ANA-1 |
| Events carry binding, exact channel, device, timestamp, end reason | ✅ | API-4 |
| Shortcuts on actions, text-path bindings, binding helpers | ✅ | API-1 |
| Stable ids with former ids; input configurations; contexts by id | ✅ | API-2, API-3 |
| Context handles, scopes tied to nodes, per-action enable | ✅ | API-6 |
| Arrival times within a frame | ✅ / 🔌 | Godot gives events no timestamps; times are arrival times, as fine as Godot delivers events (agile event flushing helps) |
| Cut-off input ends canceled with a reason (focus, suspend, disconnect, reassign, context, mode, rebind, pause, tracking, system) | ✅ | CUT-1…3 |
| Stuck keys released when Godot no longer holds them | ✅ | CUT-2 |
| Callbacks, polling, event stream, Godot signals, injected input | ✅ | |
| Auto, process, physics and manual ticking | ✅ | GD-2 |
| Allocation-free steady ticks; worst case 32 events/frame over 1000 bindings ≈ 2.5 ms (debug) | ✅ | PRF-1…3 |

## Sources and devices

| Feature | Status | Notes |
|---|---|---|
| Keys (physical, logical, label), mouse buttons, wheel, motion, position | ✅ / 🔌 | Non-QWERTY layouts and high-DPI mice need a check |
| Joypad buttons, axes, sticks, raw indices, half axes | ✅ / 🔌 | |
| Composites, text paths, InputMap actions, `InputEventAction`, virtual controls | ✅ | SRC-* |
| Pen pressure, tilt, eraser | ✅ / 🔌 | |
| Motion sensors (pads, handheld) | ✅ / 🔌 | What Godot reports varies by pad and OS |
| Device registry, roles, device profiles (calibration, inversion, names) | ✅ | DEV-1…2 |
| Persistent identity across reconnects (serial, else model), cable ↔ Bluetooth, ownership by identity | ✅ / 🔌 | DEV-5…9; which pads report serials needs a check |
| Doubtful returns of identical serial-less pads: automatic or ask | ✅ | DEV-8 |
| Device switching with fallback; lost/regained signals | ✅ | DEV-6 |
| Controller families (Xbox, Elite, DualShock, DualSense, Edge, Switch Pro, Joy-Con, Steam Deck, Steam Input) | ✅ / 🔌 | FAM-1; detection by name and USB ids |
| Backend devices with persistent ids, namespaces, replacement | ✅ | BKD-1, BKD-2 |
| Device layouts: control descriptors (path, kind, range, nature, unit, noisy, tracked) | ✅ | API-7 |
| Wheels, pedals, flight sticks, throttles (through profiles or backends) | ✅ / 🔌 | |
| XR controllers and head: poses as position, quaternion, Euler, velocities | ✅ / 🔌 | Simulated trackers in tests |
| XR hand tracking (joints, pinch, grab) and face (eye gaze, blinks, jaw) | ✅ / 🔌 | |
| Tracked values: identity/origin are readings; tracking loss cancels; `GetSample` | ✅ | TRK-1 |
| Desktop eye trackers (Tobii and similar) | ⚙️ | `IEyeTracker` adapter around the vendor SDK; a mouse simulator is included |

## Output

| Feature | Status | Notes |
|---|---|---|
| Rumble, haptic effects (envelopes, repeats, loops), mixing and priorities | ✅ / 🔌 | HAP-1…2 |
| Haptics follow ownership; transfers stop the old owner first; resistance and forces released too | ✅ | HAP-2, HAP-3 |
| Joypad lights (capable pads only) | ✅ / 🔌 | |
| Handheld (phone) vibration | ✅ / 🔌 | One buzz per finite effect, not part of the motor mix |
| XR per-hand vibration with frequency | ✅ / 🔌 | |
| Trigger rumble (impulse triggers) | ⚙️ | `IHapticsOutput.StartTriggers`; only to pads with trigger motors |
| Adaptive trigger resistance (DualSense) | ⚙️ | `IAdaptiveTriggerOutput`; only to pads with adaptive triggers |
| Wheel force feedback | ⚙️ | `InputBackend.SetForceFeedback` or `IForceFeedbackOutput` |

## Players, touch, rebinding, prompts, tools

| Feature | Status | Notes |
|---|---|---|
| Local multiplayer: joypad assignment, joining, per-player contexts and profiles | ✅ / 🔌 | PLR-* |
| Touch sources, gestures, on-screen stick, buttons and drag zone, safe area, editable layouts | ✅ / 🔌 | Needs a real touchscreen |
| Rebinding: slots, conflicts, capture, composite parts, named profiles, presets, safe writes | ✅ | MAP-* |
| Input modes, player settings, hold-to-toggle, accessibility presets | ✅ | MDE-* |
| Prompts: names in the layout's labels and pad styles; icon sets | ✅ | No artwork ships |
| Text dump and F3 debug overlay | ✅ | DBG-* |
| Recording and deterministic replay (events, controls, sensors, cut-offs, arrival moments) | ✅ | REC-1…2 |
| Binding validator | ✅ | EDT-1 |
| Editor dock: edit bindings, problems, actions, live device inspector, input log | ✅ / 🔌 | Loads in the Godot 4.8 editor; interactive use not yet checked by hand |

## Platforms

| Platform | Status |
|---|---|
| Windows, Linux, macOS | ✅ CI runs the whole suite headless; 🔌 real devices |
| Android, iOS | 🔌 Expected to work; not yet tried on a device |
| Web | Depends on Godot's C# web export |
| Consoles | Through a porting partner's input layer |

## Not included

| | |
|---|---|
| Sensor fusion (gyro + accelerometer into an orientation) | — (use a platform's fused orientation, or a backend) |
| Icon artwork for prompts | — (bring your own; `PromptIconSet` maps them) |
| Network input / rollback | — |
