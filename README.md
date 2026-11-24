# Godot Enhanced Input

Action-based input for Godot 4 C#. Gameplay code asks for **actions** — Jump, Move, Look — and
**binding contexts** decide which keys, buttons and sticks drive them right now. Every tick each
action gets a typed value and a phase, and raises events your code can subscribe to or poll.

> C# · Godot 4.7+ · configured in the Inspector or in code · works with the InputMap, physical
> keys and GUI focus · testable headless

Status: **0.1.0**, feature-complete for the release and in stabilization. All seven stages of
[the plan](docs/design.md#6-staged-plan) are done: the core, rebinding, local players and touch,
prompts and debugging, the extended feature set (more sources and devices, modes and accessibility,
recording, editor tools), rotations and device handling (quaternions, XR hands and eyes, eye
trackers, haptic effects, reconnection), and stabilization (event-accurate timing, cut-off input,
tracked values, ownership by device identity). What is tested and what still needs real hardware:
[supported features](docs/features.md). Not yet released.

## What it does

- **Typed game actions.** Digital (on/off), 1D, 2D, 3D and 4D values, quaternions and Euler angles;
  several bindings per action, combined by strongest value or by sum.
- **Binding contexts with priorities.** Add and remove sets of bindings at runtime. A higher
  context can *consume* the keys it uses, or *block* everything below it (pause menus). A context
  can ignore keys that are already held when it opens.
- **Many sources.** Keys (physical, by code or by label), mouse buttons, motion, position and wheel,
  joypad buttons, axes and sticks (also by raw index), motion sensors of pads and phones, touch,
  multi-finger gestures, pen pressure and tilt, InputMap actions, virtual controls, controls of
  backend devices, and composites ("Ctrl + S", two- and four-key axes, any-of) — all with a short
  text form such as `pad:left_stick` or `key:ctrl + key:s`.
- **Modifiers** shape values: axial and radial deadzones, anti-deadzone, scale, player sensitivity,
  acceleration, delta-time scale, negate, swizzle, smoothing, hysteresis, exponent and `Curve`
  response, remap, calibration, quantize, integrate, differentiate, normalize, clamp, DPI scale,
  gyro drift removal, 2D → world axes, view-relative (camera) rotation, field-of-view scaling, and
  rotation conversion, offset and smoothing.
- **Triggers** decide when an action fires: down, pressed, released, hold, hold-and-release, tap,
  multi-tap, toggle, pulse, repeat, threshold, hysteresis, flick, distance, gaze dwell, chord, chord
  blocker, combo and code-defined predicates — with explicit, implicit and blocker roles.
- **A per-tick state machine** turns phases (idle, ongoing, triggered) into events: started,
  ongoing, triggered, completed, canceled — with elapsed and triggered time. Hold and pulse work
  while nothing new arrives. Every change within a frame is evaluated in that frame, in order, so a
  tap or a chord is never lost or late even at low frame rates; input that is cut off (alt-tab, a
  pad unplugged, a menu opening) is canceled with a reason and never looks like a release.
- **Player rebinding.** Mark bindings rebindable, list them as slots for a settings menu, rebind
  in code or by capturing the next press (with exclusions, filters, chords and timeouts), rebind one
  part of a composite, handle conflicts (allow, reject, swap, clear), and save each player's profiles
  — several per player, plus shareable presets — as versioned JSON written safely.
- **Modes, settings and accessibility.** Input modes switch binding groups with the device in use;
  per-player settings (sensitivity, inversion, deadzones, timing scales, vibration strength) live in
  the profile; hold-to-toggle for any action and ready-made accessibility presets.
- **Devices.** A device registry whose identities survive reconnecting, controller families
  (Xbox, Elite, DualShock, DualSense, Switch, Joy-Con, Steam Deck, Steam Input) with their features,
  device switching with fallback when a pad is lost, device profiles that calibrate and name wheels,
  pedals and flight sticks, pluggable backends for other hardware, force feedback for wheels.
- **XR and eye tracking.** Controller and headset poses as quaternions, hand tracking (pinch, grab,
  joints), face and eye gaze, per-hand vibration; desktop eye trackers through a small adapter.
- **Haptics.** Effects with envelopes, repeats and loops, mixed per motor with priorities, scaled by
  the player's vibration setting; joypad lights and handheld vibration.
- **Local multiplayer.** Several players, each with its own contexts and profile; exclusive joypad
  assignment; players join by pressing a button on a new joypad.
- **Touch.** Touch sources (pressed, position, drag), gestures (pinch, twist, two-finger drag,
  swipe, trackpad pinch) and on-screen controls — a virtual stick, buttons and a drag zone for camera
  look — with multi-touch, safe-area placement and player-editable layouts.
- **Button prompts.** "Press Space" turns into "Press A" when the player picks up a pad: prompts
  follow the last used device, rebinds and chords, name keys as the keyboard layout labels them,
  and name pad buttons in Xbox-, PlayStation- or Nintendo-like styles. Icon sets map prompts to your
  own artwork.
- **Debugging and tools.** A text dump of every player's contexts and actions, an F3 overlay,
  recording and deterministic replay of a player's input, a binding validator, and an editor dock
  that edits bindings in place (actions, inputs by typing or listening, groups, modifiers, triggers,
  with undo), shows problems, and inspects connected pads live with an input log.
- **Fast.** Bindings are compiled once and reused; steady-state ticks allocate nothing.
- **Godot-friendly.** Physical key positions by default; InputMap actions as sources;
  `InputEventAction` drives game actions directly (great for tests); never marks events handled
  and never steals presses from a focused GUI control; C# callbacks, polling and Godot signals.

## Requirements

- Godot 4.7 or later, **.NET edition** (tested with 4.7.1 and 4.8-beta4)
- A C# project (the .NET SDK your Godot version asks for)

## Install

1. Get the `addons/godot_enhanced_input` folder: clone or download this repository, or (once
   releases are published) take the ZIP attached to a GitHub release.
2. Copy it into your project's `addons/` folder. The demo (`addons/godot_enhanced_input_demo`) is
   optional.
3. Build the C# solution once, then enable **Godot Enhanced Input** in
   *Project → Project Settings → Plugins*. Enabling it adds the `EnhancedInput` autoload.

## Reading actions at a glance

| You want | Use |
|---|---|
| a stick, mouse look or movement vector | `move.ReadVector2()` |
| a trigger or throttle amount | `throttle.ReadFloat()` |
| "pressed this frame" | `jump.WasPressed()` |
| "held" | `sprint.IsTriggered()` |
| "released this frame" | `sprint.WasReleased()` |
| hold for N seconds, tap, double-press, pulse while held | `.Hold(0.5f)`, `.Tap()`, `MultiTapTrigger`, `PulseTrigger` |
| a callback instead of polling | `jump.OnTriggered(Jump)` |
| which device or binding did it | `e.Device`, `e.Binding`, `e.Source` in the callback |
| a context for as long as something lasts | `using var _ = player.PushContext(vehicle)` |
| a press kept for a moment (jump buffering) | `if (IsOnFloor() && jump.ConsumeTriggered(0.15)) Jump();` |
| a cooldown, a direction, a throw | `CooldownTrigger`, `DirectionTrigger`, `ReleaseVelocityTrigger` |
| members instead of strings | a generated class: `GameInput.Shared.Gameplay.Jump` ([code generation](docs/code-generation.md)) |
| a prompt such as "Press Space" | `player.GetPromptText(jump)` |

These read the default player (player 0). For local multiplayer, pass the player:
`move.ReadVector2(player2)`, or use the same methods on `InputPlayer`.

## Quick start

The plugin adds three assets to *New Resource…*: `EnhancedInputAction`, `EnhancedInputMappingContext`
and `EnhancedInputConfig`. Mappings, modifiers, triggers and the rest are created inside them from the
Inspector's *Add …* menus.

1. An `EnhancedInputAction` "jump" (value kind *Digital*) and an `EnhancedInputAction` "move" (*Axis2D*).
2. An `EnhancedInputMappingContext` "gameplay" with mappings (*Add Mapping*; type the input in *Input*):
   - jump ← `InputSource` *Key* Space, trigger `PressedTrigger`;
   - move ← `InputSource` *KeyAxis2D* W/S/A/D;
   - move ← `InputSource` *JoyStick* Left, modifier `DeadzoneModifier`.

Then in code:

```csharp
using GodotEnhancedInput;

public partial class Hero : CharacterBody3D
{
    [Export] public EnhancedInputMappingContext Gameplay { get; set; }
    [Export] public EnhancedInputAction Jump { get; set; }
    [Export] public EnhancedInputAction Move { get; set; }

    public override void _Ready()
    {
        var input = EnhancedInput.DefaultPlayer.CreateScope(this);   // released when this node leaves the tree
        input.PushContext(Gameplay);
        input.OnTriggered(Jump, () => Velocity += Vector3.Up * 6f);
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector2 move = Move.ReadVector2();   // polling works too
        // ...
    }
}
```

Everything can also be built in code, from short text paths:

```csharp
var jump = EnhancedInputAction.Button("jump");
var move = EnhancedInputAction.Axis2D("move");
var gameplay = EnhancedInputMappingContext.Create("gameplay")
    .Bind(move, "wasd", "pad:left_stick")
    .Bind(jump, "key:space", "pad:a");
gameplay.Add(jump, "pad:rt").Hold(0.4f);
```

Or put the whole game's input in one `EnhancedInputConfig` resource — actions, contexts, control
schemes, default settings — set it on the autoload, and switch contexts by id:
`player.ActivateContext("vehicle")`.

The demo in `addons/godot_enhanced_input_demo` shows chords, tap-versus-hold on one key, a combo,
pulses, a blocking pause menu with a rebinding panel, joining with a second joypad and on-screen
touch controls (F2 on desktop). Open `demo.tscn` and press F6.

## Documentation

| Guide | Covers |
|---|---|
| [Supported features](docs/features.md) | everything the plugin does, what is tested, what needs hardware or a platform plug-in |
| [Getting started](docs/getting-started.md) | install, first actions and context, using them, a menu on top |
| [Concepts](docs/concepts.md) | actions and values (incl. rotations), sources, contexts, the tick and event timing, phases and events, cut-off input, injected input |
| [Sources and devices](docs/sources-and-devices.md) | every source kind and its text form, composites, the device registry, identity and reconnection, controller families, device profiles, backends, XR, eye trackers, tracked values, haptics, force feedback |
| [Modifiers](docs/modifiers.md) | every modifier, recipes, writing your own |
| [Triggers](docs/triggers.md) | every trigger, roles, chords, combos, tap versus hold, writing your own |
| [Rebinding](docs/rebinding.md) | rebindable bindings, slots, conflicts, capture, composite parts, named profiles and presets |
| [Modes and accessibility](docs/modes-and-accessibility.md) | input modes, player settings, hold-to-toggle, accessibility presets |
| [Players and touch](docs/players-and-touch.md) | local multiplayer, joypad assignment, joining, touch sources, gestures, on-screen controls, safe area, touch layouts |
| [Prompts and debugging](docs/prompts-and-debugging.md) | button prompts, joypad styles, icon sets, text dump, F3 overlay |
| [Recording, tools and performance](docs/recording-and-tools.md) | recording and replay, the editor dock, validation, performance and safety notes |
| [Platforms and hardware](docs/platforms-and-hardware.md) | platform support table and a hardware test checklist |
| [Godot integration](docs/godot-integration.md) | ticking, event times, stuck input, InputMap and `InputEventAction`, focus loss, physical keys, GUI focus, signals, headless tests |
| [Typed C# for a config](docs/code-generation.md) | generated classes: `input.Gameplay.Jump` instead of strings |
| [Design and requirements](docs/design.md) | requirements with their tests, architecture, the staged plan |

## Repository layout

| Path | What |
|---|---|
| `addons/godot_enhanced_input/` | the plugin: `Resources/`, `Runtime/`, `Modifiers/`, `Triggers/`, `Devices/`, `Touch/`, `Prompts/`, `Debug/`, `Editor/` |
| `addons/godot_enhanced_input_demo/` | the demo scene and its saved actions and contexts |
| `GodotEnhancedInput.Tests/` | the headless test suite |
| `docs/` | the guides above |

## Tests

```powershell
dotnet run --project GodotEnhancedInput.Tests/GodotEnhancedInput.Tests.csproj -c Debug
```

526 tests run against a real headless Godot through [2dog](https://github.com/outfox/2dog), one or
more per requirement in the [design](docs/design.md); see [the test README](GodotEnhancedInput.Tests/README.md).
The same suite runs in CI on Windows, Linux and macOS. Real hardware is covered by the
[checklist](docs/platforms-and-hardware.md#hardware-checklist).

## License

MIT; see [LICENSE](LICENSE).
