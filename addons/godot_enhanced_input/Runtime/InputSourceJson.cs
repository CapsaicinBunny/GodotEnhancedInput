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
    public static JsonObject ToJson(InputSource source)
    {
        var json = new JsonObject { ["kind"] = (source?.Kind ?? SourceKind.None).ToString() };
        if (source == null) return json;

        switch (source.Kind)
        {
            case SourceKind.Key:
                json["key"] = source.Key.ToString();
                json["keyMatch"] = source.KeyMatch.ToString();
                break;
            case SourceKind.KeyAxis1D:
                json["positiveKey"] = source.PositiveKey.ToString();
                json["negativeKey"] = source.NegativeKey.ToString();
                json["keyMatch"] = source.KeyMatch.ToString();
                break;
            case SourceKind.KeyAxis2D:
                json["upKey"] = source.UpKey.ToString();
                json["downKey"] = source.DownKey.ToString();
                json["negativeKey"] = source.NegativeKey.ToString();
                json["positiveKey"] = source.PositiveKey.ToString();
                json["keyMatch"] = source.KeyMatch.ToString();
                break;
            case SourceKind.MouseButton:
                json["mouseButton"] = source.MouseButton.ToString();
                break;
            case SourceKind.JoyButton:
                json["joyButton"] = source.JoyButton.ToString();
                json["device"] = source.Device;
                break;
            case SourceKind.JoyAxis:
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
                    break;
                case SourceKind.KeyAxis1D:
                    source.PositiveKey = ReadEnum<Key>(json, "positiveKey");
                    source.NegativeKey = ReadEnum<Key>(json, "negativeKey");
                    source.KeyMatch = ReadEnum<KeyMatch>(json, "keyMatch", KeyMatch.Physical);
                    break;
                case SourceKind.KeyAxis2D:
                    source.UpKey = ReadEnum<Key>(json, "upKey");
                    source.DownKey = ReadEnum<Key>(json, "downKey");
                    source.NegativeKey = ReadEnum<Key>(json, "negativeKey");
                    source.PositiveKey = ReadEnum<Key>(json, "positiveKey");
                    source.KeyMatch = ReadEnum<KeyMatch>(json, "keyMatch", KeyMatch.Physical);
                    break;
                case SourceKind.MouseButton:
                    source.MouseButton = ReadEnum<MouseButton>(json, "mouseButton");
                    break;
                case SourceKind.JoyButton:
                    source.JoyButton = ReadEnum<JoyButton>(json, "joyButton");
                    source.Device = Int(json, "device");
                    break;
                case SourceKind.JoyAxis:
                    source.JoyAxis = ReadEnum<JoyAxis>(json, "joyAxis");
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

    private static int Int(JsonObject json, string name)
        => json[name] is JsonValue v && v.TryGetValue<int>(out var i) ? i : SourceKey.AnyDevice;
}
