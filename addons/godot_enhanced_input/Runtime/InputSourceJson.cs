using System;
using System.Text.Json.Nodes;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Converts <see cref="InputSource"/>s to and from JSON objects. Only the fields that matter for the
/// source's kind are written, and enum values are written by name, so saved data survives changes to
/// enum numbering.
/// </summary>
public static class InputSourceJson
{
    /// <summary>A source as JSON (enums by name), for profiles and tools.</summary>
    public static JsonObject ToJson(InputSource source)
    {
        var json = new JsonObject { ["kind"] = (source?.Kind ?? SourceKind.None).ToString() };
        if (source == null) return json;

        switch (source.Kind)
        {
            case SourceKind.Key:
                json["key"] = source.Key.ToString();
                json["keyMatch"] = source.KeyMatch.ToString();
                if (source.KeySide != KeyLocation.Unspecified) json["keySide"] = source.KeySide.ToString();
                break;
            case SourceKind.KeyAxis1D:
                json["positiveKey"] = source.PositiveKey.ToString();
                json["negativeKey"] = source.NegativeKey.ToString();
                json["keyMatch"] = source.KeyMatch.ToString();
                WriteOpposing(json, source);
                break;
            case SourceKind.KeyAxis2D:
                json["upKey"] = source.UpKey.ToString();
                json["downKey"] = source.DownKey.ToString();
                json["negativeKey"] = source.NegativeKey.ToString();
                json["positiveKey"] = source.PositiveKey.ToString();
                json["keyMatch"] = source.KeyMatch.ToString();
                WriteOpposing(json, source);
                break;
            case SourceKind.MouseButton:
                json["mouseButton"] = source.MouseButton.ToString();
                break;
            case SourceKind.JoyButton:
                json["joyButton"] = source.JoyButton.ToString();
                json["device"] = source.Device;
                if (source.RawIndex >= 0) json["rawIndex"] = source.RawIndex;
                break;
            case SourceKind.JoyAxis:
                if (source.RawIndex >= 0) json["rawIndex"] = source.RawIndex;
                json["joyAxis"] = source.JoyAxis.ToString();
                json["axisRange"] = source.AxisRange.ToString();
                json["device"] = source.Device;
                break;
            case SourceKind.JoyStick:
                json["joyStick"] = source.JoyStick.ToString();
                json["device"] = source.Device;
                break;
            case SourceKind.InputMapAction:
                json["inputMapAction"] = source.InputMapAction;
                break;
            case SourceKind.Virtual:
                json["virtualName"] = source.VirtualName;
                break;
            case SourceKind.Touch:
                json["touchChannel"] = source.TouchChannel.ToString();
                json["touchIndex"] = source.TouchIndex;
                break;
            case SourceKind.Pen:
                json["penChannel"] = source.PenChannel.ToString();
                break;
            case SourceKind.Gesture:
                json["gesture"] = source.GestureChannel.ToString();
                break;
            case SourceKind.Motion:
                json["motionSensor"] = source.MotionSensor.ToString();
                json["handheld"] = source.MotionOfHandheld;
                json["device"] = source.Device;
                break;
            case SourceKind.DeviceControl:
                json["deviceType"] = source.DeviceType;
                json["control"] = source.ControlName;
                json["device"] = source.Device;
                break;
            case SourceKind.Composite:
            {
                json["mode"] = source.CompositeMode.ToString();
                var parts = new JsonArray();
                foreach (var part in source.Parts) parts.Add(ToJson(part));
                json["parts"] = parts;
                WriteOpposing(json, source);
                break;
            }
        }
        return json;
    }

