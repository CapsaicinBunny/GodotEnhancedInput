# Concepts

```
 InputSource ──► ActionBinding ──► GameAction ──► ActionState (per player)
 (what to read)  (+ modifiers,     (value kind,    value · phase · events · timings
                   triggers)        modifiers,
                                    triggers)
        grouped in BindingContexts, added to an InputPlayer with a priority
```

## Game actions and values

A `GameAction` is a resource describing something the player wants to do. Its **value kind** is
`Digital`, `Axis1D`, `Axis2D` or `Axis3D`. Values are `ActionValue`s: a kind plus a `Vector3`.

- `AsBool()`, `AsFloat()`, `AsVector2()`, `AsVector3()` read it; implicit conversions exist too.
- A source's value is **shaped** to the action's kind before anything else: extra components are
  dropped. A digital action keeps the source's strength in X (so an analog trigger at 0.3 gives
  0.3), and reads as `true` whenever it is non-zero.
- When several bindings drive one action in the same tick, their values combine by the action's
  **accumulation**: `Strongest` (per component, the largest magnitude wins — the default) or `Sum`
  (opposite keys cancel).
- Actions are compared by resource identity. The `Name` is used to match `InputEventAction` events
  and for logs; `DisplayName` is for menus.

Other action settings: **Consume input** (see contexts below), **Trigger when paused**, and
action-level **modifiers** and **triggers**, which apply to the combined value.

## Sources

An `InputSource` says what to read. Kinds: `Key`, `KeyAxis1D` (two keys), `KeyAxis2D` (four keys),
`MouseButton` (wheel directions included), `MouseMotion` (pixels summed over the tick),
`MousePosition`, `JoyButton`, `JoyAxis`, `JoyStick` (both axes of a stick), `InputMapAction`,
`Virtual` (a named control set from code) and `None`.

- Keys match **physical** positions by default, so WASD stays WASD on any layout. `KeyMatch` can
  switch to the logical key code or the printed label.
- 2D sources follow Godot's screen convention: **up is −Y**, like `Input.GetVector` and sticks.
- Joypad sources read one device or any device the player listens to.
- Sources hold no runtime state; share them freely.

## Binding contexts

A `BindingContext` is a list of `ActionBinding`s that are switched on and off together. Add it to a
player with a priority:

```csharp
player.AddContext(gameplay, priority: 0);
player.AddContext(menu, priority: 100, new ContextOptions { IgnoreHeldSourcesUntilReleased = true });
player.RemoveContext(menu);
```

- Higher priority is evaluated first; equal priorities go to the most recently added. Adding an
  active context again updates its priority and options.
- **Consumption.** When an action has *Consume input* on (the default), the sources its bindings
  read are hidden from every *lower-priority* context while this context is active — pressed or
  not. Bindings in the same context all see the source. Consumption is per channel: a context that
  binds W hides W from the WASD composite below it, but not A, S or D.
- **Blocking.** A context with *Block lower priority* hides *all* sources from lower contexts.
- **Ignore held sources until released.** Sources that are down when the context is added read as
  zero in it until released once — the key that opened a menu does not also act inside it.
- When an action loses its input (context removed, source consumed), it simply finishes on the
  next tick: completed if it was triggered, canceled if it was ongoing.

## The tick

Events only record raw input. Once per tick (by default in `_Process`) each player:

1. orders its contexts and works out which sources each one consumes or blocks;
2. evaluates each action: reads its bindings' sources, applies **binding modifiers** then
   **binding triggers**, combines the bindings, applies the action's **modifiers** then its
   **triggers**;
3. moves the action's phase and records events and timings;
4. calls callbacks and emits signals;
5. forgets per-tick data (mouse motion, presses shorter than a tick, one-tick injections).

A press and release that both arrive between two ticks still count for one tick.

## Phases and events

Triggers report a **phase** each tick: `Idle`, `Ongoing` (partway: a hold that has not finished, a
tap still within its time) or `Triggered`. The action's binding-level phase is the strongest of
its bindings; if the action has its own triggers, the final phase is the weaker of the two.

Changes of phase raise events:

| from \ to | Idle | Ongoing | Triggered |
|---|---|---|---|
| **Idle** | — | Started, Ongoing | Started, Triggered |
| **Ongoing** | Canceled | Ongoing | Triggered |
| **Triggered** | Completed | Ongoing | Triggered |

`ActionState.ElapsedSeconds` counts from the start (including the starting tick);
`TriggeredSeconds` counts the time spent triggered. Both are still reported on the completing or
canceling tick and reset afterwards.

## Reading actions

```csharp
// subscribe (callbacks run after the whole tick is evaluated)
var handle = player.Bind(jump, ActionEvents.Triggered, e => Jump(e.Value));
handle.Dispose();                       // unsubscribe

// poll
player.GetValue(move).AsVector2();
player.IsTriggered(sprint);             // phase is Triggered
player.WasStarted(charge);              // events of the last tick
player.WasCompleted(fire);
player.GetState(charge).ElapsedSeconds;

// every event of a player
player.ActionEventRaised += e => GD.Print($"{e.Action.Name} {e.Event}");

// Godot signals: ActionStarted, ActionOngoing, ActionTriggered, ActionCompleted, ActionCanceled,
// ContextAdded, ContextRemoved
player.ActionTriggered += action => GD.Print(action.Name);
```

Typical mappings: a "pressed" flag is `WasTriggered` on an action with a `PressedTrigger`; a "held"
flag is `IsTriggered` on an action with no trigger (or a `DownTrigger`); a release is `WasCompleted`.

To tell the player which input to use, ask for the action's prompts (`player.GetPromptText(jump)`);
see [prompts and debugging](prompts-and-debugging.md). `player.DescribeState()` prints everything
above for every action.

## Injected input

`InjectInput(action, value)` feeds a value for the next tick; `StartInjecting` / `StopInjecting`
feed it every tick. Injected values skip binding modifiers and triggers but go through the action's
own. They work whether or not the action is in an active context — useful for AI, replays, tutorials
and tests.

## Rebinding

Bindings with a rebind id can be changed per player through an input profile, by code or by
capturing the next press, and saved as JSON. See [rebinding](rebinding.md).

## Players

`EnhancedInput` (the autoload) owns `InputPlayer`s; player 0 always exists
(`EnhancedInput.DefaultPlayer`). Each player has its own contexts, raw input, action states and
profile, and listens to its own devices (`AcceptKeyboardMouse`, `AcceptTouch`, `AcceptActionEvents`,
owned joypads). See [local players and touch](players-and-touch.md).
