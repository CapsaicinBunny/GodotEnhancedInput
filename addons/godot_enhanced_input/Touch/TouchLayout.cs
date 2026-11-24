using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Godot.Collections;

namespace GodotEnhancedInput;

/// <summary>
/// Where each on-screen control sits, as fractions of the screen (0…1), plus a size scale and an
/// opacity for all of them — so players can move, resize and fade controls, and the result can be
/// saved per player. Applied by <see cref="TouchLayoutRoot"/> to its child controls by node name.
/// </summary>
[Tool]
[GlobalClass]
public partial class TouchLayout : Resource
{
    /// <summary>Control node name → rect in screen fractions.</summary>
    [Export] public Dictionary<string, Rect2> Rects { get; set; } = new();

    /// <summary>Size multiplier for every control (accessibility: bigger buttons).</summary>
    [Export(PropertyHint.Range, "0.5,3,0.05")] public float Scale { get; set; } = 1f;

    /// <summary>Opacity of every control.</summary>
    [Export(PropertyHint.Range, "0.1,1,0.05")] public float Opacity { get; set; } = 1f;

    /// <summary>The layout as JSON text.</summary>
    public string ToJson()
    {
        var rects = new JsonObject();
        foreach (var pair in Rects)
            rects[pair.Key] = new JsonArray(pair.Value.Position.X, pair.Value.Position.Y, pair.Value.Size.X, pair.Value.Size.Y);
        return new JsonObject { ["format"] = 1, ["scale"] = Scale, ["opacity"] = Opacity, ["rects"] = rects }
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Reads a layout from JSON text, or returns null.</summary>
    public static TouchLayout FromJson(string text)
    {
        try
        {
            if (JsonNode.Parse(text) is not JsonObject root) return null;
            var layout = new TouchLayout();
            if (root["scale"] is JsonValue s && s.TryGetValue<float>(out var scale)) layout.Scale = scale;
            if (root["opacity"] is JsonValue o && o.TryGetValue<float>(out var opacity)) layout.Opacity = opacity;
            if (root["rects"] is JsonObject rects)
                foreach (var pair in rects)
                    if (pair.Value is JsonArray a && a.Count == 4)
                        layout.Rects[pair.Key] = new Rect2((float)a[0], (float)a[1], (float)a[2], (float)a[3]);
            return layout;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Saves the layout as JSON (e.g. under user://).</summary>
    public Error SaveJson(string path)
    {
        var folder = path.GetBaseDir();
        if (!string.IsNullOrEmpty(folder)) DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(folder));
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file == null) return FileAccess.GetOpenError();
        file.StoreString(ToJson());
        return Error.Ok;
    }

    /// <summary>Loads a layout saved with <see cref="SaveJson"/>, or returns null.</summary>
    public static TouchLayout LoadJson(string path)
    {
        if (!FileAccess.FileExists(path)) return null;
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return file == null ? null : FromJson(file.GetAsText());
    }
}
