# Godot Enhanced Input

Action-based input for Godot 4 C#. Gameplay code asks for **actions** — Jump, Move, Look — and
**binding contexts** decide which keys, buttons and sticks drive them right now. Every tick each
action gets a typed value and a phase, and raises events your code can subscribe to or poll.

> C# · Godot 4.7+ · configured in the Inspector or in code · works with the InputMap, physical
> keys and GUI focus · testable headless

Status: **0.1.0** — all four stages of [the plan](docs/design.md#6-staged-plan) are done: the core,
rebinding, local players and touch, prompts and debugging. Not yet released.

## What it does

- **Typed game actions.** Digital (on/off), 1D, 2D and 3D values; several bindings per action,
  combined by strongest value or by sum.
- **Binding contexts with priorities.** Add and remove sets of bindings at runtime. A higher
  context can *consume* the keys it uses, or *block* everything below it (pause menus). A context
  can ignore keys that are already held when it opens.
- **Modifiers** shape values: axial and radial deadzones, scale, delta-time scale, negate, swizzle,
  smoothing, exponent and `Curve` response, normalize, clamp, 2D → world axes, view-relative
  (camera) rotation, field-of-view scaling.
- **Triggers** decide when an action fires: down, pressed, released, hold, hold-and-release, tap,
  pulse, chord, chord blocker and combo — with explicit, implicit and blocker roles.
- **A per-tick state machine** turns phases (idle, ongoing, triggered) into events: started,
  ongoing, triggered, completed, canceled — with elapsed and triggered time. Hold and pulse work
  while nothing new arrives.
- **Player rebinding.** Mark bindings rebindable, list them as slots for a settings menu, rebind
  in code or by capturing the next press, handle conflicts (allow, reject, swap, clear), and save
  each player's profile as versioned JSON.
- **Local multiplayer.** Several players, each with its own contexts and profile; exclusive joypad
  assignment; players join by pressing a button on a new joypad.
- **Touch.** Touch sources (pressed, position, drag) and on-screen controls — a virtual stick,
  buttons and a drag zone for camera look — with multi-touch.
- **Button prompts.** "Press Space" turns into "Press A" when the player picks up a pad: prompts
  follow the last used device, rebinds and chords, name keys as the keyboard layout labels them,
  and name pad buttons in Xbox-, PlayStation- or Nintendo-like styles. Icon sets map prompts to your
  own artwork.
- **Debugging.** A text dump of every player's contexts and actions, and an F3 overlay.
- **Godot-friendly.** Physical key positions by default; InputMap actions as sources;
  `InputEventAction` drives game actions directly (great for tests); never marks events handled
  and never steals presses from a focused GUI control; C# callbacks, polling and Godot signals.

## Install

Copy `addons/godot_enhanced_input` into your project's `addons/` folder, build the C# solution,
and enable **Godot Enhanced Input** in *Project → Project Settings → Plugins*. Enabling it adds the
`EnhancedInput` autoload.

## Quick start

Create resources in the FileSystem dock (*New Resource…*):

1. A `GameAction` "jump" (value kind *Digital*) and a `GameAction` "move" (*Axis2D*).
2. A `BindingContext` "gameplay" with `ActionBinding`s:
   - jump ← `InputSource` *Key* Space, trigger `PressedTrigger`;
   - move ← `InputSource` *KeyAxis2D* W/S/A/D;
   - move ← `InputSource` *JoyStick* Left, modifier `DeadzoneModifier`.

Then in code:

```csharp
using GodotEnhancedInput;

public partial class Hero : CharacterBody3D
{
    [Export] public BindingContext Gameplay { get; set; }
    [Export] public GameAction Jump { get; set; }
    [Export] public GameAction Move { get; set; }

    private InputPlayer _input;

    public override void _Ready()
    {
        _input = EnhancedInput.DefaultPlayer;
        _input.AddContext(Gameplay, priority: 0);
        _input.Bind(Jump, ActionEvents.Triggered, _ => Velocity += Vector3.Up * 6f);
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector2 move = _input.GetValue(Move).AsVector2();   // polling works too
        // ...
    }

    public override void _ExitTree() => _input.RemoveContext(Gameplay);
}
```

Everything can also be built in code:

```csharp
var jump = GameAction.Create("jump");
var gameplay = BindingContext.Create("gameplay");
gameplay.Add(jump, InputSource.ForKey(Key.Space), new PressedTrigger());
gameplay.Add(jump, InputSource.ForJoyButton(JoyButton.A), new PressedTrigger());
```

The demo in `addons/godot_enhanced_input_demo` shows chords, tap-versus-hold on one key, a combo,
pulses, a blocking pause menu with a rebinding panel, joining with a second joypad and on-screen
touch controls (F2 on desktop). Open `demo.tscn` and press F6.

## Documentation

- [Getting started](docs/getting-started.md)
- [Concepts: actions, contexts, phases and events](docs/concepts.md)
- [Modifiers](docs/modifiers.md) · [Triggers](docs/triggers.md) · [Rebinding](docs/rebinding.md) · [Players and touch](docs/players-and-touch.md) · [Prompts and debugging](docs/prompts-and-debugging.md)
- [Godot integration: InputMap, `InputEventAction`, GUI, tests](docs/godot-integration.md)
- [Design and requirements](docs/design.md)
- [Migrating from InputForge](docs/migration.md)

## Tests

```powershell
dotnet run --project GodotEnhancedInput.Tests/GodotEnhancedInput.Tests.csproj -c Debug
```

The suite runs against a real headless Godot through [2dog](https://github.com/outfox/2dog);
see [the test README](GodotEnhancedInput.Tests/README.md).

## Credits and license

MIT. Godot Enhanced Input began as a fork of [InputForge](https://github.com/pandoraoyun/InputForge)
by PandoraOyun (MIT), whose context stack, Inspector-first resources and test setup it grew from.
Version 0.1.0 replaces InputForge's API; see [migration](docs/migration.md). AI use is described in
[AI_DISCLOSURE.md](AI_DISCLOSURE.md).
