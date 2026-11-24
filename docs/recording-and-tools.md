# Recording, tools and performance

## Recording and replay

A player can record everything it receives — events, virtual controls, injections — together with
each tick's time step, and play it back so the same action values, phases and events happen on the
same ticks. Use it for bug reports, automated tests of gameplay code, demos and attract modes.

```csharp
player.StartRecording(notes: "level 3, seed 42");
// … play …
InputRecording recording = player.StopRecording();
recording.Save("user://replays/bug_1234.json");

var loaded = InputRecording.Load("user://replays/bug_1234.json");
player.StartReplay(loaded);                 // each tick now plays one recorded tick
player.ReplayFinished += () => GD.Print("done");
```

- During a replay the recorded time step replaces the one the player is ticked with, and live
  input (events, on-screen controls, injections from code, backend devices, and live cut-offs such
  as focus loss or an unplugged pad) is ignored.
- Everything goes through one ordered stream: events, virtual controls, injections, backend device
  controls (with whether they are tracked), motion sensor samples (polled before the frame is
  closed, so they replay in the same frame), and cut-offs with their reasons (focus lost, device
  lost, tracking lost, `ClearRawInput`). Each input also keeps the moment within its tick it arrived
  (`At`), so time-based triggers replay the same.
- Raw input is cleared when a replay starts, and whatever the replay still holds is released (as
  `InputEndReason.Replay`) when it ends or is stopped. For an exact match, start recording while
  nothing is held (a level start, a menu) and replay into a player with the same contexts, mode,
  profile and settings. Context and mode changes made by game code are not recorded.
- Godot's InputMap state, which the player polls itself, is not recorded and not polled during a
  replay.
- `recording.ReplayInto(player)` plays a whole recording at once (tests).
- `StopReplay()` ends early. `IsRecording`, `IsReplaying` and `ReplayPosition` report progress.

The file is compact JSON: one entry per tick with its time step and inputs.

## Editor dock

With the plugin enabled, the **Enhanced Input** dock in the bottom panel shows:

- **Bindings** — every `BindingContext` resource in the project with its bindings: action, input
  (as a [source path](sources-and-devices.md), with a device icon), modifiers, triggers, groups and
  rebind id; bindings with problems are coloured. Search by action, input, group, rebind id or
  modifier. The side panel edits the selected binding: pick its action or create a new one (saved
  next to the context), type an input path (checked as you type) or press **Listen** and press a key
  or button or move a stick (Esc cancels), set groups and the rebind id, and add, remove or reorder
  modifiers and triggers (double-click one to tune it in the Inspector). The toolbar adds contexts and
  bindings. Every change can be undone and is saved at once.
- **Actions** — every action with its value kind, how many bindings use it and in which contexts.
- **Problems** — what `BindingValidator` finds (below). Double-click one to jump to its binding.
- **Devices** — the joypads connected now with their family ("DualSense", "Steam Deck"), name, GUID,
  USB ids and features; live axes and buttons; a rumble test and the light colour. Turn on **Log
  input** to see each event with its source path — handy for unusual hardware — and double-click a
  line to use it for the selected binding.

## Validation

`BindingValidator.Validate(contexts)` checks a set of contexts together and returns issues as
errors, warnings or notes:

- bindings without an action or input, empty modifier and trigger slots;
- chord, chord-blocker and combo triggers that point to no action, or to actions bound nowhere;
- one input driving two plain actions in the same context;
- a rebind id used by two different actions in one context;
- actions without a name, or two different actions with the same name;
- a one-axis input bound to a 2D or 3D action without a swizzle.

Running it in a test keeps a project's contexts clean:

```csharp
var contexts = new[] { GD.Load<BindingContext>("res://input/gameplay.tres"), GD.Load<BindingContext>("res://input/menu.tres") };
Assert.DoesNotContain(BindingValidator.Validate(contexts), i => i.Severity == IssueSeverity.Error);
```

## Performance

- **Compiled bindings.** A player reads its active bindings once — action, effective source, the
  channels the source reads, modifier and trigger lists — and reuses that until something changes:
  contexts added or removed, a priority change, rebinding, a mode switch, or bindings added to or
  removed from an active context. After changing a binding **in place** while its context is active
  (its source, modifiers, triggers or groups), call `context.NotifyBindingsChanged()` or
  `player.RefreshBindings()`.
- **No garbage per tick.** Steady-state ticks allocate nothing, including while actions fire their
  events. The test suite checks this with 1000 bindings over 500 actions (about 0.5 ms per tick in a
  debug build on a desktop machine).
- **Busy frames.** Each input event re-evaluates only the actions that read its channels (plus those
  whose triggers read other actions). The worst-case benchmark — 32 key events and mouse motion in
  every frame over the same 1000 bindings — runs at about 2 ms per frame, with intake under 1 µs
  per event (debug build). A tick evaluates at most `MaxStepsPerTick` (128) events one by one; more
  wait for the next tick.
- **Signals.** Each action event is also emitted as a Godot signal. If you only use `Bind` and
  `ActionEventRaised`, set `EmitActionSignals` off to skip that work.

## Safety

- `Tick` called from inside a tick — from a trigger, modifier or event callback — is ignored (once
  with a warning; counted in `ReentrantTicksIgnored`), so state never advances half-way through an
  evaluation.
- A chord cycle (A waits for B, B waits for A) cannot hang: each action sees the other's previous
  phase, so neither fires.
- Timers use the tick's delta: holds and taps take the same real time at 4 or 1000 ticks per
  second; zero or negative deltas do not advance them.
