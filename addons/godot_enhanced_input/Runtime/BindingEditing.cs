using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Editing helpers shared by the editor dock and tools: the modifier and trigger types available,
/// creating them by name, reading and writing a binding's groups, and short descriptions for lists.
/// </summary>
public static class BindingEditing
{
    private static List<Type> _modifiers;
    private static List<Type> _triggers;

    /// <summary>Every concrete modifier type (built-in and the project's own).</summary>
    public static IReadOnlyList<Type> ModifierTypes => _modifiers ??= Find(typeof(InputModifier));

    /// <summary>Every concrete trigger type (built-in and the project's own).</summary>
    public static IReadOnlyList<Type> TriggerTypes => _triggers ??= Find(typeof(InputTrigger));

    /// <summary>Forgets the cached type lists (after the project's code was rebuilt).</summary>
    public static void Rescan()
    {
        _modifiers = null;
        _triggers = null;
    }

    private static List<Type> Find(Type baseType)
    {
        var list = new List<Type>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
            catch { continue; }
            foreach (var t in types)
                if (!t.IsAbstract && baseType.IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null && list.All(x => x.FullName != t.FullName))
                    list.Add(t);
        }
        list.Sort((a, b) => string.Compare(ShortName(a), ShortName(b), StringComparison.Ordinal));
        return list;
    }

    /// <summary>"HoldTrigger" → "Hold", "DeadzoneModifier" → "Deadzone".</summary>
    public static string ShortName(Type type)
    {
        var name = type.Name;
        foreach (var suffix in new[] { "Modifier", "Trigger" })
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal)) return name[..^suffix.Length];
        return name;
    }

    /// <summary>A short name for a modifier or trigger resource.</summary>
    public static string ShortName(Resource resource) => resource == null ? "(empty)" : ShortName(resource.GetType());

    /// <summary>Creates a modifier or trigger by its short or full type name, or null.</summary>
    public static Resource Create(string name)
    {
        foreach (var t in ModifierTypes.Concat(TriggerTypes))
            if (ShortName(t) == name || t.Name == name) return (Resource)Activator.CreateInstance(t);
        return null;
    }

    /// <summary>Groups as "a, b".</summary>
    public static string GroupsText(ActionBinding binding) => binding?.Groups == null ? string.Empty : string.Join(", ", binding.Groups);

    /// <summary>Parses "a, b" into groups (empty entries dropped).</summary>
    public static string[] ParseGroups(string text)
        => (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The binding's input as a text path ("none" when empty).</summary>
    public static string PathOf(InputSource source) => source == null ? "none" : InputSourcePath.Format(source);

    /// <summary>Parses a text path; null with an error message when it is not one.</summary>
    public static InputSource TryParsePath(string text, out string error)
    {
        error = null;
        try { return InputSourcePath.Parse(text); }
        catch (FormatException e) { error = e.Message; return null; }
    }

    /// <summary>Which device family a source belongs to, for icons and filters.</summary>
    public static InputDeviceKind DeviceKindOf(InputSource source) => source?.Kind switch
    {
        SourceKind.JoyButton or SourceKind.JoyAxis or SourceKind.JoyStick => InputDeviceKind.Joypad,
        SourceKind.Motion when !source.MotionOfHandheld => InputDeviceKind.Joypad,
        SourceKind.Touch or SourceKind.Gesture or SourceKind.Virtual => InputDeviceKind.Touch,
        SourceKind.Motion => InputDeviceKind.Touch,
        SourceKind.Pen => InputDeviceKind.Pen,
        SourceKind.DeviceControl => InputDeviceKind.Custom,
        SourceKind.Composite => source.Parts.Count > 0 ? DeviceKindOf(source.Parts[source.Parts.Count - 1]) : InputDeviceKind.Other,
        SourceKind.None or null => InputDeviceKind.Other,
        _ => InputDeviceKind.KeyboardMouse,
    };

    /// <summary>"Hold · Deadzone · groups: pad · rebind: jump".</summary>
    public static string Summary(ActionBinding binding)
    {
        if (binding == null) return string.Empty;
        var parts = new List<string>();
        foreach (var m in binding.Modifiers) parts.Add(ShortName(m));
        foreach (var t in binding.Triggers) parts.Add(ShortName(t));
        if (binding.Groups is { Length: > 0 }) parts.Add($"groups: {string.Join(",", binding.Groups)}");
        if (binding.IsRebindable) parts.Add($"rebind: {binding.RebindId}");
        return string.Join("  ·  ", parts);
    }

    /// <summary>Every action bound in these contexts, by name.</summary>
    public static List<EnhancedInputAction> ActionsIn(IEnumerable<EnhancedInputMappingContext> contexts)
    {
        var list = new List<EnhancedInputAction>();
        foreach (var context in contexts)
            foreach (var binding in context.Bindings)
                if (binding?.Action != null && !list.Contains(binding.Action)) list.Add(binding.Action);
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    /// <summary>Whether a binding matches a search text (action, input path, groups, rebind id, modifiers, triggers).</summary>
    public static bool Matches(ActionBinding binding, string search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        if (binding == null) return false;
        var s = search.Trim();
        bool Has(string text) => text != null && text.Contains(s, StringComparison.OrdinalIgnoreCase);
        return Has(binding.Action?.Name) || Has(PathOf(binding.Source)) || Has(GroupsText(binding)) || Has(binding.RebindId) || Has(Summary(binding))
            || (binding.Source != null && Has(InputPrompts.GetName(binding.Source)));
    }
}
