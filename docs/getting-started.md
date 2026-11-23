# Getting started

## 1. Install

1. Copy `addons/godot_enhanced_input` into your project.
2. Build the C# solution once (*Build* button, or `dotnet build`).
3. *Project → Project Settings → Plugins*: enable **Godot Enhanced Input**. This adds the
   `EnhancedInput` autoload.

## 2. Make actions

FileSystem dock → right-click → *New Resource…* → `GameAction`. Make three:

| File | Name | Value kind |
|---|---|---|
| `jump.tres` | jump | Digital |
| `move.tres` | move | Axis2D |
| `look.tres` | look | Axis2D |

## 3. Make a context

*New Resource…* → `BindingContext` → `gameplay.tres`. Add `ActionBinding`s to *Bindings*:

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
    [Export] public BindingContext Gameplay { get; set; }
    [Export] public GameAction Jump { get; set; }
    [Export] public GameAction Move { get; set; }
    [Export] public GameAction Look { get; set; }

    private InputPlayer _input;
    private InputPlayer.ActionSubscription _jumpHandle;

    public override void _Ready()
    {
        _input = EnhancedInput.DefaultPlayer;
        _input.AddContext(Gameplay);
        _jumpHandle = _input.Bind(Jump, ActionEvents.Triggered, _ => DoJump());
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector2 move = _input.GetValue(Move).AsVector2();   // up is −Y, like Input.GetVector
        Vector2 look = _input.GetValue(Look).AsVector2();
        // …
    }

    public override void _ExitTree()
    {
        _jumpHandle.Dispose();
        _input.RemoveContext(Gameplay);
    }

    private void DoJump() { /* … */ }
}
```

If you read input in `_PhysicsProcess`, set the autoload's `TickMode` to `Physics`.

## 5. Add a menu on top

Make `menu.tres` with *Block Lower Priority* on and bind `close_menu` to Escape with a
`PressedTrigger`. Open it with:

```csharp
_input.AddContext(Menu, priority: 100, new ContextOptions { IgnoreHeldSourcesUntilReleased = true });
```

While it is active, gameplay reads nothing; the Escape press that opened the menu is ignored until
released. `RemoveContext(Menu)` returns control.

Next: [concepts](concepts.md), [triggers](triggers.md), [modifiers](modifiers.md),
[Godot integration](godot-integration.md).
