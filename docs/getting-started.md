# Getting started

## 1. Install

1. Copy `addons/godot_enhanced_input` into your project.
2. Build the C# solution once (*Build* button, or `dotnet build`).
3. *Project → Project Settings → Plugins*: enable **Godot Enhanced Input**. This adds the
   `EnhancedInput` autoload.

## 2. Make actions

The plugin adds three assets to *Create New Resource*, and only these three:

| Asset | What it holds |
|---|---|
| `EnhancedInputAction` | One action: its name, value kind, and action-level modifiers and triggers. |
| `EnhancedInputMappingContext` | Mappings from inputs to actions, each with its own modifiers and triggers. |
| `EnhancedInputConfig` | The whole game's setup: actions, contexts, control schemes, defaults. |

Everything inside them (mappings, input sources, modifiers, triggers, control schemes, haptic effects,
touch layouts) is created in the Inspector, from the *Add …* menu above the list that holds it.

FileSystem dock → right-click → *New Resource…* → `EnhancedInputAction`. Make three:

| File | Name | Value kind |
|---|---|---|
| `jump.tres` | jump | Digital |
| `move.tres` | move | Axis2D |
| `look.tres` | look | Axis2D |

## 3. Make a context

*New Resource…* → `EnhancedInputMappingContext` → `gameplay.tres`. Click *Add Mapping* above *Bindings*
for each row below. In a mapping, type the input as a path in the *Input* box (`key:space`, `pad:a`,
`wasd`, `pad:left_stick`, `mouse:motion`) and press Enter, then use *Add Modifier…* and
*Add Trigger…* to pick from every modifier and trigger:

| Action | Source (`InputSource`) | Modifiers | Triggers |
|---|---|---|---|
| jump | Kind *Key*, Key Space | | `PressedTrigger` |
| jump | Kind *JoyButton*, A | | `PressedTrigger` |
| move | Kind *KeyAxis2D*, W/S/A/D | | |
| move | Kind *JoyStick*, Left | `DeadzoneModifier` (Radial) | |
| look | Kind *MouseMotion* | `ScaleModifier` (0.2, 0.2, 1) | |
| look | Kind *JoyStick*, Right | `DeadzoneModifier`, `DeltaTimeScaleModifier`, `ScaleModifier` (400, 400, 1) | |

On `move.tres`, add a `NormalizeModifier` with *Only If Longer* so diagonals are not faster.

## 4. Use it

```csharp
using Godot;
using GodotEnhancedInput;

public partial class Player : CharacterBody3D
{
    [Export] public EnhancedInputMappingContext Gameplay { get; set; }
    [Export] public EnhancedInputAction Jump { get; set; }
    [Export] public EnhancedInputAction Move { get; set; }
    [Export] public EnhancedInputAction Look { get; set; }

    public override void _Ready()
    {
        // Everything added to the scope is released when this node leaves the tree.
        var input = EnhancedInput.DefaultPlayer.CreateScope(this);
        input.PushContext(Gameplay);
        input.OnTriggered(Jump, DoJump);
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector2 move = Move.ReadVector2();   // up is −Y, like Input.GetVector
        Vector2 look = Look.ReadVector2();
        if (Jump.WasPressed()) { /* … polling works too */ }
    }

    private void DoJump() { /* … */ }
}
```

Actions answer for themselves on the default player (player 0); pass a player for local
multiplayer: `Move.ReadVector2(player2)`. The autoload ticks once per frame before physics and
process, so both see the same input.

### Or all in code

```csharp
var jump = EnhancedInputAction.Button("jump");
var move = EnhancedInputAction.Axis2D("move");
var fire = EnhancedInputAction.Button("fire");

var gameplay = EnhancedInputMappingContext.Create("gameplay")
    .Bind(move, "wasd", "pad:left_stick")
    .Bind(jump, "key:space", "pad:a");
gameplay.Add(fire, "mouse:left").Pressed();
gameplay.Add(fire, "pad:rt").Hold(0.4f);

EnhancedInput.DefaultPlayer.AddContext(gameplay);
```

Source paths are listed in [sources and devices](sources-and-devices.md).

### Or one configuration for the whole game

*New Resource…* → `EnhancedInputConfig` (`game_input.tres`): list the actions, the contexts, the
contexts active from the start, the control schemes (`InputMode`s), default settings, haptic effects
and touch layouts. Set it on the autoload's *Configuration* and every player is set up with it; switch
contexts by id or name:

```csharp
EnhancedInput.DefaultPlayer.ActivateContext("vehicle");
```

## 5. Add a menu on top

Make `menu.tres` with *Block Lower Priority* on and bind `close_menu` to Escape with a
`PressedTrigger`. Open it for as long as the menu is up:

```csharp
_menuInput = EnhancedInput.DefaultPlayer.PushContext(Menu, priority: 100,
    new ContextOptions { IgnoreHeldSourcesUntilReleased = true });
// …
_menuInput.Dispose();   // back to gameplay
```

While it is active, gameplay reads nothing; the Escape press that opened the menu is ignored until
released, and a key held through the menu does not count as a new press when it closes.

## 6. Show prompts, let players rebind

```csharp
_hint.Text = $"Press {_input.GetPromptText(Jump)} to jump";   // "Space", or "A" once a pad is used
_input.InputDeviceChanged += (kind, pad) => _hint.Text = $"Press {_input.GetPromptText(Jump)} to jump";
```

Give a binding a *Rebind Id* (Inspector, *Rebinding* group) and players can change it from a
settings menu; see [rebinding](rebinding.md). Add an `InputDebugOverlay` node and press F3 to see
what every action is doing.

## Next

- [Concepts](concepts.md) — how contexts, the tick, phases and events fit together; ids,
  configurations, evaluation policies, where events came from, scopes
- [Triggers](triggers.md) and [modifiers](modifiers.md) — the full sets
- [Rebinding](rebinding.md), [players and touch](players-and-touch.md),
  [prompts and debugging](prompts-and-debugging.md)
- [Godot integration](godot-integration.md) — InputMap, `InputEventAction`, GUI focus, headless tests
- The demo: `addons/godot_enhanced_input_demo/demo.tscn`