    /// <summary>Reads a source; returns null (with <paramref name="error"/>) when the object is malformed.</summary>
    public static InputSource FromJson(JsonNode node, out string error)
    {
        error = null;
        try
        {
            if (node is not JsonObject json) { error = "not an object"; return null; }
            var source = new InputSource { Kind = ReadEnum<SourceKind>(json, "kind") };
            switch (source.Kind)
            {
                case SourceKind.Key:
                    source.Key = ReadEnum<Key>(json, "key");
                    source.KeyMatch = ReadEnum<KeyMatch>(json, "keyMatch", KeyMatch.Physical);
                    source.KeySide = ReadEnum<KeyLocation>(json, "keySide", KeyLocation.Unspecified);
                    break;
                case SourceKind.KeyAxis1D:
                    source.PositiveKey = ReadEnum<Key>(json, "positiveKey");
                    source.NegativeKey = ReadEnum<Key>(json, "negativeKey");
                    source.KeyMatch = ReadEnum<KeyMatch>(json, "keyMatch", KeyMatch.Physical);
                    ReadOpposing(json, source);
                    break;
                case SourceKind.KeyAxis2D:
                    source.UpKey = ReadEnum<Key>(json, "upKey");
                    source.DownKey = ReadEnum<Key>(json, "downKey");
                    source.NegativeKey = ReadEnum<Key>(json, "negativeKey");
                    source.PositiveKey = ReadEnum<Key>(json, "positiveKey");
                    source.KeyMatch = ReadEnum<KeyMatch>(json, "keyMatch", KeyMatch.Physical);
                    ReadOpposing(json, source);
                    break;
                case SourceKind.MouseButton:
                    source.MouseButton = ReadEnum<MouseButton>(json, "mouseButton");
                    break;
                case SourceKind.JoyButton:
                    source.RawIndex = IntOr(json, "rawIndex", -1);
                    if (source.RawIndex < 0) source.JoyButton = ReadEnum<JoyButton>(json, "joyButton");
                    source.Device = Int(json, "device");
                    break;
                case SourceKind.JoyAxis:
                    source.RawIndex = IntOr(json, "rawIndex", -1);
                    if (source.RawIndex < 0) source.JoyAxis = ReadEnum<JoyAxis>(json, "joyAxis");
                    source.AxisRange = ReadEnum<JoyAxisRange>(json, "axisRange", JoyAxisRange.Full);
                    source.Device = Int(json, "device");
                    break;
                case SourceKind.JoyStick:
                    source.JoyStick = ReadEnum<JoyStickSide>(json, "joyStick");
                    source.Device = Int(json, "device");
                    break;
                case SourceKind.InputMapAction:
                    source.InputMapAction = (string)json["inputMapAction"] ?? throw new FormatException("missing inputMapAction");
                    break;
                case SourceKind.Virtual:
                    source.VirtualName = (string)json["virtualName"] ?? throw new FormatException("missing virtualName");
                    break;
                case SourceKind.Touch:
                    source.TouchChannel = ReadEnum<TouchChannel>(json, "touchChannel");
                    source.TouchIndex = Int(json, "touchIndex");
                    break;
                case SourceKind.Pen:
                    source.PenChannel = ReadEnum<PenChannel>(json, "penChannel");
                    break;
                case SourceKind.Gesture:
                    source.GestureChannel = ReadEnum<GestureChannel>(json, "gesture");
                    break;
                case SourceKind.Motion:
                    source.MotionSensor = ReadEnum<MotionSensor>(json, "motionSensor");
                    source.MotionOfHandheld = json["handheld"] is JsonValue h && h.TryGetValue<bool>(out var handheld) ? handheld : true;
                    source.Device = Int(json, "device");
                    break;
                case SourceKind.DeviceControl:
                    source.DeviceType = (string)json["deviceType"] ?? throw new FormatException("missing deviceType");
                    source.ControlName = (string)json["control"] ?? throw new FormatException("missing control");
                    source.Device = Int(json, "device");
                    break;
                case SourceKind.Composite:
                {
                    source.CompositeMode = ReadEnum<CompositeMode>(json, "mode");
                    if (json["parts"] is not JsonArray parts) throw new FormatException("missing parts");
                    foreach (var partNode in parts)
                    {
                        var part = FromJson(partNode, out var partError);
                        if (part == null) throw new FormatException($"bad part: {partError}");
                        source.Parts.Add(part);
                    }
                    ReadOpposing(json, source);
                    break;
                }
            }
            return source;
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or ArgumentException)
        {
            error = e.Message;
            return null;
        }
    }

    /// <summary>Whether two sources describe the same input.</summary>
    public static bool SameInput(InputSource a, InputSource b)
        => ToJson(a).ToJsonString() == ToJson(b).ToJsonString();

    private static void WriteOpposing(JsonObject json, InputSource source)
    {
        if (source.Opposing != OpposingInputs.Neutral) json["opposing"] = source.Opposing.ToString();
        if (source.OpposingVertical != OpposingInputs.Neutral) json["opposingVertical"] = source.OpposingVertical.ToString();
    }

    private static void ReadOpposing(JsonObject json, InputSource source)
    {
        source.Opposing = ReadEnum<OpposingInputs>(json, "opposing", OpposingInputs.Neutral);
        source.OpposingVertical = ReadEnum<OpposingInputs>(json, "opposingVertical", OpposingInputs.Neutral);
    }

    private static T ReadEnum<T>(JsonObject json, string name, T? fallback = null) where T : struct, System.Enum
    {
        var text = (string)json[name];
        if (text == null)
        {
            if (fallback.HasValue) return fallback.Value;
            throw new FormatException($"missing {name}");
        }
        if (System.Enum.TryParse<T>(text, ignoreCase: false, out var value) && System.Enum.IsDefined(value)) return value;
        throw new FormatException($"unknown {name} '{text}'");
    }

    private static int Int(JsonObject json, string name) => IntOr(json, name, SourceKey.AnyDevice);

    private static int IntOr(JsonObject json, string name, int fallback)
        => json[name] is JsonValue v && v.TryGetValue<int>(out var i) ? i : fallback;
}
