# Local players and touch

## Players

The `EnhancedInput` autoload holds one `InputPlayer` per local player. Each player has its own
contexts, raw input, action states and rebinding profile. Player 0 always exists
(`EnhancedInput.DefaultPlayer`).

```csharp
var system = EnhancedInput.Instance;
InputPlayer second = system.AddPlayer();
second.AddContext(gameplay);
```

| Player setting | Player 0 | Players added later |
|---|---|---|
| `AcceptKeyboardMouse` | on | off |
| `AcceptTouch` | on | off |
| `AcceptActionEvents` (`InputEventAction`) | on | off |
| `PollInputMap` (Godot's global InputMap state) | on | off |
| `ReceivesUnownedJoypads` | on | off |
| `JoypadDevices` | empty | empty |

Turn the flags on for whichever player should use the keyboard (for example, two players sharing
one keyboard with different contexts).

### Joypads

A joypad belongs to at most one player:

```csharp
system.AssignJoypad(second, device: 1);   // takes it from anyone else
system.UnassignJoypad(1);
InputPlayer owner = system.GetJoypadOwner(1);
```

- A joypad that a player owns reaches only that player.
- A joypad nobody owns reaches players whose `JoypadDevices` list is empty and that accept unowned
  pads (`ReceivesUnownedJoypads`: player 0 only, by default) — so a single-player
  game needs no setup at all.
- When a joypad disconnects, its state is cleared (its actions complete) but its owner keeps it,
  so it works again when it reconnects. If a *different* pad (by its identity) connects under that
  device index, the assignment is dropped (`JoypadUnassigned`) instead of being inherited.
  `JoypadConnectionChanged(device, connected)` reports connections.
- `RemovePlayer` releases the player's joypads and re-indexes the others (player 0 cannot be removed).

Joypad sources with device −1 read any joypad the player owns, so the same context works for every
player.

### Joining

```csharp
system.JoinMode = JoinMode.OnButtonPress;
system.MaxPlayers = 4;
system.PlayerJoined += (player, device) => SpawnCharacterFor(player);
```

When a button is pressed on a joypad nobody owns, it is given to the first player without joypads
(player 0 at first), otherwise to a new player, up to `MaxPlayers`. `PlayerJoined` is raised either
way — check whether you already have a character for that player. With `ConsumeJoinPress` (on by
default) the joining press does not also trigger an action. The default `JoinMode.Manual` assigns
nothing on its own.

The demo uses this: press a button on a second joypad and a second avatar appears.

### Split screen

View-dependent modifiers use each player's own view: set `player.ViewCamera` (or
`ViewBasisOverride`) per player. Rebinding profiles are per player too; the default file is
`user://input/<ProfileId>.json`; the profile id is given when the player is created and does not
change when other players leave.

## Touch

### Touch sources

An `InputSource` of kind `Touch` reads a finger:

| `TouchChannel` | Value |
|---|---|
| `Pressed` | 1 while the finger is down (no tap is lost between ticks) |
| `Position` | the finger's position in viewport pixels |
| `Drag` | the finger's movement in pixels, summed over the tick |

`TouchIndex` picks a finger by Godot's touch index, or −1 for any: *pressed* is any finger down,
*position* is the lowest-numbered finger that is down, *drag* is the sum of all fingers. Touch
follows the same GUI rules as the mouse: touches a GUI control accepts are not gameplay input, and
releases are always seen.

Note that Godot's *Emulate Mouse From Touch* project setting (on by default) also sends a mouse
event for each touch, so mouse sources react to touches too unless you turn it off.

### On-screen controls

Three `Control` nodes write to [virtual controls](godot-integration.md#virtual-controls), which you
bind like any other source:

| Control | Writes | Bind with |
|---|---|---|
| `VirtualJoystick` | a 2D value, length up to 1, up is −Y (like a real stick); zero on release. `Floating` centres the stick where the finger lands. | `InputSource.ForVirtual("touch_move")` on an `Axis2D` action |
| `VirtualButton` | 1 while any finger that started on it is down, else 0 | `ForVirtual("touch_jump")` + `PressedTrigger` (or any trigger) |
| `TouchZone` | the drag of a finger that started inside it, per tick, × `Sensitivity`; optionally 1 to `PressedControlName` while touched | `ForVirtual("touch_look")` on an `Axis2D` look action |

Common settings: `ControlName` (the virtual control), `PlayerIndex` (or `TargetPlayer` in code),
`AcceptMouse` (drive it with the left mouse button for desktop testing; touch-emulated clicks are
ignored), `Visibility` (`Always` or `TouchscreenOnly`).

Lay them out with anchors like any GUI, for example a floating stick on the left half of the
screen and a `TouchZone` on the right half for camera look. Each control follows fingers that start
inside it, so several work at once. They read `_Input` and mark the events they use as **handled**,
so a finger on the stick is not also a gameplay touch or a mouse click; touches elsewhere pass
through. Hiding a control releases whatever it was holding.

```csharp
gameplay.Add(move, InputSource.ForVirtual("touch_move"));
gameplay.Add(look, InputSource.ForVirtual("touch_look"), new ScaleModifier { Scale = new Vector3(0.2f, 0.2f, 1f) });
gameplay.Add(jump, InputSource.ForVirtual("touch_jump"), new PressedTrigger());
```

Your own widgets can do the same with `player.SetVirtualControl(name, value)` (held values) and
`player.AddToVirtualControl(name, delta)` (values for the next tick only), or by deriving from
`TouchControl`.

### Gestures

`Gesture` sources read the fingers together (and a laptop trackpad's pinch):

| `GestureChannel` | Value (per tick) | Path |
|---|---|---|
| `Pinch` | relative change of the distance between two fingers (+1 = twice as far apart); trackpad pinch adds `factor − 1` | `gesture:pinch` |
| `Rotate` | change of the angle between two fingers, in radians | `gesture:rotate` |
| `TwoFingerDrag` | movement of the two fingers' midpoint, in pixels | `gesture:two_finger_drag` |
| `Swipe` | for one tick after a quick one-finger flick: the unit direction (X or Y) | `gesture:swipe` |

A swipe needs `SwipeMinDistance` (60 px) and a release speed of `SwipeMinSpeed` (400 px/s); slower
movement is a drag. Long press and double tap are ordinary triggers on a touch source:
`touch:pressed` with a `HoldTrigger` or a `MultiTapTrigger`. Laptop two-finger scrolling arrives as
`mouse:wheel`.

### Safe area

On-screen controls keep clear of notches, rounded corners and system bars: with `KeepInSafeArea`
(on by default) a control moves — and shrinks if it must — into the display's safe area when it
appears. Call `FitToSafeArea()` after changing layout; `GetSafeArea()` gives the area in viewport
pixels, and `SafeAreaOverride` sets it by hand (tests, custom frames).

### Touch layouts

Put on-screen controls under a `TouchLayoutRoot` to let players move, resize and fade them:

```csharp
var root = GetNode<TouchLayoutRoot>("TouchControls");
root.Layout = TouchLayout.LoadJson("user://input/touch_layout.json") ?? root.Capture();
root.Apply();

root.EditMode = true;          // controls stop acting; drag them with a finger or the mouse
// … player arranges …
root.EditMode = false;
root.Capture().SaveJson("user://input/touch_layout.json");
```

A `TouchLayout` stores each control's rect by node name as fractions of the screen, so it survives
resolution and orientation changes, plus a `Scale` for every control (bigger buttons) and an
`Opacity`.
