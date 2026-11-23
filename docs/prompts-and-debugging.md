# Button prompts and debugging

## Prompts

### For an action

```csharp
// "Press Space to jump" — or "Press A" once the player picks up a joypad.
label.Text = $"Press {player.GetPromptText(jump)} to jump";

foreach (ActionPrompt prompt in player.GetPrompts(jump))
    GD.Print($"{prompt.Text}  ({prompt.DeviceKind}, icon '{prompt.IconKey}')");
```

`GetPrompts(action, kind, style)` lists the sources that drive the action **right now**: bindings
in the player's active contexts, with rebinds applied; cleared slots and empty sources are left out,
and duplicates are merged.

- **Device kind.** Only sources of one kind are listed — `KeyboardMouse`, `Joypad` or `Touch`
  (virtual controls count as touch). By default that is the kind the player **last used**; if
  nothing matches, every source is listed.
- **Chords.** A binding with a `ChordTrigger` reads "Shift + Space": the chord action's prompt for
  the same kind of device, then the source. `SourceText` and `ChordText` give the parts.
- `GetPromptText(action, kind, separator)` joins the texts (" / " by default).

Refresh prompts when they can change:

```csharp
player.InputDeviceChanged += (kind, joypad) => UpdatePrompts();
player.BindingsChanged += UpdatePrompts;
```

### Last used device

Each player tracks the kind of device it last received a **deliberate** input from: a key or mouse
button press, mouse movement of at least `MouseMotionThreshold` pixels, a joypad button, a stick or
trigger past `JoyAxisThreshold`, or a touch. `LastDeviceKind`, `LastJoypad` and the
`InputDeviceChanged(kind, joypad)` signal report it. Touch-emulated mouse events are ignored.

### Joypad styles

Joypad names follow a `JoypadStyle`:

| Button / axis | Generic | Xbox | PlayStation | Nintendo |
|---|---|---|---|---|
| bottom face button | South | A | Cross | B |
| right face button | East | B | Circle | A |
| left face button | West | X | Square | Y |
| top face button | North | Y | Triangle | X |
| shoulders | Left/Right Shoulder | LB / RB | L1 / R1 | L / R |
| triggers | Left/Right Trigger | LT / RT | L2 / R2 | ZL / ZR |
| back / start | Back / Start | View / Menu | Create / Options | − / + |

The style is detected from the joypad's name (`InputPrompts.DetectStyle`); `player.JoypadStyle`
uses the last used joypad, or the player's first owned one. Set `player.JoypadStyleOverride` to let
players choose, or pass a style to `GetPrompts`.

### Source names

`InputPrompts.GetName(source, style)` names any source:

- keys as the current keyboard layout labels them — a physical-key binding on the key right of Tab
  reads "Q" on QWERTY and "A" on AZERTY (headless runs, which cannot ask the layout, use the
  position's US name);
- key composites as "W/A/S/D", mouse buttons as "Left Mouse", "Wheel Up";
- sticks as "Left Stick", half axes as "Left Stick Up", triggers per style;
- InputMap actions by their first event; touch as "Touch" / "Swipe"; virtual controls by name.

`KeyName`, `MouseButtonName`, `JoyButtonName`, `JoyAxisName` and `EventName` are available on
their own.

### Icons

No artwork ships with the plugin (icon packs have their own licences). A `PromptIconSet` resource
maps **icon keys** to your textures:

| Source | Icon key |
|---|---|
| key | `key_space`, `key_w`, `key_shift` (from the label shown) |
| key composite | `keys_w_a_s_d` |
| mouse | `mouse_left`, `mouse_wheel_up`, `mouse_motion` |
| joypad button | `joy_a`, `joy_left_shoulder`, `joy_start` (Godot's position-based names) |
| joypad axis | `joy_trigger_left`, `joy_left_x`, `joy_left_y_negative` |
| stick | `joy_left_stick`, `joy_right_stick` |
| touch, virtual, InputMap | `touch`, `touch_drag`, `virtual_<name>`, `action_<name>` |

Add style-specific entries as `xbox/joy_a`, `playstation/joy_a`, `nintendo/joy_a`, `generic/joy_a`;
they are tried before the plain key, then the set's `Fallback`. For a `RichTextLabel`:

```csharp
richLabel.Text = $"Press {icons.ToBBCode(player.GetPrompts(jump), height: 28)} to jump";
```

`ToBBCode` writes an `[img]` for prompts that have an icon and bold text for the rest.

## Debugging

### Text dump

```csharp
GD.Print(player.DescribeState());                     // one player
GD.Print(EnhancedInput.Instance.DescribeState(onlyActive: true));   // all players, active actions only
```

The dump lists the devices the player listens to and the last one used, its contexts by priority
(with *blocks lower* and the sources it is ignoring until released), a running rebind capture,
profile overrides, and every action with its phase, value, this tick's events, and elapsed /
triggered time.

### Overlay

Add an `InputDebugOverlay` node to any scene (or `AddChild(new InputDebugOverlay())`) and press
**F3**. It shows the dump for all players — or one, with `PlayerIndex` — refreshed every frame,
followed by the most recent events. `ToggleKey`, `StartVisible`, `OnlyActiveActions`,
`HistoryLength` and `FontSize` are exported; the toggle key is not consumed.
