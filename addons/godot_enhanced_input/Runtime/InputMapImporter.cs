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

    /// <summary>Godot's own UI actions (<c>ui_accept</c>, <c>ui_left</c>…).</summary>
    public static bool IsBuiltIn(string name) => name.StartsWith("ui_") || name.StartsWith("spatial_editor/");

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
                var source = ToSource(e);
                if (source == null)
                {
                    result.Skipped.Add($"{name}: {e?.AsText() ?? "(empty)"}");
                    continue;
                }
                var binding = ActionBinding.Create(action, source);
                if (e is InputEventJoypadMotion && deadzone > 0f)
                    binding.Modifiers.Add(new DeadzoneModifier { Shape = DeadzoneShape.Axial, Lower = Mathf.Clamp(deadzone, 0f, 0.99f), Upper = 1f });
                result.Context.Bindings.Add(binding);
            }
        }
        return result;
    }

    /// <summary>The source for one Godot input event, or null when there is no equivalent.</summary>
    public static InputSource ToSource(InputEvent e)
    {
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

                var modifiers = new List<Key>();
                if (key.CtrlPressed || key.CommandOrControlAutoremap) modifiers.Add(Key.Ctrl);
                if (key.ShiftPressed) modifiers.Add(Key.Shift);
                if (key.AltPressed) modifiers.Add(Key.Alt);
                if (key.MetaPressed) modifiers.Add(Key.Meta);
                var main = key.Location != KeyLocation.Unspecified ? InputSource.ForKey(code, key.Location, match) : InputSource.ForKey(code, match);
                if (modifiers.Count == 0) return main;
                var parts = new InputSource[modifiers.Count + 1];
                for (int i = 0; i < modifiers.Count; i++) parts[i] = InputSource.ForKey(modifiers[i], match);
                parts[^1] = main;
                return InputSource.Composite(CompositeMode.WithModifiers, parts);
            }
            case InputEventMouseButton mouse:
                return InputSource.ForMouseButton(mouse.ButtonIndex);
            case InputEventJoypadButton button:
                return InputSource.ForJoyButton(button.ButtonIndex, device);
            case InputEventJoypadMotion motion:
                return InputSource.ForJoyAxis(motion.Axis, device, motion.AxisValue < 0 ? JoyAxisRange.Negative : JoyAxisRange.Positive);
            default:
                return null;
        }
    }
}
