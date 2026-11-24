using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// The output bridge to Godot's InputMap: enhanced actions with <see cref="EnhancedInputAction.MirrorToInputMap"/>
/// set press and release that Godot action (with their strength) while they are triggered, so addons and
/// code that read <c>Input.IsActionPressed("jump")</c> keep working next to the plugin. Missing Godot actions
/// are created at run time (not saved). When several players mirror the same action, the strongest wins.
/// Only the action's state is set (<c>Input.ActionPress</c>); no events are sent, so the GUI and the plugin's
/// own InputMap sources are not fed back. An action never mirrors into a Godot action it reads itself.
/// </summary>
public static class InputMapBridge
{
    private sealed class Entry
    {
        public StringName Name;
        public readonly Dictionary<InputPlayer, float> ByPlayer = new();
        public float Applied;
    }

    private static readonly Dictionary<string, Entry> Entries = new();

    /// <summary>Turns the bridge off for every player (the project setting <c>enhanced_input/input_map/mirror_actions</c>). On by default.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>How strongly a Godot action is currently pressed through the bridge (0 when released).</summary>
    public static float GetMirroredStrength(string action) => Entries.TryGetValue(action ?? string.Empty, out var e) ? e.Applied : 0f;

    internal static void Report(InputPlayer player, string action, float strength)
    {
        if (!Entries.TryGetValue(action, out var entry))
            Entries[action] = entry = new Entry { Name = new StringName(action) };
        entry.ByPlayer[player] = strength;
    }

    /// <summary>Drops a player's contributions (a removed player releases what it pressed).</summary>
    internal static void Forget(InputPlayer player)
    {
        foreach (var entry in Entries.Values) entry.ByPlayer.Remove(player);
        Flush();
    }

    /// <summary>Applies the strongest strength of each mirrored action to Godot's Input.</summary>
    internal static void Flush()
    {
        foreach (var entry in Entries.Values)
        {
            float strength = 0f;
            foreach (var s in entry.ByPlayer.Values) if (s > strength) strength = s;
            if (Mathf.IsEqualApprox(strength, entry.Applied)) continue;
            entry.Applied = strength;
            if (!InputMap.HasAction(entry.Name)) InputMap.AddAction(entry.Name);
            if (strength > 0f) Input.ActionPress(entry.Name, strength);
            else Input.ActionRelease(entry.Name);
        }
    }

    /// <summary>Releases everything the bridge pressed and forgets it (tests, shutting down).</summary>
    public static void ReleaseAll()
    {
        foreach (var entry in Entries.Values)
        {
            entry.ByPlayer.Clear();
            if (entry.Applied > 0f && InputMap.HasAction(entry.Name)) Input.ActionRelease(entry.Name);
            entry.Applied = 0f;
        }
    }
}
