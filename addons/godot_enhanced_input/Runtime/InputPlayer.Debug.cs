using System.Collections.Generic;
using System.Text;

namespace GodotEnhancedInput;

public partial class InputPlayer
{
    /// <summary>
    /// The player's state as text for logs and overlays: devices, contexts in priority order with
    /// their flags and ignored sources, a running capture, profile overrides, and every action's phase,
    /// value, last events and timings (only non-idle ones with <paramref name="onlyActive"/>).
    /// </summary>
    public string DescribeState(bool onlyActive = false)
    {
        var text = new StringBuilder();
        text.Append($"Player {PlayerIndex}  tick {_tick}");
        if (IsTreePaused) text.Append("  [tree paused]");
        text.AppendLine();

        var devices = new List<string>();
        if (AcceptKeyboardMouse) devices.Add("keyboard/mouse");
        if (AcceptTouch) devices.Add("touch");
        devices.Add(JoypadDevices.Count == 0 ? "unowned joypads" : "joypads " + string.Join(",", JoypadDevices));
        if (AcceptActionEvents) devices.Add("InputEventAction");
        text.Append("  devices: ").Append(string.Join(", ", devices));
        text.Append($"  | last: {LastDeviceKind}");
        if (LastDeviceKind == InputDeviceKind.Joypad) text.Append($" {LastJoypad} ({JoypadStyle})");
        text.AppendLine();

        var entries = new List<ContextEntry>(_entries);
        entries.Sort(CompareEntries);
        text.AppendLine(entries.Count == 0 ? "  contexts: none" : "  contexts:");
        foreach (var entry in entries)
        {
            text.Append($"    {entry.Priority,5}  {entry.Context.Name}  ({entry.Context.Bindings.Count} bindings)");
            if (entry.Context.BlockLowerPriority) text.Append("  [blocks lower]");
            if (entry.Ignored.Count > 0) text.Append("  ignoring until released: ").Append(string.Join(", ", entry.Ignored));
            text.AppendLine();
        }

        if (IsCapturing) text.AppendLine($"  capturing a press for '{CaptureId}'");
        if (_profile != null && _profile.Count > 0)
            text.AppendLine($"  profile '{_profile.Name}': {_profile.Count} override(s) — " + string.Join(", ", _profile.OverriddenIds));

        var states = new List<ActionState>(_states.Values);
        states.Sort((a, b) => string.CompareOrdinal(a.Action?.Name, b.Action?.Name));
        int shown = 0;
        text.AppendLine("  actions:");
        foreach (var state in states)
        {
            bool active = state.Phase != TriggerPhase.Idle || (state.LastTick == _tick && state.Events != ActionEvents.None);
            if (onlyActive && !active) continue;
            shown++;
            text.Append($"    {state.Action?.Name,-16} {state.Phase,-9} {state.Value,-20}");
            if (state.LastTick == _tick && state.Events != ActionEvents.None) text.Append($" [{state.Events}]");
            if (state.Phase != TriggerPhase.Idle || state.ElapsedSeconds > 0)
                text.Append($" {state.ElapsedSeconds:0.000}s / {state.TriggeredSeconds:0.000}s");
            text.AppendLine();
        }
        if (shown == 0) text.AppendLine(onlyActive ? "    (none active)" : "    (none evaluated yet)");
        return text.ToString();
    }
}
