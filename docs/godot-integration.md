# Godot integration

## The autoload and ticking

Enabling the plugin adds the `EnhancedInput` autoload. It processes while the tree is paused
(actions decide for themselves with *Trigger when paused*) and ticks its players:

| `TickMode` | When |
|---|---|
| `Process` (default) | in `_Process` |
| `Physics` | in `_PhysicsProcess` — pick this when gameplay reads input in physics |
| `Manual` | only when you call `EnhancedInput.Tick(delta)` or `InputPlayer.Tick(delta)` |

An `InputPlayer` also works on its own, without the autoload or a scene tree: feed it events with
`FeedEvent` and call `Tick`. That is how the test suite drives it.

## InputMap actions and `InputEventAction`

Two independent paths connect Godot's own action system:

1. **InputMap actions as sources.** An `InputSource` of kind `InputMapAction` reads a Godot action
   by name: from `InputEventAction` events and, when `InputPlayer.PollInputMap` is on (player 0 by default),
   from `Input.GetActionRawStrength`. So `Input.ActionPress("jump")`, devices bound to the action
   in the Project Settings, and `Input.ParseInputEvent(new InputEventAction { … })` all drive it.
2. **`InputEventAction` drives game actions by name.** When `InputPlayer.RouteGameActionEvents` is
   on (default), an `InputEventAction` whose `Action` equals the `Name` of a `GameAction` in an
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

When the window or application loses focus, or the app is suspended, `EnhancedInput` releases every
player's held input (`ResetInputOnFocusLoss`, on by default) and on-screen controls let go of their
fingers, so nothing stays stuck after alt-tab or a phone call. `ResetAllInput()` does the same on
demand.

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
`ActionCanceled` (each with the `GameAction`), `ContextAdded` (context, priority),
`ContextRemoved`, `BindingsChanged`, `RebindCaptured` (id, source), `RebindCaptureCanceled` (id).
`EnhancedInput` signals: `PlayerAdded`, `PlayerRemoved`. In C#, the signals are
also events (`player.ActionTriggered += …`), and `ActionEventRaised` streams every event with its
full `ActionEventArgs`.

## Testing your game headless

- Drive a standalone `InputPlayer` (or `EnhancedInput` with `TickMode.Manual`) with `FeedEvent`,
  `InputEventAction`, `SetVirtualControl` or `InjectInput`, and call `Tick` yourself.
- Build key events with `PhysicalKeycode`.
- No frames need to run: everything happens inside `Tick`.
