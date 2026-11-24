using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// The output bridge to Godot's InputMap: enhanced actions with <see cref="EnhancedInputAction.MirrorToInputMap"/>
/// set press and release that Godot action (with their strength) while they are triggered, so addons and
/// code that read <c>Input.IsActionPressed("jump")</c> keep working next to the plugin. Missing Godot actions
/// are created at run time (not saved).
/// <list type="bullet">
/// <item>Contributions are kept per player and per enhanced action; a Godot action gets the strongest.</item>
/// <item>Each player replaces all its contributions at once every tick, so turning mirroring off (for the
/// player or the project), renaming a target or removing it releases what was pressed.</item>
/// <item>Feedback is prevented: the links "an action reads Godot action X and mirrors into Y" of every player
/// form a graph, and an action on a cycle of it (direct, or through other actions and players) does not
/// mirror.</item>
/// <item>Only the action's state is set (<c>Input.ActionPress</c>); no events are sent.</item>
/// </list>
/// </summary>
public static class InputMapBridge
{
    private sealed class Entry
    {
        public StringName Name;
        public readonly Dictionary<(InputPlayer Player, EnhancedInputAction Action), float> By = new();
        public float Applied;
    }

    private static readonly Dictionary<string, Entry> Entries = new();
    private static readonly List<(InputPlayer, EnhancedInputAction)> Scratch = new();

    // Feedback graph: per player, (action, reads Godot action, mirrors into Godot action).
    private static readonly Dictionary<InputPlayer, List<(EnhancedInputAction Action, string From, string To)>> Links = new();
    private static readonly HashSet<(InputPlayer, EnhancedInputAction)> Blocked = new();
    private static bool _linksDirty;

    private static bool _enabled = true;

    /// <summary>Turns the bridge off for every player (the project setting <c>enhanced_input/input_map/mirror_actions</c>); turning it off releases everything it pressed. On by default.</summary>
    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (!value) ReleaseAll();
        }
    }

    /// <summary>How strongly a Godot action is currently pressed through the bridge (0 when released).</summary>
    public static float GetMirroredStrength(string action) => Entries.TryGetValue(action ?? string.Empty, out var e) ? e.Applied : 0f;

    /// <summary>Replaces everything one player contributes, then applies the result to Godot.</summary>
    internal static void SetContributions(InputPlayer player, List<(EnhancedInputAction Action, string Target, float Strength)> contributions)
    {
        foreach (var entry in Entries.Values)
        {
            Scratch.Clear();
            foreach (var key in entry.By.Keys) if (key.Player == player) Scratch.Add(key);
            foreach (var key in Scratch) entry.By.Remove(key);
        }
        if (contributions != null)
            foreach (var (action, target, strength) in contributions)
            {
                if (string.IsNullOrEmpty(target)) continue;
                if (!Entries.TryGetValue(target, out var entry)) Entries[target] = entry = new Entry { Name = new StringName(target) };
                entry.By[(player, action)] = strength;
            }
        Flush();
    }

    /// <summary>Drops a player's contributions and links (a removed player releases what it pressed).</summary>
    internal static void Forget(InputPlayer player)
    {
        if (Links.Remove(player)) _linksDirty = true;
        SetContributions(player, null);
    }

    /// <summary>Records which Godot actions a player's mirrored actions read and mirror into (for the feedback check).</summary>
    internal static void SetLinks(InputPlayer player, List<(EnhancedInputAction Action, string From, string To)> links)
    {
        Links[player] = links;
        _linksDirty = true;
    }

    /// <summary>Whether an action of a player sits on a feedback cycle and must not mirror.</summary>
    internal static bool IsBlocked(InputPlayer player, EnhancedInputAction action)
    {
        if (_linksDirty) FindCycles();
        return Blocked.Contains((player, action));
    }

    private static void FindCycles()
    {
        _linksDirty = false;
        Blocked.Clear();
        var edges = new Dictionary<string, List<string>>();
        foreach (var list in Links.Values)
            foreach (var (_, from, to) in list)
            {
                if (!edges.TryGetValue(from, out var next)) edges[from] = next = new List<string>();
                next.Add(to);
            }
        foreach (var (player, list) in Links)
            foreach (var (action, from, to) in list)
                if (Reaches(edges, to, from)) Blocked.Add((player, action));
    }

    private static bool Reaches(Dictionary<string, List<string>> edges, string start, string goal)
    {
        var seen = new HashSet<string>();
        var stack = new Stack<string>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node == goal) return true;
            if (!seen.Add(node) || !edges.TryGetValue(node, out var next)) continue;
            foreach (var n in next) stack.Push(n);
        }
        return false;
    }

    /// <summary>Applies the strongest contribution of each mirrored action to Godot's Input.</summary>
    internal static void Flush()
    {
        foreach (var entry in Entries.Values)
        {
            float strength = 0f;
            foreach (var s in entry.By.Values) if (s > strength) strength = s;
            if (Mathf.IsEqualApprox(strength, entry.Applied)) continue;
            entry.Applied = strength;
            if (!InputMap.HasAction(entry.Name)) InputMap.AddAction(entry.Name);
            if (strength > 0f) Input.ActionPress(entry.Name, strength);
            else Input.ActionRelease(entry.Name);
        }
    }

    /// <summary>Releases everything the bridge pressed and forgets every contribution (tests, shutting down).</summary>
    public static void ReleaseAll()
    {
        foreach (var entry in Entries.Values)
        {
            entry.By.Clear();
            if (entry.Applied > 0f && InputMap.HasAction(entry.Name)) Input.ActionRelease(entry.Name);
            entry.Applied = 0f;
        }
    }
}
