# Typed C# for a config

An `EnhancedInputConfig` can generate a C# class with one property per action and one nested class per
mapping context, so game code names them as members instead of strings or hand-assigned resources:

```csharp
var input = GameInput.Shared;                       // loads res://input/game_input.tres once
if (input.Gameplay.Jump.WasPressed()) Jump();
var move = input.Gameplay.Move.ReadVector2();
using var driving = input.Vehicle.Push();           // activate a mapping context for a while
```

## Generating

Select the config and press **Generate C# class** in the Inspector, or pick the config in the Enhanced
Input dock's filter and press **C#**. The class is written next to the config, named after it
(`game_input.tres` → `GameInput.cs`, class `GameInput`); build the project to use it. Generate again
after adding, removing or renaming actions or mapping contexts.

A file of that name that was not generated is never overwritten. A generated file may be given a
namespace by hand (`namespace MyGame;`): regenerating keeps it. Nothing else in it should be edited.

From code or a build script: `InputCodeGenerator.Generate(config, className, @namespace, configPath)`
returns the source; `InputCodeGenerator.ClassNameFor(config)` and `Identifier(name)` give the names.

## What it contains

| Member | What |
|---|---|
| `ConfigPath` | where the config is |
| `Shared` | one instance, loaded from `ConfigPath` on first use |
| `new GameInput()` / `new GameInput(config)` | load it, or resolve against a config you loaded (a modded copy with the same ids) |
| `Config` | the config |
| one `EnhancedInputAction` per action | every action of the config and of its mapping contexts |
| one property per mapping context | `Context`, the context's own actions, `Push(player, priority)`, `Activate(player, priority)`, `Deactivate(player)` |

Members are resolved by **stable id** (`Id`, then `FormerIds`) when the instance is made, so changing a
display name never breaks code, and renaming an id keeps working through its former ids. A member that
cannot be found logs an error naming it. Property names are the names in PascalCase (`pick_up` →
`PickUp`); clashes get a number (`Config2`). The output is deterministic, so it diffs cleanly.

The demo's `addons/godot_enhanced_input_demo/DemoInput.cs` is generated from `demo_input.tres`; a test
checks that it is up to date.
