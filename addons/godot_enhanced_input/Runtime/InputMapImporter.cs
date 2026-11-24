using System.Collections.Generic;
using Godot;
using Godot.Collections;

namespace GodotEnhancedInput;

/// <summary>What <see cref="InputMapImporter"/> made from a project's Godot input actions.</summary>
public sealed class ImportedInputMap
{
    /// <summary>One mapping context with a mapping per imported event.</summary>
    public EnhancedInputMappingContext Context { get; init; }

    /// <summary>One action per Godot input action, in the project's order.</summary>
    public List<EnhancedInputAction> Actions { get; } = new();

    /// <summary>Events that have no equivalent source (with the action they belonged to), for a report.</summary>
    public List<string> Skipped { get; } = new();

    /// <summary>Events that were converted but may behave a little differently (with why), for a report.</summary>
    public List<string> Approximated { get; } = new();
}

/// <summary>
/// Turns Godot input actions (Project Settings → Input Map) into enhanced actions and one mapping context,
/// so an existing project can switch without rebinding by hand. Each Godot action becomes a digital action
/// of the same name; each of its events becomes a mapping: keys (physical, key code or label, with
/// Ctrl/Shift/Alt/Meta as held modifiers), mouse buttons, joypad buttons and axis directions (with the
/// action's deadzone as a deadzone modifier, as Godot rescales strength past it). Built-in <c>ui_*</c>
/// actions are left out unless asked for. The editor's Enhanced Input dock saves the result as files.
/// </summary>
public static class InputMapImporter
{
    /// <summary>Imports the project's input actions as configured in Project Settings (works in the editor and in games).</summary>
    public static ImportedInputMap FromProjectSettings(bool includeBuiltIn = false, string contextName = "input_map")
    {
        var actions = new List<(string Name, float Deadzone, IEnumerable<InputEvent> Events)>();
        foreach (var property in ProjectSettings.Singleton.GetPropertyList())
        {
            var path = property["name"].AsString();
            if (!path.StartsWith("input/")) continue;
            var name = path.Substring("input/".Length);
            if (!includeBuiltIn && IsBuiltIn(name)) continue;
            var setting = ProjectSettings.GetSetting(path);
            if (setting.VariantType != Variant.Type.Dictionary) continue;
            var dict = setting.AsGodotDictionary();
            float deadzone = dict.TryGetValue("deadzone", out var dz) ? (float)dz.AsDouble() : 0.5f;
            var events = new List<InputEvent>();
            if (dict.TryGetValue("events", out var list))
                foreach (var item in list.AsGodotArray())
                    if (item.AsGodotObject() is InputEvent e) events.Add(e);
            actions.Add((name, deadzone, (IEnumerable<InputEvent>)events));
        }
        return Build(actions, contextName);
    }

    /// <summary>Imports the actions of the running game's <see cref="InputMap"/> (including ones added from code).</summary>
    public static ImportedInputMap FromInputMap(bool includeBuiltIn = false, string contextName = "input_map")
    {
        var actions = new List<(string Name, float Deadzone, IEnumerable<InputEvent> Events)>();
        foreach (var action in InputMap.GetActions())
        {
            string name = action;
            if (!includeBuiltIn && IsBuiltIn(name)) continue;
            actions.Add((name, InputMap.ActionGetDeadzone(action), InputMap.ActionGetEvents(action)));
        }
        return Build(actions, contextName);
    }

    /// <summary>
    /// Godot's own built-in actions (<c>ui_accept</c>, <c>ui_left</c>…): those Godot defines a default for.
    /// A project's own action that merely starts with <c>ui_</c> is imported.
    /// </summary>
    public static bool IsBuiltIn(string name)
    {
        if (name.StartsWith("spatial_editor/")) return true;
        if (!name.StartsWith("ui_")) return false;
        var path = "input/" + name;
        return !ProjectSettings.HasSetting(path) || ProjectSettings.Singleton.PropertyGetRevert(path).VariantType != Variant.Type.Nil;
    }

