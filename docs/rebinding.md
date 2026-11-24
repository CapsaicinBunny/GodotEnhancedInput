# Rebinding

Players can change which input drives a binding. The binding resources are never modified: each
player has an `InputProfile` that replaces the source of chosen bindings, so defaults stay intact
and a profile can be reset or swapped at any time.

## 1. Make bindings rebindable

Give a binding a **rebind id** (Inspector: *Rebinding* group on the `ActionBinding`):

| Field | Meaning |
|---|---|
| `RebindId` | Stable id, e.g. `jump_key`. Empty means the binding is fixed. |
| `RebindDisplayName` | Name for your menu (falls back to the action's display name). |
| `RebindCategory` | Group, e.g. *Keyboard*, *Gamepad*, *Combat*. |
| `RebindFormerIds` | Ids this binding used to have, so old saved profiles still apply after a rename. |

Bindings that share an id are rebound together — across contexts too. In the demo, Jump and Dash
both use Space and share `jump_key`; Pause (gameplay context) and Resume (menu context) share
`pause_key`.

In code:

```csharp
gameplay.Add(jump, InputSource.ForKey(Key.Space), new PressedTrigger()).Rebindable("jump_key", "Jump", "Keyboard");
gameplay.Add(jump, InputSource.ForJoyButton(JoyButton.A), new PressedTrigger()).Rebindable("jump_pad", "Jump", "Gamepad");
```

For a "secondary key" slot, add a second binding whose source has kind *None*: it does nothing until
the player binds it.

## 2. List the slots

```csharp
player.RegisterRebindContext(gameplay);   // list it even while a menu context is on top or it is inactive
foreach (RebindSlot slot in player.GetRebindSlots())
    GD.Print($"{slot.Category} {slot.DisplayName}: {slot.CurrentSource?.Describe() ?? "—"} {(slot.IsOverridden ? "*" : "")}");
```

A slot has its id, display name, category, action, default source, current source, whether it is
overridden, and every binding (with its context) that shares the id. Slots come from the active
contexts (by priority) and the registered ones.

## 3. Rebind

```csharp
RebindResult r = player.Rebind("jump_key", InputSource.ForKey(Key.J), RebindConflictPolicy.Swap);
player.ClearRebind("jump_pad");      // no source
player.ResetRebind("jump_key");      // back to the default
player.ResetAllRebinds();
```

Changes apply from the next tick, to every binding with the id. If the slot's input is held at that
moment, its action simply completes. Assigning the default source removes the override, so profiles
only store what differs.

### Conflicts

Before changing anything, `Rebind` looks for other bindings whose current source reads any of the
same input channels (`player.FindConflicts` does the same without changing anything). Opposite
halves of one joypad axis do not conflict.

| `ConflictScope` | Looks in |
|---|---|
| `SameContext` (default) | contexts that contain the slot |
| `AllContexts` | every active or registered context |

| `RebindConflictPolicy` | Effect |
|---|---|
| `Allow` (default) | rebind; conflicts are reported in `RebindResult.Conflicts` |
| `Reject` | do nothing if there is any conflict |
| `Swap` | rebind; the first conflicting rebindable slot gets this slot's old source |
| `ClearOthers` | rebind; conflicting rebindable slots are cleared |

Fixed bindings are reported but never changed.

## 4. Capture the next press

```csharp
player.StartCapture("jump_key", new CaptureOptions
{
    ConflictPolicy = RebindConflictPolicy.Swap,
    Joypad = false,               // keyboard and mouse only for this slot
});
player.RebindCaptured += (id, source) => GD.Print($"{id} = {source.Describe()}");
player.RebindCaptureCanceled += id => GD.Print("canceled");
player.RebindCompleted += result => UpdateMenu(result);   // C# event with the full result
```

The next press the player receives becomes the slot's source: a key (physical position by default),
a mouse button or wheel notch, a joypad button, or a joypad axis pushed past `AxisThreshold` (bound as
the half axis in that direction). Escape cancels (`CancelKeys`, `CancelJoyButtons`). Joypad input is
bound to any of the player's joypads unless `AnyJoypad` is off.

The captured press never reaches an action, and the autoload marks it **handled** so the focused
button does not also react to it — the only time the plugin marks an event handled. Releases,
mouse motion and other input pass through as usual. `InputSource.FromEvent(event)` gives the same
conversion if you capture input yourself.

## 5. Save and load

```csharp
player.LoadProfile();          // user://input/<ProfileId>.json, if it exists
player.BindingsChanged += () => player.SaveProfile();
```

`SaveProfile(path)` / `LoadProfile(path)` accept any path; `InputProfile.ToJson()` /
`InputProfile.FromJson(text, warnings)` work with strings (cloud saves, settings files). A file
looks like this:

```json
{
  "format": 1,
  "name": "default",
  "overrides": [
    { "id": "jump_key", "source": { "kind": "Key", "key": "J", "keyMatch": "Physical" } },
    { "id": "jump_pad", "source": { "kind": "None" } }
  ]
}
```

- Enum values are stored by name.
- Entries for ids no current binding uses are kept (their contexts may not be loaded yet).
- An entry saved under one of a binding's `RebindFormerIds` moves to its current id.
- Malformed entries are skipped with a warning; a file with a newer `format` is refused and the
  current profile kept.
- Profiles are plain JSON, never Godot resources, so loading one cannot run code.

Each player has its own profile (`player.Profile`, never null). Assign a different `InputProfile`
to switch presets.
