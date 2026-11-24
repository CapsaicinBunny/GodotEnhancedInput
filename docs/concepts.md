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
`Digital`, `Axis1D`, `Axis2D`, `Axis3D`, `Axis4D`, `Quaternion` or `Euler`. Values are
`ActionValue`s: a kind plus a `Vector4`.

- `AsBool()`, `AsFloat()`, `AsVector2()`, `AsVector3()`, `AsVector4()`, `AsQuaternion()`,
  `AsEuler()` and `AsBasis()` read it; implicit conversions exist too.
- **Rotations.** A `Quaternion` value holds (x, y, z, w), reads normalized, and the identity (no
  rotation) counts as no input; its `Magnitude` is the angle as a fraction of half a turn, so
  thresholds and deadzones work on it. An `Euler` value holds angles in radians (Godot's YXZ order).
  Use them for XR controllers, headsets, phone orientation and gyro aiming; the
  [rotation modifiers](modifiers.md#rotations) convert between forms.
- `ActionState.InputValue` is the untouched input before any modifier, next to the processed `Value`.
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

## Ids and configurations

Actions, contexts, bindings, modes and configurations have a stable **`Id`** (empty: the name). Saved
settings, recordings and tools refer to ids, so renaming or moving a resource breaks nothing;
`FormerIds` keeps old ids working. `player.FindAction(idOrName)` and `FindContext(idOrName)` look them up.

An **`InputConfiguration`** groups a game's input in one resource: actions, contexts, the contexts
active from the start, control schemes (`InputMode`s) with the default one, default settings, haptic
effects, touch layouts, prompt icons and profile presets. `player.LoadConfiguration(config)` (or the
autoload's `Configuration`, for every player) sets a player up; `ActivateContext("vehicle")`,
`DeactivateContext` and `PushContext("vehicle")` switch contexts by id or name, at each context's
`DefaultPriority`. It is optional — loose resources and code work the same.

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
- When an action loses its input without being released (context removed, blocked, paused, its
  pad disconnected or given to another player, the window losing focus, its binding changed), it
  ends as **canceled**, never completed, and `ActionState.EndReason` says why. Release-based
  triggers (released, tap, hold-release) do not fire for it, so alt-tab never throws the grenade.

## The tick

Events only record raw input. Once per frame, right after Godot delivers the frame's input (see
[ticking](godot-integration.md#the-autoload-and-ticking)), each player orders its contexts and works
out which sources each one consumes or blocks, then evaluates:

1. **each input event since the last tick, one at a time, in the order they arrived.** First time
   passes up to the moment the event arrived for the actions it touches, under the input as it was;
   then the event is applied and those actions are evaluated again — read their bindings' sources,
   apply **binding modifiers** then **binding triggers**, combine the bindings, apply the action's
   **modifiers** then its **triggers**, move the phase. Phase changes (started, triggered, completed,
   canceled) are raised at once, in order. Actions whose triggers read other actions (chords, combos,
   blockers, code) are evaluated at every event, so they see the others' changes in order.
2. **the frame:** every action once more, with the frame's per-frame amounts (mouse motion,
   scrolling, drags, gestures, injections). Held actions raise their steady events here — `Triggered`
   again, still `Ongoing` — **at most once per frame**, however many other events arrived.

**No press is lost or delayed, and time is real.** Each action's time advances by exactly the frame's
delta over the tick, split at the moments its input changed: a key pressed 50 ms into a slow frame
and released 400 ms later was held 0.4 s, so it is not a tap and a 0.3 s hold completes. Changes are
placed by when they reached the player (see [event times](godot-integration.md#event-times)). A press
and release within one frame are both seen in that frame — a tap fires `Started`, `Triggered` and
`Completed` on the same tick, in that order — and a chord or combo pressed in order works at any
frame rate. A one-shot that fired at an event (pressed, released, tap, a combo's last step) stays
triggered until the frame ends, so `IsTriggered` sees it; it ends on the next frame. Polling
(`WasTriggered`, `WasCompleted`, `GetEvents`) sees every event of the frame and the frame's final
value and phase.

**How an action reports while held** is its `Evaluation`:

| `ActionEvaluation` | Reports |
|---|---|
| `Continuous` (default) | `Triggered` every frame while held — movement, looking, steering |
| `OnChange` | `Triggered` only on frames where the value changed |
| `EventDriven` | only presses, releases and phase changes — buttons |
| `PassThrough` | every input event on any of its bindings on its own, with that binding's value and source — drawing, multi-touch, rhythm |
| `Tracked` | triggered exactly while tracked, whatever the value — poses |

**Where an event came from.** Callbacks get `e.Binding` (the binding the input came from: the
strongest that fired), `e.Source` (the exact channel, with its device or finger), `e.Device` (the pad,
keyboard and mouse, touchscreen or backend device), `e.Timestamp` and, for completed and canceled
events, `e.EndReason` — for prompts that follow the device in use, haptics on the pad that pressed,
analytics and debugging. Read them during the callback.

Bindings are **suppressed** — not evaluated at all — while their context is blocked, all their
sources are consumed or ignored, or the tree is paused (for actions that do not run while paused).
A suppressed binding's triggers and modifiers start over, so a menu opening never looks like a
release. When it comes back with its key still held, the key counts as held all along: held actions
simply continue, and one-shots do not fire — closing a menu with Esc does not also press "pause".

## Scopes and lifetimes

`player.PushContext(context, priority)` adds a context and returns a handle; disposing it removes the
context (a vehicle, a menu, a cutscene). `SwitchContext(from, to)` swaps one for another. Context changes
take effect together at the next tick (or the next step, when made from a callback), so there is never
a frame with a half-switched set.

`player.CreateScope(node)` collects subscriptions and contexts — `scope.OnTriggered(jump, Jump)`,
`scope.PushContext(onFoot)` — and releases them all when the node leaves the tree, so changing scenes
or characters leaves nothing behind. `player.SetActionEnabled(action, false)` turns one action off
without touching contexts (held input ends canceled with `Blocked`).

## Phases and events

Triggers report a **phase** each tick: `Idle`, `Ongoing` (partway: a hold that has not finished, a
tap still within its time) or `Triggered`. The action's binding-level phase is the strongest of
its bindings. A binding *fires* when it has no triggers or its triggers are triggered, and only firing
bindings add to the action's value (`ActionState.RawValue` keeps the value of all evaluated bindings).
If the action has its own triggers, they decide on that combined value — release, tap and
hold-release work at action level too — and the action shows ongoing while a binding is partway.

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