    private static ImportedInputMap Build(List<(string Name, float Deadzone, IEnumerable<InputEvent> Events)> actions, string contextName)
    {
        var result = new ImportedInputMap { Context = EnhancedInputMappingContext.Create(contextName) };
        result.Context.Description = "Imported from the project's Input Map.";
        foreach (var (name, deadzone, events) in actions)
        {
            var action = EnhancedInputAction.Create(name, ActionValueKind.Digital);
            action.Description = "Imported from the project's Input Map.";
            result.Actions.Add(action);
            foreach (var e in events)
            {
                var source = ToSource(e, out var note);
                if (source == null)
                {
                    result.Skipped.Add($"{name}: {e?.AsText() ?? "(empty)"}");
                    continue;
                }
                if (note != null) result.Approximated.Add($"{name}: {e.AsText()} — {note}");
                var binding = ActionBinding.Create(action, source);
                if (e is InputEventJoypadMotion && deadzone > 0f)
                    binding.Modifiers.Add(new DeadzoneModifier { Shape = DeadzoneShape.Axial, Lower = Mathf.Clamp(deadzone, 0f, 0.99f), Upper = 1f });
                result.Context.Bindings.Add(binding);
            }
        }
        return result;
    }

    /// <summary>The source for one Godot input event, or null when there is no equivalent.</summary>
    public static InputSource ToSource(InputEvent e) => ToSource(e, out _);

    /// <summary>
    /// The source for one Godot input event (null when there is none), with a note when the conversion is
    /// approximate: modifier keys are matched as held keys (extra modifiers held at the same time do not stop
    /// it), and Command-or-Control becomes the key of the platform importing it.
    /// </summary>
    public static InputSource ToSource(InputEvent e, out string note)
    {
        note = null;
        int device = e == null || e.Device < 0 ? SourceKey.AnyDevice : e.Device;
        switch (e)
        {
            case InputEventKey key:
            {
                Key code;
                KeyMatch match;
                if (key.PhysicalKeycode != Key.None) { code = key.PhysicalKeycode; match = KeyMatch.Physical; }
                else if (key.Keycode != Key.None) { code = key.Keycode; match = KeyMatch.Logical; }
                else if (key.KeyLabel != Key.None) { code = key.KeyLabel; match = KeyMatch.Label; }
                else return null;
                var main = key.Location != KeyLocation.Unspecified ? InputSource.ForKey(code, key.Location, match) : InputSource.ForKey(code, match);
                return WithModifiers(key, main, match, ref note);
            }
            case InputEventMouseButton mouse:
                return WithModifiers(mouse, InputSource.ForMouseButton(mouse.ButtonIndex), KeyMatch.Physical, ref note);
            case InputEventJoypadButton button:
                return InputSource.ForJoyButton(button.ButtonIndex, device);
            case InputEventJoypadMotion motion:
                return InputSource.ForJoyAxis(motion.Axis, device, motion.AxisValue < 0 ? JoyAxisRange.Negative : JoyAxisRange.Positive);
            default:
                return null;
        }
    }

    private static InputSource WithModifiers(InputEventWithModifiers e, InputSource main, KeyMatch match, ref string note)
    {
        var modifiers = new List<Key>();
        if (e.CommandOrControlAutoremap)
        {
            modifiers.Add(OS.GetName() == "macOS" ? Key.Meta : Key.Ctrl);
            note = $"Command-or-Control became {(OS.GetName() == "macOS" ? "Meta" : "Ctrl")} for this platform";
        }
        else if (e.CtrlPressed) modifiers.Add(Key.Ctrl);
        if (e.ShiftPressed) modifiers.Add(Key.Shift);
        if (e.AltPressed) modifiers.Add(Key.Alt);
        if (e.MetaPressed && !modifiers.Contains(Key.Meta)) modifiers.Add(Key.Meta);
        if (modifiers.Count == 0) return main;
        note ??= "modifiers are matched as held keys; other modifiers held too do not stop it";
        var parts = new InputSource[modifiers.Count + 1];
        for (int i = 0; i < modifiers.Count; i++) parts[i] = InputSource.ForKey(modifiers[i], match);
        parts[^1] = main;
        return InputSource.Composite(CompositeMode.WithModifiers, parts);
    }
}
