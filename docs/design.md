# Godot Enhanced Input — design and requirements

This document describes what Godot Enhanced Input does, how it is built, and how each
requirement is checked. It is the reference for the staged plan at the end. It is written in
this plugin's own terms; names that appear here are the plugin's own API.

Godot Enhanced Input started as a fork of [InputForge](https://github.com/pandoraoyun/InputForge)
0.2.3 (MIT). Version 0.1.0 is a redesign with a new API; see [migration.md](migration.md).

## 1. Goals

1. Gameplay code asks for **actions** ("Jump", "Move", "Look"), never for keys or buttons.
2. Which device input drives which action is data: **binding contexts** made of resources,
   switched on and off at runtime with a priority.
3. Each action has a **typed value** (on/off, 1D, 2D or 3D) and a **phase** that moves through
   a small state machine every tick, raising events (started, ongoing, triggered, completed,
   canceled) that code can subscribe to or poll.
4. Raw values are shaped by **modifiers** (deadzones, scaling, curves, axis remaps…) and turned
   into phases by **triggers** (down, pressed, released, hold, tap, pulse, chords, combos…).
5. It plays well with the rest of Godot: the InputMap and `InputEventAction` drive it, physical
   key positions are the default, GUI focus keeps its input, and everything is testable
   headless without a window.

Non-goals for 0.1: a custom editor dock (the Inspector is enough), networking/replication.

## 2. Vocabulary

| Term | Meaning |
|---|---|
| **Source** | One physical or virtual input: a key, a mouse button, mouse motion, a joypad axis or stick, an InputMap action, a named virtual control. Resource: `InputSource`. |
| **Game action** | Something the player wants to do, with a value kind. Resource: `EnhancedInputAction`. |
| **Action binding** | "This source drives this action", plus binding-level modifiers and triggers. Resource: `ActionBinding`. |
| **Binding context** | A set of bindings that are switched on and off together. Resource: `EnhancedInputMappingContext`. Added to a player with a priority. |
| **Player** | One local player's input: its device filter, active contexts, raw input state and action states. Node: `InputPlayer`. |
| **System** | The autoload that owns the players, receives Godot input events and ticks the players. Node: `EnhancedInput`. |
| **Tick** | One evaluation of every action of a player (normally once per process or physics frame). |
| **Phase** | Per-tick result of an action's triggers: `Idle`, `Ongoing` or `Triggered` (`TriggerPhase`). |
| **Event** | A change between phases from one tick to the next: `Started`, `Ongoing`, `Triggered`, `Completed`, `Canceled` (`ActionEvents`, a flags enum). |
| **Actuated** | A value whose magnitude reaches a trigger's actuation threshold. |

## 3. Requirements

Each requirement has an ID, the behaviour it asks for, and the test that checks it. Tests live
in `GodotEnhancedInput.Tests` and run headless through 2dog. Stage numbers refer to §6.

### 3.1 Actions and values (stage 1)

| ID | Requirement | Test |
|---|---|---|
| ACT-1 | An `EnhancedInputAction` is a resource with a name, a value kind (`Digital`, `Axis1D`, `Axis2D`, `Axis3D`, `Axis4D`, `Quaternion`, `Euler`), action-level modifiers and triggers, a *consume input* flag (default on), a *trigger when paused* flag (default off) and an accumulation mode. | `ValueTests.GameAction_Defaults_AreSensible` |
| ACT-2 | An `ActionValue` carries the kind and a `Vector3`; it converts to `bool`, `float`, `Vector2` and `Vector3`. Shaping a raw value to a kind keeps only the components the kind has; a digital value holds the source's strength in X and reads as `true` when non-zero. | `ValueTests.*` |
| ACT-3 | When several bindings drive one action in the same tick, their values combine by the action's accumulation mode: **strongest** (per component, the value with the largest magnitude wins) or **sum** (components add up, so opposite keys cancel). | `PipelineTests.Accumulation_Strongest_PicksLargestPerAxis`, `PipelineTests.Accumulation_Sum_OppositeKeysCancel` |
| ACT-4 | Actions are identified by resource identity, not by name. Two different resources with the same name are two actions. | `PipelineTests.Actions_AreComparedByIdentity` |

### 3.2 Sources and raw input (stage 1)

| ID | Requirement | Test |
|---|---|---|
| SRC-1 | Keys match by **physical** key position by default; a source can choose to match the logical key code or the printed key label instead. | `RawInputTests.Key_MatchesPhysicalKeycode_ByDefault`, `RawInputTests.Key_LogicalMode_MatchesKeycode` |
| SRC-2 | Mouse buttons, mouse wheel (one tick of strength per notch), mouse motion (deltas summed over the tick, reset after it), mouse position, joypad buttons, single joypad axes and two-axis sticks are supported. | `RawInputTests.*` |
| SRC-3 | Two convenience composites map keys onto axes: a key pair onto one axis and four keys onto a 2D vector, using Godot's screen convention (up is −Y), so a composite and a stick give the same sign. | `RawInputTests.KeyAxis2D_FollowsGodotScreenConvention` |
| SRC-4 | A source of kind *InputMap action* reads a Godot InputMap action: from `InputEventAction` events and from `Input.GetActionRawStrength`, taking the stronger. | `InputMapTests.InputMapSource_FollowsInputEventAction`, `InputMapTests.InputMapSource_FollowsInputActionPress` |
| SRC-5 | An `InputEventAction` whose action name equals an `EnhancedInputAction`'s name drives that game action directly (as injected input), so tests and tools can press game actions without knowing their keys. Can be turned off. | `InputMapTests.InputEventAction_WithGameActionName_DrivesAction` |
| SRC-6 | No press is lost between ticks, and none is delayed: every on/off change of a channel is queued in arrival order, and one tick evaluates all of them in steps (each step the longest run in which no channel changes twice), so a press and release in one frame are both seen in that frame and changes on different channels keep their order. Per-frame amounts (mouse motion, scrolling, drags, gestures) belong to the last step. | `RawInputTests.TapWithinOneTick_IsSeenInThatTick_InOrder`, `RobustnessTests.TwoPressesBetweenTicks_AreTwoPresses_InOrder`, `CorrectnessTests.ChangesWithinAFrame_*`, `CorrectnessTests.ChordPressedInOrder_*`, `CorrectnessTests.DoubleTap_WithinOneFrame_*`, `CorrectnessTests.MouseMotion_IsInTheFramesFinalState_*` |
| SRC-7 | Key echo (OS auto-repeat) events are ignored. | `RawInputTests.EchoEvents_AreIgnored` |
| SRC-8 | Named **virtual controls** can be set from code (`SetVirtualControl`), for on-screen sticks, touch and tests. | `RawInputTests.VirtualControl_IsReadBySource` |
| SRC-9 | Joypad sources can be limited to one device or accept any device the player owns; disconnecting a joypad clears its state. | `RawInputTests.JoyButton_DeviceFilter`, `RawInputTests.JoyDisconnect_ClearsState`, `RobustnessTests.TwoPads_NeverCombineIntoADiagonal` |
| SRC-10 | Source resources hold no runtime state, so one resource can be shared by many bindings and players. | `PipelineTests.SharedSourceResource_IsIndependentPerPlayer` |

### 3.3 Contexts, priority and consumption (stage 1)

| ID | Requirement | Test |
|---|---|---|
| CTX-1 | Contexts are added to a player at runtime with an integer priority and removed again; adding a context that is already active updates its priority and options. Higher priority evaluates first; ties go to the most recently added. | `ContextTests.AddRemove_ChangesWhatIsBound`, `ContextTests.ReAdd_UpdatesPriority` |
| CTX-2 | Contexts are compared by resource identity. | `ContextTests.ContextsWithSameName_AreDistinct` |
| CTX-3 | When a binding's action has *consume input* on, its sources are hidden from every lower-priority context while the context is active (whether or not the source is currently pressed). Consumption keeps the device or finger: a source on one pad hides that pad only, a source on any pad hides every pad; logical and label keys consume as their physical key. Bindings in the same context are not affected. | `ContextTests.HigherContext_ConsumesSharedKey`, `ContextTests.NonConsumingAction_LetsKeyThrough`, `ContextTests.SameContext_BothBindingsSeeKey`, `RobustnessTests.Consuming*`, `RobustnessTests.LogicalAndPhysical*` |
| CTX-4 | A context can **block** everything below it: with its *block lower priority* setting on, all lower contexts read zero while it is active. | `ContextTests.BlockingContext_SilencesLowerContexts` |
| CTX-5 | A context can be added with *ignore held sources until released*: sources that are actuated when it is added read as zero in that context until they have been released once. | `ContextTests.IgnoreHeldUntilRelease` |
| CTX-6 | **Suppression cancels.** A binding whose context is blocked, whose sources are all consumed or ignored, or whose action does not run while paused is not evaluated: its trigger and modifier memories are reset and it adds neither value nor phase. An action left with nothing to evaluate goes idle as `Canceled`, with the reason in `ActionState.EndReason` (blocked, paused, context removed…), and never fires a release-type trigger because of it. | `ContextTests.RemovingContext_CancelsTriggeredAction_WithItsReason`, `RobustnessTests.Blocking_*`, `RobustnessTests.Consumption_And_ContextRemoval_*`, `RobustnessTests.Suppression_ResetsSmoothing`, `RobustnessTests.BlockedCombo_*` |
| CTX-7 | Signals and C# events report contexts being added and removed. | `ContextTests.Signals_AddedRemoved` |

### 3.4 Modifiers (stage 1)

A modifier changes a value. Binding modifiers run per binding, in order, before the binding's
triggers. Action modifiers run on the combined value, before the action's triggers. Modifiers
that need memory between ticks (smoothing) keep it per binding per player, never in the resource.

| ID | Modifier | Behaviour | Test |
|---|---|---|---|
| MOD-1 | `DeadzoneModifier` | **Axial**: each component below the lower bound becomes 0; between lower and upper bound it is rescaled to 0…1 keeping its sign; above the upper bound it is 1. **Radial**: the same on the vector's length, keeping its direction. | `ModifierTests.Deadzone_*` |
| MOD-2 | `ScaleModifier` | Multiplies per component. | `ModifierTests.Scale_*` |
| MOD-3 | `DeltaTimeScaleModifier` | Multiplies by the tick's delta time. | `ModifierTests.DeltaTimeScale_*` |
| MOD-4 | `NegateModifier` | Flips the sign of chosen components. | `ModifierTests.Negate_*` |
| MOD-5 | `SwizzleModifier` | Reorders components (all six orders). | `ModifierTests.Swizzle_*` |
| MOD-6 | `SmoothingModifier` | Averages the value over the last N ticks, or follows it exponentially at a set rate. | `ModifierTests.Smoothing_*` |
| MOD-7 | `ExponentCurveModifier` | `sign(v)·|v|^e` per component. | `ModifierTests.ExponentCurve_*` |
| MOD-8 | `CurveModifier` | Remaps each component through a Godot `Curve` (sampled on the magnitude, sign kept). | `ModifierTests.Curve_*` |
| MOD-9 | `NormalizeModifier` | Scales to unit length; tiny vectors become zero. | `ModifierTests.Normalize_*` |
| MOD-10 | `ClampModifier` | Clamps each component to a range. | `ModifierTests.Clamp_*` |
| MOD-11 | `WorldAxesModifier` | Places a 2D input onto a 3D plane (XZ ground or XY), so a stick or key composite becomes a world direction. | `ModifierTests.WorldAxes_*` |
| MOD-12 | `ViewRelativeModifier` | Rotates a world direction by the player's view (camera) basis, optionally yaw-only, so "forward" means "away from the camera". | `ModifierTests.ViewRelative_*` |
| MOD-13 | `FovScaleModifier` | Scales by the view camera's field of view relative to a reference, so aiming feels the same when zoomed. | `ModifierTests.FovScale_*` |

### 3.5 Triggers (stage 1)

A trigger looks at a value each tick and reports a phase. Each trigger has an actuation
threshold (default 0.5). Triggers that need memory (timers, counters) keep it per binding per
player, never in the resource.

| ID | Trigger | Behaviour | Test |
|---|---|---|---|
| TRG-1 | *(none)* | With no triggers, the binding is `Triggered` whenever its value is non-zero and `Idle` otherwise. | `TriggerTests.NoTriggers_TriggeredWhileNonZero` |
| TRG-2 | `DownTrigger` | `Triggered` every tick the value is actuated. | `TriggerTests.Down_*` |
| TRG-3 | `PressedTrigger` | `Triggered` only on the tick the value becomes actuated. | `TriggerTests.Pressed_*` |
| TRG-4 | `ReleasedTrigger` | `Ongoing` while actuated, `Triggered` on the tick it stops being actuated. | `TriggerTests.Released_*` |
| TRG-5 | `HoldTrigger` | `Ongoing` while actuated for less than the hold time, then `Triggered` (every tick after, or only once if *one shot*). Released early: back to `Idle`, which cancels. | `TriggerTests.Hold_*` |
| TRG-6 | `HoldReleaseTrigger` | `Ongoing` while actuated; `Triggered` on release if held at least the hold time; otherwise `Idle` (cancel). | `TriggerTests.HoldRelease_*` |
| TRG-7 | `TapTrigger` | `Ongoing` while actuated; `Triggered` on release if released within the tap time; held longer: `Idle` until released. | `TriggerTests.Tap_*` |
| TRG-8 | `PulseTrigger` | While actuated: `Triggered` on the first tick (optional), then once every interval, `Ongoing` in between; an optional pulse limit after which it stays `Idle` until released. | `TriggerTests.Pulse_*` |
| TRG-9 | `ChordTrigger` | Implicit. `Triggered` only while another action is triggered this tick. | `PipelineTests.Chord_*` |
| TRG-10 | `ChordBlockerTrigger` | Blocker. Stops the set from triggering while another action is triggered, so a plain action stays quiet while its chorded variant fires. | `PipelineTests.ChordBlocker_SilencesPlainAction` |
| TRG-11 | `ComboTrigger` | A list of steps (action + the event that completes the step + the time allowed since the previous step). `Ongoing` while partway; `Triggered` on the tick the last step completes; resets when a step times out, when a step's action is used out of order, or when a listed cancel action triggers. | `PipelineTests.Combo_*` |
| TRG-12 | **Roles.** Each trigger is *explicit*, *implicit* or a *blocker*. The explicit side is satisfied when any explicit trigger is triggered, or — with no explicit triggers — when the value is non-zero. The set is `Triggered` when the explicit side is satisfied, every implicit trigger is triggered and no blocker is. Otherwise it is `Ongoing` when the explicit side shows progress (an explicit trigger ongoing or triggered, or a non-zero value with no explicit triggers) or an implicit trigger is ongoing, else `Idle`. A triggered blocker forces `Idle`. | `TriggerTests.Roles_*` |
| TRG-13 | **Binding vs. action triggers.** A binding *fires* when it has no triggers or its triggers are triggered; only firing bindings add to the action's value (the unfiltered value is `RawValue`). Without action triggers, the phase is the strongest binding phase (a trigger-less binding counts as triggered while non-zero). With action triggers, they decide on the combined value — so release, tap and hold-release work at action level — and the action shows ongoing while a binding is partway. | `PipelineTests.ActionLevelTriggers_GateBindingPhase`, `RobustnessTests.ActionLevel_*`, `RobustnessTests.UnsatisfiedChord_AddsNoValue` |
| TRG-14 | Time-based triggers advance on ticks, not on events, so holding a key without new events still completes a hold or pulses. | `PipelineTests.Hold_CompletesWithoutFurtherEvents` |

### 3.6 Phase state machine and events (stage 1)

| ID | Requirement | Test |
|---|---|---|
| EVT-1 | Each tick the player compares an action's new phase with the previous one and raises: Idle→Ongoing: `Started`+`Ongoing`; Idle→Triggered: `Started`+`Triggered`; Ongoing→Ongoing: `Ongoing`; Ongoing→Triggered: `Triggered`; Triggered→Triggered: `Triggered`; Triggered→Ongoing: `Ongoing`; Ongoing→Idle: `Canceled`; Triggered→Idle: `Completed`; Idle→Idle: nothing. | `StateMachineTests.*` (one test per transition) |
| EVT-2 | An action's state reports its value, phase, the events of the last tick, the seconds since it started (elapsed) and the seconds spent triggered; both reset when it goes idle. | `StateMachineTests.Timing_ElapsedAndTriggeredSeconds` |
| EVT-3 | Code can subscribe per action and per event set with C# callbacks (`Bind` returns a handle that unsubscribes), and can poll (`GetValue`, `GetPhase`, `IsTriggered`, `WasStarted`, `WasTriggered`, `WasCompleted`, `WasCanceled`). | `EventTests.Bind_*`, `EventTests.Poll_*` |
| EVT-4 | The player node emits Godot signals for each event (`ActionStarted`, `ActionOngoing`, `ActionTriggered`, `ActionCompleted`, `ActionCanceled`). | `EventTests.Signals_AreEmitted` |
| EVT-5 | Callbacks run after the whole tick is evaluated, so a callback reading another action sees this tick's state. | `EventTests.Callbacks_SeeConsistentState` |
| EVT-6 | While the scene tree is paused, actions without *trigger when paused* receive no input (so they complete or cancel and stay idle). | `PipelineTests.Paused_OnlyPauseActionsRun` |
| EVT-7 | Injected input: `InjectInput(action, value)` feeds one tick of value; `StartInjecting`/`StopInjecting` feed it every tick. Injected values go through the action's own modifiers and triggers. | `EventTests.Inject_*` |

### 3.7 Godot integration (stage 1 unless noted)

| ID | Requirement | Test |
|---|---|---|
| GD-1 | The system never calls `SetInputAsHandled`; GUI keeps its input. By default, presses are read from `_UnhandledInput` (so a focused `LineEdit` keeps its keys) and anything that lowers a value (releases, axes moving toward zero) from `_Input`, so a key never sticks when focus changes. A policy setting reads everything from `_Input` instead. | `GuiTests.PressConsumedByGui_IsIgnored`, `GuiTests.ReleaseIsAlwaysSeen` |
| GD-2 | The tick runs once per frame right after input is delivered, before physics and process (`Auto`, default), or in `_Process`, `_PhysicsProcess`, or manually through `Tick(delta)`. The system processes while paused. | `RotationAndTickTests.AutoTick_*`, `PipelineTests.ManualTick_*`, `PipelineTests.System_RoutesEvents*` |
| GD-3 | Raw events can be fed directly (`FeedEvent`) for tests and replays. | used throughout |
| GD-4 | Touch: screen touch and drag sources, virtual stick and buttons. *(stage 3)* | stage 3 |
| GD-5 | Button-prompt names for a source and for an action's current bindings. | see PRM-1 … PRM-5 |
| DEMO-1 | The demo's saved resources load, and the demo scene runs (chords, tap/hold, combo, blocking menu). | `DemoTests.*` |

### 3.8 Player-mappable bindings and rebinding (stage 2)

A binding becomes **rebindable** by giving it a rebind id. Each player has an **input profile**
that can replace the source of any rebindable binding; the binding resources themselves are never
changed, so the designer's defaults stay intact and one profile can be reset at any time.

| ID | Requirement | Test |
|---|---|---|
| MAP-1 | `ActionBinding` has a rebind id (empty: not rebindable), a display name, a category and a list of former ids. A player lists its **rebind slots** — one per id, from its active contexts and any contexts registered for rebinding — with the action, the default source and the current source. | `RebindTests.Slots_*` |
| MAP-2 | The player's profile overrides the source of every binding with a given id, in every context, from the next tick. A slot can be cleared (no source), reset to its default, or all slots reset. Changes raise a signal. | `RebindTests.Rebind_*`, `RebindTests.Clear_*`, `RebindTests.Reset_*` |
| MAP-3 | Rebinding reports **conflicts**: other bindings whose sources read any of the same input channels — within the slot's own contexts by default, or in all known contexts. A policy decides what happens: allow, reject, swap (the first conflicting rebindable slot takes the old source) or clear the conflicting rebindable slots. Fixed bindings are reported, never changed. | `RebindTests.Conflict_*` |
| MAP-4 | `InputSource.FromEvent` builds a source from a press: key (physical by default), mouse button or wheel, joypad button, or joypad axis past a threshold (as a half axis in the pushed direction). Joypad axis sources can read the full axis or only its positive or negative half. | `CaptureTests.FromEvent_*`, `RawInputTests.JoyAxis_HalfRanges` |
| MAP-5 | **Capture.** `StartCapture(id, options)` turns the next qualifying press into the slot's source (through the conflict policy). Options choose the accepted devices, the cancel keys (Escape by default) and the axis threshold. The captured press never reaches actions, and is marked handled so the GUI does not also act on it — the one case where the plugin marks an event handled. Signals report the result or the cancellation. | `CaptureTests.*` |
| MAP-6 | Profiles **save** to and **load** from JSON (default `user://input/<ProfileId>.json`, where the profile id is given when a player is created and never changes with re-indexing) with a format number. Enum values are stored by name. Entries for unknown ids are kept, entries saved under a binding's former id apply to its current id, malformed entries are skipped with a warning, and a newer format is refused. | `ProfileTests.*`, `RobustnessTests.ProfileIds_SurviveReindexing` |
| MAP-7 | Rebinding a source that is held cancels the action (reason `BindingChanged`) instead of completing it, and half-done holds and smoothing histories of the binding start over. | `RebindTests.RebindWhileHeld_CancelsAction_WithoutARelease`, `CorrectnessTests.Rebinding_ForgetsHalfDoneHolds` |

### 3.9 Local players and touch (stage 3)

| ID | Requirement | Test |
|---|---|---|
| PLR-1 | `EnhancedInput` holds several players, each with its own contexts, raw input, action states and profile. Player 0 always exists; players added later do not listen to keyboard, mouse, touch or `InputEventAction` unless told to. | `PlayerTests.NewPlayers_ListenOnlyToTheirDevices` |
| PLR-2 | **Joypad assignment.** A joypad assigned to a player (`AssignJoypad`) reaches only that player; assigning it moves it from any other player. Joypads nobody owns reach only players with no joypads listed that accept unowned pads (player 0 by default). | `PlayerTests.AssignedJoypad_ReachesOnlyItsOwner`, `PlayerTests.UnassignedJoypads_GoToPlayersWithoutAList`, `RobustnessTests.AddedPlayers_*` |
| PLR-3 | **Joining.** With join mode *on button press*, a button press on an unowned joypad assigns it to the first player that has no joypad, or creates a new player (up to a maximum), raising `PlayerJoined`. The joining press does not reach actions unless configured to. | `PlayerTests.Join_*` |
| PLR-4 | Disconnecting a joypad clears its state on its owner and keeps the assignment for when it comes back; a *different* pad (by identity) that reconnects under the same device index does not inherit it. `JoypadConnectionChanged` and `JoypadUnassigned` report this. Removing a player releases its joypads and re-indexes the rest. | `PlayerTests.Disconnect_KeepsAssignment`, `PlayerTests.RemovePlayer_ReleasesJoypads_AndReindexes`, `RobustnessTests.ReconnectedPad*` |
| TCH-1 | **Touch sources.** A *Touch* source reads one finger or any finger: *pressed* (1 while down; a tap shorter than a tick is still seen once), *position* (viewport pixels), or *drag* (movement summed over the tick). Touch follows the same GUI rules as the mouse. | `TouchTests.TouchSource_*` |
| TCH-2 | **Virtual deltas.** Code can add to a virtual control for the current tick only (`AddToVirtualControl`), alongside its held value. | `TouchTests.VirtualDelta_LastsOneTick` |
| TCH-3 | **On-screen stick.** `VirtualJoystick` (a `Control`) follows one finger that starts inside it and writes a 2D value (−1…1, up is −Y) to a virtual control; optionally floating (centred where the finger lands); back to zero on release. | `TouchControlTests.Joystick_*` |
| TCH-4 | **On-screen button.** `VirtualButton` writes 1 while any finger that started on it is down, and 0 after. | `TouchControlTests.Button_*` |
| TCH-5 | **Touch zone.** `TouchZone` turns drags that start inside it into per-tick deltas on a virtual control (camera look on half the screen). | `TouchControlTests.Zone_*` |
| TCH-6 | On-screen controls handle several fingers at once, can also be driven by the mouse for desktop testing, can hide themselves where there is no touchscreen, and mark the events they use as handled so they do not also act as gameplay touches or clicks. | `TouchControlTests.MultiTouch_*`, `TouchControlTests.Mouse_*` |

### 3.10 Robustness (after review)

| ID | Requirement | Test |
|---|---|---|
| ROB-1 | Two-axis joypad sticks take both axes from one pad (for "any pad": the pad pushed furthest), never a mix of two pads. | `RobustnessTests.TwoPads_NeverCombineIntoADiagonal` |
| ROB-2 | Only player 0 polls Godot's global InputMap state by default (it bypasses device filters and the GUI); players added later read InputMap actions only from the events they receive. | `RobustnessTests.AddedPlayers_DoNotPollTheInputMap_OrTakeUnownedPads` |
| ROB-3 | Losing window or application focus, or being suspended, cuts off every player's held input and every on-screen control: actions end as canceled with a reason, never as a release. | `RobustnessTests.FocusLoss_*`, `CorrectnessTests.ReleaseTrigger_DoesNotFire_WhenInputIsCutOff` |
| ROB-4 | A stick reversal is a release of the old direction, read before the GUI, and a press of the new one, which must pass the GUI like any press. | `RobustnessTests.StickReversal_ReleasesBeforeTheGui_AndPressesOnlyIfTheGuiAllows` |
| ROB-5 | Rebind conflicts tell devices and fingers apart. | `RobustnessTests.Overlaps_RespectsDevices` |
| ROB-6 | `ChangedTrigger` fires on ticks the value changes (with an optional tolerance); `PointerSpaceModifier` converts pointer positions to normalized, centred, rect or 2D-world coordinates of the player's pointer viewport. | `RobustnessTests.ChangedTrigger_*`, `RobustnessTests.PointerSpace_Conversions` |

### 3.11 Prompts and debugging (stage 4)

| ID | Requirement | Test |
|---|---|---|
| PRM-1 | **Source names** for on-screen prompts: keys as the current layout labels them (physical keys are translated), mouse buttons and wheel, joypad buttons, axes, half axes and sticks, key composites, InputMap actions (their first event), touch and virtual controls. | `PromptTests.Names_*` |
| PRM-2 | **Joypad styles.** Button and axis names follow a style — generic, Xbox-like, PlayStation-like or Nintendo-like — detected from the joypad's name, or chosen per call. | `PromptTests.Style_*` |
| PRM-3 | **Last used device.** Each player tracks the kind of device it last received a deliberate input from (keyboard and mouse, joypad, touch) and the joypad index, ignoring tiny mouse and stick movements, and raises a signal when it changes. | `PromptTests.LastDevice_*` |
| PRM-4 | **Action prompts.** For an action, a player lists the current sources of its bindings in active contexts (rebinds applied, cleared slots skipped), each with text, an icon key, its device kind and joypad style; filtered to one device kind (by default the last used one, falling back to all when nothing matches). | `PromptTests.ActionPrompts_*` |
| PRM-5 | **Icons.** A `PromptIconSet` resource maps icon keys to textures, trying the style-specific key before the generic one, and turns a prompt into BBCode (an image, or bold text when there is no icon). No artwork ships with the plugin. | `PromptTests.Icons_*` |
| DBG-1 | **Text dump.** A player describes its state as text: devices and last used device, contexts in priority order with their flags and ignored sources, a running capture, profile overrides, and every action's phase, value, events and timings (optionally only active ones). The autoload describes all players. | `DebugTests.Dump_*` |
| DBG-2 | **Overlay.** `InputDebugOverlay` (a `CanvasLayer`) shows the dump for one or all players, refreshed every frame, with a short history of recent events, toggled by a key (F3 by default) without consuming it. | `DebugTests.Overlay_*` |

### 3.12 Extended feature set (stage 5)

| ID | Requirement | Test |
|---|---|---|
| SRC-1 | **More sources.** Mouse wheel as a per-tick scroll axis (trackpad pans included), pen pressure, tilt and eraser, motion sensors of pads and of the handheld device, joypad buttons and axes by raw index, controls of backend devices; touch-emulated mouse events ignored by default. | `DeviceTests.MouseWheel_*`, `DeviceTests.Pen_*`, `DeviceTests.MotionSensors_*`, `DeviceTests.RawPadIndices_*`, `DeviceTests.TouchEmulatedMouse_*` |
| SRC-2 | **Composites**: two- and four-part axes of any sources, held modifiers then a value ("Ctrl + S"), and the strongest of several. | `DeviceTests.Composite_*` |
| SRC-3 | **Text paths** for every source kind, round-tripping through parse and format, with aliases; all kinds round-trip through profile JSON. | `DeviceTests.Paths_*`, `DeviceTests.NewKinds_RoundTripThroughJson`, `GestureTests.Paths_AndJson_ForGestures` |
| SRC-4 | Each source has a value nature (state, delta, absolute) that modifiers can use. | `MoreModifierTests.DeltaTimeScale_LeavesDeltaValuesAlone`, `MoreModifierTests.Integrate_And_Differentiate` |
| DEV-1 | **Device registry** with connect and disconnect events, guessed roles, and device assignment for any device kind. | `DeviceTests.Registry_*`, `DeviceTests.Roles_*`, `DeviceTests.AssignDevice_*` |
| DEV-2 | **Device profiles** match by GUID or name and calibrate, invert and name axes and buttons. | `DeviceTests.DeviceProfile_*` |
| DEV-3 | **Backends** add devices whose controls route like joypads; an XR backend turns tracker inputs and poses into controls. | `DeviceTests.CustomBackend_*`, `DeviceTests.XRBackend_*` |
| DEV-4 | **Haptics**: rumble to the player's pads, joypad lights, handheld vibration, all through a replaceable output, scaled by the player's vibration strength. | `HapticsTests.Rumble_*` |
| TRG-1 | Multi-tap, toggle, repeat, threshold (with a band and direction), hysteresis, flick, distance and predicate triggers. | `MoreTriggerTests.*` |
| MOD-1 | Remap, sensitivity (player setting, inversion), acceleration, anti-deadzone, calibration, quantize, hysteresis, integrate, differentiate, DPI scale and gyro drift removal modifiers; a player-tunable deadzone. | `MoreModifierTests.*`, `ModeTests.DeadzoneSetting_LetsPlayersTuneIt` |
| MDE-1 | **Input modes** select binding groups; the mode follows the device used unless locked; modes can require devices; prompts follow the active mode. | `ModeTests.Groups_*`, `ModeTests.NoActiveMode_*`, `ModeTests.Modes_*`, `ModeTests.Prompts_*` |
| MDE-2 | **Player settings** live in the profile and fall back to the mode's defaults; hold-to-toggle per action; timing scales; presets apply as one change. | `ModeTests.Settings_*`, `ModeTests.HoldToToggle_*`, `ModeTests.TimingScales_*`, `ModeTests.Presets_*`, `MoreModifierTests.ProfileSettings_*` |
| MAP-8 | Rebinding one **part** of a composite, also by capture. | `MoreRebindTests.Parts_*`, `MoreRebindTests.Capture_OfOnePart` |
| MAP-9 | Capture ignores inputs held when it starts (sticks until centred), supports exclusions, filters, modifier chords and a timeout. | `MoreRebindTests.Capture_*` |
| MAP-10 | Several **named profiles** per player, presets to import and export, writes that cannot leave a half-written file; a swap reaches listeners as one change. | `MoreRebindTests.NamedProfiles_*`, `MoreRebindTests.Presets_*`, `MoreRebindTests.Swap_*` |
| GST-1 | **Gestures**: pinch, twist, two-finger drag and swipe from touches, trackpad pinch; long press and double tap as triggers on touch. | `GestureTests.Pinch_*`, `GestureTests.TrackpadMagnify_*`, `GestureTests.Rotate_*`, `GestureTests.Swipe_*`, `GestureTests.Gestures_WorkWithTriggers_*` |
| GST-2 | On-screen controls keep inside the **safe area**; **touch layouts** place, scale and fade controls, can be edited by dragging, and save as JSON. | `GestureTests.SafeArea_*`, `GestureTests.TouchLayout_*` |
| REC-1 | **Recording and replay**: a player's input and time steps are recorded tick by tick and replay to the same values, phases and events; live input is ignored during a replay; recordings save as JSON with every event kind. | `ReplayTests.*` |
| PRF-1 | Active bindings are **compiled** once and recompiled only when contexts, profile, mode or bindings change; steady-state ticks allocate nothing (1000 bindings). | `StressTests.Bindings_*`, `StressTests.ManyActionsAndContexts_*` |
| PRF-2 | A tick started from inside a tick is ignored; chord cycles neither hang nor fire; timers are frame-rate independent and ignore zero or negative deltas; reassigning a pad mid-hold releases it for the old owner without pressing for the new one. | `StressTests.Tick_*`, `StressTests.ChordCycle_*`, `StressTests.Hold_*`, `StressTests.ZeroAndNegativeDeltas_*`, `StressTests.Tap_*`, `StressTests.ReassigningAJoypadMidHold_*` |
| EDT-1 | **Validation** finds missing actions and inputs, empty slots, dangling chords and combos, conflicting plain bindings, ambiguous rebind ids, unnamed or same-named actions and axis mismatches. | `ValidatorTests.*` |
| EDT-2 | **Editor dock**: every binding context, searchable, with device icons and problems marked; a side panel edits a binding's action (or creates one), input path (validated as typed, or by listening), groups, rebind id, modifiers and triggers, with undo, saved at once; actions and problems lists; connected pads with family, ids, features, live axes and buttons, a rumble test and the light; an input log that shows each event's path. | `BindingEditingTests.*` (the dock's widgets are checked by hand and by loading it in the editor) |

### 3.13 Rotations, devices and correctness (stage 6)

| ID | Requirement | Test |
|---|---|---|
| VAL-1 | Values carry four components; `Axis4D` keeps all four, lower kinds drop the rest. `ActionState.InputValue` holds the untouched input before modifiers. | `RotationAndTickTests.Axis4D_*` |
| VAL-2 | **Rotation values**: `Quaternion` actions read normalized, the identity counts as no input and the magnitude is the angle as a fraction of half a turn; `Euler` actions hold radians; accumulation, deadzone, scale, negate and normalize are rotation-aware. | `RotationAndTickTests.Quaternion_*`, `RotationAndTickTests.ScaleRotation_*` |
| VAL-3 | **Rotation modifiers** convert between Euler angles (radians or degrees), quaternions and rotation vectors; offset a rotation by fixed or player-set angles; smooth it along the shortest arc at any frame rate. | `RotationAndTickTests.RotationConvert_*`, `RotationAndTickTests.RotationOffset_*`, `RotationAndTickTests.RotationSmoothing_*` |
| CUT-1 | **Cut-off input never releases.** Focus loss, suspension, disconnects, reassignment, context removal, mode and binding changes end held actions as `Canceled` with `ActionState.EndReason`; release-based triggers do not fire for them. | `CorrectnessTests.ReleaseTrigger_*`, `ContextTests.RemovingContext_CancelsTriggeredAction_WithItsReason`, `PipelineTests.Paused_OnlyPauseActionsRun` |
| CUT-2 | **Stuck input** whose release went elsewhere is released (never pressed) by comparing with Godot's own key and button state each tick. | `RotationAndTickTests.StuckInput_*` |
| ISO-1 | Backend device controls and polled sensors go through recording and are ignored during a replay; explicit pad indices (sensors, haptics, lights) respect ownership; action modifiers see the bindings' value nature; touch and keyboard reassignment cut off the previous player's input. | `CorrectnessTests.DeviceControls_*`, `CorrectnessTests.ExplicitPadIndices_*`, `CorrectnessTests.ActionModifiers_*`, `CorrectnessTests.TouchReassignment_*` |
| MDE-3 | Modes require device kinds, separate roles (wheel **and** pedals) and capabilities from devices that are connected and usable by the player. | `CorrectnessTests.Modes_CanRequireSeparateRoles_*` |
| HAP-1 | **Haptic effects** (`HapticEffect`: motor strengths, length, attack, release, envelope, repeats, loop, frequency, trigger motors) play per device and end on their own; several mix per motor (strongest, sum, or override by priority); the vibration setting scales running effects; effects go only to the player's own devices; actions can play an effect on an event. | `HapticsTests.*` |
| FFB-1 | **Force feedback** (constant, spring, damper, friction, jolt) reaches the player's wheels through backends or an output, scaled by a player setting. | `RotationAndTickTests.ForceFeedback_*` |
| FAM-1 | **Controller families** (Xbox 360, One/Series, Elite, PlayStation 3/4/5/Edge, Switch Pro, Joy-Con, Steam Deck, Steam Controller, Steam Input) are recognised by name or USB ids, with hardware features, display names and prompt styles. | `DeviceRuntimeTests.Families_*`, `DeviceRuntimeTests.RegisteredPads_*` |
| DEV-5 | **Device identity** survives reconnecting, also under another index (serial number, else identity); identical pads get separate identities; a different pad never inherits an assignment; the registry is checked against Godot every few seconds. | `DeviceRuntimeTests.Reconnecting_*`, `DeviceRuntimeTests.IdenticalPads_*`, `DeviceRuntimeTests.ADifferentPad_*` |
| DEV-7 | A returning pad is matched by serial number and **model** (vendor and product ids grouped by family), not by GUID, so switching between a cable and Bluetooth keeps its identity and owner; ownership follows the pad, not the index, so a pad of another model on a freed index takes nothing and the missing pad still returns wherever it reconnects. | `ReconnectTests.Guids_*`, `ReconnectTests.SwitchingBetweenCableAndBluetooth_*`, `ReconnectTests.APadOfAnotherModel_*`, `ReconnectTests.UnassigningAMissingPad_*`, `ReconnectTests.BeingToldTwice_*` |
| DEV-8 | **Pads that cannot be told apart** (same model, no serial): with several candidates the pad goes to the player who lost theirs first and the game is told (`Automatic`), or it waits unassigned with its input reaching nobody (`Ask`); the game can settle or correct it with `ResolveJoypadReconnect`. | `ReconnectTests.TwoIdenticalPadsLost_*`, `ReconnectTests.Ask_*` |
| DEV-6 | **Device switching**: each player tracks the device it is using (pads, keyboard and mouse, touch, backend devices); losing it falls back to another usable device; `PlayerDeviceLost` and `PlayerDeviceRegained` report it. | `DeviceRuntimeTests.Switching_*`, `DeviceRuntimeTests.LosingTheCurrentPad_*`, `DeviceRuntimeTests.Reconnecting_*` |
| XR-1 | **XR**: controller and head poses as position, orientation (quaternion), Euler rotation and velocities; hands (joints, pinch, grab, tracked); face (eye gaze, blinks, jaw); per-hand vibration at the effect's frequency. | `XRAndEyeTests.XRPose_*`, `XRAndEyeTests.XRHand_*`, `XRAndEyeTests.XRFace_*`, `DeviceTests.XRBackend_*` |
| EYE-1 | **Desktop eye trackers** through a small adapter (`IEyeTracker`): gaze (screen, pixels, centred), validity, presence and head pose as device controls; the device disconnects when the tracker is gone; a mouse-driven simulator for development. `DwellTrigger` selects by looking. | `XRAndEyeTests.EyeTracker_*`, `XRAndEyeTests.Dwell_*` |
### 3.14 Stabilization (third review)

| ID | Requirement | Test |
|---|---|---|
| EVT-1 | **Event-accurate time.** A tick evaluates input events one at a time; before each, time passes for the actions it touches under the input as it was; each action's time adds up to the frame's delta. Changes are placed by arrival time when ticking in real time. A long press within one slow frame is not a tap. | `ReviewThreeTests.ALongPressWithinOneSlowFrame_*`, `ReviewThreeTests.WithoutArrivalTimes_*` |
| EVT-2 | **Once per frame.** Held actions raise steady events at most once per frame whatever else happens; one-shots stay triggered to the frame's end. | `ReviewThreeTests.AHeldAction_FiresOncePerFrame_*`, `EventTests.Poll_ReflectsLastTick` |
| EVT-3 | **Order across channels.** Each input event is its own step, and actions that read other actions see only the current step's events, so combos keep their order within a frame. | `ReviewThreeTests.ACombo_SeesItsStepsInOrder_*`, `CorrectnessTests.ChordPressedInOrder_*` |
| EVT-4 | **Held through hiding.** A binding that returns from being hidden with its key held counts it as held all along (no fresh press); callbacks that change contexts mid-tick take effect for the rest of the tick. | `ReviewThreeTests.AKeyHeldThroughAMenu_*`, `DemoTests.DemoScene_Runs_AndOpensTheMenu` |
| CUT-3 | Cut-offs are queued changes with reasons; injected input and touches the system canceled are cut off too. | `ReviewThreeTests.InjectedInput_*`, `ReviewThreeTests.ATouchTheSystemCanceled_*` |
| REC-2 | Recordings carry cut-offs, sensor samples in the right frame, tracked device controls and each input's moment within its tick; live cut-offs do not reach a replay; a replay releases what it held when it ends. | `ReviewThreeTests.CutOffsAndArrivalMoments_*`, `ReviewThreeTests.AReplayThatEndsWithAKeyDown_*`, `CorrectnessTests.DeviceControls_*` |
| TRK-1 | **Tracked values**: tracked controls are input while present, even at identity or origin; losing tracking cancels with `TrackingLost`; `GetSample` reports value, tracking, validity and change time. | `ReviewThreeTests.ATrackedIdentity_*` |
| HAP-2 | Haptics follow ownership (transfers stop the old owner first, effects on a lost device end), per-device stops leave wildcard effects off that pad, slow changes are sent, lights and trigger motors only reach capable pads, adaptive trigger resistance is separate. | `ReviewThreeTests.GivingAPadAway_*`, `ReviewThreeTests.StoppingOnePad_*`, `ReviewThreeTests.ASlowRamp_*`, `ReviewThreeTests.LightsTriggerMotorsAndResistance_*` |
| DEV-9 | **Ownership by identity**: a device's owner is kept on its registry record (`InputDeviceInfo.Owner`), connected or missing; players list their devices by persistent id (`GetOwnedDevices`, `OwnedDeviceIds`); joypad indices only route input. | `ReconnectTests.Ownership_*` |
| BKD-1 | Backend devices have persistent ids separate from runtime indices; two backends with the same type and index stay apart; a replacement backend takes a device over. | `ReviewThreeTests.TwoBackends_*`, `ReviewThreeTests.AReplacementBackend_*` |
| PRF-3 | The worst case — many events per frame over 1000 bindings — stays within a frame budget; intake cost per event is measured. | `StressTests.WorstCase_*` |

### 3.15 Fourth review and the public API (stage 8)

| ID | Requirement | Test |
|---|---|---|
| ANA-1 | Analog channels keep their turning points within a frame (`AnalogSampling`: `Latest`, `Extremes`, `All`), so a threshold crossed and recrossed within one frame is seen; resting noise within `AnalogBand` makes no steps. | `ReviewFourTests.ATriggerSqueezed*`, `ReviewFourTests.AnalogSampling_*`, `ReviewFourTests.RestingNoise_*` |
| TRK-2 | Tracking gained and lost are queued changes, also at the origin, evaluated and recorded in order. | `ReviewFourTests.APositionAtTheOrigin_*` |
| HAP-3 | Adaptive trigger resistance and force feedback are released on transfer, player removal, focus loss and suspension, and follow the strength settings at once. | `ReviewFourTests.ResistanceAndForces_*`, `ReviewFourTests.RemovingAPlayer_*` |
| DEV-10 | Assigning a backend device cuts it off for every other player, including implicit recipients. | `ReviewFourTests.AssigningAnUnownedDevice_*` |
| BKD-2 | Backend ids stay unique for any number of backends in one namespace; input from removed or superseded devices is ignored. | `ReviewFourTests.ThreeBackendsInOneNamespace_*` |
| API-1 | **Shortcuts**: actions answer for themselves on the default player (or a given one); short factories; contexts bind from text paths; binding helpers for common triggers and modifiers. | `ApiTests.ActionsAnswerForThemselves_*` |
| API-2 | **Stable ids** on actions, contexts, bindings, modes and configurations, with former ids; lookups and recordings use them. | `ApiTests.Ids_*` |
| API-3 | **Input configurations** set a player (or every player) up and switch contexts by id. | `ApiTests.AConfiguration_*`, `ApiTests.TheAutoloadsConfiguration_*` |
| API-4 | **Provenance**: events carry the binding, exact channel, device, timestamp and end reason. | `ApiTests.Events_SayWhich*` |
| API-5 | **Evaluation policies**: continuous, on change, event-driven, pass-through, tracked. | `ApiTests.Policies_*`, `ApiTests.PassThrough_*`, `ApiTests.Tracked_*` |
| API-6 | **Lifetimes**: context handles, scopes tied to a node, per-action enable. | `ApiTests.ScopesAndHandles_*`, `ApiTests.ASingleAction_*` |
| API-7 | **Device layouts**: control descriptors for gamepads (by family), keyboard and mouse, touch, pens and backend devices; every path binds. | `ApiTests.Layouts_*` |

## 4. Architecture

```
Godot events ──► on-screen controls (VirtualJoystick, VirtualButton, TouchZone: take their touches,
   │              write virtual controls)
   ▼
                 EnhancedInput (autoload node)
                   │ routes joypad events to the joypad's owner (joining on a button press if
                   │ enabled), other events to the players that accept that kind of device
                   ▼
                 InputPlayer ── rebind capture (takes the next press, if running)
                   │         ── last used device (for prompts)
                   │         ── RawInputState (per source: current value, queued on/off changes,
                   │                           motion and touch-drag accumulators, virtual controls)
                   │         ── InputProfile (rebind id → replacement source; JSON on disk)
                   │ Tick(delta):
                   │   1. order active contexts (priority, then most recent)
                   │   2. read each binding's sources; zero them if consumed/blocked/ignored
                   │   3. per action, lazily: binding modifiers → binding triggers →
                   │      combine bindings → action modifiers → action triggers → phase
                   │   4. phase change → events, timings
                   │   5. dispatch callbacks and signals; clear per-tick raw data
                   ▼
                 ActionState (per action per player): value, phase, events, timings
                   │
                   ▼
                 read by gameplay (polling, callbacks, signals), prompts (GetPrompts) and the
                 debug dump / overlay
```

Key decisions:

* **Tick-driven.** Events only update the raw state. All evaluation happens in the tick, so
  time-based triggers work while nothing new arrives.
* **Resources are definitions only.** `EnhancedInputAction`, `EnhancedInputMappingContext`, `ActionBinding`,
  `InputSource`, modifiers and triggers hold settings. Per-player memory (raw values, trigger
  timers, smoothing buffers, action states) lives in the player, keyed by resource identity.
  Modifiers and triggers receive a small memory object they may use.
* **Consumption is decided before evaluation.** Which sources are consumed depends only on the
  active contexts, never on this tick's trigger results. That lets actions be evaluated lazily
  in any order, which is what chord and combo triggers need (they ask for another action's
  phase this tick; a dependency cycle falls back to last tick's phase).
* **Value shape.** Sources yield a `Vector3`. It is shaped to the action's kind before binding
  modifiers run. A digital action keeps the source strength in X, so trigger thresholds work
  with analog triggers.
* **Rebinding never edits resources.** A binding's source is resolved through the player's
  profile every tick (`GetEffectiveSource`), so defaults stay intact, profiles are per player, and
  consumption and prompts follow rebinds automatically.
* **Few exceptions to "never handled".** The autoload never marks events handled except a press
  taken by a rebind capture; on-screen controls mark the touches they use. Everything else reaches
  the GUI and other nodes.
* **Godot conventions.** Up is −Y for 2D input (as `Input.GetVector` does); `WorldAxesModifier`
  maps X/Y onto X/Z, so "up on the stick" becomes −Z, Godot's forward.

## 5. Public API sketch

```csharp
// setup
var player = EnhancedInput.Instance.GetPlayer(0);      // player 0 always exists
player.AddContext(gameplayContext, priority: 0);
player.AddContext(menuContext, priority: 10, new ContextOptions { IgnoreHeldSourcesUntilReleased = true });

// subscribe
using var handle = player.Bind(jump, ActionEvents.Triggered, e => Jump());
player.Bind(move, ActionEvents.Triggered | ActionEvents.Completed, e => _move = e.Value.AsVector2());

// poll
Vector2 look = player.GetValue(look).AsVector2();
if (player.WasTriggered(jump)) Jump();

// tests
player.FeedEvent(new InputEventKey { PhysicalKeycode = Key.Space, Pressed = true });
player.Tick(1.0 / 60.0);
```

## 6. Staged plan

All eight stages are done; the API is frozen for 0.1.0.

| Stage | Content | Done when |
|---|---|---|
| **1 — Core** | Rename to Godot Enhanced Input; actions and typed values; sources incl. physical keys, InputMap actions and `InputEventAction`; contexts with priority, consumption and blocking; all modifiers in §3.4; all triggers in §3.5 with roles; tick-driven phase state machine with events and timings; C# callbacks, polling and signals; GUI-safe event intake; demo scene; docs. | Every stage-1 test in §3 passes; docs and migration notes written. |
| **2 — Rebinding** | Rebindable bindings and slots, per-player profiles, conflict policies, capture-a-press rebinding, half-axis joypad sources, JSON save/load; a rebinding menu in the demo. | MAP-* tests pass. |
| **3 — Players and touch** | Multiple players with device filters and joypad assignment; screen touch/drag sources; on-screen stick and buttons writing virtual controls. | PLR-1, GD-4 tests pass. |
| **4 — Prompts and debug** | Prompt names, joypad styles, last used device, action prompts, icon sets; debug text dump and overlay. | PRM-* and DBG-* tests pass. |
| **5 — Extended** | More sources and composites, text paths, value natures; device registry, profiles, backends, XR, haptics; more triggers and modifiers; input modes, player settings, accessibility; composite-part rebinding, capture options, named profiles; gestures, safe area, touch layouts; recording and replay; compiled bindings and allocation-free ticks; validation and the editor dock; CI on three platforms and a hardware checklist. | SRC-*, DEV-*, TRG-1, MOD-1, MDE-*, MAP-8…10, GST-*, REC-1, PRF-*, EDT-1 tests pass. |
| **6 — Rotations, devices, correctness** | Four-component, quaternion and Euler values and rotation modifiers; ordered evaluation of every change within a frame; cut-off input with end reasons; stuck-input release; Auto ticking; isolation fixes; haptic effects and force feedback; controller families; device identity, reconnection, switching and fallback; XR hands, face and eyes; desktop eye trackers and dwell; editing in the editor dock. | VAL-*, CUT-*, ISO-1, MDE-3, HAP-1, FFB-1, FAM-1, DEV-5…8, XR-1, EYE-1, EDT-2 tests pass. |
| **7 — Stabilization** | Event-accurate time and once-per-frame steady events; cut-offs everywhere; recording alignment; tracked values; haptics ownership and trigger kinds; backend device identity; worst-case benchmark. | EVT-*, CUT-3, REC-2, TRK-1, HAP-2, DEV-9, BKD-1, PRF-3 tests pass. |
| **8 — API and review four** | Analog turning points, queued tracking changes, persistent outputs on transfer, implicit device recipients, backend id uniqueness; shortcuts, stable ids, input configurations, provenance, evaluation policies, scopes, device layouts. | ANA-1, TRK-2, HAP-3, DEV-10, BKD-2, API-* tests pass. |
