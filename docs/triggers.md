# Triggers

A trigger looks at a value every tick and reports a phase: `Idle`, `Ongoing` or `Triggered`.
Put triggers on an `ActionBinding` (they see that binding's modified value and decide whether the
binding fires — only firing bindings add to the action's value) or on a `GameAction` (they see the
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
| `ComboTrigger` | explicit | Steps of other actions, in order, each within its time limit. Ongoing while partway; triggered when the last step completes. Starts over on timeout, on a step action completing out of order, or when a cancel action triggers. | `Steps` (`ComboStep`: action, completing events, time limit), `CancelActions` |

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

## Tap and hold on one key

Two bindings on F in the same context: Inspect with `TapTrigger` and Pick up with `HoldTrigger`
(`OneShot`). A quick tap triggers Inspect on release and cancels Pick up; a long press triggers
Pick up and cancels Inspect.

## Writing your own

```csharp
/// Each press flips the action on or off (e.g. toggle crouch).
[Tool, GlobalClass]
public partial class ToggleTrigger : InputTrigger
{
    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        var memory = context.Memory;          // per binding, per player — never fields on the resource
        if (actuated && !wasActuated) memory.Flag = !memory.Flag;
        return memory.Flag ? TriggerPhase.Triggered : TriggerPhase.Idle;
    }
}
```

Override `Role` to make a trigger implicit or a blocker. Use `context.Player.GetPhaseThisTick(action)`
or `GetEventsThisTick(action)` to depend on other actions.
