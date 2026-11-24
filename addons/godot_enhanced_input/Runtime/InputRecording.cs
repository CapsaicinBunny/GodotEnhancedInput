using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace GodotEnhancedInput;

/// <summary>What one recorded input did.</summary>
public enum RecordedInputKind
{
    /// <summary>An input event fed to the player.</summary>
    Event,
    /// <summary>A virtual control was set.</summary>
    Virtual,
    /// <summary>A one-tick amount was added to a virtual control.</summary>
    VirtualDelta,
    /// <summary>A value was injected into an action for one tick.</summary>
    Inject,
    /// <summary>A continuous injection started.</summary>
    InjectStart,
    /// <summary>A continuous injection stopped.</summary>
    InjectStop,
}

/// <summary>One input that reached a player between two ticks.</summary>
public sealed class RecordedInput
{
    public RecordedInputKind Kind { get; init; }

    /// <summary>The event (for <see cref="RecordedInputKind.Event"/>).</summary>
    public InputEvent Event { get; init; }

    /// <summary>Where the event came from in Godot's input propagation.</summary>
    public IntakeStage Stage { get; init; }

    /// <summary>The virtual control or action name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The value set, added or injected.</summary>
    public Vector3 Value { get; init; }
}

/// <summary>Everything a player received before one tick, and the time step of that tick.</summary>
public sealed class RecordedTick
{
    public double Delta { get; set; }
    public List<RecordedInput> Inputs { get; } = new();
}

/// <summary>
/// The input a player received, tick by tick: events, virtual controls and injections, with each
/// tick's time step. Replaying it into a player with the same contexts gives the same action values,
/// phases and events on the same ticks — for bug reports, automated tests, demos and attract modes.
/// Values the player polls itself (motion sensors, the project's InputMap) are not recorded and are
/// not polled during a replay. Saved as JSON (<see cref="ToJson"/>, <see cref="Save"/>).
/// </summary>
public sealed class InputRecording
{
    /// <summary>The recorded ticks, in order.</summary>
    public List<RecordedTick> Ticks { get; } = new();

    /// <summary>Free-form notes (build, level, seed…).</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Recorded time in seconds.</summary>
    public double Duration
    {
        get
        {
            double total = 0;
            foreach (var tick in Ticks) total += tick.Delta;
            return total;
        }
    }

    /// <summary>
    /// Feeds every recorded tick into a player and ticks it, right away. The player should hold the
    /// same contexts (and mode, profile and settings) as when the recording was made.
    /// </summary>
    public void ReplayInto(InputPlayer player)
    {
        if (player == null) return;
        player.StartReplay(this);
        for (int i = 0; i < Ticks.Count && player.IsReplaying; i++) player.Tick(0);
    }

    // ------------------------------------------------------------------
    //  JSON
    // ------------------------------------------------------------------

