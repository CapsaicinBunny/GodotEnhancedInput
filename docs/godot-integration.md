# Godot integration

## The autoload and ticking

Enabling the plugin adds the `EnhancedInput` autoload. It processes while the tree is paused
(actions decide for themselves with *Trigger when paused*) and ticks its players:

| `TickMode` | When |
|---|---|
| `Auto` (default) | once per frame, right after the frame's input is delivered — before the first physics step and before `_Process`, so both see the same, current input |
| `Process` | in `_Process` |
| `Physics` | in every `_PhysicsProcess` step |
| `Manual` | only when you call `EnhancedInput.Tick(delta)` or `InputPlayer.Tick(delta)` |

### Event times

Godot does not give input events timestamps, so a player stamps each change when it reaches it
(`InputPlayer.Clock`, Godot's monotonic clock) and places it within the frame by that time
(`UseEventTimes`, on for players ticked by the autoload in real time, off with `TickMode.Manual`,
where the deltas passed in are not wall-clock time; off, every change counts as arriving at the start
of its frame). Godot usually delivers the input of a frame in one burst before the frame, so arrival
times are only as fine as that: turn on **Project Settings → Input Devices → Buffering → Agile Event
Flushing** for finer times on platforms that support it. Backends and replays can place changes
exactly.

`ReleaseStuckInput` (on by default) compares held keys and buttons with Godot's own state each
tick and releases any whose release never arrived (it went to another window, or an overlay ate
it). It never presses anything, and leaves replays alone.

An `InputPlayer` also works on its own, without the autoload or a scene tree: feed it events with
`FeedEvent` and call `Tick`. That is how the test suite drives it.

## InputMap actions and `InputEventAction`

Two independent paths connect Godot's own action system:

1. **InputMap actions as sources.** An `InputSource` of kind `InputMapAction` reads a Godot action
   by name: from `InputEventAction` events and, when `InputPlayer.PollInputMap` is on (player 0 by default),
   from `Input.GetActionRawStrength`. So `Input.ActionPress("jump")`, devices bound to the action
   in the Project Settings, and `Input.ParseInputEvent(new InputEventAction { … })` all drive it.
2. **`InputEventAction` drives game actions by name.** When `InputPlayer.RouteGameActionEvents` is
   on (default), an `InputEventAction` whose `Action` equals the `Name` of an `EnhancedInputAction` in an
   active context injects input into that game action while pressed. Tests and tools can press
   game actions without knowing their keys:

```csharp
player.FeedEvent(new InputEventAction { Action = "jump", Pressed = true, Strength = 1f });
player.Tick(1.0 / 60.0);
Assert.True(player.WasTriggered(jump));
```

No press is lost between ticks. Polling reads Godot's global input state, which ignores device
filters and what the GUI accepted; turn `PollInputMap` off to use only the events a player receives.

## Focus loss

When the window or application loses focus, or the app is suspended, `EnhancedInput` cuts off every
player's held input (`ResetInputOnFocusLoss`, on by default) and on-screen controls let go of their
fingers, so nothing stays stuck after alt-tab or a phone call. Cut off is not released: held actions
end as `Canceled` with `EndReason` `FocusLost` or `ApplicationSuspended`, and release triggers do not
fire. `ResetAllInput(reason)` does the same on demand.

`InputEventAction`s have no real device, so through the autoload they reach only players with
`AcceptActionEvents` on — player 0 by default. To drive another player in a test, feed the event to
that player directly, or turn the flag on for it.

## Physical keys

Key sources match `InputEventKey.PhysicalKeycode` by default, so bindings follow key positions
whatever the keyboard layout. Set `KeyMatch` to `Logical` (the key code the layout produces) or
`Label` (the printed label) where the letter matters, e.g. "press I for inventory" menus. When you
build events by hand, set `PhysicalKeycode`.

## GUI focus

The system never calls `SetInputAsHandled`, so everything else still receives every event — with
two exceptions: a press taken by a running [rebind capture](rebinding.md#4-capture-the-next-press)
(so the focused button does not also react to it), and touches or clicks used by an
[on-screen control](players-and-touch.md#on-screen-controls) you placed in the scene. With
`GuiPolicy.RespectGui` (default):

- **presses** and movement away from zero are read in `_UnhandledInput` — when a focused `LineEdit`
  or button accepts a key, gameplay does not see it;
- **releases** and movement toward zero are read in `_Input`, before the GUI — so a key held in
  gameplay never sticks when focus moves to a control;
- the mouse **position** is always tracked; mouse **motion** over a GUI control that stops it is
  not counted as look input.

`GuiPolicy.ReadEverything` reads everything in `_Input`, even while a control has focus.

## Joypads

Joypad sources can name a device or use −1 for any device the player listens to
(`InputPlayer.JoypadDevices`; empty means unowned pads, for players that accept them). When a joypad disconnects, its state is cleared and
its actions finish.

## Virtual controls

`player.SetVirtualControl("name", value)` sets a named control that `Virtual` sources read, until
changed. `player.AddToVirtualControl("name", delta)` adds to it for the next tick only (drags,
flicks). Use them for on-screen controls, network or replay input, and tests. The built-in
`VirtualJoystick`, `VirtualButton` and `TouchZone` write virtual controls; see
[local players and touch](players-and-touch.md).

## Signals and C# events

`InputPlayer` signals: `ActionStarted`, `ActionOngoing`, `ActionTriggered`, `ActionCompleted`,
`ActionCanceled` (each with the `EnhancedInputAction`), `ContextAdded` (context, priority),
`ContextRemoved`, `BindingsChanged`, `RebindCaptured` (id, source), `RebindCaptureCanceled` (id).
`EnhancedInput` signals: `PlayerAdded`, `PlayerRemoved`. In C#, the signals are
also events (`player.ActionTriggered += …`), and `ActionEventRaised` streams every event with its
full `ActionEventArgs`.

## Testing your game headless

- Drive a standalone `InputPlayer` (or `EnhancedInput` with `TickMode.Manual`) with `FeedEvent`,
  `InputEventAction`, `SetVirtualControl` or `InjectInput`, and call `Tick` yourself.
- Build key events with `PhysicalKeycode`.
- No frames need to run: everything happens inside `Tick`.

## Physics and process frames

The players tick at one rate (see `TickMode`), but game code reads input in two loops at different rates.
Read edges ("pressed this frame") through the view of the loop you are in:

```csharp
public override void _PhysicsProcess(double delta)
{
    if (jump.WasPressed(InputLoop.Physics)) Jump();          // or player.PhysicsFrame.WasPressed(jump)
}
```

A view collects the events of every tick and shows them for exactly one frame of its loop. A short press in a
rendered frame without a physics step is still seen by the next physics step, and a frame with two physics
steps does not see it twice. The autoload starts a new physics frame at each physics step and a new process
frame each rendered frame; a `ToolInputPlayer` does the same for itself. Values and `IsTriggered` are states
and read the same in every loop. A view made after input already happened starts with the latest tick's
events; `player.EnableFrameViews()` makes both up front. Two presses between frames read as one
`WasPressed` (`ActionState.TriggerCount` counts them).

## Scene-local mapping contexts

Add an `EnhancedInputContextNode` to a scene and pick its mapping context: the context is active while the
node is in the tree — a vehicle's controls while you sit in it, a menu's while it is open. With
*Only While Parent Visible* it follows its parent's visibility; *Active* switches it off by hand. Several
nodes may hold the same context; it stays until the last one lets go.

## Godot's InputMap

**Importing.** *Import InputMap* in the Enhanced Input dock turns the project's input actions (Project
Settings → Input Map) into action files and one `input_map.tres` mapping context in a folder you pick:
keys (with Ctrl/Shift/Alt/Meta), mouse buttons, joypad buttons and axis directions, with each action's
deadzone. Godot's built-in `ui_*` actions are left out; existing action files of the same name are used, not
overwritten. Mouse buttons keep their modifier keys too. The report lists what was skipped and what was
approximated: modifier keys are matched as held keys (other modifiers held as well do not stop the
mapping), and Command-or-Control becomes the key of the platform importing it. Only Godot's own `ui_*`
actions are left out; a project's action that happens to start with `ui_` is imported. From code:
`InputMapImporter.FromProjectSettings()` or `FromInputMap()`.

**Mirroring back.** Set an action's *Mirror To Input Map* to a Godot action name and that action is pressed
(with the strength) while the enhanced action is triggered, so addons and code that call
`Input.IsActionPressed("jump")` keep working. Missing Godot actions are created at run time. When several
actions or players mirror into the same Godot action, the strongest wins. Turning mirroring off, or
renaming or clearing a target, releases what was pressed. Only the state is set, no events are sent, and
feedback is prevented: an action that reads a Godot action which leads back to its own target —
directly, or through other mirrored actions of any player — does not mirror. Turn it off project-wide with
`enhanced_input/input_map/mirror_actions`, or per player with `MirrorsToInputMap`.

## Project settings

Project Settings → Enhanced Input (added while the plugin is enabled):

| Setting | What |
|---|---|
| `enhanced_input/general/config` | the `EnhancedInputConfig` every player loads |
| `enhanced_input/general/tick_mode` | Auto, Process, Physics or Manual |
| `enhanced_input/general/gui_policy` | how input is shared with the GUI |
| `enhanced_input/general/reset_input_on_focus_loss` | release held input when the game loses focus |
| `enhanced_input/players/join_mode`, `max_players` | joining |
| `enhanced_input/input_map/mirror_actions` | the InputMap output bridge |

The autoload applies them when it starts; setting the same properties on it from code still works.

## Picking actions in the Inspector

In any script, a property typed as `EnhancedInputAction`, `EnhancedInputMappingContext` or
`EnhancedInputConfig` gets a dropdown of the project's files of that kind next to the resource picker. A
string property that holds an id gets a dropdown of ids with a hint:

```csharp
[Export(PropertyHint.PlaceholderText, EnhancedInputHints.ActionId)] public string InteractAction { get; set; } = "interact";
```

## Split screen and SubViewports

Give each player its `PointerRegion` — usually the `SubViewportContainer` showing its view. Mouse and touch
events then reach a player only inside its region, with positions converted to it (to the SubViewport's
pixels when the container stretches), and pointer sources and `PointerSpaceModifier` work per player. A
mouse button or finger pressed in a player's region stays with that player — its movement, release and
cancel — until it is released, wherever it goes. Keys and
pads are routed as before (keyboard to the players that accept it, pads to their owners).

## More than one window

A native window's events never pass the main window. `EnhancedInput.Instance.AttachWindow(window)` lets them
reach the players (in the same GUI-aware way); `AttachWindow(window, player2)` gives that window to some
players only — a second screen, a tool window. Focus moving between the game's own windows is not treated as
losing focus. `DetachWindow` undoes it and releases what was held through the window (as a cut-off).

## Editor tools and tool scripts

The autoload does not run in the editor. For editor plugins, `@tool` scripts, or a scene that should not
depend on the autoload, use a `ToolInputPlayer`: it activates its *Contexts* when ready, ticks itself in
`_Process`, and takes the events it receives (or that you feed it):

```csharp
public override int _Forward3DGuiInput(Camera3D camera, InputEvent e)
{
    _input.FeedEvent(e);
    return _input.WasPressed(snap) ? (int)AfterGuiInput.Stop : (int)AfterGuiInput.Pass;
}
```

