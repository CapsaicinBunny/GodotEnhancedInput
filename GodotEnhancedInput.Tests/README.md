# GodotEnhancedInput.Tests

Tests for Godot Enhanced Input, run against a real headless Godot embedded by
[2dog](https://github.com/outfox/2dog), with xUnit v3 on the Microsoft Testing Platform.

## Running

From the repository root:

```powershell
dotnet build GodotEnhancedInput.Tests/GodotEnhancedInput.Tests.csproj -c Debug
dotnet run --project GodotEnhancedInput.Tests/GodotEnhancedInput.Tests.csproj -c Debug --no-build
```

`dotnet run` launches the test executable directly; the VSTest path (`dotnet test` through
vstest.console) does not work with 2dog on .NET 10. Filter with xUnit's options after `--`, e.g.
`-- --filter-class "*ContextTests"`.

## Layout

| Folder | What |
|---|---|
| `Core/` | values, raw input and sources, InputMap / `InputEventAction`, modifiers, triggers |
| `Pipeline/` | contexts, the whole tick (accumulation, chords, combos, pause), the phase state machine, events, GUI intake |
| `Rebinding/` | rebind slots, conflicts, capture, profile JSON and files |
| `Players/` | joypad assignment and joining, touch sources, on-screen controls |
| `Prompts/` | prompt names, joypad styles, last used device, icon sets, the debug dump and overlay |
| `Demo/` | the demo's saved resources and scene (pause menu, rebinding ids, joining) |

Each test class names the requirement IDs from [docs/design.md](../docs/design.md) it covers.

## How the tests drive input

No frames run in the shared engine. Tests use a standalone `InputPlayer` (`Rig` in
`TestHelpers.cs`), feed it events with `FeedEvent` (key events set `PhysicalKeycode`), and call
`Tick` with a fixed delta. Nodes are freed synchronously at the end of each test.

## Regenerating the demo resources

The demo's `.tres` files and `demo.tscn` are written by Godot from `DemoContent.cs`:

```powershell
$env:GEI_WRITE_DEMO = "1"
dotnet run --project GodotEnhancedInput.Tests/GodotEnhancedInput.Tests.csproj -c Debug --no-build -- --filter-method "*WriteDemoResources*"
Remove-Item Env:GEI_WRITE_DEMO
```

## Coverage

`coverage.runsettings` measures only the plugin assembly and excludes Godot's generated code and
the editor plugin.

## If a run fails with `NativeMethodBindNotFoundException`

A stale `.godot/mono` cache built against another Godot version is the usual cause. With the
editor closed, delete `.godot`, `GodotEnhancedInput.Tests/bin` and `GodotEnhancedInput.Tests/obj`,
then build and run again, letting 2dog populate the cache first.
