# Triggers

A trigger looks at a value every tick and reports a phase: `Idle`, `Ongoing` or `Triggered`.
Put triggers on an `ActionBinding` (they see that binding's modified value and decide whether the
binding fires — only firing bindings add to the action's value) or on an `EnhancedInputAction` (they see the
combined value of the firing bindings and decide the action's phase). Timers advance with ticks, so a hold
completes and a pulse repeats while no new events arrive.

Every trigger has an **actuation threshold** (default 0.5): the magnitude at which a value counts
as actuated. Zero is never actuated.

## Built-in triggers

| Trigger | Role | Behaviour | Settings |
|---|---|---|---|
| *(none)* | — | Triggered while the value is non-zero. | — |
| `DownTrigger` | explicit | Triggered every tick the value is actuated. | — |
| `ChangedTrigger` | explicit | Triggered on every tick the value differs from the previous tick's (including returning to zero). Read `RawValue` between changes: the binding only fires on the changes. | `Tolerance` |
| `PressedTrigger` | explicit | Triggered on the tick the value becomes actuated, then idle while held. | — |
| `ReleasedTrigger` | explicit | Ongoing while actuated; triggered on the tick it is released. | — |
| `HoldTrigger` | explicit | Ongoing until held for `HoldSeconds`, then triggered (every tick, or once with `OneShot`). Released early → idle → the action is canceled. | `HoldSeconds`, `OneShot` |
| `HoldReleaseTrigger` | explicit | Ongoing while held; on release, triggered for one tick if held at least `HoldSeconds`, else canceled. | `HoldSeconds` |
| `TapTrigger` | explicit | Ongoing while held; triggered on release if released within `TapSeconds`; held longer → canceled. | `TapSeconds` |
| `PulseTrigger` | explicit | While held: triggered on the first tick (`TriggerOnStart`), then every `IntervalSeconds`, ongoing in between; after `MaxPulses` (0 = unlimited) idle until released. | `IntervalSeconds`, `TriggerOnStart`, `MaxPulses` |
| `ChordTrigger` | implicit | Triggered only while `ChordAction` is triggered this tick. | `ChordAction` |
| `ChordBlockerTrigger` | blocker | Blocks its set while `BlockingAction` is triggered this tick. | `BlockingAction` |
| `ComboTrigger` | explicit | Steps of other actions, in order, each within its time limit. Ongoing while partway; triggered when the last step completes. Starts over on timeout, on a step action completing out of order, or when a cancel action triggers. `ToleranceSeconds` lets near-simultaneous steps arrive in either order; `IgnoreOutOfOrder` keeps stray steps from breaking it. | `Steps` (`ComboStep`: action, completing events, time limit), `CancelActions`, `ToleranceSeconds`, `IgnoreOutOfOrder` |
| `MultiTapTrigger` | explicit | Triggered on the release that completes `Taps` quick taps (double tap by default). Each press must be released within `TapSeconds` and the next must start within `GapSeconds`; otherwise it starts over. With `MaxDistance`, every tap must land within that many pixels of the first (a double-click on one spot). | `Taps`, `TapSeconds`, `GapSeconds`, `MaxDistance` |
| `BufferTrigger` | explicit | Input buffering in data: a press stays triggered for `BufferSeconds`, held or not; a new press restarts it. Read it with `ConsumeTriggered` so one press is used once (see [buffering](#input-buffering)). | `BufferSeconds` |
| `CooldownTrigger` | blocker | After the action fired, it cannot fire again for `CooldownSeconds`. Shared by all the action's bindings; never cuts an activation short. `Remaining(player, action)` for a cooldown display. | `CooldownSeconds` |
| `DirectionTrigger` | explicit | Triggered while a 2D value points into a sector (`Direction` ± `SectorDegrees`/2) and is actuated — directional attacks, 4/8-way gestures, radial menus. `Once` fires on entering the sector. Up is (0, −1). | `Direction`, `SectorDegrees`, `Once` |
| `ReleaseVelocityTrigger` | explicit | Ongoing while actuated; on release, triggered for one tick if the value moved at least `MinSpeed` per second during the last `WindowSeconds` — throws, flings, quick swipes. `Measure`: a position (stick, pointer) or per-tick motion (mouse motion, drags). | `MinSpeed`, `WindowSeconds`, `Measure` |
| `ToggleTrigger` | explicit | Each press flips it on or off; triggered while on (toggle crouch, toggle aim). Starts off again when the binding is suppressed. | — |
| `RepeatTrigger` | explicit | Like a held key in a text field: triggered on the press, again after `InitialDelaySeconds`, then every `IntervalSeconds`; ongoing in between; optional `MaxRepeats`. | `TriggerOnPress`, `InitialDelaySeconds`, `IntervalSeconds`, `MaxRepeats` |
| `ThresholdTrigger` | explicit | Triggered for one tick when the magnitude crosses `Threshold` rising, falling or either way; `Band` stops a noisy value from flickering across. | `Threshold`, `Band`, `Direction` |
| `HysteresisTrigger` | explicit | Down while actuated, but turns on at `PressThreshold` and off only below `ReleaseThreshold` — for analog triggers resting near a threshold. | `PressThreshold`, `ReleaseThreshold` |
| `FlickTrigger` | explicit | Triggered for one tick when a stick goes from rest to nearly full travel within `MaxSeconds`. Read the direction from the value on that tick. | `RestRadius`, `FlickRadius`, `MaxSeconds` |
| `DistanceTrigger` | explicit | For movement (mouse motion, drags): triggered once the movement since it started adds up to `Distance` — "drag 20 px before it counts". | `Distance`, `ResetSeconds`, `OneShot` |
| `DwellTrigger` | explicit | Look (or point) and hold still to select: ongoing while a position stays within `Radius` of where it settled, triggered once after `DwellSeconds` (again only after it moves away, or every `DwellSeconds` with `Repeat`). Zero counts as "no gaze". For eye trackers, head pointers, accessibility. | `Radius`, `DwellSeconds`, `Repeat` |
| `PredicateTrigger` | any | Its phase comes from a C# function (`Evaluator`), for one-off rules. Set in code. | `Evaluator`, `TriggerRole` |

## Timing settings

Durations are multiplied by player settings, so a player can make taps more forgiving or holds
shorter without new resources (see [modes and accessibility](modes-and-accessibility.md)):
`hold_time_scale` (hold, hold-release, the delay before the first repeat), `tap_time_scale` (tap and
multi-tap windows) and `repeat_time_scale` (pulse and repeat intervals). A custom trigger can use the
same scaling through `Scaled(context, seconds, setting)`.

## Roles: how a set of triggers combines

- **Explicit**: any one triggered explicit trigger satisfies the set. If the set has no explicit
  triggers, a non-zero value satisfies it instead.
- **Implicit**: every implicit trigger must be triggered as well.
- **Blocker**: a triggered blocker makes the whole set idle.

The set is **triggered** when the explicit side is satisfied, every implicit trigger is triggered
and no blocker is. Otherwise it is **ongoing** when the explicit side shows progress (an explicit
trigger ongoing or triggered, or a non-zero value with no explicit triggers) or an implicit trigger
is ongoing; else **idle**.

So a binding on Space with only a `ChordTrigger` on Shift is ongoing while Space alone is down,
triggered while both are down, and idle while only Shift is down.

## Chords

```
Sprint  ← Shift
Dash    ← Space  [Pressed, Chord(Sprint)]
Jump    ← Space  [Pressed, ChordBlocker(Dash)]
```

Space alone jumps; Shift + Space dashes and the blocker keeps Jump quiet. The order of bindings and
actions does not matter: a trigger that asks for another action's phase gets this tick's result
(evaluating that action first if needed). In a dependency cycle, the previous tick's phase is used.

## Combos

A combo is a trigger on a binding of its own — usually with source `None`:

```csharp
var spin = new ComboTrigger();
spin.Steps.Add(ComboStep.Create(jump));                                   // first step: no time limit
spin.Steps.Add(ComboStep.Create(dash, ActionEvents.Triggered, 0.6f));    // within 0.6 s
gameplay.Add(spinAction, InputSource.Nothing(), spin);
```

Give step actions a `PressedTrigger` (or complete steps on `Started`): an action without triggers
is triggered on *every* tick it is held, which would complete several steps at once.

For fighting-game motions, `ToleranceSeconds` (e.g. 0.05) accepts steps that arrive within that time of
each other in either order — the punch a frame before the last direction — and `IgnoreOutOfOrder`
keeps an extra step action (a rolled diagonal) from restarting the combo.

## Input buffering

A press that comes a little early should not be lost: Jump pressed just before landing, an attack
pressed during the previous one's recovery. Every time an action becomes triggered counts as one
activation; `ConsumeTriggered` uses each activation once, if it happened within a window of
**input time** (`player.InputTime`, the sum of tick deltas — it pauses with the game and replays exactly):

```csharp
if (IsOnFloor() && jump.ConsumeTriggered(0.15)) Jump();          // pressed within the last 0.15 s
if (player.WasTriggeredWithin(attack, 0.2)) QueueNextAttack();   // look without consuming
```

Phases and events are not changed — the release still completes the action, so a variable jump height
read from the release keeps working. To buffer in data instead, put a `BufferTrigger` on the binding:
the action then stays triggered for the window and `jump.ConsumeTriggered()` needs no window.
`ActionState.TriggerCount` and `LastTriggeredTime` hold the activation count and time.

## Tap and hold on one key

Two bindings on F in the same context: Inspect with `TapTrigger` and Pick up with `HoldTrigger`
(`OneShot`). A quick tap triggers Inspect on release and cancels Pick up; a long press triggers
Pick up and cancels Inspect.

## Writing your own

```csharp
/// Triggered from the first press on, until the binding is suppressed or the player is reset
/// (e.g. "press any button to start").
[Tool]
public partial class LatchTrigger : InputTrigger
{
    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        var memory = context.Memory;          // per binding, per player — never fields on the resource
        if (actuated) memory.Flag = true;
        return memory.Flag ? TriggerPhase.Triggered : TriggerPhase.Idle;
    }
}
```

Mark it `[Tool]` and it appears in the Inspector's *Add Trigger…* menu (no `[GlobalClass]` needed).

Override `Role` to make a trigger implicit or a blocker. Use `context.Player.GetPhaseThisTick(action)`
or `GetEventsThisTick(action)` to depend on other actions.