    /// <summary>The recording as JSON text.</summary>
    public string ToJson()
    {
        var ticks = new JsonArray();
        foreach (var tick in Ticks)
        {
            var inputs = new JsonArray();
            foreach (var input in tick.Inputs)
            {
                var node = WriteInput(input);
                if (node != null) inputs.Add(node);
            }
            var t = new JsonObject { ["dt"] = tick.Delta };
            if (inputs.Count > 0) t["in"] = inputs;
            ticks.Add(t);
        }
        return new JsonObject { ["format"] = 1, ["notes"] = Notes, ["ticks"] = ticks }
            .ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    /// <summary>Reads a recording from JSON text, or returns null. Events of unknown types are skipped.</summary>
    public static InputRecording FromJson(string text)
    {
        try
        {
            if (JsonNode.Parse(text) is not JsonObject root || root["ticks"] is not JsonArray ticks) return null;
            var recording = new InputRecording { Notes = (string)root["notes"] ?? string.Empty };
            foreach (var t in ticks)
            {
                if (t is not JsonObject tickNode) continue;
                var tick = new RecordedTick { Delta = (double?)tickNode["dt"] ?? 0.0 };
                if (tickNode["in"] is JsonArray inputs)
                    foreach (var i in inputs)
                        if (i is JsonObject inputNode && ReadInput(inputNode) is { } input) tick.Inputs.Add(input);
                recording.Ticks.Add(tick);
            }
            return recording;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Saves the recording as JSON (e.g. under user://).</summary>
    public Error Save(string path)
    {
        var folder = path.GetBaseDir();
        if (!string.IsNullOrEmpty(folder)) DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(folder));
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file == null) return FileAccess.GetOpenError();
        file.StoreString(ToJson());
        return Error.Ok;
    }

    /// <summary>Loads a recording saved with <see cref="Save"/>, or returns null.</summary>
    public static InputRecording Load(string path)
    {
        if (!FileAccess.FileExists(path)) return null;
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return file == null ? null : FromJson(file.GetAsText());
    }

    private static JsonArray V2(Vector2 v) => new(v.X, v.Y);
    private static JsonArray V3(Vector3 v) => new(v.X, v.Y, v.Z);
    private static Vector2 ToV2(JsonNode node) => node is JsonArray a && a.Count >= 2 ? new Vector2((float)a[0], (float)a[1]) : Vector2.Zero;
    private static Vector3 ToV3(JsonNode node) => node is JsonArray a && a.Count >= 3 ? new Vector3((float)a[0], (float)a[1], (float)a[2]) : Vector3.Zero;

    private static JsonObject WriteInput(RecordedInput input)
    {
        switch (input.Kind)
        {
            case RecordedInputKind.Event:
                var e = WriteEvent(input.Event);
                if (e == null) return null;
                if (input.Stage != IntakeStage.Direct) e["stage"] = input.Stage.ToString();
                return e;
            case RecordedInputKind.Virtual: return new JsonObject { ["t"] = "virtual", ["name"] = input.Name, ["v"] = V3(input.Value) };
            case RecordedInputKind.VirtualDelta: return new JsonObject { ["t"] = "virtual_delta", ["name"] = input.Name, ["v"] = V3(input.Value) };
            case RecordedInputKind.Inject: return new JsonObject { ["t"] = "inject", ["name"] = input.Name, ["v"] = V3(input.Value) };
            case RecordedInputKind.InjectStart: return new JsonObject { ["t"] = "inject_start", ["name"] = input.Name, ["v"] = V3(input.Value) };
            case RecordedInputKind.InjectStop: return new JsonObject { ["t"] = "inject_stop", ["name"] = input.Name };
        }
        return null;
    }

    private static RecordedInput ReadInput(JsonObject node)
    {
        string type = (string)node["t"];
        string name = (string)node["name"] ?? string.Empty;
        var value = ToV3(node["v"]);
        switch (type)
        {
            case "virtual": return new RecordedInput { Kind = RecordedInputKind.Virtual, Name = name, Value = value };
            case "virtual_delta": return new RecordedInput { Kind = RecordedInputKind.VirtualDelta, Name = name, Value = value };
            case "inject": return new RecordedInput { Kind = RecordedInputKind.Inject, Name = name, Value = value };
            case "inject_start": return new RecordedInput { Kind = RecordedInputKind.InjectStart, Name = name, Value = value };
            case "inject_stop": return new RecordedInput { Kind = RecordedInputKind.InjectStop, Name = name };
        }
        var @event = ReadEvent(type, node);
        if (@event == null) return null;
        var stage = Enum.TryParse<IntakeStage>((string)node["stage"] ?? nameof(IntakeStage.Direct), out var s) ? s : IntakeStage.Direct;
        return new RecordedInput { Kind = RecordedInputKind.Event, Event = @event, Stage = stage };
    }

    private static JsonObject WriteEvent(InputEvent @event)
    {
        JsonObject node;
        switch (@event)
        {
            case InputEventKey k:
                node = new JsonObject
                {
                    ["t"] = "key", ["code"] = (long)k.Keycode, ["phys"] = (long)k.PhysicalKeycode, ["label"] = (long)k.KeyLabel,
                    ["uni"] = k.Unicode, ["down"] = k.Pressed, ["echo"] = k.Echo,
                };
                WriteMods(node, k);
                break;
            case InputEventMouseButton b:
                node = new JsonObject
                {
                    ["t"] = "mouse_button", ["button"] = (long)b.ButtonIndex, ["down"] = b.Pressed, ["double"] = b.DoubleClick,
                    ["pos"] = V2(b.Position), ["factor"] = b.Factor, ["mask"] = (long)b.ButtonMask, ["canceled"] = b.Canceled,
                };
                WriteMods(node, b);
                break;
            case InputEventMouseMotion m:
                node = new JsonObject
                {
                    ["t"] = "mouse_motion", ["pos"] = V2(m.Position), ["rel"] = V2(m.Relative), ["vel"] = V2(m.Velocity),
                    ["pressure"] = m.Pressure, ["tilt"] = V2(m.Tilt), ["eraser"] = m.PenInverted, ["mask"] = (long)m.ButtonMask,
                };
                WriteMods(node, m);
                break;
            case InputEventJoypadButton jb:
                node = new JsonObject { ["t"] = "pad_button", ["button"] = (long)jb.ButtonIndex, ["down"] = jb.Pressed };
                break;
            case InputEventJoypadMotion jm:
                node = new JsonObject { ["t"] = "pad_axis", ["axis"] = (long)jm.Axis, ["value"] = jm.AxisValue };
                break;
            case InputEventScreenTouch st:
                node = new JsonObject
                {
                    ["t"] = "touch", ["finger"] = st.Index, ["pos"] = V2(st.Position), ["down"] = st.Pressed, ["canceled"] = st.Canceled, ["double"] = st.DoubleTap,
                };
                break;
            case InputEventScreenDrag sd:
                node = new JsonObject
                {
                    ["t"] = "drag", ["finger"] = sd.Index, ["pos"] = V2(sd.Position), ["rel"] = V2(sd.Relative), ["vel"] = V2(sd.Velocity),
                    ["pressure"] = sd.Pressure, ["tilt"] = V2(sd.Tilt), ["eraser"] = sd.PenInverted,
                };
                break;
            case InputEventMagnifyGesture mg:
                node = new JsonObject { ["t"] = "magnify", ["pos"] = V2(mg.Position), ["factor"] = mg.Factor };
                break;
            case InputEventPanGesture pg:
                node = new JsonObject { ["t"] = "pan", ["pos"] = V2(pg.Position), ["delta"] = V2(pg.Delta) };
                break;
            case InputEventAction a:
                node = new JsonObject { ["t"] = "action", ["action"] = a.Action.ToString(), ["down"] = a.Pressed, ["strength"] = a.Strength };
                break;
            default:
                return null;
        }
        node["dev"] = @event.Device;
        return node;
    }

    private static void WriteMods(JsonObject node, InputEventWithModifiers e)
    {
        int mods = (e.ShiftPressed ? 1 : 0) | (e.CtrlPressed ? 2 : 0) | (e.AltPressed ? 4 : 0) | (e.MetaPressed ? 8 : 0);
        if (mods != 0) node["mods"] = mods;
    }

    private static void ReadMods(JsonObject node, InputEventWithModifiers e)
    {
        int mods = (int?)node["mods"] ?? 0;
        e.ShiftPressed = (mods & 1) != 0;
        e.CtrlPressed = (mods & 2) != 0;
        e.AltPressed = (mods & 4) != 0;
        e.MetaPressed = (mods & 8) != 0;
    }

    private static InputEvent ReadEvent(string type, JsonObject n)
    {
        bool down = (bool?)n["down"] ?? false;
        InputEvent e;
        switch (type)
        {
            case "key":
                var k = new InputEventKey
                {
                    Keycode = (Key)((long?)n["code"] ?? 0), PhysicalKeycode = (Key)((long?)n["phys"] ?? 0), KeyLabel = (Key)((long?)n["label"] ?? 0),
                    Unicode = (long?)n["uni"] ?? 0, Pressed = down, Echo = (bool?)n["echo"] ?? false,
                };
                ReadMods(n, k);
                e = k;
                break;
            case "mouse_button":
                var b = new InputEventMouseButton
                {
                    ButtonIndex = (MouseButton)((long?)n["button"] ?? 0), Pressed = down, DoubleClick = (bool?)n["double"] ?? false,
                    Position = ToV2(n["pos"]), Factor = (float?)n["factor"] ?? 1f, ButtonMask = (MouseButtonMask)((long?)n["mask"] ?? 0),
                    Canceled = (bool?)n["canceled"] ?? false,
                };
                ReadMods(n, b);
                e = b;
                break;
            case "mouse_motion":
                var m = new InputEventMouseMotion
                {
                    Position = ToV2(n["pos"]), Relative = ToV2(n["rel"]), Velocity = ToV2(n["vel"]), Pressure = (float?)n["pressure"] ?? 0f,
                    Tilt = ToV2(n["tilt"]), PenInverted = (bool?)n["eraser"] ?? false, ButtonMask = (MouseButtonMask)((long?)n["mask"] ?? 0),
                };
                ReadMods(n, m);
                e = m;
                break;
            case "pad_button":
                e = new InputEventJoypadButton { ButtonIndex = (JoyButton)((long?)n["button"] ?? 0), Pressed = down };
                break;
            case "pad_axis":
                e = new InputEventJoypadMotion { Axis = (JoyAxis)((long?)n["axis"] ?? 0), AxisValue = (float?)n["value"] ?? 0f };
                break;
            case "touch":
                e = new InputEventScreenTouch
                {
                    Index = (int?)n["finger"] ?? 0, Position = ToV2(n["pos"]), Pressed = down, Canceled = (bool?)n["canceled"] ?? false, DoubleTap = (bool?)n["double"] ?? false,
                };
                break;
            case "drag":
                e = new InputEventScreenDrag
                {
                    Index = (int?)n["finger"] ?? 0, Position = ToV2(n["pos"]), Relative = ToV2(n["rel"]), Velocity = ToV2(n["vel"]),
                    Pressure = (float?)n["pressure"] ?? 0f, Tilt = ToV2(n["tilt"]), PenInverted = (bool?)n["eraser"] ?? false,
                };
                break;
            case "magnify":
                e = new InputEventMagnifyGesture { Position = ToV2(n["pos"]), Factor = (float?)n["factor"] ?? 1f };
                break;
            case "pan":
                e = new InputEventPanGesture { Position = ToV2(n["pos"]), Delta = ToV2(n["delta"]) };
                break;
            case "action":
                e = new InputEventAction { Action = (string)n["action"] ?? string.Empty, Pressed = down, Strength = (float?)n["strength"] ?? 1f };
                break;
            default:
                return null;
        }
        e.Device = (int?)n["dev"] ?? 0;
        return e;
    }
}
